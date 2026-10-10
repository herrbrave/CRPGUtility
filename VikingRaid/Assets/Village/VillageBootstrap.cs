using System;
using System.Collections.Generic;
using System.Linq;
using CRPG.Actors;
using CRPG.Camera;
using CRPG.Combat;
using CRPG.Core;
using CRPG.Exploration;
using CRPG.Gameplay;
using CRPG.Gameplay.Interactions;
using CRPG.Gameplay.Ambient;
using CRPG.Gameplay.Scenes;
using CRPG.InputSystem;
using CRPG.Navigation;
using CRPG.Party;
using CRPG.Rules;
using UnityEngine;
using UnityEngine.AI;
using VikingRaid.Game;

namespace VikingRaid.Village
{
    /// <summary>
    /// The composition root of the village scene: an exploration-only game built from the toolkit's own pieces, with no combat and no game-specific HUD, so a scene can be dressed and
    /// walked through while its content is being authored. What it wires:
    /// <list type="bullet">
    /// <item>a real ruleset session (the Viking Raid ruleset: the four heroes are its NpcDefinitions) and a <see cref="WorldSpawnService"/>;</item>
    /// <item>the party, spawned at <c>partyStart</c> and sampled onto the NavMesh; click-to-move through <see cref="PartyExplorationController"/>, with the order's facing derived from the travel direction;</item>
    /// <item>the scene's <see cref="LocationContent"/>: every <see cref="NpcSpawnPoint"/> is spawned through <see cref="WorldSpawnService.SpawnNpc"/> exactly as the toolkit's own
    /// locations do, and its stores and loot containers are bound;</item>
    /// <item>a <see cref="PartyCommandFilter"/> between the input and everything else (a click on a villager is a "select a non-party character", which the party must not throw on),
    /// the interaction controller, and the toolkit camera rig following the selected party members, bounded by the location's camera bounds.</item>
    /// </list>
    /// Authored into the scene by <c>VillageSceneAuthor</c> (Editor). Tests may inject input through <see cref="InputFactory"/>.
    /// </summary>
    public sealed class VillageBootstrap : MonoBehaviour
    {
        /// <summary>Tests set this before the scene loads (the real input source reads the mouse); null uses the scene's <see cref="InputSystemCommandSource"/>.</summary>
        public static Func<IPlayerCommandSource> InputFactory { get; set; }
        public static VillageBootstrap Instance { get; private set; }

        private static readonly Type[] RequiredPolicies =
        {
            typeof(IArmorClassRule), typeof(IAttackBonusRule), typeof(IInitiativeRule), typeof(ISavingThrowRule), typeof(IDamageResistanceRule),
        };

        [SerializeField] private VikingRaidRulesetProvider ruleset;
        [SerializeField] private InputSystemCommandSource inputSource;
        [SerializeField] private LocationContent location;
        [SerializeField] private Transform partyStart;
        [SerializeField] private IsometricCameraRig cameraRig;
        [SerializeField] private PartyCameraFollowBehaviour cameraFollow;
        [SerializeField] private int seed;
        [SerializeField] private UnityEngine.UIElements.PanelSettings dialoguePanelSettings;

        /// <summary>The SRD gold piece: the village's currency (it is worth nothing as an item; it is the money).</summary>
        public static readonly DefinitionId GoldItemId = new("item-gold-piece");

        /// <summary>What the party carries into the village, enough that a purchase matters next to a 5 gold bed.</summary>
        public const int StartingGold = 80;
        [SerializeField] private float partySpacing = 1.6f;

        private GameSessionHost host;
        private readonly List<RuntimeEntityId> party = new();
        private readonly Dictionary<string, CharacterAgent> npcs = new();
        private bool booted;

        public GameSession Session { get; private set; }
        public WorldSpawnService Spawner { get; private set; }
        public PartyCommandFilter Commands { get; private set; }
        public PartyExplorationController Exploration { get; private set; }
        public InteractionControllerBehaviour Interactions { get; private set; }

        /// <summary>Starts quests from dialogue and keeps their progress current (see <see cref="QuestTracker"/>).</summary>
        public QuestTracker Quests { get; private set; }

        public VillageHud Hud { get; private set; }

        /// <summary>Turns a villager into a party member when a dialogue's Recruit action says so (at the end of that conversation).</summary>
        public NpcRecruitment Recruitment { get; private set; }
        public IReadOnlyList<RuntimeEntityId> Party => party;

        /// <summary>The spawned NPC of each location spawn point, by spawn id.</summary>
        public IReadOnlyDictionary<string, CharacterAgent> Npcs => npcs;

        public LocationContent Location => location;
        public Transform PartyStart => partyStart;
        public IsometricCameraRig CameraRig => cameraRig;
        public int Seed { get => seed; set => seed = value; }

        private void Awake() => Instance = this;

        private void Start() => Build();

        public void Build()
        {
            if (booted) return;
            booted = true;
            if (ruleset == null || partyStart == null)
                throw new InvalidOperationException("VillageBootstrap is missing references: re-run 'Viking Raid/Village/1. Author Village Scene'.");

            host = gameObject.GetComponent<GameSessionHost>() ?? gameObject.AddComponent<GameSessionHost>();
            Session = host.StartSession(ruleset, new DiceService(new SeededRandomSource(seed != 0 ? seed : Environment.TickCount)), RequiredPolicies);
            Spawner = new WorldSpawnService(Session);
            GrantStartingGold();
            Quests = new QuestTracker(Session);
            Recruitment = new NpcRecruitment(Spawner, Session, OnRecruited);

            SpawnParty();
            SpawnLocationNpcs();
            NpcRoutines.AttachAll(location, Spawner);   // wander / work loops authored on the spawn points
            AttachDialogues();

            var source = InputFactory?.Invoke() ?? (IPlayerCommandSource)inputSource;
            if (source == null) throw new InvalidOperationException("VillageBootstrap has no input source.");
            Commands = new PartyCommandFilter(source, () => Session.Party);

            var selectables = new SceneSelectableSource();
            Exploration = new PartyExplorationController(Session.Party, new PartyMovementService(new NavMeshService(), selectables), new WedgeFormationSolver(), Commands,
                facingSource: selectables);
            foreach (var member in party)
            {
                var agent = FindAgent(member);
                if (agent?.Motor != null) Exploration.RegisterMotor(member, agent.Motor);
            }

            var interactionHost = new GameObject("Interactions");
            interactionHost.transform.SetParent(transform, false);
            Interactions = interactionHost.AddComponent<InteractionControllerBehaviour>();
            Interactions.Initialize(Commands, Session.Party, Session);
            Interactions.Controller.RequestEnded += _ => Quests.RefreshAll();   // what a conversation recorded may have advanced a quest

            if (dialoguePanelSettings != null)
            {
                Hud = new GameObject("VillageHud").AddComponent<VillageHud>();
                Hud.transform.SetParent(transform, false);
                Hud.Initialize(Session, dialoguePanelSettings);
            }

            if (location != null) LocationInitializer.RebindStoresAndLoot(location, Session);

            if (cameraRig != null && location != null) cameraRig.Rig.SetBounds(location.CameraBoundsXZ);
            if (cameraFollow != null) cameraFollow.Initialize(Session.Party, selectables);
        }

        // The recruit is already a party member (NpcRecruitment added its own character); what only the village can do: remember it as one of ours and let the party's orders move it.
        private void OnRecruited(CharacterAgent agent)
        {
            party.Add(agent.EntityId);
            if (agent.Motor != null) Exploration?.RegisterMotor(agent.EntityId, agent.Motor);
            var name = "A new companion";
            if (Spawner.TryGetSpawnId(agent.EntityId, out var spawnId) && location != null)
            {
                var point = location.SpawnPoints.FirstOrDefault(p => p != null && p.SpawnId == spawnId);
                if (point?.Definition != null) name = point.Definition.DisplayName;
            }

            Hud?.ShowToast($"{name} joins the party.");
        }

        private void GrantStartingGold()
        {
            if (!Session.RulesDatabase.TryResolve<CRPG.Inventory.ItemDefinition>(GoldItemId, out _)) return;
            Session.Inventory.AddItem(new CRPG.Inventory.ItemInstance(RuntimeEntityId.NewId(), GoldItemId, StartingGold));
        }

        /// <summary>Makes every NPC whose spawn point has a dialogue talkable (click it, the party walks up, the conversation opens). Needs the scene's dialogue panel settings; without them the NPCs just stand and wander.</summary>
        private void AttachDialogues()
        {
            if (location == null) return;
            if (dialoguePanelSettings == null)
            {
                Debug.LogWarning("[VillageBootstrap] no dialogue panel settings assigned: NPCs will not be talkable. Run 'Viking Raid/Village/3. Author NPC Dialogues'.", this);
                return;
            }

            NpcDialogues.AttachAll(location, Spawner, Session, new NpcDialogueOptions
            {
                PanelSettings = dialoguePanelSettings,
                CurrencyItemId = GoldItemId,
                Quests = Quests,
                RequestRecruitSpawn = (spawnId, key) => Recruitment.Queue(spawnId),
                SkillCheckRuleFactory = session => new CRPG.Rules.SRD5.DefaultSkillCheckRule(session.Characters, session.RulesDatabase),
            });
        }

        private void SpawnParty()
        {
            var right = Vector3.Cross(Vector3.up, partyStart.forward).normalized;
            var index = 0;
            foreach (var unit in GameDesign.Heroes)
            {
                if (!Session.RulesDatabase.TryResolve<NpcDefinition>(new DefinitionId(unit.NpcId), out var npc))
                    throw new InvalidOperationException($"{unit.NpcId} is missing from the ruleset: run 'Viking Raid/Task 2/Author Content'.");
                var wanted = partyStart.position + right * ((index++ - (GameDesign.Heroes.Count() - 1) / 2f) * partySpacing);
                var position = NavMesh.SamplePosition(wanted, out var hit, 3f, NavMesh.AllAreas) ? hit.position : wanted;
                var agent = Spawner.SpawnNpc($"hero-{unit.Id}", npc, position, partyStart.rotation);
                Session.Party.AddMember(agent.EntityId);
                party.Add(agent.EntityId);
            }

            Session.Party.SelectAll();
        }

        private void SpawnLocationNpcs()
        {
            if (location == null) return;
            foreach (var point in location.SpawnPoints)
            {
                if (point == null || point.Definition == null || point.Anchor == null || string.IsNullOrEmpty(point.SpawnId))
                {
                    Debug.LogWarning("[VillageBootstrap] skipped an NPC spawn point with no spawn id, definition or anchor.", this);
                    continue;
                }

                npcs[point.SpawnId] = Spawner.SpawnNpc(point.SpawnId, point.Definition, point.Anchor.position, point.Anchor.rotation);
            }
        }

        public static CharacterAgent FindAgent(RuntimeEntityId id)
        {
            foreach (var agent in CharacterAgent.AllLive) if (agent != null && agent.EntityId == id) return agent;
            return null;
        }

        private void Update() => Quests?.Tick();

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            Quests?.Dispose();
            Recruitment?.Dispose();
            Exploration?.Dispose();
            Commands?.Dispose();
            host?.EndSession();
        }
    }
}

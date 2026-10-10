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

            SpawnParty();
            SpawnLocationNpcs();

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

            if (location != null) LocationInitializer.RebindStoresAndLoot(location, Session);

            if (cameraRig != null && location != null) cameraRig.Rig.SetBounds(location.CameraBoundsXZ);
            if (cameraFollow != null) cameraFollow.Initialize(Session.Party, selectables);
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

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            Exploration?.Dispose();
            Commands?.Dispose();
            host?.EndSession();
        }
    }
}

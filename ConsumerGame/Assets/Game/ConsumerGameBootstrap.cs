using System.Linq;
using CRPG.Abilities;
using CRPG.Actors;
using CRPG.Combat;
using CRPG.Consumer.Fate;
using CRPG.Core;
using CRPG.Dialogue;
using CRPG.Exploration;
using CRPG.Gameplay;
using CRPG.Navigation;
using CRPG.Party;
using CRPG.Saving;
using UnityEngine;
using UnityEngine.UIElements;

namespace CRPG.Consumer.Game
{
    /// <summary>
    /// Gate D's composition root -- construction/lifetime ONLY (session start, spawn, wiring, disposal).
    /// The Fate-specific taken-out translation lives in <see cref="FateConflictService"/> and save/load wiring
    /// in <see cref="ConsumerSaveCoordinator"/>, deliberately kept out of this class so it stays thin, per
    /// Gate D's "no copied monolithic bootstrap" requirement.
    /// </summary>
    public sealed class ConsumerGameBootstrap : MonoBehaviour
    {
        private static readonly DefinitionId PartyFaction = new("faction-party");
        private static readonly DefinitionId HostileFaction = new("faction-hostile");

        [SerializeField] private FateRulesetProvider provider;
        [SerializeField] private GameObject partyPrefab;
        [SerializeField] private GameObject hostilePrefab;
        [SerializeField] private GameObject npcPrefab;
        [SerializeField] private Vector3 partySpawnA = new(-2f, 0f, -6f);
        [SerializeField] private Vector3 partySpawnB = new(0f, 0f, -6f);
        [SerializeField] private Vector3 hostileSpawn = new(6f, 0f, 4f);
        [SerializeField] private Vector3 npcSpawn = new(-6f, 0f, 4f);
        // Fully qualified rather than `using CRPG.InputSystem;` -- this namespace sits under CRPG, which
        // (per SimpleClickCommandSource's own now-deleted precedent hitting the exact same problem with
        // CRPG.Camera) risks colliding with a UnityEngine type by bare name if one is ever added here.
        [SerializeField] private CRPG.InputSystem.InputSystemCommandSource commandSource;
        [SerializeField] private ConsumerHud hud;
        [SerializeField] private DialogueDefinition dialogue;
        [SerializeField] private PanelSettings dialoguePanelSettings;

        private GameSessionHost host;
        private WorldSpawnService worldSpawnService;
        private FateConflictService conflictService;
        private ConsumerSaveCoordinator saveCoordinator;
        private PartyExplorationController explorationController;
        private CombatEncounterService encounterService;
        private AbilityResolutionService abilityResolution;
        private ISkillCheckRule skillCheckRule;
        private ConversationController conversation;

        private RuntimeEntityId heroAId;
        private RuntimeEntityId heroBId;
        private RuntimeEntityId hostileId;
        private RuntimeEntityId npcId;
        private bool combatStarted;
        private bool combatResolvedWin;

        // Read-only testability accessors -- mirrors TacticalEncounterController's own EncounterService/
        // Scheduler exposure, so a PlayMode smoke test can confirm real wiring without reflection.
        public GameSessionHost Host => host;
        public RuntimeEntityId HeroAId => heroAId;
        public RuntimeEntityId HeroBId => heroBId;
        public RuntimeEntityId HostileId => hostileId;
        public RuntimeEntityId NpcId => npcId;
        public bool CombatStarted => combatStarted;
        public bool CombatWon => combatResolvedWin;
        public Vector3 HostileSpawnPosition => hostileSpawn;

        private static string SavePath => System.IO.Path.Combine(Application.persistentDataPath, "consumer-fate-save.json");

        private void Start()
        {
            host = gameObject.AddComponent<GameSessionHost>();
            var session = host.StartSession(provider, new DiceService(new SeededRandomSource(System.Environment.TickCount)), new[] { typeof(ISkillCheckRule) });
            skillCheckRule = session.Policies.Get<ISkillCheckRule>();
            worldSpawnService = new WorldSpawnService(session);

            var navMeshService = new NavMeshService();
            var movementService = new PartyMovementService(navMeshService, new SceneSelectableSource());
            explorationController = new PartyExplorationController(session.Party, movementService, new WedgeFormationSolver(), commandSource);

            heroAId = SpawnPartyMember(session, "hero-a", partySpawnA);
            heroBId = SpawnPartyMember(session, "hero-b", partySpawnB);
            session.Party.SelectAll();

            hostileId = SpawnCombatant(session, "hostile", hostilePrefab, hostileSpawn, HostileFaction, fight: 1, athletics: 1);
            npcId = SpawnCombatant(session, "npc", npcPrefab, npcSpawn, default, fight: 0, athletics: 0);

            abilityResolution = new AbilityResolutionService(session.Characters, session.DiceService, eventBus: session.EventBus);

            var turnResources = new TurnResourceSet(new[] { provider.ActionResource });
            encounterService = new CombatEncounterService(session.Characters, initiativeRule: null, turnResources, session.EventBus);

            saveCoordinator = new ConsumerSaveCoordinator(host, provider, encounterService);
            conflictService = new FateConflictService(worldSpawnService, encounterService);

            WireTriggerZone(hostileId, StartCombat);
            WireTriggerZone(npcId, StartDialogue);
            WireHud();

            hud.SetStatus("Walk toward the green figure to talk, or the red one to fight.");
        }

        private void OnDestroy()
        {
            conflictService?.Dispose();
            explorationController?.Dispose();
            conversation?.Cancel();
            host?.EndSession();
        }

        private RuntimeEntityId SpawnPartyMember(GameSession session, string spawnId, Vector3 position)
        {
            var character = new CharacterState(RuntimeEntityId.NewId());
            GrantFateResources(character, fight: 2, athletics: 1, notice: 1, will: 1, stressMax: 3);
            character.AddResource(new ResourceState(provider.FatePointsResource.Id, max: 2));
            session.Characters.Register(character);
            session.Party.AddMember(character.Id);

            var instance = worldSpawnService.BindRestoredView(spawnId, character.Id, default, partyPrefab, position, Quaternion.identity, PartyFaction, startInactive: false);
            explorationController.RegisterMotor(character.Id, instance.GetComponent<CharacterAgent>().Motor);
            return character.Id;
        }

        private RuntimeEntityId SpawnCombatant(GameSession session, string spawnId, GameObject prefab, Vector3 position, DefinitionId faction, int fight, int athletics)
        {
            var character = new CharacterState(RuntimeEntityId.NewId());
            GrantFateResources(character, fight, athletics, notice: 0, will: 0, stressMax: 2);
            session.Characters.Register(character);
            worldSpawnService.BindRestoredView(spawnId, character.Id, default, prefab, position, Quaternion.identity, faction, startInactive: false);
            return character.Id;
        }

        private void GrantFateResources(CharacterState character, int fight, int athletics, int notice, int will, int stressMax)
        {
            character.AddResource(new ResourceState(provider.ActionResource.Id, max: 1, current: 0));
            character.AddResource(new ResourceState(provider.StressResource.Id, max: stressMax));
            character.AddResource(new ResourceState(provider.SkillResource("Skill:Fight").Id, max: fight));
            character.AddResource(new ResourceState(provider.SkillResource("Skill:Athletics").Id, max: athletics));
            character.AddResource(new ResourceState(provider.SkillResource("Skill:Notice").Id, max: notice));
            character.AddResource(new ResourceState(provider.SkillResource("Skill:Will").Id, max: will));
        }

        private void WireTriggerZone(RuntimeEntityId ownerId, System.Action onEntered)
        {
            var agent = FindObjectsByType<CharacterAgent>(FindObjectsSortMode.None).FirstOrDefault(a => a.EntityId == ownerId);
            if (agent == null) return;

            var trigger = agent.gameObject.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = 3f;

            var zone = agent.gameObject.AddComponent<ProximityTriggerZone>();
            zone.PartyMemberEntered += _ => onEntered();
        }

        private void StartDialogue()
        {
            if (conversation != null) return;

            var session = host.Session;
            var context = new DialogueContext(npcId, session.Party, session.Characters, session.RulesDatabase, session.Timeline, session.Inventory, session.DiceService, skillCheckRule);
            conversation = new ConversationController(dialogue, context, dialoguePanelSettings);
            conversation.Completed += () => conversation = null;
            conversation.Cancelled += () => conversation = null;
        }

        private void StartCombat()
        {
            if (combatStarted) return;
            combatStarted = true;

            encounterService.StartCombatWithExplicitOrder(new[] { (heroAId, PartyFaction), (heroBId, PartyFaction), (hostileId, HostileFaction) });
            hud.SetCombatVisible(true);
            hud.SetStatus("Combat started.");
        }

        private void Update()
        {
            if (!combatStarted || combatResolvedWin) return;

            var encounter = encounterService.Encounter;
            if (encounter == null) return;

            if (!encounter.Combatants.Any(c => c.Id == hostileId))
            {
                combatResolvedWin = true;
                encounterService.EndCombat();
                hud.SetCombatVisible(false);
                hud.SetStatus("Victory! Autosaving...");
                AutoSaveOnVictory();
                return;
            }

            var current = encounter.CurrentTurn;
            if (current == null) return;

            if (current.Id == hostileId)
            {
                var target = encounter.Combatants.FirstOrDefault(c => c.Faction == PartyFaction);
                if (target != null) abilityResolution.Execute(hostileId, provider.StrikeAbility, new[] { target.Id });
                encounterService.EndTurn();
            }
            else
            {
                hud.SetStatus(current.Id == heroAId ? "Hero A's turn." : "Hero B's turn.");
                hud.SetCombatButtonsEnabled(true);
            }

            UpdateStressDisplay();
        }

        /// <summary>Public for the same reason <c>TacticalPlayerMoveAdapter.TryHandleMove</c> is -- a real
        /// integration test needs to drive a party member's turn without simulating an actual button click.</summary>
        public void RequestStrike(bool invoking) => OnStrikeRequested(invoking);

        private void OnStrikeRequested(bool invoking)
        {
            var current = encounterService.Encounter?.CurrentTurn;
            if (current == null || (current.Id != heroAId && current.Id != heroBId)) return;

            var ability = invoking ? provider.StrikeInvokingAbility : provider.StrikeAbility;
            var result = abilityResolution.Execute(current.Id, ability, new[] { hostileId });
            hud.SetStatus(result.Succeeded ? "Hit landed." : $"Rejected: {result.Message}");

            if (result.Succeeded)
            {
                hud.SetCombatButtonsEnabled(false);
                encounterService.EndTurn();
            }
        }

        private void UpdateStressDisplay() =>
            hud.SetStress(CurrentStress(heroAId), CurrentStress(hostileId));

        private int CurrentStress(RuntimeEntityId characterId)
        {
            if (!host.Session.Characters.TryGetCharacter(characterId, out var character)) return 0;
            return character.TryGetResource(provider.StressResource.Id, out var stress) ? stress.Current : 0;
        }

        private void WireHud()
        {
            hud.StrikeRequested += () => OnStrikeRequested(false);
            hud.StrikeInvokingRequested += () => OnStrikeRequested(true);
            hud.SaveRequested += OnSaveRequested;
            hud.LoadRequested += OnLoadRequested;
        }

        private void OnSaveRequested()
        {
            var result = saveCoordinator.Save(worldSpawnService, SavePath);
            hud.SetStatus(result.Outcome == SaveRequestOutcome.Success ? "Saved." : $"Save blocked: {result.Outcome}");
        }

        private void AutoSaveOnVictory()
        {
            var result = saveCoordinator.Save(worldSpawnService, SavePath);
            hud.SetStatus(result.Outcome == SaveRequestOutcome.Success ? "Victory! Saved." : $"Victory! Save blocked: {result.Outcome}");
        }

        /// <summary>Public for the same testability reason as <see cref="RequestStrike"/>.</summary>
        public void RequestLoad() => OnLoadRequested();

        private void OnLoadRequested()
        {
            var result = saveCoordinator.Load(SavePath, worldSpawnService, partyPrefab, new[] { heroAId, heroBId });
            if (result.Outcome != LoadOutcome.Success)
            {
                hud.SetStatus($"Load failed: {result.Outcome}");
                return;
            }

            worldSpawnService = result.WorldSpawnService;
            conflictService.Rebind(worldSpawnService);
            hud.SetStatus("Loaded.");
        }
    }
}

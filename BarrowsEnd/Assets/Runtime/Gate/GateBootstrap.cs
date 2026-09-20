using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BarrowsEnd;
using CRPG.Actors;
using CRPG.Combat;
using CRPG.Core;
using CRPG.Gameplay;
using CRPG.Gameplay.Interactions;
using CRPG.InputSystem;
using CRPG.Inventory;
using CRPG.Navigation;
using CRPG.Navigation.Grid;
using CRPG.Saving;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BarrowsEnd.Gate
{
    /// <summary>One party member the gate scene spawns: where, how they look, what they wear.</summary>
    [Serializable]
    public sealed class GateMemberSpec
    {
        public string displayName = "Hero";
        public string spawnId = "hero-1";
        public Vector2Int cell;
        public int maxHp = 20;
        [Tooltip("layer-slot=option-id, e.g. layer-body=body-kenney-0-0")] public string[] appearance = Array.Empty<string>();
        [Tooltip("Item definition ids worn from the start (their slots come from the items).")] public string[] gear = Array.Empty<string>();
    }

    /// <summary>
    /// The gate scene's composition root (Task 2.7b): starts a real ruleset-aware <see cref="GameSession"/>, spawns the party through
    /// <see cref="WorldSpawnService"/> (the load-shape: appearance and gear set BEFORE the view binds) and the skeleton NPC, wires the real
    /// click/selection/movement/interaction stack, and owns quick-save / quick-load through the real <see cref="SessionLoadCoordinator"/>.
    /// Everything session-scoped is rebuilt by <see cref="GateSessionServices"/> after a load.
    /// </summary>
    public sealed class GateBootstrap : MonoBehaviour
    {
        [SerializeField] private BarrowsEndRulesetProvider ruleset;
        [SerializeField] private GameObject partyPrefab;
        [SerializeField] private WalkabilityGridProvider gridProvider;
        [SerializeField] private InputSystemCommandSource inputSource;
        [SerializeField] private GateCamera gateCamera;
        [SerializeField] private GameObject doorObject;
        [SerializeField] private GateMemberSpec[] members = Array.Empty<GateMemberSpec>();
        [SerializeField] private Vector2Int skeletonCell;
        [SerializeField] private string skeletonNpcId = "npc-skeleton";
        [SerializeField] private int seed = 12345;

        private GameSessionHost host;
        private GateCommandSource commandSource;
        private GridPathfindingService pathfinding;
        private SessionLoadCoordinator loadCoordinator;
        private readonly SessionConsumerRegistry consumers = new();

        public GateSessionServices Services { get; private set; }
        public GameSession Session => Services?.Session;
        public GameObject DoorObject => doorObject;
        public IReadOnlyList<GateMemberSpec> Members => members;
        public string LastMessage { get; private set; } = "";
        public event Action<string> MessageChanged;

        /// <summary>Where quick-save writes. Tests point this at a temp file.</summary>
        public string SavePath { get; set; }

        public bool IsReady => Services != null && Services.Session != null;

        private static readonly Type[] RequiredPolicies =
        {
            typeof(IArmorClassRule), typeof(IAttackBonusRule), typeof(IInitiativeRule), typeof(ISavingThrowRule),
        };

        private void Awake()
        {
            SavePath ??= Path.Combine(Application.persistentDataPath, "barrowsend-gate-quicksave.json");
        }

        private void Start() => Build();

        /// <summary>Builds the whole gate. Public so a test can construct the scene, set fields, and call it deterministically.</summary>
        public void Build()
        {
            if (IsReady) return;
            if (ruleset == null || partyPrefab == null || gridProvider == null || inputSource == null)
                throw new InvalidOperationException("GateBootstrap needs its ruleset, party prefab, grid provider and input source assigned (author the scene with 'Barrows End/Gate/Build Phase Two Gate').");

            pathfinding = new GridPathfindingService();
            pathfinding.SetActiveGrid(gridProvider.Grid);

            host = gameObject.GetComponent<GameSessionHost>() ?? gameObject.AddComponent<GameSessionHost>();
            var session = host.StartSession(ruleset, new DiceService(new SeededRandomSource(seed)), RequiredPolicies);
            var spawner = new WorldSpawnService(session) { GridNavigation = pathfinding };

            SpawnParty(session, spawner);
            SpawnSkeleton(session, spawner);
            if (session.Party.Members.Count > 0) session.Party.Select(session.Party.Members[0]);

            var interactionHost = GetComponentInChildren<InteractionControllerBehaviour>() ?? CreateInteractionHost();
            commandSource = new GateCommandSource(inputSource, () => Services?.Session?.Party);
            loadCoordinator = new SessionLoadCoordinator(gridPathfinding: pathfinding);

            Services = new GateSessionServices(commandSource, interactionHost, pathfinding);
            consumers.Register(Services);
            Services.Rebind(session, spawner);
            if (gateCamera != null) gateCamera.Follow = () => GateSessionServices.FindAgent(Services.SelectedId)?.transform.position;
            Say("ready");
        }

        private InteractionControllerBehaviour CreateInteractionHost()
        {
            var go = new GameObject("InteractionControllerHost");
            go.transform.SetParent(transform, false);
            return go.AddComponent<InteractionControllerBehaviour>();
        }

        private static DefinitionId Slot(string s) => new(s);

        private void SpawnParty(GameSession session, WorldSpawnService spawner)
        {
            var equipment = new EquipmentService(session.Characters, session.RulesDatabase, session.Inventory, session.EventBus);
            var grid = gridProvider.Grid;

            foreach (var spec in members)
            {
                var character = new CharacterState(RuntimeEntityId.NewId());
                character.AddResource(new ResourceState(GateSessionServices.HitPoints, spec.maxHp));
                foreach (var entry in spec.appearance)
                {
                    var parts = entry.Split('=');
                    if (parts.Length == 2) character.SetAppearance(Slot(parts[0].Trim()), Slot(parts[1].Trim()));
                    else Debug.LogWarning($"[Gate] Ignoring malformed appearance entry '{entry}' on {spec.displayName} (expected slot=option).");
                }

                session.Characters.Register(character);
                session.Party.AddMember(character.Id);

                foreach (var itemId in spec.gear)
                {
                    if (!session.RulesDatabase.TryResolve<ItemDefinition>(Slot(itemId), out var definition))
                    {
                        Debug.LogWarning($"[Gate] {spec.displayName}: unknown gear '{itemId}'.");
                        continue;
                    }

                    var instance = new ItemInstance(RuntimeEntityId.NewId(), definition.Id);
                    session.Inventory.AddItem(instance);
                    equipment.Equip(instance.InstanceId, character.Id, definition.EquipmentSlotId); // worn before the view binds: the load shape
                }

                var position = grid.ToWorld(new GridCoordinate(spec.cell.x, spec.cell.y));
                spawner.BindRestoredView(spec.spawnId, character.Id, default, partyPrefab, position, Quaternion.identity, default, startInactive: false);
            }
        }

        private void SpawnSkeleton(GameSession session, WorldSpawnService spawner)
        {
            if (!session.RulesDatabase.TryResolve<NpcDefinition>(Slot(skeletonNpcId), out var npc))
            {
                Debug.LogWarning($"[Gate] NPC '{skeletonNpcId}' is not in the ruleset; the gate has no hostile target.");
                return;
            }

            spawner.SpawnNpc("skeleton-1", npc, gridProvider.Grid.ToWorld(new GridCoordinate(skeletonCell.x, skeletonCell.y)), Quaternion.identity);
        }

        // ---------------------------------------------------------------------------------------------
        // Save / load (real SaveService + SessionLoadCoordinator)
        // ---------------------------------------------------------------------------------------------

        public bool QuickSave()
        {
            var session = Services.Session;
            var snapshots = Services.Spawner.CaptureSnapshot().ToList();
            // Only party members + the listed extras are captured: the skeleton (an NPC) must be listed or its world spawn fails validation on load.
            var extras = snapshots.Select(s => RuntimeEntityId.FromString(s.CharacterId)).ToList();
            var data = SaveService.Capture(
                session.Party, session.Characters, session.Inventory, session.Timeline, session.Quests,
                additionalCharacterIds: extras, worldSpawns: snapshots,
                scenePath: SceneManager.GetActiveScene().path, rulesetId: ruleset.RulesetId);

            var result = new SaveFileStore().Save(SavePath, SaveService.ToJson(data));
            Say(result.Success ? "saved" : "save failed: " + result.Message);
            return result.Success;
        }

        public bool QuickLoad()
        {
            if (!File.Exists(SavePath)) { Say("nothing saved yet"); return false; }

            var prefabs = new Dictionary<RuntimeEntityId, GameObject>();
            foreach (var member in Services.Session.Party.Members) prefabs[member] = partyPrefab;

            var result = loadCoordinator.Load(SavePath, ruleset, new DiceService(new SeededRandomSource(seed)), RequiredPolicies, host, Services.Spawner, prefabs);
            if (result.Outcome != LoadOutcome.Success)
            {
                Say($"load failed ({result.Outcome}): {result.Message}");
                return false;
            }

            consumers.RebindAll(result.Session, result.WorldSpawnService);
            Say("loaded");
            return true;
        }

        public void Say(string message)
        {
            LastMessage = message;
            MessageChanged?.Invoke(message);
        }

        private void OnDestroy()
        {
            Services?.Dispose();
            commandSource?.Dispose();
            host?.EndSession();
        }
    }
}

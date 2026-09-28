using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BarrowsEnd.Gate;
using CRPG.Actors;
using CRPG.Camera;
using CRPG.Combat;
using CRPG.Core;
using CRPG.Exploration;
using CRPG.Gameplay;
using CRPG.Gameplay.Interactions;
using CRPG.Gameplay.Interactions.Adapters;
using CRPG.Gameplay.Scenes;
using CRPG.InputSystem;
using CRPG.Inventory;
using CRPG.Navigation.Grid;
using CRPG.Party;
using CRPG.Presentation.Creator;
using CRPG.Rules.SRD5;
using CRPG.UI;
using CRPG.Saving;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BarrowsEnd.Shell
{
    /// <summary>
    /// The campaign's composition root, living in the persistent Core scene (Task 3.4; ported from the toolkit dogfood AdventureSliceBootstrap, minus its
    /// dialogue/quest/inventory UI). It starts the ruleset-aware session, owns the travel and load coordinators, spawns the party into the Core scene and
    /// loads the location scenes additively. Everything that holds the current session is rebuilt by <see cref="GateSessionServices"/> after a load; travel
    /// keeps the same session and needs none of that.
    /// <para>The party uses FIXED character ids so a save can be loaded after a process restart (a runtime-generated id would only work in the same process).</para>
    /// <para>Task 4.3: in <see cref="ShellStartMode.Title"/> mode the start flow runs after the session exists and before any location loads: the title screen, then New Game
    /// (the creator; Confirm hands over the protagonist, Cancel returns to the title) or Continue (boot the authored default party, then load the save over it, which works because
    /// the fixed ids match). <see cref="ShellStartMode.AuthoredParty"/> is the old straight-into-the-town boot. Every member, protagonist or companion, is dressed (their gear
    /// equipped) before the view binds, the load shape.</para>
    /// </summary>
    public sealed class ShellBootstrap : MonoBehaviour
    {
        [Header("Content")]
        [SerializeField] private BarrowsEndRulesetProvider ruleset;
        [SerializeField] private GameObject partyPrefab;
        [SerializeField] private GateMemberSpec[] members = Array.Empty<GateMemberSpec>();
        [SerializeField] private int seed = 4242;

        /// <summary>Task 5.1: the authored party spec, read-only -- the HUD needs each member's display name
        /// (never persisted in a save, see SaveService) to build its PartyPresenter's memberNames dictionary.</summary>
        public IReadOnlyList<GateMemberSpec> Members => members;

        [Header("Start")]
        [SerializeField] private string startingScenePath;
        [SerializeField] private string startingDestinationId = "start";

        [Header("Input / camera")]
        [SerializeField] private InputSystemCommandSource inputSource;
        [SerializeField] private IsometricCameraRig cameraRig;
        [SerializeField] private PartyCameraFollowBehaviour cameraFollow;

        [Header("Start flow (Task 4.3)")]
        [SerializeField] private ShellStartMode startMode = ShellStartMode.AuthoredParty;
        [SerializeField] private ShellTitleScreen titleScreen;
        [SerializeField] private CharacterCreatorScreen creatorScreen;
        [SerializeField] private TextAsset creatorPicks;
        [SerializeField] private TextAsset creatorClassTable;

        private static readonly Type[] RequiredPolicies =
        {
            typeof(IArmorClassRule), typeof(IAttackBonusRule), typeof(IInitiativeRule), typeof(ISavingThrowRule),
        };

        private readonly Dictionary<RuntimeEntityId, GameObject> partyVisualPrefabs = new();
        private GameSessionHost host;
        private GridPathfindingService pathfinding;
        private WorldTransitionGate transitionGate;
        private SceneTravelRunner runner;
        private SessionLoadCoordinator loadCoordinator;
        private GateCommandSource commandSource;
        private CreatorResult protagonist;
        private bool continueRequested;

        public GateSessionServices Services { get; private set; }
        public GameSession Session => Services?.Session;
        public GridPathfindingService Pathfinding => pathfinding;
        public WorldTransitionGate Gate => transitionGate;
        public SceneTravelRunner Runner => runner;
        public IsometricCameraRig CameraRig => cameraRig;

        /// <summary>2D-Campaign-ExplorationUX-Followups.md D3: the composition-root's own real
        /// InputSystemCommandSource reference, for a consumer (HudBootstrap's drag-select overlay, the move
        /// destination marker) that needs it directly -- wiring happens in runtime code, not a cross-assembly
        /// SerializeField, since CRPG.UI/CRPG.Presentation cannot serialize a CRPG.InputSystem type (B2).</summary>
        public InputSystemCommandSource InputSource => inputSource;
        public bool IsReady { get; private set; }
        /// <summary>Test hook: when set, <c>Boot</c> uses this instead of the serialized start mode. The 6 tests that boot straight into the town set AuthoredParty BEFORE they load
        /// Core (Boot reads it in Start) and clear it in teardown. A static, because the alternative is a second Core scene.</summary>
        public static ShellStartMode? StartModeOverride { get; set; }

        /// <summary>Task 6.0: test hook mirroring <see cref="StartModeOverride"/> exactly -- when set, <c>Boot</c>
        /// uses this instead of the serialized <c>startingScenePath</c>/<c>startingDestinationId</c>. Lets
        /// ShellTests/ShellCaptureTests/ShellStartFlowTests keep booting the Phase 3 demo slice (whose exact
        /// geometry their own click coordinates/door counts assert against) even though the real campaign's
        /// authored default now points at the real Town. A static, same reason as StartModeOverride.</summary>
        public static (string scenePath, string destinationId)? StartingSceneOverride { get; set; }

        /// <summary>Where the start flow is; Playing once the game is running (for Continue, only after its load finished).</summary>
        public ShellStartPhase Phase { get; private set; } = ShellStartPhase.Booting;

        /// <summary>The creator's model while the start flow runs (null in AuthoredParty mode).</summary>
        public CharacterCreatorModel CreatorModel { get; private set; }

        /// <summary>How many times the creator was shown (a test asserts Continue never shows it).</summary>
        public int CreatorRuns { get; private set; }

        public bool IsLoading => loadCoordinator != null && loadCoordinator.IsLoading;
        public string LastMessage { get; private set; } = "";
        public event Action<string> MessageChanged;

        /// <summary>Where <see cref="SaveNow"/> writes and <see cref="LoadNow"/> reads. Tests point this at a temp file.</summary>
        public string SavePath { get; set; }

        /// <summary>The scene holding the current location (the active scene).</summary>
        public Scene LocationScene => SceneManager.GetActiveScene();

        private void Awake() => SavePath ??= Path.Combine(Application.persistentDataPath, "barrowsend-quicksave.json");

        private void Start() => StartCoroutine(Boot());

        private IEnumerator Boot()
        {
            var effectiveScenePath = StartingSceneOverride?.scenePath ?? startingScenePath;
            var effectiveDestinationId = StartingSceneOverride?.destinationId ?? startingDestinationId;

            if (ruleset == null || partyPrefab == null || inputSource == null || cameraRig == null || string.IsNullOrEmpty(effectiveScenePath))
                throw new InvalidOperationException("ShellBootstrap needs its ruleset, party prefab, input source, camera rig and starting scene (author Core.unity with 'Barrows End/Content/Author Core Scene').");

            // Both screens are authored enabled: hidden first on every path, or a full-screen document would capture the pointer over the world from scene load on.
            titleScreen?.Hide();
            creatorScreen?.Hide();
            var mode = StartModeOverride ?? startMode;
            if (mode == ShellStartMode.Title && (titleScreen == null || creatorScreen == null || creatorPicks == null || creatorClassTable == null))
                throw new InvalidOperationException("ShellBootstrap in Title mode needs its title screen, creator screen and both creator JSON files (author Core.unity with 'Barrows End/Content/Author Core Scene').");

            var coreScene = gameObject.scene;
            pathfinding = new GridPathfindingService();
            host = gameObject.AddComponent<GameSessionHost>();
            var session = host.StartSession(ruleset, new DiceService(new SeededRandomSource(seed)), RequiredPolicies);
            var spawner = new WorldSpawnService(session) { GridNavigation = pathfinding };

            transitionGate = new WorldTransitionGate();
            var runnerGo = new GameObject("SceneTravelRunner");
            runnerGo.transform.SetParent(transform);
            runner = runnerGo.AddComponent<SceneTravelRunner>();
            runner.Coordinator = NewTravelCoordinator(session, spawner);
            loadCoordinator = new SessionLoadCoordinator(gate: transitionGate, gridPathfinding: pathfinding);
            inputSource.TransitionGate = transitionGate;

            if (mode == ShellStartMode.Title) yield return RunStartFlow(session);

            yield return SceneManager.LoadSceneAsync(effectiveScenePath, LoadSceneMode.Additive);
            var startingScene = SceneManager.GetSceneByPath(effectiveScenePath);
            SceneManager.SetActiveScene(startingScene);
            yield return null;
            ActivateGridOf(startingScene);

            SpawnParty(session, spawner, coreScene);

            var interactionHost = GetComponentInChildren<InteractionControllerBehaviour>() ?? NewInteractionHost();
            commandSource = new GateCommandSource(inputSource, () => Services?.Session?.Party);
            Services = new GateSessionServices(commandSource, interactionHost, pathfinding);
            Services.Rebind(session, spawner);

            PrepareLocation(startingScene, session, spawner);
            WireTravelAdapters();
            WireTalkAdapters(session);
            SceneManager.sceneLoaded += OnSceneLoaded;

            PlaceParty(session, startingScene, effectiveDestinationId);
            InitializeCamera(session, startingScene);

            IsReady = true;
            Say("ready");

            // LoadNow refuses before IsReady, and is fire-and-forget: the phase stays Loading until its callback fires (callers wait on that, not on IsReady).
            if (continueRequested)
            {
                Phase = ShellStartPhase.Loading;
                LoadNow(_ => Phase = ShellStartPhase.Playing);
            }
            else
            {
                Phase = ShellStartPhase.Playing;
            }
        }

        // ---------------------------------------------------------------------------------------------
        // Start flow (Title mode)
        // ---------------------------------------------------------------------------------------------

        private enum StartChoice { None, NewGame, Continue }

        private IEnumerator RunStartFlow(GameSession session)
        {
            CreatorModel = new CharacterCreatorModel(session.RulesDatabase, CreatorContentLoader.Build(creatorPicks.text, creatorClassTable.text), Debug.LogWarning);

            var choice = StartChoice.None;
            CreatorResult confirmed = null;
            var cancelled = false;
            titleScreen.Initialize(() => File.Exists(SavePath), () => choice = StartChoice.NewGame, () => choice = StartChoice.Continue);
            creatorScreen.UsePlaceholderStyle = false; // Task 5.3: the skin's real look; the panel settings already carry the theme (BarrowsEndCreatorAuthor)
            creatorScreen.Initialize(CreatorModel, result => confirmed = result, () => cancelled = true);

            while (true)
            {
                Phase = ShellStartPhase.Title;
                creatorScreen.Hide();
                titleScreen.Show();
                choice = StartChoice.None;
                yield return new WaitUntil(() => choice != StartChoice.None);
                titleScreen.Hide();

                if (choice == StartChoice.Continue)
                {
                    continueRequested = true;
                    yield break;
                }

                Phase = ShellStartPhase.Creating;
                CreatorRuns++;
                confirmed = null;
                cancelled = false;
                creatorScreen.Show();
                yield return new WaitUntil(() => confirmed != null || cancelled);
                creatorScreen.Hide();

                if (confirmed != null)
                {
                    protagonist = confirmed;
                    yield break;
                }
            }
        }

        private SceneTravelCoordinator NewTravelCoordinator(GameSession session, WorldSpawnService spawner) =>
            new(session, spawner, session.Party, transitionGate, cameraRig != null ? cameraRig.Rig : null, gridPathfinding: pathfinding);

        private InteractionControllerBehaviour NewInteractionHost()
        {
            var go = new GameObject("InteractionControllerHost");
            go.transform.SetParent(transform, false);
            return go.AddComponent<InteractionControllerBehaviour>();
        }

        private void ActivateGridOf(Scene scene)
        {
            var provider = WalkabilityGridProvider.FindInScene(scene);
            if (provider == null) throw new InvalidOperationException($"Location '{scene.path}' has no WalkabilityGridProvider: bake it ('Barrows End/Location/Bake Selected Location').");
            pathfinding.SetActiveGrid(provider.Grid);
        }

        /// <summary>The fixed party-slot id formula (Task 3.4): the same id every session/save, since a save must
        /// load after a process restart and a runtime-generated id would only work in the same process. Public so
        /// the HUD (Task 5.1) can build a memberId -> displayName map without duplicating this formula.</summary>
        public static RuntimeEntityId MemberIdFor(int index) => RuntimeEntityId.FromString("ba00000000000000000000000000000" + (index + 1).ToString("x"));

        private static readonly DefinitionId[] AbilityScoreIds =
        {
            new("stat-strength"), new("stat-dexterity"), new("stat-constitution"),
            new("stat-intelligence"), new("stat-wisdom"), new("stat-charisma"),
        };

        /// <summary>Task 5.2: an authored companion (<see cref="GateMemberSpec"/>) has no ability-score data yet -- real
        /// per-companion scores are content the user will author once the whole world's data is being built. Until then,
        /// "4d6 drop lowest" (the toolkit's own <c>RandomRollsGenerationMethod</c>, the same generator the creator offers
        /// the player) gives a companion a real, non-zero StatBlock rather than the all-zero one <see cref="StatBlock.GetScore"/>
        /// silently returns for an absent stat. A distinct seed per member (offset from the session seed) keeps this
        /// deterministic without perturbing the main DiceService's own roll sequence.</summary>
        private static void RollAbilityScores(IRulesDatabase rules, CharacterState character, int seed)
        {
            var stats = new List<StatDefinition>(AbilityScoreIds.Length);
            foreach (var id in AbilityScoreIds)
                if (rules.TryResolve<StatDefinition>(id, out var stat)) stats.Add(stat);

            if (stats.Count == 0) return; // a ruleset with no SRD5 stats at all -- nothing to roll

            var block = new RandomRollsGenerationMethod(new SeededRandomSource(seed)).Generate(stats);
            foreach (var (statId, score) in block.AllScores) character.Stats.SetScore(statId, score);
        }

        private void SpawnParty(GameSession session, WorldSpawnService spawner, Scene coreScene)
        {
            var equipment = new EquipmentService(session.Characters, session.RulesDatabase, session.Inventory, session.EventBus);

            for (var i = 0; i < members.Length; i++)
            {
                var spec = members[i];
                var id = MemberIdFor(i);

                CharacterState character;
                IEnumerable<DefinitionId> gear;
                if (i == 0 && protagonist != null)
                {
                    // Task 4.3: the created protagonist takes slot 1 (Aldric's spec, gear included, is replaced), built with real hit points and wearing the class kit.
                    character = ProtagonistFactory.Build(session.RulesDatabase, protagonist, id);
                    gear = protagonist.StartingItemIds;
                }
                else
                {
                    character = new CharacterState(id);
                    character.AddResource(new ResourceState(GateSessionServices.HitPoints, spec.maxHp));
                    foreach (var entry in spec.appearance)
                    {
                        var parts = entry.Split('=');
                        if (parts.Length == 2) character.SetAppearance(new DefinitionId(parts[0].Trim()), new DefinitionId(parts[1].Trim()));
                    }

                    RollAbilityScores(session.RulesDatabase, character, seed + i + 1000);
                    gear = spec.gear.Select(g => new DefinitionId(g));
                }

                session.Characters.Register(character);
                session.Party.AddMember(id);
                partyVisualPrefabs[id] = partyPrefab;
                Dress(session, equipment, character, gear);

                var instance = spawner.BindRestoredView(spec.spawnId, id, default, partyPrefab, Vector3.zero, Quaternion.identity, default, startInactive: false);
                SceneManager.MoveGameObjectToScene(instance, coreScene);
            }

            if (session.Party.Members.Count > 0) session.Party.Select(session.Party.Members[0]);
        }

        // Worn BEFORE the view binds (the load shape, as the gate does): the sprite equipment binder reconciles from the inventory when the view initializes.
        // Until Task 4.3 the shell ignored every member's authored gear.
        private static void Dress(GameSession session, EquipmentService equipment, CharacterState character, IEnumerable<DefinitionId> gear)
        {
            foreach (var itemId in gear)
            {
                if (!session.RulesDatabase.TryResolve<ItemDefinition>(itemId, out var definition))
                {
                    Debug.LogWarning($"[Shell] Unknown gear '{itemId.Value}' for {character.Id}.");
                    continue;
                }

                var instance = new ItemInstance(RuntimeEntityId.NewId(), definition.Id);
                session.Inventory.AddItem(instance);
                equipment.Equip(instance.InstanceId, character.Id, definition.EquipmentSlotId);
            }
        }

        private static ILocationContent FindLocationContent(Scene scene)
        {
            foreach (var candidate in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (candidate is ILocationContent content && candidate.gameObject.scene == scene) return content;
            return null;
        }

        private void PrepareLocation(Scene scene, GameSession session, WorldSpawnService spawner)
        {
            var content = FindLocationContent(scene);
            if (content == null) return;
            LocationInitializer.Prepare(content, session, spawner, scene);
        }

        private void PlaceParty(GameSession session, Scene scene, string destinationId)
        {
            SceneTravelDestination start = null;
            foreach (var d in FindObjectsByType<SceneTravelDestination>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (d.gameObject.scene == scene && d.DestinationId == destinationId) { start = d; break; }
            if (start == null) throw new InvalidOperationException($"'{scene.path}' has no SceneTravelDestination '{destinationId}'.");

            var members = session.Party.Members;
            var wanted = new WedgeFormationSolver().Solve(start.Anchor.position, start.Anchor.forward, members.Count, spacing: 1.5f);
            var claimed = new List<Vector3>();
            for (var i = 0; i < members.Count; i++)
            {
                var agent = GateSessionServices.FindAgent(members[i]);
                if (agent == null) continue;
                if (!pathfinding.TrySamplePositionAvoiding(wanted[i], out var spot, 6f, claimed)) spot = start.Anchor.position;
                claimed.Add(spot);
                agent.Motor?.Warp(spot);
                agent.transform.rotation = start.Anchor.rotation;
            }
        }

        private void InitializeCamera(GameSession session, Scene scene)
        {
            var content = FindLocationContent(scene);
            if (content != null) cameraRig.Rig.SetBounds(content.CameraBoundsXZ);
            if (cameraFollow != null) cameraFollow.Initialize(session.Party, new SceneSelectableSource());
        }

        // A travel interactable's runner and gate cannot be authored into a location scene (the runner lives in the Core scene), so every travel adapter of
        // every loaded scene is wired here: at boot and each time a scene finishes loading.
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            WireTravelAdapters();
            WireTalkAdapters(Services.Session);
        }

        private void WireTravelAdapters()
        {
            foreach (var adapter in FindObjectsByType<TravelInteractionAdapter>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                adapter.Runner = runner;
                adapter.Gate = transitionGate;
            }
        }

        /// <summary>Task 6.0: TalkInteractionAdapter's session dependencies can't be authored at scene-authoring
        /// time -- no live session exists yet, and a session load swaps in a brand-new one (Characters/
        /// RulesDatabase/EventBus all change identity, and RestService captures those at construction, so a
        /// stale one silently rests/saves against a disposed session). LocationInitializer only wires an
        /// adapter's Store half (via RebindStoresAndLoot/Prepare); this covers the rest, for every
        /// TalkInteractionAdapter currently loaded, idempotently -- mirrors WireTravelAdapters' own shape and
        /// AdventureSliceBootstrap's own precedent (TestProject/Assets/AdventureSlice/Scripts/
        /// AdventureSliceBootstrap.cs).</summary>
        private void WireTalkAdapters(GameSession session)
        {
            var skillCheckRule = new DefaultSkillCheckRule(session.Characters, session.RulesDatabase);
            var restService = new RestService(session.Characters, session.RulesDatabase, session.EventBus);
            foreach (var adapter in FindObjectsByType<TalkInteractionAdapter>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                adapter.Party = session.Party;
                adapter.Characters = session.Characters;
                adapter.RulesDatabase = session.RulesDatabase;
                adapter.Timeline = session.Timeline;
                adapter.Inventory = session.Inventory;
                adapter.DiceService = session.DiceService;
                adapter.SkillCheckRule = skillCheckRule;
                adapter.RestService = restService;
                adapter.RequestSave = () => SaveNow();
            }
        }

        // ---------------------------------------------------------------------------------------------
        // Save / load (real SaveService, cross-scene SessionLoadCoordinator.LoadCoroutine)
        // ---------------------------------------------------------------------------------------------

        public bool SaveNow()
        {
            if (!IsReady || transitionGate.IsBusy) { Say("cannot save right now"); return false; }

            var session = Services.Session;
            var snapshots = Services.Spawner.CaptureSnapshot().ToList();
            var extras = snapshots.Select(s => RuntimeEntityId.FromString(s.CharacterId)).ToList();
            var data = SaveService.Capture(
                session.Party, session.Characters, session.Inventory, session.Timeline, session.Quests,
                additionalCharacterIds: extras, worldSpawns: snapshots, stores: session.Stores, lootContainers: session.LootContainers,
                scenePath: SceneManager.GetActiveScene().path, rulesetId: ruleset.RulesetId);

            var result = new SaveFileStore().Save(SavePath, SaveService.ToJson(data));
            Say(result.Success ? "saved" : "save failed: " + result.Message);
            return result.Success;
        }

        /// <summary>Starts a load; <see cref="IsLoading"/> is true until it finishes. The optional callback receives the outcome.</summary>
        public void LoadNow(Action<LoadOutcome> onFinished = null)
        {
            if (!IsReady || IsLoading || transitionGate.IsBusy) { Say("cannot load right now"); onFinished?.Invoke(LoadOutcome.LoadAlreadyInProgress); return; }
            if (!File.Exists(SavePath)) { Say("nothing saved yet"); onFinished?.Invoke(LoadOutcome.FileNotFound); return; }

            runner.Run(loadCoordinator.LoadCoroutine(
                SavePath, ruleset, new DiceService(new SeededRandomSource(seed)), RequiredPolicies, host, Services.Spawner,
                gameObject.scene, partyVisualPrefabs, result =>
                {
                    if (result.Outcome == LoadOutcome.Success) OnLoaded(result);
                    else Say($"load failed ({result.Outcome}): {result.Message}");
                    onFinished?.Invoke(result.Outcome);
                }));
        }

        private void OnLoaded(LoadResult result)
        {
            var session = result.Session;
            var spawner = result.WorldSpawnService;
            runner.Coordinator = NewTravelCoordinator(session, spawner);
            Services.Rebind(session, spawner);

            var scene = SceneManager.GetActiveScene();
            ActivateGridOf(scene); // the load activates the grid for a cross-scene load; this also covers a same-scene one
            WireTravelAdapters();
            WireTalkAdapters(session);
            // NOT PrepareLocation/LocationInitializer.Prepare here -- SessionLoadCoordinator already fully
            // restores this scene's SpawnPoints itself (its own BindRestoredView calls); re-running Prepare's
            // SpawnPoints loop on top of that would duplicate every already-restored NPC (BindRestoredView
            // unconditionally instantiates a fresh view even for an already-known spawn id). Only the Store/
            // LootContainer half needs rebinding here -- SessionLoadCoordinator does not touch those at all.
            var content = FindLocationContent(scene);
            if (content != null)
            {
                LocationInitializer.RebindStoresAndLoot(content, session);
                // RebindStoresAndLoot only just (re)created these StoreInventoryState/LootContainerState
                // instances via GetOrCreateStore/GetOrCreateLootContainer -- freshly seeded, not yet carrying
                // whatever was actually saved (SessionLoadCoordinator's own SaveService.Restore call, earlier in
                // the load, only covers Party/Characters/Inventory/Timeline/Quests). Apply the saved listings/
                // multipliers/contents onto them now that they actually exist -- found live (a real depleted-
                // stock-survives-a-load test) that skipping this silently reseeds every store back to its
                // InitialListings on every load.
                SaveService.RestoreStores(result.Data, session.Stores);
                SaveService.RestoreLootContainers(result.Data, session.LootContainers);
            }
            InitializeCamera(session, scene);
            Say("loaded");
        }

        public void Say(string message)
        {
            LastMessage = message;
            MessageChanged?.Invoke(message);
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Services?.Dispose();
            commandSource?.Dispose();
            host?.EndSession();
        }
    }
}

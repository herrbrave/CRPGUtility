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
using CRPG.Quests;
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
        /// (never persisted in a save, see SaveService) to build its PartyPresenter's memberNames dictionary.
        /// Task 6.1: reflects <see cref="MembersOverride"/> once <c>Boot</c> has run (falls back to the serialized
        /// field before then), so a test that overrides members sees the SAME array through both this property
        /// and what actually got spawned.</summary>
        public IReadOnlyList<GateMemberSpec> Members => effectiveMembers ?? members;

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
        private GateMemberSpec[] effectiveMembers;
        private GameSessionHost host;
        private GridPathfindingService pathfinding;
        private WorldTransitionGate transitionGate;
        private SceneTravelRunner runner;
        private SessionLoadCoordinator loadCoordinator;
        private PartyCommandFilter commandSource;
        private CreatorResult protagonist;
        private bool continueRequested;

        public GateSessionServices Services { get; private set; }
        public TacticalCombatSession CombatSession { get; private set; }
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

        /// <summary>Task 6.1: test hook mirroring <see cref="StartingSceneOverride"/> exactly -- when set,
        /// <c>Boot</c> uses this instead of the serialized <c>members</c> array. The real campaign now boots with
        /// only the protagonist (Garrick/Elara/Silas join through <see cref="RecruitCompanion"/>, not at boot),
        /// but several existing tests assert against the OLD 4-member demo-slice geometry (drag-select/
        /// multi-select coverage, HP-roll-differs assertions, doll member-cycling) -- confirmed by reading those
        /// test files during the Opus review, not assumed. Those tests set this to <c>GateLayout.DefaultMembers()</c>
        /// before booting Core and clear it in teardown, exactly as they already do for <see cref="StartModeOverride"/>.
        /// A static, same reason as the other two overrides.</summary>
        public static GateMemberSpec[] MembersOverride { get; set; }

        /// <summary>Where the start flow is; Playing once the game is running (for Continue, only after its load finished).</summary>
        public ShellStartPhase Phase { get; private set; } = ShellStartPhase.Booting;

        /// <summary>The creator's model while the start flow runs (null in AuthoredParty mode).</summary>
        public CharacterCreatorModel CreatorModel { get; private set; }

        /// <summary>How many times the creator was shown (a test asserts Continue never shows it).</summary>
        public int CreatorRuns { get; private set; }

        public bool IsLoading => loadCoordinator != null && loadCoordinator.IsLoading;
        public string LastMessage { get; private set; } = "";

        /// <summary>Raised after a load (the HUD's Load button, a lost fight's checkpoint reload, Continue) has swapped in a brand-new session,
        /// so UI bound to the old session's party/characters can rebind. Only the Load button used to do this, which left the party panel
        /// showing a stale session after every other kind of load.</summary>
        public event Action SessionRebound;
        public event Action<string> MessageChanged;

        /// <summary>Where <see cref="SaveNow"/> writes and <see cref="LoadNow"/> reads. Tests point this at a temp file.</summary>
        public string SavePath { get; set; }

        /// <summary>The scene holding the current location (the active scene).</summary>
        public Scene LocationScene => SceneManager.GetActiveScene();

        private void Awake() => SavePath ??= Path.Combine(Application.persistentDataPath, "barrowsend-quicksave.json");

        private void Start() => StartCoroutine(Boot());

        // Task 6.1: ticks the tactical combat stack (ActionScheduler + AI turn driver + manual win-condition
        // polling -- neither CombatEncounterService nor TacticalEncounterController auto-detects "one side
        // wiped," confirmed by reading both classes). CombatSession.Tick itself no-ops when no encounter is
        // active, so this costs nothing outside of combat.
        private void Update()
        {
            CombatSession?.Tick(Time.deltaTime);
            HookInteractionEnded();
        }

        // The interaction controller is rebuilt on every session rebind, so re-subscribe whenever it changes.
        // A finished conversation can record Timeline events a quest objective tracks (a turn-in, a flag), and
        // recording never re-checks quests by itself.
        private InteractionController hookedInteractionController;

        private void HookInteractionEnded()
        {
            var controller = GetComponentInChildren<InteractionControllerBehaviour>()?.Controller;
            if (controller == null || ReferenceEquals(controller, hookedInteractionController)) return;

            hookedInteractionController = controller;
            controller.RequestEnded += _ => RefreshAllActiveQuests();
        }

        /// <summary>Combat owns the party from the first turn: drop whatever it was in the middle of. Without this a walk
        /// to a door or arch carried on through the fight and the interaction (travel!) still completed on arrival.</summary>
        private void HaltPartyForCombat()
        {
            GetComponentInChildren<InteractionControllerBehaviour>()?.Controller?.Cancel();

            var session = Services?.Session;
            if (session == null) return;
            foreach (var member in session.Party.Members)
                GateSessionServices.FindAgent(member)?.Motor?.Stop();
        }

        /// <summary>Re-checks every Active quest against the Timeline. Called after a quest starts (earlier
        /// progress may already count) and after every finished interaction.</summary>
        public void RefreshAllActiveQuests()
        {
            var session = Services?.Session;
            if (session == null) return;

            var service = new QuestService(session.Quests, session.Timeline, session.EventBus, session.Inventory);
            foreach (var state in session.Quests.Quests.Where(q => q.Status == QuestStatus.Active).ToList())
            {
                if (session.RulesDatabase.TryResolve<QuestDefinition>(state.QuestId, out var definition)) service.RefreshProgress(definition);
            }
        }

        private IEnumerator Boot()
        {
            var effectiveScenePath = StartingSceneOverride?.scenePath ?? startingScenePath;
            var effectiveDestinationId = StartingSceneOverride?.destinationId ?? startingDestinationId;
            effectiveMembers = MembersOverride ?? members;

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

            SpawnParty(session, spawner, coreScene, effectiveMembers);

            var interactionHost = GetComponentInChildren<InteractionControllerBehaviour>() ?? NewInteractionHost();
            commandSource = new PartyCommandFilter(inputSource, () => Services?.Session?.Party);
            Services = new GateSessionServices(commandSource, interactionHost, pathfinding) { PartyCanDie = false };
            Services.Rebind(session, spawner);

            // Task 6.1: constructed once (never rebuilt on a load, unlike Services -- the HudRoot/AbilityBarView
            // it wires are real scene objects that outlive any session swap); RebindSession keeps its own
            // session-scoped references fresh the same way WireTalkAdapters does for TalkInteractionAdapter.
            if (CombatSession == null)
            {
                var hudRoot = FindFirstObjectByType<HudRoot>();
                var abilityBarGo = new GameObject("AbilityBar");
                abilityBarGo.transform.SetParent(transform);
                var abilityBarView = abilityBarGo.AddComponent<AbilityBarView>();
                var turnOrderGo = new GameObject("TurnOrder");
                turnOrderGo.transform.SetParent(transform);
                var turnOrderView = turnOrderGo.AddComponent<TurnOrderView>();
                CombatSession = new TacticalCombatSession(commandSource, inputSource, hudRoot, abilityBarView, BarrowsEndLayers.RouterMask,
                    turnOrderView, DisplayNameFor, Say, id => Services.DeathSaves.RollDeathSave(id), id => Services.DeathSaves.Stabilize(id));
                CombatSession.StartBlocker = () => transitionGate.IsBusy || IsLoading;
                CombatSession.EncounterStarted += HaltPartyForCombat;
            }
            CombatSession.RebindSession(session, spawner);

            PrepareLocation(startingScene, session, spawner);
            WireTravelAdapters();
            WireTalkAdapters(session);
            WireCombatTriggers(session);
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

        /// <summary>Task 6.1: the currency item every store's <c>CurrencyItemId</c> is wired to
        /// (<see cref="WireTalkAdapters"/>) -- authored by <c>BarrowsEndGearAuthor</c> since Task 6.0.</summary>
        private static readonly DefinitionId CurrencyItemId = new("item-gold");

        /// <summary>Task 6.1: how much starting gold the party begins with -- otherwise every shop is
        /// technically wired but practically unusable (a real gap the Opus review found: <see cref="WireTalkAdapters"/>
        /// never set <c>CurrencyItemId</c> before this task, and nothing granted any either).</summary>
        private const int StartingGold = 50;

        // Duplicated from BarrowsEndGearAuthor.StrikeAbilityId (Editor-only) -- Task 6.1: every party member,
        // protagonist or companion, needs this granted or the tactical combat ability bar shows nothing to
        // click. ProtagonistFactory.Build grants its own copy directly (the protagonist's CharacterState is
        // fully built there before SpawnParty ever sees it); every OTHER member (boot-time companion or a
        // recruit) is granted it here / in RecruitCompanion.
        private static readonly DefinitionId StrikeAbility = new("ability-strike");

        private void SpawnParty(GameSession session, WorldSpawnService spawner, Scene coreScene, GateMemberSpec[] roster)
        {
            var equipment = new EquipmentService(session.Characters, session.RulesDatabase, session.Inventory, session.EventBus);

            for (var i = 0; i < roster.Length; i++)
            {
                var spec = roster[i];
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
                    character.LearnAbility(StrikeAbility); // the protagonist branch already granted its own copy inside ProtagonistFactory.Build
                }

                session.Characters.Register(character);
                session.Party.AddMember(id);
                partyVisualPrefabs[id] = partyPrefab;
                Dress(session, equipment, character, gear);

                var instance = spawner.BindRestoredView(spec.spawnId, id, default, partyPrefab, Vector3.zero, Quaternion.identity, default, startInactive: false);
                SceneManager.MoveGameObjectToScene(instance, coreScene);
            }

            if (session.Party.Members.Count > 0) session.Party.Select(session.Party.Members[0]);

            // Task 6.1 (fact 6): every companion's fixed id gets a partyVisualPrefabs entry unconditionally, whether
            // or not they're recruited yet -- they all use the same partyPrefab, so this costs nothing, and it's
            // what lets SessionLoadCoordinator restore an ALREADY-recruited companion after a process restart
            // (a fresh process's dictionary would otherwise only know about boot-time members and throw
            // ViewBindingFailed -- confirmed as a real crash, not a hypothetical, by the Opus review).
            foreach (var key in CompanionRoster.Keys) partyVisualPrefabs[CompanionRoster.IdFor(key)] = partyPrefab;

            // Task 6.1 (T6.1-D7 / B10): shops are otherwise wired but unusable -- nothing ever granted the party
            // any currency. Granted once here, not per-load (SaveService.Capture/Restore already round-trips
            // PartyInventoryState, so a loaded save keeps whatever gold was actually spent/earned).
            if (session.RulesDatabase.TryResolve<ItemDefinition>(CurrencyItemId, out var gold))
                session.Inventory.AddItem(new ItemInstance(RuntimeEntityId.NewId(), gold.Id, StartingGold));
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
            WireCombatTriggers(Services.Session);
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
                adapter.RequestRecruit = RecruitCompanion;
                adapter.RequestStartQuest = RequestStartQuest;
                adapter.CurrencyItemId = CurrencyItemId;
            }

            // Task 6.1 (T6.1-D2): a companion's own talk-point fixture is never destroyed mid-conversation (its
            // ConversationController/UIDocument might still be live) -- hidden here instead, the next time this
            // scene's session-scoped state is (re)wired, which covers boot, every scene load, and a session load.
            HideRecruitedCompanionFixtures(session);
        }

        /// <summary>Task 6.1: wires every EncounterTrigger currently loaded with the current session/
        /// spawner/CombatSession, and re-checks each one's own persisted "already fired" Timeline flag -- same
        /// shape and call sites as WireTalkAdapters (boot, every scene load, after a session load).</summary>
        private void WireCombatTriggers(GameSession session)
        {
            WireQuestNotifications(session);
            foreach (var trigger in FindObjectsByType<QuestStartTrigger>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                trigger.Session = session;
                trigger.RequestStartQuest = RequestStartQuest;
            }

            foreach (var hint in FindObjectsByType<HintTrigger>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                hint.Session = session;
                hint.Notify = Say;
            }

            foreach (var gate in FindObjectsByType<PortcullisGate>(FindObjectsInactive.Include, FindObjectsSortMode.None)) gate.Refresh(session);
            foreach (var lever in FindObjectsByType<LeverInteractionAdapter>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                lever.Session = session;
                lever.RequestQuestRefresh = RefreshAllActiveQuests;
                lever.Notify = Say;
                lever.OnPulled = () =>
                {
                    foreach (var gate in FindObjectsByType<PortcullisGate>(FindObjectsInactive.Include, FindObjectsSortMode.None)) gate.Refresh(session);
                };
                lever.RefreshState();
            }

            foreach (var node in FindObjectsByType<HarvestInteractionAdapter>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                node.Session = session;
                node.RequestQuestRefresh = RefreshAllActiveQuests;
                node.Notify = Say;
                node.RefreshState();
            }

            foreach (var trigger in FindObjectsByType<EncounterTrigger>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                trigger.Session = session;
                trigger.Spawner = Services.Spawner;
                trigger.CombatSession = CombatSession;
                trigger.RequestQuestRefresh = RefreshAllActiveQuests;
                trigger.RequestCheckpoint = SaveCheckpoint;
                trigger.RequestDefeat = HandlePartyDefeated;
                trigger.Notify = Say;
                trigger.RefreshFiredState();
            }
        }

        // BarrowsEnd has no quest log UI yet, so a quest's life shows up as a one-line message. Re-subscribed on every
        // session (boot, load) because each session has its own event bus.
        private IGameEventBus questNotificationBus;
        private Action<GameEventContext> onQuestStarted, onQuestObjectiveCompleted, onQuestCompleted;

        private void WireQuestNotifications(GameSession session)
        {
            if (questNotificationBus != null)
            {
                questNotificationBus.Unsubscribe(GameEvent.QuestStarted, onQuestStarted);
                questNotificationBus.Unsubscribe(GameEvent.QuestObjectiveCompleted, onQuestObjectiveCompleted);
                questNotificationBus.Unsubscribe(GameEvent.QuestCompleted, onQuestCompleted);
            }

            questNotificationBus = session.EventBus;
            onQuestStarted = ctx => Say($"Quest started: {QuestName(session, ctx)}");
            onQuestCompleted = ctx =>
            {
                Say($"Quest complete: {QuestName(session, ctx)}");
                PlayEpilogueFor(session, ctx.Data.TryGetValue("QuestId", out var id) ? id as string : null);
            };
            onQuestObjectiveCompleted = ctx =>
            {
                var objective = ctx.Data.TryGetValue("ObjectiveId", out var id) ? id as string : null;
                var name = session.RulesDatabase.TryResolve<QuestDefinition>(new DefinitionId((string)ctx.Data["QuestId"]), out var quest)
                    ? quest.Objectives.FirstOrDefault(o => o.Id == objective)?.DisplayName : null;
                Say($"Objective complete: {name ?? objective}");
            };
            questNotificationBus.Subscribe(GameEvent.QuestStarted, onQuestStarted);
            questNotificationBus.Subscribe(GameEvent.QuestObjectiveCompleted, onQuestObjectiveCompleted);
            questNotificationBus.Subscribe(GameEvent.QuestCompleted, onQuestCompleted);
        }

        private void PlayEpilogueFor(GameSession session, string questId)
        {
            if (string.IsNullOrEmpty(questId) || session.Party.Members.Count == 0) return;
            foreach (var epilogue in FindObjectsByType<EpilogueTrigger>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (epilogue.QuestId == questId) epilogue.Play(session.Party.Members[0]);
        }

        private static string QuestName(GameSession session, GameEventContext ctx)
        {
            var id = ctx.Data.TryGetValue("QuestId", out var value) ? value as string : null;
            return id != null && session.RulesDatabase.TryResolve<QuestDefinition>(new DefinitionId(id), out var quest) ? quest.DisplayName : id;
        }

        /// <summary>Task 6.1 (T6.1-D4): DialogueContext.RequestStartQuest's real target -- resolves the quest by
        /// id and starts it through a freshly-constructed QuestService, same "constructed fresh every call, never
        /// cached" reasoning as WireTalkAdapters' own RestService (captures session-scoped references).</summary>
        private void RequestStartQuest(string questKey)
        {
            var session = Services?.Session;
            if (session == null) return;
            if (!session.RulesDatabase.TryResolve<QuestDefinition>(new DefinitionId(questKey), out var quest))
            {
                Debug.LogWarning($"[Shell] Unknown quest '{questKey}'.");
                return;
            }

            new QuestService(session.Quests, session.Timeline, session.EventBus, session.Inventory).StartQuest(quest);
            RefreshAllActiveQuests();
        }

        /// <summary>Task 6.1 (T6.1-D2): recruits one companion for real -- builds their CharacterState fresh (they
        /// were never a spawned CharacterAgent to "promote"; a world NPC can't even be clicked, see
        /// DialogueContext.RequestRecruit's own doc comment), mirroring exactly what SpawnParty already does for
        /// a boot-time member. Idempotent: a second call for an already-recruited companion (e.g. a stray repeat
        /// invocation) is a no-op rather than a duplicate-registration throw.</summary>
        public void RecruitCompanion(string companionKey)
        {
            if (Services?.Session == null) return;
            var session = Services.Session;
            var id = CompanionRoster.IdFor(companionKey);
            if (session.Party.Members.Contains(id)) return;

            var spec = CompanionRoster.SpecFor(companionKey);
            var character = new CharacterState(id);
            character.AddResource(new ResourceState(GateSessionServices.HitPoints, spec.maxHp));
            foreach (var entry in spec.appearance)
            {
                var parts = entry.Split('=');
                if (parts.Length == 2) character.SetAppearance(new DefinitionId(parts[0].Trim()), new DefinitionId(parts[1].Trim()));
            }

            RollAbilityScores(session.RulesDatabase, character, seed + CompanionSeedOffset(companionKey));
            character.LearnAbility(StrikeAbility);
            session.Characters.Register(character);
            session.Party.AddMember(id);
            partyVisualPrefabs[id] = partyPrefab; // already set at boot (SpawnParty); harmless to set again

            var equipment = new EquipmentService(session.Characters, session.RulesDatabase, session.Inventory, session.EventBus);
            Dress(session, equipment, character, spec.gear.Select(g => new DefinitionId(g)));

            // Falls back to the active party member's own (necessarily walkable) position if no fixture is found
            // in the scene -- ShellBootstrap's own transform is an authoring-time position with no guarantee of
            // sitting inside the walkable grid at all, which silently stranded a recruit outside pathfinding
            // range (found live by this task's own recruit-proof test: the click-to-move assertion failed with
            // zero movement, not an exception, because SetDestination correctly rejected an unreachable point).
            var recruitPosition = FindCompanionFixturePosition(companionKey)
                ?? GateSessionServices.FindAgent(session.Party.ActiveCharacter)?.transform.position
                ?? transform.position;
            var instance = Services.Spawner.BindRestoredView(spec.spawnId, id, default, partyPrefab, recruitPosition, Quaternion.identity, default, startInactive: false);
            SceneManager.MoveGameObjectToScene(instance, gameObject.scene);

            var agent = instance.GetComponent<CharacterAgent>();
            if (agent != null && agent.Motor != null) Services.RegisterMemberMotor(id, agent.Motor);
        }

        // A fixed per-companion offset (distinct from the boot-order-indexed "seed + i + 1000" SpawnParty uses,
        // since recruiting isn't boot-ordered) -- deterministic, so the same companion always rolls the same
        // ability scores for a given session seed.
        private static int CompanionSeedOffset(string companionKey) => companionKey switch
        {
            "garrick" => 2000, "elara" => 2001, "silas" => 2002,
            _ => 2999,
        };

        private static Vector3? FindCompanionFixturePosition(string companionKey)
        {
            var name = CompanionRoster.FixtureName(companionKey);
            foreach (var adapter in FindObjectsByType<TalkInteractionAdapter>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (adapter.gameObject.name == name) return adapter.transform.position;
            return null;
        }

        private static void HideRecruitedCompanionFixtures(GameSession session)
        {
            foreach (var key in CompanionRoster.Keys)
            {
                if (!session.Party.Members.Contains(CompanionRoster.IdFor(key))) continue;
                var name = CompanionRoster.FixtureName(key);
                foreach (var adapter in FindObjectsByType<TalkInteractionAdapter>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (adapter.gameObject.name == name) adapter.gameObject.SetActive(false);
            }
        }

        /// <summary>Task 6.1 (T6.1-D9): display name for the HUD -- covers both boot members and the 3 fixed
        /// companion ids, so a recruited companion shows their real name instead of HudBootstrap's raw-GUID
        /// fallback (HudBootstrap previously only knew about boot-time members).</summary>
        public string DisplayNameFor(RuntimeEntityId id)
        {
            var roster = effectiveMembers ?? members;
            for (var i = 0; i < roster.Length; i++)
                if (MemberIdFor(i) == id) return roster[i].displayName;

            foreach (var key in CompanionRoster.Keys)
                if (CompanionRoster.IdFor(key) == id) return CompanionRoster.SpecFor(key).displayName;

            return null;
        }

        // ---------------------------------------------------------------------------------------------
        // Save / load (real SaveService, cross-scene SessionLoadCoordinator.LoadCoroutine)
        // ---------------------------------------------------------------------------------------------

        public bool SaveNow()
        {
            if (!IsReady || transitionGate.IsBusy) { Say("cannot save right now"); return false; }
            if (CombatSession != null && CombatSession.IsActive) { Say("cannot save during combat"); return false; }

            var saved = SaveTo(SavePath, out var message);
            Say(saved ? "saved" : "save failed: " + message);
            return saved;
        }

        /// <summary>Where the pre-encounter checkpoint lives (never the player's own quicksave).</summary>
        public string CheckpointPath => SavePath + ".checkpoint";

        /// <summary>Silently writes the checkpoint a lost encounter reloads. Called by an <see cref="EncounterTrigger"/>
        /// just before it spawns hostiles; the player's quicksave is left alone.</summary>
        public bool SaveCheckpoint() => IsReady && !transitionGate.IsBusy && SaveTo(CheckpointPath, out _);

        private bool SaveTo(string path, out string message)
        {
            var session = Services.Session;
            var snapshots = Services.Spawner.CaptureSnapshot().ToList();
            var extras = snapshots.Select(s => RuntimeEntityId.FromString(s.CharacterId)).ToList();
            var data = SaveService.Capture(
                session.Party, session.Characters, session.Inventory, session.Timeline, session.Quests,
                additionalCharacterIds: extras, worldSpawns: snapshots, stores: session.Stores, lootContainers: session.LootContainers,
                scenePath: SceneManager.GetActiveScene().path, rulesetId: ruleset.RulesetId);

            var result = new SaveFileStore().Save(path, SaveService.ToJson(data));
            message = result.Message;
            return result.Success;
        }

        /// <summary>The party was wiped: say so, give the player a beat, then reload the pre-encounter checkpoint.</summary>
        public void HandlePartyDefeated()
        {
            Say("The party has fallen...");
            StartCoroutine(ReloadCheckpointAfterDelay());
        }

        private IEnumerator ReloadCheckpointAfterDelay()
        {
            yield return new WaitForSeconds(1.5f);
            if (File.Exists(CheckpointPath)) LoadFrom(CheckpointPath);
            else Say("No checkpoint to return to.");
        }

        /// <summary>Starts a load; <see cref="IsLoading"/> is true until it finishes. The optional callback receives the outcome.</summary>
        public void LoadNow(Action<LoadOutcome> onFinished = null) => LoadFrom(SavePath, onFinished);

        public void LoadFrom(string path, Action<LoadOutcome> onFinished = null)
        {
            if (!IsReady || IsLoading || transitionGate.IsBusy) { Say("cannot load right now"); onFinished?.Invoke(LoadOutcome.LoadAlreadyInProgress); return; }
            if (!File.Exists(path)) { Say("nothing saved yet"); onFinished?.Invoke(LoadOutcome.FileNotFound); return; }

            runner.Run(loadCoordinator.LoadCoroutine(
                path, ruleset, new DiceService(new SeededRandomSource(seed)), RequiredPolicies, host, Services.Spawner,
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

            CombatSession.RebindSession(session, spawner);

            var scene = SceneManager.GetActiveScene();
            ActivateGridOf(scene); // the load activates the grid for a cross-scene load; this also covers a same-scene one
            WireTravelAdapters();
            WireTalkAdapters(session);
            WireCombatTriggers(session);
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

            // A reload can drop the party inside an encounter zone (a lost fight's checkpoint always does): no instant
            // re-fire. After Say("loaded") so the zone's own "step out and back in" message is the one left showing.
            foreach (var trigger in FindObjectsByType<EncounterTrigger>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                trigger.DisarmUntilPartyLeaves();

            SessionRebound?.Invoke();
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

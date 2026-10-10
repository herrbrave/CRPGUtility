using System;
using System.Collections.Generic;
using System.Linq;
using CRPG.AI;
using CRPG.Actors;
using CRPG.Combat;
using CRPG.Core;
using CRPG.Exploration;
using CRPG.Gameplay;
using CRPG.InputSystem;
using CRPG.Navigation;
using CRPG.Party;
using CRPG.Rules;
using CRPG.Rules.SRD5;
using CRPG.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace VikingRaid.Game
{
    public enum GameState { Booting, Exploring, Staging, Fighting, Won, Defeated }

    /// <summary>
    /// The composition root of Viking Raid (Task 4): builds a real ruleset session, spawns the four heroes and every pre-placed goblin through <c>WorldSpawnService</c>, wires input,
    /// exploration, the HUD and the camera, and runs the loop: exploring -> a trigger -> staging (the party walks to the encounter's stage anchors so the fight starts from the
    /// formation the balance was measured on) -> a tactical fight -> won (everyone healed and revived, the road continues) or wiped (reload at the checkpoint) -> after the last boss,
    /// "Camp destroyed". Authored into the scene by <c>WorldAuthor</c>; tests may inject input through <see cref="InputFactory"/> (read once, in Start).
    /// </summary>
    public sealed class VikingGame : MonoBehaviour
    {
        /// <summary>A command source plus the way to hand it the ability targeting gate (the real input source and the test router both own one).</summary>
        public sealed class InputBinding
        {
            public IPlayerCommandSource Source;
            public Action<IPointerTargetingGate> SetGate;
        }

        /// <summary>Tests set this before the scene loads; it survives a scene reload on purpose, so a post-wipe boot keeps the injected input. Reset in every test SetUp/TearDown.</summary>
        public static Func<InputBinding> InputFactory { get; set; }
        public static VikingGame Instance { get; private set; }

        private const float StageArriveDistance = 0.5f, DefeatReloadSeconds = 2.5f;

        [SerializeField] private VikingRaidRulesetProvider ruleset;
        [SerializeField] private WorldAnchors anchors;
        [SerializeField] private InputSystemCommandSource inputSource;
        [SerializeField] private HudRoot hudRoot;
        [SerializeField] private AbilityBarView abilityBarView;
        [SerializeField] private TurnOrderView turnOrderView;
        [SerializeField] private PartyView partyView;
        [SerializeField] private VikingCamera cameraRig;
        [SerializeField] private CombatFx fx;
        [SerializeField] private int seed;
        [SerializeField] private float stageTimeout = 9f;

        private GameSessionHost host;
        private DeathSaveService deathSaves;
        private PartyExplorationController exploration;
        private InputBinding binding;
        private readonly List<RuntimeEntityId> heroes = new();
        private readonly Dictionary<int, List<(string UnitId, RuntimeEntityId Id)>> goblinsByEncounter = new();
        private readonly Dictionary<RuntimeEntityId, string> heroNames = new();
        private VisualElement abilityRegion, turnRegion, partyRegion, overlay;
        private Label feedbackLabel, bannerLabel;
        private float bannerUntil, stageElapsed, defeatTimer, hpPollAt;
        private string hpSignature = "";
        private bool booted;

        public GameSession Session { get; private set; }
        public WorldSpawnService Spawner { get; private set; }
        public VikingCombatSession Combat { get; private set; }
        public PartyCommandFilter Commands { get; private set; }
        public DeathSaveService DeathSaves => deathSaves;
        public GameState State { get; private set; } = GameState.Booting;
        public int ActiveEncounter { get; private set; } = -1;
        public int StageWarps { get; private set; }
        public float StageSeconds { get; private set; }
        public string Banner => bannerLabel?.text ?? "";
        public bool OverlayShown => overlay != null && overlay.style.display == DisplayStyle.Flex;
        public IReadOnlyList<RuntimeEntityId> Heroes => heroes;
        public CombatFx Fx => fx;
        public VikingCamera CameraRig => cameraRig;
        public VisualElement AbilityRegion => abilityRegion;
        public VisualElement OverlayElement => overlay;
        public int HudChildCount => hudRoot.DocumentRoot.childCount;
        public WorldAnchors Anchors => anchors;
        public int Seed { get => seed; set => seed = value; }
        public event Action<GameState> StateChanged;
        public event Action<int, FightOutcome> EncounterEnded;

        public IReadOnlyList<(string UnitId, RuntimeEntityId Id)> GoblinsOf(int encounter) =>
            goblinsByEncounter.TryGetValue(encounter, out var list) ? list : (IReadOnlyList<(string, RuntimeEntityId)>)Array.Empty<(string, RuntimeEntityId)>();

        private void Awake() => Instance = this;

        private void Start() => Build();

        private static readonly Type[] RequiredPolicies =
        {
            typeof(IArmorClassRule), typeof(IAttackBonusRule), typeof(IInitiativeRule), typeof(ISavingThrowRule), typeof(IDamageResistanceRule),
        };

        // ------------------------------------------------------------------ build
        public void Build()
        {
            if (booted) return;
            booted = true;
            if (ruleset == null || anchors == null || hudRoot == null || abilityBarView == null || turnOrderView == null || partyView == null)
                throw new InvalidOperationException("VikingGame is missing references: re-author the scene with 'Viking Raid/Task 3/Author World'.");

            host = gameObject.GetComponent<GameSessionHost>() ?? gameObject.AddComponent<GameSessionHost>();
            Session = host.StartSession(ruleset, new DiceService(new SeededRandomSource(seed != 0 ? seed : Environment.TickCount)), RequiredPolicies);
            Spawner = new WorldSpawnService(Session);

            var hp = new DefinitionId(GameDesign.Hp);
            deathSaves = new DeathSaveService(Session.Characters, Session.DiceService, Session.EventBus, hp, PartyVitals.Unconscious,
                usesDeathSaves: IsParty, canDie: id => !IsParty(id),   // heroes fall and roll death saves but never die (an overkill hit must not destroy a party member)
                reviveOnHeal: true);                                 // a heal that lifts a downed hero above 0 HP stands him up (the toolkit publishes the recovered cue)

            SpawnHeroes();
            SpawnGoblins();

            binding = InputFactory?.Invoke() ?? new InputBinding { Source = inputSource, SetGate = g => { if (inputSource != null) inputSource.TargetingGate = g; } };
            Commands = new PartyCommandFilter(binding.Source, () => Session.Party);

            exploration = new PartyExplorationController(Session.Party, new PartyMovementService(new NavMeshService(), new SceneSelectableSource()), new WedgeFormationSolver(), Commands,
                canMove: id => Session.Characters.TryGetCharacter(id, out var state) && !PartyVitals.IsDown(state),
                facingSource: new SceneSelectableSource());   // the order's facing is derived from the party's travel direction (the router always says world forward)
            foreach (var hero in heroes) { var agent = FindAgent(hero); if (agent?.Motor != null) exploration.RegisterMotor(hero, agent.Motor); }

            BuildHud();
            Combat = new VikingCombatSession(Commands, binding.SetGate, abilityBarView, turnOrderView, abilityRegion, turnRegion, feedbackLabel, deathSaves, Say, fx != null ? fx.Log : null);
            if (fx != null) ModelActionCues.LandingDelayRule = CombatFx.HitDelayBetween;
            Combat.Bind(Session, Spawner);
            abilityRegion.style.display = DisplayStyle.None; turnRegion.style.display = DisplayStyle.None;

            for (var i = 0; i < anchors.encounters.Length; i++)
            {
                var trigger = anchors.encounters[i].trigger.gameObject.GetComponent<VikingEncounterTrigger>() ?? anchors.encounters[i].trigger.gameObject.AddComponent<VikingEncounterTrigger>();
                trigger.Game = this; trigger.EncounterIndex = i; trigger.Armed = i >= Checkpoint.Index;
            }

            if (cameraRig != null)
            {
                cameraRig.Follow = FollowPoint;
                cameraRig.TacticalFocus = () => State is GameState.Staging or GameState.Fighting && ActiveEncounter >= 0 ? anchors.encounters[ActiveEncounter].cameraTarget.position : null;
            }

            SetState(Checkpoint.CampDestroyed ? GameState.Won : GameState.Exploring);
            Say(Checkpoint.SpawnAtCheckpoint ? "Back on the road. Click to move; walk toward the goblins to fight." : "Click to move your party. Follow the road to the goblin camp.");
        }

        private bool IsParty(RuntimeEntityId id) => Session != null && Session.Party.Members.Contains(id);

        private void SpawnHeroes()
        {
            var atCheckpoint = Checkpoint.SpawnAtCheckpoint && Checkpoint.Index < Checkpoint.EncounterCount;
            var slots = atCheckpoint ? anchors.encounters[Checkpoint.Index].checkpointSlots : anchors.startSlots;
            var facing = atCheckpoint ? WorldLayout.Arenas[Checkpoint.Index].Forward : Vector3.right;
            var index = 0;
            foreach (var unit in GameDesign.Heroes)
            {
                var agent = SpawnUnit($"hero-{unit.Id}", unit, slots[index++].position, facing);
                Session.Party.AddMember(agent.EntityId);
                heroes.Add(agent.EntityId);
                heroNames[agent.EntityId] = unit.DisplayName;
            }

            Session.Party.SelectAll();
        }

        private void SpawnGoblins()
        {
            for (var i = Mathf.Max(0, Checkpoint.Index); i < anchors.encounters.Length; i++)
            {
                var encounter = anchors.encounters[i]; var list = new List<(string, RuntimeEntityId)>();
                for (var j = 0; j < encounter.goblinUnitIds.Length; j++)
                {
                    var unit = GameDesign.Unit(encounter.goblinUnitIds[j]);
                    var agent = SpawnUnit($"e{i + 1}-{unit.Id}", unit, encounter.goblinSpawns[j].position, -WorldLayout.Arenas[i].Forward);
                    list.Add((unit.Id, agent.EntityId));
                }

                goblinsByEncounter[i] = list;
            }
        }

        private CharacterAgent SpawnUnit(string spawnId, UnitSpec unit, Vector3 position, Vector3 facing)
        {
            if (!Session.RulesDatabase.TryResolve<NpcDefinition>(new DefinitionId(unit.NpcId), out var npc))
                throw new InvalidOperationException($"{unit.NpcId} is missing from the ruleset: run 'Viking Raid/Task 2/Author Content'.");
            return Spawner.SpawnNpc(spawnId, npc, position, Quaternion.LookRotation(facing));
        }

        // ------------------------------------------------------------------ hud
        private void BuildHud()
        {
            abilityRegion = hudRoot.CreateRegion(HudCorner.BottomRight, 290f);
            turnRegion = hudRoot.CreateRegion(HudCorner.BottomLeft, 270f);
            partyRegion = hudRoot.CreateRegion(HudCorner.TopLeft, 430f);   // four roster cards (name + HP) need ~100 px each

            feedbackLabel = new Label { pickingMode = PickingMode.Ignore };
            feedbackLabel.style.whiteSpace = WhiteSpace.Normal; feedbackLabel.style.color = new Color(1f, 0.9f, 0.4f);
            feedbackLabel.style.backgroundColor = new Color(0f, 0f, 0f, 0.55f);
            feedbackLabel.style.paddingLeft = feedbackLabel.style.paddingRight = 8; feedbackLabel.style.paddingTop = feedbackLabel.style.paddingBottom = 4;
            feedbackLabel.style.display = DisplayStyle.None;

            bannerLabel = new Label { pickingMode = PickingMode.Ignore };
            var s = bannerLabel.style;
            s.position = Position.Absolute; s.top = Length.Percent(10); s.left = 460; s.right = 460; s.whiteSpace = WhiteSpace.Normal; s.unityTextAlign = TextAnchor.MiddleCenter;   // clear of the party panel and the turn order
            s.fontSize = 34; s.color = Color.white; s.unityFontStyleAndWeight = FontStyle.Bold; s.display = DisplayStyle.None;
            hudRoot.DocumentRoot.Add(bannerLabel);

            var hint = new Label("W A S D  pan     Q E  rotate     mouse wheel  zoom     Space  recenter") { pickingMode = PickingMode.Ignore };
            hint.style.position = Position.Absolute; hint.style.right = 16; hint.style.top = 12; hint.style.fontSize = 13;
            hint.style.color = new Color(1f, 1f, 1f, 0.65f); hint.style.backgroundColor = new Color(0f, 0f, 0f, 0.35f);
            hint.style.paddingLeft = hint.style.paddingRight = 8; hint.style.paddingTop = hint.style.paddingBottom = 4;
            hudRoot.DocumentRoot.Add(hint);

            hudRoot.DocumentRoot.Add(overlay = BuildOverlay());

            Session.RulesDatabase.TryResolve<ResourceDefinition>(new DefinitionId(GameDesign.Hp), out var hpDefinition);
            partyView.Initialize(new PartyPresenter(Session.Party, Session.Characters, hpDefinition, heroNames), partyRegion);
        }

        private VisualElement BuildOverlay()
        {
            var root = new VisualElement { pickingMode = PickingMode.Position };
            var s = root.style;
            s.position = Position.Absolute; s.left = s.right = s.top = s.bottom = 0; s.backgroundColor = new Color(0f, 0f, 0f, 0.7f);
            s.alignItems = Align.Center; s.justifyContent = Justify.Center; s.display = DisplayStyle.None;
            var title = new Label("Camp destroyed!") { name = "overlay-title" };
            title.style.fontSize = 56; title.style.color = new Color(1f, 0.85f, 0.35f); title.style.unityFontStyleAndWeight = FontStyle.Bold;
            var sub = new Label("The goblin warlord is slain and the raid is won.") { name = "overlay-sub" };
            sub.style.fontSize = 22; sub.style.color = Color.white; sub.style.marginBottom = 24;
            var button = new Button(PlayAgain) { text = "Play again", name = "overlay-button" };
            button.style.fontSize = 24; button.style.paddingLeft = button.style.paddingRight = 32; button.style.paddingTop = button.style.paddingBottom = 10;
            root.Add(title); root.Add(sub); root.Add(button);
            return root;
        }

        public void PlayAgain()
        {
            Checkpoint.Reset();
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex >= 0 ? SceneManager.GetActiveScene().path : SceneManager.GetActiveScene().path);
        }

        private void Say(string message) => ShowBanner(message, 4f, small: true);

        private void ShowBanner(string text, float seconds, bool small = false)
        {
            if (bannerLabel == null) return;
            bannerLabel.text = text; bannerLabel.style.fontSize = small ? 22 : 40;
            bannerLabel.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
            bannerUntil = Time.unscaledTime + seconds;
        }

        // ------------------------------------------------------------------ the loop
        private void Update()
        {
            if (!booted || Session == null) return;
            var dt = Time.deltaTime;

            Combat.Tick(dt);
            if (State == GameState.Staging) UpdateStaging(dt);
            UpdateIdleFacing();
            if (State == GameState.Defeated && (defeatTimer -= dt) <= 0f) Reload();
            if (bannerLabel != null && bannerLabel.style.display == DisplayStyle.Flex && Time.unscaledTime > bannerUntil && State != GameState.Defeated) bannerLabel.style.display = DisplayStyle.None;
            PollPartyHp();
        }

        /// <summary>The toolkit only turns a character to face its velocity; once a hero stops he keeps whatever way his last step pointed (sideways, for the ones who arrived last). While exploring,
        /// a stopped hero is asked to face the way the party was sent (priority Interaction, dropped the moment he moves or a fight begins).</summary>
        private void UpdateIdleFacing()
        {
            var exploring = State == GameState.Exploring;
            foreach (var hero in heroes)
            {
                var agent = FindAgent(hero);
                if (agent?.Facing == null || agent.Motor == null) continue;
                var moving = agent.Motor.Snapshot.Velocity.sqrMagnitude > 0.04f;
                var facing = LastOrderFacing;
                if (!exploring || moving || facing.sqrMagnitude < 0.01f) agent.Facing.ClearFacing(FacingPriority.Interaction);
                else agent.Facing.RequestFacing(FacingPriority.Interaction, facing);
            }
        }

        /// <summary>The direction the party was last sent (derived by the toolkit's exploration controller from the travel direction); zero until the first order.</summary>
        public Vector3 LastOrderFacing => exploration != null ? exploration.LastOrderFacing : Vector3.zero;

        public bool CanBeginEncounter(int index) => booted && State == GameState.Exploring && index == Checkpoint.Index && goblinsByEncounter.ContainsKey(index) && !Combat.IsActive;

        /// <summary>Starts the stage walk for encounter <paramref name="index"/>; the fight begins when the party has arrived.</summary>
        public bool BeginEncounter(int index)
        {
            if (!CanBeginEncounter(index)) return false;
            ActiveEncounter = index; StageWarps = 0; stageElapsed = 0f; StageSeconds = 0f;
            Commands.MovesLocked = true;                 // a click now would override the stage order
            Session.Party.SelectAll();
            var stage = anchors.encounters[index].partyStage;
            for (var i = 0; i < heroes.Count; i++) FindAgent(heroes[i])?.Motor?.SetDestination(stage[i].position);
            anchors.encounters[index].trigger.GetComponent<VikingEncounterTrigger>().Armed = false;
            SetState(GameState.Staging);
            ShowBanner("Ambush!", 3f);
            return true;
        }

        private void UpdateStaging(float dt)
        {
            stageElapsed += dt; StageSeconds = stageElapsed;
            var stage = anchors.encounters[ActiveEncounter].partyStage;
            var arrived = true;
            for (var i = 0; i < heroes.Count; i++)
            {
                var agent = FindAgent(heroes[i]);
                if (agent == null) continue;
                var d = agent.transform.position - stage[i].position; d.y = 0f;
                if (d.magnitude > StageArriveDistance) arrived = false;
            }

            if (!arrived && stageElapsed < stageTimeout) return;

            for (var i = 0; i < heroes.Count; i++)   // stragglers (a blocked agent, a one-hero selection that left the others behind) are placed, and counted: a test asserts zero
            {
                var agent = FindAgent(heroes[i]);
                if (agent?.Motor == null) continue;
                var d = agent.transform.position - stage[i].position; d.y = 0f;
                if (d.magnitude > StageArriveDistance) { StageWarps++; agent.Motor.Warp(stage[i].position); }
                agent.Motor.Stop();
                agent.transform.rotation = Quaternion.LookRotation(WorldLayout.Arenas[ActiveEncounter].Forward);
            }

            StartFight();
        }

        private void StartFight()
        {
            var index = ActiveEncounter;
            var goblins = GoblinsOf(index);
            var brains = new List<(RuntimeEntityId, BehaviorSetDefinition)>();
            var names = new Dictionary<RuntimeEntityId, string>(heroNames);
            var counts = new Dictionary<string, int>();
            foreach (var (unitId, id) in goblins)
            {
                var unit = GameDesign.Unit(unitId);
                Session.RulesDatabase.TryResolve<BehaviorSetDefinition>(new DefinitionId(unit.Brain), out var set);
                brains.Add((id, set));
                counts[unit.DisplayName] = counts.TryGetValue(unit.DisplayName, out var n) ? n + 1 : 1;
                names[id] = $"{unit.DisplayName} {counts[unit.DisplayName]}";
            }

            Commands.MovesLocked = false;
            if (!Combat.StartEncounter(heroes, brains, names, outcome => OnFightEnded(index, outcome)))
            {
                Debug.LogError("[VikingGame] the combat session refused to start");
                SetState(GameState.Exploring);
                return;
            }

            SetState(GameState.Fighting);
        }

        private void OnFightEnded(int index, FightOutcome outcome)
        {
            if (outcome == FightOutcome.Won)
            {
                Checkpoint.EncounterWon(index);
                foreach (var hero in heroes) if (Session.Characters.TryGetCharacter(hero, out var state)) PartyVitals.FullHealAndRevive(state, Session.EventBus);
                Session.Party.SelectAll();
                if (index == Checkpoint.EncounterCount - 1)
                {
                    SetState(GameState.Won);
                    ShowOverlay();
                }
                else
                {
                    SetState(GameState.Exploring);
                    ShowBanner(index == 0 ? "Victory! The party is rested. The road continues." : "Victory! The party is rested.", 4f);
                }
            }
            else if (outcome == FightOutcome.Lost)
            {
                SetState(GameState.Defeated);
                defeatTimer = DefeatReloadSeconds;
                ShowBanner("The party has fallen... returning to the road.", DefeatReloadSeconds + 5f);
            }

            ActiveEncounter = outcome == FightOutcome.Ongoing ? ActiveEncounter : -1;
            EncounterEnded?.Invoke(index, outcome);
        }

        private void ShowOverlay() => overlay.style.display = DisplayStyle.Flex;

        private void Reload()
        {
            Checkpoint.WipeReload();
            SceneManager.LoadScene(SceneManager.GetActiveScene().path);
        }

        private void SetState(GameState state)
        {
            State = state;
            StateChanged?.Invoke(state);
        }

        // ------------------------------------------------------------------ helpers
        private Vector3? PartyCentre()
        {
            var sum = Vector3.zero; var n = 0;
            foreach (var hero in heroes) if (FindAgent(hero) is { } agent) { sum += agent.transform.position; n++; }
            return n > 0 ? sum / n : null;
        }

        /// <summary>What the camera follows: the centre of the SELECTED heroes (all four unless the player picked some), not one hero (the party spreads out, and after a fight the "active"
        /// hero is wherever he happened to finish).</summary>
        private Vector3? FollowPoint()
        {
            if (Session == null) return null;
            var sum = Vector3.zero; var n = 0;
            foreach (var hero in heroes)
                if (Session.Party.IsSelected(hero) && FindAgent(hero) is { } agent) { sum += agent.transform.position; n++; }
            return n > 0 ? sum / n : PartyCentre();
        }

        private void PollPartyHp()
        {
            if (Time.unscaledTime < hpPollAt || partyView == null) return;
            hpPollAt = Time.unscaledTime + 0.25f;
            var signature = string.Join(",", heroes.Select(h => Session.Characters.TryGetCharacter(h, out var s) && s.TryGetResource(PartyVitals.Hp, out var r) ? r.Current : -1));
            if (signature == hpSignature) return;
            hpSignature = signature;
            partyView.RefreshNow();   // PartyView refreshes only on party changes; the HP bars need a nudge
        }

        public int GetPriority(RuntimeEntityId id) => FindAgent(id)?.GetComponent<UnityEngine.AI.NavMeshAgent>()?.avoidancePriority ?? -1;

        public static CharacterAgent FindAgent(RuntimeEntityId id)
        {
            foreach (var agent in CharacterAgent.AllLive) if (agent != null && agent.EntityId == id) return agent;
            return null;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (ModelActionCues.LandingDelayRule == (Func<RuntimeEntityId, RuntimeEntityId, float>)CombatFx.HitDelayBetween) ModelActionCues.LandingDelayRule = null;
            Combat?.Abort();
            exploration?.Dispose();
            Commands?.Dispose();
            host?.EndSession();
        }
    }
}

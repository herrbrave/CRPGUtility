using System;
using System.Collections.Generic;
using System.Linq;
using CRPG.AI;
using CRPG.Abilities;
using CRPG.Actors;
using CRPG.Combat;
using CRPG.Core;
using CRPG.Gameplay;
using CRPG.Gameplay.Actions;
using CRPG.Gameplay.Combat;
using CRPG.Navigation;
using CRPG.Rules;
using CRPG.Rules.SRD5;
using CRPG.UI;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UIElements;

namespace VikingRaid.Game
{
    /// <summary>
    /// One live tactical encounter at a time: wires the toolkit's combat stack for this game (adapted from BarrowsEnd's TacticalCombatSession, with every difference the Task 3/4
    /// reviews found). The Arena harness proved the rules wiring in simulation; this adds real time, real agents and the UI.
    /// <para>Tick order (finding 16, polled, never inside a bus handler): controller.Tick, revive sweep, downed-hero turn skip, auto end-turn, move watchdog, AI driver, outcome poll.</para>
    /// <para>Differences from the 2D precedent: real SRD5 policies, EncounterFactionProvider, eye-height line of sight on obstacle colliders only, all four turn resources, the animation
    /// hook, brains behind the zero-score veto, "down" = HP &lt;= 0 (PartyVitals), a watchdog that cancels a move that can never arrive, our own move handler (samples the click onto the
    /// NavMesh, keeps clear of other combatants, no potential reactors, so no stop-start threat-circle stutter), the ability bar refreshed after every ability and hidden on goblin turns,
    /// the targeting gate disarmed every turn, and an outcome that settles (scheduler idle + a second) so the death cue plays before the fight ends.</para>
    /// </summary>
    public sealed class VikingCombatSession
    {
        public static readonly DefinitionId HeroFaction = new(GameDesign.HeroesFaction);
        public static readonly DefinitionId GoblinFaction = new(GameDesign.GoblinsFaction);
        private const float OutcomeSettleSeconds = 1.0f, EyeHeight = 1.4f;
        private const int IdlePriority = 30, MovingPriority = 60;   // avoidance: the lower number wins; a downed hero is 0

        private readonly PartyCommandFilter commandSource;
        private readonly Action<IPointerTargetingGate> setTargetingGate;
        private readonly AbilityBarView abilityBarView;
        private readonly TurnOrderView turnOrderView;
        private readonly VisualElement abilityRegion, turnOrderRegion;
        private readonly Label feedbackLabel;
        private readonly DeathSaveService deathSaves;
        private readonly Action<string> notify;
        private readonly ICombatLog combatLog;

        private GameSession session;
        private WorldSpawnService spawner;
        private TacticalEncounterController controller;
        private TacticalAiTurnDriver aiDriver;
        private AbilityTargetingGate targetingGate;
        private TacticalCombatUIBridge uiBridge;
        private AbilityResolutionService abilityResolution;
        private Action<GameEventContext> onAbilityResolved, onTurnStarted;
        private Dictionary<RuntimeEntityId, string> names;
        private List<RuntimeEntityId> hostiles;
        private Action<FightOutcome> onEnded;
        private ResourceDefinition movementDefinition;
        private TacticalPlayerMoveAdapter moveAdapter;

        private bool checkAutoEnd;
        private FightOutcome pendingOutcome = FightOutcome.Ongoing;
        private float outcomeSettle;
        private MoveActionExecution watchedMove;
        private MoveWatchdog watchdog;
        private RuntimeEntityId watchedMover;

        public VikingCombatSession(PartyCommandFilter commandSource, Action<IPointerTargetingGate> setTargetingGate, AbilityBarView abilityBarView, TurnOrderView turnOrderView,
            VisualElement abilityRegion, VisualElement turnOrderRegion, Label feedbackLabel, DeathSaveService deathSaves, Action<string> notify, ICombatLog combatLog = null)
        {
            this.combatLog = combatLog;
            this.commandSource = commandSource; this.setTargetingGate = setTargetingGate; this.abilityBarView = abilityBarView; this.turnOrderView = turnOrderView;
            this.abilityRegion = abilityRegion; this.turnOrderRegion = turnOrderRegion; this.feedbackLabel = feedbackLabel; this.deathSaves = deathSaves; this.notify = notify;
        }

        public bool IsActive => controller != null;
        public TacticalEncounterController Controller => controller;
        public AbilityTargetingGate TargetingGate => targetingGate;
        public int WatchdogCancels { get; private set; }
        public RuntimeEntityId? CurrentTurnId => controller?.EncounterService.Encounter?.CurrentTurn?.Id;

        public void Bind(GameSession session, WorldSpawnService spawner) { this.session = session; this.spawner = spawner; }

        // ------------------------------------------------------------------ start
        /// <param name="heroes">The party, in order.</param>
        /// <param name="goblins">This encounter's goblins with the behaviour set that drives each.</param>
        public bool StartEncounter(IReadOnlyList<RuntimeEntityId> heroes, IReadOnlyList<(RuntimeEntityId Id, BehaviorSetDefinition Brain)> goblins,
            IReadOnlyDictionary<RuntimeEntityId, string> displayNames, Action<FightOutcome> onEnded)
        {
            if (IsActive || session == null) return false;
            this.onEnded = onEnded;
            hostiles = goblins.Select(g => g.Id).ToList();

            var db = session.RulesDatabase;
            ResourceDefinition Resolve(string id)
            {
                if (!db.TryResolve<ResourceDefinition>(new DefinitionId(id), out var r)) throw new InvalidOperationException($"resource '{id}' is missing from the ruleset");
                return r;
            }

            movementDefinition = Resolve(GameDesign.Movement);
            var turnResources = new TurnResourceSet(new[] { Resolve(GameDesign.Action), Resolve(GameDesign.BonusAction), Resolve(GameDesign.Reaction), movementDefinition });

            var positions = new LiveCharacterPositionProvider();
            var encounterService = new CombatEncounterService(session.Characters, session.Policies.Get<IInitiativeRule>(), turnResources, session.EventBus);
            var factionProvider = new EncounterFactionProvider(encounterService);
            abilityResolution = new AbilityResolutionService(session.Characters, session.DiceService, combatLog: combatLog, eventBus: session.EventBus, positions: positions,
                attackBonusRule: session.Policies.Get<IAttackBonusRule>(), armorClassRule: session.Policies.Get<IArmorClassRule>(),
                savingThrowRule: session.Policies.Get<ISavingThrowRule>(), damageResistanceRule: session.Policies.Get<IDamageResistanceRule>(),
                rulesDatabase: db, factionProvider: factionProvider, lineOfSightProvider: new EyeHeightLineOfSight(new PhysicsLineOfSightProvider(WorldLayers.SightBlocker), EyeHeight));
            var movementResolution = new MovementResolutionService(session.Characters, positions, new NoMovementController(), rulesDatabase: db, threatRange: 1.5f);

            var combatants = new List<(RuntimeEntityId Id, DefinitionId Faction)>();
            foreach (var h in heroes) combatants.Add((h, HeroFaction));
            foreach (var g in goblins) combatants.Add((g.Id, GoblinFaction));

            foreach (var (id, _) in combatants) SetAvoidance(id, IdlePriority);

            controller = new TacticalEncounterController(session.Characters, session.EventBus, positions, abilityResolution, movementResolution, new NavMeshService(),
                encounterService, spawner, combatants, new CharacterAnimationPresentationHook());

            var brainByGoblin = goblins.ToDictionary(g => g.Id, g => g.Brain);
            var random = new SeededRandomSource(session.GetHashCode());
            // scoreFloor 0: a zero score means a consideration vetoed it ("the target is already downed"), so a goblin that has spent its movement does not hit the downed hero beside it
            var brain = new PerCombatantDecisionProvider(id => brainByGoblin.TryGetValue(id, out var set) ? set : null,
                set => new UtilityAIDecisionProvider(set, db, session.Characters, abilityResolution, positions, random, scoreFloor: 0f));
            aiDriver = new TacticalAiTurnDriver(controller, session.Characters, brain, GoblinFaction);

            targetingGate = new AbilityTargetingGate(controller.Scheduler, abilityResolution, WorldLayers.Hittable);
            setTargetingGate(targetingGate);
            targetingGate.TargetRejected += OnTargetRejected;
            // the toolkit's adapter with this game's rules: a downed hero is not walked, the click is snapped onto the NavMesh and kept clear of other combatants so the move can arrive,
            // and no potential reactors are passed (no opportunity attacks against player movement, so no stop-start at threat circles)
            moveAdapter = new TacticalPlayerMoveAdapter(controller, movementDefinition.Id, HeroFaction,
                canMove: id => session.Characters.TryGetCharacter(id, out var mover) && !PartyVitals.IsDown(mover),
                adjustDestination: NavMeshDestinationAdjuster.Create(() => controller.EncounterService.Encounter.Combatants.Select(c => c.Id)),
                provokesReactions: false);
            moveAdapter.MoveRefused += Feedback;
            commandSource.CombatMoveOverride = HandleMove;

            names = new Dictionary<RuntimeEntityId, string>();
            foreach (var kv in displayNames) names[kv.Key] = kv.Value;

            abilityBarView.Initialize(new AbilityBarPresenter(session.Characters, db, abilityResolution), abilityRegion);
            turnOrderView.Initialize(new TurnOrderPresenter(controller.EncounterService, names), turnOrderRegion, session.EventBus);
            if (feedbackLabel.parent != turnOrderRegion) turnOrderRegion.Add(feedbackLabel);
            Feedback("");
            SetUiShown(true);

            uiBridge = new TacticalCombatUIBridge(controller, targetingGate, abilityBarView, session.EventBus, HeroFaction, id => RollDeathSave(id));

            onAbilityResolved = OnAbilityResolved; session.EventBus.Subscribe(GameEvent.AbilityResolved, onAbilityResolved);
            onTurnStarted = OnTurnStarted; session.EventBus.Subscribe(GameEvent.TurnStarted, onTurnStarted);
            pendingOutcome = FightOutcome.Ongoing; outcomeSettle = 0f; checkAutoEnd = false; watchedMove = null; WatchdogCancels = 0;
            return true;
        }

        // ------------------------------------------------------------------ tick
        public void Tick(float deltaTime)
        {
            if (!IsActive) return;

            controller.Tick(deltaTime);
            if (!IsActive) return;

            UpdateDownedAvoidance();
            SkipDownedHeroTurn();
            AutoEndTurn();
            UpdateAbilityBarVisibility();
            Watchdog(deltaTime);
            aiDriver.Tick(deltaTime);
            PollOutcome(deltaTime);
        }

        private IEnumerable<CharacterState> HeroStates()
        {
            foreach (var combatant in controller.EncounterService.Encounter?.Combatants ?? Enumerable.Empty<Combatant>())
                if (combatant.Faction == HeroFaction && session.Characters.TryGetCharacter(combatant.Id, out var state)) yield return state;
        }

        private void UpdateDownedAvoidance()
        {
            foreach (var hero in HeroStates())
            {
                // a body on the ground is not shoved by a goblin walking past: priority 0 is the strongest in NavMesh avoidance (everyone else yields to it)
                var down = PartyVitals.IsDown(hero);
                if (FindAgent(hero.Id)?.GetComponent<NavMeshAgent>() is { } navAgent && (navAgent.avoidancePriority == 0) != down && !(watchedMove != null && watchedMover == hero.Id))
                    navAgent.avoidancePriority = down ? 0 : IdlePriority;
            }
        }

        // A downed hero takes no decision: on its turn the death save rolls itself (if still dying) and the turn passes. A hero STABILISED at 0 HP has nothing to roll but is still down.
        private void SkipDownedHeroTurn()
        {
            if (controller.Scheduler.IsBusy) return;
            var current = controller.EncounterService.Encounter?.CurrentTurn;
            if (current == null || current.Faction != HeroFaction) return;
            if (!session.Characters.TryGetCharacter(current.Id, out var state) || !PartyVitals.IsDown(state)) return;

            if (state.DeathSaves != null) RollDeathSave(current.Id);
            if (controller.EncounterService.Encounter?.CurrentTurn == current) controller.EncounterService.EndTurn();
        }

        private void RollDeathSave(RuntimeEntityId id)
        {
            if (session.Characters.TryGetCharacter(id, out var state) && state.DeathSaves != null) deathSaves.RollDeathSave(id);
        }

        // The Action is spent and nothing else is worth doing (see TurnEndRule): the turn ends by itself. Otherwise the player ends it with End Turn.
        private void AutoEndTurn()
        {
            if (!checkAutoEnd || controller.Scheduler.IsBusy) return;
            checkAutoEnd = false;
            var current = controller.EncounterService.Encounter?.CurrentTurn;
            if (current == null || current.Faction != HeroFaction || !session.Characters.TryGetCharacter(current.Id, out var state)) return;

            if (TurnEndRule.ShouldAutoEnd(state, HeroStates(), id => positionsOf(id))) controller.EncounterService.EndTurn();
            else abilityBarView.Refresh();   // the buttons for spent resources must grey out (the bar only refreshes itself on turn events)
        }

        private Vector3 positionsOf(RuntimeEntityId id) => FindAgent(id) is { } agent ? agent.transform.position : Vector3.zero;

        private void UpdateAbilityBarVisibility()
        {
            var current = controller.EncounterService.Encounter?.CurrentTurn;
            var heroTurn = current != null && current.Faction == HeroFaction && session.Characters.TryGetCharacter(current.Id, out var state) && !PartyVitals.IsDown(state);
            abilityRegion.style.display = heroTurn ? DisplayStyle.Flex : DisplayStyle.None;   // End Turn must never be offered on a goblin's turn
        }

        // A move that can never arrive (two agents ordered onto one spot, a click beside a goblin) would block the AI driver, End Turn and the gate forever: cancel it.
        private void Watchdog(float deltaTime)
        {
            if (controller.Scheduler.CurrentAction is not MoveActionExecution move) { EndWatch(); return; }

            if (!ReferenceEquals(move, watchedMove))
            {
                EndWatch();
                watchedMove = move; watchedMover = move.Source;
                var agent = FindAgent(move.Source);
                var navAgent = agent != null ? agent.GetComponent<NavMeshAgent>() : null;
                var budget = session.Characters.TryGetCharacter(move.Source, out var state) && state.TryGetResource(movementDefinition.Id, out var m) ? m.Current : 8;
                watchdog = new MoveWatchdog(budget, navAgent != null ? navAgent.speed : 3f);
                SetAvoidance(move.Source, MovingPriority);   // NavMesh avoidance: the LOWER number wins, so idle combatants (30) are not shoved aside by the one that is walking (60)
            }

            var snapshot = FindAgent(watchedMover)?.Motor?.Snapshot;
            if (watchdog.ShouldCancel(deltaTime, snapshot?.Velocity.magnitude ?? 0f, snapshot?.RemainingDistance ?? float.PositiveInfinity))
            {
                WatchdogCancels++;
                var hero = session.Party.Members.Contains(watchedMover);
                Debug.LogWarning($"[VikingCombatSession] cancelled a move that could not arrive (a {(hero ? "hero" : "goblin")}, after {watchdog.Elapsed:F1}s, {snapshot?.RemainingDistance:F1} m still to go)");
                controller.Scheduler.Cancel();
                EndWatch();
            }
        }

        private void EndWatch()
        {
            if (watchedMove != null) SetAvoidance(watchedMover, IdlePriority);
            watchedMove = null; watchdog = null;
        }

        private static void SetAvoidance(RuntimeEntityId id, int priority)
        {
            var navAgent = FindAgent(id)?.GetComponent<NavMeshAgent>();
            if (navAgent != null) navAgent.avoidancePriority = priority;
        }

        // ------------------------------------------------------------------ outcome
        private void PollOutcome(float deltaTime)
        {
            if (!IsActive) return;
            if (pendingOutcome == FightOutcome.Ongoing)
            {
                var outcome = PartyVitals.Evaluate(HeroStates().ToList(), hostiles.Select(StateOf).Where(s => s != null).ToList());
                // a goblin the registry no longer knows is dead; an empty list means Won
                if (outcome == FightOutcome.Ongoing && hostiles.All(h => StateOf(h) == null)) outcome = FightOutcome.Won;
                if (outcome == FightOutcome.Ongoing) return;
                pendingOutcome = outcome; outcomeSettle = 0f;
            }

            // the killing blow lands before the death cue: let the scheduler go idle and a second pass, then end
            outcomeSettle += deltaTime;
            if (controller.Scheduler.IsBusy || outcomeSettle < OutcomeSettleSeconds) return;
            var result = pendingOutcome;
            controller.EncounterService.EndCombat();
            Finish(result);
        }

        private CharacterState StateOf(RuntimeEntityId id) => session.Characters.TryGetCharacter(id, out var s) ? s : null;

        private void Finish(FightOutcome outcome)
        {
            if (session != null)
            {
                session.EventBus.Unsubscribe(GameEvent.AbilityResolved, onAbilityResolved);
                session.EventBus.Unsubscribe(GameEvent.TurnStarted, onTurnStarted);
            }

            if (targetingGate != null) targetingGate.TargetRejected -= OnTargetRejected;
            if (moveAdapter != null) moveAdapter.MoveRefused -= Feedback;
            moveAdapter = null;
            EndWatch();
            setTargetingGate(null);
            commandSource.CombatMoveOverride = null;
            abilityBarView.ClearCurrentCharacter();
            SetUiShown(false);
            uiBridge?.Dispose();
            controller?.Dispose();
            uiBridge = null; controller = null; aiDriver = null; targetingGate = null;

            var callback = onEnded; onEnded = null;
            callback?.Invoke(outcome);
        }

        /// <summary>Ends the encounter without an outcome (a scene reload or session teardown).</summary>
        public void Abort()
        {
            if (!IsActive) return;
            onEnded = null;
            controller.EncounterService.EndCombat();
            Finish(FightOutcome.Ongoing);
        }

        // ------------------------------------------------------------------ events and input
        private void OnTurnStarted(GameEventContext context)
        {
            targetingGate?.Disarm();   // the bridge never does: a gate armed for the previous hero would stay armed
            checkAutoEnd = false;
            Feedback("");
        }

        private void OnAbilityResolved(GameEventContext context)
        {
            var current = controller?.EncounterService.Encounter?.CurrentTurn;
            if (current == null || current.Id != context.Source || current.Faction != HeroFaction) return;
            checkAutoEnd = true;
            abilityBarView.Refresh();
        }

        private void OnTargetRejected(AbilityDefinition ability, RuntimeEntityId target, AbilityValidationResult result)
        {
            var message = string.IsNullOrEmpty(result.Message) ? $"{ability.DisplayName}: {result.Reason}" : $"{ability.DisplayName}: {result.Message}";
            if (names != null && names.TryGetValue(target, out var targetName)) message = message.Replace(target.ToString(), targetName);
            Feedback(message);
            notify?.Invoke(message);
        }

        /// <summary>A ground click during a fight: the toolkit's move adapter configured in <c>StartEncounter</c> (null outside an encounter).</summary>
        public void HandleMove(MoveCommand command) => TryHandleMove(command);

        public MoveActionExecution TryHandleMove(MoveCommand command) => moveAdapter?.TryHandleMove(command);

        // ------------------------------------------------------------------ ui
        private void SetUiShown(bool shown)
        {
            var display = shown ? DisplayStyle.Flex : DisplayStyle.None;
            abilityRegion.style.display = display;
            turnOrderRegion.style.display = display;
        }

        private void Feedback(string message)
        {
            feedbackLabel.text = message;
            feedbackLabel.style.display = string.IsNullOrEmpty(message) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private static CharacterAgent FindAgent(RuntimeEntityId id)
        {
            foreach (var agent in CharacterAgent.AllLive) if (agent != null && agent.EntityId == id) return agent;
            return null;
        }

        // ------------------------------------------------------------------ small adapters
        private sealed class LiveCharacterPositionProvider : IPositionProvider
        {
            public Vector3 GetPosition(RuntimeEntityId entity) => FindAgent(entity) is { } agent ? agent.transform.position : Vector3.zero;
        }

        /// <summary>The scheduler drives movement frame by frame through the NavMesh motors; the legacy MoveToward path of the movement service is never used.</summary>
        private sealed class NoMovementController : IMovementController
        {
            public float TryMoveTowards(RuntimeEntityId entity, Vector3 destination, float maxDistance) => 0f;
        }

        /// <summary>Line of sight between two characters is judged at eye height (their feet are on the ground, which never blocks anyway; this just keeps low rocks from blocking shots).</summary>
        private sealed class EyeHeightLineOfSight : ILineOfSightProvider
        {
            private readonly ILineOfSightProvider inner; private readonly float height;
            public EyeHeightLineOfSight(ILineOfSightProvider inner, float height) { this.inner = inner; this.height = height; }
            public bool HasLineOfSight(Vector3 from, Vector3 to) => inner.HasLineOfSight(from + Vector3.up * height, to + Vector3.up * height);
        }
    }
}

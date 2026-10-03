using System;
using System.Collections.Generic;
using CRPG.AI;
using CRPG.Abilities;
using CRPG.Actors;
using CRPG.Combat;
using CRPG.Core;
using CRPG.Gameplay;
using CRPG.Gameplay.Combat;
using CRPG.InputSystem;
using CRPG.UI;
using UnityEngine;

namespace BarrowsEnd.Gate
{
    /// <summary>Task 6.1 (T6.1-D6): BarrowsEnd's first real turn-based tactical encounter -- wires the toolkit's
    /// full combat stack (<see cref="CombatEncounterService"/>/<see cref="EncounterFactionProvider"/>/
    /// <see cref="AbilityResolutionService"/>/<see cref="TacticalEncounterController"/>/
    /// <see cref="TacticalAiTurnDriver"/>/<see cref="AbilityTargetingGate"/>/<see cref="TacticalCombatUIBridge"/>/
    /// <see cref="AbilityBarView"/>), following the exact construction order proven in
    /// <c>TestProject/Assets/TacticalSlice/Scripts/TacticalSliceBootstrap.cs</c> and the shipped
    /// `crpg-toolkit/Samples~/TacticalEncounter` sample -- the only two places in the whole codebase this stack
    /// has ever been wired together before now, both against 3D NavMesh movement, never BarrowsEnd's 2D grid.
    /// <para>Turn economy: each combatant has a per-turn Movement budget (<see cref="MovementPerTurn"/> world
    /// units, spent by <see cref="TacticalPlayerMoveAdapter"/> for the player and by the hostile brain's Close
    /// Distance behavior) and acts once -- a resolved ability ends the turn automatically. The Movement resource
    /// is created at runtime (no authored asset) and granted to every combatant when the encounter starts.
    /// <see cref="GateCommandSource.CombatMoveOverride"/> redirects ground clicks to the move adapter so they
    /// can never also drive exploration movement.</para></summary>
    public sealed class TacticalCombatSession
    {
        public static readonly DefinitionId PartyFaction = new("faction-party");
        public const string MovementResourceId = "resource-movement";
        public const string ActionResourceId = "resource-action";
        public const string BonusActionResourceId = "resource-bonus-action";
        public const int MovementPerTurn = 6;

        private readonly GateCommandSource commandSource;
        private readonly InputSystemCommandSource inputSource;
        private readonly HudRoot hudRoot;
        private readonly AbilityBarView abilityBarView;
        private readonly LayerMask hittableMask;
        private readonly TurnOrderView turnOrderView;
        private readonly Func<RuntimeEntityId, string> displayNameFor;
        private readonly Action<string> notify;
        private readonly Action<RuntimeEntityId> rollDeathSave;
        private readonly Action<RuntimeEntityId> stabilize;

        private GameSession session;
        private WorldSpawnService spawner;

        private TacticalEncounterController controller;
        private TacticalAiTurnDriver aiDriver;
        private AbilityTargetingGate targetingGate;
        private TacticalCombatUIBridge uiBridge;
        private VisualElementRegionHolder abilityBarRegionHolder;
        private VisualElementRegionHolder turnOrderRegionHolder;
        private UnityEngine.UIElements.Label feedbackLabel;
        private Action<GameEventContext> onCombatEnded;
        private Action<GameEventContext> onAbilityResolved;
        private Dictionary<RuntimeEntityId, string> combatantNames;
        private TacticalPlayerMoveAdapter moveAdapter;
        private bool pendingEndTurn;
        private Action<bool> onEncounterEnded;
        private bool lastEncounterPartyWon;

        public TacticalCombatSession(GateCommandSource commandSource, InputSystemCommandSource inputSource,
            HudRoot hudRoot, AbilityBarView abilityBarView, LayerMask hittableMask,
            TurnOrderView turnOrderView, Func<RuntimeEntityId, string> displayNameFor, Action<string> notify,
            Action<RuntimeEntityId> rollDeathSave, Action<RuntimeEntityId> stabilize)
        {
            this.rollDeathSave = rollDeathSave ?? throw new ArgumentNullException(nameof(rollDeathSave));
            this.stabilize = stabilize ?? throw new ArgumentNullException(nameof(stabilize));
            this.turnOrderView = turnOrderView ?? throw new ArgumentNullException(nameof(turnOrderView));
            this.displayNameFor = displayNameFor ?? throw new ArgumentNullException(nameof(displayNameFor));
            this.notify = notify;
            this.commandSource = commandSource ?? throw new ArgumentNullException(nameof(commandSource));
            this.inputSource = inputSource ?? throw new ArgumentNullException(nameof(inputSource));
            this.hudRoot = hudRoot ?? throw new ArgumentNullException(nameof(hudRoot));
            this.abilityBarView = abilityBarView ?? throw new ArgumentNullException(nameof(abilityBarView));
            this.hittableMask = hittableMask;
        }

        public bool IsActive => controller != null;

        /// <summary>True when a new encounter could start right now (none active, session bound) -- lets a trigger
        /// check BEFORE it spawns hostiles.</summary>
        public bool CanStart => !IsActive && session != null && (StartBlocker == null || !StartBlocker());

        /// <summary>Returns true while something outside combat owns the party (a scene transition or load in flight);
        /// an encounter must wait rather than start into a half-moved world.</summary>
        public Func<bool> StartBlocker { get; set; }

        /// <summary>Raised once an encounter has actually started, so the shell can halt anything the party was in the
        /// middle of (a walk, an interaction with a door or arch) -- combat owns movement from here on.</summary>
        public event Action EncounterStarted;

        /// <summary>Whose turn it is right now, or null when no encounter is active.</summary>
        public RuntimeEntityId? CurrentTurnId => controller?.EncounterService.Encounter?.CurrentTurn?.Id;

        /// <summary>Called alongside every <see cref="GateSessionServices.Rebind"/> (boot and after a load) so
        /// this session's own session-scoped references never go stale -- mirrors every other consumer's own
        /// rebind convention in this project.</summary>
        public void RebindSession(GameSession session, WorldSpawnService spawner)
        {
            // A session swap mid-combat can't happen in real play (no save during combat) -- never leave a
            // stale encounter wired regardless. The completion callback is cleared first: it belongs to whoever
            // started the now-defunct encounter, not this unrelated session swap.
            if (IsActive) { onEncounterEnded = null; EndEncounter(); }
            this.session = session;
            this.spawner = spawner;
        }

        /// <summary>Starts a real, live encounter. A no-op if one is already active (a trigger firing twice
        /// before its own one-shot guard lands should never double-start combat). <paramref name="onEncounterEnded"/>
        /// fires exactly once, right as combat ends, with whether the party won -- computed and captured
        /// BEFORE <see cref="CombatEncounterService.EndCombat"/> runs, since the encounter's own Combatants list
        /// is already cleared by the time CombatEnded actually fires (confirmed against the shipped sample's own
        /// win/loss-detection comment).</summary>
        /// <returns>True if the encounter actually started; false (nothing changed) if one is already active or
        /// the session/ruleset is not ready -- a caller must not treat its encounter as fired on false.</returns>
        public bool StartEncounter(IReadOnlyList<(RuntimeEntityId Id, DefinitionId Faction)> combatants, DefinitionId hostileFaction,
            BehaviorSetDefinition hostileBrain, Action<bool> onEncounterEnded = null) =>
            StartEncounter(combatants, hostileFaction, _ => hostileBrain, onEncounterEnded);

        /// <param name="brainFor">The behavior set driving each hostile combatant (so an encounter can mix enemy
        /// types). <paramref name="hostileNameFor"/> supplies turn-order display names; null falls back to "Hostile N".</param>
        public bool StartEncounter(IReadOnlyList<(RuntimeEntityId Id, DefinitionId Faction)> combatants, DefinitionId hostileFaction,
            Func<RuntimeEntityId, BehaviorSetDefinition> brainFor, Action<bool> onEncounterEnded = null,
            Func<RuntimeEntityId, string> hostileNameFor = null)
        {
            if (!CanStart) return false;
            this.onEncounterEnded = onEncounterEnded;

            var positions = new LiveCharacterPositionProvider();
            if (!session.RulesDatabase.TryResolve<ResourceDefinition>(new DefinitionId(MovementResourceId), out var movementDefinition))
            {
                Debug.LogError("[TacticalCombatSession] resource-movement is missing -- run Barrows End/Content/Author Gear And Ruleset.");
                return false;
            }

            foreach (var (id, _) in combatants) GrantMovement(id);
            var turnResources = new List<ResourceDefinition> { movementDefinition };
            foreach (var turnResourceId in new[] { ActionResourceId, BonusActionResourceId })
            {
                if (!session.RulesDatabase.TryResolve<ResourceDefinition>(new DefinitionId(turnResourceId), out var turnResource))
                {
                    Debug.LogError($"[TacticalCombatSession] {turnResourceId} is missing from the ruleset.");
                    return false;
                }
                turnResources.Add(turnResource);
                foreach (var (id, _) in combatants) GrantTurnResource(id, turnResource.Id, 1);
            }
            var encounterService = new CombatEncounterService(session.Characters, null, new TurnResourceSet(turnResources), session.EventBus);
            var factionProvider = new EncounterFactionProvider(encounterService);
            var abilityResolution = new AbilityResolutionService(session.Characters, session.DiceService,
                eventBus: session.EventBus, positions: positions, rulesDatabase: session.RulesDatabase, factionProvider: factionProvider);
            var movementResolution = new MovementResolutionService(session.Characters, positions, new TacticalMovementController(), rulesDatabase: session.RulesDatabase);

            controller = new TacticalEncounterController(session.Characters, session.EventBus, positions, abilityResolution,
                movementResolution, spawner.GridNavigation, encounterService, spawner, combatants, useExplicitTurnOrder: true);

            var random = new SeededRandomSource(session.GetHashCode());
            var brain = new PerCombatantDecisionProvider(brainFor,
                set => new UtilityAIDecisionProvider(set, session.RulesDatabase, session.Characters, abilityResolution, positions, random));
            aiDriver = new TacticalAiTurnDriver(controller, session.Characters, brain, hostileFaction);

            targetingGate = new AbilityTargetingGate(controller.Scheduler, abilityResolution, hittableMask);
            inputSource.TargetingGate = targetingGate;
            moveAdapter = new TacticalPlayerMoveAdapter(controller, movementDefinition.Id, PartyFaction);
            commandSource.CombatMoveOverride = moveAdapter.HandleMove;
            targetingGate.TargetRejected += OnTargetRejected;

            pendingEndTurn = false;
            onAbilityResolved = OnAbilityResolved;
            session.EventBus.Subscribe(GameEvent.AbilityResolved, onAbilityResolved);

            // 260f matches HudBootstrap's own RosterWidth/MenuWidth (not the shipped sample's own 420f, which
            // was sized for that sample's own larger reference resolution) -- keeps this region's on-screen
            // footprint consistent with BarrowsEnd's two existing HUD corners rather than introducing a new,
            // much larger one.
            abilityBarRegionHolder ??= new VisualElementRegionHolder(hudRoot.CreateRegion(HudCorner.BottomRight, 260f));
            var abilityBarPresenter = new AbilityBarPresenter(session.Characters, session.RulesDatabase, abilityResolution);
            abilityBarView.Initialize(abilityBarPresenter, abilityBarRegionHolder.Region);

            var names = new Dictionary<RuntimeEntityId, string>();
            var hostileCount = 0;
            foreach (var (id, faction) in combatants)
            {
                if (faction == PartyFaction) names[id] = displayNameFor(id) ?? "Party member";
                else names[id] = hostileNameFor?.Invoke(id) ?? $"Hostile {++hostileCount}";
            }
            combatantNames = names;
            var turnOrderPresenter = new TurnOrderPresenter(controller.EncounterService, names);
            turnOrderRegionHolder ??= new VisualElementRegionHolder(hudRoot.CreateRegion(HudCorner.BottomLeft, 260f));
            turnOrderView.Initialize(turnOrderPresenter, turnOrderRegionHolder.Region, session.EventBus);
            if (feedbackLabel == null)
            {
                feedbackLabel = new UnityEngine.UIElements.Label { pickingMode = UnityEngine.UIElements.PickingMode.Ignore };
                feedbackLabel.style.whiteSpace = UnityEngine.UIElements.WhiteSpace.Normal;
                feedbackLabel.style.color = new Color(1f, 0.9f, 0.4f, 1f);
                feedbackLabel.style.backgroundColor = new Color(0f, 0f, 0f, 0.55f);
                feedbackLabel.style.paddingLeft = feedbackLabel.style.paddingRight = 8;
                feedbackLabel.style.paddingTop = feedbackLabel.style.paddingBottom = 4;
            }
            turnOrderRegionHolder.Region.Add(feedbackLabel);
            SetCombatUiShown(true);
            ShowFeedback("");

            uiBridge = new TacticalCombatUIBridge(controller, targetingGate, abilityBarView, session.EventBus, PartyFaction);

            onCombatEnded = _ => EndEncounter();
            session.EventBus.Subscribe(GameEvent.CombatEnded, onCombatEnded);
            EncounterStarted?.Invoke();
            return true;
        }

        /// <summary>Called every frame from <c>ShellBootstrap.Update</c> -- a no-op when no encounter is active,
        /// so this costs nothing outside of combat. Manual win-condition polling: neither
        /// <see cref="CombatEncounterService"/> nor <see cref="TacticalEncounterController"/> auto-detects "one
        /// side wiped" (confirmed by reading both classes) -- every real example in the toolkit polls this
        /// itself, every frame, and calls <see cref="CombatEncounterService.EndCombat"/>.</summary>
        public void Tick(float deltaTime)
        {
            if (!IsActive) return;

            controller.Tick(deltaTime);
            RollDeathSaveForDownedPartyTurn();
            if (pendingEndTurn && !controller.Scheduler.IsBusy)
            {
                pendingEndTurn = false;
                var current = controller.EncounterService.Encounter?.CurrentTurn;
                if (current != null && !HasActionLeft(current.Id)) controller.EncounterService.EndTurn();
            }

            aiDriver.Tick(deltaTime);
            CheckWinCondition();
        }

        private bool HasActionLeft(RuntimeEntityId id) =>
            session.Characters.TryGetCharacter(id, out var character)
            && character.TryGetResource(new DefinitionId(ActionResourceId), out var action) && action.Current > 0;

        // A downed (dying) party member takes no decision: on their turn the death save rolls itself and the turn
        // passes, exactly as TacticalAiTurnDriver does for a dying hostile-faction combatant.
        private void RollDeathSaveForDownedPartyTurn()
        {
            if (controller.Scheduler.IsBusy) return;
            var current = controller.EncounterService.Encounter?.CurrentTurn;
            if (current == null || current.Faction != PartyFaction) return;
            if (!IsDowned(current.Id)) return;

            rollDeathSave(current.Id);
            if (controller.EncounterService.Encounter?.CurrentTurn == current) controller.EncounterService.EndTurn();
        }

        private bool IsDowned(RuntimeEntityId id) =>
            session.Characters.TryGetCharacter(id, out var character) && character.DeathSaves != null;

        // One prominent line under the turn order: what just happened (or why a click did nothing). The shell's own
        // message label is a small muted line in the menu corner that is easy to miss mid-fight.
        private void ShowFeedback(string message)
        {
            if (feedbackLabel == null) return;
            feedbackLabel.text = message;
            feedbackLabel.style.display = string.IsNullOrEmpty(message) ? UnityEngine.UIElements.DisplayStyle.None : UnityEngine.UIElements.DisplayStyle.Flex;
        }

        private void OnAbilityResolved(GameEventContext context)
        {
            var current = controller?.EncounterService.Encounter?.CurrentTurn;
            if (current != null && current.Id == context.Source) pendingEndTurn = true;
        }

        private void OnTargetRejected(AbilityDefinition ability, RuntimeEntityId target, AbilityValidationResult result)
        {
            var message = string.IsNullOrEmpty(result.Message) ? $"{ability.DisplayName}: {result.Reason}" : $"{ability.DisplayName}: {result.Message}";
            // The toolkit's message names the target by raw id; show who it actually is.
            if (combatantNames != null && combatantNames.TryGetValue(target, out var targetName)) message = message.Replace(target.ToString(), targetName);
            ShowFeedback(message);
            notify?.Invoke(message);
        }

        private void GrantMovement(RuntimeEntityId id)
        {
            if (!session.Characters.TryGetCharacter(id, out var character)) return;
            var movementId = new DefinitionId(MovementResourceId);
            if (!character.TryGetResource(movementId, out _)) character.AddResource(new ResourceState(movementId, MovementPerTurn));
        }

        private void GrantTurnResource(RuntimeEntityId id, DefinitionId resourceId, int max)
        {
            if (!session.Characters.TryGetCharacter(id, out var character)) return;
            if (!character.TryGetResource(resourceId, out _)) character.AddResource(new ResourceState(resourceId, max));
        }

        private void CheckWinCondition()
        {
            var encounter = controller.EncounterService.Encounter;
            if (encounter == null) return; // already ending/ended this frame

            var partyAlive = false;
            var hostileAlive = false;
            foreach (var combatant in encounter.Combatants)
            {
                if (combatant.Faction == PartyFaction) partyAlive |= !IsDowned(combatant.Id); // a downed member is out of the fight, not a survivor
                else hostileAlive = true;
            }

            if (partyAlive && hostileAlive) return;
            lastEncounterPartyWon = partyAlive;
            if (partyAlive)
            {
                foreach (var combatant in encounter.Combatants)
                    if (combatant.Faction == PartyFaction && IsDowned(combatant.Id)) stabilize(combatant.Id);
            }

            controller.EncounterService.EndCombat();
        }

        // The ability bar (End Turn!) and the turn-order list live in HUD regions that outlive any one encounter; with
        // no fight they would sit there offering End Turn beside "No combat in progress".
        private void SetCombatUiShown(bool shown)
        {
            var display = shown ? UnityEngine.UIElements.DisplayStyle.Flex : UnityEngine.UIElements.DisplayStyle.None;
            if (abilityBarRegionHolder != null) abilityBarRegionHolder.Region.style.display = display;
            if (turnOrderRegionHolder != null) turnOrderRegionHolder.Region.style.display = display;
        }

        private void EndEncounter()
        {
            if (session != null)
            {
                session.EventBus.Unsubscribe(GameEvent.CombatEnded, onCombatEnded);
                session.EventBus.Unsubscribe(GameEvent.AbilityResolved, onAbilityResolved);
            }
            if (targetingGate != null) targetingGate.TargetRejected -= OnTargetRejected;
            moveAdapter = null;
            pendingEndTurn = false;
            inputSource.TargetingGate = null;
            commandSource.CombatMoveOverride = null;
            abilityBarView.ClearCurrentCharacter();
            SetCombatUiShown(false);

            uiBridge?.Dispose();
            controller?.Dispose();
            uiBridge = null;
            controller = null;
            aiDriver = null;
            targetingGate = null;

            var callback = onEncounterEnded;
            onEncounterEnded = null;
            callback?.Invoke(lastEncounterPartyWon);
        }

        // HudRoot.CreateRegion is only ever called once -- a second encounter reuses the same region rather
        // than accumulating orphaned VisualElements in the HUD's own document tree.
        private sealed class VisualElementRegionHolder
        {
            public VisualElementRegionHolder(UnityEngine.UIElements.VisualElement region) => Region = region;
            public UnityEngine.UIElements.VisualElement Region { get; }
        }
    }
}

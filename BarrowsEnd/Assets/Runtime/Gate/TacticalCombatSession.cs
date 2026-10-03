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

        private GameSession session;
        private WorldSpawnService spawner;

        private TacticalEncounterController controller;
        private TacticalAiTurnDriver aiDriver;
        private AbilityTargetingGate targetingGate;
        private TacticalCombatUIBridge uiBridge;
        private VisualElementRegionHolder abilityBarRegionHolder;
        private VisualElementRegionHolder turnOrderRegionHolder;
        private Action<GameEventContext> onCombatEnded;
        private Action<GameEventContext> onAbilityResolved;
        private TacticalPlayerMoveAdapter moveAdapter;
        private bool pendingEndTurn;
        private Action<bool> onEncounterEnded;
        private bool lastEncounterPartyWon;

        public TacticalCombatSession(GateCommandSource commandSource, InputSystemCommandSource inputSource,
            HudRoot hudRoot, AbilityBarView abilityBarView, LayerMask hittableMask,
            TurnOrderView turnOrderView, Func<RuntimeEntityId, string> displayNameFor, Action<string> notify)
        {
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
        public void StartEncounter(IReadOnlyList<(RuntimeEntityId Id, DefinitionId Faction)> combatants, DefinitionId hostileFaction,
            BehaviorSetDefinition hostileBrain, Action<bool> onEncounterEnded = null)
        {
            if (IsActive || session == null) return;
            this.onEncounterEnded = onEncounterEnded;

            var positions = new LiveCharacterPositionProvider();
            if (!session.RulesDatabase.TryResolve<ResourceDefinition>(new DefinitionId(MovementResourceId), out var movementDefinition))
            {
                Debug.LogError("[TacticalCombatSession] resource-movement is missing -- run Barrows End/Content/Author Gear And Ruleset.");
                return;
            }

            foreach (var (id, _) in combatants) GrantMovement(id);
            var turnResources = new List<ResourceDefinition> { movementDefinition };
            foreach (var turnResourceId in new[] { ActionResourceId, BonusActionResourceId })
            {
                if (!session.RulesDatabase.TryResolve<ResourceDefinition>(new DefinitionId(turnResourceId), out var turnResource))
                {
                    Debug.LogError($"[TacticalCombatSession] {turnResourceId} is missing from the ruleset.");
                    return;
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

            var brain = new UtilityAIDecisionProvider(hostileBrain, session.RulesDatabase, session.Characters, abilityResolution, positions, new SeededRandomSource(session.GetHashCode()));
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
            var ratCount = 0;
            foreach (var (id, faction) in combatants)
                names[id] = faction == PartyFaction ? displayNameFor(id) ?? "Party member" : $"Giant Rat {++ratCount}";
            var turnOrderPresenter = new TurnOrderPresenter(controller.EncounterService, names);
            turnOrderRegionHolder ??= new VisualElementRegionHolder(hudRoot.CreateRegion(HudCorner.BottomLeft, 260f));
            turnOrderView.Initialize(turnOrderPresenter, turnOrderRegionHolder.Region, session.EventBus);

            uiBridge = new TacticalCombatUIBridge(controller, targetingGate, abilityBarView, session.EventBus, PartyFaction);

            onCombatEnded = _ => EndEncounter();
            session.EventBus.Subscribe(GameEvent.CombatEnded, onCombatEnded);
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

        private void OnAbilityResolved(GameEventContext context)
        {
            var current = controller?.EncounterService.Encounter?.CurrentTurn;
            if (current != null && current.Id == context.Source) pendingEndTurn = true;
        }

        private void OnTargetRejected(AbilityDefinition ability, RuntimeEntityId target, AbilityValidationResult result) =>
            notify?.Invoke(string.IsNullOrEmpty(result.Message) ? $"{ability.DisplayName}: {result.Reason}" : $"{ability.DisplayName}: {result.Message}");

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
                if (combatant.Faction == PartyFaction) partyAlive = true;
                else hostileAlive = true;
            }

            if (partyAlive && hostileAlive) return;
            lastEncounterPartyWon = partyAlive;
            controller.EncounterService.EndCombat();
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

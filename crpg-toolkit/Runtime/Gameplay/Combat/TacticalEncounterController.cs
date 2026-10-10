using System;
using System.Collections.Generic;
using CRPG.Abilities;
using CRPG.Actors;
using CRPG.Combat;
using CRPG.Core;
using CRPG.Gameplay.Actions;
using CRPG.Navigation;

namespace CRPG.Gameplay.Combat
{
    /// <summary>
    /// Task 4.5: the bridge nothing before this task built -- a live <see cref="CombatEncounterService"/>
    /// running against real spawned <see cref="CharacterAgent"/>s through <see cref="ActionScheduler"/>, the
    /// single entry point both the player-facing UI bridge and <see cref="TacticalAiTurnDriver"/> call so
    /// "AI using shared validation" is true structurally, not by convention.
    /// </summary>
    /// <remarks>
    /// Deliberately takes only GENERIC collaborators -- never an SRD-specific type like
    /// <c>CRPG.Rules.SRD5.DeathSaveService</c>/<c>CRPG.Rules.SRD5.ConcentrationService</c>/
    /// <c>CRPG.Rules.SRD5.DefaultInitiativeRule</c>, all of which stay the composition root's job to construct
    /// (mirrors Task 4.1's ruleset seam: generic Gameplay code must never construct ruleset policy). Those
    /// SRD-specific services self-subscribe to the SAME <see cref="IGameEventBus"/> passed in here and need no
    /// further wiring through this class at all.
    /// </remarks>
    /// <remarks>
    /// Constructed already bound to one encounter -- there is no separate reusable "StartEncounter" method. A
    /// new encounter means a new instance, which is what makes "restarting cannot duplicate subscriptions"
    /// true by construction rather than by caller discipline. <see cref="Dispose"/> tears down exactly what
    /// this instance itself subscribed (its own <see cref="ConditionDurationService"/> plus its CreatureDied
    /// handler) -- never call any method on this instance after disposing it.
    /// </remarks>
    /// <remarks>
    /// Takes an ALREADY-CONSTRUCTED (but not yet started) <see cref="CombatEncounterService"/> rather than
    /// building one itself -- <c>EncounterFactionProvider</c> needs to wrap that same instance and
    /// be handed to <see cref="IAbilityResolutionService"/> (via its own constructor) BEFORE this class can
    /// exist, since <see cref="ActionScheduler"/> is built from an already-fully-wired ability-resolution
    /// service. Building the encounter service here instead would make that ordering impossible without a
    /// late-bound mutable setter on ability resolution, which Task 4.2 deliberately didn't add.
    /// </remarks>
    public sealed class TacticalEncounterController : IDisposable
    {
        private readonly WorldSpawnService worldSpawnService;
        private readonly IGameEventBus eventBus;
        private readonly ConditionDurationService conditionDuration;
        private readonly Action<GameEventContext> onCreatureDied;
        private bool disposed;

        public CombatEncounterService EncounterService { get; }
        public ActionScheduler Scheduler { get; }

        /// <summary>Task 5.2 gap fix: <paramref name="useExplicitTurnOrder"/> drives the encounter through
        /// <see cref="CombatEncounterService.StartCombatWithExplicitOrder"/> instead of the rolled
        /// <see cref="CombatEncounterService.StartCombat(IEnumerable{ValueTuple{RuntimeEntityId, DefinitionId}})"/>
        /// -- for a ruleset with no <see cref="IInitiativeRule"/> at all (<paramref name="combatants"/> is then
        /// used as the turn order directly, in the order given).</summary>
        public TacticalEncounterController(
            ICharacterRegistry characters,
            IGameEventBus eventBus,
            IPositionProvider positions,
            IAbilityResolutionService abilityResolution,
            MovementResolutionService movementResolution,
            INavMeshService navMeshService,
            CombatEncounterService encounterService,
            WorldSpawnService worldSpawnService,
            IEnumerable<(RuntimeEntityId Id, DefinitionId Faction)> combatants,
            IActionPresentationHook presentationHook = null,
            ICombatLog combatLog = null,
            bool useExplicitTurnOrder = false)
        {
            this.worldSpawnService = worldSpawnService ?? throw new ArgumentNullException(nameof(worldSpawnService));
            this.eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));

            EncounterService = encounterService ?? throw new ArgumentNullException(nameof(encounterService));
            Scheduler = new ActionScheduler(abilityResolution, movementResolution, navMeshService, characters, positions, EncounterService, presentationHook);
            conditionDuration = new ConditionDurationService(EncounterService, characters, eventBus, combatLog);

            onCreatureDied = OnCreatureDied;
            eventBus.Subscribe(GameEvent.CreatureDied, onCreatureDied);

            if (useExplicitTurnOrder) EncounterService.StartCombatWithExplicitOrder(combatants);
            else EncounterService.StartCombat(combatants);
        }

        /// <summary>Real death (SRD5's "0 HP, three failed death saves"), never merely
        /// <see cref="GameEvent.CreatureDroppedToZeroHP"/> (unconscious-but-stable) -- that combatant stays
        /// fully live in the encounter, keeps its GameObject/agent, and keeps taking its turns (routed to a
        /// death-save roll by whichever turn driver owns it) until it either stabilizes or genuinely dies.
        /// Only on a real CreatureDied does this tear the live agent down and drop the encounter's own turn
        /// slot for it.</summary>
        private void OnCreatureDied(GameEventContext context) => Defeat(context.Target);

        /// <summary>Takes a combatant out of the fight for good: the spawn is marked defeated (its agent is torn down, or handed to its defeat presentation) and the encounter drops its
        /// turn slot. This is exactly what a real <see cref="GameEvent.CreatureDied"/> does -- exposed so a ruleset WITHOUT death (Fate's "Taken Out": the attacker narrates what happens
        /// next) can end a combatant's part in the encounter without publishing CreatureDied. Idempotent; a no-op once this controller is disposed.</summary>
        public void Defeat(RuntimeEntityId id)
        {
            if (disposed) return;
            Defeat(worldSpawnService, EncounterService, id);
        }

        /// <summary>The same teardown for a caller that runs combat on a bare <see cref="CombatEncounterService"/> and never builds a controller (Fate's ConsumerGame does).</summary>
        public static void Defeat(WorldSpawnService worldSpawnService, CombatEncounterService encounterService, RuntimeEntityId id)
        {
            if (worldSpawnService == null) throw new ArgumentNullException(nameof(worldSpawnService));
            if (encounterService == null) throw new ArgumentNullException(nameof(encounterService));

            worldSpawnService.MarkDefeated(id);
            encounterService.Encounter?.RemoveCombatant(id);
        }

        public void Tick(float deltaTime) => Scheduler.Tick(deltaTime);

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;

            eventBus.Unsubscribe(GameEvent.CreatureDied, onCreatureDied);
            conditionDuration.Dispose();
        }
    }
}

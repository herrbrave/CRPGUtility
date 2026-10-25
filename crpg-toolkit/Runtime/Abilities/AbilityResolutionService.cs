using System;
using System.Collections.Generic;
using CRPG.Actors;
using CRPG.Core;

namespace CRPG.Abilities
{
    public sealed partial class AbilityResolutionService : IAbilityResolutionService
    {
        private readonly ICharacterRegistry characters;
        private readonly IDiceService diceService;
        private readonly ICombatLog combatLog;
        private readonly IGameEventBus eventBus;
        private readonly IPositionProvider positions;
        private readonly InfluenceMap influenceMap;
        private readonly IAttackBonusRule attackBonusRule;
        private readonly IArmorClassRule armorClassRule;
        private readonly ISavingThrowRule savingThrowRule;
        private readonly IDamageResistanceRule damageResistanceRule;
        private readonly IRulesDatabase rulesDatabase;
        private readonly IFactionProvider factionProvider;
        private readonly ILineOfSightProvider lineOfSightProvider;

        public AbilityResolutionService(
            ICharacterRegistry characters,
            IDiceService diceService,
            ICombatLog combatLog = null,
            IGameEventBus eventBus = null,
            IPositionProvider positions = null,
            InfluenceMap influenceMap = null,
            IAttackBonusRule attackBonusRule = null,
            IArmorClassRule armorClassRule = null,
            ISavingThrowRule savingThrowRule = null,
            IDamageResistanceRule damageResistanceRule = null,
            IRulesDatabase rulesDatabase = null,
            IFactionProvider factionProvider = null,
            ILineOfSightProvider lineOfSightProvider = null)
        {
            this.characters = characters;
            this.diceService = diceService;
            this.combatLog = combatLog;
            this.eventBus = eventBus;
            this.positions = positions;
            this.influenceMap = influenceMap;
            this.attackBonusRule = attackBonusRule;
            this.armorClassRule = armorClassRule;
            this.savingThrowRule = savingThrowRule;
            this.damageResistanceRule = damageResistanceRule;
            this.rulesDatabase = rulesDatabase;
            this.factionProvider = factionProvider;
            this.lineOfSightProvider = lineOfSightProvider;
        }

        /// <summary>Who could be inside an aimed shape (the character registry is lookup-only, so the game supplies the candidates). Without it an aimed SHAPE is refused, never silently resolved to nobody.</summary>
        public ITargetCandidateSource CandidateSource { get; set; }

        /// <summary>The game's say on a clicked ground point (standing room, walls, bodies); optional.</summary>
        public IAimSurface AimSurface { get; set; }

        /// <summary>Moves a creature for <see cref="RelocateEffect"/> (Misty Step, a leap); optional, but an ability with that effect is refused up front when it is missing.</summary>
        public IRelocationService Relocation { get; set; }

        /// <summary>
        /// Task 4.2: the single preflight check that replaces CanUse/CanUseAsRitual/CanUseAtSlotLevel/
        /// IsValidTargetSelection -- safe to call repeatedly/non-destructively (never mutates anything), per
        /// IAbilityResolutionService's own documented intent that a UI/presentation layer calls this "as often
        /// as it needs while the player is still choosing." Execute() calls this again internally right before
        /// committing ("validate again at commit").
        /// </summary>
        public AbilityValidationResult Validate(
            RuntimeEntityId source, AbilityDefinition ability, IReadOnlyList<RuntimeEntityId> targets,
            bool asRitual = false, ResourceDefinition upcastWithSlot = null)
        {
            var castingModeRejection = CheckCastingMode(source, ability, asRitual, upcastWithSlot);
            if (castingModeRejection != null) return castingModeRejection;

            // An aimed ability (a point, a direction, a shaped area) is not cast by picking creatures: its targets are derived from where it is pointed.
            if (ability.Targeting.RequiresAim || ability.Targeting.CasterCentred)
                return AbilityValidationResult.Reject(AbilityRejectionReason.AimRequired, $"'{ability.DisplayName}' is aimed at the ground, not at creatures.");

            var targetCount = targets?.Count ?? 0;
            if (targetCount < ability.Targeting.MinTargets || targetCount > ability.Targeting.MaxTargets)
                return AbilityValidationResult.Reject(AbilityRejectionReason.InvalidTargetCount,
                    $"'{ability.DisplayName}' requires between {ability.Targeting.MinTargets} and {ability.Targeting.MaxTargets} targets.");

            var seenTargets = new HashSet<RuntimeEntityId>();
            foreach (var targetId in targets)
            {
                if (!seenTargets.Add(targetId))
                    return AbilityValidationResult.Reject(AbilityRejectionReason.DuplicateTarget, $"Target '{targetId}' was listed more than once.");
            }

            foreach (var targetId in targets)
            {
                if (!characters.TryGetCharacter(targetId, out _))
                    return AbilityValidationResult.Reject(AbilityRejectionReason.InvalidTarget, $"Target '{targetId}' does not resolve to a live character.");
            }

            if (factionProvider != null)
            {
                foreach (var targetId in targets)
                {
                    var rejection = CheckRelation(source, targetId, ability.Targeting.Relation);
                    if (rejection != null) return rejection;
                }
            }

            if (positions != null)
            {
                var sourcePosition = positions.GetPosition(source);
                foreach (var targetId in targets)
                {
                    if (UnityEngine.Vector3.Distance(sourcePosition, positions.GetPosition(targetId)) > ability.Targeting.Range)
                        return AbilityValidationResult.Reject(AbilityRejectionReason.OutOfRange,
                            $"Target '{targetId}' is beyond '{ability.DisplayName}''s range ({ability.Targeting.Range}).");
                }

                if (lineOfSightProvider != null && ability.Targeting.RequireLineOfSight)
                {
                    foreach (var targetId in targets)
                    {
                        if (!lineOfSightProvider.HasLineOfSight(sourcePosition, positions.GetPosition(targetId)))
                            return AbilityValidationResult.Reject(AbilityRejectionReason.NoLineOfSight,
                                $"No line of sight to target '{targetId}'.");
                    }
                }
            }

            var readinessRejection = CheckReadiness(source, ability, asRitual, upcastWithSlot);
            if (readinessRejection != null) return readinessRejection;

            return AbilityValidationResult.Ok();
        }

        /// <summary>Task 4.5: the target-INDEPENDENT subset of <see cref="Validate"/> -- can this caster even
        /// attempt this ability right now (not incapacitated, knows it if the known-abilities gate applies,
        /// authored casting mode/upcast is legal, can afford its costs, every required effect service is
        /// wired), before any real target has been chosen. A UI ability bar uses this to decide a button's
        /// enabled state ahead of targeting; <see cref="Validate"/> (which additionally needs real targets)
        /// stays the sole authority once a target is actually picked -- this reuses its exact same internal
        /// checks via <see cref="CheckCastingMode"/>/<see cref="CheckReadiness"/> rather than duplicating any
        /// of its logic.</summary>
        public AbilityValidationResult CanAttempt(RuntimeEntityId source, AbilityDefinition ability, bool asRitual = false, ResourceDefinition upcastWithSlot = null)
        {
            var castingModeRejection = CheckCastingMode(source, ability, asRitual, upcastWithSlot);
            if (castingModeRejection != null) return castingModeRejection;

            var readinessRejection = CheckReadiness(source, ability, asRitual, upcastWithSlot);
            if (readinessRejection != null) return readinessRejection;

            return AbilityValidationResult.Ok();
        }

        /// <summary>True when a feature the character currently holds (a class feature, a racial one, or one an equipped item grants: a wielded weapon's attack) grants this ability. Such abilities are available without ever being
        /// "learned": the ability bar already lists them, and without this the cast gate rejected them as AbilityNotKnown for any character that knew some other ability.</summary>
        private bool IsGrantedByAFeature(CharacterState character, DefinitionId abilityId)
        {
            if (rulesDatabase == null) return false;
            foreach (var featureId in character.Features)
            {
                if (!rulesDatabase.TryResolve<FeatureDefinition>(featureId, out var feature)) continue;
                for (var i = 0; i < feature.GrantedAbilityIds.Count; i++) if (feature.GrantedAbilityIds[i] == abilityId) return true;
            }

            return false;
        }

        private AbilityValidationResult CheckCastingMode(RuntimeEntityId source, AbilityDefinition ability, bool asRitual, ResourceDefinition upcastWithSlot)
        {
            if (!CanAct(source)) return AbilityValidationResult.Reject(AbilityRejectionReason.Incapacitated);

            // Legacy/compatibility mode: a character that has never called LearnAbility (e.g. AICombatDemoBootstrap's
            // demo NPCs, which Task 4.5 -- not 4.2 -- retrofits) is treated as not opted into this gate at all.
            // A character with at least one known ability is properly restricted to what it knows.
            if (characters.TryGetCharacter(source, out var gateCharacter)
                && gateCharacter.KnownAbilities.Count > 0 && !gateCharacter.KnowsAbility(ability.Id) && !IsGrantedByAFeature(gateCharacter, ability.Id))
                return AbilityValidationResult.Reject(AbilityRejectionReason.AbilityNotKnown);

            if (asRitual)
            {
                if ((ability.Tags & AbilityTag.Ritual) == 0)
                    return AbilityValidationResult.Reject(AbilityRejectionReason.RitualTagRequired,
                        $"'{ability.DisplayName}' has no Ritual tag.");
            }
            else if (upcastWithSlot != null)
            {
                if (upcastWithSlot.SpellSlotLevel < ability.SpellLevel)
                    return AbilityValidationResult.Reject(AbilityRejectionReason.UpcastSlotBelowSpellLevel,
                        $"'{upcastWithSlot.DisplayName}' is below '{ability.DisplayName}''s own spell level.");
                if (!characters.TryGetCharacter(source, out var upcastCaster)
                    || !upcastCaster.TryGetResource(upcastWithSlot.Id, out var slotState) || slotState.Current < 1)
                    return AbilityValidationResult.Reject(AbilityRejectionReason.InsufficientResources,
                        $"No '{upcastWithSlot.DisplayName}' available.");
            }

            return null;
        }

        private AbilityValidationResult CheckReadiness(RuntimeEntityId source, AbilityDefinition ability, bool asRitual, ResourceDefinition upcastWithSlot)
        {
            if (!characters.TryGetCharacter(source, out var costCaster))
                return AbilityValidationResult.Reject(AbilityRejectionReason.InsufficientResources, "Caster not found.");

            var costPlan = AbilityCostPlan.Build(ability, asRitual, upcastWithSlot);
            foreach (var entry in costPlan.Entries)
            {
                if (!costCaster.TryGetResource(entry.Resource.Id, out var resourceState) || resourceState.Current < entry.Amount)
                    return AbilityValidationResult.Reject(AbilityRejectionReason.InsufficientResources,
                        $"Not enough '{entry.Resource.DisplayName}' ({entry.Amount} required).");
            }

            var missingService = FindMissingRequiredEffectService(ability);
            if (missingService != null)
                return AbilityValidationResult.Reject(AbilityRejectionReason.MissingRequiredEffectService, missingService);

            return null;
        }

        /// <summary>Task 4.2: builds an ability, applies its effects, and reports the real outcome -- never
        /// "false success." Re-validates against current state right before committing (Execute can be called
        /// with a decision made moments earlier, e.g. by AI scoring, so state may have changed since); on
        /// rejection, nothing is mutated at all -- no resource touched, no event published. Once committed,
        /// resources are spent exactly once via the same AbilityCostPlan Validate already checked affordability
        /// against, then effects run inside a try/catch: an unexpected exception becomes a Faulted result and
        /// an AbilityFaulted event rather than an unhandled exception with no completion signal -- resources
        /// are NOT rolled back in that case (matches a real tabletop cast not un-happening after a later
        /// mishap; this is "spend exactly once," not "every effect is atomic").
        ///
        /// Task 4.4: a fresh EffectOwnerId is generated for every cast (not just Concentration-tagged ones), so
        /// any condition it applies is individually ownership-traceable. For a Concentration-tagged ability, the
        /// caster's PREVIOUS concentration (if any) is broken via ConcentrationEffectCleanup.Break BEFORE this
        /// cast's own effects run -- deterministic regardless of whether the new cast goes on to succeed or
        /// fault. The new concentration record is registered using AbilityExecutionContext.OwnedTargets (who
        /// ACTUALLY received a condition, not the originally-requested target list) on BOTH the success path and
        /// the fault path -- registering it even on fault is deliberate: a faulted cast's partially-applied
        /// conditions must not be left with a valid owner that no character's ConcentrationEffectOwnerId ever
        /// points to (they'd still be reachable via DispelEffect's independent owner-scan, but not via a later
        /// concentration-based Break), matching the existing "a fault doesn't un-happen what already happened"
        /// precedent AbilityFaulted's own doc comment already establishes.</summary>
        public AbilityExecutionResult Execute(
            RuntimeEntityId source, AbilityDefinition ability, IReadOnlyList<RuntimeEntityId> targets,
            bool asRitual = false, ResourceDefinition upcastWithSlot = null)
        {
            var validation = Validate(source, ability, targets, asRitual, upcastWithSlot);
            if (!validation.IsValid) return AbilityExecutionResult.FromRejection(validation);
            return ExecuteCore(source, ability, targets, AbilityAim.None, asRitual, upcastWithSlot);
        }

        /// <summary>Casts an AIMED ability: validates the aim (range, line of sight, the game's aim surface, the candidates in the shape), derives the creatures inside the shape and runs the effects over them with the aim in the context. Costs are spent once, exactly as for a creature-targeted cast.</summary>
        public AbilityExecutionResult Execute(
            RuntimeEntityId source, AbilityDefinition ability, AbilityAim aim,
            bool asRitual = false, ResourceDefinition upcastWithSlot = null)
        {
            var validation = ValidateAim(source, ability, aim, asRitual, upcastWithSlot, out var accepted, out var derived);
            if (!validation.IsValid) return AbilityExecutionResult.FromRejection(validation);
            return ExecuteCore(source, ability, derived, accepted, asRitual, upcastWithSlot);
        }

        private AbilityExecutionResult ExecuteCore(
            RuntimeEntityId source, AbilityDefinition ability, IReadOnlyList<RuntimeEntityId> targets, AbilityAim aim,
            bool asRitual, ResourceDefinition upcastWithSlot)
        {

            // Validate() already confirmed the caster resolves (both CanAct and the cost-affordability check
            // require it) -- re-resolving here rather than threading it through is simpler and just as safe.
            characters.TryGetCharacter(source, out var caster);

            var costPlan = AbilityCostPlan.Build(ability, asRitual, upcastWithSlot);
            foreach (var entry in costPlan.Entries)
            {
                if (caster.TryGetResource(entry.Resource.Id, out var resourceState)) resourceState.Spend(entry.Amount);
            }

            eventBus?.Publish(new GameEventContext(GameEvent.AbilityStarted, source));

            var isConcentration = (ability.Tags & AbilityTag.Concentration) != 0;
            var ownerId = EffectOwnerId.NewOwner();

            // Break BEFORE the new effects run, unconditionally once we know this cast will replace the old
            // one -- see the method doc comment for why this ordering (not "after effects succeed") matters.
            if (isConcentration && caster.ConcentratingOnAbilityId.IsValid)
                ConcentrationEffectCleanup.Break(source, caster, characters, eventBus, combatLog);

            var upcastLevels = upcastWithSlot != null ? Math.Max(0, upcastWithSlot.SpellSlotLevel - ability.SpellLevel) : 0;
            var context = new AbilityExecutionContext(
                source, StrikesFor(ability, targets), characters, diceService, combatLog, eventBus, positions, influenceMap,
                attackBonusRule, armorClassRule, criticalTargets: null, savingThrowRule: savingThrowRule,
                damageResistanceRule: damageResistanceRule, upcastLevels: upcastLevels, ownerId: ownerId, aim: aim, relocation: Relocation);

            void RegisterConcentration()
            {
                if (!isConcentration) return;
                caster.SetConcentrationEffect(ability.Id, ownerId, context.OwnedTargets);
                eventBus?.Publish(new GameEventContext(GameEvent.ConcentrationStarted, source, source));
            }

            try
            {
                foreach (var effect in ability.Effects) effect.Apply(context);
            }
            catch (Exception ex)
            {
                RegisterConcentration();
                eventBus?.Publish(new GameEventContext(GameEvent.AbilityFaulted, source));
                return AbilityExecutionResult.Fault(ex);
            }

            RegisterConcentration();
            eventBus?.Publish(new GameEventContext(GameEvent.AbilityResolved, source));
            return AbilityExecutionResult.Ok();
        }

        /// <summary>The creatures each effect runs over: the chosen ones, or for an ability that fires several projectiles, the chosen ones dealt out in turn until every projectile has a creature (three darts on two targets: first, second, first).</summary>
        private static IReadOnlyList<RuntimeEntityId> StrikesFor(AbilityDefinition ability, IReadOnlyList<RuntimeEntityId> chosen)
        {
            var targeting = ability.Targeting;
            if (!targeting.SpreadProjectiles || chosen == null || chosen.Count == 0 || chosen.Count >= targeting.MaxTargets || targeting.RequiresAim || targeting.CasterCentred) return chosen;
            var strikes = new List<RuntimeEntityId>(targeting.MaxTargets);
            for (var i = 0; i < targeting.MaxTargets; i++) strikes.Add(chosen[i % chosen.Count]);
            return strikes;
        }

        /// <summary>Shared incapacitation gate: unconscious-and-dying (DeathSaves) or any currently-applied
        /// condition whose ConditionDefinition.PreventsActions is set (Incapacitated, Paralyzed, Petrified,
        /// Stunned, Unconscious, ...).</summary>
        private bool CanAct(RuntimeEntityId source)
        {
            if (!characters.TryGetCharacter(source, out var character)) return false;
            if (character.DeathSaves != null) return false;

            if (rulesDatabase != null)
            {
                foreach (var condition in character.Conditions)
                {
                    if (rulesDatabase.TryResolve<ConditionDefinition>(condition.ConditionId, out var conditionDefinition)
                        && conditionDefinition.PreventsActions)
                        return false;
                }
            }

            return true;
        }

        /// <summary>Generalizes the exact "same faction = ally, different = enemy" rule
        /// CombatEncounter.GetEnemiesOf/GetAlliesOf already use. Self/Neutral/Any targeting relations aren't
        /// faction-restricted at all (there's no "neutral" concept in the underlying two-way faction model, and
        /// Self/Any aren't relation restrictions by definition) -- only Ally/Enemy are actually enforced.
        /// Returns null when the relation is satisfied (or not applicable), a rejection otherwise.</summary>
        private AbilityValidationResult CheckRelation(RuntimeEntityId source, RuntimeEntityId target, TargetRelation relation)
        {
            if (relation != TargetRelation.Ally && relation != TargetRelation.Enemy) return null;

            if (!factionProvider.TryGetFaction(source, out var sourceFaction) || !factionProvider.TryGetFaction(target, out var targetFaction))
                return AbilityValidationResult.Reject(AbilityRejectionReason.WrongRelation,
                    $"Faction could not be resolved for source or target '{target}'.");

            var isAlly = sourceFaction == targetFaction;
            if (relation == TargetRelation.Ally && !isAlly)
                return AbilityValidationResult.Reject(AbilityRejectionReason.WrongRelation, $"Target '{target}' is not an ally.");
            if (relation == TargetRelation.Enemy && isAlly)
                return AbilityValidationResult.Reject(AbilityRejectionReason.WrongRelation, $"Target '{target}' is not an enemy.");

            return null;
        }

        /// <summary>Task 4.2: the authoritative effect/service compatibility preflight map -- every known
        /// AbilityEffect type that needs an optional AbilityExecutionContext service gets listed here, so a
        /// caster is rejected up front rather than charged for an ability whose effects will silently no-op
        /// (every effect type already gracefully no-ops when its optional service is missing; the bug this
        /// closes is charging for that no-op, not the no-op itself). A new effect type that depends on a new
        /// optional service must be added here too, or it silently regresses into charge-and-no-op -- a more
        /// scalable design (each AbilityEffect declaring its own required services) is a reasonable future
        /// improvement, not attempted this task for the handful of effect types that exist today.
        /// DamageEffect's DamageResistanceRule is deliberately NOT listed -- missing resistance data degrades
        /// to "full unresisted damage," a real (if unintended) outcome, not a "nothing happened" one.</summary>
        private string FindMissingRequiredEffectService(AbilityDefinition ability)
        {
            foreach (var effect in ability.Effects)
            {
                var missing = FindMissingRequiredEffectService(effect);
                if (missing != null) return missing;
            }

            return null;
        }

        private string FindMissingRequiredEffectService(AbilityEffect effect)
        {
            switch (effect)
            {
                case AttackRollEffect when attackBonusRule == null || armorClassRule == null:
                    return "An AttackRollEffect requires AttackBonusRule and ArmorClassRule to be wired up.";
                case SavingThrowEffect when savingThrowRule == null:
                    return "A SavingThrowEffect requires SavingThrowRule to be wired up.";
                case CreateInfluenceSourceEffect when positions == null || influenceMap == null:
                    return "A CreateInfluenceSourceEffect requires Positions and an InfluenceMap to be wired up.";
                case RelocateEffect when Relocation == null:
                    return "A RelocateEffect requires a relocation service to be wired up.";
            }

            foreach (var child in effect.GetChildEffects())
            {
                var missing = FindMissingRequiredEffectService(child);
                if (missing != null) return missing;
            }

            return null;
        }
    }
}

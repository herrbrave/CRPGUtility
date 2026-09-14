using System;
using System.Collections.Generic;
using CRPG.Actors;
using CRPG.Core;

namespace CRPG.Abilities
{
    public sealed class AbilityResolutionService : IAbilityResolutionService
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
            if (!CanAct(source)) return AbilityValidationResult.Reject(AbilityRejectionReason.Incapacitated);

            // Legacy/compatibility mode: a character that has never called LearnAbility (e.g. AICombatDemoBootstrap's
            // demo NPCs, which Task 4.5 -- not 4.2 -- retrofits) is treated as not opted into this gate at all.
            // A character with at least one known ability is properly restricted to what it knows.
            if (characters.TryGetCharacter(source, out var gateCharacter)
                && gateCharacter.KnownAbilities.Count > 0 && !gateCharacter.KnowsAbility(ability.Id))
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

            return AbilityValidationResult.Ok();
        }

        /// <summary>Task 4.2: builds an ability, applies its effects, and reports the real outcome -- never
        /// "false success." Re-validates against current state right before committing (Execute can be called
        /// with a decision made moments earlier, e.g. by AI scoring, so state may have changed since); on
        /// rejection, nothing is mutated at all -- no resource touched, no event published. Once committed,
        /// resources are spent exactly once via the same AbilityCostPlan Validate already checked affordability
        /// against, then effects run inside a try/catch: an unexpected exception becomes a Faulted result and
        /// an AbilityFaulted event rather than an unhandled exception with no completion signal -- resources
        /// are NOT rolled back in that case (matches a real tabletop cast not un-happening after a later
        /// mishap; this is "spend exactly once," not "every effect is atomic").</summary>
        public AbilityExecutionResult Execute(
            RuntimeEntityId source, AbilityDefinition ability, IReadOnlyList<RuntimeEntityId> targets,
            bool asRitual = false, ResourceDefinition upcastWithSlot = null)
        {
            var validation = Validate(source, ability, targets, asRitual, upcastWithSlot);
            if (!validation.IsValid) return AbilityExecutionResult.FromRejection(validation);

            // Validate() already confirmed the caster resolves (both CanAct and the cost-affordability check
            // require it) -- re-resolving here rather than threading it through is simpler and just as safe.
            characters.TryGetCharacter(source, out var caster);

            var costPlan = AbilityCostPlan.Build(ability, asRitual, upcastWithSlot);
            foreach (var entry in costPlan.Entries)
            {
                if (caster.TryGetResource(entry.Resource.Id, out var resourceState)) resourceState.Spend(entry.Amount);
            }

            eventBus?.Publish(new GameEventContext(GameEvent.AbilityStarted, source));

            var upcastLevels = upcastWithSlot != null ? Math.Max(0, upcastWithSlot.SpellSlotLevel - ability.SpellLevel) : 0;
            var context = new AbilityExecutionContext(
                source, targets, characters, diceService, combatLog, eventBus, positions, influenceMap,
                attackBonusRule, armorClassRule, criticalTargets: null, savingThrowRule: savingThrowRule,
                damageResistanceRule: damageResistanceRule, upcastLevels: upcastLevels);

            try
            {
                foreach (var effect in ability.Effects) effect.Apply(context);
            }
            catch (Exception ex)
            {
                eventBus?.Publish(new GameEventContext(GameEvent.AbilityFaulted, source));
                return AbilityExecutionResult.Fault(ex);
            }

            // Casting a Concentration-tagged ability always starts (or restarts) concentration on it, ending
            // whatever the caster was concentrating on before -- SRD5 allows only one at a time. See
            // CharacterState.ConcentratingOnAbilityId's own remarks on what this deliberately does NOT do.
            if ((ability.Tags & AbilityTag.Concentration) != 0 && characters.TryGetCharacter(source, out var concentratingCharacter))
            {
                if (concentratingCharacter.ConcentratingOnAbilityId.IsValid)
                    eventBus?.Publish(new GameEventContext(GameEvent.ConcentrationBroken, source, source));
                concentratingCharacter.ConcentratingOnAbilityId = ability.Id;
                eventBus?.Publish(new GameEventContext(GameEvent.ConcentrationStarted, source, source));
            }

            eventBus?.Publish(new GameEventContext(GameEvent.AbilityResolved, source));
            return AbilityExecutionResult.Ok();
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

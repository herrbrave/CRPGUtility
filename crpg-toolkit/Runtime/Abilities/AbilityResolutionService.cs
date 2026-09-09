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
            IRulesDatabase rulesDatabase = null)
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
        }

        public bool CanUse(RuntimeEntityId source, AbilityDefinition ability)
        {
            if (!CanAct(source)) return false;

            foreach (var cost in ability.Costs)
            {
                if (!characters.TryGetCharacter(source, out var character)
                    || !character.TryGetResource(cost.Resource.Id, out var resource) || resource.Current < cost.Amount)
                    return false;
            }

            return true;
        }

        /// <summary>Whether this ability can be cast as a ritual -- skipping its normal resource costs
        /// (spending no spell slot, taking no action) in exchange for the extra time a ritual cast takes in
        /// real SRD5 rules (not itself modeled -- there's no turn/time-of-day system granular enough to enforce
        /// "10 extra minutes" yet). Still gated by CanAct: an incapacitated/dying caster can't ritual-cast either.</summary>
        public bool CanUseAsRitual(RuntimeEntityId source, AbilityDefinition ability) =>
            (ability.Tags & AbilityTag.Ritual) != 0 && CanAct(source);

        /// <summary>Whether this spell can be upcast into the given slot -- the slot must be at or above the
        /// spell's own SpellLevel (never a lower slot) and the caster must actually have one available.</summary>
        public bool CanUseAtSlotLevel(RuntimeEntityId source, AbilityDefinition ability, ResourceDefinition slotResource)
        {
            if (!CanAct(source)) return false;
            if (slotResource.SpellSlotLevel < ability.SpellLevel) return false;

            return characters.TryGetCharacter(source, out var character)
                && character.TryGetResource(slotResource.Id, out var resource) && resource.Current >= 1;
        }

        /// <summary>Shared incapacitation gate for CanUse/CanUseAsRitual: unconscious-and-dying (DeathSaves) or
        /// any currently-applied condition whose ConditionDefinition.PreventsActions is set (Incapacitated,
        /// Paralyzed, Petrified, Stunned, Unconscious, ...). Generalizes what used to be a DeathSaves-only
        /// check into real, data-driven condition content -- ReactionService.ResolveOpportunity calls through
        /// CanUse too, so a Stunned character correctly can't take opportunity-attack reactions either, for free.</summary>
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

        public bool IsValidTargetSelection(AbilityDefinition ability, IReadOnlyList<RuntimeEntityId> targets)
        {
            var count = targets?.Count ?? 0;
            return count >= ability.Targeting.MinTargets && count <= ability.Targeting.MaxTargets;
        }

        public void Execute(
            RuntimeEntityId source, AbilityDefinition ability, IReadOnlyList<RuntimeEntityId> targets,
            bool asRitual = false, ResourceDefinition upcastWithSlot = null)
        {
            if (asRitual)
            {
                if (!CanUseAsRitual(source, ability))
                    throw new InvalidOperationException($"'{ability.DisplayName}' cannot be cast as a ritual by {source} right now.");
            }
            else if (upcastWithSlot != null)
            {
                if (!CanUseAtSlotLevel(source, ability, upcastWithSlot))
                    throw new InvalidOperationException($"'{ability.DisplayName}' cannot be cast at {upcastWithSlot.DisplayName} by {source} right now.");
            }
            else if (!CanUse(source, ability))
            {
                throw new InvalidOperationException($"'{ability.DisplayName}' cannot be used by {source} right now.");
            }

            if (!IsValidTargetSelection(ability, targets))
                throw new InvalidOperationException(
                    $"'{ability.DisplayName}' requires between {ability.Targeting.MinTargets} and {ability.Targeting.MaxTargets} targets.");

            eventBus?.Publish(new GameEventContext(GameEvent.AbilityStarted, source));

            // A ritual cast spends no resources at all -- no spell slot, no action -- per SRD5's ritual casting
            // rule. Upcasting spends exactly the chosen slot instead of the ability's own authored Costs (a
            // spell's Costs are assumed to name just its base-level slot).
            if (upcastWithSlot != null)
            {
                if (characters.TryGetCharacter(source, out var upcastingCharacter) && upcastingCharacter.TryGetResource(upcastWithSlot.Id, out var slot))
                    slot.Spend(1);
            }
            else if (!asRitual && characters.TryGetCharacter(source, out var sourceCharacter))
            {
                foreach (var cost in ability.Costs)
                {
                    if (sourceCharacter.TryGetResource(cost.Resource.Id, out var resource)) resource.Spend(cost.Amount);
                }
            }

            var upcastLevels = upcastWithSlot != null ? Math.Max(0, upcastWithSlot.SpellSlotLevel - ability.SpellLevel) : 0;
            var context = new AbilityExecutionContext(
                source, targets, characters, diceService, combatLog, eventBus, positions, influenceMap,
                attackBonusRule, armorClassRule, criticalTargets: null, savingThrowRule: savingThrowRule,
                damageResistanceRule: damageResistanceRule, upcastLevels: upcastLevels);
            foreach (var effect in ability.Effects) effect.Apply(context);

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
        }
    }
}

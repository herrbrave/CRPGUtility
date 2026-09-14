using System.Collections.Generic;
using CRPG.Actors;
using CRPG.Core;

namespace CRPG.Abilities
{
    /// <summary>Everything an AbilityEffect needs to actually apply itself: who cast it, who/what it's
    /// hitting, and the shared services (character lookup, dice, logging, events) it needs along the way.</summary>
    public sealed class AbilityExecutionContext
    {
        public AbilityExecutionContext(
            RuntimeEntityId source,
            IReadOnlyList<RuntimeEntityId> targets,
            ICharacterRegistry characters,
            IDiceService diceService,
            ICombatLog combatLog,
            IGameEventBus eventBus = null,
            IPositionProvider positions = null,
            InfluenceMap influenceMap = null,
            IAttackBonusRule attackBonusRule = null,
            IArmorClassRule armorClassRule = null,
            IReadOnlyCollection<RuntimeEntityId> criticalTargets = null,
            ISavingThrowRule savingThrowRule = null,
            IDamageResistanceRule damageResistanceRule = null,
            int upcastLevels = 0,
            EffectOwnerId ownerId = default,
            HashSet<RuntimeEntityId> ownedTargets = null)
        {
            Source = source;
            Targets = targets;
            Characters = characters;
            DiceService = diceService;
            CombatLog = combatLog;
            EventBus = eventBus;
            Positions = positions;
            InfluenceMap = influenceMap;
            AttackBonusRule = attackBonusRule;
            ArmorClassRule = armorClassRule;
            CriticalTargets = criticalTargets;
            SavingThrowRule = savingThrowRule;
            DamageResistanceRule = damageResistanceRule;
            UpcastLevels = upcastLevels;
            OwnerId = ownerId;
            OwnedTargets = ownedTargets ?? new HashSet<RuntimeEntityId>();
        }

        public RuntimeEntityId Source { get; }
        public IReadOnlyList<RuntimeEntityId> Targets { get; }
        public ICharacterRegistry Characters { get; }
        public IDiceService DiceService { get; }
        public ICombatLog CombatLog { get; }
        public IGameEventBus EventBus { get; }

        /// <summary>Null unless whoever's driving this ability's resolution wired up spatial services -- only
        /// effects that need a position (e.g. CreateInfluenceSourceEffect) require these.</summary>
        public IPositionProvider Positions { get; }
        public InfluenceMap InfluenceMap { get; }

        /// <summary>Null unless attack-roll resolution is wired up -- only AttackRollEffect requires these.</summary>
        public IAttackBonusRule AttackBonusRule { get; }
        public IArmorClassRule ArmorClassRule { get; }

        /// <summary>Which of this effect's Targets scored a critical hit -- set by AttackRollEffect on the
        /// narrowed context it hands to its onHit effect, so e.g. DamageEffect can double dice for just those
        /// targets. Null outside an attack-roll's onHit effect.</summary>
        public IReadOnlyCollection<RuntimeEntityId> CriticalTargets { get; }

        /// <summary>Null unless saving-throw resolution is wired up -- only SavingThrowEffect requires this.</summary>
        public ISavingThrowRule SavingThrowRule { get; }

        /// <summary>Null unless damage resistance/immunity/vulnerability is wired up -- only DamageEffect
        /// requires this, and only for damage tagged with a DamageTypeId.</summary>
        public IDamageResistanceRule DamageResistanceRule { get; }

        /// <summary>How many levels above the ability's own AbilityDefinition.SpellLevel it was cast at (0 for
        /// a normal cast, or any non-spell ability) -- set by AbilityResolutionService.Execute's upcastWithSlot
        /// parameter. DamageEffect/HealEffect use this to add their authored per-level bonus dice.</summary>
        public int UpcastLevels { get; }

        /// <summary>Task 4.4: which specific cast this context belongs to -- ApplyConditionEffect tags every
        /// ConditionInstance it creates with this. EffectOwnerId.None for any caller that doesn't pass one
        /// (matches pre-Task-4.4 behavior: an ownerless condition can only be removed by type, never by
        /// ownership-scoped cleanup).</summary>
        public EffectOwnerId OwnerId { get; }

        /// <summary>Task 4.4: the *actual* recipients ApplyConditionEffect has tagged with <see cref="OwnerId"/>
        /// so far, as opposed to <see cref="Targets"/> (what the ability's target selection originally
        /// requested) -- a self-effect, an area-expanded target, or a redirected target all still end up here.
        /// A narrowed context (AttackRollEffect's onHit, ConditionalEffect's thenEffect) MUST be constructed
        /// with this SAME instance (not a fresh one) so a nested ApplyConditionEffect's recordings are visible
        /// to whoever reads the outer, top-level context afterward -- see those two classes' own remarks.</summary>
        public HashSet<RuntimeEntityId> OwnedTargets { get; }
    }
}

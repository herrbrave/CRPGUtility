using CRPG.Core;

namespace CRPG.Abilities
{
    /// <summary>What a rider rule is told about an attack that HIT: who attacked whom, with which ability, the advantage state of the roll that was kept (so "had advantage" is the roll's own, flanking included), and whether it was a critical hit.</summary>
    public readonly struct AttackRiderQuery
    {
        public AttackRiderQuery(RuntimeEntityId attacker, RuntimeEntityId target, AbilityDefinition ability, AdvantageState advantage, bool isCritical)
        { Attacker = attacker; Target = target; Ability = ability; Advantage = advantage; IsCritical = isCritical; }

        public RuntimeEntityId Attacker { get; }
        public RuntimeEntityId Target { get; }
        /// <summary>The ability being resolved (null when the caller did not supply one).</summary>
        public AbilityDefinition Ability { get; }
        public AdvantageState Advantage { get; }
        public bool IsCritical { get; }
    }

    /// <summary>Extra damage a rule adds to a weapon hit: dice (doubled on a critical hit, like the weapon's own) dealt with the weapon's own hit-point resource and damage type, under a label the log and the presentation show ("Sneak Attack").</summary>
    public readonly struct AttackRider
    {
        public AttackRider(DiceExpression extraDamage, string label) { ExtraDamage = extraDamage; Label = label; }
        public DiceExpression ExtraDamage { get; }
        public string Label { get; }
    }

    /// <summary>
    /// A ruleset's say on EXTRA damage for an attack that hit (Sneak Attack, and later anything of its kind: Divine Smite, Hunter's Mark). <see cref="AttackRollEffect"/> asks it once per hit target, after the weapon's own damage; an offered rider is rolled and dealt through the same damage path
    /// (resistance, events, the log) and announced with an <see cref="AttackRiderLogEntry"/>. Null on the context means nothing is ever added.
    /// </summary>
    public interface IAttackRiderRule
    {
        bool TryGetRider(in AttackRiderQuery attack, out AttackRider rider);
    }
}

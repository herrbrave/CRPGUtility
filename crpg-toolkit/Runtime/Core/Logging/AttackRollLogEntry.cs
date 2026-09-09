namespace CRPG.Core
{
    public sealed class AttackRollLogEntry : CombatLogEntry
    {
        public AttackRollLogEntry(RuntimeEntityId source, RuntimeEntityId target, RollResult roll, int armorClass, bool isHit, bool isCriticalHit)
            : base(source, target)
        {
            Roll = roll;
            ArmorClass = armorClass;
            IsHit = isHit;
            IsCriticalHit = isCriticalHit;
        }

        public RollResult Roll { get; }
        public int ArmorClass { get; }
        public bool IsHit { get; }
        public bool IsCriticalHit { get; }

        public override string Summary()
        {
            var outcome = !IsHit ? "Miss." : IsCriticalHit ? "Critical Hit!" : "Hit!";
            return $"{Source} attacks {Target}: {Roll.Total} vs AC {ArmorClass} -- {outcome}";
        }
    }
}

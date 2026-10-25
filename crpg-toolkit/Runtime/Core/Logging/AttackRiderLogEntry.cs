namespace CRPG.Core
{
    /// <summary>An attack rider (Sneak Attack and the like) was added to a hit: logged just before its extra damage, so a presentation can give the moment its own effect and sound.</summary>
    public sealed class AttackRiderLogEntry : CombatLogEntry
    {
        public AttackRiderLogEntry(RuntimeEntityId source, RuntimeEntityId target, string label, bool isCriticalHit) : base(source, target)
        {
            Label = label ?? string.Empty;
            IsCriticalHit = isCriticalHit;
        }

        public string Label { get; }
        public bool IsCriticalHit { get; }

        public override string Summary() => $"{Source} lands a {Label} on {Target}{(IsCriticalHit ? " (critical)" : string.Empty)}.";
    }
}

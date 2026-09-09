using CRPG.Core;

namespace CRPG.Actors
{
    /// <summary>A condition currently affecting a character. <see cref="RemainingDuration"/> of null means
    /// indefinite -- it lasts until something explicitly removes it (a saving throw, an end-of-combat cleanup, ...).</summary>
    public sealed class ConditionInstance
    {
        public ConditionInstance(DefinitionId conditionId, RuntimeEntityId source, RuntimeEntityId target, int? remainingDuration = null, int stackCount = 1)
        {
            ConditionId = conditionId;
            Source = source;
            Target = target;
            RemainingDuration = remainingDuration;
            StackCount = stackCount;
        }

        public DefinitionId ConditionId { get; }
        public RuntimeEntityId Source { get; }
        public RuntimeEntityId Target { get; }
        public int? RemainingDuration { get; set; }
        public int StackCount { get; set; }
    }
}

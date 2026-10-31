using System.Collections.Generic;

namespace CRPG.Core
{
    /// <summary>The result of one Hide attempt: the Stealth total, who it fooled and who still sees the hider.</summary>
    public sealed class HideOutcome
    {
        public HideOutcome(int total, IReadOnlyList<RuntimeEntityId> fooled, IReadOnlyList<RuntimeEntityId> seen)
        {
            Total = total;
            Fooled = fooled;
            Seen = seen;
        }

        public int Total { get; }
        public IReadOnlyList<RuntimeEntityId> Fooled { get; }
        public IReadOnlyList<RuntimeEntityId> Seen { get; }
        public bool Hidden => Fooled.Count > 0;
    }
}

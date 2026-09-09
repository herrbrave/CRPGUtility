using System.Collections.Generic;
using CRPG.Core;

namespace CRPG.Dialogue
{
    /// <summary>Result of a CheckNode. Most CheckPerformer policies roll exactly one die and Success mirrors
    /// that roll; EntireParty rolls once per party member, so Rolls can have more than one entry.</summary>
    public sealed class DialogueCheckOutcome
    {
        public DialogueCheckOutcome(bool success, IReadOnlyList<RollResult> rolls)
        {
            Success = success;
            Rolls = rolls;
        }

        public bool Success { get; }
        public IReadOnlyList<RollResult> Rolls { get; }
    }
}

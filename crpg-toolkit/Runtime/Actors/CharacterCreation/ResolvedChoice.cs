using System.Collections.Generic;
using CRPG.Core;

namespace CRPG.Actors
{
    public sealed class ResolvedChoice
    {
        public ResolvedChoice(ChoiceRequirement requirement, IReadOnlyList<DefinitionId> selections)
        {
            Requirement = requirement;
            Selections = selections;
        }

        public ChoiceRequirement Requirement { get; }
        public IReadOnlyList<DefinitionId> Selections { get; }
    }
}

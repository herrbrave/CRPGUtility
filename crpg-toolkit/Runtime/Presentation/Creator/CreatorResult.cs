using System.Collections.Generic;
using System.Linq;
using CRPG.Actors;
using CRPG.Core;

namespace CRPG.Presentation.Creator
{
    /// <summary>What <see cref="CharacterCreatorModel.Build"/> hands back: a complete <see cref="CharacterBuildPlan"/> (race, class, background, scores,
    /// appearance and every level-1 choice resolved), and the class's starting kit (armor, weapon), which a plan cannot carry (it has no equipment) so
    /// the consumer equips it after building the character.</summary>
    public sealed class CreatorResult
    {
        public CreatorResult(CharacterBuildPlan plan, DefinitionId classId, IEnumerable<DefinitionId> startingItemIds)
        {
            Plan = plan;
            ClassId = classId;
            StartingItemIds = (startingItemIds ?? Enumerable.Empty<DefinitionId>()).ToArray();
        }

        public CharacterBuildPlan Plan { get; }
        public DefinitionId ClassId { get; }

        /// <summary>The item ids to equip, in kit order (armor first, then weapon); empty when the class has no kit.</summary>
        public IReadOnlyList<DefinitionId> StartingItemIds { get; }
    }
}

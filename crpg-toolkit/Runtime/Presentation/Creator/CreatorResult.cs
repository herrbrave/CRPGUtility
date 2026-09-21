using CRPG.Actors;
using CRPG.Core;

namespace CRPG.Presentation.Creator
{
    /// <summary>What <see cref="CharacterCreatorModel.Build"/> hands back: a complete <see cref="CharacterBuildPlan"/> (race, class,
    /// background, scores, appearance and every level-1 choice resolved), and the starter weapon item, which a plan cannot carry
    /// (it has no equipment) so the consumer equips it after building the character.</summary>
    public sealed class CreatorResult
    {
        public CreatorResult(CharacterBuildPlan plan, DefinitionId classId, DefinitionId weaponItemId)
        {
            Plan = plan;
            ClassId = classId;
            WeaponItemId = weaponItemId;
        }

        public CharacterBuildPlan Plan { get; }
        public DefinitionId ClassId { get; }

        /// <summary>The chosen starter weapon's item id; <see cref="DefinitionId.None"/> when the class offers none.</summary>
        public DefinitionId WeaponItemId { get; }
    }
}

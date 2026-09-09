using UnityEngine;

namespace CRPG.Actors
{
    /// <summary>A Martial Archetype, Divine Domain, Arcane Tradition, ... chosen via a ChoiceRequirement at
    /// whatever level the parent class introduces the choice. Same leveled-grant shape as ClassDefinition.</summary>
    [CreateAssetMenu(menuName = "CRPG Toolkit/Actors/Subclass Definition")]
    public sealed class SubclassDefinition : LeveledFeatureDefinition
    {
    }
}

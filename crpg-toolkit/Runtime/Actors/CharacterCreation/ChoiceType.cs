namespace CRPG.Actors
{
    /// <summary>What kind of thing a ChoiceRequirement is asking the player to pick. The same generic
    /// mechanism handles race choices, class proficiencies, fighting styles, spell selection, subclass
    /// choices, equipment packages, ability-score improvements, feats, and multiclass decisions (spec
    /// "Generic character choice system") -- no `if (race == Elf)` branches anywhere in engine code.</summary>
    public enum ChoiceType
    {
        SelectDefinition,
        SelectFeature,
        SelectSkill,
        SelectProficiency,
        SelectLanguage,
        SelectAbility,
        SelectSpell,
        SelectEquipment,
        SelectAbilityScore,
        SelectDamageType,
        SelectCreatureType,
        SelectTerrain,
        SelectNumber
    }
}

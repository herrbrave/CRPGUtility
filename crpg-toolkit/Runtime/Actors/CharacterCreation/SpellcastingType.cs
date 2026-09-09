namespace CRPG.Actors
{
    /// <summary>How a class's spell list becomes castable spells on a character. Known: a fixed set learned
    /// permanently via SelectSpell choices (Sorcerer/Bard/Warlock/Ranger). Prepared: a daily-swappable subset
    /// chosen from the class's entire SpellListAbilityIds pool (Wizard/Cleric/Druid/Paladin) -- see
    /// SpellPreparationService. Cantrips are always learned via SelectSpell regardless of this value.</summary>
    public enum SpellcastingType
    {
        None,
        Known,
        Prepared
    }
}

namespace CRPG.Core
{
    /// <summary>Derived stats for resolving a saving throw. Lives in CRPG.Core (not CRPG.Combat, where its
    /// default implementation does) for the same reason IAttackBonusRule/IArmorClassRule do: SavingThrowEffect
    /// lives in CRPG.Abilities, which can't reference CRPG.Combat without a circular dependency.</summary>
    public interface ISavingThrowRule
    {
        int ComputeSavingThrowBonus(RuntimeEntityId character, DefinitionId savingStatId);
        AdvantageState ComputeAdvantage(RuntimeEntityId character, DefinitionId savingStatId);

        /// <summary>True if the character automatically fails this saving throw without rolling (e.g.
        /// Paralyzed/Stunned/Unconscious auto-failing Strength and Dexterity saves) -- gathered the same way as
        /// Advantage/Disadvantage, via a RuleModifierOperation.AutoFailSave-tagged FeatureModifier under the
        /// same "SavingThrow:&lt;ability&gt;" query key.</summary>
        bool AutoFails(RuntimeEntityId character, DefinitionId savingStatId);

        /// <summary>SRD5's spell save DC formula: 8 + proficiency bonus + the caster's spellcasting ability
        /// modifier. Always assumes proficiency (a caster is always proficient in their own spell DC) -- unlike
        /// ComputeSavingThrowBonus, which only adds proficiency if the target is proficient in that save.</summary>
        int ComputeSpellSaveDC(RuntimeEntityId caster, DefinitionId spellcastingStatId);
    }
}

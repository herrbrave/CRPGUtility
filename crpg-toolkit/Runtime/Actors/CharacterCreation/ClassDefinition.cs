using System.Collections.Generic;
using CRPG.Core;
using UnityEngine;

namespace CRPG.Actors
{
    [CreateAssetMenu(menuName = "CRPG Toolkit/Actors/Class Definition")]
    public sealed class ClassDefinition : LeveledFeatureDefinition
    {
        [SerializeField] private DiceExpression hitDie;
        [SerializeField] private List<DefinitionId> primaryStatIds = new();
        [SerializeField] private List<DefinitionId> savingThrowProficiencyIds = new();
        [SerializeField] private DefinitionId spellcastingStatId;
        [SerializeField] private SpellcastingType spellcastingType = SpellcastingType.None;

        /// <summary>Only meaningful when <see cref="SpellcastingType"/> is Prepared -- character level (or
        /// half of it for a half-caster like Paladin) divided by this, rounded down, plus the spellcasting
        /// ability modifier, is how many spells can be prepared at once. See SpellPreparationService.</summary>
        [SerializeField] private int preparedSpellsLevelDivisor = 1;

        /// <summary>Only meaningful when <see cref="SpellcastingType"/> is Prepared -- the full pool of spells
        /// this class can choose to prepare from (e.g. "every Cleric spell"). Known-casters instead learn a
        /// fixed subset directly via SelectSpell choices, so they don't need this list.</summary>
        [SerializeField] private List<DefinitionId> spellListAbilityIds = new();

        public DiceExpression HitDie => hitDie;
        public IReadOnlyList<DefinitionId> PrimaryStatIds => primaryStatIds;
        public IReadOnlyList<DefinitionId> SavingThrowProficiencyIds => savingThrowProficiencyIds;
        public DefinitionId SpellcastingStatId => spellcastingStatId;
        public SpellcastingType SpellcastingType => spellcastingType;
        public int PreparedSpellsLevelDivisor => preparedSpellsLevelDivisor;
        public IReadOnlyList<DefinitionId> SpellListAbilityIds => spellListAbilityIds;
    }
}

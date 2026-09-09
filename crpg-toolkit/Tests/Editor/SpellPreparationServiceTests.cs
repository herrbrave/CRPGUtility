using System.Collections.Generic;
using CRPG.Actors;
using CRPG.Core;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    public class SpellPreparationServiceTests
    {
        private static readonly DefinitionId Wisdom = new("stat-wisdom");
        private static readonly DefinitionId CureWounds = new("spell-cure-wounds");
        private static readonly DefinitionId Guidance = new("spell-guidance");
        private static readonly DefinitionId Fireball = new("spell-fireball");

        private static ClassDefinition CreatePreparedCasterClass(int levelDivisor = 1, params DefinitionId[] spellList)
        {
            var classDefinition = TestDefinitionFactory.CreateClassDefinition("class-cleric", "Cleric", new DiceExpression(1, 8));
            TestDefinitionFactory.SetPrivateField(classDefinition, "spellcastingStatId", Wisdom);
            TestDefinitionFactory.SetPrivateField(classDefinition, "spellcastingType", SpellcastingType.Prepared);
            TestDefinitionFactory.SetPrivateField(classDefinition, "preparedSpellsLevelDivisor", levelDivisor);
            TestDefinitionFactory.SetPrivateField(classDefinition, "spellListAbilityIds", new List<DefinitionId>(spellList));
            return classDefinition;
        }

        [Test]
        public void MaxPreparedIsLevelPlusAbilityModifier()
        {
            var classDefinition = CreatePreparedCasterClass();
            var character = new CharacterState(RuntimeEntityId.NewId()) { Level = 5 };
            character.Stats.SetScore(Wisdom, 16); // +3 modifier

            Assert.AreEqual(8, SpellPreparationService.MaxPrepared(character, classDefinition)); // 5 + 3
        }

        [Test]
        public void MaxPreparedHalvesLevelForAHalfCaster()
        {
            var classDefinition = CreatePreparedCasterClass(levelDivisor: 2);
            var character = new CharacterState(RuntimeEntityId.NewId()) { Level = 5 };
            character.Stats.SetScore(Wisdom, 14); // +2 modifier

            Assert.AreEqual(4, SpellPreparationService.MaxPrepared(character, classDefinition)); // (5/2=2) + 2
        }

        [Test]
        public void MaxPreparedNeverGoesBelowOne()
        {
            var classDefinition = CreatePreparedCasterClass();
            var character = new CharacterState(RuntimeEntityId.NewId()) { Level = 1 };
            character.Stats.SetScore(Wisdom, 8); // -1 modifier, would otherwise be 0

            Assert.AreEqual(1, SpellPreparationService.MaxPrepared(character, classDefinition));
        }

        [Test]
        public void TryPrepareSucceedsWithinPoolAndLimit()
        {
            var classDefinition = CreatePreparedCasterClass(spellList: new[] { CureWounds, Guidance });
            var character = new CharacterState(RuntimeEntityId.NewId()) { Level = 5 };
            character.Stats.SetScore(Wisdom, 16); // max 8

            var result = SpellPreparationService.TryPrepare(character, classDefinition, new[] { CureWounds, Guidance });

            Assert.IsTrue(result);
            Assert.IsTrue(character.HasPrepared(CureWounds));
            Assert.IsTrue(character.HasPrepared(Guidance));
        }

        [Test]
        public void TryPrepareFailsWhenExceedingMax()
        {
            var classDefinition = CreatePreparedCasterClass(spellList: new[] { CureWounds, Guidance });
            var character = new CharacterState(RuntimeEntityId.NewId()) { Level = 1 };
            character.Stats.SetScore(Wisdom, 10); // max 1

            var result = SpellPreparationService.TryPrepare(character, classDefinition, new[] { CureWounds, Guidance });

            Assert.IsFalse(result);
            Assert.IsFalse(character.HasPrepared(CureWounds));
        }

        [Test]
        public void TryPrepareFailsWhenAbilityIsNotOnTheClassSpellList()
        {
            var classDefinition = CreatePreparedCasterClass(spellList: new[] { CureWounds });
            var character = new CharacterState(RuntimeEntityId.NewId()) { Level = 5 };
            character.Stats.SetScore(Wisdom, 16);

            var result = SpellPreparationService.TryPrepare(character, classDefinition, new[] { Fireball });

            Assert.IsFalse(result);
        }

        [Test]
        public void TryPrepareFailsWhenClassIsNotAPreparedCaster()
        {
            var classDefinition = TestDefinitionFactory.CreateClassDefinition("class-sorcerer", "Sorcerer", new DiceExpression(1, 6));
            TestDefinitionFactory.SetPrivateField(classDefinition, "spellcastingType", SpellcastingType.Known);
            var character = new CharacterState(RuntimeEntityId.NewId()) { Level = 5 };

            var result = SpellPreparationService.TryPrepare(character, classDefinition, new[] { CureWounds });

            Assert.IsFalse(result);
        }

        [Test]
        public void PrepareSpellsReplacesThePreviousSelectionEntirely()
        {
            var character = new CharacterState(RuntimeEntityId.NewId());
            character.PrepareSpells(new[] { CureWounds });
            Assert.IsTrue(character.HasPrepared(CureWounds));

            character.PrepareSpells(new[] { Guidance });

            Assert.IsFalse(character.HasPrepared(CureWounds));
            Assert.IsTrue(character.HasPrepared(Guidance));
        }
    }
}

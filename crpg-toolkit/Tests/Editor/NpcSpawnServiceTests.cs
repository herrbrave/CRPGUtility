using CRPG.Actors;
using CRPG.Core;
using CRPG.Rules;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    public class NpcSpawnServiceTests
    {
        private static readonly DefinitionId Strength = new("stat-strength");
        private static readonly DefinitionId Wisdom = new("stat-wisdom");

        [Test]
        public void SpawnSetsLevelAndAbilityScoresDirectly()
        {
            var rulesDatabase = new RulesDatabase();
            var npc = TestDefinitionFactory.CreateNpcDefinition("npc-goblin", "Goblin", level: 2,
                TestDefinitionFactory.CreateStatScoreEntry(Strength, 8), TestDefinitionFactory.CreateStatScoreEntry(Wisdom, 12));

            var character = NpcSpawnService.Spawn(npc, rulesDatabase, RuntimeEntityId.NewId());

            Assert.AreEqual(2, character.Level);
            Assert.AreEqual(8, character.Stats.GetScore(Strength));
            Assert.AreEqual(12, character.Stats.GetScore(Wisdom));
        }

        [Test]
        public void SpawnSetsSpellcastingStatIdDirectly()
        {
            var rulesDatabase = new RulesDatabase();
            var npc = TestDefinitionFactory.CreateNpcDefinition("npc-hexer", "Hexer");
            TestDefinitionFactory.SetPrivateField(npc, "spellcastingStatId", Wisdom);

            var character = NpcSpawnService.Spawn(npc, rulesDatabase, RuntimeEntityId.NewId());

            Assert.AreEqual(Wisdom, character.SpellcastingStatId);
        }

        [Test]
        public void SpawnGrantsProficienciesResourcesAndKnownAbilitiesDirectly()
        {
            var rulesDatabase = new RulesDatabase();
            var stealth = new DefinitionId("skill-stealth");
            var rageUses = TestDefinitionFactory.CreateResourceDefinition("resource-rage", "Rage");
            var shortSword = new DefinitionId("ability-shortsword-strike");

            var npc = TestDefinitionFactory.CreateNpcDefinition("npc-bandit", "Bandit");
            TestDefinitionFactory.SetPrivateField(npc, "proficiencies", new System.Collections.Generic.List<DefinitionId> { stealth });
            TestDefinitionFactory.SetPrivateField(npc, "resources", new System.Collections.Generic.List<ResourceGrant>
            {
                TestDefinitionFactory.CreateResourceGrant(rageUses, 2),
            });
            TestDefinitionFactory.SetPrivateField(npc, "abilityIds", new System.Collections.Generic.List<DefinitionId> { shortSword });

            var character = NpcSpawnService.Spawn(npc, rulesDatabase, RuntimeEntityId.NewId());

            Assert.IsTrue(character.HasProficiency(stealth));
            Assert.IsTrue(character.TryGetResource(rageUses.Id, out var rage));
            Assert.AreEqual(2, rage.Max);
            Assert.IsTrue(character.KnowsAbility(shortSword));
        }

        [Test]
        public void SpawnGrantsFeaturesAndTheirOwnResourceAndProficiencyGrants()
        {
            var rulesDatabase = new RulesDatabase();
            var darkvisionProficiency = new DefinitionId("sense-darkvision");
            var packTactics = TestDefinitionFactory.CreateFeatureDefinition("feature-pack-tactics", "Pack Tactics");
            TestDefinitionFactory.SetGrantedProficiencies(packTactics, darkvisionProficiency);
            rulesDatabase.Register(packTactics);

            var npc = TestDefinitionFactory.CreateNpcDefinition("npc-wolf", "Wolf");
            TestDefinitionFactory.SetPrivateField(npc, "featureIds", new System.Collections.Generic.List<DefinitionId> { packTactics.Id });

            var character = NpcSpawnService.Spawn(npc, rulesDatabase, RuntimeEntityId.NewId());

            Assert.IsTrue(character.HasFeature(packTactics.Id));
            Assert.IsTrue(character.HasProficiency(darkvisionProficiency));
        }

        [Test]
        public void SpawnAppliesAbilityScoreBonusesFromGrantedFeatures()
        {
            var rulesDatabase = new RulesDatabase();
            var strengthStat = TestDefinitionFactory.CreateStatDefinition("stat-strength", "Strength");
            rulesDatabase.Register(strengthStat);

            var bonus = TestDefinitionFactory.CreateFeatureModifier("stat-strength", RuleModifierOperation.Add, 4f);
            var monsterTrait = TestDefinitionFactory.CreateFeatureDefinition("feature-brutish", "Brutish", bonus);
            rulesDatabase.Register(monsterTrait);

            var npc = TestDefinitionFactory.CreateNpcDefinition("npc-ogre", "Ogre", abilityScores: TestDefinitionFactory.CreateStatScoreEntry(Strength, 15));
            TestDefinitionFactory.SetPrivateField(npc, "featureIds", new System.Collections.Generic.List<DefinitionId> { monsterTrait.Id });

            var character = NpcSpawnService.Spawn(npc, rulesDatabase, RuntimeEntityId.NewId());

            Assert.AreEqual(19, character.Stats.GetScore(Strength)); // 15 + 4
        }
    }
}

using System.Linq;
using CRPG.Actors;
using CRPG.Core;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    public class CharacterBuildTests
    {
        [Test]
        public void RefreshPendingChoicesFindsChoicesFromRaceClassAndBackground()
        {
            var rulesDatabase = new RulesDatabase();
            var subraceChoice = TestDefinitionFactory.CreateChoiceRequirement(
                "choose-subrace", "Choose your subrace", ChoiceType.SelectFeature, 1, new DefinitionId("feature-high-elf"));
            var raceFeature = TestDefinitionFactory.CreateFeatureDefinition("feature-elf", "Elf");
            TestDefinitionFactory.SetChoices(raceFeature, subraceChoice);
            rulesDatabase.Register(raceFeature);
            var race = TestDefinitionFactory.CreateRaceDefinition("race-elf", "Elf", raceFeature.Id);

            var plan = new CharacterBuildPlan { Race = race };
            var service = new CharacterBuildService(rulesDatabase);

            service.RefreshPendingChoices(plan);

            Assert.AreEqual(1, plan.PendingChoices.Count);
            Assert.AreEqual("choose-subrace", plan.PendingChoices[0].Id);
        }

        [Test]
        public void ResolvingAChoiceThatGrantsAFeatureSurfacesThatFeaturesOwnChoicesOnRefresh()
        {
            var rulesDatabase = new RulesDatabase();

            var cantripChoice = TestDefinitionFactory.CreateChoiceRequirement(
                "choose-cantrip", "Choose a cantrip", ChoiceType.SelectSpell, 1, new DefinitionId("spell-fire-bolt"));
            var highElfFeature = TestDefinitionFactory.CreateFeatureDefinition("feature-high-elf", "High Elf");
            TestDefinitionFactory.SetChoices(highElfFeature, cantripChoice);
            rulesDatabase.Register(highElfFeature);

            var subraceChoice = TestDefinitionFactory.CreateChoiceRequirement(
                "choose-subrace", "Choose your subrace", ChoiceType.SelectFeature, 1, highElfFeature.Id);
            var raceFeature = TestDefinitionFactory.CreateFeatureDefinition("feature-elf", "Elf");
            TestDefinitionFactory.SetChoices(raceFeature, subraceChoice);
            rulesDatabase.Register(raceFeature);
            var race = TestDefinitionFactory.CreateRaceDefinition("race-elf", "Elf", raceFeature.Id);

            var plan = new CharacterBuildPlan { Race = race };
            var service = new CharacterBuildService(rulesDatabase);
            service.RefreshPendingChoices(plan);

            plan.ResolveChoice("choose-subrace", new[] { highElfFeature.Id });
            service.RefreshPendingChoices(plan);

            Assert.AreEqual(1, plan.PendingChoices.Count);
            Assert.AreEqual("choose-cantrip", plan.PendingChoices[0].Id);
        }

        [Test]
        public void ResolveChoiceThrowsWhenSelectionCountDoesNotMatch()
        {
            var rulesDatabase = new RulesDatabase();
            var choice = TestDefinitionFactory.CreateChoiceRequirement(
                "choose-skill", "Choose a skill", ChoiceType.SelectSkill, 2, new DefinitionId("skill-stealth"), new DefinitionId("skill-athletics"));
            var feature = TestDefinitionFactory.CreateFeatureDefinition("feature-background", "Background Feature");
            TestDefinitionFactory.SetChoices(feature, choice);
            rulesDatabase.Register(feature);
            var background = TestDefinitionFactory.CreateBackgroundDefinition("background-test", "Test", feature.Id);

            var plan = new CharacterBuildPlan { Background = background };
            new CharacterBuildService(rulesDatabase).RefreshPendingChoices(plan);

            Assert.Throws<System.InvalidOperationException>(() => plan.ResolveChoice("choose-skill", new[] { new DefinitionId("skill-stealth") }));
        }

        [Test]
        public void BuildThrowsWhileChoicesArePending()
        {
            var rulesDatabase = new RulesDatabase();
            var choice = TestDefinitionFactory.CreateChoiceRequirement("choose-skill", "Choose a skill", ChoiceType.SelectSkill, 1, new DefinitionId("skill-stealth"));
            var feature = TestDefinitionFactory.CreateFeatureDefinition("feature-background", "Background Feature");
            TestDefinitionFactory.SetChoices(feature, choice);
            rulesDatabase.Register(feature);
            var background = TestDefinitionFactory.CreateBackgroundDefinition("background-test", "Test", feature.Id);

            var plan = new CharacterBuildPlan { Background = background };
            var service = new CharacterBuildService(rulesDatabase);
            service.RefreshPendingChoices(plan);

            Assert.Throws<System.InvalidOperationException>(() => service.Build(plan, RuntimeEntityId.NewId()));
        }

        [Test]
        public void BuildGrantsFeaturesStatsResourcesAndProficienciesFromEveryPart()
        {
            var rulesDatabase = new RulesDatabase();
            var strength = TestDefinitionFactory.CreateStatDefinition("stat-strength", "Strength");

            var rageResource = TestDefinitionFactory.CreateResourceDefinition("resource-rage", "Rage");
            var classFeature = TestDefinitionFactory.CreateFeatureDefinition("feature-rage", "Rage");
            TestDefinitionFactory.SetGrantedResources(classFeature, TestDefinitionFactory.CreateResourceGrant(rageResource, 2));
            rulesDatabase.Register(classFeature);
            var barbarian = TestDefinitionFactory.CreateClassDefinition(
                "class-barbarian", "Barbarian", new DiceExpression(1, 12),
                TestDefinitionFactory.CreateClassLevelGrant(1, classFeature.Id));

            var raceFeature = TestDefinitionFactory.CreateFeatureDefinition("feature-darkvision", "Darkvision");
            rulesDatabase.Register(raceFeature);
            var race = TestDefinitionFactory.CreateRaceDefinition("race-halforc", "Half-Orc", raceFeature.Id);

            var athletics = new DefinitionId("skill-athletics");
            var backgroundFeature = TestDefinitionFactory.CreateFeatureDefinition("feature-outlander", "Outlander");
            TestDefinitionFactory.SetGrantedProficiencies(backgroundFeature, athletics);
            rulesDatabase.Register(backgroundFeature);
            var background = TestDefinitionFactory.CreateBackgroundDefinition("background-outlander", "Outlander", backgroundFeature.Id);

            var plan = new CharacterBuildPlan { Race = race, Class = barbarian, Background = background, Level = 1 };
            plan.AbilityScores.SetScore(strength.Id, 16);

            var service = new CharacterBuildService(rulesDatabase);
            service.RefreshPendingChoices(plan);
            Assert.IsTrue(plan.IsComplete);

            var character = service.Build(plan, RuntimeEntityId.NewId());

            Assert.AreEqual(16, character.Stats.GetScore(strength.Id));
            Assert.IsTrue(character.HasFeature(classFeature.Id));
            Assert.IsTrue(character.HasFeature(raceFeature.Id));
            Assert.IsTrue(character.HasFeature(backgroundFeature.Id));
            Assert.IsTrue(character.HasProficiency(athletics));
            character.TryGetResource(rageResource.Id, out var rage);
            Assert.AreEqual(2, rage.Max);
        }

        [Test]
        public void BuildAppliesResolvedSelectSkillChoicesAsProficiencies()
        {
            var rulesDatabase = new RulesDatabase();
            var stealth = new DefinitionId("skill-stealth");
            var choice = TestDefinitionFactory.CreateChoiceRequirement("choose-skill", "Choose a skill", ChoiceType.SelectSkill, 1, stealth);
            var feature = TestDefinitionFactory.CreateFeatureDefinition("feature-background", "Background Feature");
            TestDefinitionFactory.SetChoices(feature, choice);
            rulesDatabase.Register(feature);
            var background = TestDefinitionFactory.CreateBackgroundDefinition("background-test", "Test", feature.Id);

            var plan = new CharacterBuildPlan { Background = background };
            var service = new CharacterBuildService(rulesDatabase);
            service.RefreshPendingChoices(plan);
            plan.ResolveChoice("choose-skill", new[] { stealth });

            var character = service.Build(plan, RuntimeEntityId.NewId());

            Assert.IsTrue(character.HasProficiency(stealth));
        }

        [Test]
        public void BuildGrantsProficiencyInTheClassesSavingThrows()
        {
            var rulesDatabase = new RulesDatabase();
            var strength = new DefinitionId("stat-strength");
            var constitution = new DefinitionId("stat-constitution");
            var wisdom = new DefinitionId("stat-wisdom");
            var classDefinition = TestDefinitionFactory.CreateClassDefinition("class-fighter", "Fighter", new DiceExpression(1, 10));
            TestDefinitionFactory.SetPrivateField(classDefinition, "savingThrowProficiencyIds", new System.Collections.Generic.List<DefinitionId> { strength, constitution });

            var plan = new CharacterBuildPlan { Class = classDefinition, Level = 1 };
            var service = new CharacterBuildService(rulesDatabase);

            var character = service.Build(plan, RuntimeEntityId.NewId());

            Assert.IsTrue(character.HasProficiency(strength));
            Assert.IsTrue(character.HasProficiency(constitution));
            Assert.IsFalse(character.HasProficiency(wisdom));
        }

        [Test]
        public void BuildSetsSpellcastingStatIdFromClass()
        {
            var rulesDatabase = new RulesDatabase();
            var intelligence = new DefinitionId("stat-intelligence");
            var wizard = TestDefinitionFactory.CreateClassDefinition("class-wizard", "Wizard", new DiceExpression(1, 6));
            TestDefinitionFactory.SetPrivateField(wizard, "spellcastingStatId", intelligence);

            var plan = new CharacterBuildPlan { Class = wizard, Level = 1 };
            var character = new CharacterBuildService(rulesDatabase).Build(plan, RuntimeEntityId.NewId());

            Assert.AreEqual(intelligence, character.SpellcastingStatId);
        }

        [Test]
        public void BuildAppliesResolvedSelectSpellChoicesAsKnownAbilities()
        {
            var rulesDatabase = new RulesDatabase();
            var fireBolt = new DefinitionId("spell-fire-bolt");
            var choice = TestDefinitionFactory.CreateChoiceRequirement("choose-cantrip", "Choose a cantrip", ChoiceType.SelectSpell, 1, fireBolt);
            var feature = TestDefinitionFactory.CreateFeatureDefinition("feature-wizard-cantrip", "Wizard Cantrip");
            TestDefinitionFactory.SetChoices(feature, choice);
            rulesDatabase.Register(feature);
            var background = TestDefinitionFactory.CreateBackgroundDefinition("background-test", "Test", feature.Id);

            var plan = new CharacterBuildPlan { Background = background };
            var service = new CharacterBuildService(rulesDatabase);
            service.RefreshPendingChoices(plan);
            plan.ResolveChoice("choose-cantrip", new[] { fireBolt });

            var character = service.Build(plan, RuntimeEntityId.NewId());

            Assert.IsTrue(character.KnowsAbility(fireBolt));
        }
    }
}

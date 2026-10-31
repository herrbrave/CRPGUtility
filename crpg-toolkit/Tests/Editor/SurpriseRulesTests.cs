using System.Collections.Generic;
using System.Linq;
using CRPG.Abilities;
using CRPG.Actors;
using CRPG.Combat;
using CRPG.Core;
using CRPG.Rules;
using CRPG.Rules.SRD5;
using NUnit.Framework;
using UnityEditor;

namespace CRPG.Tests.EditMode
{
    /// <summary>Surprise: who it falls on, what it does (no actions or reactions on the first turn) and that it ends when that creature's first turn does.</summary>
    public sealed class SurpriseRulesTests
    {
        private RulesDatabase rules;
        private CharacterRegistry characters;
        private GameEventBus events;
        private CharacterState hero, guard, sentry;
        private static DefinitionId Id(string value) => new(value);

        [SetUp]
        public void SetUp()
        {
            rules = new RulesDatabase();
            var provider = AssetDatabase.LoadAssetAtPath<SRD5RulesetProvider>("Packages/com.crpgtoolkit.core/Rules/SRD5e2014/Content/SRD5RulesetProvider.asset");
            rules.RegisterRange(provider.Content);
            characters = new CharacterRegistry(rules); events = new GameEventBus();
            hero = Make(); guard = Make(); sentry = Make();
        }

        private CharacterState Make()
        {
            var character = new CharacterState(RuntimeEntityId.NewId());
            character.AddResource(new ResourceState(Id("resource-action"), 1)); character.AddResource(new ResourceState(Id("resource-reaction"), 1));
            characters.Register(character);
            return character;
        }

        [Test]
        public void ACreatureIsSurprisedOnlyWhenItNoticedNoneOfTheThreats()
        {
            var second = Make();
            var surprised = SurpriseRules.Surprised(new[] { guard.Id, sentry.Id }, new[] { hero.Id, second.Id }, (observer, threat) => observer == sentry.Id && threat == second.Id);
            Assert.That(surprised, Is.EqualTo(new[] { guard.Id }), "the sentry noticed one of them");
            Assert.That(SurpriseRules.Surprised(new[] { guard.Id }, new RuntimeEntityId[0], (_, _) => false), Is.Empty, "nobody to be surprised by");
        }

        [Test]
        public void TheSurprisedConditionPreventsActionsAndIsAppliedOnceWithItsEvents()
        {
            var seen = new List<GameEvent>(); events.Subscribe(GameEvent.ConditionApplied, c => seen.Add(c.Event));
            SurpriseRules.Apply(characters, events, null, new[] { guard.Id, guard.Id });
            Assert.That(guard.HasCondition(SurpriseRules.SurprisedId), Is.True); Assert.That(guard.Conditions.Count(c => c.ConditionId == SurpriseRules.SurprisedId), Is.EqualTo(1)); Assert.That(seen, Has.Count.EqualTo(1));
            Assert.That(rules.Resolve<ConditionDefinition>(SurpriseRules.SurprisedId).PreventsActions, Is.True);

            var resolution = new AbilityResolutionService(characters, new DiceService(new FixedDie()), eventBus: events, rulesDatabase: rules);
            var dodge = rules.Resolve<AbilityDefinition>(Id("ability-dodge"));
            resolution.IsCombatActive = () => true;
            Assert.That(resolution.CanAttempt(guard.Id, dodge).IsValid, Is.False, "a surprised creature cannot act");
        }

        [Test]
        public void SurpriseEndsWhenTheCreaturesFirstTurnEnds_AndLeavesTheOthersAlone()
        {
            var budgets = new TurnResourceSet(new[] { rules.Resolve<ResourceDefinition>(Id("resource-action")), rules.Resolve<ResourceDefinition>(Id("resource-reaction")) });
            var encounter = new CombatEncounterService(characters, null, budgets, events);
            using var durations = new ConditionDurationService(encounter, characters, events);
            encounter.StartCombatWithExplicitOrder(new[] { (hero.Id, Id("friends")), (guard.Id, Id("foes")), (sentry.Id, Id("foes")) });
            SurpriseRules.Apply(characters, events, null, new[] { guard.Id });
            Assert.That(encounter.Encounter.CurrentTurn.Id, Is.EqualTo(hero.Id));
            encounter.EndTurn();   // hero's turn ends: the guard's turn begins
            Assert.That(encounter.Encounter.CurrentTurn.Id, Is.EqualTo(guard.Id)); Assert.That(guard.HasCondition(SurpriseRules.SurprisedId), Is.True, "still surprised during that first turn");
            encounter.EndTurn();
            Assert.That(guard.HasCondition(SurpriseRules.SurprisedId), Is.False, "gone once the turn has ended");
        }

        private sealed class FixedDie : IRandomSource { public int Next(int minInclusive, int maxExclusive) => minInclusive; }
    }
}

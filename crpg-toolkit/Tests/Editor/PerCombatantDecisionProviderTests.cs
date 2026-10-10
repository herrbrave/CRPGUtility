using System;
using System.Collections.Generic;
using CRPG.Abilities;
using CRPG.AI;
using CRPG.Core;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    public class PerCombatantDecisionProviderTests
    {
        private sealed class TaggedProvider : ITurnDecisionProvider
        {
            private readonly TurnDecision decision;
            public int Calls;
            public TaggedProvider(TurnDecision decision) => this.decision = decision;
            public TurnDecision GetDecision(TurnContext context) { Calls++; return decision; }
        }

        private static TurnContext ContextFor(RuntimeEntityId self) => new(self, Array.Empty<RuntimeEntityId>(), Array.Empty<RuntimeEntityId>());

        private static TurnDecision DecisionNamed(string abilityId) =>
            new(TestDefinitionFactory.CreateAbilityDefinition(abilityId, abilityId, TargetRelation.Enemy), new[] { RuntimeEntityId.NewId() }, 1f);

        [Test]
        public void EachCombatantIsRoutedToTheProviderOfItsOwnBehaviourSet()
        {
            var grunt = RuntimeEntityId.NewId();
            var archer = RuntimeEntityId.NewId();
            var gruntSet = TestDefinitionFactory.CreateBehaviorSetDefinition("set-grunt", "Grunt");
            var archerSet = TestDefinitionFactory.CreateBehaviorSetDefinition("set-archer", "Archer");
            var providers = new Dictionary<BehaviorSetDefinition, TaggedProvider>
            {
                [gruntSet] = new TaggedProvider(DecisionNamed("stab")),
                [archerSet] = new TaggedProvider(DecisionNamed("shoot")),
            };
            var brain = new PerCombatantDecisionProvider(id => id == grunt ? gruntSet : archerSet, set => providers[set]);

            Assert.AreEqual("stab", brain.GetDecision(ContextFor(grunt)).Ability.Id.ToString());
            Assert.AreEqual("shoot", brain.GetDecision(ContextFor(archer)).Ability.Id.ToString());
        }

        [Test]
        public void ACombatantWithNoBehaviourSetGetsNoDecisionAndNoProviderIsBuilt()
        {
            var built = 0;
            var brain = new PerCombatantDecisionProvider(_ => null, _ => { built++; return new TaggedProvider(null); });

            Assert.IsNull(brain.GetDecision(ContextFor(RuntimeEntityId.NewId())));
            Assert.AreEqual(0, built);
        }

        [Test]
        public void EachDistinctSetBuildsItsProviderOnceAndReusesItAcrossCombatantsAndTurns()
        {
            var set = TestDefinitionFactory.CreateBehaviorSetDefinition("set-grunt", "Grunt");
            var built = 0;
            var provider = new TaggedProvider(DecisionNamed("stab"));
            var brain = new PerCombatantDecisionProvider(_ => set, _ => { built++; return provider; });

            brain.GetDecision(ContextFor(RuntimeEntityId.NewId()));
            brain.GetDecision(ContextFor(RuntimeEntityId.NewId()));
            brain.GetDecision(ContextFor(RuntimeEntityId.NewId()));

            Assert.AreEqual(1, built);
            Assert.AreEqual(3, provider.Calls);
        }

        [Test]
        public void NullCallbacksAreRejected()
        {
            Assert.Throws<ArgumentNullException>(() => new PerCombatantDecisionProvider(null, _ => null));
            Assert.Throws<ArgumentNullException>(() => new PerCombatantDecisionProvider(_ => null, null));
        }
    }
}

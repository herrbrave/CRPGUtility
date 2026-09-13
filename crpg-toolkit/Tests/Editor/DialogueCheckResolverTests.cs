using CRPG.Actors;
using CRPG.Core;
using CRPG.Dialogue;
using CRPG.Party;
using CRPG.Rules;
using CRPG.Rules.SRD5;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    public class DialogueCheckResolverTests
    {
        [Test]
        public void BestPartyMemberPicksTheHighestGatheredModifierForTheQueryKey()
        {
            var rulesDatabase = new RulesDatabase();
            var registry = new CharacterRegistry();

            var silverTongue = TestDefinitionFactory.CreateFeatureModifier("Skill:Persuasion", RuleModifierOperation.Add, 5f);
            var goodFeature = TestDefinitionFactory.CreateFeatureDefinition("feature-silver-tongue", "Silver Tongue", silverTongue);
            rulesDatabase.Register(goodFeature);

            var bruteTalk = TestDefinitionFactory.CreateFeatureModifier("Skill:Persuasion", RuleModifierOperation.Add, 1f);
            var weakFeature = TestDefinitionFactory.CreateFeatureDefinition("feature-brute-talk", "Brute Talk", bruteTalk);
            rulesDatabase.Register(weakFeature);

            var smoothTalker = new CharacterState(RuntimeEntityId.NewId());
            smoothTalker.GrantFeature(goodFeature.Id);
            var bruiser = new CharacterState(RuntimeEntityId.NewId());
            bruiser.GrantFeature(weakFeature.Id);
            registry.Register(smoothTalker);
            registry.Register(bruiser);

            var party = new PartyState(new[] { bruiser.Id, smoothTalker.Id });

            var checkers = DialogueCheckResolver.ResolveCheckers(
                CheckPerformer.BestPartyMember, RuntimeEntityId.NewId(), party, registry,
                new DefaultSkillCheckRule(registry, rulesDatabase), "Skill:Persuasion");

            Assert.AreEqual(1, checkers.Count);
            Assert.AreEqual(smoothTalker.Id, checkers[0]);
        }

        [Test]
        public void ActiveCharacterPolicyReturnsThePartysActiveCharacterRegardlessOfModifiers()
        {
            var rulesDatabase = new RulesDatabase();
            var registry = new CharacterRegistry();

            var a = new CharacterState(RuntimeEntityId.NewId());
            var b = new CharacterState(RuntimeEntityId.NewId());
            registry.Register(a);
            registry.Register(b);

            var party = new PartyState(new[] { a.Id, b.Id });
            party.Select(b.Id);

            var checkers = DialogueCheckResolver.ResolveCheckers(
                CheckPerformer.ActiveCharacter, RuntimeEntityId.NewId(), party, registry, null, "Skill:Persuasion");

            Assert.AreEqual(1, checkers.Count);
            Assert.AreEqual(b.Id, checkers[0]);
        }

        [Test]
        public void SpecificSpeakerPolicyReturnsTheNpcRatherThanAnyPartyMember()
        {
            var rulesDatabase = new RulesDatabase();
            var registry = new CharacterRegistry();
            var party = new PartyState(new[] { RuntimeEntityId.NewId() });
            var npc = RuntimeEntityId.NewId();

            var checkers = DialogueCheckResolver.ResolveCheckers(
                CheckPerformer.SpecificSpeaker, npc, party, registry, null, "Skill:Persuasion");

            Assert.AreEqual(1, checkers.Count);
            Assert.AreEqual(npc, checkers[0]);
        }

        [Test]
        public void EntirePartyPolicyReturnsEveryPartyMember()
        {
            var rulesDatabase = new RulesDatabase();
            var registry = new CharacterRegistry();
            var a = RuntimeEntityId.NewId();
            var b = RuntimeEntityId.NewId();
            var party = new PartyState(new[] { a, b });

            var checkers = DialogueCheckResolver.ResolveCheckers(
                CheckPerformer.EntireParty, RuntimeEntityId.NewId(), party, registry, null, "Skill:Persuasion");

            Assert.AreEqual(2, checkers.Count);
            Assert.AreEqual(a, checkers[0]);
            Assert.AreEqual(b, checkers[1]);
        }

        [Test]
        public void BestPartyMemberWithNoSkillCheckRuleThrows()
        {
            var registry = new CharacterRegistry();
            var member = new CharacterState(RuntimeEntityId.NewId());
            registry.Register(member);
            var party = new PartyState(new[] { member.Id });

            Assert.Throws<System.InvalidOperationException>(() => DialogueCheckResolver.ResolveCheckers(
                CheckPerformer.BestPartyMember, RuntimeEntityId.NewId(), party, registry, null, "Skill:Persuasion"));
        }
    }
}

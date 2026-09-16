using System.Linq;
using CRPG.Abilities;
using CRPG.Actors;
using CRPG.Combat;
using CRPG.Core;
using CRPG.Dialogue;
using CRPG.Gameplay;
using CRPG.Inventory;
using CRPG.Party;
using CRPG.Rules;
using CRPG.Tests.Fixtures.AlternateRuleset;
using NUnit.Framework;
using UnityEngine;

namespace CRPG.Tests.EditMode
{
    /// <summary>
    /// Task 5.1: proves the ruleset seam Task 4.1 built is genuinely swappable, not just organizationally
    /// separate -- by running the real gameplay paths (DialogueRunner.PerformCheck, AbilityResolutionService,
    /// GameSessionHost.StartSession) against a second, small, non-d20 ruleset
    /// (<see cref="AlternateRulesetProvider"/>) instead of only ever exercising them through SRD5. An earlier
    /// draft of this file only asserted registration (that a die/resource existed), not actual gameplay
    /// behavior; see the approved plan for the review that caught this.
    /// </summary>
    public class RulesetContractTests
    {
        // --- Test 1: the non-d20 check, proven through the real gameplay path (DialogueRunner.PerformCheck),
        // not just by reading CheckDie back off the registered policy. Mirrors DialogueRunnerTests.cs's own
        // PerformCheckSucceeds/FailsAndBranches tests, substituting AlternateCheckRule for DefaultSkillCheckRule
        // and a 3-die queue for a single d20 value. ---

        [Test]
        public void AlternateCheckRollsThroughDialogueRunnerAsThreeSixSidedDice()
        {
            var rulesDatabase = new RulesDatabase();
            var characters = new CharacterRegistry();
            var inventory = new PartyInventoryState();
            var timeline = new Timeline();
            var npc = RuntimeEntityId.NewId();

            var pc = new CharacterState(RuntimeEntityId.NewId());
            characters.Register(pc);
            var party = new PartyState(new[] { pc.Id });
            party.Select(pc.Id);

            var alternateRule = new AlternateCheckRule(characters, rulesDatabase);

            // Diagnostic only -- the real proof is the roll below, not this property read.
            Assert.AreEqual(3, alternateRule.CheckDie.Count);
            Assert.AreEqual(6, alternateRule.CheckDie.Sides);

            var successOutcome = RunCheck(
                alternateRule, party, characters, rulesDatabase, timeline, inventory, npc,
                difficultyClass: 15, queuedDice: new[] { 4, 5, 6 }, out var successRunnerNode, out var successNode);

            Assert.AreEqual(3, successOutcome.Rolls[0].Dice.Count(d => d.Kept));
            Assert.IsTrue(successOutcome.Rolls[0].Dice.Where(d => d.Kept).All(d => d.Value is >= 1 and <= 6));
            Assert.AreEqual(4 + 5 + 6 + 2, successOutcome.Rolls[0].Total); // +2 is AlternateCheckRule's flat base bonus
            Assert.IsTrue(successOutcome.Success);
            Assert.AreSame(successNode, successRunnerNode);

            var failureOutcome = RunCheck(
                alternateRule, party, characters, rulesDatabase, timeline, inventory, npc,
                difficultyClass: 20, queuedDice: new[] { 4, 5, 6 }, out var failureRunnerNode, out _);

            Assert.AreEqual(17, failureOutcome.Rolls[0].Total);
            Assert.IsFalse(failureOutcome.Success);
            Assert.AreNotSame(successNode, failureRunnerNode);
        }

        private static DialogueCheckOutcome RunCheck(
            ISkillCheckRule rule, PartyState party, ICharacterRegistry characters, IRulesDatabase rulesDatabase,
            ITimeline timeline, PartyInventoryState inventory, RuntimeEntityId npc, int difficultyClass, int[] queuedDice,
            out DialogueNode currentNode, out DialogueNode successNodeOut)
        {
            var success = DialogueTestFactory.CreateEnd("success");
            var failure = DialogueTestFactory.CreateEnd("failure");
            var check = DialogueTestFactory.CreateCheck("start", "Skill:Persuasion", difficultyClass, CheckPerformer.ActiveCharacter, "success", "failure");
            var dialogue = DialogueTestFactory.CreateDialogue("dlg-test", "start", check, success, failure);

            var random = new TestRandomSource();
            random.EnqueueResults(queuedDice);
            var context = new DialogueContext(npc, party, characters, rulesDatabase, timeline, inventory, new DiceService(random), rule);
            var runner = new DialogueRunner(dialogue, context);

            var outcome = runner.PerformCheck();
            currentNode = runner.CurrentNode;
            successNodeOut = success;
            return outcome;
        }

        // --- Test 2: the alternate action economy, proven as a real economy (different capacities, different
        // per-ability costs, real rejection, real restore) -- not one resource renamed with one ability costing
        // all of it. AbilityResolutionService.Execute takes the AbilityDefinition instance directly, so no
        // RulesDatabase/session is needed to exercise it. ---

        [Test]
        public void AlternateResourceEconomySupportsDifferentCostsAndRejectsOverspend()
        {
            using var provider = new AlternateRulesetProvider();

            var characters = new CharacterRegistry();
            var character = new CharacterState(RuntimeEntityId.NewId());
            characters.Register(character);

            var actionPoints = new ResourceState(AlternateRulesetProvider.ActionPointsResourceId, max: 3);
            character.AddResource(actionPoints);

            var service = new AbilityResolutionService(characters, new DiceService(new SeededRandomSource(1)));

            var quickJabResult = service.Execute(character.Id, provider.QuickJab, new[] { character.Id });
            Assert.IsTrue(quickJabResult.Succeeded);
            Assert.AreEqual(2, actionPoints.Current);

            var heavyJabResult = service.Execute(character.Id, provider.HeavyJab, new[] { character.Id });
            Assert.IsTrue(heavyJabResult.Succeeded);
            Assert.AreEqual(0, actionPoints.Current);

            var overspendResult = service.Execute(character.Id, provider.QuickJab, new[] { character.Id });
            Assert.AreEqual(AbilityExecutionOutcome.Rejected, overspendResult.Outcome);
            Assert.AreEqual(AbilityRejectionReason.InsufficientResources, overspendResult.RejectionReason);
            Assert.AreEqual(0, actionPoints.Current); // rejection is zero-mutation

            new TurnResourceSet(new[] { provider.ActionPoints }).RestoreFull(character);
            Assert.AreEqual(3, actionPoints.Current);
        }

        // --- Test 3: RulesetId itself is opaque to session composition and gameplay outcomes -- the behavioral
        // half of the guarantee RulesetAssemblyDependencyTests proves at compile time (that no generic assembly
        // even references SRD5). Two AlternateRulesetProvider instances differing only in RulesetId run through
        // the real GameSessionHost.StartSession path plus the check/economy steps above; identical inputs must
        // produce identical outcomes. ---

        [Test]
        public void RulesetIdIsOpaqueToSessionComposition()
        {
            using var providerA = new AlternateRulesetProvider("test-alternate-a");
            using var providerB = new AlternateRulesetProvider("totally-different-ruleset-id");

            Assert.AreNotEqual(providerA.RulesetId, providerB.RulesetId); // the ids really do differ

            var (checkTotalA, resourceAfterTwoSpendsA) = RunAgainstProvider(providerA);
            var (checkTotalB, resourceAfterTwoSpendsB) = RunAgainstProvider(providerB);

            Assert.AreEqual(checkTotalA, checkTotalB);
            Assert.AreEqual(resourceAfterTwoSpendsA, resourceAfterTwoSpendsB);
        }

        private static (double checkTotal, int resourceAfterTwoSpends) RunAgainstProvider(AlternateRulesetProvider provider)
        {
            var go = new GameObject("RulesetContractTests_Host");
            try
            {
                var host = go.AddComponent<GameSessionHost>();
                var session = host.StartSession(provider, new DiceService(new SeededRandomSource(1)), new[] { typeof(ISkillCheckRule) });

                var skillCheckRule = session.Policies.Get<ISkillCheckRule>();
                var random = new TestRandomSource();
                random.EnqueueResults(4, 5, 6);
                var roll = new DiceService(random).Roll(new RollRequest(skillCheckRule.CheckDie, RollPurpose.AbilityCheck));

                var character = new CharacterState(RuntimeEntityId.NewId());
                session.Characters.Register(character);
                character.AddResource(new ResourceState(AlternateRulesetProvider.ActionPointsResourceId, max: 3));

                var service = new AbilityResolutionService(session.Characters, new DiceService(new SeededRandomSource(1)));
                service.Execute(character.Id, provider.QuickJab, new[] { character.Id });
                service.Execute(character.Id, provider.HeavyJab, new[] { character.Id });
                character.TryGetResource(AlternateRulesetProvider.ActionPointsResourceId, out var resource);

                return (roll.Total, resource.Current);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using CRPG.Abilities;
using CRPG.AI;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Presentation.Animation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VikingRaid.Game;

namespace VikingRaid.Tests.EditMode
{
    /// <summary>The authored content is complete and self-consistent. Each rule here came out of the Task 2 design review (turn resources are only refilled,
    /// never granted; a missing ability score reads as -5; an ability the NPC does not list is rejected at cast time; an unresolvable AI input key silently
    /// scores 0), so each is a thing that fails silently in play.</summary>
    public class ContentIntegrityTests
    {
        private ContentFixture content;

        [SetUp]
        public void SetUp() => content = ContentFixture.Load();

        [Test]
        public void EveryDesignedUnitHasAnNpcDefinition()
        {
            foreach (var unit in GameDesign.Units) Assert.IsTrue(content.Npcs.ContainsKey(unit.NpcId), $"no NpcDefinition for {unit.Id}");
            Assert.AreEqual(GameDesign.Units.Length, content.Npcs.Count, "NpcDefinitions the design does not know about");
        }

        [Test]
        public void EveryNpcOwnsHitPointsAndAllFourTurnResources()
        {
            // TurnResourceSet.RestoreFull only refills what a character already owns; nothing grants Action/BonusAction/Reaction/Movement.
            foreach (var unit in GameDesign.Units)
            {
                var granted = content.Npcs[unit.NpcId].Resources.ToDictionary(r => r.Resource.Id.Value, r => r.MaxAmount);
                foreach (var id in new[] { GameDesign.Hp, GameDesign.Action, GameDesign.BonusAction, GameDesign.Reaction, GameDesign.Movement })
                    Assert.IsTrue(granted.ContainsKey(id), $"{unit.Id} is never granted {id}");
                Assert.AreEqual(unit.Hp, granted[GameDesign.Hp], $"{unit.Id} hit points");
                Assert.AreEqual(unit.Movement, granted[GameDesign.Movement], $"{unit.Id} movement");
                Assert.AreEqual(1, granted[GameDesign.Action]);
                Assert.AreEqual(1, granted[GameDesign.BonusAction]);
            }
        }

        [Test]
        public void EveryNpcAuthorsAllSixAbilityScoresAsDesigned()
        {
            // a missing stat reads 0 = a -5 modifier (AC 5, -5 to hit)
            foreach (var unit in GameDesign.Units)
            {
                var scores = content.Npcs[unit.NpcId].AbilityScores.ToDictionary(s => s.StatId.Value, s => s.Score);
                Assert.AreEqual(6, scores.Count, $"{unit.Id} must author all six scores");
                Assert.AreEqual(unit.Str, scores[GameDesign.Str]); Assert.AreEqual(unit.Dex, scores[GameDesign.Dex]); Assert.AreEqual(unit.Con, scores[GameDesign.Con]);
                Assert.AreEqual(unit.Int, scores[GameDesign.Int]); Assert.AreEqual(unit.Wis, scores[GameDesign.Wis]); Assert.AreEqual(unit.Cha, scores[GameDesign.Cha]);
            }
        }

        [Test]
        public void EveryAbilityAnNpcMayUseResolvesAndIsInItsOwnAbilityList()
        {
            foreach (var unit in GameDesign.Units)
            {
                var npc = content.Npcs[unit.NpcId];
                CollectionAssert.AreEquivalent(unit.Abilities, npc.AbilityIds.Select(a => a.Value).ToList(), $"{unit.Id} abilityIds");
                foreach (var id in npc.AbilityIds) content.Resolve<AbilityDefinition>(id.Value);
            }
        }

        [Test]
        public void EveryAbilityCostsActionOrBonusActionAndTheNpcOwnsEveryCostResource()
        {
            // an ability with no turn-economy cost repeats up to 8 times per AI turn
            foreach (var unit in GameDesign.Units)
            {
                var owned = new HashSet<string>(content.Npcs[unit.NpcId].Resources.Select(r => r.Resource.Id.Value));
                foreach (var abilityId in unit.Abilities)
                {
                    var ability = content.Resolve<AbilityDefinition>(abilityId);
                    var costIds = ability.Costs.Select(c => c.Resource.Id.Value).ToList();
                    Assert.IsTrue(costIds.Contains(GameDesign.Action) ^ costIds.Contains(GameDesign.BonusAction), $"{abilityId} must cost exactly one of Action / Bonus Action ({string.Join(",", costIds)})");
                    foreach (var cost in costIds) Assert.IsTrue(owned.Contains(cost), $"{unit.Id} uses {abilityId} but owns no {cost}");
                }
            }
        }

        [Test]
        public void EveryNpcFeatureResolvesInTheRulesetAndMultiattackIsOnlyOnTheTwoBosses()
        {
            // an unresolvable featureId is silently skipped at spawn: the NPC would simply have no armor
            foreach (var unit in GameDesign.Units)
            {
                var npc = content.Npcs[unit.NpcId];
                foreach (var featureId in npc.FeatureIds) content.Resolve<FeatureDefinition>(featureId.Value);
                Assert.AreEqual(unit.Multiattack, npc.FeatureIds.Any(f => f.Value == GameDesign.MultiattackFeature), $"{unit.Id} multiattack");
            }
        }

        [Test]
        public void EveryNpcHasItsPrefabAndTheShootCueAndEveryAbilityCueExistInThatPrefabsAnimationProfile()
        {
            foreach (var unit in GameDesign.Units)
            {
                var npc = content.Npcs[unit.NpcId];
                Assert.IsNotNull(npc.VisualPrefab, $"{unit.Id} has no visual prefab");
                Assert.AreEqual(unit.Prefab, AssetDatabase.GetAssetPath(npc.VisualPrefab), $"{unit.Id} prefab path");
                var animator = npc.VisualPrefab.GetComponent<CharacterPlayableAnimator>();
                Assert.IsNotNull(animator, $"{unit.Id}'s prefab has no CharacterPlayableAnimator");
                var profile = ContentFixture.Field<CharacterAnimationProfile>(animator, "profile");
                Assert.IsNotNull(profile, $"{unit.Id}'s animator has no profile");

                foreach (var abilityId in unit.Abilities)
                {
                    var cue = content.Resolve<AbilityDefinition>(abilityId).AnimationCueId;
                    if (!cue.IsValid) continue; // buffs and the shipped Second Wind have no cue
                    Assert.IsTrue(profile.TryGetCue(cue, out _), $"{unit.Id}: ability {abilityId} asks for cue '{cue}' which the prefab's profile lacks");
                }
            }
        }

        [Test]
        public void EveryGoblinBrainResolvesWithAbilitiesTheUnitsUsingItActuallyKnow()
        {
            foreach (var brain in GameDesign.Brains)
            {
                var set = content.Resolve<BehaviorSetDefinition>(brain.Id);
                Assert.IsNotEmpty(set.BehaviorIds);
                var users = GameDesign.Goblins.Where(u => u.Brain == brain.Id).ToList();
                Assert.IsNotEmpty(users, $"nobody uses brain {brain.Id}");

                foreach (var behaviorId in set.BehaviorIds)
                {
                    var behavior = content.Resolve<BehaviorDefinition>(behaviorId.Value);
                    if (behavior.Kind == BehaviorKind.UseAbility)
                    {
                        content.Resolve<AbilityDefinition>(behavior.AbilityId.Value);
                        foreach (var user in users) CollectionAssert.Contains(user.Abilities, behavior.AbilityId.Value, $"{user.Id} uses brain {brain.Id} but does not know {behavior.AbilityId}");
                    }
                    else
                    {
                        Assert.AreEqual(GameDesign.Movement, behavior.MovementResourceId.Value);
                    }
                }
            }

            foreach (var goblin in GameDesign.Goblins) Assert.IsNotNull(goblin.Brain, $"{goblin.Id} has no brain");
        }

        [Test]
        public void EveryConsiderationInputKeyResolvesToARealInput()
        {
            // ConsiderationInputRegistry returns 0 for an unknown key: a typo reads as "every target is dead" and vetoes every attack
            var known = new HashSet<string> { "Distance", "RandomChance" };
            foreach (var brain in GameDesign.Brains)
            {
                foreach (var behaviorId in content.Resolve<BehaviorSetDefinition>(brain.Id).BehaviorIds)
                {
                    var behavior = content.Resolve<BehaviorDefinition>(behaviorId.Value);
                    Assert.IsNotEmpty(behavior.Considerations, $"{behaviorId} has no considerations (an empty list scores 0)");
                    foreach (var consideration in behavior.Considerations)
                    {
                        var key = consideration.InputKey;
                        var ok = known.Contains(key);
                        foreach (var prefix in new[] { "SelfResourcePercent:", "TargetResourcePercent:" })
                            if (key.StartsWith(prefix)) ok = content.Db.TryResolve<ResourceDefinition>(new DefinitionId(key.Substring(prefix.Length)), out _);
                        Assert.IsTrue(ok, $"{behaviorId}: input key '{key}' does not resolve");
                    }
                }
            }
        }

        [Test]
        public void TheHpConsiderationIsAPureZeroHpVetoOnEveryBehaviour()
        {
            // (0,0),(0.01,1),(1,1): a downed target scores 0, a living one scores 1 -- NEVER below ~0.73 or Attack would lose to Move (compensation factor)
            foreach (var brain in GameDesign.Brains)
            foreach (var behaviorId in content.Resolve<BehaviorSetDefinition>(brain.Id).BehaviorIds)
            {
                var behavior = content.Resolve<BehaviorDefinition>(behaviorId.Value);
                var hp = behavior.Considerations.Single(c => c.InputKey == "TargetResourcePercent:" + GameDesign.Hp);
                Assert.AreEqual(0f, hp.Curve.Evaluate(0f), 1e-4f, $"{behaviorId}: a 0-HP target must score 0");
                foreach (var living in new[] { 0.05f, 0.2f, 0.5f, 1f }) Assert.AreEqual(1f, hp.Curve.Evaluate(living), 1e-3f, $"{behaviorId}: a target at {living:P0} HP must not be penalised");
            }
        }

        [Test]
        public void TheRecklessConditionGrantsAdvantageToTheUserAndToAttackersWhichIsTheOppositeOfTheShippedOne()
        {
            var ours = content.Resolve<ConditionDefinition>(GameDesign.RecklessCondition);
            var byKey = ours.Modifiers.ToDictionary(m => m.QueryKey, m => m.ToRuleModifier("t").Operation);
            Assert.AreEqual(CRPG.Rules.RuleModifierOperation.Advantage, byKey["AttackBonus"]);
            Assert.AreEqual(CRPG.Rules.RuleModifierOperation.Advantage, byKey["AttacksAgainst"], "attackers must get ADVANTAGE against a reckless character (the shipped condition gives disadvantage)");
        }
    }
}

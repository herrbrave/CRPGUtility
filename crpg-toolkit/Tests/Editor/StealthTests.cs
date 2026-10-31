using System;
using System.Collections.Generic;
using System.Linq;
using CRPG.Abilities;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Rules;
using CRPG.Rules.SRD5;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CRPG.Tests.EditMode
{
    /// <summary>Hide under the SRD: who it can be tried against (not what sees you clearly), the Stealth check against passive Perception, what a hider gets (unseen-attacker advantage, the AI losing track) and what gives it away (an attack, a hostile cast, damage, walking into the open).</summary>
    public sealed class StealthTests
    {
        private sealed class FixedRandom : IRandomSource
        {
            public int Value = 10;
            public int Next(int minInclusive, int maxExclusive) => Math.Clamp(Value, minInclusive, maxExclusive - 1);
        }

        private sealed class FakeFactions : IFactionProvider
        {
            public readonly Dictionary<RuntimeEntityId, DefinitionId> Map = new();
            public bool TryGetFaction(RuntimeEntityId entity, out DefinitionId faction) => Map.TryGetValue(entity, out faction);
        }

        private sealed class FakeLight : ILightProvider
        {
            public Func<Vector3, LightLevel> Level = _ => LightLevel.Bright;
            public LightLevel LightAt(Vector3 point) => Level(point);
        }

        private RulesDatabase rules;
        private CharacterRegistry characters;
        private CharacterState hider, guard, watcher;
        private FakePositionProvider positions;
        private FakeLineOfSightProvider sight;
        private FakeLight light;
        private FakeFactions factions;
        private FixedRandom random;
        private GameEventBus events;
        private Srd5PerceptionQuery perception;
        private StealthRules stealth;
        private DefaultAttackBonusRule attacks;
        private readonly List<UnityEngine.Object> owned = new();
        private static DefinitionId Id(string value) => new(value);

        [SetUp]
        public void SetUp()
        {
            rules = new RulesDatabase();
            var provider = AssetDatabase.LoadAssetAtPath<SRD5RulesetProvider>("Packages/com.crpgtoolkit.core/Rules/SRD5e2014/Content/SRD5RulesetProvider.asset");
            Assert.That(provider, Is.Not.Null);
            rules.RegisterRange(provider.Content);
            characters = new CharacterRegistry(rules);
            hider = Make(dexterity: 14); guard = Make(); watcher = Make();
            positions = new FakePositionProvider(); positions.Set(hider.Id, Vector3.zero); positions.Set(guard.Id, new Vector3(0f, 0f, 8f)); positions.Set(watcher.Id, new Vector3(8f, 0f, 0f));
            sight = new FakeLineOfSightProvider(); light = new FakeLight(); random = new FixedRandom(); events = new GameEventBus();
            factions = new FakeFactions();
            factions.Map[hider.Id] = Id("party"); factions.Map[guard.Id] = Id("foes"); factions.Map[watcher.Id] = Id("foes");
            perception = new Srd5PerceptionQuery(characters, rules, positions, sight, light: light);
            stealth = new StealthRules(characters, rules, events, perception, new SkillAwareCheckRule(characters, rules), new DiceService(random), factions, new[] { hider.Id, guard.Id, watcher.Id });
            attacks = new DefaultAttackBonusRule(characters, rules) { Perception = perception, Stealth = stealth };
        }

        [TearDown]
        public void TearDown()
        {
            stealth?.Dispose();
            foreach (var value in owned) UnityEngine.Object.DestroyImmediate(value);
            owned.Clear();
        }

        private CharacterState Make(int dexterity = 10, int wisdom = 10)
        {
            var character = new CharacterState(RuntimeEntityId.NewId());
            character.Stats.SetScore(Id("stat-dexterity"), dexterity); character.Stats.SetScore(Id("stat-wisdom"), wisdom);
            character.AddResource(new ResourceState(Id("resource-hp"), 10));
            characters.Register(character);
            return character;
        }

        private void DarkAroundTheHider() => light.Level = p => Vector3.Distance(p, Vector3.zero) < 1f ? LightLevel.Dark : LightLevel.Bright;

        // can I hide at all

        [Test]
        public void HidingIsRefusedWithAReasonWhileEveryEnemyCanSeeYouClearly()
        {
            Assert.That(stealth.CanAttemptHide(hider.Id, out var reason), Is.False);
            StringAssert.Contains("can see you clearly", reason);
        }

        [Test]
        public void HidingIsPossibleInDarknessBehindCoverAndFromABlindEnemy()
        {
            DarkAroundTheHider();
            Assert.That(stealth.CanAttemptHide(hider.Id, out _), Is.True, "heavily obscured");
            light.Level = _ => LightLevel.Bright; sight.AlwaysBlocked = true;
            Assert.That(stealth.CanAttemptHide(hider.Id, out _), Is.True, "behind total cover");
            sight.AlwaysBlocked = false;
            Assert.That(stealth.CanAttemptHide(hider.Id, out _), Is.False);
            guard.ApplyCondition(new ConditionInstance(Id("condition-blinded"), guard.Id, guard.Id)); watcher.ApplyCondition(new ConditionInstance(Id("condition-blinded"), watcher.Id, watcher.Id));
            Assert.That(stealth.CanAttemptHide(hider.Id, out _), Is.True, "everyone who could see is blind");
        }

        [Test]
        public void DimLightIsNotEnoughToHideIn()
        {
            light.Level = _ => LightLevel.Dim;
            Assert.That(stealth.CanAttemptHide(hider.Id, out _), Is.False, "lightly obscured: they still see you, only less well");
        }

        // the check

        [Test]
        public void AStealthTotalAtLeastAnObserversPassivePerceptionHidesYouFromThemAndIsAppliedAsACondition()
        {
            DarkAroundTheHider(); random.Value = 8;   // 8 + Dex +2 = 10 against passive 10: ties go to the hider
            var outcome = stealth.Hide(hider.Id);
            Assert.That(outcome.Total, Is.EqualTo(10)); Assert.That(outcome.Hidden, Is.True); Assert.That(outcome.Fooled, Has.Count.EqualTo(2));
            Assert.That(stealth.IsHiddenFrom(hider.Id, guard.Id), Is.True); Assert.That(hider.HasCondition(StealthRules.HiddenId), Is.True);
            Assert.That(perception.CanSee(guard.Id, hider.Id), Is.False, "a hider reads as unseen");
            Assert.That(perception.CanSee(hider.Id, guard.Id), Is.True, "and still sees them");
        }

        [Test]
        public void ATotalBelowAnObserversPassivePerceptionLeavesThatObserverSeeingYou()
        {
            DarkAroundTheHider(); random.Value = 5;   // total 7
            var outcome = stealth.Hide(hider.Id);
            Assert.That(outcome.Hidden, Is.False); Assert.That(outcome.Seen, Has.Count.EqualTo(2));
            Assert.That(hider.HasCondition(StealthRules.HiddenId), Is.False);
        }

        [Test]
        public void TheOneTotalIsComparedWithEachObserverSoASharperEyeStillSeesYou()
        {
            DarkAroundTheHider(); watcher.Stats.SetScore(Id("stat-wisdom"), 18);   // passive 14
            random.Value = 8;   // total 10
            var outcome = stealth.Hide(hider.Id);
            Assert.That(outcome.Fooled, Is.EquivalentTo(new[] { guard.Id })); Assert.That(outcome.Seen, Is.EquivalentTo(new[] { watcher.Id }));
            Assert.That(stealth.IsHiddenFrom(hider.Id, guard.Id), Is.True); Assert.That(stealth.IsHiddenFrom(hider.Id, watcher.Id), Is.False);
            Assert.That(perception.CanSee(watcher.Id, hider.Id), Is.False, "darkness still hides you from the sharper eye: its passive Perception only matters when it could see you; here it found you, but cannot see in the dark");
        }

        [Test]
        public void AnObserverThatArrivesLaterIsComparedWithTheSameTotal()
        {
            DarkAroundTheHider(); random.Value = 12;   // total 14
            stealth.Hide(hider.Id);
            var dull = Make(wisdom: 8); var keen = Make(wisdom: 20);   // passive 9 and 15
            factions.Map[dull.Id] = Id("foes"); factions.Map[keen.Id] = Id("foes");
            positions.Set(dull.Id, new Vector3(5f, 0f, 5f)); positions.Set(keen.Id, new Vector3(-5f, 0f, 5f));
            Assert.That(stealth.IsHiddenFrom(hider.Id, dull.Id), Is.True, "14 against 9");
            Assert.That(stealth.IsHiddenFrom(hider.Id, keen.Id), Is.False, "14 against 15");
        }

        // what a hider gets

        [Test]
        public void AHiderAttacksWithAdvantageAndIsAttackedWithDisadvantage()
        {
            DarkAroundTheHider(); random.Value = 12;
            stealth.Hide(hider.Id);
            Assert.That(attacks.ComputeAdvantage(hider.Id, guard.Id), Is.EqualTo(AdvantageState.Advantage));
            Assert.That(attacks.ComputeAdvantage(guard.Id, hider.Id), Is.EqualTo(AdvantageState.Disadvantage), "the guard cannot see its target");
        }

        // what gives a hider away

        [Test]
        public void MakingAnAttackRevealsTheAttackerAfterTheRollWhetherOrNotItHits()
        {
            DarkAroundTheHider(); random.Value = 12; stealth.Hide(hider.Id);
            attacks.OnAttackRolled(hider.Id, guard.Id);
            Assert.That(stealth.IsHidden(hider.Id), Is.False); Assert.That(hider.HasCondition(StealthRules.HiddenId), Is.False);
            Assert.That(attacks.ComputeAdvantage(hider.Id, guard.Id), Is.EqualTo(AdvantageState.Advantage), "still standing in the dark: unseen for that reason alone");
            light.Level = _ => LightLevel.Bright;
            Assert.That(attacks.ComputeAdvantage(hider.Id, guard.Id), Is.EqualTo(AdvantageState.Normal), "in the light the revealed attacker has no edge");
        }

        private AbilityDefinition Ability(string id, AbilityTag tags)
        {
            var ability = TestDefinitionFactory.CreateAbilityDefinition(id, id, TargetRelation.Enemy);
            TestDefinitionFactory.SetPrivateField(ability, "tags", tags); owned.Add(ability);
            return ability;
        }

        private void Resolved(AbilityDefinition ability, params RuntimeEntityId[] targets)
        {
            var context = new GameEventContext(GameEvent.AbilityResolved, hider.Id);
            context.Data["ability"] = ability; context.Data["targets"] = (IReadOnlyList<RuntimeEntityId>)targets;
            events.Publish(context);
        }

        [Test]
        public void HostileAbilitiesAndSpellsAtOthersRevealButTheCommonActionsDoNot()
        {
            DarkAroundTheHider(); random.Value = 12;
            stealth.Hide(hider.Id);
            Resolved(Ability("ability-utility", AbilityTag.Utility), hider.Id); Assert.That(stealth.IsHidden(hider.Id), Is.True, "Dash, Dodge, Hide and the like");
            Resolved(Ability("ability-move", AbilityTag.Utility | AbilityTag.Movement), hider.Id); Assert.That(stealth.IsHidden(hider.Id), Is.True);
            Resolved(Ability("ability-self-spell", AbilityTag.Spell), hider.Id); Assert.That(stealth.IsHidden(hider.Id), Is.True, "a spell on yourself");
            Resolved(Ability("ability-bless", AbilityTag.Spell), guard.Id); Assert.That(stealth.IsHidden(hider.Id), Is.False, "a spell at another creature gives you away");
            random.Value = 12; stealth.Hide(hider.Id);
            Resolved(Ability("ability-attack", AbilityTag.Attack | AbilityTag.Melee), guard.Id); Assert.That(stealth.IsHidden(hider.Id), Is.False);
            stealth.Hide(hider.Id);
            Resolved(Ability("ability-hostile", AbilityTag.Hostile), guard.Id); Assert.That(stealth.IsHidden(hider.Id), Is.False);
        }

        [Test]
        public void BeingDamagedRevealsTheHiderAsAHouseRule()
        {
            DarkAroundTheHider(); random.Value = 12; stealth.Hide(hider.Id);
            events.Publish(new GameEventContext(GameEvent.AfterDamage, guard.Id, hider.Id));
            Assert.That(stealth.IsHidden(hider.Id), Is.False);
        }

        [Test]
        public void WalkingIntoTheOpenLetsAnObserverFindYouAndBeingFoundByEveryoneEndsHiding()
        {
            DarkAroundTheHider(); random.Value = 12; stealth.Hide(hider.Id);
            positions.Set(hider.Id, new Vector3(0f, 0f, 3f));   // out of the dark patch and in the light
            Assert.That(stealth.IsHiddenFrom(hider.Id, guard.Id), Is.False, "in the open the observer can see you");
            stealth.Tick();
            Assert.That(stealth.IsHidden(hider.Id), Is.False, "both observers have found the hider");
            positions.Set(hider.Id, Vector3.zero);
            Assert.That(stealth.IsHiddenFrom(hider.Id, guard.Id), Is.False, "stepping back into the dark does not hide a creature that was found: leaving cover is the failure");
        }

        [Test]
        public void FoundByOneObserverButNotTheOtherKeepsHidingFromTheOther()
        {
            DarkAroundTheHider(); random.Value = 12; stealth.Hide(hider.Id);
            guard.ApplyCondition(new ConditionInstance(Id("condition-blinded"), guard.Id, guard.Id)); watcher.ApplyCondition(new ConditionInstance(Id("condition-blinded"), watcher.Id, watcher.Id));
            light.Level = _ => LightLevel.Bright;    // lights come on, but both observers are blind
            stealth.Tick();
            Assert.That(stealth.IsHidden(hider.Id), Is.True, "a blind observer cannot find a hider by light");
            sight.AlwaysBlocked = false;
            guard.RemoveConditions(c => true);
            stealth.Tick();
            Assert.That(stealth.IsHidden(hider.Id), Is.True, "the watcher is still blind");
            Assert.That(stealth.IsHiddenFrom(hider.Id, guard.Id), Is.False); Assert.That(stealth.IsHiddenFrom(hider.Id, watcher.Id), Is.True);
        }

        [Test]
        public void TheEndOfCombatClearsEveryHiderAndItsCondition()
        {
            DarkAroundTheHider(); random.Value = 12; stealth.Hide(hider.Id);
            events.Publish(new GameEventContext(GameEvent.CombatEnded));
            Assert.That(stealth.IsHidden(hider.Id), Is.False); Assert.That(hider.HasCondition(StealthRules.HiddenId), Is.False);
        }

        [Test]
        public void ADownedOrSleepingEnemyIsNotSomethingToHideFrom()
        {
            Assert.That(stealth.CanAttemptHide(hider.Id, out _), Is.False);
            characters.TryGetCharacter(guard.Id, out var g); g.TryGetResource(Id("resource-hp"), out var hp); hp.Spend(10);
            characters.TryGetCharacter(watcher.Id, out var w); w.TryGetResource(Id("resource-hp"), out var hp2); hp2.Spend(10);
            Assert.That(stealth.CanAttemptHide(hider.Id, out var reason), Is.False); StringAssert.Contains("no one", reason);
        }

        // through the real ability

        private AbilityResolutionService Resolution(out CharacterState actor)
        {
            actor = hider;
            hider.AddResource(new ResourceState(Id("resource-action"), 1));
            return new AbilityResolutionService(characters, new DiceService(random), eventBus: events, positions: positions, attackBonusRule: attacks, rulesDatabase: rules,
                factionProvider: factions, lineOfSightProvider: sight, perception: perception) { Stealth = stealth, IsCombatActive = () => true };
        }

        [Test]
        public void TheHideAbilityIsRefusedWithTheReasonAndSpendsNothingWhileEveryoneSeesYou()
        {
            var resolution = Resolution(out var actor);
            var result = resolution.Execute(actor.Id, rules.Resolve<AbilityDefinition>(Id("ability-hide")), new[] { actor.Id });
            Assert.That(result.Succeeded, Is.False); StringAssert.Contains("can see you clearly", result.Message);
            actor.TryGetResource(Id("resource-action"), out var action); Assert.That(action.Current, Is.EqualTo(1));
        }

        [Test]
        public void TheHideAbilitySpendsTheActionAndHidesYouInTheDark_AndItsOwnCastDoesNotGiveYouAway()
        {
            DarkAroundTheHider(); random.Value = 12;
            var resolution = Resolution(out var actor);
            var result = resolution.Execute(actor.Id, rules.Resolve<AbilityDefinition>(Id("ability-hide")), new[] { actor.Id });
            Assert.That(result.Succeeded, Is.True, result.Message);
            actor.TryGetResource(Id("resource-action"), out var action); Assert.That(action.Current, Is.Zero);
            Assert.That(stealth.IsHidden(actor.Id), Is.True); Assert.That(stealth.IsHiddenFrom(actor.Id, guard.Id), Is.True);
        }

        [Test]
        public void EveryCharacterGetsHideThroughTheCommonActionsFeature()
        {
            var common = rules.Resolve<FeatureDefinition>(Id("feature-common-actions"));
            CollectionAssert.Contains(common.GrantedAbilityIds.Select(i => i.Value).ToArray(), "ability-hide");
            var hide = rules.Resolve<AbilityDefinition>(Id("ability-hide"));
            Assert.That(hide.CombatOnly, Is.True); Assert.That(hide.Tags & AbilityTag.Utility, Is.EqualTo(AbilityTag.Utility)); Assert.That(hide.Description, Is.Not.Empty);
        }

        [Test]
        public void TheMovementServiceTellsWhoeverIsWatchingEveryFrameAWalkIsInProgress()
        {
            var movement = new CRPG.Combat.MovementResolutionService(characters, positions, null);
            var seen = new List<RuntimeEntityId>();
            movement.Moved = seen.Add;
            movement.NotifyMoved(hider.Id); movement.NotifyMoved(hider.Id);
            Assert.That(seen, Is.EqualTo(new[] { hider.Id, hider.Id }));
            movement.Moved = null; Assert.DoesNotThrow(() => movement.NotifyMoved(hider.Id));
        }

        // the stealth skill itself

        [Test]
        public void StealthIsADexteritySkillInTheRuleset()
        {
            var stealthSkill = rules.Resolve<SkillDefinition>(Id("skill-stealth"));
            Assert.That(stealthSkill.GoverningStat.Id, Is.EqualTo(Id("stat-dexterity")));
            var check = new SkillAwareCheckRule(characters, rules);
            Assert.That(check.ComputeSkillCheckBonus(hider.Id, StealthRules.StealthKey), Is.EqualTo(2), "Dex 14 and not proficient");
            hider.GrantProficiency(Id("skill-stealth"));
            Assert.That(check.ComputeSkillCheckBonus(hider.Id, StealthRules.StealthKey), Is.EqualTo(4), "plus the proficiency bonus at level 1");
        }
    }
}

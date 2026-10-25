using CRPG.Abilities;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Rules.SRD5;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    /// <summary>
    /// SRD 5e Sneak Attack as an attack rider: once per turn, +1d6 per two rogue levels (rounded up) on a weapon attack that hit, when the attack had advantage (flanking counts) or another enemy of the target stands next to it, but never with disadvantage. The forgiving default lets ANY weapon attack
    /// sneak; the strict SRD rule (finesse or ranged only) is a switch. A spell attack never qualifies. Only a character who HAS the feature gets it.
    /// </summary>
    public class SneakAttackRuleTests
    {
        private static readonly DefinitionId Feature = new("feature-sneak-attack");

        private sealed class Adjacency : IAllyAdjacencyQuery
        {
            public bool Answer;
            public bool IsAllyAdjacentToTarget(RuntimeEntityId attacker, RuntimeEntityId target) => Answer;
        }

        private static (SneakAttackRule Rule, CharacterRegistry Registry, GameEventBus Bus, RuntimeEntityId Rogue, RuntimeEntityId Target, Adjacency Allies) Setup(int level = 1, bool hasFeature = true)
        {
            var registry = new CharacterRegistry(); var bus = new GameEventBus();
            var rogue = new CharacterState(RuntimeEntityId.NewId()) { Level = level }; if (hasFeature) rogue.GrantFeature(Feature); registry.Register(rogue);
            var target = new CharacterState(RuntimeEntityId.NewId()); registry.Register(target);
            var allies = new Adjacency();
            return (new SneakAttackRule(registry, bus) { AllyAdjacency = allies }, registry, bus, rogue.Id, target.Id, allies);
        }

        private static AbilityDefinition Weapon(AbilityTag tags)
        {
            var ability = UnityEngine.ScriptableObject.CreateInstance<AbilityDefinition>();
            TestDefinitionFactory.SetPrivateField(ability, "id", "ability-test-weapon"); TestDefinitionFactory.SetPrivateField(ability, "tags", tags);
            return ability;
        }

        private const AbilityTag Dagger = AbilityTag.Attack | AbilityTag.Weapon | AbilityTag.Melee | AbilityTag.Finesse, Bow = AbilityTag.Attack | AbilityTag.Weapon | AbilityTag.Ranged, Longsword = AbilityTag.Attack | AbilityTag.Weapon | AbilityTag.Melee, FireBolt = AbilityTag.Attack | AbilityTag.Spell | AbilityTag.Ranged;

        private static bool Ask(SneakAttackRule rule, RuntimeEntityId rogue, RuntimeEntityId target, AbilityTag tags, AdvantageState advantage, out AttackRider rider, bool critical = false)
            => rule.TryGetRider(new AttackRiderQuery(rogue, target, Weapon(tags), advantage, critical), out rider);

        [Test]
        public void AFinesseWeaponWithAdvantageGetsOneDieAtLevelOne_AndOneMoreEveryTwoLevels()
        {
            foreach (var (level, dice) in new[] { (1, 1), (2, 1), (3, 2), (4, 2), (5, 3), (9, 5), (11, 6) })
            {
                var (rule, _, _, rogue, target, _) = Setup(level);
                Assert.IsTrue(Ask(rule, rogue, target, Dagger, AdvantageState.Advantage, out var rider), "level " + level);
                Assert.AreEqual(new DiceExpression(dice, 6, 0), rider.ExtraDamage, "level " + level); Assert.AreEqual("Sneak Attack", rider.Label);
            }
        }

        [Test]
        public void TheForgivingDefaultLetsAnyWeaponSneak_ButNeverASpell()
        {
            var (rule, _, bus, rogue, target, _) = Setup();
            Assert.IsTrue(Ask(rule, rogue, target, Bow, AdvantageState.Advantage, out _), "a bow");
            bus.Publish(new GameEventContext(GameEvent.TurnStarted, rogue));
            Assert.IsTrue(Ask(rule, rogue, target, Longsword, AdvantageState.Advantage, out _), "a longsword: any weapon sneaks by default");
            bus.Publish(new GameEventContext(GameEvent.TurnStarted, rogue));
            Assert.IsFalse(Ask(rule, rogue, target, FireBolt, AdvantageState.Advantage, out _), "a spell attack");
        }

        [Test]
        public void TheStrictSrdSwitchWantsAFinesseOrRangedWeapon()
        {
            var (rule, _, bus, rogue, target, _) = Setup(); rule.RequireFinesseOrRanged = true;
            Assert.IsFalse(Ask(rule, rogue, target, Longsword, AdvantageState.Advantage, out _), "a longsword is neither finesse nor ranged");
            Assert.IsTrue(Ask(rule, rogue, target, Dagger, AdvantageState.Advantage, out _), "a dagger");
            bus.Publish(new GameEventContext(GameEvent.TurnStarted, rogue));
            Assert.IsTrue(Ask(rule, rogue, target, Bow, AdvantageState.Advantage, out _), "a bow");
        }

        [Test]
        public void WithoutAdvantageItNeedsAnotherEnemyBesideTheTarget_AndNeverWithDisadvantage()
        {
            var (rule, _, bus, rogue, target, allies) = Setup();
            allies.Answer = false; Assert.IsFalse(Ask(rule, rogue, target, Dagger, AdvantageState.Normal, out _), "alone, no advantage");
            allies.Answer = true; Assert.IsTrue(Ask(rule, rogue, target, Dagger, AdvantageState.Normal, out _), "an ally beside the target");
            bus.Publish(new GameEventContext(GameEvent.TurnStarted, rogue));
            Assert.IsFalse(Ask(rule, rogue, target, Dagger, AdvantageState.Disadvantage, out _), "disadvantage cancels it even with the ally");
        }

        [Test]
        public void OncePerTurn_TheNextTurnOfTheRogueRestoresIt_AndSoDoesTheEndOfTheFight()
        {
            var (rule, _, bus, rogue, target, _) = Setup();
            Assert.IsTrue(Ask(rule, rogue, target, Dagger, AdvantageState.Advantage, out _));
            Assert.IsFalse(Ask(rule, rogue, target, Dagger, AdvantageState.Advantage, out _), "the second hit in the same turn");
            bus.Publish(new GameEventContext(GameEvent.TurnStarted, RuntimeEntityId.NewId()));
            Assert.IsFalse(Ask(rule, rogue, target, Dagger, AdvantageState.Advantage, out _), "somebody else's turn does not restore it");
            bus.Publish(new GameEventContext(GameEvent.TurnStarted, rogue));
            Assert.IsTrue(Ask(rule, rogue, target, Dagger, AdvantageState.Advantage, out _), "the rogue's next turn");
            Assert.IsFalse(Ask(rule, rogue, target, Dagger, AdvantageState.Advantage, out _));
            bus.Publish(new GameEventContext(GameEvent.CombatEnded, rogue));
            Assert.IsTrue(Ask(rule, rogue, target, Dagger, AdvantageState.Advantage, out _), "a new fight starts fresh");
        }

        [Test]
        public void OnlyACharacterWhoHasTheFeatureGetsIt_AndACriticalDoesNotChangeEligibility()
        {
            var (rule, _, _, rogue, target, _) = Setup(hasFeature: false);
            Assert.IsFalse(Ask(rule, rogue, target, Dagger, AdvantageState.Advantage, out _));
            var (rule2, _, _, rogue2, target2, _) = Setup();
            Assert.IsTrue(Ask(rule2, rogue2, target2, Dagger, AdvantageState.Advantage, out _, critical: true));
        }
    }
}

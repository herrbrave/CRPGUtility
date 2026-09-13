using CRPG.Actors;
using CRPG.Combat;
using CRPG.Core;
using CRPG.Rules.SRD5;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    public class ConcentrationServiceTests
    {
        private static readonly DefinitionId Constitution = new("stat-constitution");
        private static readonly DefinitionId SomeSpell = new("spell-some-concentration-spell");

        private sealed class FixedSavingThrowRule : ISavingThrowRule
        {
            private readonly int bonus;
            private readonly bool autoFail;
            public FixedSavingThrowRule(int bonus, bool autoFail = false) { this.bonus = bonus; this.autoFail = autoFail; }
            public int ComputeSavingThrowBonus(RuntimeEntityId character, DefinitionId savingStatId) => bonus;
            public AdvantageState ComputeAdvantage(RuntimeEntityId character, DefinitionId savingStatId) => AdvantageState.Normal;
            public bool AutoFails(RuntimeEntityId character, DefinitionId savingStatId) => autoFail;
            public int ComputeSpellSaveDC(RuntimeEntityId caster, DefinitionId spellcastingStatId) => 0;
        }

        private static (CharacterRegistry Registry, GameEventBus Bus, CharacterState Character) SetUp(int savingThrowBonus, bool autoFail = false)
        {
            var registry = new CharacterRegistry();
            var character = new CharacterState(RuntimeEntityId.NewId()) { ConcentratingOnAbilityId = SomeSpell };
            registry.Register(character);

            var bus = new GameEventBus();
            var random = new TestRandomSource();
            random.EnqueueResult(10); // concentration save d20 (irrelevant if autoFail)
            var dice = new DiceService(random);
            _ = new ConcentrationService(registry, dice, new FixedSavingThrowRule(savingThrowBonus, autoFail), bus, Constitution);

            return (registry, bus, character);
        }

        private static void FireBeforeDamage(GameEventBus bus, RuntimeEntityId target, int amount)
        {
            var context = new GameEventContext(GameEvent.BeforeDamage, RuntimeEntityId.NewId(), target);
            context.Data["Amount"] = amount;
            bus.Publish(context);
        }

        [Test]
        public void ClearlyFailedSaveEndsConcentration()
        {
            var (_, bus, character) = SetUp(savingThrowBonus: -5); // roll 10 - 5 = 5, well under any real DC

            FireBeforeDamage(bus, character.Id, amount: 20); // DC = max(10, 10) = 10

            Assert.AreEqual(DefinitionId.None, character.ConcentratingOnAbilityId);
        }

        [Test]
        public void SucceededSaveKeepsConcentration()
        {
            var (_, bus, character) = SetUp(savingThrowBonus: 20); // guaranteed success

            FireBeforeDamage(bus, character.Id, amount: 40); // DC = max(10, 20) = 20

            Assert.AreEqual(SomeSpell, character.ConcentratingOnAbilityId);
        }

        [Test]
        public void AutoFailEndsConcentrationWithoutRolling()
        {
            var (_, bus, character) = SetUp(savingThrowBonus: 20, autoFail: true); // would succeed if rolled

            FireBeforeDamage(bus, character.Id, amount: 10);

            Assert.AreEqual(DefinitionId.None, character.ConcentratingOnAbilityId);
        }

        [Test]
        public void ZeroDamageDoesNotTriggerASave()
        {
            var (_, bus, character) = SetUp(savingThrowBonus: -100); // would always fail if a save were rolled

            FireBeforeDamage(bus, character.Id, amount: 0);

            Assert.AreEqual(SomeSpell, character.ConcentratingOnAbilityId);
        }

        [Test]
        public void NotConcentratingIsANoOp()
        {
            var registry = new CharacterRegistry();
            var character = new CharacterState(RuntimeEntityId.NewId()); // ConcentratingOnAbilityId left at None
            registry.Register(character);
            var bus = new GameEventBus();
            var dice = new DiceService(new TestRandomSource());
            _ = new ConcentrationService(registry, dice, new FixedSavingThrowRule(-100), bus, Constitution);

            Assert.DoesNotThrow(() => FireBeforeDamage(bus, character.Id, amount: 50));
        }
    }
}

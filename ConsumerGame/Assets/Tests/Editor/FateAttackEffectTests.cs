using System.Collections.Generic;
using CRPG.Abilities;
using CRPG.Actors;
using CRPG.Consumer.Fate;
using CRPG.Core;
using NUnit.Framework;

namespace CRPG.Consumer.Fate.Tests
{
    /// <summary>
    /// FateAttackEffect's opposed 4dF+skill resolution -- proven directly against AbilityExecutionContext
    /// (no AbilityResolutionService/AbilityDefinition needed, since the effect only reads Source/Targets/
    /// Characters/DiceService/CombatLog off the context).
    /// </summary>
    public class FateAttackEffectTests
    {
        private ResourceDefinition fight;
        private ResourceDefinition athletics;
        private ResourceDefinition stress;
        private CharacterRegistry characters;
        private CharacterState attacker;
        private CharacterState defender;
        private RuntimeEntityId takenOut;

        [SetUp]
        public void SetUp()
        {
            fight = CreateResource("fight");
            athletics = CreateResource("athletics");
            stress = CreateResource("stress");

            characters = new CharacterRegistry();
            attacker = new CharacterState(RuntimeEntityId.NewId());
            defender = new CharacterState(RuntimeEntityId.NewId());
            characters.Register(attacker);
            characters.Register(defender);

            defender.AddResource(new ResourceState(stress.Id, max: 3));

            takenOut = default;
            FateAttackEffect.OnTakenOut = id => takenOut = id;
        }

        [TearDown]
        public void TearDown() => FateAttackEffect.OnTakenOut = null;

        private static ResourceDefinition CreateResource(string id)
        {
            var resource = UnityEngine.ScriptableObject.CreateInstance<ResourceDefinition>();
            SetField(resource, "id", id);
            return resource;
        }

        private FateAttackEffect CreateEffect(int invokeBonus = 0)
        {
            var effect = new FateAttackEffect();
            SetField(effect, "attackSkillResource", fight);
            SetField(effect, "defenseSkillResource", athletics);
            SetField(effect, "stressResource", stress);
            SetField(effect, "invokeBonus", invokeBonus);
            return effect;
        }

        /// <summary>Private fields declared on a BASE type (e.g. DefinitionAsset.id) are invisible to a bare
        /// GetField call on the derived type -- must walk BaseType explicitly.</summary>
        private static void SetField(object target, string fieldName, object value)
        {
            var type = target.GetType();
            while (type != null)
            {
                var field = type.GetField(fieldName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly);
                if (field != null) { field.SetValue(target, value); return; }
                type = type.BaseType;
            }

            throw new System.Exception($"Field '{fieldName}' not found on {target.GetType().Name} or its base types.");
        }

        private AbilityExecutionContext BuildContext(IDiceService diceService) =>
            new(attacker.Id, new[] { defender.Id }, characters, diceService, combatLog: null);

        [Test]
        public void AHitThatFullyOverwhelmsStressTakesTheDefenderOut()
        {
            attacker.AddResource(new ResourceState(fight.Id, max: 2));
            defender.AddResource(new ResourceState(athletics.Id, max: 0));

            var random = new TestRandomSource();
            random.EnqueueResults(3, 3, 3, 3); // attack: 12 - 8 = 4, +2 fight = 6
            random.EnqueueResults(1, 1, 1, 1); // defense: 4 - 8 = -4, +0 athletics = -4 -- shifts = 10

            CreateEffect().Apply(BuildContext(new DiceService(random)));

            defender.TryGetResource(stress.Id, out var defenderStress);
            Assert.AreEqual(0, defenderStress.Current, "Stress can only absorb up to its max.");
            Assert.AreEqual(defender.Id, takenOut, "Overflow beyond Stress must take the defender out.");
        }

        [Test]
        public void AHitFullyAbsorbedByStressDoesNotTakeTheDefenderOut()
        {
            attacker.AddResource(new ResourceState(fight.Id, max: 0));
            defender.AddResource(new ResourceState(athletics.Id, max: 0));

            var random = new TestRandomSource();
            random.EnqueueResults(3, 3, 2, 2); // attack: 10 - 8 = 2
            random.EnqueueResults(2, 2, 2, 2); // defense: 8 - 8 = 0 -- shifts = 2

            CreateEffect().Apply(BuildContext(new DiceService(random)));

            defender.TryGetResource(stress.Id, out var defenderStress);
            Assert.AreEqual(1, defenderStress.Current, "3 max - 2 shifts = 1 remaining.");
            Assert.AreEqual(default(RuntimeEntityId), takenOut, "A fully-absorbed hit must not trigger Taken Out.");
        }

        [Test]
        public void ASuccessfulDefenseSpendsNoStressAtAll()
        {
            attacker.AddResource(new ResourceState(fight.Id, max: 0));
            defender.AddResource(new ResourceState(athletics.Id, max: 2));

            var random = new TestRandomSource();
            random.EnqueueResults(1, 1, 1, 1); // attack: 4 - 8 = -4
            random.EnqueueResults(3, 3, 3, 3); // defense: 12 - 8 = 4, +2 athletics = 6 -- shifts negative

            CreateEffect().Apply(BuildContext(new DiceService(random)));

            defender.TryGetResource(stress.Id, out var defenderStress);
            Assert.AreEqual(3, defenderStress.Current, "A successful defense must be zero-mutation.");
            Assert.AreEqual(default(RuntimeEntityId), takenOut);
        }

        [Test]
        public void InvokingAStuntAddsItsBonusToTheAttackTotal()
        {
            attacker.AddResource(new ResourceState(fight.Id, max: 0));
            defender.AddResource(new ResourceState(athletics.Id, max: 0));

            // Without the invoke bonus this would tie (0 shifts); with +2 it becomes a real hit.
            var random = new TestRandomSource();
            random.EnqueueResults(2, 2, 2, 2); // attack: 8 - 8 = 0
            random.EnqueueResults(2, 2, 2, 2); // defense: 8 - 8 = 0

            CreateEffect(invokeBonus: 2).Apply(BuildContext(new DiceService(random)));

            defender.TryGetResource(stress.Id, out var defenderStress);
            Assert.AreEqual(1, defenderStress.Current, "The +2 invoke bonus must turn a tie into a 2-shift hit.");
        }
    }
}

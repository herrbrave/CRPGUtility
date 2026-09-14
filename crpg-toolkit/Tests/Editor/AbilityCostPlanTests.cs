using System.Collections.Generic;
using System.Linq;
using CRPG.Abilities;
using UnityEngine;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    public class AbilityCostPlanTests
    {
        private static AbilityDefinition CreateAbility(int spellLevel, params (Actors.ResourceDefinition Resource, int Amount)[] costs)
        {
            var ability = ScriptableObject.CreateInstance<AbilityDefinition>();
            TestDefinitionFactory.SetPrivateField(ability, "id", "ability-cost-plan-test");
            TestDefinitionFactory.SetPrivateField(ability, "spellLevel", spellLevel);

            var costList = new List<AbilityCost>();
            foreach (var (resource, amount) in costs)
            {
                object boxed = new AbilityCost();
                TestDefinitionFactory.SetPrivateField(boxed, "resource", resource);
                TestDefinitionFactory.SetPrivateField(boxed, "amount", amount);
                costList.Add((AbilityCost)boxed);
            }
            TestDefinitionFactory.SetPrivateField(ability, "costs", costList);

            return ability;
        }

        private static Actors.ResourceDefinition CreateSlotResource(string id, int spellSlotLevel)
        {
            var resource = TestDefinitionFactory.CreateResourceDefinition(id, id);
            TestDefinitionFactory.SetPrivateField(resource, "spellSlotLevel", spellSlotLevel);
            return resource;
        }

        [Test]
        public void AggregatesDuplicateResourceEntriesIntoOneCombinedAmount()
        {
            var actionResource = TestDefinitionFactory.CreateResourceDefinition("resource-action", "Action");
            var ability = CreateAbility(0, (actionResource, 2), (actionResource, 2));

            var plan = AbilityCostPlan.Build(ability, asRitual: false, upcastWithSlot: null);

            Assert.AreEqual(1, plan.Entries.Count);
            Assert.AreSame(actionResource, plan.Entries[0].Resource);
            Assert.AreEqual(4, plan.Entries[0].Amount);
        }

        [Test]
        public void NormalCastKeepsEveryEntryUnchanged()
        {
            var slot = CreateSlotResource("resource-slot-1", 1);
            var material = TestDefinitionFactory.CreateResourceDefinition("resource-material", "Material");
            var ability = CreateAbility(1, (slot, 1), (material, 1));

            var plan = AbilityCostPlan.Build(ability, asRitual: false, upcastWithSlot: null);

            Assert.AreEqual(2, plan.Entries.Count);
            Assert.IsTrue(plan.Entries.Any(e => e.Resource == slot && e.Amount == 1));
            Assert.IsTrue(plan.Entries.Any(e => e.Resource == material && e.Amount == 1));
        }

        [Test]
        public void RitualRemovesTheSlotShapedEntryButKeepsEveryOtherEntry()
        {
            var slot = CreateSlotResource("resource-slot-1", 1);
            var actionResource = TestDefinitionFactory.CreateResourceDefinition("resource-action", "Action");
            var ability = CreateAbility(1, (slot, 1), (actionResource, 1));

            var plan = AbilityCostPlan.Build(ability, asRitual: true, upcastWithSlot: null);

            Assert.AreEqual(1, plan.Entries.Count);
            Assert.AreSame(actionResource, plan.Entries[0].Resource);
            Assert.AreEqual(1, plan.Entries[0].Amount);
        }

        [Test]
        public void UpcastReplacesTheSlotShapedEntryButKeepsEveryOtherEntry()
        {
            var baseSlot = CreateSlotResource("resource-slot-1", 1);
            var higherSlot = CreateSlotResource("resource-slot-3", 3);
            var material = TestDefinitionFactory.CreateResourceDefinition("resource-material", "Material");
            var ability = CreateAbility(1, (baseSlot, 1), (material, 1));

            var plan = AbilityCostPlan.Build(ability, asRitual: false, upcastWithSlot: higherSlot);

            Assert.AreEqual(2, plan.Entries.Count);
            Assert.IsTrue(plan.Entries.Any(e => e.Resource == higherSlot && e.Amount == 1));
            Assert.IsFalse(plan.Entries.Any(e => e.Resource == baseSlot));
            Assert.IsTrue(plan.Entries.Any(e => e.Resource == material && e.Amount == 1));
        }

        [Test]
        public void AbilityWithNoSlotShapedCostIsUnaffectedByRitualOrUpcast()
        {
            var actionResource = TestDefinitionFactory.CreateResourceDefinition("resource-action", "Action");
            var ability = CreateAbility(0, (actionResource, 1));

            var ritualPlan = AbilityCostPlan.Build(ability, asRitual: true, upcastWithSlot: null);
            var normalPlan = AbilityCostPlan.Build(ability, asRitual: false, upcastWithSlot: null);

            Assert.AreEqual(1, ritualPlan.Entries.Count);
            Assert.AreEqual(1, normalPlan.Entries.Count);
            Assert.AreEqual(1, ritualPlan.Entries[0].Amount);
        }
    }
}

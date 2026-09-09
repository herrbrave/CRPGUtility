using CRPG.Rules;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    public class NumericRuleQueryTests
    {
        [Test]
        public void AddAndMultiplyModifiersApplyInDefinedOrder()
        {
            var query = new NumericRuleQuery(subject: null, baseValue: 10);
            query.AddModifier(new RuleModifier(RuleModifierOperation.Add, 2, "Shield"));
            query.AddModifier(new RuleModifier(RuleModifierOperation.Multiply, 2, "Doubling Effect"));

            var result = query.Evaluate();

            Assert.AreEqual(24, result); // (10 + 2) * 2
        }

        [Test]
        public void OverrideReplacesAccumulatedValue()
        {
            var query = new NumericRuleQuery(subject: null, baseValue: 10);
            query.AddModifier(new RuleModifier(RuleModifierOperation.Add, 5, "Bonus"));
            query.AddModifier(new RuleModifier(RuleModifierOperation.Override, 20, "Petrified"));

            Assert.AreEqual(20, query.Evaluate());
        }

        [Test]
        public void MinimumClampsFinalValue()
        {
            var query = new NumericRuleQuery(subject: null, baseValue: 10);
            query.AddModifier(new RuleModifier(RuleModifierOperation.Subtract, 100, "Massive Penalty"));
            query.AddModifier(new RuleModifier(RuleModifierOperation.Minimum, 1, "Floor"));

            Assert.AreEqual(1, query.Evaluate());
        }

        [Test]
        public void EvaluateRecordsATraceForEveryStep()
        {
            var query = new NumericRuleQuery(subject: null, baseValue: 16);
            query.AddModifier(new RuleModifier(RuleModifierOperation.Add, 2, "Shield"));

            query.Evaluate();

            Assert.AreEqual(2, query.Trace.Count);
            Assert.AreEqual("Shield", query.Trace[1].Source);
            Assert.AreEqual(18, query.Trace[1].ResultingValue);
        }
    }
}

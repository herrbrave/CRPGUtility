using CRPG.Gameplay.Actions;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    /// <summary>A move spends <c>Ceil(distance so far)</c> of an integer Movement resource at every leg arrival, so the resource left over is rounded DOWN at each corner. The NEXT leg must still be allowed the real distance left (start budget minus distance walked), otherwise a path with corners stops up to a metre short of its budget and nothing on screen can say where.</summary>
    public class MoveLegBudgetTests
    {
        [Test]
        public void AtTheStartTheBudgetIsTheResource() => Assert.AreEqual(6f, MoveActionExecution.LegBudget(6f, 0, 0f), 0.0001f);

        [Test]
        public void AfterACornerTheLegGetsTheRealDistanceLeftNotTheRoundedResource()
        {
            // budget 6, a first leg of 2.3 m spent Ceil = 3, so the resource reads 3 -- but 3.7 m of the real budget remain
            Assert.AreEqual(3.7f, MoveActionExecution.LegBudget(3f, 3, 2.3f), 0.0001f);
        }

        [Test]
        public void TheWholeBudgetIsReachableAcrossAnyNumberOfCorners()
        {
            // legs 1.4 + 1.4 + 1.4 + 1.4 + 0.4 = 6.0 against a budget of 6: after each corner the allowance equals what is really left
            float walked = 0f; var resource = 6; var spent = 0;
            foreach (var leg in new[] { 1.4f, 1.4f, 1.4f, 1.4f })
            {
                var allowed = MoveActionExecution.LegBudget(resource, spent, walked);
                Assert.GreaterOrEqual(allowed + 0.0001f, leg, "the next leg of " + leg + " is allowed");
                walked += leg;
                var rounded = (int)System.Math.Ceiling(walked); resource -= rounded - spent; spent = rounded;
            }

            Assert.AreEqual(6f - walked, MoveActionExecution.LegBudget(resource, spent, walked), 0.0001f);
        }

        [Test]
        public void NeverNegativeAndNeverBelowTheResource()
        {
            Assert.AreEqual(0f, MoveActionExecution.LegBudget(0f, 6, 6f), 0.0001f);
            Assert.AreEqual(0f, MoveActionExecution.LegBudget(0f, 6, 6.4f), 0.0001f, "walked a hair over (arrival tolerance): nothing left, not a negative");
            Assert.GreaterOrEqual(MoveActionExecution.LegBudget(2f, 5, 5.0f), 2f);
        }
    }
}

using CRPG.Gameplay.Scenes;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    public class SceneTravelResultTests
    {
        [Test]
        public void OkReportsSuccess()
        {
            var result = SceneTravelResult.Ok();

            Assert.AreEqual(SceneTravelOutcome.Success, result.Outcome);
            Assert.IsTrue(result.Success);
            Assert.IsNull(result.Message);
        }

        [Test]
        public void FailReportsTheGivenOutcomeAndMessage()
        {
            var result = SceneTravelResult.Fail(SceneTravelOutcome.DestinationNotFound, "no match");

            Assert.AreEqual(SceneTravelOutcome.DestinationNotFound, result.Outcome);
            Assert.IsFalse(result.Success);
            Assert.AreEqual("no match", result.Message);
        }

        [Test]
        public void WorldTransitionGateTracksBusyStateAndRejectsReentrantBegin()
        {
            var gate = new WorldTransitionGate();
            Assert.IsFalse(gate.IsBusy);

            gate.BeginBusy("scene-travel");
            Assert.IsTrue(gate.IsBusy);
            Assert.AreEqual("scene-travel", gate.BusyReason);

            Assert.Throws<System.InvalidOperationException>(() => gate.BeginBusy("session-load"));

            gate.EndBusy();
            Assert.IsFalse(gate.IsBusy);
            Assert.IsNull(gate.BusyReason);
        }
    }
}

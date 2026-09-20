using CRPG.Presentation.Sprites;
using NUnit.Framework;
using UnityEngine;

namespace CRPG.Tests.EditMode
{
    /// <summary>2D campaign Task 2.6: the pure cue state machine.</summary>
    public class SpriteCueStateTests
    {
        private SpriteCueSettings settings;
        private SpriteCueState state;

        [SetUp]
        public void SetUp()
        {
            settings = new SpriteCueSettings();
            state = new SpriteCueState(settings);
        }

        private void Run(float seconds)
        {
            for (var t = 0f; t < seconds; t += 0.01f) state.Tick(0.01f);
        }

        [Test]
        public void NeutralHasNoPose()
        {
            state.Tick(0.05f);
            Assert.AreEqual(0f, state.HopOffset);
            Assert.AreEqual(1f, state.Scale);
            Assert.AreEqual(0f, state.TipDegrees);
            Assert.AreEqual(Color.white, state.Tint);
        }

        [Test]
        public void AttackHopsAndPunchesThenReturnsToExactBaseline()
        {
            state.PlayAttack();
            Run(settings.attackSeconds / 2f);
            Assert.Greater(state.HopOffset, settings.hopHeight * 0.9f);
            Assert.Greater(state.Scale, 1f + settings.attackScaleBoost * 0.9f);
            Run(settings.attackSeconds);
            Assert.AreEqual(0f, state.HopOffset);
            Assert.AreEqual(1f, state.Scale);
        }

        [Test]
        public void AttackIsIgnoredWhileDownOrDead()
        {
            state.Down();
            state.PlayAttack();
            Run(0.05f);
            Assert.AreEqual(0f, state.HopOffset);

            state.Die();
            state.PlayAttack();
            Run(0.05f);
            Assert.AreEqual(0f, state.HopOffset);
        }

        [Test]
        public void DownDuringAnAttackCancelsIt()
        {
            state.PlayAttack();
            Run(0.05f);
            state.Down();
            Assert.AreEqual(0f, state.HopOffset);
            Assert.AreEqual(1f, state.Scale);
        }

        [Test]
        public void HitFlashesRedThenFadesToWhite()
        {
            state.PlayHit();
            Assert.AreEqual(settings.hitTint, state.Tint);
            Run(settings.hitSeconds + 0.05f);
            Assert.AreEqual(Color.white, state.Tint);
        }

        [Test]
        public void HitIsAllowedWhileDownButIgnoredOnceDead()
        {
            state.Down();
            state.PlayHit();
            Assert.AreEqual(settings.hitTint, state.Tint);

            state.Reset();
            state.Die();
            state.PlayHit();
            Assert.AreEqual(Color.white, state.Tint);
        }

        [Test]
        public void DownTipsOverAndStaysDown()
        {
            state.Down();
            Run(settings.tipSeconds + 0.1f);
            Assert.IsTrue(state.IsDown);
            Assert.AreEqual(settings.tipDegrees, state.TipDegrees, 0.001f);
            Run(2f);
            Assert.AreEqual(settings.tipDegrees, state.TipDegrees, 0.001f);
        }

        [Test]
        public void RecoverStandsBackUpOnlyFromDown()
        {
            state.Recover(); // neutral: no-op
            Assert.IsFalse(state.IsDown);

            state.Down();
            Run(settings.tipSeconds + 0.1f);
            state.Recover();
            Assert.IsFalse(state.IsDown);
            Run(settings.tipSeconds + 0.1f);
            Assert.AreEqual(0f, state.TipDegrees, 0.001f);
        }

        [Test]
        public void SnapDownIsAlreadyTipped()
        {
            state.SnapDown();
            Assert.IsTrue(state.IsDown);
            Assert.AreEqual(settings.tipDegrees, state.TipDegrees, 0.001f);
        }

        [Test]
        public void DieTipsThenFadesToCorpseAlphaAndIsTerminal()
        {
            state.Die();
            Assert.IsTrue(state.IsDead);
            Assert.IsFalse(state.IsFadeComplete);
            Run(settings.tipSeconds * 0.5f);
            Assert.AreEqual(1f, state.Tint.a, 0.0001f, "no fade until the tip completes");
            Run(settings.tipSeconds + settings.dieFadeSeconds + 0.2f);
            Assert.IsTrue(state.IsFadeComplete);
            Assert.AreEqual(settings.deadAlpha, state.Tint.a, 0.001f);
            Assert.AreEqual(settings.tipDegrees, state.TipDegrees, 0.001f);
        }

        [Test]
        public void DieIsNotUndoneByRecoverDownOrAttack()
        {
            state.Die();
            state.Recover();
            state.Down();
            state.PlayAttack();
            Assert.IsTrue(state.IsDead);
            Assert.IsFalse(state.IsDown);
        }

        [Test]
        public void DiedThenDroppedToZeroStillEndsDead()
        {
            state.Die();
            state.Down(); // real massive-damage event order
            Assert.IsTrue(state.IsDead);
        }

        [Test]
        public void SnapDownAfterDieStaysDead()
        {
            state.Die();
            state.SnapDown();
            Assert.IsTrue(state.IsDead);
        }

        [Test]
        public void BadDeltaTimesAreIgnoredAndHugeOnesClamped()
        {
            state.Down();
            state.Tick(float.NaN);
            state.Tick(-1f);
            state.Tick(0f);
            Assert.AreEqual(0f, state.TipDegrees);
            state.Tick(1000f); // clamped to 0.1s
            Assert.Less(state.TipDegrees, settings.tipDegrees);
            Assert.Greater(state.TipDegrees, 0f);
        }

        [Test]
        public void SanitizedSettingsNeverDivideByZero()
        {
            var bad = new SpriteCueSettings { attackSeconds = 0f, hitSeconds = -1f, tipSeconds = 0f, dieFadeSeconds = 0f };
            var s = new SpriteCueState(bad);
            s.PlayAttack();
            s.PlayHit();
            s.Die();
            Assert.DoesNotThrow(() => s.Tick(0.05f));
            Assert.IsFalse(float.IsNaN(s.HopOffset));
            Assert.IsFalse(float.IsNaN(s.Tint.a));
        }

        [Test]
        public void ResetReturnsToNeutral()
        {
            state.Die();
            Run(2f);
            state.Reset();
            Assert.IsFalse(state.IsDead);
            Assert.AreEqual(0f, state.TipDegrees);
            Assert.AreEqual(Color.white, state.Tint);
        }
    }
}

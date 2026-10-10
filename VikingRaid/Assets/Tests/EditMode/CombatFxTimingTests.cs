using NUnit.Framework;
using VikingRaid.Game;

namespace VikingRaid.Tests.EditMode
{
    /// <summary>The timing rule that keeps hit reactions in step with what the eye sees: a melee blow lands quickly, an arrow only after the bow's release plus its flight.</summary>
    public class CombatFxTimingTests
    {
        [Test]
        public void AMeleeBlowLandsAfterTheFixedSwingDelay()
        {
            Assert.AreEqual(CombatFx.MeleeHitDelay, CombatFx.HitDelay(1.5f, bowUser: false), 1e-4f);
            Assert.AreEqual(CombatFx.MeleeHitDelay, CombatFx.HitDelay(CombatFx.RangedThreshold, bowUser: true), 1e-4f, "right at the threshold is still melee");
        }

        [Test]
        public void AnArrowLandsAfterTheBowReleasePlusItsFlightAndASpellAfterTheQuickerCast()
        {
            Assert.AreEqual(CombatFx.BowRelease + 10f / CombatFx.ProjectileSpeed, CombatFx.HitDelay(10f, bowUser: true), 1e-4f);
            Assert.AreEqual(CombatFx.CastRelease + 10f / CombatFx.ProjectileSpeed, CombatFx.HitDelay(10f, bowUser: false), 1e-4f);
            Assert.Greater(CombatFx.HitDelay(10f, true), CombatFx.HitDelay(10f, false));
            Assert.Greater(CombatFx.HitDelay(10f, false), CombatFx.MeleeHitDelay);
        }

        [Test]
        public void TheBowReleaseMatchesTheAuthoredAnimationFrame()
        {
            Assert.AreEqual(29f / 30f, CombatFx.BowRelease, 0.01f, "Bow_Draw releases on frame 29 at 30 fps (plan Section 12)");
        }

        [Test]
        public void FartherShotsTakeLongerToLand()
        {
            Assert.Greater(CombatFx.HitDelay(14f, true), CombatFx.HitDelay(6f, true));
        }
    }
}

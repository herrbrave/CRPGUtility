using System.Collections;
using CRPG.Presentation;
using CRPG.Presentation.Animation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CRPG.Tests.PlayMode
{
    /// <summary>
    /// Task 2.5 (Production Foundation) / Gate A: regression coverage for the SECOND real Humanoid art source
    /// (Docs/Design/Production-Foundation.md sec.8's "two different real Humanoid art sources"), built via the
    /// CharacterSetupWindow/CharacterWrapperBuilder tooling rather than a one-off author script.
    /// <para>Corrected after a real, visually-reproduced bug (Phase 2 review follow-up, 2026-09-11): the SAME
    /// CharacterAnimationProfile plays correctly on both differently-shaped bodies because both source FBX
    /// packs happen to share an identical bone-PATH hierarchy (confirmed via a live Editor query) and
    /// PlayableCharacterAnimationDriver applies clip curves by raw path, with NO Humanoid muscle-space
    /// retargeting step anywhere in this system -- it is not "Humanoid retargeting" despite this class's own
    /// former docstring claiming that. A HUMANOID avatar on either model actively CORRUPTS this system's
    /// playback (confirmed empirically: a live character collapsed into a wrong, ground-level pose within
    /// about a second of Play, reproducible only through Unity's own continuous graph evaluation, not a
    /// one-shot manual sample) -- both fixtures must use a Generic avatar instead.</para>
    /// </summary>
    public class GateAFixtureTests
    {
        private GameObject instance;

        [TearDown]
        public void TearDown()
        {
            if (instance != null) UnityEngine.Object.DestroyImmediate(instance);
        }

        [UnityTest]
        public IEnumerator TheFirstHumanoidFixtureStandsUprightRatherThanCollapsingDuringRealPlayback()
        {
            // The exact fixture (and exact bug) the user visually caught: no test anywhere previously checked
            // the REAL rendered pose of the primary/original character during actual Play, only binding state.
            var prefab = Resources.Load<GameObject>("NPCs/BaseHumanMaleCharacter");
            Assert.IsNotNull(prefab, "Expected Assets/Resources/NPCs/BaseHumanMaleCharacter.prefab.");

            instance = UnityEngine.Object.Instantiate(prefab);
            var view = instance.GetComponentInChildren<CharacterView>();
            Assert.IsNotNull(view);

            yield return null;

            Assert.IsFalse(view.Animator.isHuman, "Must use a Generic avatar -- see CharacterRigValidator.ValidateModel's remarks.");

            for (var i = 0; i < 30; i++) yield return null;

            AssertReasonablyUprightPose(instance);
        }

        [UnityTest]
        public IEnumerator TheSecondHumanoidFixtureBindsAndPlaysBaselineLocomotionWithoutError()
        {
            var prefab = Resources.Load<GameObject>("NPCs/BaseHumanFemaleCharacter");
            Assert.IsNotNull(prefab, "Expected Task 2.5's second Gate A fixture at Assets/Resources/NPCs/BaseHumanFemaleCharacter.prefab.");

            instance = UnityEngine.Object.Instantiate(prefab);
            var view = instance.GetComponentInChildren<CharacterView>();
            Assert.IsNotNull(view);

            yield return null; // let CharacterView.Start()'s fallback bind run (Task 1.3's nested-PrefabInstance-timing gotcha)

            Assert.IsNotNull(view.Model, "The nested Superhero_Female_FullBody model must bind.");
            Assert.IsNotNull(view.Animator);
            Assert.IsFalse(view.Animator.isHuman,
                "This fixture must use a Generic avatar, not Humanoid -- a Humanoid avatar actively corrupts this " +
                "system's raw Playables-based clip playback (see CharacterRigValidator.ValidateModel's own remarks).");

            var playableAnimator = instance.GetComponent<CharacterPlayableAnimator>();
            Assert.IsNotNull(playableAnimator);
            Assert.IsTrue(playableAnimator.IsPresentationBound, "BindPresentation must have run as part of CharacterView's Model bind.");

            // Tick real frames of baseline (idle) playback -- proves the SAME clip authored against a
            // different skeleton (Superhero_Male_FullBody) plays cleanly by bone-path matching, without
            // throwing AND without visually collapsing. IsPresentationBound alone does NOT catch a collapsed
            // pose -- the driver's own weight/port bookkeeping can read perfectly clean (exactly one port,
            // full weight) while the actual skeleton is heaped on the ground; this is exactly the gap that
            // let the original bug ship unnoticed. AssertReasonablyUprightPose below is the real regression
            // guard, checked against actual world-space mesh bounds, not driver state.
            for (var i = 0; i < 30; i++) yield return null;

            Assert.IsTrue(playableAnimator.IsPresentationBound, "Playback must still be bound after several ticks -- no silent teardown.");
            AssertReasonablyUprightPose(instance);
        }

        // General, bone-name-independent sanity check against the ACTUAL rendered geometry (not driver/state
        // bookkeeping): a character collapsed into a heap has a small vertical extent close to the ground; a
        // properly standing/idle Humanoid-proportioned character does not. Deliberately generous bounds --
        // this is a regression guard against gross collapse, not a precise pose-correctness assertion (that's
        // what an actual human looking at CharacterLab.unity is for).
        private static void AssertReasonablyUprightPose(GameObject character)
        {
            var renderers = character.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Assert.IsTrue(renderers.Length > 0, "Expected at least one SkinnedMeshRenderer to check.");

            var combined = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++) combined.Encapsulate(renderers[i].bounds);

            Assert.Greater(combined.size.y, 1.0f,
                $"The character's rendered vertical extent ({combined.size.y:F2}m) is too small for a standing " +
                "Humanoid-proportioned body -- this is the exact shape a collapsed/heaped pose produces.");
            Assert.Greater(combined.max.y, 0.8f,
                $"The character's highest rendered point ({combined.max.y:F2}m above its origin) is too low for " +
                "a standing pose -- consistent with the character having collapsed toward the ground.");
        }
    }
}

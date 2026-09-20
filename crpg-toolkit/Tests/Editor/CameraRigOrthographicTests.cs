using System.Linq;
using CRPG.Camera;
using CRPG.Editor.Validation;
using NUnit.Framework;
using UnityEngine;

namespace CRPG.Tests.EditMode
{
    /// <summary>2D campaign Task 3.1: the orthographic pixel-art mode of CameraRigState (default-off) and its settings validator.</summary>
    public class CameraRigOrthographicTests
    {
        private const float Height = 1080f;
        private const float Aspect = 16f / 9f;

        private static CRPGCameraSettings Settings(bool orthographic = true, int defaultScale = 3, int[] scales = null, bool snap = true, bool topDownLocked = true)
        {
            var settings = ScriptableObject.CreateInstance<CRPGCameraSettings>();
            TestDefinitionFactory.SetPrivateField(settings, "orthographic", orthographic);
            TestDefinitionFactory.SetPrivateField(settings, "defaultScale", defaultScale);
            TestDefinitionFactory.SetPrivateField(settings, "zoomScales", scales ?? new[] { 2, 3, 4, 5, 6 });
            TestDefinitionFactory.SetPrivateField(settings, "pixelSnapFocus", snap);
            if (topDownLocked)
            {
                TestDefinitionFactory.SetPrivateField(settings, "rotationSpeed", 0f);
                TestDefinitionFactory.SetPrivateField(settings, "snapRotationDegrees", 0f);
                TestDefinitionFactory.SetPrivateField(settings, "minimumPitch", 90f);
                TestDefinitionFactory.SetPrivateField(settings, "maximumPitch", 90f);
                TestDefinitionFactory.SetPrivateField(settings, "defaultPitch", 90f);
            }

            return settings;
        }

        private static CameraRigState Rig(CRPGCameraSettings settings)
        {
            var rig = new CameraRigState(settings);
            rig.SetViewport(Height, Aspect);
            return rig;
        }

        // ---------------------------------------------------------------------------------------------
        // Scale and orthographic size
        // ---------------------------------------------------------------------------------------------

        [Test]
        public void OrthographicSizeMakesOneSourcePixelExactlyScaleScreenPixels()
        {
            var rig = Rig(Settings(defaultScale: 3));
            Assert.AreEqual(3, rig.Scale);
            Assert.AreEqual(Height / (2f * 16f * 3f), rig.OrthographicSize, 1e-4f);

            rig.StepScale(+1);
            Assert.AreEqual(4, rig.Scale);
            Assert.AreEqual(Height / (2f * 16f * 4f), rig.OrthographicSize, 1e-4f);
        }

        [Test]
        public void ADefaultScaleNotInTheListFallsBackToTheNearestListedScale()
        {
            Assert.AreEqual(6, Rig(Settings(defaultScale: 9)).Scale);
            Assert.AreEqual(2, Rig(Settings(defaultScale: 1)).Scale);
            Assert.AreEqual(4, Rig(Settings(defaultScale: 4, scales: new[] { 2, 4, 8 })).Scale);
        }

        [Test]
        public void OrthographicSizeIsZeroUntilAViewportIsKnown()
        {
            var rig = new CameraRigState(Settings());
            Assert.AreEqual(0f, rig.OrthographicSize);
            rig.SetViewport(0f, 1f); // ignored
            Assert.AreEqual(0f, rig.OrthographicSize);
        }

        [Test]
        public void ZoomInputStepsExactlyOneScalePerCallAndNeverChangesTheDistance()
        {
            var settings = Settings(defaultScale: 3);
            var rig = Rig(settings);
            var distance = rig.Zoom;

            rig.ApplyZoom(1000f); // a huge wheel delta is still one notch
            Assert.AreEqual(4, rig.Scale);
            rig.ApplyZoom(-0.001f);
            Assert.AreEqual(3, rig.Scale);
            rig.ApplyZoom(0f);
            Assert.AreEqual(3, rig.Scale);
            Assert.AreEqual(distance, rig.Zoom, "in orthographic mode the camera distance is not the zoom");
        }

        [Test]
        public void ScaleStepsClampAtBothEnds()
        {
            var rig = Rig(Settings());
            for (var i = 0; i < 10; i++) rig.StepScale(+1);
            Assert.AreEqual(6, rig.Scale);
            for (var i = 0; i < 10; i++) rig.StepScale(-1);
            Assert.AreEqual(2, rig.Scale);
        }

        [Test]
        public void ANonOrthographicRigIsCompletelyUnchanged()
        {
            var settings = Settings(orthographic: false);
            var rig = Rig(settings);
            var distance = rig.Zoom;

            rig.ApplyZoom(1f);
            Assert.AreNotEqual(distance, rig.Zoom, "perspective zoom still changes the distance");
            Assert.AreEqual(0f, rig.OrthographicSize, "no orthographic size");

            rig.FocusOn(new Vector3(1.2345f, 0f, 6.789f));
            Assert.AreEqual(rig.FocusPoint, rig.PresentationFocus, "no snapping");

            rig.SetBounds(new Rect(0f, 0f, 10f, 10f));
            rig.FocusOn(new Vector3(-5f, 0f, 50f));
            Assert.AreEqual(new Vector3(0f, 0f, 10f), rig.FocusPoint, "the plain focus clamp, not a viewport clamp");
        }

        [Test]
        public void YawStaysZeroWhateverRotationInputArrivesWhenTheSettingsLockIt()
        {
            var rig = Rig(Settings());
            rig.Rotate(1f, 10f);
            rig.SnapRotate(1f);
            rig.SnapRotate(-1f);
            Assert.AreEqual(0f, rig.Yaw, "rotationSpeed and snapRotationDegrees of 0 already lock yaw");
        }

        // ---------------------------------------------------------------------------------------------
        // Viewport bounds clamp
        // ---------------------------------------------------------------------------------------------

        [Test]
        public void TheVisibleRectangleNotJustTheFocusPointStaysInsideTheBounds()
        {
            var rig = Rig(Settings(defaultScale: 3));
            var half = rig.HalfExtents; // (20, 11.25)
            rig.SetBounds(new Rect(0f, 0f, 100f, 100f));

            rig.FocusOn(new Vector3(-50f, 0f, -50f));
            Assert.AreEqual(half.x, rig.FocusPoint.x, 1e-3f);
            Assert.AreEqual(half.y, rig.FocusPoint.z, 1e-3f);

            rig.FocusOn(new Vector3(500f, 0f, 500f));
            Assert.AreEqual(100f - half.x, rig.FocusPoint.x, 1e-3f);
            Assert.AreEqual(100f - half.y, rig.FocusPoint.z, 1e-3f);

            rig.FocusOn(new Vector3(50f, 0f, 50f));
            Assert.AreEqual(new Vector3(50f, 0f, 50f), rig.FocusPoint, "well inside: untouched");
        }

        [Test]
        public void AMapSmallerThanTheViewOnAnAxisCentresOnItOnThatAxisOnly()
        {
            var rig = Rig(Settings(defaultScale: 3));
            rig.SetBounds(new Rect(10f, 0f, 20f, 100f)); // 20 wide < the 40 visible; 100 deep > the 22.5 visible

            rig.FocusOn(new Vector3(500f, 0f, 500f));

            Assert.AreEqual(20f, rig.FocusPoint.x, 1e-3f, "centred on the narrow axis");
            Assert.AreEqual(100f - rig.HalfExtents.y, rig.FocusPoint.z, 1e-3f, "clamped on the wide one");
        }

        [Test]
        public void ZoomingOutReclampsTheFocusBecauseTheVisibleAreaGrew()
        {
            var rig = Rig(Settings(defaultScale: 6));
            rig.SetBounds(new Rect(0f, 0f, 100f, 100f));
            rig.FocusOn(new Vector3(500f, 0f, 500f));
            var atScaleSix = rig.FocusPoint.x;

            rig.StepScale(-1);
            rig.StepScale(-1);
            rig.StepScale(-1);
            rig.StepScale(-1); // scale 2: much more visible

            Assert.Less(rig.FocusPoint.x, atScaleSix);
            Assert.AreEqual(100f - rig.HalfExtents.x, rig.FocusPoint.x, 1e-3f);
        }

        [Test]
        public void ChangingTheViewportReclampsTheCurrentFocus()
        {
            var rig = new CameraRigState(Settings(defaultScale: 3));
            rig.SetBounds(new Rect(0f, 0f, 100f, 100f));
            rig.FocusOn(new Vector3(0f, 0f, 0f)); // viewport unknown: the plain clamp leaves it at the corner
            Assert.AreEqual(0f, rig.FocusPoint.x);

            rig.SetViewport(Height, Aspect);

            Assert.AreEqual(rig.HalfExtents.x, rig.FocusPoint.x, 1e-3f, "now shown with the map edge at the screen edge, not off the map");
        }

        // ---------------------------------------------------------------------------------------------
        // Pixel snapping
        // ---------------------------------------------------------------------------------------------

        [Test]
        public void PresentationFocusIsSnappedToWholeScreenPixelsAndTheRealFocusIsUntouched()
        {
            var rig = Rig(Settings(defaultScale: 3));
            var raw = new Vector3(5.01234f, 2f, 7.98765f);
            rig.FocusOn(raw);

            var step = 1f / (16f * 3f);
            var snapped = rig.PresentationFocus;

            Assert.AreEqual(raw, rig.FocusPoint, "the source of truth is not snapped (no drift)");
            Assert.AreEqual(0f, Mathf.Repeat(snapped.x / step + 0.5f, 1f) - 0.5f, 1e-3f, "x is a whole number of screen pixels");
            Assert.AreEqual(0f, Mathf.Repeat(snapped.z / step + 0.5f, 1f) - 0.5f, 1e-3f, "z is a whole number of screen pixels");
            Assert.LessOrEqual(Mathf.Abs(snapped.x - raw.x), step * 0.5f + 1e-5f);
            Assert.LessOrEqual(Mathf.Abs(snapped.z - raw.z), step * 0.5f + 1e-5f);
            Assert.AreEqual(raw.y, snapped.y, "height is not snapped");
        }

        [Test]
        public void SnappingFollowsTheScaleAndCanBeTurnedOff()
        {
            var raw = new Vector3(1.0123f, 0f, 1.0123f);
            var rig = Rig(Settings(defaultScale: 2));
            rig.FocusOn(raw);
            var atTwo = rig.PresentationFocus.x;
            rig.StepScale(+3); // scale 5
            rig.FocusOn(raw);
            var atFive = rig.PresentationFocus.x;
            Assert.AreNotEqual(atTwo, atFive, "a different scale has a different pixel grid");

            var off = Rig(Settings(snap: false));
            off.FocusOn(raw);
            Assert.AreEqual(raw, off.PresentationFocus);
        }

        [Test]
        public void SmoothedFollowNeverDriftsBecauseOfSnapping()
        {
            var rig = Rig(Settings(defaultScale: 3));
            rig.FocusOn(Vector3.zero);
            var target = new Vector3(10.123f, 0f, 4.567f);
            for (var i = 0; i < 600; i++) rig.FollowTarget(target, 0.016f);

            Assert.AreEqual(target.x, rig.FocusPoint.x, 1e-3f, "the unsnapped focus converges exactly on the target");
            Assert.AreEqual(target.z, rig.FocusPoint.z, 1e-3f);
        }

        // ---------------------------------------------------------------------------------------------
        // Settings validator
        // ---------------------------------------------------------------------------------------------

        [Test]
        public void ValidatorIgnoresNonOrthographicAndAcceptsAProperTopDownSetup()
        {
            Assert.IsEmpty(CameraSettingsValidator.Validate(Settings(orthographic: false, topDownLocked: false), "x"));
            Assert.IsEmpty(CameraSettingsValidator.Validate(Settings(), "x"));
            Assert.IsEmpty(CameraSettingsValidator.Validate(null, "x"));
        }

        [Test]
        public void ValidatorReportsEmptyUnorderedAndNonPositiveScalesAsErrors()
        {
            Assert.IsTrue(CameraSettingsValidator.Validate(Settings(scales: new int[0]), "x").Any(r => r.Severity == ValidationSeverity.Error));
            Assert.IsTrue(CameraSettingsValidator.Validate(Settings(scales: new[] { 3, 2 }, defaultScale: 3), "x").Any(r => r.Severity == ValidationSeverity.Error && r.Field.StartsWith("zoomScales")));
            Assert.IsTrue(CameraSettingsValidator.Validate(Settings(scales: new[] { 2, 2, 3 }), "x").Any(r => r.Severity == ValidationSeverity.Error));
            Assert.IsTrue(CameraSettingsValidator.Validate(Settings(scales: new[] { 0, 2 }), "x").Any(r => r.Severity == ValidationSeverity.Error));
        }

        [Test]
        public void ValidatorWarnsAboutADefaultScaleOutsideTheListAnUnlockedYawAndAPitchOffNinety()
        {
            var results = CameraSettingsValidator.Validate(Settings(defaultScale: 9), "x");
            Assert.IsTrue(results.Any(r => r.Severity == ValidationSeverity.Warning && r.Field == "defaultScale"));

            var unlocked = CameraSettingsValidator.Validate(Settings(topDownLocked: false), "x"); // stock rotation speed 90 and pitch 30-70
            Assert.IsTrue(unlocked.Any(r => r.Severity == ValidationSeverity.Warning && r.Field == "rotationSpeed"));
            Assert.IsTrue(unlocked.Any(r => r.Severity == ValidationSeverity.Warning && r.Field == "pitch"));
        }
    }
}

using System;
using CRPG.Maps;
using NUnit.Framework;
using UnityEngine;

namespace CRPG.Tests.EditMode
{
    public class MapViewportStateTests
    {
        private static readonly Rect Bounds = new Rect(-100f, -50f, 200f, 100f);
        private static readonly Vector2 Viewport = new Vector2(400f, 300f);

        private static MapViewportState Area(Vector2? viewport = null)
        {
            var state = new MapViewportState(Bounds, MapViewportOptions.Area());
            state.SetViewportSize(viewport ?? Viewport);
            return state;
        }

        private static MapViewportState Minimap(Rect? bounds = null)
        {
            var state = new MapViewportState(bounds ?? new Rect(-500f, -500f, 1000f, 1000f), MapViewportOptions.Minimap());
            state.SetViewportSize(new Vector2(240f, 240f));
            return state;
        }

        private static void AssertVector(Vector2 expected, Vector2 actual, float tolerance = 0.001f)
        {
            Assert.AreEqual(expected.x, actual.x, tolerance, $"x of {actual}");
            Assert.AreEqual(expected.y, actual.y, tolerance, $"y of {actual}");
        }

        // ----- construction ------------------------------------------------------------------------------

        [Test]
        public void RejectsEmptyOrNonFiniteBounds()
        {
            Assert.Throws<ArgumentException>(() => new MapViewportState(new Rect(0f, 0f, 0f, 10f), MapViewportOptions.Area()));
            Assert.Throws<ArgumentException>(() => new MapViewportState(new Rect(0f, 0f, 10f, -1f), MapViewportOptions.Area()));
            Assert.Throws<ArgumentException>(() => new MapViewportState(new Rect(float.NaN, 0f, 10f, 10f), MapViewportOptions.Area()));
            Assert.Throws<ArgumentException>(() => new MapViewportState(new Rect(0f, 0f, float.PositiveInfinity, 10f), MapViewportOptions.Area()));
        }

        [Test]
        public void RejectsAnInvertedZoomRange()
        {
            var options = new MapViewportOptions { MinZoom = 4f, MaxZoom = 1f };
            Assert.Throws<ArgumentException>(() => new MapViewportState(Bounds, options));
        }

        // ----- layout deferral ---------------------------------------------------------------------------

        [Test]
        public void HasNoProjectionUntilLayoutSuppliesASize()
        {
            var state = new MapViewportState(Bounds, MapViewportOptions.Area());
            Assert.IsFalse(state.HasLayout);
            Assert.IsFalse(state.Projection.IsValid);
            Assert.AreEqual(0f, state.Scale);
        }

        [Test]
        public void ZeroSizedLayoutDefersProjectionAndPanIsANoOp()
        {
            var state = new MapViewportState(Bounds, MapViewportOptions.Area());
            state.SetViewportSize(Vector2.zero);
            state.PanByViewportDelta(new Vector2(50f, 50f));
            state.ZoomAt(new Vector2(10f, 10f), 2f);

            Assert.IsFalse(state.HasLayout);
            Assert.IsFalse(state.Projection.IsValid);
            AssertVector(Bounds.center, state.Center);
        }

        [Test]
        public void ANonFiniteSizeClearsAnExistingLayout()
        {
            var state = Area();
            state.SetViewportSize(new Vector2(float.NaN, 100f));
            Assert.IsFalse(state.HasLayout);
        }

        [Test]
        public void LayoutArrivingLateStartsFittedAndCentred()
        {
            var state = new MapViewportState(Bounds, MapViewportOptions.Area());
            state.SetViewportSize(Vector2.zero);
            state.SetViewportSize(Viewport);

            Assert.IsTrue(state.HasLayout);
            Assert.AreEqual(2f, state.Scale, 0.0001f);
            AssertVector(Vector2.zero, state.Center);
        }

        // ----- area map: fit, letterbox, no stretching ----------------------------------------------------

        [Test]
        public void AreaMapStartsWithTheWholeImageFitted()
        {
            var state = Area();
            Assert.AreEqual(1f, state.ZoomFactor);
            Assert.AreEqual(2f, state.BaseScale, 0.0001f);
            var rect = state.Projection.ImageRect;
            Assert.AreEqual(400f, rect.width, 0.001f);   // full width...
            Assert.AreEqual(200f, rect.height, 0.001f);  // ...with the 2:1 aspect preserved, not stretched to 300
            Assert.AreEqual(50f, rect.y, 0.001f);
        }

        [Test]
        public void AreaMapFitsATallImageIntoAWideViewportByHeight()
        {
            var state = new MapViewportState(new Rect(0f, 0f, 100f, 200f), MapViewportOptions.Area());
            state.SetViewportSize(new Vector2(500f, 400f));

            Assert.AreEqual(2f, state.BaseScale, 0.0001f);
            var rect = state.Projection.ImageRect;
            Assert.AreEqual(200f, rect.width, 0.001f);
            Assert.AreEqual(400f, rect.height, 0.001f);
            Assert.AreEqual(150f, rect.x, 0.001f); // centred horizontally
        }

        [Test]
        public void ResizeKeepsTheZoomFactorAndRefits()
        {
            var state = Area();
            state.ZoomAtCenter(2f);
            Assert.AreEqual(2f, state.ZoomFactor, 0.0001f);

            state.SetViewportSize(new Vector2(800f, 600f));

            Assert.AreEqual(2f, state.ZoomFactor, 0.0001f);
            Assert.AreEqual(4f, state.BaseScale, 0.0001f);
            Assert.AreEqual(8f, state.Scale, 0.0001f);
        }

        // ----- zoom ---------------------------------------------------------------------------------------

        [Test]
        public void PointerAnchoredZoomKeepsTheWorldPointUnderThePointer()
        {
            var state = Area();
            var pointer = new Vector2(300f, 100f);

            state.ZoomAt(pointer, 2f); // scale 2 -> 4

            Assert.AreEqual(4f, state.Scale, 0.0001f);
            AssertVector(new Vector2(25f, 12.5f), state.Center);
            AssertVector(new Vector2(50f, 25f), state.Projection.ViewportToWorld(pointer));
        }

        [Test]
        public void ZoomButtonsZoomAboutTheViewportCentre()
        {
            var state = Area();
            state.ZoomAtCenter(2f);
            AssertVector(Vector2.zero, state.Center);
            Assert.AreEqual(4f, state.Scale, 0.0001f);
        }

        [Test]
        public void AreaZoomIsLimitedToOneToEightTimesFit()
        {
            var state = Area();
            state.ZoomAtCenter(0.1f);
            Assert.AreEqual(1f, state.ZoomFactor, 0.0001f);
            state.ZoomAtCenter(1000f);
            Assert.AreEqual(8f, state.ZoomFactor, 0.0001f);
        }

        [Test]
        public void MinimapZoomIsLimitedToHalfToFourTimesDefault()
        {
            var state = Minimap();
            Assert.AreEqual(3f, state.Scale, 0.0001f); // 240 points / 80 m
            state.ZoomAtCenter(0.01f);
            Assert.AreEqual(1.5f, state.Scale, 0.0001f);   // 160 m across 240 points
            state.ZoomAtCenter(1000f);
            Assert.AreEqual(12f, state.Scale, 0.0001f);    // 20 m across 240 points
            Assert.AreEqual(20f, 240f / state.Scale, 0.0001f);
        }

        [Test]
        public void SetZoomFactorIgnoresNonPositiveOrNonFiniteRequests()
        {
            var state = Area();
            state.SetZoomFactor(2f);
            state.SetZoomFactor(float.NaN);
            state.SetZoomFactor(-3f);
            state.SetZoomFactor(0f);
            Assert.AreEqual(2f, state.ZoomFactor, 0.0001f);
        }

        [Test]
        public void ZoomAnchorHoldsAtEveryCornerWhenNotClamped()
        {
            // Minimap: no clamp, so the anchor identity must hold exactly for any pointer position.
            foreach (var pointer in new[] { new Vector2(0f, 0f), new Vector2(240f, 0f), new Vector2(240f, 240f), new Vector2(0f, 240f), new Vector2(60f, 190f) })
            {
                var state = Minimap();
                state.CenterOn(new Vector2(120f, -340f));
                var worldBefore = state.Projection.ViewportToWorld(pointer);

                state.ZoomAt(pointer, 2.5f);

                AssertVector(worldBefore, state.Projection.ViewportToWorld(pointer), 0.01f);
            }
        }

        // ----- pan ----------------------------------------------------------------------------------------

        [Test]
        public void DraggingRightAndDownMovesTheCentreWestAndNorth()
        {
            var state = Minimap();
            state.CenterOn(Vector2.zero);

            state.PanByViewportDelta(new Vector2(30f, 0f));   // pointer right -> world slides right -> centre moves west
            AssertVector(new Vector2(-10f, 0f), state.Center);   // 30 / scale 3

            state.PanByViewportDelta(new Vector2(0f, 30f));   // pointer down -> centre moves north (+Z)
            AssertVector(new Vector2(-10f, 10f), state.Center);
        }

        [Test]
        public void DraggedImageMovesWithThePointer()
        {
            var state = Area();
            state.ZoomAtCenter(2f);
            var before = state.Projection.WorldToViewport(10f, 5f);

            state.PanByViewportDelta(new Vector2(-24f, 16f));

            AssertVector(before + new Vector2(-24f, 16f), state.Projection.WorldToViewport(10f, 5f));
        }

        // ----- area clamping ------------------------------------------------------------------------------

        [Test]
        public void FittedAreaMapCannotBePannedAwayFromCentre()
        {
            var state = Area();
            state.PanByViewportDelta(new Vector2(500f, 500f));
            AssertVector(Vector2.zero, state.Center);
        }

        [Test]
        public void ZoomedAreaMapPanStopsAtTheImageEdges()
        {
            var state = Area();
            state.ZoomAtCenter(2f); // scale 4: visible span 100 x 75 m

            state.PanByViewportDelta(new Vector2(-100000f, 100000f)); // drag left = centre east, drag down = centre north
            AssertVector(new Vector2(50f, 12.5f), state.Center);

            state.PanByViewportDelta(new Vector2(200000f, -200000f));
            AssertVector(new Vector2(-50f, -12.5f), state.Center);
        }

        [Test]
        public void ClampCentresAnAxisThatIsShorterThanTheViewport()
        {
            // Zoom 2x on a tall image in a wide viewport: width fits with room to spare, height does not.
            var state = new MapViewportState(new Rect(0f, 0f, 100f, 400f), MapViewportOptions.Area());
            state.SetViewportSize(new Vector2(400f, 200f)); // fit scale 0.5; image 50x200 pts, centred
            state.ZoomAtCenter(2f);                          // scale 1: visible 400 x 200 m, image 100 wide

            state.PanByViewportDelta(new Vector2(300f, 0f));

            Assert.AreEqual(50f, state.Center.x, 0.001f);   // still centred on x: the image is narrower than the view
        }

        [Test]
        public void WheelZoomAtAnEdgeIsClampedWhenTheAnchorCannotBeHeld()
        {
            var state = Area();
            state.ZoomAt(new Vector2(399f, 299f), 8f); // far bottom-right of the viewport, huge zoom

            var half = new Vector2(state.ViewportSize.x / state.Scale, state.ViewportSize.y / state.Scale) * 0.5f;
            Assert.LessOrEqual(state.Center.x + half.x, Bounds.xMax + 0.001f);
            Assert.GreaterOrEqual(state.Center.y - half.y, Bounds.yMin - 0.001f);
        }

        [Test]
        public void FitLocationRestoresTheFittedScaleAndImageCentre()
        {
            var state = Area();
            state.ZoomAt(new Vector2(350f, 80f), 4f);
            state.PanByViewportDelta(new Vector2(-40f, 20f));

            state.Fit();

            Assert.AreEqual(1f, state.ZoomFactor, 0.0001f);
            AssertVector(Bounds.center, state.Center);
            Assert.AreEqual(2f, state.Scale, 0.0001f);
        }

        [Test]
        public void RecenterKeepsTheCurrentZoomAndIsClampedToTheImage()
        {
            var state = Area();
            state.ZoomAtCenter(2f);

            state.CenterOn(new Vector2(30f, 10f));
            Assert.AreEqual(2f, state.ZoomFactor, 0.0001f);
            AssertVector(new Vector2(30f, 10f), state.Center);

            state.CenterOn(new Vector2(99f, -49f)); // right at the south-east corner -> pulled back so no blank space shows
            AssertVector(new Vector2(50f, -12.5f), state.Center);
        }

        // ----- minimap follow -----------------------------------------------------------------------------

        [Test]
        public void MinimapKeepsTheTrackedPointCentredEvenOutsideTheImage()
        {
            var state = Minimap(new Rect(-100f, -100f, 200f, 200f));

            state.CenterOn(new Vector2(100f, 100f)); // exactly the north-east corner
            AssertVector(new Vector2(100f, 100f), state.Center);
            AssertVector(new Vector2(120f, 120f), state.Projection.WorldToViewport(100f, 100f));

            state.CenterOn(new Vector2(500f, -500f)); // far outside: still centred, never silently moved
            AssertVector(new Vector2(500f, -500f), state.Center);
        }

        [Test]
        public void MinimapCentreSurvivesAResize()
        {
            var state = Minimap();
            state.CenterOn(new Vector2(75f, -25f));
            state.SetViewportSize(new Vector2(180f, 180f));
            AssertVector(new Vector2(75f, -25f), state.Center);
            Assert.AreEqual(180f / 80f, state.Scale, 0.0001f);
        }

        [Test]
        public void MinimapIgnoresNonFiniteCentres()
        {
            var state = Minimap();
            state.CenterOn(new Vector2(10f, 20f));
            state.CenterOn(new Vector2(float.NaN, 0f));
            state.CenterOn(new Vector2(0f, float.PositiveInfinity));
            AssertVector(new Vector2(10f, 20f), state.Center);
        }

        [Test]
        public void MinimapStartsAtTheImageCentreBeforeAnyFollowTarget()
        {
            var state = Minimap(new Rect(40f, 60f, 100f, 100f));
            AssertVector(new Vector2(90f, 110f), state.Center);
        }

        [Test]
        public void TwoViewsOverOneImageNeverShareState()
        {
            var minimap = Minimap(Bounds);
            var area = Area();
            minimap.CenterOn(new Vector2(70f, 30f));
            area.ZoomAtCenter(4f);

            AssertVector(Vector2.zero, area.Center);
            Assert.AreEqual(1f, minimap.ZoomFactor);
        }
    }
}

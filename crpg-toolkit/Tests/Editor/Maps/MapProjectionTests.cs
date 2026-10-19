using CRPG.Maps;
using NUnit.Framework;
using UnityEngine;

namespace CRPG.Tests.EditMode
{
    public class MapProjectionTests
    {
        // Independently calculated fixture: bounds x -100..100, z -50..50 (a 2:1 image) in a 400x300 viewport
        // at 2 points/metre, centred on the origin. The image is 400x200 points, so 50 points of letterbox
        // above and below.
        private static readonly Rect Bounds = new Rect(-100f, -50f, 200f, 100f);
        private static readonly Vector2 Viewport = new Vector2(400f, 300f);

        private static MapProjection Fixture(float scale = 2f, float cx = 0f, float cz = 0f) =>
            new MapProjection(Bounds, Viewport, new Vector2(cx, cz), scale);

        private static void AssertVector(Vector2 expected, Vector2 actual, float tolerance = 0.001f)
        {
            Assert.AreEqual(expected.x, actual.x, tolerance, $"x of {actual}");
            Assert.AreEqual(expected.y, actual.y, tolerance, $"y of {actual}");
        }

        [Test]
        public void NorthwestCornerProjectsToTheLeftOfTheLetterboxedImage()
        {
            AssertVector(new Vector2(0f, 50f), Fixture().WorldToViewport(-100f, 50f));
        }

        [Test]
        public void CentreProjectsToTheViewportCentre()
        {
            AssertVector(new Vector2(200f, 150f), Fixture().WorldToViewport(0f, 0f));
        }

        [Test]
        public void SoutheastCornerProjectsToTheRightOfTheLetterboxedImage()
        {
            AssertVector(new Vector2(400f, 250f), Fixture().WorldToViewport(100f, -50f));
        }

        [Test]
        public void NortheastAndSouthwestCornersProject()
        {
            AssertVector(new Vector2(400f, 50f), Fixture().WorldToViewport(100f, 50f));
            AssertVector(new Vector2(0f, 250f), Fixture().WorldToViewport(-100f, -50f));
        }

        [Test]
        public void NorthIsTowardTheTopAndEastIsTowardTheRight()
        {
            var p = Fixture();
            Assert.Less(p.WorldToViewport(0f, 10f).y, p.WorldToViewport(0f, 0f).y, "+Z must move up (smaller y)");
            Assert.Greater(p.WorldToViewport(10f, 0f).x, p.WorldToViewport(0f, 0f).x, "+X must move right");
        }

        [Test]
        public void PointerAtThreeHundredOneHundredIsWorldFiftyTwentyFive()
        {
            AssertVector(new Vector2(50f, 25f), Fixture().ViewportToWorld(new Vector2(300f, 100f)));
        }

        [Test]
        public void ImageRectIsLetterboxedWithFiftyPointMargins()
        {
            var rect = Fixture().ImageRect;
            Assert.AreEqual(0f, rect.x, 0.001f);
            Assert.AreEqual(50f, rect.y, 0.001f);
            Assert.AreEqual(400f, rect.width, 0.001f);
            Assert.AreEqual(200f, rect.height, 0.001f);
        }

        [Test]
        public void ImageRectCornersMatchMarkerProjectionOfTheBoundsCorners()
        {
            // The image and the markers must use the same formula: the image's top-left is the projection of the
            // world north-west corner, and its bottom-right is the projection of the south-east corner.
            var p = Fixture(scale: 3f, cx: 17f, cz: -9f);
            var rect = p.ImageRect;
            AssertVector(p.WorldToViewport(Bounds.xMin, Bounds.yMax), new Vector2(rect.xMin, rect.yMin));
            AssertVector(p.WorldToViewport(Bounds.xMax, Bounds.yMin), new Vector2(rect.xMax, rect.yMax));
        }

        [Test]
        public void ImageRectKeepsTheWorldAspectRatioAtAnyScale()
        {
            foreach (var scale in new[] { 0.5f, 1f, 2f, 7.3f })
            {
                var rect = Fixture(scale).ImageRect;
                Assert.AreEqual(Bounds.width / Bounds.height, rect.width / rect.height, 0.0001f);
            }
        }

        [Test]
        public void UvUsesABottomLeftOrigin()
        {
            var p = Fixture();
            AssertVector(new Vector2(0f, 0f), p.WorldToUv(-100f, -50f)); // south-west = UV origin
            AssertVector(new Vector2(0f, 1f), p.WorldToUv(-100f, 50f));  // north-west = top-left of the image
            AssertVector(new Vector2(1f, 1f), p.WorldToUv(100f, 50f));
            AssertVector(new Vector2(1f, 0f), p.WorldToUv(100f, -50f));
            AssertVector(new Vector2(0.5f, 0.5f), p.WorldToUv(0f, 0f));
        }

        [Test]
        public void UvDoesNotDependOnViewportCentreOrScale()
        {
            AssertVector(Fixture().WorldToUv(30f, -20f), Fixture(scale: 9f, cx: 40f, cz: 40f).WorldToUv(30f, -20f));
        }

        [Test]
        public void UvRoundTripsThroughWorld()
        {
            var p = Fixture();
            var world = p.UvToWorld(p.WorldToUv(-37.5f, 12.25f));
            AssertVector(new Vector2(-37.5f, 12.25f), world);
        }

        [Test]
        public void ViewportRoundTripsThroughWorldAtEveryCorner()
        {
            var p = Fixture(scale: 3.5f, cx: -12f, cz: 8f);
            foreach (var corner in new[] { new Vector2(-100f, 50f), new Vector2(100f, 50f), new Vector2(100f, -50f), new Vector2(-100f, -50f), Vector2.zero })
            {
                AssertVector(corner, p.ViewportToWorld(p.WorldToViewport(corner.x, corner.y)));
            }
        }

        [Test]
        public void NegativeOriginAndAsymmetricBoundsProjectCorrectly()
        {
            // x -300..-100 (200 wide), z 40..340 (300 tall): nothing symmetric about the origin.
            var bounds = new Rect(-300f, 40f, 200f, 300f);
            var p = new MapProjection(bounds, new Vector2(200f, 300f), new Vector2(-200f, 190f), 1f);

            AssertVector(new Vector2(100f, 150f), p.WorldToViewport(-200f, 190f));
            AssertVector(new Vector2(0f, 0f), p.WorldToViewport(-300f, 340f));   // north-west
            AssertVector(new Vector2(200f, 300f), p.WorldToViewport(-100f, 40f)); // south-east
            AssertVector(new Vector2(0f, 1f), p.WorldToUv(-300f, 340f));
            AssertVector(new Vector2(1f, 0f), p.WorldToUv(-100f, 40f));
        }

        [Test]
        public void WorldHeightDoesNotAffectProjection()
        {
            var p = Fixture();
            AssertVector(p.WorldToViewport(new Vector3(12f, 0f, -7f)), p.WorldToViewport(new Vector3(12f, 250f, -7f)));
            AssertVector(p.WorldToViewport(12f, -7f), p.WorldToViewport(new Vector3(12f, -3f, -7f)));
        }

        [Test]
        public void ResizingTheViewportKeepsTheCentreAtTheViewportCentre()
        {
            foreach (var size in new[] { new Vector2(400f, 300f), new Vector2(240f, 240f), new Vector2(1000f, 90f) })
            {
                var p = new MapProjection(Bounds, size, new Vector2(33f, -14f), 2f);
                AssertVector(size * 0.5f, p.WorldToViewport(33f, -14f));
            }
        }

        [Test]
        public void DefaultMinimapScaleIsThreePointsPerMetre()
        {
            Assert.AreEqual(3f, 240f / 80f, 0f);
            var p = new MapProjection(Bounds, new Vector2(240f, 240f), Vector2.zero, 240f / 80f);
            // 40 m east of centre lands 120 points right of the 120-point centre: the right edge.
            AssertVector(new Vector2(240f, 120f), p.WorldToViewport(40f, 0f));
        }

        [Test]
        public void FitScaleIsTheTighterAxis()
        {
            Assert.AreEqual(2f, MapProjection.FitScale(Bounds, Viewport), 0.0001f);   // min(400/200, 300/100)
            Assert.AreEqual(1.2f, MapProjection.FitScale(new Rect(0f, 0f, 100f, 100f), new Vector2(300f, 120f)), 0.0001f);
        }

        [Test]
        public void FitScaleIsZeroForUnusableInput()
        {
            Assert.AreEqual(0f, MapProjection.FitScale(Bounds, Vector2.zero));
            Assert.AreEqual(0f, MapProjection.FitScale(new Rect(0f, 0f, 0f, 10f), Viewport));
        }

        [Test]
        public void ContainsWorldIncludesTheEdgesAndExcludesOutside()
        {
            var p = Fixture();
            Assert.IsTrue(p.ContainsWorld(-100f, 50f));
            Assert.IsTrue(p.ContainsWorld(100f, -50f));
            Assert.IsFalse(p.ContainsWorld(100.01f, 0f));
            Assert.IsFalse(p.ContainsWorld(0f, -50.01f));
        }

        [Test]
        public void ContainsViewportClipsAtTheViewportBoundary()
        {
            var p = Fixture();
            Assert.IsTrue(p.ContainsViewport(new Vector2(0f, 0f)));
            Assert.IsTrue(p.ContainsViewport(new Vector2(400f, 300f)));
            Assert.IsFalse(p.ContainsViewport(new Vector2(-0.5f, 10f)));
            Assert.IsFalse(p.ContainsViewport(new Vector2(10f, 300.5f)));
        }

        [Test]
        public void DefaultProjectionIsNotValid()
        {
            Assert.IsFalse(default(MapProjection).IsValid);
        }

        [Test]
        public void ZeroSizedViewportOrScaleIsNotValid()
        {
            Assert.IsFalse(new MapProjection(Bounds, Vector2.zero, Vector2.zero, 2f).IsValid);
            Assert.IsFalse(new MapProjection(Bounds, Viewport, Vector2.zero, 0f).IsValid);
            Assert.IsFalse(new MapProjection(Bounds, Viewport, new Vector2(float.NaN, 0f), 2f).IsValid);
            Assert.IsTrue(Fixture().IsValid);
        }
    }
}

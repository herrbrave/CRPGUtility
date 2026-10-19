using System;
using System.Collections.Generic;
using CRPG.Core;
using CRPG.Maps;
using CRPG.UI;
using NUnit.Framework;
using UnityEngine;

namespace CRPG.Tests.EditMode
{
    public class LocationMapPresenterTests
    {
        private readonly List<UnityEngine.Object> created = new();
        private FakeMarkerSource source;
        private LocationMapPresenter presenter;

        [SetUp]
        public void SetUp()
        {
            source = new FakeMarkerSource();
            presenter = new LocationMapPresenter(MapTestSupport.WideDefinition(created), source);
            presenter.Minimap.SetViewportSize(new Vector2(240f, 240f));
            presenter.Area.SetViewportSize(new Vector2(400f, 300f));
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in created) if (o != null) UnityEngine.Object.DestroyImmediate(o);
            created.Clear();
        }

        private static void AssertVector(Vector2 expected, Vector2 actual, float tolerance = 0.001f)
        {
            Assert.AreEqual(expected.x, actual.x, tolerance, $"x of {actual}");
            Assert.AreEqual(expected.y, actual.y, tolerance, $"y of {actual}");
        }

        [Test]
        public void RejectsAnInvalidDefinitionWithTheProblemsListed()
        {
            var texture = new Texture2D(8, 8);
            created.Add(texture);
            var bad = LocationMapDefinition.Create("x", "X", texture, MapTestSupport.WideBounds); // square image, 2:1 world
            created.Add(bad);

            var ex = Assert.Throws<InvalidOperationException>(() => new LocationMapPresenter(bad, source));
            StringAssert.Contains("aspect", ex.Message);
        }

        [Test]
        public void RequiresADefinitionAndASource()
        {
            Assert.Throws<ArgumentNullException>(() => new LocationMapPresenter(null, source));
            Assert.Throws<ArgumentNullException>(() => new LocationMapPresenter(MapTestSupport.WideDefinition(created), null));
        }

        [Test]
        public void MinimapAndAreaOverTheSameImageStartIndependent()
        {
            Assert.AreEqual(3f, presenter.Minimap.Scale, 0.0001f);
            Assert.AreEqual(2f, presenter.Area.Scale, 0.0001f);
            Assert.AreNotSame(presenter.Minimap, presenter.Area);
        }

        [Test]
        public void SamplingFillsTheSharedSnapshotOnce()
        {
            source.Markers.Add(MapMarkerSnapshot.ForLandmark("l", "dock", "Dock", new Vector2(1f, 2f)));

            presenter.SampleNow();

            Assert.AreEqual(1, source.CollectCount);
            Assert.AreEqual(1, presenter.Markers.Count);
            Assert.AreSame(presenter.Markers, presenter.Markers, "views must read one shared list");
        }

        [Test]
        public void MinimapFollowsTheFocus()
        {
            source.Focus = new Vector2(30f, -20f);
            presenter.SampleNow();
            AssertVector(new Vector2(30f, -20f), presenter.Minimap.Center);
            Assert.IsTrue(presenter.FocusAvailable);
            Assert.IsTrue(presenter.HasObservedFocus);
        }

        [Test]
        public void MinimapStaysCentredOnTheFocusEvenNearTheImageEdge()
        {
            source.Focus = new Vector2(99f, 49f); // almost the north-east corner
            presenter.SampleNow();
            AssertVector(new Vector2(120f, 120f), presenter.Minimap.Projection.WorldToViewport(99f, 49f));
        }

        [Test]
        public void MissingFocusHoldsTheLastValidCentreAndFlagsUnavailable()
        {
            source.Focus = new Vector2(30f, -20f);
            presenter.SampleNow();

            source.Focus = null;
            presenter.SampleNow();

            AssertVector(new Vector2(30f, -20f), presenter.Minimap.Center);
            Assert.IsFalse(presenter.FocusAvailable);
            Assert.IsTrue(presenter.HasObservedFocus);
        }

        [Test]
        public void FocusReturningRestoresFollowingAndAvailability()
        {
            source.Focus = new Vector2(30f, -20f);
            presenter.SampleNow();
            source.Focus = null;
            presenter.SampleNow();

            source.Focus = new Vector2(-10f, 5f);
            presenter.SampleNow();

            AssertVector(new Vector2(-10f, 5f), presenter.Minimap.Center);
            Assert.IsTrue(presenter.FocusAvailable);
        }

        [Test]
        public void BeforeAnyFocusTheMinimapIsAtTheImageCentreNotTheWorldOrigin()
        {
            var texture = new Texture2D(16, 16);
            created.Add(texture);
            var offset = LocationMapDefinition.Create("off", "Off", texture, new Rect(400f, 600f, 100f, 100f));
            created.Add(offset);
            var p = new LocationMapPresenter(offset, source);
            p.Minimap.SetViewportSize(new Vector2(240f, 240f));

            source.Focus = null;
            p.SampleNow();

            AssertVector(new Vector2(450f, 650f), p.Minimap.Center);
            Assert.IsFalse(p.FocusAvailable);
            Assert.IsFalse(p.HasObservedFocus);
        }

        [Test]
        public void NonFiniteFocusIsTreatedAsUnavailable()
        {
            source.Focus = new Vector2(10f, 10f);
            presenter.SampleNow();

            source.Focus = new Vector2(float.NaN, 0f);
            presenter.SampleNow();

            AssertVector(new Vector2(10f, 10f), presenter.Minimap.Center);
            Assert.IsFalse(presenter.FocusAvailable);
        }

        [Test]
        public void TickSamplesAtTwentyHertz()
        {
            Assert.IsTrue(presenter.Tick(0f));
            Assert.IsFalse(presenter.Tick(0.02f));
            Assert.IsFalse(presenter.Tick(0.049f));
            Assert.IsTrue(presenter.Tick(0.051f));
            Assert.AreEqual(2, source.CollectCount);
        }

        [Test]
        public void TickWorksWhenTheClockStartsAtALargeValue()
        {
            Assert.IsTrue(presenter.Tick(12345.6f));
            Assert.IsFalse(presenter.Tick(12345.61f));
        }

        [Test]
        public void SampledFiresAfterEverySample()
        {
            var count = 0;
            presenter.Sampled += () => count++;
            presenter.SampleNow();
            presenter.Tick(1f);
            presenter.Tick(1.01f); // too soon
            Assert.AreEqual(2, count);
        }

        [Test]
        public void SamplingDoesNotTouchTheAreaViewport()
        {
            presenter.Area.ZoomAtCenter(4f);
            var center = presenter.Area.Center;
            source.Focus = new Vector2(80f, 40f);

            presenter.SampleNow();

            AssertVector(center, presenter.Area.Center);
            Assert.AreEqual(4f, presenter.Area.ZoomFactor, 0.0001f);
        }

        [Test]
        public void FitAreaRestoresTheWholeLocation()
        {
            presenter.Area.ZoomAt(new Vector2(350f, 80f), 4f);
            presenter.FitArea();
            Assert.AreEqual(1f, presenter.Area.ZoomFactor, 0.0001f);
            AssertVector(MapTestSupport.WideBounds.center, presenter.Area.Center);
        }

        [Test]
        public void RecenterOnActiveKeepsTheZoomAndTargetsTheActiveCharacter()
        {
            source.Focus = new Vector2(30f, 10f);
            presenter.SampleNow();
            presenter.Area.ZoomAtCenter(2f);

            presenter.RecenterAreaOnActive();

            Assert.AreEqual(2f, presenter.Area.ZoomFactor, 0.0001f);
            AssertVector(new Vector2(30f, 10f), presenter.Area.Center);
        }

        [Test]
        public void RecenterIsANoOpUntilTheActiveCharacterHasBeenSeen()
        {
            presenter.Area.ZoomAtCenter(2f);
            var before = presenter.Area.Center;

            presenter.RecenterAreaOnActive();

            AssertVector(before, presenter.Area.Center);
        }

        [Test]
        public void RecenterUsesTheLastValidPositionWhenTheActiveActorIsGone()
        {
            source.Focus = new Vector2(-40f, 10f);
            presenter.SampleNow();
            presenter.Area.ZoomAtCenter(2f);
            source.Focus = null;
            presenter.SampleNow();

            presenter.RecenterAreaOnActive();

            AssertVector(new Vector2(-40f, 10f), presenter.Area.Center);
        }
    }
}

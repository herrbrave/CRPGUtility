using System;
using System.Collections.Generic;
using CRPG.Maps;
using CRPG.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace CRPG.Tests.EditMode
{
    public class LocationMapHudTests
    {
        private sealed class CountingLease : IDisposable
        {
            public int Disposed;
            public void Dispose() => Disposed++;
        }

        private readonly List<UnityEngine.Object> created = new();
        private FakeMarkerSource source;
        private LocationMapHud hud;
        private VisualElement region;
        private VisualElement documentRoot;
        private LocationMapDefinition definition;

        [SetUp]
        public void SetUp()
        {
            source = new FakeMarkerSource { Focus = new Vector2(10f, 5f) };
            definition = MapTestSupport.WideDefinition(created);
            region = new VisualElement();
            documentRoot = new VisualElement();
            var go = new GameObject("MapHud");
            created.Add(go);
            hud = go.AddComponent<LocationMapHud>();
        }

        [TearDown]
        public void TearDown()
        {
            hud.CloseAreaMap(); // release the static modal scope even if a test failed midway
            foreach (var o in created) if (o != null) UnityEngine.Object.DestroyImmediate(o);
            created.Clear();
        }

        private void Init() => hud.Initialize(definition, source, region, documentRoot);

        // ----- initialization --------------------------------------------------------------------------------

        [Test]
        public void InitializeBuildsTheMinimapIntoTheRegionAndTheOverlayIntoTheDocument()
        {
            Init();

            Assert.AreEqual(1, region.childCount);
            Assert.IsNotNull(region.Q("minimap-panel"));
            Assert.AreEqual(1, documentRoot.childCount);
            Assert.IsNotNull(documentRoot.Q("area-map-overlay"));
            Assert.IsTrue(hud.IsInitialized);
            Assert.IsFalse(hud.IsAreaMapOpen);
        }

        [Test]
        public void TheMinimapHasATitleNorthIndicatorZoomButtonsAndAnExplicitOpenButton()
        {
            Init();
            Assert.AreEqual("Test Location", region.Q<Label>("minimap-title").text);
            Assert.AreEqual("N", region.Q<Label>("minimap-north").text);
            Assert.IsNotNull(region.Q<Button>("minimap-zoom-in"));
            Assert.IsNotNull(region.Q<Button>("minimap-zoom-out"));
            Assert.AreEqual("Open Map", region.Q<Button>("minimap-open-map").text);
        }

        [Test]
        public void TheAreaOverlayHasTitleLegendAndTheFourControls()
        {
            Init();
            var overlay = documentRoot.Q("area-map-overlay");
            Assert.AreEqual("Test Location", overlay.Q<Label>("area-map-title").text);
            Assert.IsNotNull(overlay.Q<Button>("area-zoom-in"));
            Assert.IsNotNull(overlay.Q<Button>("area-zoom-out"));
            Assert.IsNotNull(overlay.Q<Button>("area-fit"));
            Assert.IsNotNull(overlay.Q<Button>("area-recenter"));
            Assert.IsNotNull(overlay.Q<Button>("area-close"));
            Assert.AreEqual(4, overlay.Q("area-map-legend").childCount);
        }

        [Test]
        public void TheAreaOverlayStartsHiddenAndTheMinimapRootOnlyPicksItsOwnRegion()
        {
            Init();
            Assert.AreEqual(DisplayStyle.None, documentRoot.Q("area-map-overlay").style.display.value);
            Assert.AreEqual(PickingMode.Position, documentRoot.Q("area-map-overlay").pickingMode);
        }

        [Test]
        public void InitializeWithAnInvalidDefinitionThrowsBeforeBuildingAnything()
        {
            var texture = new Texture2D(8, 8);
            created.Add(texture);
            var bad = LocationMapDefinition.Create("x", "X", texture, MapTestSupport.WideBounds);
            created.Add(bad);

            Assert.Throws<InvalidOperationException>(() => hud.Initialize(bad, source, region, documentRoot));

            Assert.AreEqual(0, region.childCount);
            Assert.AreEqual(0, documentRoot.childCount);
            Assert.IsFalse(hud.IsInitialized);
        }

        [Test]
        public void AFailedReinitializeKeepsTheWorkingMapRunning()
        {
            Init();
            var texture = new Texture2D(8, 8);
            created.Add(texture);
            var bad = LocationMapDefinition.Create("x", "X", texture, MapTestSupport.WideBounds);
            created.Add(bad);

            Assert.Throws<InvalidOperationException>(() => hud.Initialize(bad, source, region, documentRoot));

            Assert.IsTrue(hud.IsInitialized);
            Assert.AreEqual(1, region.childCount);
        }

        [Test]
        public void ReinitializingDoesNotDuplicateTheUi()
        {
            Init();
            Init();
            Init();

            Assert.AreEqual(1, region.childCount);
            Assert.AreEqual(1, documentRoot.childCount);
        }

        [Test]
        public void ReinitializingRebindsToANewHostWithoutLeavingTheOldOneUsed()
        {
            Init();
            var otherRegion = new VisualElement();
            var otherRoot = new VisualElement();

            hud.Initialize(definition, source, otherRegion, otherRoot);

            Assert.AreEqual(0, region.childCount);
            Assert.AreEqual(0, documentRoot.childCount);
            Assert.AreEqual(1, otherRegion.childCount);
            Assert.AreEqual(1, otherRoot.childCount);
        }

        [Test]
        public void ReinitializingWhileOpenReleasesTheOpenMapsLeasesOnce()
        {
            var lease = new CountingLease();
            hud.SuspendWorldInput = () => lease;
            Init();
            hud.OpenAreaMap();

            Init();

            Assert.IsFalse(hud.IsAreaMapOpen);
            Assert.AreEqual(1, lease.Disposed);
            Assert.IsFalse(new UIToolkitPointerCapture().IsPointerOverUI(new Vector2(-9999f, -9999f)), "modal must be released");
        }

        [Test]
        public void InitializeSamplesImmediatelyAndShowsTheMarkers()
        {
            source.Markers.Add(MapMarkerSnapshot.ForLandmark("l", "dock", "Dock", new Vector2(1f, 2f)));
            Init();
            Assert.GreaterOrEqual(source.CollectCount, 1);
            Assert.AreEqual(1, hud.Presenter.Markers.Count);
        }

        // ----- minimap size ----------------------------------------------------------------------------------

        [Test]
        public void TheMinimapStartsAtItsNominalSize()
        {
            Init();
            Assert.AreEqual(240f, hud.MinimapView.Root.style.width.value.value, 0.001f);
            Assert.AreEqual(240f, hud.MinimapView.Root.style.height.value.value, 0.001f);
        }

        // ----- open / close ----------------------------------------------------------------------------------

        [Test]
        public void OpeningShowsTheOverlayFittedToTheLocation()
        {
            Init();
            hud.Presenter.Area.SetViewportSize(new Vector2(400f, 300f));
            hud.ZoomArea(4f);

            Assert.IsTrue(hud.OpenAreaMap());

            Assert.IsTrue(hud.IsAreaMapOpen);
            Assert.AreEqual(DisplayStyle.Flex, documentRoot.Q("area-map-overlay").style.display.value);
            Assert.AreEqual(1f, hud.Presenter.Area.ZoomFactor, 0.0001f, "every opening starts fitted");
        }

        [Test]
        public void EachOpeningStartsFittedAndCentredAgain()
        {
            Init();
            hud.Presenter.Area.SetViewportSize(new Vector2(400f, 300f));
            hud.OpenAreaMap();
            hud.ZoomArea(4f);
            hud.PanArea(new Vector2(30f, 30f));
            hud.CloseAreaMap();

            hud.OpenAreaMap();

            Assert.AreEqual(1f, hud.Presenter.Area.ZoomFactor, 0.0001f);
            Assert.AreEqual(MapTestSupport.WideBounds.center.x, hud.Presenter.Area.Center.x, 0.001f);
            Assert.AreEqual(MapTestSupport.WideBounds.center.y, hud.Presenter.Area.Center.y, 0.001f);
        }

        [Test]
        public void ClosingHidesTheOverlay()
        {
            Init();
            hud.OpenAreaMap();
            hud.CloseAreaMap();
            Assert.IsFalse(hud.IsAreaMapOpen);
            Assert.AreEqual(DisplayStyle.None, documentRoot.Q("area-map-overlay").style.display.value);
        }

        [Test]
        public void ToggleOpensThenCloses()
        {
            Init();
            hud.ToggleAreaMap();
            Assert.IsTrue(hud.IsAreaMapOpen);
            hud.ToggleAreaMap();
            Assert.IsFalse(hud.IsAreaMapOpen);
        }

        [Test]
        public void OpeningHoldsAPointerModalScopeUntilClosed()
        {
            Init();
            var capture = new UIToolkitPointerCapture();
            var far = new Vector2(-9999f, -9999f);
            Assert.IsFalse(capture.IsPointerOverUI(far));

            hud.OpenAreaMap();
            Assert.IsTrue(capture.IsPointerOverUI(far), "the whole screen is captured while the area map is open");

            hud.CloseAreaMap();
            Assert.IsFalse(capture.IsPointerOverUI(far));
        }

        [Test]
        public void OpenAndCloseAcquireAndReleaseEachLeaseExactlyOnce()
        {
            var leases = new List<CountingLease>();
            hud.SuspendWorldInput = () => { var l = new CountingLease(); leases.Add(l); return l; };
            Init();

            hud.OpenAreaMap();
            hud.OpenAreaMap(); // already open: no second lease
            Assert.AreEqual(1, leases.Count);
            Assert.AreEqual(0, leases[0].Disposed);

            hud.CloseAreaMap();
            hud.CloseAreaMap(); // already closed: no double dispose
            Assert.AreEqual(1, leases[0].Disposed);
        }

        [Test]
        public void ReopeningTakesAFreshLeaseRatherThanReusingTheDisposedOne()
        {
            var leases = new List<CountingLease>();
            hud.SuspendWorldInput = () => { var l = new CountingLease(); leases.Add(l); return l; };
            Init();

            hud.OpenAreaMap();
            hud.CloseAreaMap();
            hud.OpenAreaMap();

            Assert.AreEqual(2, leases.Count);
            Assert.AreEqual(1, leases[0].Disposed);
            Assert.AreEqual(0, leases[1].Disposed);
        }

        [Test]
        public void ThePointerModalIsAcquiredBeforeWorldInputIsSuspended()
        {
            var capture = new UIToolkitPointerCapture();
            var far = new Vector2(-9999f, -9999f);
            var capturedWhenSuspending = false;
            hud.SuspendWorldInput = () => { capturedWhenSuspending = capture.IsPointerOverUI(far); return new CountingLease(); };
            Init();

            hud.OpenAreaMap();

            Assert.IsTrue(capturedWhenSuspending);
        }

        [Test]
        public void ThePointerModalIsReleasedBeforeWorldInputResumes()
        {
            var capture = new UIToolkitPointerCapture();
            var far = new Vector2(-9999f, -9999f);
            var capturedAtResume = true;
            var lease = new ActionLease(() => capturedAtResume = capture.IsPointerOverUI(far));
            hud.SuspendWorldInput = () => lease;
            Init();
            hud.OpenAreaMap();

            hud.CloseAreaMap();

            Assert.IsFalse(capturedAtResume);
        }

        private sealed class ActionLease : IDisposable
        {
            private readonly Action onDispose;
            public ActionLease(Action onDispose) => this.onDispose = onDispose;
            public void Dispose() => onDispose();
        }

        [Test]
        public void TheHostCanRefuseOpeningWhileAModalIsUp()
        {
            Init();
            var modalOpen = true;
            hud.CanOpenAreaMap = () => !modalOpen;

            Assert.IsFalse(hud.OpenAreaMap());
            Assert.IsFalse(hud.IsAreaMapOpen);
            Assert.IsFalse(new UIToolkitPointerCapture().IsPointerOverUI(new Vector2(-9999f, -9999f)));

            modalOpen = false;
            Assert.IsTrue(hud.OpenAreaMap());
        }

        [Test]
        public void ARefusedOpenAcquiresNoLease()
        {
            var acquired = 0;
            hud.SuspendWorldInput = () => { acquired++; return new CountingLease(); };
            hud.CanOpenAreaMap = () => false;
            Init();

            hud.OpenAreaMap();

            Assert.AreEqual(0, acquired);
        }

        [Test]
        public void OpeningNeverTouchesTheTimeScale()
        {
            var before = Time.timeScale;
            Init();
            hud.OpenAreaMap();
            Assert.AreEqual(before, Time.timeScale);
            hud.CloseAreaMap();
            Assert.AreEqual(before, Time.timeScale);
        }

        [Test]
        public void OpenBeforeInitializeIsRefused()
        {
            Assert.IsFalse(hud.OpenAreaMap());
            Assert.DoesNotThrow(() => hud.CloseAreaMap());
        }

        // ----- area view commands ----------------------------------------------------------------------------

        [Test]
        public void AreaZoomPanFitAndRecenterDriveTheAreaViewport()
        {
            Init();
            hud.Presenter.Area.SetViewportSize(new Vector2(400f, 300f));
            hud.OpenAreaMap();

            hud.ZoomArea(2f);
            Assert.AreEqual(2f, hud.Presenter.Area.ZoomFactor, 0.0001f);

            hud.PanArea(new Vector2(-40f, 0f));
            Assert.Greater(hud.Presenter.Area.Center.x, 0f);

            hud.RecenterArea();   // focus is (10, 5)
            Assert.AreEqual(10f, hud.Presenter.Area.Center.x, 0.001f);
            Assert.AreEqual(2f, hud.Presenter.Area.ZoomFactor, 0.0001f);

            hud.FitArea();
            Assert.AreEqual(1f, hud.Presenter.Area.ZoomFactor, 0.0001f);
            Assert.AreEqual(0f, hud.Presenter.Area.Center.x, 0.001f);
        }

        [Test]
        public void MinimapZoomDoesNotChangeTheAreaMap()
        {
            Init();
            hud.Presenter.Minimap.SetViewportSize(new Vector2(240f, 240f));
            var areaZoom = hud.Presenter.Area.ZoomFactor;

            hud.ZoomMinimap(2f);

            Assert.AreEqual(2f, hud.Presenter.Minimap.ZoomFactor, 0.0001f);
            Assert.AreEqual(areaZoom, hud.Presenter.Area.ZoomFactor);
        }

        [Test]
        public void AreaZoomDoesNotChangeTheMinimap()
        {
            Init();
            hud.Presenter.Area.SetViewportSize(new Vector2(400f, 300f));
            hud.ZoomArea(3f);
            Assert.AreEqual(1f, hud.Presenter.Minimap.ZoomFactor);
        }

        // ----- both views, one snapshot ----------------------------------------------------------------------

        [Test]
        public void BothViewsCreateTheSameMarkersFromOneSample()
        {
            source.Markers.Add(MapMarkerSnapshot.ForLandmark("l", "dock", "Dock", new Vector2(1f, 2f)));
            Init();
            hud.Presenter.Minimap.SetViewportSize(new Vector2(240f, 240f));
            hud.Presenter.Area.SetViewportSize(new Vector2(400f, 300f));
            hud.OpenAreaMap();

            Assert.AreEqual(1, hud.MinimapView.MarkerVisualCount);
            Assert.AreEqual(1, hud.AreaView.MarkerVisualCount);
        }

        [Test]
        public void TheUnavailableStateShowsWhenTheActiveActorIsMissing()
        {
            Init();
            var label = region.Q<Label>("minimap-unavailable");
            Assert.AreEqual(DisplayStyle.None, label.style.display.value);

            source.Focus = null;
            hud.Presenter.SampleNow();
            Assert.AreEqual(DisplayStyle.Flex, label.style.display.value);

            source.Focus = new Vector2(1f, 1f);
            hud.Presenter.SampleNow();
            Assert.AreEqual(DisplayStyle.None, label.style.display.value);
        }

        [Test]
        public void TheMinimapCanBeDisabledWhileAModalOwnsTheScreen()
        {
            Init();
            hud.SetMinimapInteractable(false);
            Assert.IsFalse(hud.MinimapPanel.enabledSelf);
            hud.SetMinimapInteractable(true);
            Assert.IsTrue(hud.MinimapPanel.enabledSelf);
        }
    }
}

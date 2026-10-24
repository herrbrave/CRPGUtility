using System;
using System.Collections;
using System.Collections.Generic;
using CRPG.Maps;
using CRPG.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace CRPG.Tests.PlayMode
{
    /// <summary>The map HUD against a real UIDocument panel: real layout passes, real button handling, real
    /// OnDisable/OnDestroy delivery -- the things EditMode (no panel, no layout, no OnDestroy) cannot observe.</summary>
    public class LocationMapHudPlayTests
    {
        private sealed class Source : IMapMarkerSource
        {
            public readonly List<MapMarkerSnapshot> Markers = new();
            public Vector2? Focus = new Vector2(10f, 5f);
            public int Collects;

            public void Collect(List<MapMarkerSnapshot> buffer)
            {
                Collects++;
                buffer.Clear();
                buffer.AddRange(Markers);
            }

            public bool TryGetFocusXZ(out Vector2 focusXZ)
            {
                focusXZ = Focus ?? default;
                return Focus.HasValue;
            }
        }

        private sealed class Lease : IDisposable
        {
            public int Disposed;
            public void Dispose() => Disposed++;
        }

        private static readonly Rect Bounds = new Rect(-100f, -50f, 200f, 100f);

        private GameObject root;
        private LocationMapHud hud;
        private UIDocument document;
        private VisualElement region;
        private Source source;
        private LocationMapDefinition definition;
        private Texture2D texture;
        private PanelSettings panelSettings;
        private Lease lease;

        [SetUp]
        public void SetUp()
        {
            texture = new Texture2D(64, 32);
            definition = LocationMapDefinition.Create("play-loc", "Play Location", texture, Bounds);
            source = new Source();
            lease = new Lease();
        }

        [TearDown]
        public void TearDown()
        {
            if (hud != null) hud.CloseAreaMap();
            if (root != null) UnityEngine.Object.DestroyImmediate(root);
            if (panelSettings != null) UnityEngine.Object.DestroyImmediate(panelSettings);
            foreach (var o in created) if (o != null) UnityEngine.Object.DestroyImmediate(o);
            created.Clear();
            UnityEngine.Object.DestroyImmediate(definition);
            UnityEngine.Object.DestroyImmediate(texture);
        }

        private IEnumerator Build()
        {
            root = new GameObject("MapHudPlay");
            panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            panelSettings.scaleMode = PanelScaleMode.ConstantPixelSize;
            panelSettings.scale = 1f;
            document = root.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            yield return null;

            region = new VisualElement();
            region.style.position = Position.Absolute;
            region.style.right = 16;
            region.style.top = 16;
            document.rootVisualElement.Add(region);

            hud = root.AddComponent<LocationMapHud>();
            hud.SuspendWorldInput = () => lease;
            hud.Initialize(definition, source, region, document.rootVisualElement);
            for (var i = 0; i < 4; i++) yield return null; // real layout passes
        }

        private static void Submit(Button button)
        {
            using var evt = NavigationSubmitEvent.GetPooled();
            evt.target = button;
            button.SendEvent(evt);
        }

        [UnityTest]
        public IEnumerator TheMinimapGetsARealLayoutAndAlignsItsMarkers()
        {
            source.Markers.Add(MapMarkerSnapshot.ForLandmark("play-loc", "dock", "Dock", new Vector2(0f, 0f)));
            yield return Build();

            Assert.IsTrue(hud.Presenter.Minimap.HasLayout);
            var size = hud.MinimapView.Root.resolvedStyle.width;
            Assert.Greater(size, 90f);
            Assert.AreEqual(hud.MinimapView.Root.resolvedStyle.height, size, 0.5f, "the minimap is square");

            hud.MinimapView.TryGetMarkerVisual(MapMarkerKeys.Landmark("play-loc", "dock"), out var dock);
            // The minimap is centred on the focus (10, 5); the dock at (0, 0) is 10 m west and 5 m south of it.
            var scale = hud.Presenter.Minimap.Scale;
            var expectedX = size * 0.5f - 10f * scale;
            var expectedY = size * 0.5f + 5f * scale;
            var box = dock.resolvedStyle;
            Assert.AreEqual(expectedX, box.left + box.width * 0.5f, 1f);
            Assert.AreEqual(expectedY, box.top + box.height * 0.5f, 1f);
        }

        [UnityTest]
        public IEnumerator TheMinimapRegionOnlyCoversItsOwnArea()
        {
            yield return Build();
            Assert.Less(region.resolvedStyle.width, 400f);
            Assert.Less(region.resolvedStyle.height, 400f);
        }

        [UnityTest]
        public IEnumerator TheOpenMapAndCloseButtonsWork()
        {
            yield return Build();

            Submit(region.Q<Button>("minimap-open-map"));
            Assert.IsTrue(hud.IsAreaMapOpen);
            yield return null;
            yield return null;
            Assert.IsTrue(hud.Presenter.Area.HasLayout, "the overlay lays out once visible");

            Submit(document.rootVisualElement.Q<Button>("area-close"));
            Assert.IsFalse(hud.IsAreaMapOpen);
            Assert.AreEqual(1, lease.Disposed);
        }

        [UnityTest]
        public IEnumerator TheAreaMapFitsTheWholeImageWithoutDistortion()
        {
            yield return Build();
            hud.OpenAreaMap();
            for (var i = 0; i < 4; i++) yield return null;

            var rect = hud.AreaView.ImageElement.resolvedStyle;
            Assert.AreEqual(2f, rect.width / rect.height, 0.01f, "a 2:1 location stays 2:1 in any viewport");
            var canvas = hud.AreaView.Root.resolvedStyle;
            Assert.LessOrEqual(rect.width, canvas.width + 0.5f);
            Assert.LessOrEqual(rect.height, canvas.height + 0.5f);
        }

        [UnityTest]
        public IEnumerator DisablingTheHudWhileOpenReleasesEverything()
        {
            yield return Build();
            hud.OpenAreaMap();

            hud.enabled = false;

            Assert.IsFalse(hud.IsAreaMapOpen);
            Assert.AreEqual(1, lease.Disposed);
            Assert.IsFalse(new UIToolkitPointerCapture().IsPointerOverUI(new Vector2(-9999f, -9999f)));
        }

        [UnityTest]
        public IEnumerator DestroyingTheHudWhileOpenReleasesLeasesAndRemovesItsUi()
        {
            yield return Build();
            hud.OpenAreaMap();
            var overlayHost = document.rootVisualElement;

            UnityEngine.Object.DestroyImmediate(hud);
            hud = null;

            Assert.AreEqual(1, lease.Disposed);
            Assert.IsFalse(new UIToolkitPointerCapture().IsPointerOverUI(new Vector2(-9999f, -9999f)));
            Assert.AreEqual(0, region.childCount, "the minimap panel is removed");
            Assert.IsNull(overlayHost.Q("area-map-overlay"), "the overlay is removed");
        }

        [UnityTest]
        public IEnumerator TheHudSamplesOnItsOwnWhileRunning()
        {
            yield return Build();
            var before = source.Collects;

            var deadline = Time.unscaledTime + 0.4f;
            while (Time.unscaledTime < deadline) yield return null;

            Assert.Greater(source.Collects, before, "Update must tick the presenter");
        }

        [UnityTest]
        public IEnumerator WheelOverTheAreaMapZoomsAndWheelOverTheMinimapZoomsOnlyTheMinimap()
        {
            yield return Build();
            hud.OpenAreaMap();
            for (var i = 0; i < 4; i++) yield return null;

            var areaZoom = hud.Presenter.Area.ZoomFactor;
            var minimapZoom = hud.Presenter.Minimap.ZoomFactor;

            SendWheel(hud.AreaView.Root, -1f);
            Assert.Greater(hud.Presenter.Area.ZoomFactor, areaZoom);
            Assert.AreEqual(minimapZoom, hud.Presenter.Minimap.ZoomFactor);

            var zoomedArea = hud.Presenter.Area.ZoomFactor;
            SendWheel(hud.MinimapView.Root, -1f);
            Assert.Greater(hud.Presenter.Minimap.ZoomFactor, minimapZoom);
            Assert.AreEqual(zoomedArea, hud.Presenter.Area.ZoomFactor);
        }

        private static void SendWheel(VisualElement target, float deltaY)
        {
            var center = target.worldBound.center;
            using var evt = WheelEvent.GetPooled(new Event { type = EventType.ScrollWheel, delta = new Vector2(0f, deltaY), mousePosition = center });
            evt.target = target;
            target.SendEvent(evt);
        }

        [UnityTest]
        public IEnumerator DraggingTheAreaMapPansItAndReleasingEndsTheDrag()
        {
            yield return Build();
            hud.OpenAreaMap();
            for (var i = 0; i < 4; i++) yield return null;
            hud.ZoomArea(4f);
            var canvas = hud.AreaView.Root;
            var start = canvas.worldBound.center;
            var centerBefore = hud.Presenter.Area.Center;

            SendPointer<PointerDownEvent>(canvas, EventType.MouseDown, start);
            Assert.IsTrue(hud.IsDragging);
            SendPointer<PointerMoveEvent>(canvas, EventType.MouseDrag, start + new Vector2(-40f, 0f));

            Assert.Greater(hud.Presenter.Area.Center.x, centerBefore.x, "dragging left slides the world left, so the centre moves east");

            SendPointer<PointerUpEvent>(canvas, EventType.MouseUp, start + new Vector2(-40f, 0f));
            Assert.IsFalse(hud.IsDragging);
        }

        [UnityTest]
        public IEnumerator ClosingTheMapWhileDraggingEndsTheDragAndReleasesTheLease()
        {
            yield return Build();
            hud.OpenAreaMap();
            for (var i = 0; i < 4; i++) yield return null;
            var canvas = hud.AreaView.Root;

            SendPointer<PointerDownEvent>(canvas, EventType.MouseDown, canvas.worldBound.center);
            Assert.IsTrue(hud.IsDragging);

            hud.CloseAreaMap();

            Assert.IsFalse(hud.IsDragging);
            Assert.AreEqual(1, lease.Disposed);
        }

        // ----- clicks (cards and pins are the host's; the HUD only says where and what) -------------------------

        [UnityTest]
        public IEnumerator ACleanClickOnBareGroundRaisesAnAreaClickWithTheWorldPositionAndNoPan()
        {
            yield return Build();
            hud.OpenAreaMap();
            for (var i = 0; i < 4; i++) yield return null;
            MapClick? seen = null; hud.AreaClicked += c => seen = c;
            var canvas = hud.AreaView.Root; var at = canvas.worldBound.center + new Vector2(20f, -10f);
            var centreBefore = hud.Presenter.Area.Center;
            SendPointer<PointerDownEvent>(canvas, EventType.MouseDown, at);
            SendPointer<PointerUpEvent>(canvas, EventType.MouseUp, at);
            Assert.IsTrue(seen.HasValue, "a press and release without moving is a click");
            Assert.AreEqual(0, seen.Value.Button); Assert.IsFalse(seen.Value.OnMarker);
            var expected = hud.Presenter.Area.Projection.ViewportToWorld(canvas.WorldToLocal(at));
            Assert.AreEqual(expected.x, seen.Value.WorldXZ.x, 0.01f); Assert.AreEqual(expected.y, seen.Value.WorldXZ.y, 0.01f);
            Assert.AreEqual(centreBefore, hud.Presenter.Area.Center, "a click does not pan");
            Assert.IsFalse(hud.IsDragging);
        }

        [UnityTest]
        public IEnumerator ARightClickIsAClickWithButtonOneAndNeverPans()
        {
            yield return Build();
            hud.OpenAreaMap();
            for (var i = 0; i < 4; i++) yield return null;
            MapClick? seen = null; hud.AreaClicked += c => seen = c;
            var canvas = hud.AreaView.Root; var at = canvas.worldBound.center; var centreBefore = hud.Presenter.Area.Center;
            SendPointer<PointerDownEvent>(canvas, EventType.MouseDown, at, 1);
            Assert.IsFalse(hud.IsDragging, "the right button never starts a pan");
            SendPointer<PointerMoveEvent>(canvas, EventType.MouseDrag, at + new Vector2(2f, 0f), 1);
            SendPointer<PointerUpEvent>(canvas, EventType.MouseUp, at + new Vector2(2f, 0f), 1);
            Assert.IsTrue(seen.HasValue); Assert.AreEqual(1, seen.Value.Button);
            Assert.AreEqual(centreBefore, hud.Presenter.Area.Center);
        }

        [UnityTest]
        public IEnumerator ADragPastTheThresholdPansAndIsNotAClick()
        {
            yield return Build();
            hud.OpenAreaMap();
            for (var i = 0; i < 4; i++) yield return null;
            hud.ZoomArea(4f);
            var clicks = 0; hud.AreaClicked += _ => clicks++;
            var canvas = hud.AreaView.Root; var start = canvas.worldBound.center; var centreBefore = hud.Presenter.Area.Center;
            SendPointer<PointerDownEvent>(canvas, EventType.MouseDown, start);
            SendPointer<PointerMoveEvent>(canvas, EventType.MouseDrag, start + new Vector2(-30f, 0f));
            SendPointer<PointerUpEvent>(canvas, EventType.MouseUp, start + new Vector2(-30f, 0f));
            Assert.AreEqual(0, clicks, "a drag is not a click"); Assert.AreNotEqual(centreBefore, hud.Presenter.Area.Center);
        }

        [UnityTest]
        public IEnumerator AClickOnALandmarkOrAPinNamesItsKey()
        {
            source.Markers.Add(MapMarkerSnapshot.ForLandmark("play-loc", "temple", "Temple", new Vector2(10f, 5f), null, null, MapGlyphShape.Hexagon));
            source.Markers.Add(MapMarkerSnapshot.ForPin("play-loc", 4, new Vector2(-30f, 10f)));
            yield return Build();
            hud.OpenAreaMap();
            for (var i = 0; i < 4; i++) yield return null;
            var keys = new List<(string key, int button)>(); hud.AreaClicked += c => keys.Add((c.MarkerKey, c.Button));
            var canvas = hud.AreaView.Root;
            foreach (var (key, button) in new[] { (MapMarkerKeys.Landmark("play-loc", "temple"), 0), (MapMarkerKeys.Pin("play-loc", 4), 1) })
            {
                Assert.IsTrue(hud.AreaView.TryGetMarkerVisual(key, out var marker));
                var centre = marker.worldBound.center;
                SendPointer<PointerDownEvent>(marker, EventType.MouseDown, centre, button);
                SendPointer<PointerUpEvent>(canvas, EventType.MouseUp, centre, button);   // the view root holds the capture, so the release arrives there
            }

            CollectionAssert.AreEqual(new[] { (MapMarkerKeys.Landmark("play-loc", "temple"), 0), (MapMarkerKeys.Pin("play-loc", 4), 1) }, keys);
        }

        [UnityTest]
        public IEnumerator TheOverlayCoversBothViewsAndSurvivesAReinitialisation()
        {
            yield return Build();
            var mask = new Texture2D(8, 4); created.Add(mask);
            hud.SetOverlay(mask, new Color(1f, 1f, 1f, 0.7f));
            Assert.AreSame(mask, ((Image)hud.MinimapView.OverlayElement).image); Assert.AreSame(mask, ((Image)hud.AreaView.OverlayElement).image);
            hud.Initialize(definition, source, region, document.rootVisualElement);
            for (var i = 0; i < 3; i++) yield return null;
            Assert.AreSame(mask, ((Image)hud.MinimapView.OverlayElement).image, "a rebuilt HUD keeps the fog");
            hud.ClearOverlay();
            Assert.AreEqual(DisplayStyle.None, hud.AreaView.OverlayElement.style.display.value);
        }

        private readonly List<UnityEngine.Object> created = new();

        private static void SendPointer<T>(VisualElement target, EventType type, Vector2 position, int button = 0) where T : PointerEventBase<T>, new()
        {
            var systemEvent = new Event { type = type, mousePosition = position, button = button };
            using var evt = PointerEventBase<T>.GetPooled(systemEvent);
            evt.target = target;
            target.SendEvent(evt);
        }
    }
}

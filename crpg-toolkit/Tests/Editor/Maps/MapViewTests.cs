using System.Collections.Generic;
using CRPG.Core;
using CRPG.Maps;
using CRPG.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace CRPG.Tests.EditMode
{
    /// <summary>EditMode runs no layout pass, so these drive the view through SetViewportSize and read back the
    /// style values the view itself assigned (style.left etc., never resolvedStyle).</summary>
    public class MapViewTests
    {
        private readonly List<UnityEngine.Object> created = new();
        private LocationMapDefinition definition;
        private MapView view;

        [SetUp]
        public void SetUp() => definition = MapTestSupport.WideDefinition(created);

        [TearDown]
        public void TearDown()
        {
            view?.Dispose();
            view = null;
            foreach (var o in created) if (o != null) UnityEngine.Object.DestroyImmediate(o);
            created.Clear();
        }

        private MapView AreaView(Vector2? size = null, bool hover = true)
        {
            var state = new MapViewportState(MapTestSupport.WideBounds, MapViewportOptions.Area());
            view = new MapView(state, definition, MapViewStyle.Default, hover);
            view.SetViewportSize(size ?? new Vector2(400f, 300f));
            return view;
        }

        private static List<MapMarkerSnapshot> Markers(params MapMarkerSnapshot[] markers) => new(markers);

        private static MapMarkerSnapshot Party(RuntimeEntityId id, float x, float z, MapPartyMarkerState state = MapPartyMarkerState.Unselected) =>
            MapMarkerSnapshot.ForParty(id, "hero", new Vector2(x, z), state);

        private static MapMarkerSnapshot Landmark(string id, float x, float z) =>
            MapMarkerSnapshot.ForLandmark("loc", id, id, new Vector2(x, z));

        private static Vector2 TopLeft(VisualElement e) => new(e.style.left.value.value, e.style.top.value.value);

        // Includes the margin the diamond glyph uses to centre its rotated square inside its marker box.
        private static Vector2 Centre(VisualElement e) =>
            TopLeft(e) + new Vector2(e.style.marginLeft.value.value, e.style.marginTop.value.value)
            + new Vector2(e.style.width.value.value, e.style.height.value.value) * 0.5f;

        // ----- image ---------------------------------------------------------------------------------------

        [Test]
        public void TheImageIsLetterboxedNotStretched()
        {
            var v = AreaView(); // 400x300 viewport over a 2:1 image
            v.Refresh(new List<MapMarkerSnapshot>());
            var image = v.ImageElement;

            Assert.AreEqual(0f, image.style.left.value.value, 0.001f);
            Assert.AreEqual(50f, image.style.top.value.value, 0.001f);
            Assert.AreEqual(400f, image.style.width.value.value, 0.001f);
            Assert.AreEqual(200f, image.style.height.value.value, 0.001f);
            Assert.AreEqual(2f, image.style.width.value.value / image.style.height.value.value, 0.0001f);
        }

        [Test]
        public void TheImageUsesTheDefinitionsTextureWithoutOwningIt()
        {
            var v = AreaView();
            Assert.AreSame(definition.Background, ((Image)v.ImageElement).image);
            v.Dispose();
            Assert.IsNotNull(definition.Background, "a view must never destroy the borrowed texture");
            Assert.IsTrue(definition.Background != null);
        }

        [Test]
        public void TheViewportClipsItsContents()
        {
            Assert.AreEqual(Overflow.Hidden, AreaView().Root.style.overflow.value);
        }

        [Test]
        public void ImageFollowsAZoomedProjection()
        {
            var v = AreaView();
            v.State.ZoomAtCenter(2f);
            v.Refresh(new List<MapMarkerSnapshot>());

            var image = v.ImageElement;
            Assert.AreEqual(800f, image.style.width.value.value, 0.001f);
            Assert.AreEqual(400f, image.style.height.value.value, 0.001f);
            Assert.AreEqual(-200f, image.style.left.value.value, 0.001f);
            Assert.AreEqual(-50f, image.style.top.value.value, 0.001f);
        }

        // ----- markers align with the image ------------------------------------------------------------------

        [Test]
        public void MarkersSitWhereTheProjectionPutsThem()
        {
            var v = AreaView();
            v.Refresh(Markers(Landmark("nw", -100f, 50f), Landmark("c", 0f, 0f), Landmark("se", 100f, -50f)));

            v.TryGetMarkerVisual(MapMarkerKeys.Landmark("loc", "nw"), out var nw);
            v.TryGetMarkerVisual(MapMarkerKeys.Landmark("loc", "c"), out var c);
            v.TryGetMarkerVisual(MapMarkerKeys.Landmark("loc", "se"), out var se);

            Assert.AreEqual(0f, Centre(nw).x, 0.001f);
            Assert.AreEqual(50f, Centre(nw).y, 0.001f);
            Assert.AreEqual(200f, Centre(c).x, 0.001f);
            Assert.AreEqual(150f, Centre(c).y, 0.001f);
            Assert.AreEqual(400f, Centre(se).x, 0.001f);
            Assert.AreEqual(250f, Centre(se).y, 0.001f);
        }

        [Test]
        public void MarkerCornersAlignWithTheImageCorners()
        {
            // The marker at the image's north-west world corner must sit on the image rect's top-left.
            var v = AreaView();
            v.Refresh(Markers(Landmark("nw", -100f, 50f)));
            v.TryGetMarkerVisual(MapMarkerKeys.Landmark("loc", "nw"), out var nw);

            Assert.AreEqual(v.ImageElement.style.left.value.value, Centre(nw).x, 0.001f);
            Assert.AreEqual(v.ImageElement.style.top.value.value, Centre(nw).y, 0.001f);
        }

        [Test]
        public void MarkersAndImageStayAlignedAfterZoomAndPan()
        {
            var v = AreaView();
            var markers = Markers(Landmark("p", 30f, -10f));
            v.State.ZoomAt(new Vector2(300f, 100f), 3f);
            v.State.PanByViewportDelta(new Vector2(-20f, 10f));
            v.Refresh(markers);

            v.TryGetMarkerVisual(MapMarkerKeys.Landmark("loc", "p"), out var p);
            var expected = v.State.Projection.WorldToViewport(30f, -10f);
            Assert.AreEqual(expected.x, Centre(p).x, 0.01f);
            Assert.AreEqual(expected.y, Centre(p).y, 0.01f);
        }

        [Test]
        public void TwoViewsAgreeOnTheSameSnapshot()
        {
            var markers = Markers(Landmark("p", 30f, -10f));
            var minimap = new MapView(new MapViewportState(MapTestSupport.WideBounds, MapViewportOptions.Minimap()),
                definition, MapViewStyle.Default, false);
            minimap.SetViewportSize(new Vector2(240f, 240f));
            var area = AreaView();

            minimap.Refresh(markers);
            area.Refresh(markers);

            minimap.TryGetMarkerVisual(MapMarkerKeys.Landmark("loc", "p"), out var a);
            area.TryGetMarkerVisual(MapMarkerKeys.Landmark("loc", "p"), out var b);
            var inMinimap = minimap.State.Projection.ViewportToWorld(Centre(a));
            var inArea = area.State.Projection.ViewportToWorld(Centre(b));
            Assert.AreEqual(inMinimap.x, inArea.x, 0.01f);
            Assert.AreEqual(inMinimap.y, inArea.y, 0.01f);
            minimap.Dispose();
        }

        [Test]
        public void HeightIsNotPartOfAMarkerPosition()
        {
            // Snapshots carry XZ only, so a hilltop and a valley at the same XZ are the same pixel.
            var v = AreaView();
            v.Refresh(Markers(Landmark("a", 10f, 10f)));
            v.TryGetMarkerVisual(MapMarkerKeys.Landmark("loc", "a"), out var a);
            var first = Centre(a);
            v.Refresh(Markers(Landmark("a", 10f, 10f)));
            Assert.AreEqual(first, Centre(a));
        }

        // ----- visual lifecycle ------------------------------------------------------------------------------

        [Test]
        public void MarkerVisualsAreReusedAcrossRefreshes()
        {
            var id = RuntimeEntityId.NewId();
            var v = AreaView();
            v.Refresh(Markers(Party(id, 0f, 0f)));
            v.TryGetMarkerVisual(MapMarkerKeys.Party(id), out var first);

            v.Refresh(Markers(Party(id, 10f, 5f)));
            v.TryGetMarkerVisual(MapMarkerKeys.Party(id), out var second);

            Assert.AreSame(first, second);
            Assert.AreEqual(1, v.MarkerVisualCount);
            Assert.AreEqual(220f, Centre(second).x, 0.001f);
        }

        [Test]
        public void VisualsAreCreatedAndRemovedOnlyWhenMembershipChanges()
        {
            var a = RuntimeEntityId.NewId();
            var b = RuntimeEntityId.NewId();
            var v = AreaView();

            v.Refresh(Markers(Party(a, 0f, 0f)));
            Assert.AreEqual(1, v.MarkerVisualCount);

            v.Refresh(Markers(Party(a, 0f, 0f), Party(b, 5f, 5f)));
            Assert.AreEqual(2, v.MarkerVisualCount);

            v.Refresh(Markers(Party(b, 5f, 5f)));
            Assert.AreEqual(1, v.MarkerVisualCount);
            Assert.IsFalse(v.TryGetMarkerVisual(MapMarkerKeys.Party(a), out _));
        }

        [Test]
        public void RemovedMarkersLeaveNoVisualInTheHierarchy()
        {
            var id = RuntimeEntityId.NewId();
            var v = AreaView();
            v.Refresh(Markers(Party(id, 0f, 0f)));
            v.Refresh(Markers());

            Assert.AreEqual(0, v.MarkerVisualCount);
            Assert.AreEqual(0, v.Root.Query(className: "map-marker--party").ToList().Count);
        }

        [Test]
        public void RefreshingWithoutLayoutIsASafeNoOp()
        {
            var state = new MapViewportState(MapTestSupport.WideBounds, MapViewportOptions.Area());
            view = new MapView(state, definition, MapViewStyle.Default, true);
            Assert.DoesNotThrow(() => view.Refresh(Markers(Landmark("a", 0f, 0f))));
            Assert.AreEqual(0, view.MarkerVisualCount);
        }

        [Test]
        public void MarkersAppearOnceLayoutArrives()
        {
            var state = new MapViewportState(MapTestSupport.WideBounds, MapViewportOptions.Area());
            view = new MapView(state, definition, MapViewStyle.Default, true);
            view.Refresh(Markers(Landmark("a", 0f, 0f)));

            view.SetViewportSize(new Vector2(400f, 300f)); // layout finally reports a size

            Assert.AreEqual(1, view.MarkerVisualCount);
        }

        [Test]
        public void ResizingReprojectsWithoutANewSample()
        {
            var v = AreaView();
            v.Refresh(Markers(Landmark("e", 100f, 0f)));

            v.SetViewportSize(new Vector2(800f, 600f));

            v.TryGetMarkerVisual(MapMarkerKeys.Landmark("loc", "e"), out var e);
            Assert.AreEqual(800f, Centre(e).x, 0.001f);
            Assert.AreEqual(300f, Centre(e).y, 0.001f);
        }

        // ----- marker states are not colour-only --------------------------------------------------------------

        [Test]
        public void ActiveSelectedAndUnselectedDifferInShapeAndSize()
        {
            var a = RuntimeEntityId.NewId();
            var b = RuntimeEntityId.NewId();
            var c = RuntimeEntityId.NewId();
            var v = AreaView();
            v.Refresh(Markers(
                Party(a, 0f, 0f, MapPartyMarkerState.Active),
                Party(b, 1f, 0f, MapPartyMarkerState.Selected),
                Party(c, 2f, 0f, MapPartyMarkerState.Unselected)));
            v.TryGetMarkerVisual(MapMarkerKeys.Party(a), out var active);
            v.TryGetMarkerVisual(MapMarkerKeys.Party(b), out var selected);
            v.TryGetMarkerVisual(MapMarkerKeys.Party(c), out var unselected);

            // Active: solid fill with an outline. Selected: ring (outline, no fill). Unselected: smallest, no outline.
            Assert.Greater(active.style.backgroundColor.value.a, 0.5f);
            Assert.Greater(active.style.borderTopWidth.value, 0f);
            Assert.AreEqual(0f, selected.style.backgroundColor.value.a, 0.001f);
            Assert.Greater(selected.style.borderTopWidth.value, 0f);
            Assert.Greater(unselected.style.backgroundColor.value.a, 0f);
            Assert.AreEqual(0f, unselected.style.borderTopWidth.value, 0.001f);

            var sizes = new[] { active.style.width.value.value, selected.style.width.value.value, unselected.style.width.value.value };
            Assert.Greater(sizes[0], sizes[1]);
            Assert.Greater(sizes[1], sizes[2]);
        }

        [Test]
        public void AMarkerChangingStateIsRestyledInPlaceNotRecreated()
        {
            var id = RuntimeEntityId.NewId();
            var v = AreaView();
            v.Refresh(Markers(Party(id, 0f, 0f, MapPartyMarkerState.Selected)));
            v.TryGetMarkerVisual(MapMarkerKeys.Party(id), out var before);
            var selectedWidth = before.style.width.value.value;

            v.Refresh(Markers(Party(id, 0f, 0f, MapPartyMarkerState.Active)));
            v.TryGetMarkerVisual(MapMarkerKeys.Party(id), out var after);

            Assert.AreSame(before, after);
            Assert.Greater(after.style.width.value.value, selectedWidth);
            Assert.Greater(after.style.backgroundColor.value.a, 0.5f);
        }

        [Test]
        public void ALandmarkWithoutAnIconGetsTheDefaultDiamondSymbol()
        {
            var v = AreaView();
            v.Refresh(Markers(Landmark("d", 0f, 0f)));
            v.TryGetMarkerVisual(MapMarkerKeys.Landmark("loc", "d"), out var d);

            Assert.AreEqual(45f, d.style.rotate.value.angle.value, 0.01f);
            Assert.Greater(d.style.backgroundColor.value.a, 0.5f);
        }

        [Test]
        public void ThePartyIsDrawnAboveLandmarks()
        {
            var id = RuntimeEntityId.NewId();
            var v = AreaView();
            v.Refresh(Markers(Party(id, 0f, 0f), Landmark("d", 0f, 0f)));
            v.TryGetMarkerVisual(MapMarkerKeys.Party(id), out var party);
            v.TryGetMarkerVisual(MapMarkerKeys.Landmark("loc", "d"), out var landmark);

            var partyLayer = party.parent;
            var landmarkLayer = landmark.parent;
            Assert.AreNotSame(partyLayer, landmarkLayer);
            Assert.Greater(v.Root.IndexOf(partyLayer), v.Root.IndexOf(landmarkLayer));
        }

        // ----- picking ---------------------------------------------------------------------------------------

        [Test]
        public void MarkersAreNotPickableExceptHoverableLandmarks()
        {
            var id = RuntimeEntityId.NewId();
            var v = AreaView(hover: true);
            v.Refresh(Markers(Party(id, 0f, 0f), Landmark("d", 5f, 5f)));
            v.TryGetMarkerVisual(MapMarkerKeys.Party(id), out var party);
            v.TryGetMarkerVisual(MapMarkerKeys.Landmark("loc", "d"), out var landmark);

            Assert.AreEqual(PickingMode.Ignore, party.pickingMode);
            Assert.AreEqual(PickingMode.Position, landmark.pickingMode);
            Assert.AreEqual(PickingMode.Ignore, v.Root.Q("map-image").pickingMode);
        }

        [Test]
        public void WithoutHoverLabelsNoMarkerIsPickable()
        {
            var v = AreaView(hover: false);
            v.Refresh(Markers(Landmark("d", 5f, 5f)));
            v.TryGetMarkerVisual(MapMarkerKeys.Landmark("loc", "d"), out var landmark);
            Assert.AreEqual(PickingMode.Ignore, landmark.pickingMode);
        }

        // ----- hover label -----------------------------------------------------------------------------------

        [Test]
        public void HoveringALandmarkShowsItsNameNearIt()
        {
            var v = AreaView();
            v.Refresh(Markers(Landmark("dock", 0f, 0f)));

            v.SetHoveredMarker(MapMarkerKeys.Landmark("loc", "dock"));

            var label = v.Root.Q<Label>("map-hover-label");
            Assert.AreEqual(DisplayStyle.Flex, label.style.display.value);
            Assert.AreEqual("dock", label.text);
            Assert.Greater(label.style.left.value.value, 200f);
        }

        [Test]
        public void ClearingHoverHidesTheLabel()
        {
            var v = AreaView();
            v.Refresh(Markers(Landmark("dock", 0f, 0f)));
            v.SetHoveredMarker(MapMarkerKeys.Landmark("loc", "dock"));
            v.SetHoveredMarker(null);
            Assert.AreEqual(DisplayStyle.None, v.Root.Q<Label>("map-hover-label").style.display.value);
        }

        [Test]
        public void HoverIsClearedWhenTheHoveredMarkerDisappears()
        {
            var v = AreaView();
            v.Refresh(Markers(Landmark("dock", 0f, 0f)));
            v.SetHoveredMarker(MapMarkerKeys.Landmark("loc", "dock"));

            v.Refresh(Markers());

            Assert.IsNull(v.HoveredKey);
            Assert.AreEqual(DisplayStyle.None, v.Root.Q<Label>("map-hover-label").style.display.value);
        }

        [Test]
        public void TheHoverLabelFollowsTheMarkerAfterAZoom()
        {
            var v = AreaView();
            var markers = Markers(Landmark("dock", 50f, 0f));
            v.Refresh(markers);
            v.SetHoveredMarker(MapMarkerKeys.Landmark("loc", "dock"));
            var label = v.Root.Q<Label>("map-hover-label");
            var before = label.style.left.value.value;

            v.State.ZoomAtCenter(2f);
            v.Refresh(markers);

            Assert.Greater(label.style.left.value.value, before);
        }

        // ----- disposal --------------------------------------------------------------------------------------

        [Test]
        public void DisposeDetachesTheViewAndIsIdempotent()
        {
            var host = new VisualElement();
            var v = AreaView();
            host.Add(v.Root);

            v.Dispose();
            v.Dispose();

            Assert.AreEqual(0, host.childCount);
            Assert.AreEqual(0, v.MarkerVisualCount);
            Assert.DoesNotThrow(() => v.Refresh(Markers(Landmark("a", 0f, 0f))));
            Assert.AreEqual(0, v.MarkerVisualCount, "a disposed view must not rebuild visuals");
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using CRPG.Maps;
using CRPG.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace CRPG.Tests.EditMode
{
    /// <summary>Landmark silhouettes, the player's numbered pins, and the optional overlay layer (a fog-of-war mask) that sits over the map image and follows it exactly.</summary>
    public class MapGlyphAndOverlayTests
    {
        private readonly List<Object> created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in created) if (o != null) Object.DestroyImmediate(o);
            created.Clear();
        }

        // ----- silhouettes -----------------------------------------------------------------------------------

        [Test]
        public void EveryShapeHasItsOwnOutlineInsideTheUnitBox()
        {
            var seen = new HashSet<string>();
            foreach (MapGlyphShape shape in System.Enum.GetValues(typeof(MapGlyphShape)))
            {
                var outline = MapGlyphElement.Outline(shape);
                Assert.GreaterOrEqual(outline.Count, 3, shape + " is a polygon");
                foreach (var p in outline) { Assert.That(p.x, Is.InRange(0f, 1f), shape + " x"); Assert.That(p.y, Is.InRange(0f, 1f), shape + " y"); }
                var signature = string.Join(";", outline.Select(p => $"{p.x:0.00},{p.y:0.00}"));
                // a ring and a circle share their outline on purpose: the ring is drawn open
                if (shape != MapGlyphShape.Ring) Assert.IsTrue(seen.Add(signature), shape + " has the same outline as another shape");
            }
        }

        [Test]
        public void AShapedLandmarkIsDrawnWithItsShapeAndASmallerOrLargerBoxThanTheDiamond()
        {
            var style = MapViewStyle.Default;
            var element = new MapGlyphElement();
            var size = MapMarkerGlyphs.Apply(element, MapMarkerSnapshot.ForLandmark("l", "temple", "Temple", Vector2.zero, null, Color.cyan, MapGlyphShape.Hexagon), style);
            Assert.IsTrue(element.IsDrawn); Assert.AreEqual(MapGlyphShape.Hexagon, element.Shape); Assert.AreEqual(style.ShapedLandmarkSize, size);
            Assert.AreEqual(style.ShapedLandmarkSize, element.style.width.value.value);
        }

        [Test]
        public void TheDefaultDiamondKeepsItsStyledBoxAndAShapedMarkerReturningToItIsCleared()
        {
            var style = MapViewStyle.Default; var element = new MapGlyphElement();
            MapMarkerGlyphs.Apply(element, MapMarkerSnapshot.ForLandmark("l", "a", "A", Vector2.zero, null, null, MapGlyphShape.Star), style);
            Assert.IsTrue(element.IsDrawn);
            var size = MapMarkerGlyphs.Apply(element, MapMarkerSnapshot.ForLandmark("l", "a", "A", Vector2.zero), style);
            Assert.IsFalse(element.IsDrawn, "a diamond is the styled box, nothing is painted");
            Assert.AreEqual(style.LandmarkSize, size); Assert.AreEqual(45f, element.style.rotate.value.angle.value, 0.01f);
        }

        [Test]
        public void ALandmarkWithAnIconIsNeverRedrawnAsAShape()
        {
            var texture = new Texture2D(2, 2); created.Add(texture);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.one * 0.5f); created.Add(sprite);
            var element = new MapGlyphElement();
            MapMarkerGlyphs.Apply(element, MapMarkerSnapshot.ForLandmark("l", "a", "A", Vector2.zero, sprite, null, MapGlyphShape.Star), MapViewStyle.Default);
            Assert.IsFalse(element.IsDrawn);
        }

        // ----- pins ------------------------------------------------------------------------------------------

        [Test]
        public void APinIsKeyedByLocationAndNumberAndDrawnWithItsNumber()
        {
            var pin = MapMarkerSnapshot.ForPin("town", 3, new Vector2(12f, -4f));
            Assert.AreEqual(MapMarkerKind.Pin, pin.Kind); Assert.AreEqual("pin:town:3", pin.Key); Assert.AreEqual(3, pin.Number);
            Assert.AreEqual(MapGlyphShape.Pin, pin.Shape); Assert.AreEqual(new Vector2(12f, -4f), pin.PositionXZ);
            var element = new MapGlyphElement();
            var size = MapMarkerGlyphs.Apply(element, pin, MapViewStyle.Default);
            Assert.AreEqual(MapViewStyle.Default.PinSize, size); Assert.IsTrue(element.IsDrawn);
            Assert.AreEqual("3", element.NumberLabel.text);
        }

        [Test]
        public void ARestyledPinChangesItsNumberAndAShapedMarkerWithoutAMapGlyphElementIsRefused()
        {
            var element = new MapGlyphElement();
            MapMarkerGlyphs.Apply(element, MapMarkerSnapshot.ForPin("town", 1, Vector2.zero), MapViewStyle.Default);
            MapMarkerGlyphs.Apply(element, MapMarkerSnapshot.ForPin("town", 7, Vector2.zero), MapViewStyle.Default);
            Assert.AreEqual("7", element.NumberLabel.text);
            Assert.Throws<System.ArgumentException>(() => MapMarkerGlyphs.Apply(new VisualElement(), MapMarkerSnapshot.ForPin("town", 1, Vector2.zero), MapViewStyle.Default));
        }

        [Test]
        public void TheViewDrawsPinsAndShapedLandmarksAsGlyphElementsAndLetsBothBePointedAt()
        {
            var view = ViewWith(out _, hover: true, MapMarkerSnapshot.ForLandmark("test-location", "gate", "Gate", Vector2.zero, null, null, MapGlyphShape.Cross), MapMarkerSnapshot.ForPin("test-location", 2, new Vector2(10f, 10f)));
            Assert.IsTrue(view.TryGetMarkerVisual(MapMarkerKeys.Landmark("test-location", "gate"), out var gate));
            Assert.IsTrue(view.TryGetMarkerVisual(MapMarkerKeys.Pin("test-location", 2), out var pin));
            Assert.IsInstanceOf<MapGlyphElement>(gate); Assert.IsInstanceOf<MapGlyphElement>(pin);
            Assert.IsTrue(pin.ClassListContains("map-marker--pin")); Assert.IsTrue(gate.ClassListContains("map-marker--landmark"));
            Assert.AreEqual(PickingMode.Position, gate.pickingMode); Assert.AreEqual(PickingMode.Position, pin.pickingMode);
        }

        // ----- overlay ---------------------------------------------------------------------------------------

        [Test]
        public void TheOverlayCoversExactlyTheImageRectangleAndFollowsItsPanAndZoom()
        {
            var view = ViewWith(out var state, hover: false);
            var mask = new Texture2D(4, 2); created.Add(mask);
            view.SetOverlay(mask, new Color(1f, 1f, 1f, 0.8f));
            var overlay = view.OverlayElement;
            Assert.AreEqual(DisplayStyle.Flex, overlay.style.display.value);
            AssertSameRect(view.ImageElement, overlay);
            state.ZoomAtCenter(2f); view.Refresh(null);
            AssertSameRect(view.ImageElement, overlay);
            state.PanByViewportDelta(new Vector2(30f, -10f)); view.Refresh(null);
            AssertSameRect(view.ImageElement, overlay);
            Assert.AreSame(mask, ((Image)overlay).image);
        }

        [Test]
        public void TheOverlayIsBetweenTheImageAndTheMarkersAndNeverTakesThePointer()
        {
            var view = ViewWith(out _, hover: true, MapMarkerSnapshot.ForLandmark("test-location", "a", "A", Vector2.zero));
            var children = view.Root.Children().ToList();
            Assert.Less(children.IndexOf(view.ImageElement), children.IndexOf(view.OverlayElement));
            Assert.Less(children.IndexOf(view.OverlayElement), children.IndexOf(view.Root.Q("map-landmarks")));
            Assert.AreEqual(PickingMode.Ignore, view.OverlayElement.pickingMode);
        }

        [Test]
        public void ANullOverlayHidesTheLayer_AndNoOverlayIsTheDefault()
        {
            var view = ViewWith(out _, hover: false);
            Assert.AreEqual(DisplayStyle.None, view.OverlayElement.style.display.value, "nothing unless asked for");
            var mask = new Texture2D(2, 2); created.Add(mask);
            view.SetOverlay(mask, Color.white); view.SetOverlay(null, Color.white);
            Assert.AreEqual(DisplayStyle.None, view.OverlayElement.style.display.value);
        }

        private MapView ViewWith(out MapViewportState state, bool hover, params MapMarkerSnapshot[] markers)
        {
            var definition = MapTestSupport.WideDefinition(created);
            state = new MapViewportState(definition.TextureWorldBoundsXZ, MapViewportOptions.Area());
            var view = new MapView(state, definition, MapViewStyle.Default, hover);
            view.SetViewportSize(new Vector2(400f, 300f));
            view.Refresh(markers);
            return view;
        }

        private static void AssertSameRect(VisualElement a, VisualElement b)
        {
            Assert.AreEqual(a.style.left.value.value, b.style.left.value.value, 0.01f, "left");
            Assert.AreEqual(a.style.top.value.value, b.style.top.value.value, 0.01f, "top");
            Assert.AreEqual(a.style.width.value.value, b.style.width.value.value, 0.01f, "width");
            Assert.AreEqual(a.style.height.value.value, b.style.height.value.value, 0.01f, "height");
        }
    }
}

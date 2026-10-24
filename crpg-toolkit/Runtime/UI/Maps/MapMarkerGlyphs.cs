using CRPG.Maps;
using UnityEngine;
using UnityEngine.UIElements;

namespace CRPG.UI
{
    /// <summary>
    /// Styles a VisualElement as one marker glyph. Shared by both map views and the legend so a marker looks
    /// identical everywhere. Every call sets every property it relies on, so an element can be re-styled in place
    /// when its marker changes state (selected becoming active) without leaving stale styling behind.
    /// </summary>
    public static class MapMarkerGlyphs
    {
        /// <summary>Applies the glyph for <paramref name="marker"/> and returns the element's box size in UI
        /// points (the caller centres that box on the projected position).</summary>
        public static float Apply(VisualElement element, in MapMarkerSnapshot marker, MapViewStyle style)
        {
            element.style.position = Position.Absolute;
            element.style.rotate = new Rotate(0f);
            element.style.backgroundImage = StyleKeyword.None;
            element.style.unityBackgroundImageTintColor = Color.white;
            element.style.backgroundColor = Color.clear;
            SetBorder(element, 0f, Color.clear);

            var glyph = element as MapGlyphElement;
            if (marker.Kind == MapMarkerKind.Pin || (marker.Kind == MapMarkerKind.Landmark && marker.Icon == null && marker.Shape != MapGlyphShape.Diamond))
            {
                if (glyph == null) throw new System.ArgumentException("A shaped marker needs a MapGlyphElement.", nameof(element));
                var shaped = marker.Kind == MapMarkerKind.Pin ? style.PinSize : style.ShapedLandmarkSize;
                SetBox(element, shaped, shaped);
                element.style.marginLeft = 0f;
                element.style.marginTop = 0f;
                SetRadius(element, 0f);
                glyph.Show(marker.Shape, marker.Tint, style.LandmarkOutline, marker.Number);
                return shaped;
            }

            glyph?.Clear();
            float size;
            if (marker.Kind == MapMarkerKind.Landmark)
            {
                size = style.LandmarkSize;
                SetBox(element, size, size);
                if (marker.Icon != null)
                {
                    element.style.backgroundImage = new StyleBackground(marker.Icon);
                    element.style.unityBackgroundImageTintColor = marker.Tint;
                }
                else
                {
                    // Default landmark symbol: a diamond. Smaller side so the rotated corners fit the box.
                    var side = style.LandmarkDiamondSide;
                    SetBox(element, side, side);
                    element.style.marginLeft = (size - side) * 0.5f;
                    element.style.marginTop = (size - side) * 0.5f;
                    element.style.backgroundColor = marker.Tint;
                    element.style.rotate = new Rotate(45f);
                    SetBorder(element, 1f, style.LandmarkOutline);
                    SetRadius(element, 0f);
                    return size;
                }

                element.style.marginLeft = 0f;
                element.style.marginTop = 0f;
                SetRadius(element, 0f);
                return size;
            }

            element.style.marginLeft = 0f;
            element.style.marginTop = 0f;
            switch (marker.PartyState)
            {
                case MapPartyMarkerState.Active:
                    size = style.ActiveSize;
                    SetBox(element, size, size);
                    element.style.backgroundColor = style.ActiveFill;
                    SetBorder(element, style.ActiveOutlineWidth, style.ActiveOutline);
                    break;
                case MapPartyMarkerState.Selected:
                    size = style.SelectedSize;
                    SetBox(element, size, size);
                    SetBorder(element, style.SelectedRingWidth, style.SelectedRing);
                    break;
                default:
                    size = style.UnselectedSize;
                    SetBox(element, size, size);
                    element.style.backgroundColor = style.UnselectedFill;
                    break;
            }

            SetRadius(element, size * 0.5f);
            return size;
        }

        private static void SetBox(VisualElement element, float width, float height)
        {
            element.style.width = width;
            element.style.height = height;
        }

        private static void SetRadius(VisualElement element, float radius)
        {
            element.style.borderTopLeftRadius = radius;
            element.style.borderTopRightRadius = radius;
            element.style.borderBottomLeftRadius = radius;
            element.style.borderBottomRightRadius = radius;
        }

        private static void SetBorder(VisualElement element, float width, Color color)
        {
            element.style.borderTopWidth = width;
            element.style.borderRightWidth = width;
            element.style.borderBottomWidth = width;
            element.style.borderLeftWidth = width;
            element.style.borderTopColor = color;
            element.style.borderRightColor = color;
            element.style.borderBottomColor = color;
            element.style.borderLeftColor = color;
        }
    }
}

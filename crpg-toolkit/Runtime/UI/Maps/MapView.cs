using System;
using System.Collections.Generic;
using CRPG.Maps;
using UnityEngine;
using UnityEngine.UIElements;

namespace CRPG.UI
{
    /// <summary>
    /// One clipped map viewport: the shared background image plus a marker overlay, both positioned from the same
    /// <see cref="MapProjection"/>, so the image and its markers cannot disagree. Used twice (minimap and area map),
    /// each over its own <see cref="MapViewportState"/>. Plain C# over a VisualElement tree; it reads no input and
    /// owns no pointer capture -- the HUD that hosts it decides how a view can be panned or zoomed.
    /// </summary>
    /// <remarks>
    /// Marker visuals are created and removed only when the set of marker keys changes; a moving marker just gets
    /// new left/top values. The background texture is borrowed and never destroyed here.
    /// </remarks>
    public sealed class MapView : IDisposable
    {
        private sealed class MarkerEntry
        {
            public VisualElement Element;
            public MapMarkerKind Kind;
            public MapPartyMarkerState PartyState;
            public Sprite Icon;
            public Color Tint;
            public MapGlyphShape Shape;
            public int Number;
            public string Label;
            public float Size;
            public Vector2 Local;
            public int Generation;
        }

        private readonly MapViewStyle style;
        private readonly bool hoverLabels;
        private readonly Image image;
        private readonly Image overlayImage;
        private readonly VisualElement landmarkLayer;
        private readonly VisualElement partyLayer;
        private readonly Label hoverLabel;
        private readonly Dictionary<string, MarkerEntry> entries = new();
        private readonly List<string> staleKeys = new();
        private IReadOnlyList<MapMarkerSnapshot> lastMarkers;
        private int generation;
        private string hoveredKey;
        private bool disposed;

        public MapView(MapViewportState state, LocationMapDefinition definition, MapViewStyle style, bool hoverLabels)
        {
            State = state ?? throw new ArgumentNullException(nameof(state));
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            this.style = style ?? MapViewStyle.Default;
            this.hoverLabels = hoverLabels;

            Root = new VisualElement { name = "map-view" };
            Root.AddToClassList("map-view");
            Root.style.overflow = Overflow.Hidden;
            Root.style.backgroundColor = this.style.ViewportBackground;

            image = new Image { name = "map-image", image = definition.Background, scaleMode = ScaleMode.StretchToFill };
            image.pickingMode = PickingMode.Ignore;
            image.style.position = Position.Absolute;
            Root.Add(image);

            // an optional layer over the image and under every marker (the fog of war): always the image's own rectangle
            overlayImage = new Image { name = "map-overlay", scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore };
            overlayImage.style.position = Position.Absolute;
            overlayImage.style.display = DisplayStyle.None;
            Root.Add(overlayImage);

            landmarkLayer = CreateLayer("map-landmarks");
            partyLayer = CreateLayer("map-party");

            hoverLabel = new Label { name = "map-hover-label", pickingMode = PickingMode.Ignore };
            hoverLabel.style.position = Position.Absolute;
            hoverLabel.style.display = DisplayStyle.None;
            hoverLabel.style.backgroundColor = this.style.HoverLabelBackground;
            hoverLabel.style.color = this.style.TextColor;
            hoverLabel.style.paddingLeft = 6;
            hoverLabel.style.paddingRight = 6;
            hoverLabel.style.paddingTop = 2;
            hoverLabel.style.paddingBottom = 2;
            hoverLabel.style.fontSize = 12;
            Root.Add(hoverLabel);

            Root.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        }

        /// <summary>The clipped viewport element. The host sizes and places it.</summary>
        public VisualElement Root { get; }

        public MapViewportState State { get; }

        /// <summary>The key of the landmark currently showing a hover label, or null.</summary>
        public string HoveredKey => hoveredKey;

        public int MarkerVisualCount => entries.Count;

        public bool TryGetMarkerVisual(string key, out VisualElement visual)
        {
            if (entries.TryGetValue(key, out var entry))
            {
                visual = entry.Element;
                return true;
            }

            visual = null;
            return false;
        }

        /// <summary>The element showing the background image (position/size set from the projection).</summary>
        public VisualElement ImageElement => image;

        /// <summary>The overlay layer (null texture: hidden).</summary>
        public VisualElement OverlayElement => overlayImage;

        /// <summary>Puts a texture over the background and under the markers, covering exactly the same world rectangle as the image and following the same pan and zoom (a fog-of-war mask: its alpha is the veil). Borrowed, never destroyed here. Null removes it.</summary>
        public void SetOverlay(Texture overlay, Color tint)
        {
            overlayImage.image = overlay;
            overlayImage.tintColor = tint;
            overlayImage.style.display = overlay != null ? DisplayStyle.Flex : DisplayStyle.None;
            Refresh(lastMarkers);
        }

        /// <summary>Feeds the viewport size (UI points) into the state, then re-lays-out. Normally driven by
        /// layout; also callable directly.</summary>
        public void SetViewportSize(Vector2 size)
        {
            State.SetViewportSize(size);
            Refresh(lastMarkers);
        }

        /// <summary>Repositions the image and every marker from the current state. <paramref name="markers"/> is
        /// the presenter's shared snapshot; it is remembered (not copied) so a later layout change can re-lay-out
        /// without waiting for the next sample.</summary>
        public void Refresh(IReadOnlyList<MapMarkerSnapshot> markers)
        {
            if (disposed) return;
            lastMarkers = markers;

            var projection = State.Projection;
            if (!projection.IsValid)
            {
                // No layout yet: nothing can be placed honestly. Leave existing visuals alone.
                return;
            }

            var imageRect = projection.ImageRect;
            image.style.left = imageRect.x;
            image.style.top = imageRect.y;
            image.style.width = imageRect.width;
            image.style.height = imageRect.height;
            overlayImage.style.left = imageRect.x;
            overlayImage.style.top = imageRect.y;
            overlayImage.style.width = imageRect.width;
            overlayImage.style.height = imageRect.height;

            generation++;
            if (markers != null)
            {
                for (var i = 0; i < markers.Count; i++)
                {
                    var marker = markers[i];
                    if (!entries.TryGetValue(marker.Key, out var entry))
                    {
                        entry = CreateEntry(marker);
                        entries.Add(marker.Key, entry);
                    }
                    else if (NeedsRestyle(entry, marker))
                    {
                        Restyle(entry, marker);
                    }

                    entry.Label = marker.Label;
                    entry.Generation = generation;

                    var local = projection.WorldToViewport(marker.PositionXZ.x, marker.PositionXZ.y);
                    entry.Local = local;
                    var half = entry.Size * 0.5f;
                    entry.Element.style.left = local.x - half;
                    entry.Element.style.top = local.y - half;
                }
            }

            RemoveStaleEntries();
            UpdateHoverLabel(projection);
        }

        /// <summary>Shows the hover label for a landmark (the pointer-enter handler's target; null clears it).</summary>
        public void SetHoveredMarker(string key)
        {
            hoveredKey = key;
            UpdateHoverLabel(State.Projection);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Root.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            foreach (var entry in entries.Values) UnregisterHover(entry.Element);
            entries.Clear();
            hoveredKey = null;
            Root.RemoveFromHierarchy();
        }

        private VisualElement CreateLayer(string layerName)
        {
            var layer = new VisualElement { name = layerName, pickingMode = PickingMode.Ignore };
            layer.style.position = Position.Absolute;
            layer.style.left = 0;
            layer.style.top = 0;
            layer.style.right = 0;
            layer.style.bottom = 0;
            Root.Add(layer);
            return layer;
        }

        private void OnGeometryChanged(GeometryChangedEvent evt)
        {
            SetViewportSize(evt.newRect.size);
        }

        private MarkerEntry CreateEntry(in MapMarkerSnapshot marker)
        {
            var element = marker.Kind == MapMarkerKind.Party ? new VisualElement { name = "map-marker" } : new MapGlyphElement { name = "map-marker" };
            element.AddToClassList(marker.Kind == MapMarkerKind.Party ? "map-marker--party" : marker.Kind == MapMarkerKind.Pin ? "map-marker--pin" : "map-marker--landmark");
            element.userData = marker.Key;

            var entry = new MarkerEntry { Element = element };
            Restyle(entry, marker);

            if (marker.Kind != MapMarkerKind.Party)
            {
                landmarkLayer.Add(element);
                if (hoverLabels)
                {
                    element.pickingMode = PickingMode.Position;
                    element.RegisterCallback<PointerEnterEvent>(OnMarkerPointerEnter);
                    element.RegisterCallback<PointerLeaveEvent>(OnMarkerPointerLeave);
                }
                else
                {
                    element.pickingMode = PickingMode.Ignore;
                }
            }
            else
            {
                element.pickingMode = PickingMode.Ignore;
                partyLayer.Add(element);
            }

            return entry;
        }

        private static bool NeedsRestyle(MarkerEntry entry, in MapMarkerSnapshot marker) =>
            entry.Kind != marker.Kind || entry.PartyState != marker.PartyState
            || entry.Icon != marker.Icon || entry.Tint != marker.Tint || entry.Shape != marker.Shape || entry.Number != marker.Number;

        private void Restyle(MarkerEntry entry, in MapMarkerSnapshot marker)
        {
            entry.Kind = marker.Kind;
            entry.PartyState = marker.PartyState;
            entry.Icon = marker.Icon;
            entry.Tint = marker.Tint;
            entry.Shape = marker.Shape;
            entry.Number = marker.Number;
            entry.Size = MapMarkerGlyphs.Apply(entry.Element, marker, style);

            // The active character is always drawn above the other party markers.
            if (marker.Kind == MapMarkerKind.Party && marker.PartyState == MapPartyMarkerState.Active)
                entry.Element.BringToFront();
        }

        private void RemoveStaleEntries()
        {
            staleKeys.Clear();
            foreach (var pair in entries)
                if (pair.Value.Generation != generation) staleKeys.Add(pair.Key);

            foreach (var key in staleKeys)
            {
                var entry = entries[key];
                UnregisterHover(entry.Element);
                entry.Element.RemoveFromHierarchy();
                entries.Remove(key);
                if (hoveredKey == key) hoveredKey = null;
            }
        }

        private void UpdateHoverLabel(MapProjection projection)
        {
            if (hoveredKey == null || !projection.IsValid || !entries.TryGetValue(hoveredKey, out var entry))
            {
                hoverLabel.style.display = DisplayStyle.None;
                return;
            }

            hoverLabel.text = string.IsNullOrEmpty(entry.Label) ? hoveredKey : entry.Label;
            hoverLabel.style.display = DisplayStyle.Flex;
            hoverLabel.style.left = entry.Local.x + entry.Size * 0.5f + 4f;
            hoverLabel.style.top = entry.Local.y - style.HoverLabelOffset;
        }

        private void UnregisterHover(VisualElement element)
        {
            element.UnregisterCallback<PointerEnterEvent>(OnMarkerPointerEnter);
            element.UnregisterCallback<PointerLeaveEvent>(OnMarkerPointerLeave);
        }

        private void OnMarkerPointerEnter(PointerEnterEvent evt)
        {
            if (evt.target is VisualElement element && element.userData is string key) SetHoveredMarker(key);
        }

        private void OnMarkerPointerLeave(PointerLeaveEvent evt)
        {
            if (evt.target is VisualElement element && element.userData is string key && key == hoveredKey) SetHoveredMarker(null);
        }
    }
}

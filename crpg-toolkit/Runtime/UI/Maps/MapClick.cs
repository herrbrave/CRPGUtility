using UnityEngine;

namespace CRPG.UI
{
    /// <summary>A click on the area map (a press and release that did not become a drag): where in the world, which mouse button, and the marker under the pointer when there was one. Raised by <see cref="LocationMapHud.AreaClicked"/>; the host decides what it means (a card for a place, a pin on empty ground).</summary>
    public readonly struct MapClick
    {
        public MapClick(Vector2 worldXZ, Vector2 viewportLocal, int button, string markerKey)
        {
            WorldXZ = worldXZ; ViewportLocal = viewportLocal; Button = button; MarkerKey = markerKey;
        }

        /// <summary>World (x, z) under the pointer.</summary>
        public Vector2 WorldXZ { get; }

        /// <summary>The pointer in the area view's own UI points.</summary>
        public Vector2 ViewportLocal { get; }

        /// <summary>0 = left, 1 = right.</summary>
        public int Button { get; }

        /// <summary>The key (<see cref="CRPG.Maps.MapMarkerKeys"/>) of the landmark or pin that was pressed, or null for bare ground.</summary>
        public string MarkerKey { get; }

        public bool OnMarker => MarkerKey != null;
    }
}

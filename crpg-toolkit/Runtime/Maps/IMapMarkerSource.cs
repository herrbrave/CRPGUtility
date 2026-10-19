using System.Collections.Generic;
using UnityEngine;

namespace CRPG.Maps
{
    /// <summary>
    /// Supplies the markers for one location's map. Deliberately not a registry: whoever composes the HUD hands
    /// the one source to the presenter, and the presenter samples it into a buffer it owns.
    /// </summary>
    public interface IMapMarkerSource
    {
        /// <summary>Clears <paramref name="buffer"/> and refills it with the current markers. The caller owns
        /// the list and reuses it, so an implementation must not allocate a new collection per call.</summary>
        void Collect(List<MapMarkerSnapshot> buffer);

        /// <summary>The world (x, z) the minimap should follow (the active character). False when it is
        /// temporarily unavailable; the presenter then holds the last valid centre rather than inventing one.</summary>
        bool TryGetFocusXZ(out Vector2 focusXZ);
    }
}

using CRPG.Core;
using UnityEngine;

namespace CRPG.Gameplay
{
    /// <summary>
    /// A game's say over WHAT in the world a pointer can reach (optional; <see cref="PlayerCommandRouter.WorldFilter"/> is null by default and the router then behaves exactly as before). For a building drawn one floor at a time: the upper floor's ground, walls and people are still there for
    /// physics, navigation and doors, but a click must not stop on a floor the viewer cannot see, and a drag box must not sweep up a character on another floor. The filter only DECLINES; it never redirects a click, and it changes nothing in the world.
    /// </summary>
    public interface IPointerWorldFilter
    {
        /// <summary>False: the pointer ray passes through this collider as if it were not there (a hidden floor, a roof). Called for each collider the ray crosses, nearest first, until one is pickable.</summary>
        bool IsPickable(Collider collider);

        /// <summary>False: this character is not caught by a drag-selection box (it is on a floor that is not shown).</summary>
        bool IsSelectable(RuntimeEntityId id, Vector3 worldPosition);
    }
}

using UnityEngine;

namespace CRPG.Gameplay
{
    /// <summary>Reserved extension point for the design's "targeting" priority tier (captured UI/modal ->
    /// targeting -> interactable/selectable -> ground) -- e.g. a future "select an ability's target" mode that
    /// should consume the next click instead of it resolving to a normal Select/Move. No implementation exists
    /// yet (no ability-targeting system to plug in); <see cref="PlayerCommandRouter.TargetingGate"/> is null by
    /// default and this tier is skipped entirely until something sets it.</summary>
    public interface IPointerTargetingGate
    {
        /// <returns>True if this click was consumed by the active targeting session (normal Select/Move/
        /// Interact resolution is skipped for it); false to fall through to the next priority tier.</returns>
        bool TryConsume(Ray ray);
    }
}

using UnityEngine;

namespace CRPG.Core
{
    /// <summary>
    /// Answers "would a click at this screen position (or right now, for a modal) be captured by UI instead of
    /// the game world" -- consulted by <see cref="IPlayerCommandSource"/> producers before resolving a pointer
    /// gesture into a world-space command. Lives in Core (not a UI assembly) specifically so a Gameplay-side
    /// resolver and a UI-side implementation (UI Toolkit, uGUI, ...) can share this contract without Gameplay
    /// and UI referencing each other.
    /// </summary>
    public interface IPointerCaptureProvider
    {
        bool IsPointerOverUI(Vector2 screenPosition);
    }
}

using System;
using UnityEngine;

namespace CRPG.Core
{
    /// <summary>
    /// Live visual feedback for an in-progress screen-space drag-select gesture -- deliberately separate from
    /// <see cref="IPlayerCommandSource"/> (not every input scheme has a screen-space pointer drag to visualize,
    /// and a UI-layer consumer of this needs no dependency on the rest of the command-source machinery). Defined
    /// here (not in a UI or input assembly) so both <c>PlayerCommandRouter</c> (<c>CRPG.Gameplay</c>) and a UI
    /// overlay (<c>CRPG.UI</c>) can reference the SAME type without either assembly needing the other -- both
    /// already reference <c>CRPG.Core</c>, and the alternative (the overlay referencing the router's own
    /// assembly directly) would cycle, since <c>CRPG.Gameplay</c> already references <c>CRPG.UI</c> for
    /// <see cref="IPointerCaptureProvider"/>.
    /// </summary>
    public interface IDragSelectionFeedback
    {
        /// <summary>Fired on every pointer move while a real, unsuppressed drag-select gesture is in progress
        /// (past the click/drag threshold), with the CURRENT screen-space rect -- the exact same rect the
        /// gesture will resolve a selection against if released right now. Never fired for a plain click (never
        /// exceeding the threshold) or while the gesture is suppressed (started over UI/a modal, or the world is
        /// mid-transition).</summary>
        event Action<Rect> DragBoxChanged;

        /// <summary>Fired exactly once per gesture that previously fired <see cref="DragBoxChanged"/>, the
        /// moment that gesture stops being a live, visible drag -- a normal release, a modal beginning mid-drag,
        /// or any other path that invalidates the in-progress gesture. A listener should treat this as "hide the
        /// box now," unconditionally.</summary>
        event Action DragBoxEnded;
    }
}

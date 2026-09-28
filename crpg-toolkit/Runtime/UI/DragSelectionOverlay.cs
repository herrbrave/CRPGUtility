using System;
using UnityEngine;
using UnityEngine.UIElements;
using CRPG.Core;

namespace CRPG.UI
{
    /// <summary>
    /// Live rectangle overlay for an in-progress screen-space drag-select gesture (2D-Campaign-
    /// ExplorationUX-Followups.md D2). Attaches directly to <see cref="HudRoot.DocumentRoot"/> as a plain
    /// sibling element -- NOT through <see cref="HudRoot.CreateRegion"/>, which anchors a fixed, corner-sized
    /// region that defaults to <see cref="PickingMode.Position"/>; a region stretched full-screen to host this
    /// overlay would make <see cref="UIToolkitPointerCapture.IsPointerOverUI"/> true everywhere, swallowing
    /// every world click including the drag's own release (Opus review finding B5). Stays
    /// <see cref="PickingMode.Ignore"/> for the same reason -- visual feedback only, never a click target.
    /// <para>Takes <see cref="IDragSelectionFeedback"/>, never a concrete source type -- <c>CRPG.UI</c> cannot
    /// reference <c>CRPG.Gameplay</c>/<c>CRPG.InputSystem</c> at all (an assembly cycle, both already reference
    /// <c>CRPG.UI</c>); the interface, defined in <c>CRPG.Core</c>, is the only shared seam (finding B1).</para>
    /// </summary>
    public sealed class DragSelectionOverlay : IDisposable
    {
        private readonly VisualElement box;
        private readonly IDragSelectionFeedback feedback;

        public DragSelectionOverlay(VisualElement hudDocumentRoot, IDragSelectionFeedback feedback)
        {
            if (hudDocumentRoot == null) throw new ArgumentNullException(nameof(hudDocumentRoot));
            this.feedback = feedback ?? throw new ArgumentNullException(nameof(feedback));

            box = new VisualElement { name = "drag-select-box", pickingMode = PickingMode.Ignore };
            box.style.position = Position.Absolute;
            box.style.display = DisplayStyle.None;
            box.style.borderTopWidth = 2;
            box.style.borderBottomWidth = 2;
            box.style.borderLeftWidth = 2;
            box.style.borderRightWidth = 2;
            var borderColor = new Color(0.4f, 0.9f, 0.4f, 0.9f);
            box.style.borderTopColor = borderColor;
            box.style.borderBottomColor = borderColor;
            box.style.borderLeftColor = borderColor;
            box.style.borderRightColor = borderColor;
            box.style.backgroundColor = new Color(0.4f, 0.9f, 0.4f, 0.15f);
            hudDocumentRoot.Add(box);

            this.feedback.DragBoxChanged += OnDragBoxChanged;
            this.feedback.DragBoxEnded += OnDragBoxEnded;
        }

        /// <summary>Unsubscribes from the feedback source -- does NOT remove <c>box</c> from its parent, since
        /// a HUD's document root normally outlives this overlay for the whole session anyway; a caller that
        /// needs the element gone too can still remove it via its own reference before disposing.</summary>
        public void Dispose()
        {
            feedback.DragBoxChanged -= OnDragBoxChanged;
            feedback.DragBoxEnded -= OnDragBoxEnded;
        }

        private void OnDragBoxChanged(Rect screenRect)
        {
            var panel = box.panel;
            if (panel == null) return; // detached (or not yet attached) -- nothing to draw into

            // Both corners converted, then min/max taken (finding S1) -- the bottom-left-to-top-left y-flip
            // inside ScreenToPanelPoint reorders which screen corner ends up on top in panel space.
            var a = UIToolkitPointerCapture.ScreenToPanelPoint(panel, new Vector2(screenRect.xMin, screenRect.yMin));
            var b = UIToolkitPointerCapture.ScreenToPanelPoint(panel, new Vector2(screenRect.xMax, screenRect.yMax));
            var panelRect = Rect.MinMaxRect(
                Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y),
                Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));

            box.style.left = panelRect.xMin;
            box.style.top = panelRect.yMin;
            box.style.width = panelRect.width;
            box.style.height = panelRect.height;
            box.style.display = DisplayStyle.Flex;
            // HudBootstrap adds its own regions later, inside Update -- without this, the box would draw
            // beneath panels added after this overlay was constructed (finding B5/D2).
            box.BringToFront();
        }

        private void OnDragBoxEnded() => box.style.display = DisplayStyle.None;
    }
}

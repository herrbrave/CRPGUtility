using System;
using System.Collections.Generic;
using CRPG.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace CRPG.UI
{
    /// <summary>
    /// The UI Toolkit <see cref="IPointerCaptureProvider"/>: registered <see cref="UIDocument"/>s (not a
    /// per-click <c>FindObjectsByType</c> scan) are checked via UI Toolkit's own picking (<c>panel.Pick</c>),
    /// generalizing the logic that used to live inline in the FighterDemo's MouseClickCommandSource. Also owns
    /// modal tracking -- a modal open blocks EVERY pointer position, not just clicks landing on the modal's
    /// own bounds, which is what "modal UI prevents camera/world input as specified" requires beyond ordinary
    /// positional pointer-over checks.
    /// </summary>
    public sealed class UIToolkitPointerCapture : IPointerCaptureProvider
    {
        private static readonly HashSet<UIDocument> registered = new();
        private static int modalDepth;

        public static void Register(UIDocument document)
        {
            if (document != null) registered.Add(document);
        }

        public static void Unregister(UIDocument document) => registered.Remove(document);

        /// <summary>Begins a modal scope -- every pointer position reads as captured until the returned scope
        /// is disposed. Nesting-safe (a counter, not a bool): two stacked modals only release capture once
        /// both have ended.</summary>
        public static IDisposable BeginModal()
        {
            modalDepth++;
            return new ModalScope();
        }

        public bool IsPointerOverUI(Vector2 screenPosition)
        {
            if (modalDepth > 0) return true;

            foreach (var document in registered)
            {
                if (document == null) continue; // destroyed since registering -- Unity's null check catches this

                var panel = document.rootVisualElement?.panel;
                if (panel == null) continue;

                if (panel.Pick(ScreenToPanelPoint(panel, screenPosition)) != null) return true;
            }

            return false;
        }

        /// <summary>Converts a single screen-space point (Input System/Camera convention: origin bottom-left)
        /// into the given panel's own panel-space coordinates. Extracted (2D-Campaign-ExplorationUX-Followups.md
        /// S1) so another UI Toolkit consumer needing this exact conversion -- e.g. <c>DragSelectionOverlay</c>
        /// converting a drag rect's corners -- doesn't hand-duplicate it. Converting a RECT means calling this
        /// for BOTH corners and taking min/max of the results, never treating one converted point as a rect's
        /// top-left directly -- the bottom-left-to-top-left y-flip below reorders which corner ends up where.</summary>
        public static Vector2 ScreenToPanelPoint(IPanel panel, Vector2 screenPosition)
        {
            // Input System / Camera screen coordinates start at bottom-left; UI Toolkit's ScreenToPanel expects
            // top-left pixels (and then applies panel scaling).
            var topLeftPosition = new Vector2(screenPosition.x, Screen.height - screenPosition.y);
            return RuntimePanelUtils.ScreenToPanel(panel, topLeftPosition);
        }

        private sealed class ModalScope : IDisposable
        {
            private bool disposed;

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                modalDepth = Mathf.Max(0, modalDepth - 1);
            }
        }
    }
}

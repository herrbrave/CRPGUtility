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

                var panelPosition = RuntimePanelUtils.ScreenToPanel(panel, screenPosition);
                if (panel.Pick(panelPosition) != null) return true;
            }

            return false;
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

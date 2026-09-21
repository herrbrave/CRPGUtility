using System;
using System.Collections.Generic;
using CRPG.Presentation.Sprites;
using UnityEngine; // ScaleMode lives here, not in UIElements
using UnityEngine.UIElements;

namespace CRPG.Presentation.Creator
{
    /// <summary>
    /// Paints a <see cref="SpriteLayerState"/> as a stack of UI Toolkit <see cref="Image"/>s (one per slot, in draw order) inside a square whose
    /// pixel size is <see cref="CanvasPixels"/> x <see cref="Scale"/>: the creator's live preview. Refresh is EXPLICIT (call it when the state
    /// changed; it re-applies only when the state's <c>Version</c> moved), so there is no scheduler and no per-frame cost.
    /// <para>The size is always a whole number of screen pixels per sprite pixel so point filtering never samples between texels. Keep the
    /// panel's dynamic atlas from re-filtering the textures (see the Task 4.1 spike): an atlassed or bilinear texture would blur at any scale.</para>
    /// </summary>
    public sealed class LayeredSpritePreview : VisualElement
    {
        private readonly List<Image> images = new();
        private SpriteLayerState state;
        private int appliedVersion = -1;
        private int scale = 4;
        private int canvasPixels = 16;

        public LayeredSpritePreview()
        {
            style.flexShrink = 0;
            style.position = Position.Relative;
            ApplySize();
        }

        /// <summary>Whole screen pixels per sprite pixel (>= 1, default 4).</summary>
        public int Scale
        {
            get => scale;
            set
            {
                scale = Math.Max(1, value);
                ApplySize();
            }
        }

        /// <summary>The sprite canvas edge in sprite pixels (default 16, the Kenney character cell).</summary>
        public int CanvasPixels
        {
            get => canvasPixels;
            set
            {
                canvasPixels = Math.Max(1, value);
                ApplySize();
            }
        }

        /// <summary>The per-slot images, in slot (draw) order.</summary>
        public IReadOnlyList<Image> LayerImages => images;

        /// <summary>Binds a state (null unbinds) and paints it. Replaces any previous images.</summary>
        public void Bind(SpriteLayerState newState)
        {
            Clear();
            images.Clear();
            state = newState;
            appliedVersion = -1;
            if (state == null) return;

            for (var i = 0; i < state.SlotCount; i++)
            {
                var image = new Image
                {
                    name = "Layer_" + state.SlotAt(i).Value,
                    scaleMode = ScaleMode.StretchToFill,
                    pickingMode = PickingMode.Ignore,
                };
                image.style.position = Position.Absolute;
                image.style.left = 0;
                image.style.top = 0;
                image.style.right = 0;
                image.style.bottom = 0;
                images.Add(image);
                Add(image);
            }

            Refresh();
        }

        /// <summary>Re-applies sprite, tint and visibility from the state if its <c>Version</c> changed since the last refresh.</summary>
        public void Refresh()
        {
            if (state == null || state.Version == appliedVersion) return;

            var layers = state.Layers;
            for (var i = 0; i < images.Count; i++)
            {
                var layer = layers[i];
                var image = images[i];
                image.sprite = layer.Sprite;
                image.tintColor = layer.Tint;
                image.style.display = layer.Visible && layer.Sprite != null ? DisplayStyle.Flex : DisplayStyle.None;
            }

            appliedVersion = state.Version;
        }

        private void ApplySize()
        {
            var pixels = canvasPixels * scale;
            style.width = pixels;
            style.height = pixels;
        }
    }
}

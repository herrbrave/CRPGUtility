using System.Collections.Generic;
using System.Reflection;
using CRPG.Core;
using CRPG.Gameplay;
using CRPG.Presentation.Sprites;
using UnityEngine;

namespace CRPG.Editor.Character
{
    /// <summary>
    /// 2D campaign Task 2.5: generates the layered sprite "card" Model of a sprite wrapper. Hierarchy (all local transforms
    /// identity unless stated):
    /// <c>Model</c> (CameraFacingBillboard, SpriteDepthSorter, RockingMotionDriver) -&gt; <c>RockPivot</c> (0, -0.5, 0: the character's
    /// feet) -&gt; <c>Layers</c> (0, +0.5, 0; SpriteLayerCompositor) -&gt; one <c>Layer_&lt;slot&gt;</c> SpriteRenderer child per slot.
    /// The card's rotation is owned by the billboard at runtime and its world-Y lift by the wrapper's VisualRoot
    /// (CharacterView.SetModel resets a swapped-in Model's local transform, so neither can live on the card). The Layer_ children are
    /// pre-authored so the prefab structure is deterministic and nothing is allocated at runtime; the compositor reuses them by name.
    /// The sprite material is left at the renderer's default (URP's double-sided Sprite-Unlit-Default in the editor).
    /// </summary>
    public static class SpriteCardBuilder
    {
        public static GameObject Build(CharacterSetupSpec spec)
        {
            var model = new GameObject("Model");
            model.AddComponent<CameraFacingBillboard>();
            model.AddComponent<SpriteDepthSorter>();

            var pivot = new GameObject("RockPivot").transform;
            pivot.SetParent(model.transform, false);
            pivot.localPosition = new Vector3(0f, -0.5f, 0f);

            var driver = model.AddComponent<RockingMotionDriver>();
            driver.RockPivot = pivot;

            var layers = new GameObject("Layers").transform;
            layers.SetParent(pivot, false);
            layers.localPosition = new Vector3(0f, 0.5f, 0f);

            var slots = spec.SpriteLayerSlots != null && spec.SpriteLayerSlots.Count > 0
                ? new List<DefinitionId>(spec.SpriteLayerSlots)
                : SpriteLayerCompositor.DefaultSlots();

            var compositor = layers.gameObject.AddComponent<SpriteLayerCompositor>();
            SetField(compositor, "slots", slots);
            compositor.SortingLayerName = spec.SpriteSortingLayer;

            var useLayer = SortingLayerExists(spec.SpriteSortingLayer);
            foreach (var slot in slots)
            {
                if (!slot.IsValid) continue;
                var layer = new GameObject("Layer_" + slot.Value);
                layer.transform.SetParent(layers, false);
                var renderer = layer.AddComponent<SpriteRenderer>();
                if (useLayer) renderer.sortingLayerName = spec.SpriteSortingLayer;
            }

            return model;
        }

        public static bool SortingLayerExists(string layerName)
        {
            foreach (var layer in SortingLayer.layers)
                if (layer.name == layerName) return true;
            return false;
        }

        private static void SetField(object target, string fieldName, object value)
        {
            var type = target.GetType();
            while (type != null)
            {
                var field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (field != null) { field.SetValue(target, value); return; }
                type = type.BaseType;
            }

            throw new System.Exception($"Field '{fieldName}' not found on {target.GetType().Name}.");
        }
    }
}

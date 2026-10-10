using System.Collections.Generic;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace VikingRaid.Appearance
{
    /// <summary>
    /// Skin tone and hair colour for the Sidekick human: composes <see cref="ModelAppearanceKind.ColourCells"/> options on top of a look's 32x32 colour map and returns a material that uses the result.
    /// With nothing to compose (no tints, no look) or nothing to compose onto (the look's map is not readable) it returns the look itself, so a character with no colour choice wears exactly the
    /// look's own material, which is also what a baked prefab must hold (a composed material is a runtime object and cannot be saved).
    /// <para>Composed materials are shared: the cache key is the look material plus the chosen options' ids AND cell values (so a rebuilt catalog with the same ids and new colours never serves a stale
    /// texture). They are <c>HideAndDontSave</c> and destroyed before an assembly reload and when play mode starts, so none leak.</para>
    /// </summary>
    public static class ModelAppearanceColours
    {
        /// <summary>The colour map property of the Sidekick shader graph.</summary>
        public const string ColourMapProperty = "_ColorMap";

        private static readonly Dictionary<Material, Dictionary<string, Material>> Cache = new();
        private static readonly HashSet<Material> Warned = new();

        /// <param name="tints">The chosen ColourCells options, in row order (a later one wins where two override the same cell).</param>
        public static Material Resolve(Material look, IReadOnlyList<ModelAppearanceOption> tints)
        {
            if (look == null || tints == null || tints.Count == 0 || !look.HasTexture(ColourMapProperty)) return look;

            var key = Signature(tints);
            if (!Cache.TryGetValue(look, out var perLook)) Cache[look] = perLook = new Dictionary<string, Material>();
            if (perLook.TryGetValue(key, out var cached) && cached != null) return cached;

            if (look.GetTexture(ColourMapProperty) is not Texture2D map || !map.isReadable)
            {
                if (Warned.Add(look)) Debug.LogWarning($"[ModelAppearance] the colour map of '{look.name}' is not readable, so skin and hair colours cannot be applied (the Sidekick colour maps are made readable by SidekickColourMapImportSettings); using the look as it is.");
                return look;
            }

            var pixels = map.GetPixels32();
            foreach (var tint in tints)
                foreach (var cell in tint.cells)
                {
                    if (cell.x < 0 || cell.y < 0 || cell.x >= map.width || cell.y >= map.height) continue;
                    var index = cell.y * map.width + cell.x;
                    pixels[index] = new Color32(cell.colour.r, cell.colour.g, cell.colour.b, pixels[index].a);
                }

            // sRGB like the source (linear: false): the cells are authored as display colours
            var texture = new Texture2D(map.width, map.height, TextureFormat.RGBA32, false, false)
                { name = map.name + " (composed)", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            texture.SetPixels32(pixels);
            texture.Apply(false, false);

            var material = new Material(look) { name = look.name, hideFlags = HideFlags.HideAndDontSave };
            material.SetTexture(ColourMapProperty, texture);
            perLook[key] = material;
            return material;
        }

        private static string Signature(IReadOnlyList<ModelAppearanceOption> tints)
        {
            var text = new StringBuilder();
            foreach (var tint in tints)
            {
                text.Append(tint.id).Append(':');
                foreach (var cell in tint.cells) text.Append(cell.x).Append(',').Append(cell.y).Append('=').Append(cell.colour.r).Append('.').Append(cell.colour.g).Append('.').Append(cell.colour.b).Append(';');
                text.Append('|');
            }

            return text.ToString();
        }

        /// <summary>Destroys every composed material and texture (they are recreated on demand).</summary>
        public static void Clear()
        {
            foreach (var perLook in Cache.Values)
                foreach (var material in perLook.Values)
                {
                    if (material == null) continue;
                    var texture = material.GetTexture(ColourMapProperty);
                    Object.DestroyImmediate(material);
                    if (texture != null) Object.DestroyImmediate(texture);
                }

            Cache.Clear();
            Warned.Clear();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlayMode() => Clear();

#if UNITY_EDITOR
        [InitializeOnLoadMethod]
        private static void ClearBeforeAssemblyReload() => AssemblyReloadEvents.beforeAssemblyReload += Clear;
#endif
    }
}

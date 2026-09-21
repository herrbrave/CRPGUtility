using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using CRPG.Core;
using CRPG.Presentation.Sprites;
using UnityEditor;
using UnityEngine;

namespace BarrowsEnd.Editor
{
    /// <summary>
    /// Generates one <see cref="SpriteLayerCatalog"/> per layer slot from the sliced Kenney sheets (plus the custom skeleton bodies),
    /// using the column-block layout verified in Task 2.0a. Idempotent: existing catalog assets are updated in place (asset GUIDs
    /// stay), and sprites are looked up by sliced-sprite NAME (stable across re-slices).
    /// <para><b>Option ids are permanent save contracts.</b> A saved character stores (slot, option id); an id that stops resolving
    /// degrades that layer to empty. Ids are <c>&lt;slot&gt;-&lt;sheet&gt;-&lt;col&gt;-&lt;row&gt;</c> (sheet keys: kenney, be), stable as long
    /// as the source art is not rearranged. Curation may change display names, hide options from a creator, add new options -- but
    /// must NEVER rename or reuse an id.</para>
    /// </summary>
    public static class BarrowsEndCatalogAuthor
    {
        public const string OutputDir = "Assets/Content/SpriteCatalogs";
        public const string KenneyChar = "Assets/Art/Source/roguelikeChar_transparent.png";
        public const string BarrowsEndBodies = "Assets/Art/Source/barrowsend_bodies_transparent.png";
        /// <summary>The five generated class armors (a Kenney top over generated trousers and boots; make_class_armor.py), sheet key <c>be</c>.</summary>
        public const string BarrowsEndArmor = "Assets/Art/Source/barrowsend_armor_transparent.png";

        /// <summary>One rectangle of cells on one sheet that feeds a catalog.</summary>
        public readonly struct CellRange
        {
            public CellRange(string sheetKey, string texturePath, int colMin, int colMax, int rowMin, int rowMax)
            {
                SheetKey = sheetKey;
                TexturePath = texturePath;
                ColMin = colMin;
                ColMax = colMax;
                RowMin = rowMin;
                RowMax = rowMax;
            }

            public string SheetKey { get; }
            public string TexturePath { get; }
            public int ColMin { get; }
            public int ColMax { get; }
            public int RowMin { get; }
            public int RowMax { get; }
        }

        public sealed class CatalogSpec
        {
            public string ShortName;
            public string LayerSlot;
            public string DisplayName;
            public CellRange[] Ranges;
        }

        public static readonly CatalogSpec[] Specs =
        {
            new() { ShortName = "body", LayerSlot = "layer-body", DisplayName = "Body", Ranges = new[]
            {
                new CellRange("kenney", KenneyChar, 0, 1, 0, 3),   // 8 bare bodies (rows 5-11 are pre-made characters, not layers)
                new CellRange("be", BarrowsEndBodies, 0, 1, 0, 0), // the two custom skeleton bodies
            } },
            new() { ShortName = "feet", LayerSlot = "layer-feet", DisplayName = "Feet", Ranges = new[] { new CellRange("kenney", KenneyChar, 3, 4, 0, 11) } },
            new() { ShortName = "clothes", LayerSlot = "layer-clothes", DisplayName = "Clothes", Ranges = new[]
            {
                new CellRange("kenney", KenneyChar, 6, 17, 0, 11),
                new CellRange("be", BarrowsEndArmor, 0, 4, 0, 0), // the five class armors, in class order (barbarian, cleric, fighter, rogue, wizard)
            } },
            new() { ShortName = "hair", LayerSlot = "layer-hair", DisplayName = "Hair", Ranges = new[] { new CellRange("kenney", KenneyChar, 19, 26, 0, 11) } },
            new() { ShortName = "helmet", LayerSlot = "layer-helmet", DisplayName = "Helmet", Ranges = new[] { new CellRange("kenney", KenneyChar, 28, 31, 0, 11) } },
            new() { ShortName = "shield", LayerSlot = "layer-shield", DisplayName = "Shield", Ranges = new[] { new CellRange("kenney", KenneyChar, 33, 40, 0, 11) } },
            new() { ShortName = "weapon", LayerSlot = "layer-weapon", DisplayName = "Weapon", Ranges = new[] { new CellRange("kenney", KenneyChar, 42, 53, 0, 11) } },
        };

        public static string CatalogPath(CatalogSpec spec) => $"{OutputDir}/catalog-{spec.ShortName}.asset";
        public static string OptionId(CatalogSpec spec, string sheetKey, int col, int row) => $"{spec.ShortName}-{sheetKey}-{col}-{row}";

        [MenuItem("Barrows End/Content/Author Sprite Catalogs")]
        public static void Author()
        {
            Directory.CreateDirectory(OutputDir);
            AssetDatabase.Refresh();

            foreach (var spec in Specs)
            {
                var path = CatalogPath(spec);
                var catalog = AssetDatabase.LoadAssetAtPath<SpriteLayerCatalog>(path);
                var isNew = catalog == null;
                if (isNew) catalog = ScriptableObject.CreateInstance<SpriteLayerCatalog>();

                var options = new List<SpriteOptionEntry>();
                foreach (var range in spec.Ranges)
                {
                    var sprites = AssetDatabase.LoadAllAssetsAtPath(range.TexturePath).OfType<Sprite>().ToDictionary(s => s.name);
                    var sheetName = Path.GetFileNameWithoutExtension(range.TexturePath);
                    for (var row = range.RowMin; row <= range.RowMax; row++)
                    for (var col = range.ColMin; col <= range.ColMax; col++)
                    {
                        if (!sprites.TryGetValue($"{sheetName}_{col}_{row}", out var sprite)) continue; // empty cell: nothing sliced there
                        var id = OptionId(spec, range.SheetKey, col, row);
                        options.Add(new SpriteOptionEntry(new DefinitionId(id), id, sprite, Color.white));
                    }
                }

                SetField(catalog, "id", "catalog-" + spec.ShortName);
                SetField(catalog, "layerSlot", new DefinitionId(spec.LayerSlot));
                SetField(catalog, "displayName", spec.DisplayName);
                SetField(catalog, "options", options);

                if (isNew) AssetDatabase.CreateAsset(catalog, path);
                else EditorUtility.SetDirty(catalog);
                Debug.Log($"[BarrowsEnd] {(isNew ? "Created" : "Updated")} {path}: {options.Count} options");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static void SetField(object target, string name, object value)
        {
            var type = target.GetType();
            while (type != null)
            {
                var field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null) { field.SetValue(target, value); return; }
                type = type.BaseType;
            }

            throw new System.Exception($"Field '{name}' not found on {target.GetType().Name}.");
        }
    }
}

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace VikingRaid.Appearance.Editor
{
    /// <summary>The project's appearance families: each one a base prefab (the master behind the toolkit wrapper, carrying a <see cref="ModelAppearanceBinder"/> that points at the family's catalog). The creator
    /// offers one button per family for an NPC with no visual prefab, and uses the list to tell another family's leftover appearance entries from a genuinely foreign creator's.</summary>
    public static class ModelAppearanceFamilies
    {
        public static readonly (string Family, string BasePrefabPath)[] All =
        {
            ("Viking", "Assets/Characters/Villagers/VikingNpcBase.prefab"),
            ("Goblin", "Assets/Characters/Npcs/GoblinNpcBase.prefab"),
            ("Human", "Assets/Characters/Npcs/HumanNpcBase.prefab"),
        };

        public static string BasePrefabPath(string family)
        {
            foreach (var entry in All) if (entry.Family == family) return entry.BasePrefabPath;
            return null;
        }

        /// <summary>The catalog the binder on this prefab dresses from (null: not an appearance prefab).</summary>
        public static ModelAppearanceCatalog CatalogOf(GameObject prefab) => prefab != null && prefab.TryGetComponent<ModelAppearanceBinder>(out var binder) ? binder.Catalog : null;

        /// <summary>The catalogs of every family whose base prefab exists.</summary>
        public static IEnumerable<ModelAppearanceCatalog> Catalogs()
        {
            foreach (var entry in All)
            {
                var catalog = CatalogOf(AssetDatabase.LoadAssetAtPath<GameObject>(entry.BasePrefabPath));
                if (catalog != null) yield return catalog;
            }
        }
    }
}

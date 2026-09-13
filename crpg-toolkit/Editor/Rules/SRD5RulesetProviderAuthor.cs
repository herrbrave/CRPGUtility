using System.Linq;
using CRPG.Core;
using CRPG.Rules.SRD5;
using UnityEditor;
using UnityEngine;

namespace CRPG.Editor
{
    /// <summary>
    /// Authors/refreshes the one canonical SRD5RulesetProvider.asset by scanning Rules/SRD5e2014/Content/ for
    /// every DefinitionAsset -- avoids hand-wiring ~200 references. Sorts by asset GUID before assigning the
    /// serialized list so re-running this produces a stable, diff-quiet ordering rather than whatever order
    /// AssetDatabase.FindAssets happens to return.
    /// </summary>
    public static class SRD5RulesetProviderAuthor
    {
        private const string ContentFolder = "Packages/com.crpgtoolkit.core/Rules/SRD5e2014/Content";
        private const string ProviderAssetPath = "Packages/com.crpgtoolkit.core/Rules/SRD5e2014/Content/SRD5RulesetProvider.asset";

        [MenuItem("CRPG Toolkit/Rules/Refresh SRD5 Ruleset Provider")]
        public static void Refresh()
        {
            // FindAssets searches subfolders too -- Content/Oakhaven/ holds fixture-specific quest/dialogue
            // content for one demo (not generic SRD5 rules content) and is deliberately excluded here. It also
            // duplicates at least one top-level id (SkillDefinition_Persuasion, "skill-persuasion" in both),
            // which would otherwise make RulesDatabase.Register throw the moment this provider registers.
            var guids = AssetDatabase.FindAssets("t:DefinitionAsset", new[] { ContentFolder });
            var definitions = guids
                .OrderBy(guid => guid)
                .Select(guid => AssetDatabase.GUIDToAssetPath(guid))
                .Where(path => !path.Contains("/Oakhaven/"))
                .Select(AssetDatabase.LoadAssetAtPath<DefinitionAsset>)
                .Where(definition => definition != null)
                .ToList();

            var provider = AssetDatabase.LoadAssetAtPath<SRD5RulesetProvider>(ProviderAssetPath);
            if (provider == null)
            {
                provider = ScriptableObject.CreateInstance<SRD5RulesetProvider>();
                AssetDatabase.CreateAsset(provider, ProviderAssetPath);
            }

            var serialized = new SerializedObject(provider);
            var contentProperty = serialized.FindProperty("content");
            contentProperty.ClearArray();
            for (var i = 0; i < definitions.Count; i++)
            {
                contentProperty.InsertArrayElementAtIndex(i);
                contentProperty.GetArrayElementAtIndex(i).objectReferenceValue = definitions[i];
            }

            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(provider);
            AssetDatabase.SaveAssets();

            Debug.Log($"[SRD5RulesetProviderAuthor] Refreshed {definitions.Count} definitions into {ProviderAssetPath}.");
        }
    }
}

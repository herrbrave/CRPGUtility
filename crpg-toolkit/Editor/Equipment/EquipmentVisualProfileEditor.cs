using CRPG.Presentation.Equipment;
using UnityEditor;
using UnityEngine;

namespace CRPG.Editor.Equipment
{
    /// <summary>
    /// Inspector for <see cref="EquipmentVisualProfile"/> that shows only the fields relevant to the chosen
    /// <see cref="EquipmentVisualKind"/>: one asset type carries 3D (Rigid/Skinned) and 2D (Sprite) data, and showing every
    /// field at once invites the classic mistakes (filling the sprite layers but leaving the kind at None, or the 3D hidden
    /// body regions instead of the sprite hidden layer slots). Fields belonging to the definition base (id, display name,
    /// ...) always show. The relevance rule is a pure function so it is unit-tested.
    /// </summary>
    [CustomEditor(typeof(EquipmentVisualProfile))]
    public sealed class EquipmentVisualProfileEditor : UnityEditor.Editor
    {
        private static readonly string[] RigidFields =
            { "visualPrefab", "socketId", "localPosition", "localRotation", "localScale", "bodyRegionId", "hiddenBodyRegions" };

        private static readonly string[] SkinnedFields =
            { "visualPrefab", "boneMap", "bodyRegionId", "hiddenBodyRegions" };

        private static readonly string[] SpriteFields =
            { "spriteLayers", "spritePriority", "hiddenLayerSlots" };

        private static readonly string[] AllKindFields =
            { "visualPrefab", "socketId", "localPosition", "localRotation", "localScale", "bodyRegionId", "hiddenBodyRegions", "boneMap", "spriteLayers", "spritePriority", "hiddenLayerSlots" };

        /// <summary>Whether the serialized field <paramref name="fieldName"/> should be drawn for a profile of this kind.
        /// Base-definition fields and the kind selector itself are always relevant.</summary>
        public static bool IsFieldRelevant(string fieldName, EquipmentVisualKind kind)
        {
            if (System.Array.IndexOf(AllKindFields, fieldName) < 0) return true; // m_Script, kind, id, displayName, ...

            var relevant = kind switch
            {
                EquipmentVisualKind.Rigid => RigidFields,
                EquipmentVisualKind.Skinned => SkinnedFields,
                EquipmentVisualKind.Sprite => SpriteFields,
                _ => System.Array.Empty<string>(),
            };
            return System.Array.IndexOf(relevant, fieldName) >= 0;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var kindProperty = serializedObject.FindProperty("kind");
            var kind = kindProperty != null ? (EquipmentVisualKind)kindProperty.enumValueIndex : EquipmentVisualKind.None;

            var property = serializedObject.GetIterator();
            for (var enterChildren = true; property.NextVisible(enterChildren); enterChildren = false)
            {
                if (property.propertyPath == "m_Script")
                {
                    using (new EditorGUI.DisabledScope(true)) EditorGUILayout.PropertyField(property);
                    continue;
                }

                if (!IsFieldRelevant(property.name, kind)) continue;
                EditorGUILayout.PropertyField(property, true);
            }

            if (kind == EquipmentVisualKind.None)
                EditorGUILayout.HelpBox("Kind is None: this item draws nothing. Pick Rigid or Skinned for a 3D character, or Sprite for a layered sprite character.", MessageType.Info);

            serializedObject.ApplyModifiedProperties();
        }
    }
}

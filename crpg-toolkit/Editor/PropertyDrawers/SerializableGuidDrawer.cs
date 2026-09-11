using CRPG.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace CRPG.Editor.PropertyDrawers
{
    /// <summary>Shows a SerializableGuid as a read-only string (never hand-typed -- there's no valid free-form
    /// value to type, it's either a real generated id or nothing) with a "Regenerate" button as the one
    /// explicit way to change it. Used automatically for every SerializableGuid field in every Inspector.</summary>
    [CustomPropertyDrawer(typeof(SerializableGuid))]
    public sealed class SerializableGuidDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var lowProperty = property.FindPropertyRelative("low");
            var highProperty = property.FindPropertyRelative("high");

            var row = new VisualElement { style = { flexDirection = FlexDirection.Row } };

            var field = new TextField(property.displayName) { isReadOnly = true, style = { flexGrow = 1 } };
            field.SetValueWithoutNotify(SerializableGuidPropertyExtensions.ReadDisplayString(lowProperty, highProperty));
            row.Add(field);

            var regenerateButton = new Button(() =>
            {
                property.serializedObject.Update();
                SerializableGuidPropertyExtensions.SetValue(lowProperty, highProperty, SerializableGuid.NewGuid());
                property.serializedObject.ApplyModifiedProperties();
                field.SetValueWithoutNotify(SerializableGuidPropertyExtensions.ReadDisplayString(lowProperty, highProperty));
            })
            { text = "Regenerate" };
            row.Add(regenerateButton);

            return row;
        }
    }
}

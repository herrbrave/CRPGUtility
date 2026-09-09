using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace CRPG.Editor.Dialogue
{
    /// <summary>One output port per ChoiceOption, added/removed dynamically as options are added/removed --
    /// the only DialogueNode type with a variable number of "next" links. Keeps port rendering (just a labeled
    /// connector) separate from field editing (a numbered list of option rows in the extension container below)
    /// rather than trying to embed a text field directly on a port's row, which GraphView's Port doesn't
    /// straightforwardly support -- simpler and more robust for a first version, at the cost of not showing the
    /// option's own text right next to its connector.</summary>
    public sealed class ChoiceNodeView : DialogueNodeView
    {
        private readonly VisualElement optionsContainer;

        private SerializedProperty OptionsProperty => NodeProperty.FindPropertyRelative("options");

        public ChoiceNodeView(SerializedProperty nodeProperty, DialogueGraphView owner) : base(nodeProperty, "Choice", owner)
        {
            optionsContainer = new VisualElement();
            extensionContainer.Add(optionsContainer);

            var addButton = new Button(AddOption) { text = "+ Add Option" };
            extensionContainer.Add(addButton);

            RebuildOptions();
        }

        private void AddOption()
        {
            NodeProperty.serializedObject.Update();
            var options = OptionsProperty;
            var newIndex = options.arraySize;
            options.InsertArrayElementAtIndex(newIndex);
            var newOption = options.GetArrayElementAtIndex(newIndex);
            newOption.FindPropertyRelative("text").stringValue = "New Option";
            newOption.FindPropertyRelative("nextNodeId").stringValue = "";
            newOption.FindPropertyRelative("hasVisibilityCondition").boolValue = false;
            NodeProperty.serializedObject.ApplyModifiedProperties();
            RebuildOptions();
        }

        private void RemoveOption(int index)
        {
            NodeProperty.serializedObject.Update();
            OptionsProperty.DeleteArrayElementAtIndex(index);
            NodeProperty.serializedObject.ApplyModifiedProperties();
            RebuildOptions();
        }

        private void RebuildOptions()
        {
            ClearOutputPorts();
            optionsContainer.Clear();

            var options = OptionsProperty;
            for (var i = 0; i < options.arraySize; i++)
            {
                var option = options.GetArrayElementAtIndex(i);
                var capturedIndex = i;

                var row = new VisualElement { style = { flexDirection = FlexDirection.Row } };

                var textField = new PropertyField(option.FindPropertyRelative("text"), $"Option {i}") { style = { flexGrow = 1 } };
                textField.Bind(NodeProperty.serializedObject);
                row.Add(textField);

                var removeButton = new Button(() => RemoveOption(capturedIndex)) { text = "-" };
                row.Add(removeButton);
                optionsContainer.Add(row);

                var visibilityToggle = new PropertyField(option.FindPropertyRelative("hasVisibilityCondition"), "Conditional?");
                visibilityToggle.Bind(NodeProperty.serializedObject);
                optionsContainer.Add(visibilityToggle);

                var conditionFields = new VisualElement { style = { marginLeft = 12 } };
                foreach (var relativeField in new[] { "timelineKey", "secondTimelineKey", "conditionType", "threshold" })
                {
                    var field = new PropertyField(option.FindPropertyRelative(relativeField));
                    field.Bind(NodeProperty.serializedObject);
                    conditionFields.Add(field);
                }
                optionsContainer.Add(conditionFields);

                AddOutputPort($"Option {i}", $"options.Array.data[{i}].nextNodeId");
            }

            RefreshExpandedState();
            RefreshPorts();
        }
    }
}

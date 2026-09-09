using System;
using System.Linq;
using CRPG.Dialogue;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace CRPG.Editor.Dialogue
{
    /// <summary>DialogueAction is [SerializeReference]-polymorphic with no built-in Unity type-picker UI for
    /// arbitrary SerializeReference fields, so this node draws its own: a dropdown of every non-abstract
    /// DialogueAction subclass (found once via reflection, cheap enough for an Editor-only tool), swapping
    /// managedReferenceValue and rebuilding the field below it when changed.</summary>
    public sealed class ActionNodeView : DialogueNodeView
    {
        private static readonly Type[] ActionTypes = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => a.GetTypes())
            .Where(t => typeof(DialogueAction).IsAssignableFrom(t) && !t.IsAbstract)
            .OrderBy(t => t.Name)
            .ToArray();

        private readonly VisualElement actionFieldContainer;

        public ActionNodeView(SerializedProperty nodeProperty, DialogueGraphView owner) : base(nodeProperty, "Action", owner)
        {
            var actionProperty = NodeProperty.FindPropertyRelative("action");
            var currentTypeName = actionProperty.managedReferenceValue?.GetType().Name ?? "(None)";

            var typeDropdown = new DropdownField("Action Type", ActionTypes.Select(t => t.Name).ToList(), 0);
            typeDropdown.SetValueWithoutNotify(currentTypeName);
            typeDropdown.RegisterValueChangedCallback(evt =>
            {
                var type = ActionTypes.First(t => t.Name == evt.newValue);
                NodeProperty.serializedObject.Update();
                actionProperty.managedReferenceValue = Activator.CreateInstance(type);
                NodeProperty.serializedObject.ApplyModifiedProperties();
                RebuildActionField(actionProperty);
            });
            extensionContainer.Add(typeDropdown);

            actionFieldContainer = new VisualElement();
            extensionContainer.Add(actionFieldContainer);
            RebuildActionField(actionProperty);

            AddOutputPort("Next", "nextNodeId");

            RefreshExpandedState();
            RefreshPorts();
        }

        private void RebuildActionField(SerializedProperty actionProperty)
        {
            actionFieldContainer.Clear();
            if (actionProperty.managedReferenceValue == null) return;

            var field = new PropertyField(actionProperty, "");
            field.Bind(NodeProperty.serializedObject);
            actionFieldContainer.Add(field);
        }
    }
}

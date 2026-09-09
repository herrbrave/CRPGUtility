using System;
using System.Collections.Generic;
using System.Linq;
using CRPG.Dialogue;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace CRPG.Editor.Dialogue
{
    /// <summary>Renders/edits one DialogueDefinition's node graph directly against its own SerializedObject --
    /// there's no separate "graph" serialized format to keep in sync, the graph view IS a live editor over the
    /// same Nodes list DialogueRunner reads at runtime. Node position is the only field this tool owns that
    /// DialogueRunner never reads (DialogueNode.EditorPosition).</summary>
    public sealed class DialogueGraphView : GraphView
    {
        private static readonly Dictionary<string, Func<SerializedProperty, DialogueGraphView, DialogueNodeView>> ViewFactoriesByTypeName = new()
        {
            [nameof(LineNode)] = (p, o) => new LineNodeView(p, o),
            [nameof(ChoiceNode)] = (p, o) => new ChoiceNodeView(p, o),
            [nameof(BranchNode)] = (p, o) => new BranchNodeView(p, o),
            [nameof(CheckNode)] = (p, o) => new CheckNodeView(p, o),
            [nameof(ActionNode)] = (p, o) => new ActionNodeView(p, o),
            [nameof(EndNode)] = (p, o) => new EndNodeView(p, o),
        };

        private readonly DialogueDefinition dialogueDefinition;
        private readonly SerializedObject serializedObject;
        private readonly SerializedProperty nodesProperty;
        private readonly Dictionary<string, DialogueNodeView> viewsByNodeId = new();

        public event Action DirtyStateChanged;

        public DialogueGraphView(DialogueDefinition dialogueDefinition)
        {
            this.dialogueDefinition = dialogueDefinition;
            serializedObject = new SerializedObject(dialogueDefinition);
            nodesProperty = serializedObject.FindProperty("nodes");

            SetupZoom(ContentZoomer.DefaultMinScale, ContentZoomer.DefaultMaxScale);
            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());
            var grid = new GridBackground();
            Insert(0, grid);
            grid.StretchToParentSize();

            graphViewChanged += OnGraphViewChanged;

            RebuildFromScratch();
        }

        private void RebuildFromScratch()
        {
            DeleteElements(graphElements.ToList());
            viewsByNodeId.Clear();

            for (var i = 0; i < nodesProperty.arraySize; i++)
            {
                var nodeProperty = nodesProperty.GetArrayElementAtIndex(i);
                var view = CreateView(nodeProperty);
                if (view == null) continue;
                viewsByNodeId[view.Model.NodeId] = view;
                AddElement(view);
            }

            foreach (var view in viewsByNodeId.Values)
            {
                foreach (var (port, _, targetNodeId) in view.GetOutputLinks())
                {
                    if (string.IsNullOrEmpty(targetNodeId) || !viewsByNodeId.TryGetValue(targetNodeId, out var targetView)) continue;
                    AddElement(port.ConnectTo(targetView.InputPort));
                }
            }

            RefreshDanglingStates();
            RefreshStartNodeHighlight();
        }

        private DialogueNodeView CreateView(SerializedProperty nodeProperty)
        {
            var value = nodeProperty.managedReferenceValue;
            if (value == null) return null;

            return ViewFactoriesByTypeName.TryGetValue(value.GetType().Name, out var factory) ? factory(nodeProperty, this) : null;
        }

        public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            var localMousePosition = contentViewContainer.WorldToLocal(evt.mousePosition);

            if (evt.target is DialogueNodeView nodeView)
            {
                evt.menu.AppendAction("Set as Start", _ => SetStartNode(nodeView.Model.NodeId));
                evt.menu.AppendSeparator();
            }

            evt.menu.AppendAction("Create Node/Line", _ => CreateNode<LineNode>(localMousePosition));
            evt.menu.AppendAction("Create Node/Choice", _ => CreateNode<ChoiceNode>(localMousePosition));
            evt.menu.AppendAction("Create Node/Branch", _ => CreateNode<BranchNode>(localMousePosition));
            evt.menu.AppendAction("Create Node/Check", _ => CreateNode<CheckNode>(localMousePosition));
            evt.menu.AppendAction("Create Node/Action", _ => CreateNode<ActionNode>(localMousePosition));
            evt.menu.AppendAction("Create Node/End", _ => CreateNode<EndNode>(localMousePosition));
        }

        private void CreateNode<T>(Vector2 position) where T : DialogueNode
        {
            serializedObject.Update();
            var index = nodesProperty.arraySize;
            nodesProperty.InsertArrayElementAtIndex(index);
            var newProperty = nodesProperty.GetArrayElementAtIndex(index);
            newProperty.managedReferenceValue = Activator.CreateInstance<T>();
            newProperty.FindPropertyRelative("nodeId").stringValue = "node-" + Guid.NewGuid().ToString("N")[..8];
            newProperty.FindPropertyRelative("editorPosition").vector2Value = position;
            serializedObject.ApplyModifiedProperties();

            RebuildFromScratch();
            RaiseDirty();
        }

        private void SetStartNode(string nodeId)
        {
            serializedObject.Update();
            serializedObject.FindProperty("startNodeId").stringValue = nodeId;
            serializedObject.ApplyModifiedProperties();
            RefreshStartNodeHighlight();
            RaiseDirty();
        }

        private void RefreshStartNodeHighlight()
        {
            foreach (var view in viewsByNodeId.Values)
            {
                var isStart = view.Model.NodeId == dialogueDefinition.StartNodeId;
                view.style.borderTopColor = view.style.borderBottomColor = view.style.borderLeftColor = view.style.borderRightColor =
                    isStart ? new StyleColor(new Color(0.3f, 0.85f, 0.3f)) : new StyleColor(StyleKeyword.Null);
                view.style.borderTopWidth = view.style.borderBottomWidth = view.style.borderLeftWidth = view.style.borderRightWidth =
                    isStart ? 3 : new StyleFloat(StyleKeyword.Null);
            }
        }

        private GraphViewChange OnGraphViewChanged(GraphViewChange change)
        {
            var dirty = false;

            if (change.movedElements != null)
            {
                foreach (var element in change.movedElements.OfType<DialogueNodeView>())
                {
                    element.CommitPosition();
                    dirty = true;
                }
            }

            if (change.edgesToCreate != null)
            {
                foreach (var edge in change.edgesToCreate)
                {
                    if (edge.output.node is not DialogueNodeView sourceView) continue;
                    var targetId = edge.input.node is DialogueNodeView targetView ? targetView.Model.NodeId : "";
                    WriteNextId(sourceView, (string)edge.output.userData, targetId);
                    dirty = true;
                }
            }

            if (change.elementsToRemove != null)
            {
                foreach (var edge in change.elementsToRemove.OfType<Edge>())
                {
                    if (edge.output.node is DialogueNodeView sourceView)
                    {
                        WriteNextId(sourceView, (string)edge.output.userData, "");
                        dirty = true;
                    }
                }

                foreach (var nodeView in change.elementsToRemove.OfType<DialogueNodeView>())
                {
                    RemoveNode(nodeView.Model.NodeId);
                    viewsByNodeId.Remove(nodeView.Model.NodeId);
                    dirty = true;
                }
            }

            if (dirty)
            {
                RefreshDanglingStates();
                RaiseDirty();
            }

            return change;
        }

        private void WriteNextId(DialogueNodeView sourceView, string relativePath, string targetNodeId)
        {
            sourceView.NodeProperty.serializedObject.Update();
            sourceView.NodeProperty.FindPropertyRelative(relativePath).stringValue = targetNodeId;
            sourceView.NodeProperty.serializedObject.ApplyModifiedProperties();
        }

        private void RemoveNode(string nodeId)
        {
            serializedObject.Update();
            for (var i = 0; i < nodesProperty.arraySize; i++)
            {
                var element = nodesProperty.GetArrayElementAtIndex(i).managedReferenceValue as DialogueNode;
                if (element?.NodeId != nodeId) continue;
                nodesProperty.DeleteArrayElementAtIndex(i);
                break;
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void RefreshDanglingStates()
        {
            var validIds = new HashSet<string>(viewsByNodeId.Keys);
            foreach (var view in viewsByNodeId.Values) view.RefreshDanglingState(validIds);
        }

        private void RaiseDirty()
        {
            EditorUtility.SetDirty(dialogueDefinition);
            DirtyStateChanged?.Invoke();
        }

        /// <summary>Called by the window's Save button just before AssetDatabase.SaveAssets() -- every edit
        /// already applies to the SerializedObject immediately (bound PropertyFields, or explicit
        /// ApplyModifiedProperties in this class), so there's nothing left to flush here today. Kept as an
        /// explicit seam in case a future change introduces batched/deferred edits.</summary>
        public void CommitPendingChanges()
        {
            serializedObject.ApplyModifiedProperties();
        }

        public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter) =>
            ports.ToList().Where(p => p.direction != startPort.direction && p.node != startPort.node).ToList();
    }
}

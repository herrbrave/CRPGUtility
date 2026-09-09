using System.Collections.Generic;
using System.Linq;
using CRPG.Dialogue;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace CRPG.Editor.Dialogue
{
    /// <summary>Base GraphView Node for every DialogueNode type. Fields are edited via PropertyField bound
    /// directly to the node's own SerializedProperty (found via [SerializeReference] array-element path on the
    /// DialogueDefinition's Nodes list) -- DialogueNode's fields are all private with no public setters (it's
    /// meant to be authored data), so SerializedProperty is the only way to write them at all, not just the
    /// nicest way. This also means every edit gets real Undo/Redo for free, the same as any other Inspector.
    /// <para>Each output port's <see cref="Port.userData"/> holds the relative property path (from this node's
    /// own SerializedProperty) of the string field that port represents -- e.g. "nextNodeId" or
    /// "options.Array.data[2].nextNodeId" -- so DialogueGraphView can update the right field generically on
    /// connect/disconnect without every subclass needing its own wiring code.</para></summary>
    public abstract class DialogueNodeView : Node
    {
        private static readonly Color DanglingPortColor = new(0.9f, 0.2f, 0.2f);

        private readonly List<Port> outputPorts = new();
        private readonly Dictionary<Port, Color> defaultPortColors = new();

        protected DialogueNodeView(SerializedProperty nodeProperty, string title, GraphView ownerGraphView)
        {
            NodeProperty = nodeProperty;
            Model = (DialogueNode)nodeProperty.managedReferenceValue;
            OwnerGraphView = ownerGraphView;
            this.title = title;
            viewDataKey = Model.NodeId;

            InputPort = Port.Create<Edge>(Orientation.Horizontal, Direction.Input, Port.Capacity.Multi, typeof(bool));
            InputPort.portName = "";
            inputContainer.Add(InputPort);

            SetPosition(new Rect(Model.EditorPosition, Vector2.zero));
        }

        public SerializedProperty NodeProperty { get; }
        public DialogueNode Model { get; }
        public Port InputPort { get; }
        public IReadOnlyList<Port> OutputPorts => outputPorts;
        protected GraphView OwnerGraphView { get; }

        /// <summary>Every (port, relative property path, current target NodeId) pair this node's outputs
        /// represent -- used by DialogueGraphView to build initial edges on load and to detect dangling
        /// references (a non-empty NodeId that doesn't match any node currently in the graph).</summary>
        public IEnumerable<(Port Port, string RelativePath, string TargetNodeId)> GetOutputLinks()
        {
            foreach (var port in outputPorts)
            {
                var relativePath = (string)port.userData;
                yield return (port, relativePath, NodeProperty.FindPropertyRelative(relativePath).stringValue);
            }
        }

        /// <summary>Adds one output port bound to a string field on this node (by path relative to this node's
        /// own SerializedProperty). Call RefreshExpandedState()/RefreshPorts() once after all setup is done.</summary>
        protected Port AddOutputPort(string label, string relativeNextIdPropertyPath)
        {
            var port = Port.Create<Edge>(Orientation.Horizontal, Direction.Output, Port.Capacity.Single, typeof(bool));
            port.portName = label;
            port.userData = relativeNextIdPropertyPath;
            outputContainer.Add(port);
            outputPorts.Add(port);
            defaultPortColors[port] = port.portColor;
            return port;
        }

        /// <summary>Removes every current output port (and any edges connected to them) so a variable-arity
        /// node (ChoiceNode, one port per option) can rebuild its ports after an option is added or removed.</summary>
        protected void ClearOutputPorts()
        {
            foreach (var port in outputPorts)
            {
                foreach (var edge in port.connections.ToList())
                {
                    edge.input.Disconnect(edge);
                    edge.output.Disconnect(edge);
                    OwnerGraphView.RemoveElement(edge);
                }

                outputContainer.Remove(port);
                defaultPortColors.Remove(port);
            }

            outputPorts.Clear();
        }

        /// <summary>Binds a body field (shown below the ports) to a SerializedProperty relative to this node.</summary>
        protected void AddField(string relativePropertyPath, string label = null)
        {
            var property = NodeProperty.FindPropertyRelative(relativePropertyPath);
            var field = new PropertyField(property, label);
            field.Bind(NodeProperty.serializedObject);
            extensionContainer.Add(field);
        }

        /// <summary>Writes the node's current on-canvas position back into its EditorPosition field. Called by
        /// DialogueGraphView whenever GraphView reports this node was moved.</summary>
        public void CommitPosition()
        {
            NodeProperty.serializedObject.Update();
            NodeProperty.FindPropertyRelative("editorPosition").vector2Value = GetPosition().position;
            NodeProperty.serializedObject.ApplyModifiedProperties();
        }

        /// <summary>Tints an output port red if its target NodeId is set but doesn't resolve to any node
        /// currently in the graph -- an unset (empty) target is just "not wired up yet", not dangling.</summary>
        public void RefreshDanglingState(HashSet<string> validNodeIds)
        {
            foreach (var (port, _, targetNodeId) in GetOutputLinks())
            {
                var dangling = !string.IsNullOrEmpty(targetNodeId) && !validNodeIds.Contains(targetNodeId);
                port.portColor = dangling ? DanglingPortColor : defaultPortColors[port];
            }
        }
    }
}

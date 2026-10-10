using System.Collections.Generic;
using CRPG.Core;
using CRPG.Dialogue;
using UnityEditor;

namespace CRPG.Editor.Validation
{
    /// <summary>
    /// Task 3.4 (Production Foundation): validates a DialogueDefinition's authored graph -- pure logic, no
    /// scene/GameObject required, mirroring CharacterRigValidator's shape and reusing its ValidationResult/
    /// ValidationSeverity types. Catches exactly the class of authored mistake design sec."Dialogue and quests"
    /// calls out ("invalid authored cycles can hang a conversation/editor") before DialogueRunner's own runtime
    /// MaxAutoSettleSteps backstop would ever need to fire.
    /// </summary>
    public static class DialogueValidator
    {
        public static List<ValidationResult> Validate(DialogueDefinition definition)
        {
            var results = new List<ValidationResult>();
            var assetPath = AssetDatabase.GetAssetPath(definition);

            // A null entry in the node list (a node type that was renamed or deleted) would crash every check below: report it and stop.
            for (var i = 0; i < definition.Nodes.Count; i++)
            {
                if (definition.Nodes[i] != null) continue;
                results.Add(new ValidationResult(ValidationSeverity.Error, assetPath, $"Nodes[{i}]",
                    $"Node {i} is missing (its node type no longer exists, or it was never assigned).",
                    "Delete the empty node entry from the dialogue."));
                return results;
            }

            var nodesById = new Dictionary<SerializableGuid, DialogueNode>();
            ValidateNoDuplicateNodeIds(definition, assetPath, nodesById, results);
            ValidateStartNode(definition, assetPath, nodesById, results);
            ValidateNoDanglingReferences(definition, assetPath, nodesById, results);
            ValidateReachability(definition, assetPath, nodesById, results);
            ValidateNoInescapableAutoCycle(definition, assetPath, nodesById, results);
            ValidateActionsAssigned(definition, assetPath, results);

            return results;
        }

        /// <summary>An ActionNode with no action would throw the moment the runner executes it.</summary>
        private static void ValidateActionsAssigned(DialogueDefinition definition, string assetPath, List<ValidationResult> results)
        {
            foreach (var node in definition.Nodes)
            {
                if (node is ActionNode { Action: null } action)
                {
                    results.Add(new ValidationResult(ValidationSeverity.Error, assetPath, $"ActionNode '{action.NodeId}'.Action",
                        "The action node has no action assigned.",
                        "Assign an action, or remove the node and link its neighbours directly."));
                }
                else if (node is ActionNode { Action: StartQuestAction { QuestKey: null or "" } } quest)
                {
                    results.Add(new ValidationResult(ValidationSeverity.Error, assetPath, $"ActionNode '{quest.NodeId}'.Action",
                        "The start-quest action has no quest key (nothing would start).",
                        "Type the quest's id (the QuestDefinition's DefinitionId)."));
                }
                else if (node is ActionNode { Action: SpendItemAction { Item: null } } spend)
                {
                    results.Add(new ValidationResult(ValidationSeverity.Error, assetPath, $"ActionNode '{spend.NodeId}'.Action",
                        "The spend-item action has no item assigned.",
                        "Assign the item to spend (gold for an inn)."));
                }
                else if (node is BranchNode { Source: BranchSource.PartyItemCount, Item: null } branch)
                {
                    results.Add(new ValidationResult(ValidationSeverity.Error, assetPath, $"BranchNode '{branch.NodeId}'.Item",
                        "The branch asks about an item the party carries but has no item assigned (it would always be false).",
                        "Assign the item, or switch the branch back to a Timeline query."));
                }
                else if (node is ActionNode { Action: GiveItemAction { Item: null } } give)
                {
                    results.Add(new ValidationResult(ValidationSeverity.Error, assetPath, $"ActionNode '{give.NodeId}'.Action",
                        "The give-item action has no item assigned (it would throw when the conversation reaches it).",
                        "Assign the item to give."));
                }
            }
        }

        private static void ValidateNoDuplicateNodeIds(DialogueDefinition definition, string assetPath, Dictionary<SerializableGuid, DialogueNode> nodesById, List<ValidationResult> results)
        {
            foreach (var node in definition.Nodes)
            {
                if (!nodesById.TryAdd(node.NodeId, node))
                {
                    results.Add(new ValidationResult(ValidationSeverity.Error, assetPath, "Nodes",
                        $"More than one node shares id '{node.NodeId}'.",
                        "Give each node a distinct id -- DialogueRunner would otherwise resolve every reference to only one of them, silently dropping the other(s)."));
                }
            }
        }

        private static void ValidateStartNode(DialogueDefinition definition, string assetPath, Dictionary<SerializableGuid, DialogueNode> nodesById, List<ValidationResult> results)
        {
            if (!nodesById.ContainsKey(definition.StartNodeId))
            {
                results.Add(new ValidationResult(ValidationSeverity.Error, assetPath, nameof(definition.StartNodeId),
                    $"StartNodeId '{definition.StartNodeId}' does not match any authored node.",
                    "Set StartNodeId to an id that exists among this dialogue's Nodes."));
            }
        }

        private static void ValidateNoDanglingReferences(DialogueDefinition definition, string assetPath, Dictionary<SerializableGuid, DialogueNode> nodesById, List<ValidationResult> results)
        {
            void CheckRef(SerializableGuid target, string fieldDescription)
            {
                if (!nodesById.ContainsKey(target))
                {
                    results.Add(new ValidationResult(ValidationSeverity.Error, assetPath, fieldDescription,
                        $"Points to node id '{target}', which doesn't exist.",
                        "Fix the reference to point at a real node, or add the missing node."));
                }
            }

            foreach (var node in definition.Nodes)
            {
                switch (node)
                {
                    case LineNode line:
                        CheckRef(line.NextNodeId, $"LineNode '{line.NodeId}'.NextNodeId");
                        break;
                    case ActionNode action:
                        CheckRef(action.NextNodeId, $"ActionNode '{action.NodeId}'.NextNodeId");
                        break;
                    case BranchNode branch:
                        CheckRef(branch.TrueNodeId, $"BranchNode '{branch.NodeId}'.TrueNodeId");
                        CheckRef(branch.FalseNodeId, $"BranchNode '{branch.NodeId}'.FalseNodeId");
                        break;
                    case CheckNode check:
                        CheckRef(check.SuccessNodeId, $"CheckNode '{check.NodeId}'.SuccessNodeId");
                        CheckRef(check.FailureNodeId, $"CheckNode '{check.NodeId}'.FailureNodeId");
                        break;
                    case ChoiceNode choice:
                        for (var i = 0; i < choice.Options.Count; i++)
                            CheckRef(choice.Options[i].NextNodeId, $"ChoiceNode '{choice.NodeId}'.Options[{i}].NextNodeId");
                        break;
                }
            }
        }

        private static void ValidateReachability(DialogueDefinition definition, string assetPath, Dictionary<SerializableGuid, DialogueNode> nodesById, List<ValidationResult> results)
        {
            if (!nodesById.ContainsKey(definition.StartNodeId)) return; // already reported as an Error above

            var reachable = new HashSet<SerializableGuid>();
            var queue = new Queue<SerializableGuid>();
            queue.Enqueue(definition.StartNodeId);
            reachable.Add(definition.StartNodeId);

            while (queue.Count > 0)
            {
                var id = queue.Dequeue();
                if (!nodesById.TryGetValue(id, out var node)) continue; // dangling ref, already reported

                foreach (var next in Successors(node))
                {
                    if (nodesById.ContainsKey(next) && reachable.Add(next)) queue.Enqueue(next);
                }
            }

            foreach (var node in definition.Nodes)
            {
                if (!reachable.Contains(node.NodeId))
                {
                    results.Add(new ValidationResult(ValidationSeverity.Warning, assetPath, "Nodes",
                        $"Node '{node.NodeId}' ({node.GetType().Name}) is never reachable from StartNodeId.",
                        "Not blocking -- but this node is authored dead content. Link it in from somewhere, or remove it."));
                }
            }
        }

        /// <summary>Reverse-reachability fixpoint: a node "can pause" if a real player-facing stop (Line/Choice/
        /// Check/End) is reachable from it. An ActionNode can-pause iff its single successor can; a BranchNode
        /// can-pause only if BOTH successors can (the validator can't know which branch a real Timeline state
        /// will take, so it must assume the worst case). Any Branch/Action node that can never pause would trip
        /// DialogueRunner's MaxAutoSettleSteps guard the moment runtime reached it.</summary>
        private static void ValidateNoInescapableAutoCycle(DialogueDefinition definition, string assetPath, Dictionary<SerializableGuid, DialogueNode> nodesById, List<ValidationResult> results)
        {
            var canPause = new Dictionary<SerializableGuid, bool>();
            foreach (var node in definition.Nodes)
                canPause[node.NodeId] = node is LineNode or ChoiceNode or CheckNode or EndNode;

            bool changed;
            do
            {
                changed = false;
                foreach (var node in definition.Nodes)
                {
                    if (canPause[node.NodeId]) continue;

                    bool nowCanPause = node switch
                    {
                        ActionNode action => ResolvesToPause(action.NextNodeId, nodesById, canPause),
                        BranchNode branch => ResolvesToPause(branch.TrueNodeId, nodesById, canPause) && ResolvesToPause(branch.FalseNodeId, nodesById, canPause),
                        _ => true,
                    };

                    if (nowCanPause)
                    {
                        canPause[node.NodeId] = true;
                        changed = true;
                    }
                }
            } while (changed);

            foreach (var node in definition.Nodes)
            {
                if ((node is ActionNode or BranchNode) && !canPause[node.NodeId])
                {
                    results.Add(new ValidationResult(ValidationSeverity.Error, assetPath, "Nodes",
                        $"Node '{node.NodeId}' ({node.GetType().Name}) can never reach a Line/Choice/Check/End node -- every path from it stays inside automatic Branch/Action nodes forever.",
                        "Break the cycle by pointing at least one path toward a Line/Choice/Check/End node. Left as-is, this would hang at runtime once DialogueRunner's step budget is exceeded."));
                }
            }
        }

        private static bool ResolvesToPause(SerializableGuid target, Dictionary<SerializableGuid, DialogueNode> nodesById, Dictionary<SerializableGuid, bool> canPause) =>
            nodesById.ContainsKey(target) && canPause.TryGetValue(target, out var value) && value;

        private static IEnumerable<SerializableGuid> Successors(DialogueNode node)
        {
            switch (node)
            {
                case LineNode line:
                    yield return line.NextNodeId;
                    break;
                case ActionNode action:
                    yield return action.NextNodeId;
                    break;
                case BranchNode branch:
                    yield return branch.TrueNodeId;
                    yield return branch.FalseNodeId;
                    break;
                case CheckNode check:
                    yield return check.SuccessNodeId;
                    yield return check.FailureNodeId;
                    break;
                case ChoiceNode choice:
                    foreach (var option in choice.Options) yield return option.NextNodeId;
                    break;
            }
        }
    }
}

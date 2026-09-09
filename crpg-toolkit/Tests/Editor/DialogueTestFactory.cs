using System.Collections.Generic;
using CRPG.Core;
using CRPG.Dialogue;
using CRPG.Inventory;
using UnityEngine;

namespace CRPG.Tests.EditMode
{
    /// <summary>Builds dialogue nodes/actions/definitions via reflection, same rationale as TestDefinitionFactory.</summary>
    internal static class DialogueTestFactory
    {
        public static LineNode CreateLine(string nodeId, string speakerName, string text, string nextNodeId)
        {
            var node = new LineNode();
            TestDefinitionFactory.SetPrivateField(node, "nodeId", nodeId);
            TestDefinitionFactory.SetPrivateField(node, "speakerName", speakerName);
            TestDefinitionFactory.SetPrivateField(node, "text", text);
            TestDefinitionFactory.SetPrivateField(node, "nextNodeId", nextNodeId);
            return node;
        }

        public static ChoiceOption CreateOption(string text, string nextNodeId)
        {
            var option = new ChoiceOption();
            TestDefinitionFactory.SetPrivateField(option, "text", text);
            TestDefinitionFactory.SetPrivateField(option, "nextNodeId", nextNodeId);
            return option;
        }

        public static ChoiceOption CreateConditionalOption(
            string text, string nextNodeId, string timelineKey, BranchConditionType conditionType, double threshold, string secondTimelineKey = null)
        {
            var option = CreateOption(text, nextNodeId);
            TestDefinitionFactory.SetPrivateField(option, "hasVisibilityCondition", true);
            TestDefinitionFactory.SetPrivateField(option, "timelineKey", timelineKey);
            TestDefinitionFactory.SetPrivateField(option, "secondTimelineKey", secondTimelineKey);
            TestDefinitionFactory.SetPrivateField(option, "conditionType", conditionType);
            TestDefinitionFactory.SetPrivateField(option, "threshold", threshold);
            return option;
        }

        public static ChoiceNode CreateChoice(string nodeId, params ChoiceOption[] options)
        {
            var node = new ChoiceNode();
            TestDefinitionFactory.SetPrivateField(node, "nodeId", nodeId);
            TestDefinitionFactory.SetPrivateField(node, "options", new List<ChoiceOption>(options));
            return node;
        }

        public static BranchNode CreateBranch(
            string nodeId, string timelineKey, BranchConditionType conditionType, double threshold,
            string trueNodeId, string falseNodeId, string secondTimelineKey = null)
        {
            var node = new BranchNode();
            TestDefinitionFactory.SetPrivateField(node, "nodeId", nodeId);
            TestDefinitionFactory.SetPrivateField(node, "timelineKey", timelineKey);
            TestDefinitionFactory.SetPrivateField(node, "secondTimelineKey", secondTimelineKey);
            TestDefinitionFactory.SetPrivateField(node, "conditionType", conditionType);
            TestDefinitionFactory.SetPrivateField(node, "threshold", threshold);
            TestDefinitionFactory.SetPrivateField(node, "trueNodeId", trueNodeId);
            TestDefinitionFactory.SetPrivateField(node, "falseNodeId", falseNodeId);
            return node;
        }

        public static CheckNode CreateCheck(
            string nodeId, string queryKey, int difficultyClass, CheckPerformer performer, string successNodeId, string failureNodeId)
        {
            var node = new CheckNode();
            TestDefinitionFactory.SetPrivateField(node, "nodeId", nodeId);
            TestDefinitionFactory.SetPrivateField(node, "queryKey", queryKey);
            TestDefinitionFactory.SetPrivateField(node, "difficultyClass", difficultyClass);
            TestDefinitionFactory.SetPrivateField(node, "performer", performer);
            TestDefinitionFactory.SetPrivateField(node, "successNodeId", successNodeId);
            TestDefinitionFactory.SetPrivateField(node, "failureNodeId", failureNodeId);
            return node;
        }

        public static ActionNode CreateAction(string nodeId, DialogueAction action, string nextNodeId)
        {
            var node = new ActionNode();
            TestDefinitionFactory.SetPrivateField(node, "nodeId", nodeId);
            TestDefinitionFactory.SetPrivateField(node, "action", action);
            TestDefinitionFactory.SetPrivateField(node, "nextNodeId", nextNodeId);
            return node;
        }

        public static EndNode CreateEnd(string nodeId)
        {
            var node = new EndNode();
            TestDefinitionFactory.SetPrivateField(node, "nodeId", nodeId);
            return node;
        }

        public static RecordEventAction CreateRecordEventAction(
            string key, TimelineValueKind kind = TimelineValueKind.Flag, double number = 0, string text = null, double amount = 1)
        {
            var action = new RecordEventAction();
            TestDefinitionFactory.SetPrivateField(action, "key", key);
            TestDefinitionFactory.SetPrivateField(action, "valueKind", kind);
            TestDefinitionFactory.SetPrivateField(action, "numberValue", number);
            TestDefinitionFactory.SetPrivateField(action, "textValue", text);
            TestDefinitionFactory.SetPrivateField(action, "amount", amount);
            return action;
        }

        public static OpenStoreAction CreateOpenStoreAction() => new();

        public static SetStorePriceMultiplierAction CreateSetStorePriceMultiplierAction(float buyPriceMultiplier, float sellPriceMultiplier)
        {
            var action = new SetStorePriceMultiplierAction();
            TestDefinitionFactory.SetPrivateField(action, "buyPriceMultiplier", buyPriceMultiplier);
            TestDefinitionFactory.SetPrivateField(action, "sellPriceMultiplier", sellPriceMultiplier);
            return action;
        }

        public static GiveItemAction CreateGiveItemAction(ItemDefinition item, int quantity = 1)
        {
            var action = new GiveItemAction();
            TestDefinitionFactory.SetPrivateField(action, "item", item);
            TestDefinitionFactory.SetPrivateField(action, "quantity", quantity);
            return action;
        }

        public static DialogueDefinition CreateDialogue(string id, string startNodeId, params DialogueNode[] nodes)
        {
            var definition = ScriptableObject.CreateInstance<DialogueDefinition>();
            TestDefinitionFactory.SetPrivateField(definition, "id", id);
            TestDefinitionFactory.SetPrivateField(definition, "startNodeId", startNodeId);
            TestDefinitionFactory.SetPrivateField(definition, "nodes", new List<DialogueNode>(nodes));
            return definition;
        }
    }
}

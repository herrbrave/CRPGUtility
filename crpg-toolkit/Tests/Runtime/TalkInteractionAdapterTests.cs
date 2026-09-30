using System.Collections.Generic;
using System.Reflection;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Dialogue;
using CRPG.Gameplay.Interactions;
using CRPG.Gameplay.Interactions.Adapters;
using CRPG.Inventory;
using CRPG.Party;
using CRPG.Rules;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace CRPG.Tests.PlayMode
{
    /// <summary>Task 6.0 (2D-Campaign-Phase6-CampaignContent.md): proves TalkInteractionAdapter's two new
    /// pass-through properties (RestService, RequestSave) actually reach the DialogueContext it builds --
    /// there was no dedicated test file for this adapter before now (confirmed by search; the Opus review
    /// caught a first-draft plan wrongly assuming one existed to extend). Mirrors just the reflection helpers
    /// this file needs, the same "Tests/Editor's DialogueTestFactory isn't visible from PlayMode" precedent
    /// ConversationControllerTests.cs already established.</summary>
    public class TalkInteractionAdapterTests
    {
        private static SerializableGuid Id(string seed) => SerializableGuid.FromString(seed);

        private static void SetField(object target, string fieldName, object value)
        {
            var type = target.GetType();
            while (type != null)
            {
                var field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (field != null) { field.SetValue(target, value); return; }
                type = type.BaseType;
            }

            throw new System.Exception($"Field '{fieldName}' not found on {target.GetType().Name} or its base types.");
        }

        private static ActionNode CreateActionNode(string nodeId, DialogueAction action, string nextNodeId)
        {
            var node = new ActionNode();
            SetField(node, "nodeId", Id(nodeId));
            SetField(node, "action", action);
            SetField(node, "nextNodeId", Id(nextNodeId));
            return node;
        }

        private static EndNode CreateEnd(string nodeId)
        {
            var node = new EndNode();
            SetField(node, "nodeId", Id(nodeId));
            return node;
        }

        private static DialogueDefinition CreateDialogue(string startNodeId, params DialogueNode[] nodes)
        {
            var definition = ScriptableObject.CreateInstance<DialogueDefinition>();
            SetField(definition, "id", "dlg-test");
            SetField(definition, "startNodeId", Id(startNodeId));
            SetField(definition, "nodes", new List<DialogueNode>(nodes));
            return definition;
        }

        private static ResourceDefinition CreateResourceDefinition(string id, RestTrigger restTrigger)
        {
            var definition = ScriptableObject.CreateInstance<ResourceDefinition>();
            SetField(definition, "id", id);
            SetField(definition, "displayName", id);
            SetField(definition, "restTrigger", restTrigger);
            return definition;
        }

        private GameObject go;
        private TalkInteractionAdapter adapter;
        private CharacterRegistry characters;
        private RulesDatabase rulesDatabase;
        private CharacterState fighter;

        [SetUp]
        public void SetUp()
        {
            // Built inactive so RequireComponent's WorldInteractable doesn't run its own Awake() (which logs an
            // error without an explicit interactionAnchor) before Configure() below can supply one -- this test
            // calls BeginExecute directly and never goes through WorldInteractable's own click resolution, so an
            // anchor's exact position doesn't matter, only that one exists.
            go = new GameObject("TalkAdapter");
            go.SetActive(false);
            adapter = go.AddComponent<TalkInteractionAdapter>();
            adapter.GetComponent<WorldInteractable>().Configure(go.transform, 1.5f);
            SetField(adapter, "panelSettings", ScriptableObject.CreateInstance<PanelSettings>());
            go.SetActive(true);

            rulesDatabase = new RulesDatabase();
            characters = new CharacterRegistry();
            fighter = new CharacterState(RuntimeEntityId.NewId());
            characters.Register(fighter);

            adapter.Party = new PartyState(new[] { fighter.Id });
            adapter.Characters = characters;
            adapter.RulesDatabase = rulesDatabase;
            adapter.Timeline = new Timeline();
            adapter.Inventory = new PartyInventoryState();
            adapter.DiceService = new DiceService(new SeededRandomSource(1));
        }

        [TearDown]
        public void TearDown()
        {
            if (go != null) Object.DestroyImmediate(go);
        }

        [Test]
        public void TheAdaptersRestServicePropertyReachesTheConstructedContext()
        {
            var hp = CreateResourceDefinition("resource-hp", RestTrigger.LongRest);
            rulesDatabase.Register(hp);
            fighter.AddResource(new ResourceState(hp.Id, max: 10, current: 2));
            adapter.RestService = new RestService(characters, rulesDatabase);

            var end = CreateEnd("end");
            var actionNode = CreateActionNode("start", new RequestLongRestAction(), "end");
            SetField(adapter, "dialogue", CreateDialogue("start", actionNode, end));

            var execution = adapter.BeginExecute(fighter.Id, _ => { });

            fighter.TryGetResource(hp.Id, out var hpState);
            Assert.AreEqual(10, hpState.Current);
            execution.Cancel();
        }

        [Test]
        public void TheAdaptersRequestSavePropertyReachesTheConstructedContext()
        {
            var saveRequested = false;
            adapter.RequestSave = () => saveRequested = true;

            var end = CreateEnd("end");
            var actionNode = CreateActionNode("start", new RequestSaveAction(), "end");
            SetField(adapter, "dialogue", CreateDialogue("start", actionNode, end));

            var execution = adapter.BeginExecute(fighter.Id, _ => { });

            Assert.IsTrue(saveRequested);
            execution.Cancel();
        }

        [Test]
        public void WithNeitherPropertySetBothActionsAreSilentNoOps()
        {
            // Both RestService and RequestSave are left unset (adapter's own defaults) -- proves
            // RequestLongRestAction/RequestSaveAction's own null-safety holds through the real adapter, not just
            // in DialogueRunnerTests' more direct construction.
            var end = CreateEnd("end");
            var restNode = CreateActionNode("start", new RequestLongRestAction(), "save");
            var saveNode = CreateActionNode("save", new RequestSaveAction(), "end");
            SetField(adapter, "dialogue", CreateDialogue("start", restNode, saveNode, end));

            IInteractionExecution execution = null;
            Assert.DoesNotThrow(() => execution = adapter.BeginExecute(fighter.Id, _ => { }));
            execution?.Cancel();
        }

        // Task 6.1 (2D-Campaign-Phase6-CampaignContent.md): RequestRecruit/RequestStartQuest are the same
        // pass-through shape as RestService/RequestSave above (Task 6.0's own precedent).
        [Test]
        public void TheAdaptersRequestRecruitPropertyReachesTheConstructedContextWithTheActionsOwnCompanionKey()
        {
            string recruited = null;
            adapter.RequestRecruit = key => recruited = key;

            var end = CreateEnd("end");
            var action = new RecruitToPartyAction();
            SetField(action, "companionKey", "elara");
            var actionNode = CreateActionNode("start", action, "end");
            SetField(adapter, "dialogue", CreateDialogue("start", actionNode, end));

            var execution = adapter.BeginExecute(fighter.Id, _ => { });

            Assert.AreEqual("elara", recruited);
            execution.Cancel();
        }

        [Test]
        public void TheAdaptersRequestStartQuestPropertyReachesTheConstructedContextWithTheActionsOwnQuestKey()
        {
            string started = null;
            adapter.RequestStartQuest = key => started = key;

            var end = CreateEnd("end");
            var action = new StartQuestAction();
            SetField(action, "questKey", "quest-gathering-storm");
            var actionNode = CreateActionNode("start", action, "end");
            SetField(adapter, "dialogue", CreateDialogue("start", actionNode, end));

            var execution = adapter.BeginExecute(fighter.Id, _ => { });

            Assert.AreEqual("quest-gathering-storm", started);
            execution.Cancel();
        }

        [Test]
        public void WithNeitherRecruitNorQuestPropertySetBothActionsAreSilentNoOps()
        {
            var end = CreateEnd("end");
            var recruitAction = new RecruitToPartyAction();
            SetField(recruitAction, "companionKey", "elara");
            var questAction = new StartQuestAction();
            SetField(questAction, "questKey", "quest-gathering-storm");
            var recruitNode = CreateActionNode("start", recruitAction, "quest");
            var questNode = CreateActionNode("quest", questAction, "end");
            SetField(adapter, "dialogue", CreateDialogue("start", recruitNode, questNode, end));

            IInteractionExecution execution = null;
            Assert.DoesNotThrow(() => execution = adapter.BeginExecute(fighter.Id, _ => { }));
            execution?.Cancel();
        }
    }
}

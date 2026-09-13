using System.Collections.Generic;
using System.Reflection;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Dialogue;
using CRPG.Gameplay;
using CRPG.Inventory;
using CRPG.Party;
using CRPG.Rules;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace CRPG.Tests.PlayMode
{
    /// <summary>
    /// Task 3.4 (Production Foundation): ConversationController's store hand-off round-trip and cancellation
    /// contract, driven against real DialogueView/StoreView MonoBehaviours (via its CurrentDialogueView/
    /// CurrentStoreView diagnostics -- same rationale as PlayableCharacterAnimationDriver's own test-only
    /// diagnostics). Builds DialogueDefinitions via a small local reflection helper since Tests/Editor's
    /// DialogueTestFactory isn't visible from this PlayMode assembly (mirrors CharacterBindingTests' own
    /// "mirror just the two reflection helpers this file needs" precedent).
    /// </summary>
    public class ConversationControllerTests
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

        private static LineNode CreateLine(string nodeId, string text, string nextNodeId)
        {
            var node = new LineNode();
            SetField(node, "nodeId", Id(nodeId));
            SetField(node, "speakerName", "Shopkeep");
            SetField(node, "text", text);
            SetField(node, "nextNodeId", Id(nextNodeId));
            return node;
        }

        private static EndNode CreateEnd(string nodeId)
        {
            var node = new EndNode();
            SetField(node, "nodeId", Id(nodeId));
            return node;
        }

        private static ActionNode CreateOpenStoreNode(string nodeId, string nextNodeId)
        {
            var node = new ActionNode();
            SetField(node, "nodeId", Id(nodeId));
            SetField(node, "action", new OpenStoreAction());
            SetField(node, "nextNodeId", Id(nextNodeId));
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

        private DialogueContext context;
        private PanelSettings panelSettings;
        private ConversationController controller;

        private ConversationController CreateController(DialogueDefinition dialogue)
        {
            controller = new ConversationController(dialogue, context, panelSettings);
            return controller;
        }

        [TearDown]
        public void TearDown()
        {
            // UIToolkitPointerCapture's modal depth is process-static -- a test that ends mid-conversation
            // (store open, or paused at a Line) would otherwise leak an un-disposed modal scope into whichever
            // test runs next. Cancel() is idempotent and safe even after the conversation already completed.
            controller?.Cancel();
        }

        [SetUp]
        public void SetUp()
        {
            var rulesDatabase = new RulesDatabase();
            var characters = new CharacterRegistry();
            var fighter = new CharacterState(RuntimeEntityId.NewId());
            characters.Register(fighter);
            var party = new PartyState(new[] { fighter.Id });
            var inventory = new PartyInventoryState();
            var timeline = new Timeline();
            var diceService = new DiceService(new SeededRandomSource(1));
            var store = new StoreInventoryState();

            context = new DialogueContext(RuntimeEntityId.NewId(), party, characters, rulesDatabase, timeline, inventory, diceService, store: store);
            panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
        }

        [Test]
        public void AConversationWithNoStoreRequestShowsTheDialogueViewAndDoesNotCompleteImmediately()
        {
            var end = CreateEnd("end");
            var line = CreateLine("start", "Halt.", "end");
            var dialogue = CreateDialogue("start", line, end);

            var completed = false;
            CreateController(dialogue);
            controller.Completed += () => completed = true;

            Assert.IsNotNull(controller.CurrentDialogueView);
            Assert.IsNull(controller.CurrentStoreView);
            Assert.IsFalse(completed);
        }

        [Test]
        public void AConversationThatStartsAtEndCompletesImmediatelyOnConstruction()
        {
            // Completed fires synchronously from inside the constructor for this case -- there is no way for
            // a caller to subscribe in time to observe the event itself, so this asserts the resulting STATE
            // (both views torn down) rather than racing to catch the event.
            var end = CreateEnd("end");
            var dialogue = CreateDialogue("end", end);

            CreateController(dialogue);

            Assert.IsNull(controller.CurrentDialogueView);
            Assert.IsNull(controller.CurrentStoreView);
        }

        [Test]
        public void OpeningTheStoreShowsTheStoreViewInsteadOfCompletingEvenThoughTheRunnerAlreadySettledPastIt()
        {
            // OpenStoreAction's own NextNodeId ("thanks") is itself a real pause point (a Line) -- proves the
            // controller notices the store request BEFORE presenting whatever the runner already auto-settled
            // to while executing the action, not after.
            var end = CreateEnd("end");
            var thanks = CreateLine("thanks", "Thanks for shopping!", "end");
            var openStore = CreateOpenStoreNode("start", "thanks");
            var dialogue = CreateDialogue("start", openStore, thanks, end);

            var completed = false;
            CreateController(dialogue);
            controller.Completed += () => completed = true;

            Assert.IsNotNull(controller.CurrentStoreView);
            Assert.IsNull(controller.CurrentDialogueView);
            Assert.IsFalse(completed);
        }

        [Test]
        public void ClosingTheStoreResumesTheConversationAtWhereverItActuallyLeftOffRatherThanCompleting()
        {
            var end = CreateEnd("end");
            var thanks = CreateLine("thanks", "Thanks for shopping!", "end");
            var openStore = CreateOpenStoreNode("start", "thanks");
            var dialogue = CreateDialogue("start", openStore, thanks, end);

            var completed = false;
            CreateController(dialogue);
            controller.Completed += () => completed = true;

            controller.CurrentStoreView.Leave();

            Assert.IsFalse(completed, "The dialogue still has a real Line node (\"thanks\") to show -- resuming must not skip straight to completion.");
            Assert.IsNotNull(controller.CurrentDialogueView);
            Assert.IsNull(controller.CurrentStoreView);
        }

        [Test]
        public void CancelTearsDownAndFiresCancelledNeverCompleted()
        {
            var end = CreateEnd("end");
            var line = CreateLine("start", "Halt.", "end");
            var dialogue = CreateDialogue("start", line, end);

            var completed = false;
            var cancelled = false;
            CreateController(dialogue);
            controller.Completed += () => completed = true;
            controller.Cancelled += () => cancelled = true;

            controller.Cancel();

            Assert.IsTrue(cancelled);
            Assert.IsFalse(completed);
            Assert.IsNull(controller.CurrentDialogueView);
            Assert.IsNull(controller.CurrentStoreView);
        }

        [Test]
        public void CancelIsIdempotentAndSafeAfterAlreadyCompleting()
        {
            var end = CreateEnd("end");
            var dialogue = CreateDialogue("end", end);

            var cancelled = false;
            CreateController(dialogue);
            controller.Cancelled += () => cancelled = true;

            Assert.DoesNotThrow(() => controller.Cancel());
            Assert.IsFalse(cancelled, "Already Completed -- Cancel() after the fact must not retroactively fire Cancelled.");
        }
    }
}

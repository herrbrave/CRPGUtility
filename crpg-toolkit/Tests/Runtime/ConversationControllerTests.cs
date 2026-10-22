using System.Collections.Generic;
using System.Reflection;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Dialogue;
using CRPG.Gameplay;
using CRPG.Gameplay.Interactions;
using CRPG.Gameplay.Interactions.Adapters;
using CRPG.Inventory;
using CRPG.Party;
using CRPG.Rules;
using CRPG.Rules.SRD5;
using CRPG.UI;
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

        private static ChoiceNode CreateChoice(string nodeId, string optionText, string nextNodeId)
        {
            var option = new ChoiceOption();
            SetField(option, "text", optionText);
            SetField(option, "nextNodeId", Id(nextNodeId));
            var node = new ChoiceNode();
            SetField(node, "nodeId", Id(nodeId));
            SetField(node, "options", new List<ChoiceOption> { option });
            return node;
        }

        private static ActionNode CreateRecordEventNode(string nodeId, string eventKey, string nextNodeId)
        {
            var action = new RecordEventAction();
            SetField(action, "key", eventKey);
            var node = new ActionNode();
            SetField(node, "nodeId", Id(nodeId));
            SetField(node, "action", action);
            SetField(node, "nextNodeId", Id(nextNodeId));
            return node;
        }

        // Resolve the planned API at runtime so the baseline fails an assertion, not compilation.
        private static DialoguePresenter ReadControllerPresenter(ConversationController owner)
        {
            var property = typeof(ConversationController).GetProperty("Presenter", BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(property, "ConversationController must expose public read-only DialoguePresenter Presenter, retained for its entire lifetime including the shop.");
            Assert.AreEqual(typeof(DialoguePresenter), property.PropertyType);
            Assert.NotNull(property.GetGetMethod(), "ConversationController.Presenter must have a public getter.");
            Assert.IsNull(property.GetSetMethod(), "ConversationController.Presenter must not have a public setter.");
            var presenter = (DialoguePresenter)property.GetValue(owner);
            Assert.NotNull(presenter, "The controller's retained Presenter must exist even while the shop is open.");
            return presenter;
        }

        private static T ReadPresenterProperty<T>(DialoguePresenter presenter, string propertyName)
        {
            var property = typeof(DialoguePresenter).GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(property, $"DialoguePresenter must expose public read-only {typeof(T).Name} {propertyName} for retained dialogue context.");
            Assert.AreEqual(typeof(T), property.PropertyType, $"DialoguePresenter.{propertyName} has the wrong type.");
            Assert.NotNull(property.GetGetMethod(), $"DialoguePresenter.{propertyName} must have a public getter.");
            Assert.IsNull(property.GetSetMethod(), $"DialoguePresenter.{propertyName} must not have a public setter.");
            return (T)property.GetValue(presenter);
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
        private GameObject adapterGo;

        private static int ModalDepth => (int)typeof(UIToolkitPointerCapture)
            .GetField("modalDepth", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);

        private sealed class TestConversationView : IConversationView
        {
            public event System.Action Completed;
            public event System.Action Driven;
            public DialoguePresenter Presenter { get; private set; }
            public bool PlainStyle { get; private set; }
            public IDialogueCheckPresenter CheckPresenter { get; private set; }
            public bool SubscribedBeforeInitialize { get; private set; }
            public int DisposeCount { get; private set; }
            public System.Action<TestConversationView> OnInitialize;
            public System.Action OnDispose;

            public void Initialize(DialoguePresenter presenter, bool plainStyle = false, IDialogueCheckPresenter checkPresenter = null)
            {
                SubscribedBeforeInitialize = Completed != null && Driven != null;
                Presenter = presenter;
                PlainStyle = plainStyle;
                CheckPresenter = checkPresenter;
                OnInitialize?.Invoke(this);
            }

            public void Advance()
            {
                Presenter.Advance();
                if (Presenter.IsComplete) EmitCompleted();
                else EmitDriven();
            }

            // Deliberately retain delegates on Dispose: tests prove the owner unsubscribes, rather than
            // allowing an accommodating fake to conceal stale callbacks or doubled disposal.
            public void EmitCompleted() => Completed?.Invoke();
            public void EmitDriven() => Driven?.Invoke();
            public void Dispose()
            {
                DisposeCount++;
                OnDispose?.Invoke();
            }
        }

        private sealed class SynchronousCheckPresenter : IDialogueCheckPresenter
        {
            public void Present(CheckPrompt prompt, System.Func<CheckRollOptions, DialogueCheckOutcome> roll, System.Action done)
            {
                roll(null);
                done();
            }
        }

        private static CheckNode CreateCheck(string nodeId, string nextNodeId)
        {
            var node = new CheckNode();
            SetField(node, "nodeId", Id(nodeId));
            SetField(node, "queryKey", "Skill:Persuasion");
            SetField(node, "difficultyClass", 1);
            SetField(node, "performer", CheckPerformer.ActiveCharacter);
            SetField(node, "successNodeId", Id(nextNodeId));
            SetField(node, "failureNodeId", Id(nextNodeId));
            return node;
        }

        private TalkInteractionAdapter CreateAdapter(DialogueDefinition dialogue)
        {
            adapterGo = new GameObject("CustomConversationAdapter");
            adapterGo.SetActive(false);
            var adapter = adapterGo.AddComponent<TalkInteractionAdapter>();
            adapter.GetComponent<WorldInteractable>().Configure(adapterGo.transform, 1.5f);
            adapter.Dialogue = dialogue;
            adapter.Party = context.Party;
            adapter.Characters = context.Characters;
            adapter.RulesDatabase = context.RulesDatabase;
            adapter.Timeline = context.Timeline;
            adapter.Inventory = context.PartyInventory;
            adapter.DiceService = context.DiceService;
            adapterGo.SetActive(true);
            return adapter;
        }

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
            if (adapterGo != null) Object.DestroyImmediate(adapterGo);
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

            context = new DialogueContext(RuntimeEntityId.NewId(), party, characters, rulesDatabase, timeline, inventory, diceService,
                new DefaultSkillCheckRule(characters, rulesDatabase), store: store);
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
        public void AnOpenStoreActionReachedMidConversation_OpensTheShop_AndLeavingResumesTheDialogue()
        {
            // Line -> (player continues) -> Open Store -> Line -> End: the shop must open when the player's Continue reaches the action, not only when the graph STARTS with it.
            var end = CreateEnd("end");
            var after = CreateLine("after", "Anything else?", "end");
            var openStore = CreateOpenStoreNode("open", "after");
            var hello = CreateLine("start", "Welcome.", "open");
            var dialogue = CreateDialogue("start", hello, openStore, after, end);

            CreateController(dialogue);
            Assert.IsNotNull(controller.CurrentDialogueView);
            Assert.IsNull(controller.CurrentStoreView);

            typeof(CRPG.UI.DialogueView).GetMethod("OnContinueClicked", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(controller.CurrentDialogueView, null);

            Assert.IsNotNull(controller.CurrentStoreView, "the Continue reached the Open Store action");
            Assert.IsNull(controller.CurrentDialogueView);

            controller.CurrentStoreView.Leave();
            Assert.IsNotNull(controller.CurrentDialogueView, "back to the dialogue, at the line after the shop");
            Assert.IsNull(controller.CurrentStoreView);
        }

        [Test]
        public void ShopRoundTripRetainsTheSamePresenterNodeAndNpcPromptWithoutReplayingActions()
        {
            // The runner settles on a choice after OpenStoreAction. No new Line can reconstruct the
            // previous NPC prompt when the dialogue view is recreated, so the presenter must survive.
            var end = CreateEnd("end");
            var resume = CreateChoice("resume", "Goodbye", "end");
            var openStore = CreateOpenStoreNode("open", "resume");
            var record = CreateRecordEventNode("record", "shop-visit", "open");
            var choice = CreateChoice("reply", "Show me your wares", "record");
            var hello = CreateLine("start", "Welcome. What do you need?", "reply");
            var dialogue = CreateDialogue("start", hello, choice, record, openStore, resume, end);
            CreateController(dialogue);
            var completed = false;
            controller.Completed += () => completed = true;

            var presenter = ReadControllerPresenter(controller);
            var viewPresenterField = typeof(DialogueView).GetField("presenter", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(viewPresenterField, "Inspect the presenter actually bound to the real dialogue view.");
            Assert.IsNotNull(controller.CurrentDialogueView);
            Assert.AreSame(presenter, viewPresenterField.GetValue(controller.CurrentDialogueView));
            Assert.AreEqual(Id("start"), ReadPresenterProperty<SerializableGuid>(presenter, "CurrentNodeId"));

            var continueMethod = typeof(DialogueView).GetMethod("OnContinueClicked", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(continueMethod);
            continueMethod.Invoke(controller.CurrentDialogueView, null);

            Assert.AreSame(presenter, ReadControllerPresenter(controller));
            Assert.IsTrue(presenter.IsAtChoice);
            Assert.AreEqual(Id("reply"), ReadPresenterProperty<SerializableGuid>(presenter, "CurrentNodeId"));
            Assert.AreEqual("Welcome. What do you need?", ReadPresenterProperty<string>(presenter, "LastLineText"));
            Assert.AreEqual("Shopkeep", ReadPresenterProperty<string>(presenter, "LastSpeakerName"));
            Assert.AreEqual(Id("start"), ReadPresenterProperty<SerializableGuid>(presenter, "LastLineNodeId"));
            Assert.AreEqual(0, context.Timeline.CountOccurrences("shop-visit"));

            var choiceMethod = typeof(DialogueView).GetMethod("OnChoiceClicked", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(choiceMethod);
            choiceMethod.Invoke(controller.CurrentDialogueView, new object[] { 0 });

            Assert.IsNotNull(controller.CurrentStoreView);
            Assert.IsNull(controller.CurrentDialogueView);
            Assert.IsFalse(completed);
            Assert.AreSame(presenter, ReadControllerPresenter(controller), "Opening the shop must not discard the dialogue presenter.");
            Assert.AreEqual(Id("resume"), ReadPresenterProperty<SerializableGuid>(presenter, "CurrentNodeId"));
            Assert.AreEqual("Welcome. What do you need?", ReadPresenterProperty<string>(presenter, "LastLineText"));
            Assert.AreEqual("Shopkeep", ReadPresenterProperty<string>(presenter, "LastSpeakerName"));
            Assert.AreEqual(Id("start"), ReadPresenterProperty<SerializableGuid>(presenter, "LastLineNodeId"));
            Assert.AreEqual(1, context.Timeline.CountOccurrences("shop-visit"));

            controller.CurrentStoreView.Leave();

            Assert.IsNull(controller.CurrentStoreView);
            Assert.IsNotNull(controller.CurrentDialogueView);
            Assert.IsFalse(completed);
            Assert.AreSame(presenter, ReadControllerPresenter(controller), "Closing the shop must reuse the same presenter, not wrap the runner again.");
            Assert.AreSame(presenter, viewPresenterField.GetValue(controller.CurrentDialogueView), "The recreated view must bind the retained presenter.");
            Assert.IsTrue(presenter.IsAtChoice);
            Assert.AreEqual(Id("resume"), ReadPresenterProperty<SerializableGuid>(presenter, "CurrentNodeId"));
            Assert.AreEqual(string.Empty, presenter.LineText);
            Assert.AreEqual(string.Empty, presenter.SpeakerName);
            Assert.AreEqual("Welcome. What do you need?", ReadPresenterProperty<string>(presenter, "LastLineText"));
            Assert.AreEqual("Shopkeep", ReadPresenterProperty<string>(presenter, "LastSpeakerName"));
            Assert.AreEqual(Id("start"), ReadPresenterProperty<SerializableGuid>(presenter, "LastLineNodeId"));
            Assert.AreEqual("Goodbye", presenter.GetChoices()[0].Text);
            Assert.AreEqual("Welcome. What do you need?", ((Label)typeof(DialogueView)
                .GetField("textLabel", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(controller.CurrentDialogueView)).text,
                "The real UITK view must display the retained NPC prompt beside choices after returning from the shop.");
            Assert.AreEqual("Shopkeep", ((Label)typeof(DialogueView)
                .GetField("speakerLabel", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(controller.CurrentDialogueView)).text);
            Assert.AreEqual(1, context.Timeline.CountOccurrences("shop-visit"), "Closing the shop must not replay dialogue actions.");

            choiceMethod.Invoke(controller.CurrentDialogueView, new object[] { 0 });

            Assert.IsTrue(completed);
            Assert.IsNull(controller.CurrentDialogueView);
            Assert.IsNull(controller.CurrentStoreView);
            Assert.AreSame(presenter, ReadControllerPresenter(controller), "The controller owns one presenter for its whole lifetime.");
            Assert.AreEqual(1, context.Timeline.CountOccurrences("shop-visit"));
        }

        [Test]
        public void AnOpenStoreActionInAConversationWithNoStore_IsSkippedWithAWarning_NotAnException()
        {
            var characters = new CharacterRegistry();
            var fighter = new CharacterState(RuntimeEntityId.NewId());
            characters.Register(fighter);
            var noStoreContext = new DialogueContext(RuntimeEntityId.NewId(), new PartyState(new[] { fighter.Id }), characters, new RulesDatabase(), new Timeline(),
                new PartyInventoryState(), new DiceService(new SeededRandomSource(1)));   // no store wired

            var end = CreateEnd("end");
            var thanks = CreateLine("thanks", "No shop here, friend.", "end");
            var dialogue = CreateDialogue("start", CreateOpenStoreNode("start", "thanks"), thanks, end);

            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("asked to open a store but this conversation has none"));
            controller = new ConversationController(dialogue, noStoreContext, panelSettings);

            Assert.IsNull(controller.CurrentStoreView, "no store screen");
            Assert.IsNotNull(controller.CurrentDialogueView, "the dialogue carried on to the next line");
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

        [TestCase(false)]
        [TestCase(true)]
        public void CustomViewOwnsNoDefaultDocumentAndIsDisposedOnceOnCompleteOrCancel(bool cancel)
        {
            var dialogue = CreateDialogue("start", CreateLine("start", "Welcome.", "end"), CreateEnd("end"));
            var view = new TestConversationView();
            var checkPresenter = new SynchronousCheckPresenter();
            var depth = ModalDepth;
            var documents = Object.FindObjectsByType<UIDocument>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            var factoryCalls = 0;
            controller = new ConversationController(dialogue, context, null, true, checkPresenter, () => { factoryCalls++; return view; });
            var completed = 0;
            var cancelled = 0;
            controller.Completed += () => completed++;
            controller.Cancelled += () => cancelled++;

            Assert.AreEqual(1, factoryCalls);
            Assert.IsTrue(view.SubscribedBeforeInitialize);
            Assert.IsTrue(view.PlainStyle);
            Assert.AreSame(checkPresenter, view.CheckPresenter);
            Assert.AreSame(controller.Presenter, view.Presenter);
            Assert.AreSame(view, controller.CurrentView);
            Assert.IsNull(controller.CurrentDialogueView);
            Assert.IsNull(controller.CurrentStoreView);
            Assert.AreEqual(documents, Object.FindObjectsByType<UIDocument>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length);
            Assert.AreEqual(depth + 1, ModalDepth);

            if (cancel) controller.Cancel();
            else view.Advance();
            controller.Dispose();
            view.EmitCompleted();
            view.EmitDriven();

            Assert.IsTrue(controller.IsEnded);
            Assert.AreEqual(cancel ? 0 : 1, completed);
            Assert.AreEqual(cancel ? 1 : 0, cancelled);
            Assert.AreEqual(1, view.DisposeCount);
            Assert.IsNull(controller.CurrentView);
            Assert.AreEqual(depth, ModalDepth);
        }

        [Test]
        public void CustomViewCanCompleteSynchronouslyInsideInitialize()
        {
            var view = new TestConversationView { OnInitialize = current => current.Advance() };
            var depth = ModalDepth;
            controller = new ConversationController(CreateDialogue("start", CreateLine("start", "Welcome.", "end"), CreateEnd("end")),
                context, null, viewFactory: () => view);

            Assert.IsTrue(view.SubscribedBeforeInitialize);
            Assert.IsTrue(controller.Presenter.IsComplete);
            Assert.IsTrue(controller.IsEnded);
            Assert.IsNull(controller.CurrentView);
            Assert.AreEqual(1, view.DisposeCount);
            Assert.AreEqual(depth, ModalDepth);
            controller.Dispose();
            Assert.AreEqual(1, view.DisposeCount);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CustomViewStartupFailureReleasesModalAndDisposesAnInitializedView(bool failInInitialize)
        {
            var view = new TestConversationView { OnInitialize = _ => throw new System.InvalidOperationException("initialize failed") };
            var dialogue = CreateDialogue("start", CreateLine("start", "Welcome.", "end"), CreateEnd("end"));
            var depth = ModalDepth;
            Assert.Throws<System.InvalidOperationException>(() => new ConversationController(dialogue, context, null, viewFactory: () =>
            {
                if (!failInInitialize) throw new System.InvalidOperationException("factory failed");
                return view;
            }));

            Assert.AreEqual(failInInitialize ? 1 : 0, view.DisposeCount);
            Assert.AreEqual(depth, ModalDepth);
            view.EmitCompleted();
            view.EmitDriven();
            Assert.AreEqual(failInInitialize ? 1 : 0, view.DisposeCount);
            Assert.AreEqual(depth, ModalDepth);
        }

        [Test]
        public void ThrowingCustomDisposeStillReleasesModalAndNotifiesCancellationOnce()
        {
            var view = new TestConversationView { OnDispose = () => throw new System.InvalidOperationException("dispose failed") };
            var depth = ModalDepth;
            controller = new ConversationController(CreateDialogue("start", CreateLine("start", "Welcome.", "end"), CreateEnd("end")),
                context, null, viewFactory: () => view);
            var cancelled = 0;
            controller.Cancelled += () => cancelled++;

            Assert.Throws<System.InvalidOperationException>(() => controller.Cancel());
            Assert.DoesNotThrow(() => controller.Dispose());
            view.EmitCompleted();
            view.EmitDriven();

            Assert.AreEqual(1, cancelled);
            Assert.AreEqual(1, view.DisposeCount);
            Assert.IsNull(controller.CurrentView);
            Assert.AreEqual(depth, ModalDepth);
        }

        [Test]
        public void DrivenInsideCustomInitializeCanOpenStoreAndReturnToANewViewWithTheSamePresenter()
        {
            var dialogue = CreateDialogue("start", CreateLine("start", "What do you need?", "open"),
                CreateOpenStoreNode("open", "reply"), CreateChoice("reply", "Leave", "end"), CreateEnd("end"));
            var first = new TestConversationView { OnInitialize = current => current.Advance() };
            var second = new TestConversationView();
            var factoryCalls = 0;
            var depth = ModalDepth;
            controller = new ConversationController(dialogue, context, panelSettings,
                viewFactory: () => ++factoryCalls == 1 ? first : second);

            Assert.AreEqual(1, factoryCalls);
            Assert.AreEqual(1, first.DisposeCount);
            Assert.IsNull(controller.CurrentView);
            Assert.IsNotNull(controller.CurrentStoreView, "Returning from the old Initialize must not tear down its replacement store.");
            Assert.IsFalse(controller.IsEnded);
            Assert.AreEqual(depth + 1, ModalDepth);

            controller.CurrentStoreView.Leave();
            Assert.AreEqual(2, factoryCalls);
            Assert.AreSame(second, controller.CurrentView);
            Assert.AreSame(first.Presenter, second.Presenter);
            Assert.AreSame(controller.Presenter, second.Presenter);
            Assert.IsTrue(second.Presenter.IsAtChoice);
            Assert.AreEqual("What do you need?", second.Presenter.LastLineText);
            first.EmitCompleted();
            first.EmitDriven();
            Assert.AreSame(second, controller.CurrentView, "Old view callbacks must not replace or dispose the new view.");
            Assert.AreEqual(0, second.DisposeCount);

            controller.Cancel();
            Assert.AreEqual(1, first.DisposeCount);
            Assert.AreEqual(1, second.DisposeCount);
            Assert.AreEqual(depth, ModalDepth);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DefaultCheckRollNotifiesStoreHandoffForBuiltInAndSynchronousCheckPresentation(bool customCheck)
        {
            var dialogue = CreateDialogue("start", CreateLine("start", "Show your credentials.", "check"), CreateCheck("check", "open"),
                CreateOpenStoreNode("open", "reply"), CreateChoice("reply", "Leave", "end"), CreateEnd("end"));
            controller = new ConversationController(dialogue, context, panelSettings,
                checkPresenter: customCheck ? new SynchronousCheckPresenter() : null);
            typeof(DialogueView).GetMethod("OnContinueClicked", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(controller.CurrentDialogueView, null);
            if (!customCheck)
            {
                Assert.IsTrue(controller.Presenter.IsAtCheck);
                typeof(DialogueView).GetMethod("OnRollClicked", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(controller.CurrentDialogueView, null);
            }

            Assert.IsNotNull(controller.CurrentStoreView, "Rolling into OpenStoreAction must drive the same store hand-off as Continue or a choice.");
            Assert.IsNull(controller.CurrentDialogueView);
            Assert.AreEqual("Show your credentials.", controller.Presenter.LastLineText);
            controller.CurrentStoreView.Leave();
            Assert.IsTrue(controller.Presenter.IsAtChoice);
            Assert.IsNotNull(controller.CurrentDialogueView);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AdapterObservesSynchronousCompletionAtEndOrInsideCustomInitialize(bool completeInInitialize)
        {
            var dialogue = completeInInitialize
                ? CreateDialogue("start", CreateLine("start", "Welcome.", "end"), CreateEnd("end"))
                : CreateDialogue("end", CreateEnd("end"));
            var adapter = CreateAdapter(dialogue);
            var view = new TestConversationView { OnInitialize = current => current.Advance() };
            adapter.ViewFactory = _ => view;
            var events = new List<string>();
            var performer = context.Party.ActiveCharacter;
            var depth = ModalDepth;
            adapter.ConversationStarted += actual => { Assert.AreEqual(performer, actual); events.Add("start"); };
            adapter.ConversationEnded += actual => { Assert.AreEqual(performer, actual); events.Add("end"); };
            var execution = adapter.BeginExecute(performer, result =>
            {
                Assert.AreEqual(InteractionExecutionResult.Succeeded, result);
                events.Add("finished");
            });

            CollectionAssert.AreEqual(new[] { "start", "end", "finished" }, events);
            Assert.IsTrue(adapter.IsAvailable(performer), "Synchronous End must not leave conversationOpen or the interaction execution waiting forever.");
            Assert.AreEqual(completeInInitialize ? 1 : 0, view.DisposeCount);
            Assert.AreEqual(depth, ModalDepth);
            execution.Cancel();
            CollectionAssert.AreEqual(new[] { "start", "end", "finished" }, events);
        }

        [Test]
        public void AdapterStartupDisableDoesNotOverrideCompletionInsideTheSameInitialize()
        {
            var adapter = CreateAdapter(CreateDialogue("start", CreateLine("start", "Welcome.", "end"), CreateEnd("end")));
            var view = new TestConversationView
            {
                OnInitialize = current =>
                {
                    adapter.enabled = false;
                    current.Advance();
                }
            };
            adapter.ViewFactory = _ => view;
            var results = new List<InteractionExecutionResult>();
            var ends = 0;
            var depth = ModalDepth;
            adapter.ConversationEnded += _ => ends++;

            var execution = adapter.BeginExecute(context.Party.ActiveCharacter, results.Add);
            execution.Cancel();
            view.EmitCompleted();
            view.EmitDriven();

            CollectionAssert.AreEqual(new[] { InteractionExecutionResult.Succeeded }, results,
                "A conversation already completed during construction keeps completion precedence over pending owner disable.");
            Assert.AreEqual(1, ends);
            Assert.AreEqual(1, view.DisposeCount);
            Assert.AreEqual(depth, ModalDepth);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AdapterDisabledDuringCustomInitializeFailsOwnerOnceWithoutRetainingViewOrModal(bool disableGameObject)
        {
            TalkInteractionAdapter adapter = CreateAdapter(CreateDialogue("start", CreateLine("start", "Welcome.", "end"), CreateEnd("end")));
            RuntimeEntityId performer = context.Party.ActiveCharacter;
            List<string> events = new List<string>();
            TestConversationView view = new TestConversationView
            {
                OnInitialize = current =>
                {
                    Assert.IsTrue(current.SubscribedBeforeInitialize);
                    Assert.IsFalse(current.Presenter.IsComplete, "Disable must interrupt a live Line, not synchronous completion.");
                    Assert.AreEqual(Id("start"), current.Presenter.CurrentNodeId);
                    events.Add("initialize");
                    if (disableGameObject)
                    {
                        adapterGo.SetActive(false);
                    }
                    else
                    {
                        adapter.enabled = false;
                    }
                    Assert.IsFalse(adapter.isActiveAndEnabled, "Exercise native disable delivery, not a reflected OnDisable call.");
                }
            };
            adapter.ConversationStarted += actual => { Assert.AreEqual(performer, actual); events.Add("start"); };
            adapter.ConversationEnded += actual => { Assert.AreEqual(performer, actual); events.Add("end"); };
            adapter.ViewFactory = actual => { Assert.AreEqual(performer, actual); events.Add("factory"); return view; };
            int depth = ModalDepth;
            var results = new List<InteractionExecutionResult>();
            Assert.IsTrue(adapter.isActiveAndEnabled, "The owner must be active before BeginExecute so Unity delivers its disable lifecycle.");
            IInteractionExecution execution = adapter.BeginExecute(performer, results.Add);
            try
            {
                // Owner disable must both clean presentation and finish its waiting interaction owner.
                Assert.AreEqual(1, view.DisposeCount, "Disabling the owner inside Initialize must dispose the startup view before BeginExecute returns.");
                CollectionAssert.AreEqual(new[] { "start", "factory", "initialize", "end" }, events);
                CollectionAssert.AreEqual(new[] { InteractionExecutionResult.Failed }, results,
                    "Owner disable must report Failed once; only an explicit execution.Cancel suppresses its callback.");
                Assert.AreEqual(depth, ModalDepth);
                Assert.IsNull(typeof(TalkInteractionAdapter).GetField("activeExecution", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(adapter));
                Assert.IsFalse((bool)typeof(TalkInteractionAdapter).GetField("conversationOpen", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(adapter));
                Assert.IsTrue(adapter.IsAvailable(performer), "Cancellation must release admission without changing disabled-owner availability policy.");

                execution.Cancel();
                view.EmitCompleted();
                view.EmitDriven();
                Assert.AreEqual(1, view.DisposeCount);
                CollectionAssert.AreEqual(new[] { "start", "factory", "initialize", "end" }, events);
                CollectionAssert.AreEqual(new[] { InteractionExecutionResult.Failed }, results);
                Assert.AreEqual(depth, ModalDepth);
            }
            finally
            {
                // Keep cleanup safe for either a retained startup execution or a terminal token.
                execution.Cancel();
            }
        }

        [TestCase("complete")]
        [TestCase("cancel")]
        [TestCase("disable")]
        [TestCase("destroy")]
        public void AdapterCustomViewReceivesActualPerformerAndEndsOnceAcrossItsLifecycle(string termination)
        {
            var adapter = CreateAdapter(CreateDialogue("start", CreateLine("start", "Welcome.", "end"), CreateEnd("end")));
            var performer = RuntimeEntityId.NewId(); // Deliberately not the active party member.
            var events = new List<string>();
            var view = new TestConversationView { OnInitialize = _ => events.Add("initialize") };
            adapter.ConversationStarted += actual => { Assert.AreEqual(performer, actual); events.Add("start"); };
            adapter.ConversationEnded += actual => { Assert.AreEqual(performer, actual); events.Add("end"); };
            adapter.ViewFactory = actual => { Assert.AreEqual(performer, actual); events.Add("factory"); return view; };
            var depth = ModalDepth;
            var results = new List<InteractionExecutionResult>();
            var execution = adapter.BeginExecute(performer, results.Add);
            CollectionAssert.AreEqual(new[] { "start", "factory", "initialize" }, events);
            Assert.IsFalse(adapter.IsAvailable(performer));

            switch (termination)
            {
                case "complete": view.Advance(); break;
                case "cancel": execution.Cancel(); break;
                case "disable": adapterGo.SetActive(false); break;
                case "destroy": Object.DestroyImmediate(adapterGo); break;
            }
            execution.Cancel();
            view.EmitCompleted();
            view.EmitDriven();

            CollectionAssert.AreEqual(new[] { "start", "factory", "initialize", "end" }, events);
            // Public execution.Cancel is controller-requested cancellation and deliberately suppresses
            // onFinished. Owner disable/destruction instead owes the waiting controller one Failed result.
            var expected = termination == "cancel" ? new InteractionExecutionResult[0]
                : new[] { termination == "complete" ? InteractionExecutionResult.Succeeded : InteractionExecutionResult.Failed };
            CollectionAssert.AreEqual(expected, results);
            Assert.AreEqual(1, view.DisposeCount);
            Assert.AreEqual(depth, ModalDepth);
            if (adapter != null) Assert.IsTrue(adapter.IsAvailable(performer));
        }

        [Test]
        public void AdapterStartupFailureEndsLifecycleAndReleasesCustomViewAndModal()
        {
            var adapter = CreateAdapter(CreateDialogue("start", CreateLine("start", "Welcome.", "end"), CreateEnd("end")));
            var view = new TestConversationView { OnInitialize = _ => throw new System.InvalidOperationException("initialize failed") };
            adapter.ViewFactory = _ => view;
            var events = new List<string>();
            adapter.ConversationStarted += _ => events.Add("start");
            adapter.ConversationEnded += _ => events.Add("end");
            var depth = ModalDepth;

            Assert.Throws<System.InvalidOperationException>(() => adapter.BeginExecute(context.Party.ActiveCharacter, _ => Assert.Fail("A failed start returns no execution result.")));

            CollectionAssert.AreEqual(new[] { "start", "end" }, events);
            Assert.AreEqual(1, view.DisposeCount);
            Assert.AreEqual(depth, ModalDepth);
            Assert.IsTrue(adapter.IsAvailable(context.Party.ActiveCharacter));
        }
    }
}

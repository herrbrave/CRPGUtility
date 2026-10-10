using System.Collections.Generic;
using System.Linq;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Dialogue;
using CRPG.Editor.Validation;
using CRPG.Gameplay;
using CRPG.Gameplay.Interactions;
using CRPG.Gameplay.Interactions.Adapters;
using CRPG.Gameplay.Scenes;
using CRPG.Inventory;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace CRPG.Tests.EditMode
{
    /// <summary>Slice 2 of NPC services (<c>Docs/Plans/Toolkit-NpcServices.md</c>): an inn is a price plus a rest, built from a branch on what the party carries, a spend action and the long-rest action.</summary>
    public sealed class InnDialogueTests
    {
        private readonly List<Object> toDestroy = new();
        private GameSession session;
        private ItemDefinition gold;
        private DialogueContext context;
        private int longRests;

        [SetUp]
        public void SetUp()
        {
            longRests = 0;
            var rules = new RulesDatabase();
            gold = TestDefinitionFactory.CreateItemDefinition("item-gold", "Gold Piece", baseValue: 0f, maxStackSize: 999);
            toDestroy.Add(gold);
            rules.Register(gold);
            session = new GameSession(rules, new DiceService(new SeededRandomSource(1)));
            var member = new CharacterState(RuntimeEntityId.NewId());
            session.Characters.Register(member);
            session.Party.AddMember(member.Id);
            session.EventBus.Subscribe(GameEvent.LongRest, _ => longRests++);
            context = new DialogueContext(member.Id, session.Party, session.Characters, rules, session.Timeline, session.Inventory, session.DiceService,
                restService: new RestService(session.Characters, rules, session.EventBus));
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in toDestroy) if (o != null) Object.DestroyImmediate(o);
            toDestroy.Clear();
            session.Dispose();
        }

        private void Give(int amount) => session.Inventory.AddItem(new ItemInstance(RuntimeEntityId.NewId(), gold.Id, amount));

        private static SerializableGuid Id(string seed) => SerializableGuid.FromString(seed);

        private BranchNode ItemBranch(string id, ItemDefinition item, int quantity, string whenTrue, string whenFalse)
        {
            var node = new BranchNode();
            TestDefinitionFactory.SetPrivateField(node, "nodeId", Id(id));
            TestDefinitionFactory.SetPrivateField(node, "source", BranchSource.PartyItemCount);
            TestDefinitionFactory.SetPrivateField(node, "item", item);
            TestDefinitionFactory.SetPrivateField(node, "quantity", quantity);
            TestDefinitionFactory.SetPrivateField(node, "trueNodeId", Id(whenTrue));
            TestDefinitionFactory.SetPrivateField(node, "falseNodeId", Id(whenFalse));
            return node;
        }

        private static ActionNode Act(string id, DialogueAction action, string next)
        {
            var node = new ActionNode();
            TestDefinitionFactory.SetPrivateField(node, "nodeId", Id(id));
            TestDefinitionFactory.SetPrivateField(node, "action", action);
            TestDefinitionFactory.SetPrivateField(node, "nextNodeId", Id(next));
            return node;
        }

        private static SpendItemAction Spend(ItemDefinition item, int quantity)
        {
            var action = new SpendItemAction();
            TestDefinitionFactory.SetPrivateField(action, "item", item);
            TestDefinitionFactory.SetPrivateField(action, "quantity", quantity);
            return action;
        }

        private DialogueDefinition Dialogue(string start, params DialogueNode[] nodes)
        {
            var dialogue = ScriptableObject.CreateInstance<DialogueDefinition>();
            toDestroy.Add(dialogue);
            TestDefinitionFactory.SetPrivateField(dialogue, "id", "dlg-inn");
            TestDefinitionFactory.SetPrivateField(dialogue, "startNodeId", Id(start));
            TestDefinitionFactory.SetPrivateField(dialogue, "nodes", new List<DialogueNode>(nodes));
            return dialogue;
        }

        // "Rent a bed (5 gold)": if you have it, pay and rest; otherwise the innkeeper says so.
        private DialogueDefinition Inn(int price) => Dialogue("check",
            ItemBranch("check", gold, price, "pay", "poor"),
            Act("pay", Spend(gold, price), "rest"),
            Act("rest", new RequestLongRestAction(), "slept"),
            DialogueTestFactory.CreateLine("slept", "Hild", "Sleep well.", "end"),
            DialogueTestFactory.CreateLine("poor", "Hild", "That is five gold, friend.", "end"),
            DialogueTestFactory.CreateEnd("end"));

        // ---------------------------------------------------------------- the branch

        [Test]
        public void Branch_PartyItemCount_IsTrueFromExactlyTheThreshold_AcrossStacks()
        {
            var branch = ItemBranch("b", gold, 5, "t", "f");

            Assert.IsFalse(branch.Evaluate(context), "no gold");
            Give(3);
            Assert.IsFalse(branch.Evaluate(context), "three is not five");
            Give(2);
            Assert.IsTrue(branch.Evaluate(context), "3 + 2 across two stacks is exactly five");
        }

        [Test]
        public void Branch_PartyItemCount_WithNoItemIsAlwaysFalse_AndTheDefaultSourceIsStillTheTimeline()
        {
            Give(100);
            Assert.IsFalse(ItemBranch("b", null, 1, "t", "f").Evaluate(context));

            var timelineBranch = DialogueTestFactory.CreateBranch("tb", "quest-done", TimelineConditionType.HasOccurred, 0, "t", "f");
            Assert.AreEqual(BranchSource.Timeline, timelineBranch.Source);
            Assert.IsFalse(timelineBranch.Evaluate(context));
            session.Timeline.Record("quest-done", TimelineValue.OfNumber(1), 1, context.NpcId);
            Assert.IsTrue(timelineBranch.Evaluate(context), "existing graphs keep asking the Timeline");
        }

        // ---------------------------------------------------------------- the inn, run through the real runner

        [Test]
        public void TheInn_TakesThePriceAndRests_WhenThePartyCanPay()
        {
            Give(8);
            var runner = new DialogueRunner(Inn(5), context);

            Assert.IsInstanceOf<LineNode>(runner.CurrentNode);
            Assert.AreEqual("Sleep well.", ((LineNode)runner.CurrentNode).Text);
            Assert.AreEqual(3, session.Inventory.CountShared(gold.Id), "paid five of eight");
            Assert.AreEqual(1, longRests, "and rested: the LongRest event is the hook a game reacts to");
        }

        [Test]
        public void TheInn_TurnsAnUnaffordablePartyAway_WithoutTakingCoinOrResting()
        {
            Give(4);
            var runner = new DialogueRunner(Inn(5), context);

            Assert.AreEqual("That is five gold, friend.", ((LineNode)runner.CurrentNode).Text);
            Assert.AreEqual(4, session.Inventory.CountShared(gold.Id), "nothing taken");
            Assert.AreEqual(0, longRests);
        }

        [Test]
        public void SpendItem_IsAllOrNothing_AndSafeWithNoItemOrNoInventory()
        {
            Give(4);
            Spend(gold, 5).Execute(context);
            Assert.AreEqual(4, session.Inventory.CountShared(gold.Id), "short: untouched");

            Spend(null, 5).Execute(context);   // no throw
            Spend(gold, 3).Execute(context);
            Assert.AreEqual(1, session.Inventory.CountShared(gold.Id));
        }

        // ---------------------------------------------------------------- wiring + validator

        private sealed class Content : ILocationContent
        {
            public readonly List<NpcSpawnPoint> Points = new();
            public IReadOnlyList<NpcSpawnPoint> SpawnPoints => Points;
            public IReadOnlyList<StoreContentPoint> Stores => new List<StoreContentPoint>();
            public IReadOnlyList<LootContentPoint> LootContainers => new List<LootContentPoint>();
            public Rect? CameraBoundsXZ => null;
        }

        private TalkInteractionAdapter TalkableInn(NpcDialogueOptions options)
        {
            var spawner = new WorldSpawnService(session);
            var definition = TestDefinitionFactory.CreateNpcDefinition("npc-hild", "Hild");
            var visual = new GameObject("visual");
            toDestroy.Add(visual);
            TestDefinitionFactory.SetPrivateField(definition, "visualPrefab", visual);
            toDestroy.Add(definition);
            var anchor = new GameObject("anchor");
            toDestroy.Add(anchor);
            var agent = spawner.SpawnNpc("hild", definition, Vector3.zero, Quaternion.identity);
            toDestroy.Add(agent.gameObject);
            var content = new Content();
            content.Points.Add(new NpcSpawnPoint { SpawnId = "hild", Definition = definition, Anchor = anchor.transform, Dialogue = Inn(5) });
            NpcDialogues.AttachAll(content, spawner, session, options);
            return agent.GetComponent<TalkInteractionAdapter>();
        }

        [Test]
        public void ALongRestWorksOutOfTheBox_WithADefaultRestService()
        {
            var panel = ScriptableObject.CreateInstance<PanelSettings>();
            toDestroy.Add(panel);

            var adapter = TalkableInn(new NpcDialogueOptions { PanelSettings = panel });

            Assert.IsNotNull(adapter.RestService, "no factory given: the generic service is used");
        }

        [Test]
        public void AGamesOwnRestServiceFactory_WinsOverTheDefault()
        {
            var panel = ScriptableObject.CreateInstance<PanelSettings>();
            toDestroy.Add(panel);
            var mine = new RestService(session.Characters, session.RulesDatabase, session.EventBus);

            var adapter = TalkableInn(new NpcDialogueOptions { PanelSettings = panel, RestServiceFactory = _ => mine });

            Assert.AreSame(mine, adapter.RestService);
        }

        [Test]
        public void DialogueValidator_SpendAndBranchActionsWithNoItem_AreErrors()
        {
            var noItem = Dialogue("check",
                ItemBranch("check", null, 5, "pay", "end"),
                Act("pay", Spend(null, 5), "end"),
                DialogueTestFactory.CreateEnd("end"));

            var errors = DialogueValidator.Validate(noItem).Where(r => r.Severity == ValidationSeverity.Error).ToList();

            Assert.IsTrue(errors.Any(r => r.Message.Contains("spend-item")), "the spend action");
            Assert.IsTrue(errors.Any(r => r.Message.Contains("branch asks about an item")), "the branch");
            Assert.IsEmpty(DialogueValidator.Validate(Inn(5)).Where(r => r.Severity == ValidationSeverity.Error).ToList(), "a proper inn graph is clean");
        }
    }
}

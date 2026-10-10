using System.Collections.Generic;
using System.Linq;
using CRPG.Core;
using CRPG.Dialogue;
using CRPG.Editor.Validation;
using CRPG.Gameplay;
using CRPG.Gameplay.Interactions;
using CRPG.Inventory;
using CRPG.Quests;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace CRPG.Tests.EditMode
{
    /// <summary>Slice 3 of NPC services (<c>Docs/Plans/Toolkit-NpcServices.md</c>): the quest tracker (start from an authored key, keep progress current, leave Timeline traces) and the dialogue option that hands it to NPCs.</summary>
    public sealed class QuestTrackerTests
    {
        private readonly List<Object> toDestroy = new();
        private RulesDatabase rules;
        private GameSession session;
        private QuestTracker tracker;
        private QuestDefinition sacks;
        private ItemDefinition gold;

        [SetUp]
        public void SetUp()
        {
            rules = new RulesDatabase();
            gold = TestDefinitionFactory.CreateItemDefinition("item-gold", "Gold Piece", maxStackSize: 999);
            toDestroy.Add(gold);
            rules.Register(gold);

            // Barrow's End's working pattern: the objective counts an authored key SINCE the quest started, so an earlier occurrence cannot complete it.
            sacks = TestDefinitionFactory.CreateQuestDefinition("quest-sacks", "The Sacks",
                TestDefinitionFactory.CreateQuestObjective("tell-hild", "Tell Hild", QuestObjectiveType.Custom, "hild-told", progressMode: QuestObjectiveProgressMode.CountSinceActivation));
            TestDefinitionFactory.SetPrivateField(sacks, "rewards", new List<QuestItemReward> { TestDefinitionFactory.CreateQuestReward(gold, 15) });
            toDestroy.Add(sacks);
            rules.Register(sacks);

            session = new GameSession(rules, new DiceService(new SeededRandomSource(1)));
            tracker = new QuestTracker(session);
        }

        [TearDown]
        public void TearDown()
        {
            tracker.Dispose();
            foreach (var o in toDestroy) if (o != null) Object.DestroyImmediate(o);
            toDestroy.Clear();
            session.Dispose();
        }

        private void Hear(string key) => session.Timeline.Record(key, TimelineValue.Flag);

        [Test]
        public void Start_ResolvesTheKeyAndStartsTheQuest_Once()
        {
            var state = tracker.Start("quest-sacks");

            Assert.IsNotNull(state);
            Assert.AreEqual(QuestStatus.Active, state.Status);
            Assert.AreSame(state, tracker.Start("quest-sacks"), "starting it again changes nothing");
            Assert.AreEqual(1, session.Quests.Quests.Count);
        }

        [Test]
        public void Start_RefreshesAtOnce_SoAQuestWhoseConditionAlreadyHoldsCompletesWithoutAnyoneRefreshingLater()
        {
            var history = TestDefinitionFactory.CreateQuestDefinition("quest-history", "Old News",
                TestDefinitionFactory.CreateQuestObjective("done", "Already done", QuestObjectiveType.Custom, "already-happened"));   // HistoricalCount: counts events from before the start
            toDestroy.Add(history);
            rules.Register(history);
            Hear("already-happened");

            var state = tracker.Start("quest-history");

            Assert.AreEqual(QuestStatus.Completed, state.Status, "the start refreshed: nothing else had to");
        }

        [Test]
        public void Start_AnUnknownKey_WarnsAndStartsNothing()
        {
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("no quest 'quest-nope'"));
            Assert.IsNull(tracker.Start("quest-nope"));
            Assert.IsNull(tracker.Start(null), "an empty key too");
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("no quest ''"));
            Assert.AreEqual(0, session.Quests.Quests.Count);
        }

        [Test]
        public void RefreshAll_AdvancesAnObjective_WhenItsKeyIsRecordedAfterTheStart_AndCompletesTheQuestWithItsReward()
        {
            tracker.Start("quest-sacks");
            Hear("hild-told");

            tracker.RefreshAll();

            session.Quests.TryGetQuest(sacks.Id, out var state);
            Assert.AreEqual(QuestStatus.Completed, state.Status);
            Assert.AreEqual(15, session.Inventory.CountShared(gold.Id), "the reward, granted by completion");
        }

        [Test]
        public void AnEventFromBeforeTheStart_DoesNotCompleteACountSinceActivationObjective()
        {
            Hear("hild-told");   // the party talked to Hild BEFORE taking the quest
            tracker.Start("quest-sacks");
            tracker.RefreshAll();

            session.Quests.TryGetQuest(sacks.Id, out var state);
            Assert.AreEqual(QuestStatus.Active, state.Status);
            Assert.AreEqual(0, session.Inventory.CountShared(gold.Id));
        }

        [Test]
        public void TheRewardIsGrantedOnce_HoweverOftenItIsRefreshed()
        {
            tracker.Start("quest-sacks");
            Hear("hild-told");
            tracker.RefreshAll();
            tracker.RefreshAll();
            Hear("hild-told");
            tracker.RefreshAll();

            Assert.AreEqual(15, session.Inventory.CountShared(gold.Id));
        }

        [Test]
        public void Tick_RefreshesOnlyWhenTheTimelineHasGrown()
        {
            tracker.Start("quest-sacks");
            tracker.Tick();   // nothing changed since the start's refresh
            session.Quests.TryGetQuest(sacks.Id, out var state);
            Assert.AreEqual(QuestStatus.Active, state.Status);

            Hear("hild-told");
            Assert.AreEqual(QuestStatus.Active, state.Status, "the timeline has no change event: nothing happens by itself");
            tracker.Tick();

            Assert.AreEqual(QuestStatus.Completed, state.Status, "the tick noticed the new event");
        }

        [Test]
        public void TheTrackerRecordsWhereAQuestStands_SoADialogueCanBranchOnIt()
        {
            tracker.Start("quest-sacks");
            Assert.IsTrue(session.Timeline.HasOccurred(QuestTracker.StartedKey("quest-sacks")));
            Assert.IsFalse(session.Timeline.HasOccurred(QuestTracker.CompletedKey("quest-sacks")));

            Hear("hild-told");
            tracker.RefreshAll();
            Assert.IsTrue(session.Timeline.HasOccurred(QuestTracker.CompletedKey("quest-sacks")));
        }

        [Test]
        public void ADisposedTrackerDoesNothing_AndStopsRecording()
        {
            tracker.Dispose();

            Assert.IsNull(tracker.Start("quest-sacks"));
            tracker.RefreshAll();
            tracker.Tick();

            // started by someone else: the disposed tracker no longer leaves its trace
            new QuestService(session.Quests, session.Timeline, session.EventBus, session.Inventory).StartQuest(sacks);
            Assert.IsFalse(session.Timeline.HasOccurred(QuestTracker.StartedKey("quest-sacks")));
        }

        [Test]
        public void ARefreshSkipsAnActiveQuestTheRulesetCannotResolve_WithoutThrowing()
        {
            session.Quests.Add(new QuestState(new DefinitionId("quest-ghost")) { Status = QuestStatus.Active });
            Assert.DoesNotThrow(tracker.RefreshAll);
        }

        // ---------------------------------------------------------------- validators

        [Test]
        public void QuestValidator_FlagsTheMistakesThatCrashOrStrandAQuest()
        {
            var noItemReward = TestDefinitionFactory.CreateQuestDefinition("quest-bad", "Bad",
                TestDefinitionFactory.CreateQuestObjective("a", "A", QuestObjectiveType.Custom, "", progressMode: QuestObjectiveProgressMode.CountSinceActivation),
                TestDefinitionFactory.CreateQuestObjective("a", "A again", QuestObjectiveType.Custom, "k"));
            TestDefinitionFactory.SetPrivateField(noItemReward, "rewards", new List<QuestItemReward> { TestDefinitionFactory.CreateQuestReward(null, 3) });
            toDestroy.Add(noItemReward);

            var findings = QuestValidator.Validate(noItemReward);

            Assert.IsTrue(findings.Any(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("would throw")), "a reward with no item");
            Assert.IsTrue(findings.Any(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("no tracking key")));
            Assert.IsTrue(findings.Any(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("used twice")));
            Assert.IsTrue(findings.Any(r => r.Severity == ValidationSeverity.Warning && r.Message.Contains("HISTORICAL")));
        }

        [Test]
        public void QuestValidator_AProperQuestIsClean_AndOneWithNoObjectivesIsAnError()
        {
            Assert.IsEmpty(QuestValidator.Validate(sacks).ToList());

            var empty = TestDefinitionFactory.CreateQuestDefinition("quest-empty", "Empty");
            toDestroy.Add(empty);
            Assert.IsTrue(QuestValidator.Validate(empty).Any(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("no objectives")));
        }

        [Test]
        public void DialogueValidator_AStartQuestActionWithNoKey_IsAnError()
        {
            var start = new ActionNode();
            TestDefinitionFactory.SetPrivateField(start, "nodeId", SerializableGuid.FromString("start"));
            TestDefinitionFactory.SetPrivateField(start, "action", new StartQuestAction());
            TestDefinitionFactory.SetPrivateField(start, "nextNodeId", SerializableGuid.FromString("end"));
            var dialogue = ScriptableObject.CreateInstance<DialogueDefinition>();
            toDestroy.Add(dialogue);
            TestDefinitionFactory.SetPrivateField(dialogue, "id", "dlg-q");
            TestDefinitionFactory.SetPrivateField(dialogue, "startNodeId", SerializableGuid.FromString("start"));
            TestDefinitionFactory.SetPrivateField(dialogue, "nodes", new List<DialogueNode> { start, DialogueTestFactory.CreateEnd("end") });

            Assert.IsTrue(DialogueValidator.Validate(dialogue).Any(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("no quest key")));
        }

        // ---------------------------------------------------------------- the dialogue option

        [Test]
        public void TheOptionsHandTheTrackerToTheAdapters_ButAGamesOwnDelegateWins_AndAForeignSessionsTrackerIsIgnored()
        {
            var panel = ScriptableObject.CreateInstance<PanelSettings>();
            toDestroy.Add(panel);
            var options = new NpcDialogueOptions { PanelSettings = panel, Quests = tracker };
            var adapter = new GameObject("npc").AddComponent<CRPG.Gameplay.Interactions.Adapters.TalkInteractionAdapter>();
            toDestroy.Add(adapter.gameObject);

            NpcDialogues.Wire(adapter, session, options);
            adapter.RequestStartQuest("quest-sacks");
            Assert.IsTrue(session.Quests.TryGetQuest(sacks.Id, out _), "the dialogue's Start Quest action reached the tracker");

            string mine = null;
            options.RequestStartQuest = key => mine = key;
            NpcDialogues.Wire(adapter, session, options);
            adapter.RequestStartQuest("quest-other");
            Assert.AreEqual("quest-other", mine, "the game's own delegate wins");

            var other = new GameSession(rules, new DiceService(new SeededRandomSource(3)));
            try
            {
                var foreign = new NpcDialogueOptions { PanelSettings = panel, Quests = new QuestTracker(other) };
                NpcDialogues.Wire(adapter, session, foreign);
                Assert.IsNull(adapter.RequestStartQuest, "a tracker bound to another session must not be used");
            }
            finally
            {
                other.Dispose();
            }
        }
    }
}

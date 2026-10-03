using System;
using System.Collections.Generic;
using System.Linq;
using BarrowsEnd.Editor;
using BarrowsEnd.Gate;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Dialogue;
using CRPG.Editor.Validation;
using CRPG.Gameplay;
using CRPG.Gameplay.Scenes;
using CRPG.Inventory;
using CRPG.Party;
using CRPG.Quests;
using CRPG.Rules;
using CRPG.Rules.SRD5;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BarrowsEnd.Tests
{
    /// <summary>Task 6.1 (task breakdown items 7-9): the real Act 1 cast's authored content -- every dialogue
    /// asset validated (no dangling refs/unreachable nodes/inescapable cycles, per the master plan's own source
    /// fact 3), Mayor Thaddeus's ready-to-descend gate proven both ways by driving the REAL authored dialogue
    /// through a real DialogueRunner (not a hand-built test fixture), and each vendor's real stock resolving.</summary>
    public class Act1ContentTests
    {
        private static readonly string[] AllDialogueIds =
        {
            "dlg-mayor-thaddeus", "dlg-companion-garrick", "dlg-companion-elara", "dlg-companion-silas",
            "dlg-elida", "dlg-grumble", "dlg-store-barnaby", "dlg-store-fendrick", "dlg-store-marjorie",
            "dlg-store-lyra", "dlg-store-silvan", "dlg-flavor-kael", "dlg-flavor-jenkins",
        };

        private GameSession session;
        private GameObject hostObject;

        [OneTimeSetUp]
        public void Author()
        {
            BarrowsEndCatalogAuthor.Author();
            BarrowsEndGearAuthor.Author();
            BarrowsEndTownAuthor.Author(overwrite: true);
        }

        [SetUp]
        public void SetUp()
        {
            var provider = AssetDatabase.LoadAssetAtPath<BarrowsEndRulesetProvider>(BarrowsEndGearAuthor.RulesetPath);
            Assert.IsNotNull(provider);
            hostObject = new GameObject("Act1ContentTestsHost");
            session = hostObject.AddComponent<GameSessionHost>().StartSession(provider, new DiceService(new SeededRandomSource(11)), System.Array.Empty<System.Type>());
        }

        [TearDown]
        public void TearDown()
        {
            session?.Dispose();
            if (hostObject != null) UnityEngine.Object.DestroyImmediate(hostObject);
        }

        private static T Load<T>(string path) where T : UnityEngine.Object => AssetDatabase.LoadAssetAtPath<T>(path);

        [Test]
        public void EveryAct1DialogueValidatesCleanly()
        {
            foreach (var id in AllDialogueIds)
            {
                var dialogue = Load<DialogueDefinition>($"{BarrowsEndGearAuthor.NpcDir}/{id}.asset");
                Assert.IsNotNull(dialogue, id);
                var results = DialogueValidator.Validate(dialogue);
                var errors = results.Where(r => r.Severity == ValidationSeverity.Error).ToList();
                Assert.IsEmpty(errors, $"{id}: " + string.Join("; ", errors.Select(e => $"{e.Field}: {e.Message}")));
            }
        }

        private DialogueContext CreateMayorContext(RuntimeEntityId npc, PartyState party, ICharacterRegistry characters, ITimeline timeline, Action<string> requestStartQuest = null) =>
            new(npc, party, characters, session.RulesDatabase, timeline, session.Inventory, session.DiceService,
                requestStartQuest: requestStartQuest);

        [Test]
        public void MayorThaddeusRefusesTheQuestUntilAllThreeCompanionsAreRecruited()
        {
            var fighter = new CharacterState(RuntimeEntityId.NewId());
            session.Characters.Register(fighter);
            var party = new PartyState(new[] { fighter.Id });

            var dialogue = Load<DialogueDefinition>($"{BarrowsEndGearAuthor.NpcDir}/dlg-mayor-thaddeus.asset");
            string startedQuest = null;
            var context = CreateMayorContext(fighter.Id, party, session.Characters, session.Timeline, key => startedQuest = key);
            var runner = new DialogueRunner(dialogue, context);
            runner.Advance(); // briefing (LineNode) -> choice

            var readyOptionIndex = FindOptionIndex(runner, "ready to descend");
            runner.ChooseOption(readyOptionIndex); // -> readyCheck (auto) -> notReady (LineNode)
            runner.Advance(); // notReady -> end

            Assert.IsNull(startedQuest, "the quest must not start before 3 companions are recruited");
            Assert.IsTrue(runner.IsComplete);
        }

        [Test]
        public void MayorThaddeusStartsTheQuestOnceAllThreeCompanionsAreRecruited()
        {
            var fighter = new CharacterState(RuntimeEntityId.NewId());
            session.Characters.Register(fighter);
            var party = new PartyState(new[] { fighter.Id });
            session.Timeline.Record("companion-recruited", TimelineValue.Flag);
            session.Timeline.Record("companion-recruited", TimelineValue.Flag);
            session.Timeline.Record("companion-recruited", TimelineValue.Flag);

            var dialogue = Load<DialogueDefinition>($"{BarrowsEndGearAuthor.NpcDir}/dlg-mayor-thaddeus.asset");
            string startedQuest = null;
            var context = CreateMayorContext(fighter.Id, party, session.Characters, session.Timeline, key => startedQuest = key);
            var runner = new DialogueRunner(dialogue, context);
            runner.Advance(); // briefing (LineNode) -> choice

            var readyOptionIndex = FindOptionIndex(runner, "ready to descend");
            runner.ChooseOption(readyOptionIndex); // -> readyCheck (auto) -> startQuest (auto) -> questGiven (LineNode)
            runner.Advance(); // questGiven -> end

            Assert.AreEqual(BarrowsEndGearAuthor.GatheringStormQuestId, startedQuest);
            Assert.IsTrue(runner.IsComplete);
        }

        [Test]
        public void MayorThaddeussExpositionOptionLoopsBackToTheSameChoiceWithoutStartingTheQuest()
        {
            var fighter = new CharacterState(RuntimeEntityId.NewId());
            session.Characters.Register(fighter);
            var party = new PartyState(new[] { fighter.Id });

            var dialogue = Load<DialogueDefinition>($"{BarrowsEndGearAuthor.NpcDir}/dlg-mayor-thaddeus.asset");
            string startedQuest = null;
            var context = CreateMayorContext(fighter.Id, party, session.Characters, session.Timeline, key => startedQuest = key);
            var runner = new DialogueRunner(dialogue, context);
            runner.Advance(); // briefing (LineNode) -> choice

            var expositionIndex = FindOptionIndex(runner, "down there");
            runner.ChooseOption(expositionIndex); // -> exposition (LineNode)
            runner.Advance(); // exposition -> choice (looped back)

            Assert.IsNull(startedQuest);
            Assert.IsInstanceOf<ChoiceNode>(runner.CurrentNode, "looped back to the same choice");
        }

        private static int FindOptionIndex(DialogueRunner runner, string textContains)
        {
            var choice = runner.CurrentNode as ChoiceNode;
            Assert.IsNotNull(choice, "expected the runner to be at a ChoiceNode");
            var options = choice.Options;
            for (var i = 0; i < options.Count; i++)
                if (options[i].Text.Contains(textContains)) return i;
            Assert.Fail($"no choice option contains '{textContains}'");
            return -1;
        }

        [Test]
        public void TheGatheringStormQuestsObjectiveTracksThePalisadeTriggersOwnVictoryKey()
        {
            Assert.IsTrue(session.RulesDatabase.TryResolve<QuestDefinition>(new DefinitionId(BarrowsEndGearAuthor.GatheringStormQuestId), out var quest));
            Assert.AreEqual(1, quest.Objectives.Count);
            Assert.AreEqual(QuestObjectiveType.Kill, quest.Objectives[0].Type);
            Assert.AreEqual(EncounterTrigger.PalisadeWonKey, quest.Objectives[0].TrackingKey);
            Assert.AreEqual(1, quest.Objectives[0].TargetCount);
        }

        [Test]
        public void RecordingThePalisadeVictoryKeyCompletesTheQuestOnRefresh()
        {
            Assert.IsTrue(session.RulesDatabase.TryResolve<QuestDefinition>(new DefinitionId(BarrowsEndGearAuthor.GatheringStormQuestId), out var quest));
            var log = new QuestLog();
            var questService = new QuestService(log, session.Timeline);
            questService.StartQuest(quest);

            session.Timeline.Record(EncounterTrigger.PalisadeWonKey, TimelineValue.Flag);
            questService.RefreshProgress(quest);

            Assert.IsTrue(log.TryGetQuest(quest.Id, out var state));
            Assert.AreEqual(QuestStatus.Completed, state.Status);
        }

        private static UnityEngine.SceneManagement.Scene OpenTown() =>
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(BarrowsEndTownAuthor.Path, UnityEditor.SceneManagement.OpenSceneMode.Additive);

        [TestCase("store-barnaby", "item-short-sword")]
        [TestCase("store-fendrick", "item-healing-potion")]
        [TestCase("store-marjorie", "item-tunic")]
        [TestCase("store-lyra", "item-scroll-identify")]
        [TestCase("store-silvan", "item-shortbow")]
        public void EachVendorsStoreStocksItsExpectedItem(string storeId, string expectedItemId)
        {
            var scene = OpenTown();
            try
            {
                var content = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<LocationContent>(true)).Single();
                var so = new SerializedObject(content);
                var storesProp = so.FindProperty("stores");
                var found = false;
                for (var i = 0; i < storesProp.arraySize; i++)
                {
                    var element = storesProp.GetArrayElementAtIndex(i);
                    if (element.FindPropertyRelative("StoreId").stringValue != storeId) continue;
                    found = true;
                    var listings = element.FindPropertyRelative("InitialListings");
                    var hasItem = false;
                    for (var j = 0; j < listings.arraySize; j++)
                    {
                        var item = listings.GetArrayElementAtIndex(j).FindPropertyRelative("Item").objectReferenceValue as ItemDefinition;
                        if (item != null && item.Id.Value == expectedItemId) hasItem = true;
                    }
                    Assert.IsTrue(hasItem, $"{storeId} does not stock {expectedItemId}");
                }
                Assert.IsTrue(found, $"{storeId} is not registered in the Town's LocationContent");
            }
            finally
            {
                UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}

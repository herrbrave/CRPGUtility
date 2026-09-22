using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BarrowsEnd.Editor;
using BarrowsEnd.Gate;
using BarrowsEnd.Shell;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Gameplay;
using CRPG.Presentation.Creator;
using CRPG.Rules.SRD5;
using CRPG.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace BarrowsEnd.Tests
{
    /// <summary>
    /// Task 4.3, EditMode: the pure pieces of the creator-in-the-game flow against the real ruleset and content: <see cref="ProtagonistFactory"/> (a built character
    /// with real hit points), <see cref="ShellPanel.Summarize"/> (the selected member's summary line that Gate D reads) and the authored Core scene's wiring.
    /// </summary>
    public class ShellStartFlowContentTests
    {
        private static readonly Dictionary<string, int> ExpectedHitPoints = new()
        {
            // hit-die maximum + Constitution modifier (14 from the standard array + Human's +1 = 15 = +2): d12+2, d8+2, d10+2, d8+2, d6+2
            ["class-barbarian"] = 14, ["class-cleric"] = 10, ["class-fighter"] = 12, ["class-rogue"] = 10, ["class-wizard"] = 8,
        };

        private GameSession session;
        private GameObject hostObject;

        [SetUp]
        public void SetUp()
        {
            var provider = AssetDatabase.LoadAssetAtPath<BarrowsEndRulesetProvider>(BarrowsEndGearAuthor.RulesetPath);
            Assert.IsNotNull(provider, "the ruleset asset is authored");
            hostObject = new GameObject("ShellStartFlowContentTestsHost");
            session = hostObject.AddComponent<GameSessionHost>().StartSession(provider, new DiceService(new SeededRandomSource(7)), Array.Empty<Type>());
        }

        [TearDown]
        public void TearDown()
        {
            session?.Dispose();
            if (hostObject != null) UnityEngine.Object.DestroyImmediate(hostObject);
        }

        private CharacterCreatorModel NewModel() => new(session.RulesDatabase,
            CreatorContentLoader.Build(File.ReadAllText(BarrowsEndCreatorAuthor.PicksPath), File.ReadAllText(BarrowsEndCreatorAuthor.ClassTablePath)), Debug.LogWarning);

        private static readonly RuntimeEntityId ProtagonistId = RuntimeEntityId.FromString("ba000000000000000000000000000001");

        // ---- ProtagonistFactory ----

        [Test]
        public void EveryClassBuildsAProtagonistWithHitDiePlusConstitutionModifierHitPointsAndTheChosenLook()
        {
            var model = NewModel();
            model.RowFor("hair-color").SelectIndex(2);
            model.RowFor("layer-hair").SelectIndex(4);
            model.RowFor("layer-body").SelectIndex(3);

            for (var c = 0; c < model.ClassRow.Count; c++)
            {
                model.ClassRow.SelectIndex(c);
                var classId = model.ClassRow.Selected.Id.Value;
                var result = model.Build();

                var character = ProtagonistFactory.Build(session.RulesDatabase, result, ProtagonistId);

                Assert.AreEqual(ProtagonistId, character.Id);
                Assert.AreEqual(1, character.Level);
                Assert.IsTrue(character.TryGetResource(GateSessionServices.HitPoints, out var hp), $"{classId} has resource-hp");
                Assert.AreEqual(hp.Max, hp.Current, "starts at full health");

                Assert.IsTrue(session.RulesDatabase.TryResolve<ClassDefinition>(result.ClassId, out var cls));
                var expected = cls.HitDie.Count * cls.HitDie.Sides + cls.HitDie.Modifier + AbilityModifier.From(character.Stats.GetScore(new DefinitionId("stat-constitution")));
                Assert.AreEqual(expected, hp.Max, $"{classId}: hit-die maximum + Constitution modifier");
                Assert.AreEqual(ExpectedHitPoints[classId], hp.Max, $"{classId} (the plan's expected maximum)");
                Assert.Greater(hp.Max, 0);

                CollectionAssert.AreEquivalent(result.Plan.Appearance.ToList(), character.Appearance.ToList(), $"{classId}: the chosen look");
                Assert.IsNotEmpty(character.Features, $"{classId} has its class features");
            }
        }

        [Test]
        public void TheFactoryRefusesAPlanAboveLevelOneRatherThanUnderSupplyingHitPoints()
        {
            var result = NewModel().Build();
            result.Plan.Level = 2;

            var e = Assert.Throws<InvalidOperationException>(() => ProtagonistFactory.Build(session.RulesDatabase, result, ProtagonistId));
            StringAssert.Contains("level", e.Message.ToLowerInvariant());
        }

        [Test]
        public void TheFactoryRefusesAPlanWithNoClass()
        {
            var result = new CreatorResult(new CharacterBuildPlan(), new DefinitionId("class-none"), Array.Empty<DefinitionId>());

            Assert.Throws<InvalidOperationException>(() => ProtagonistFactory.Build(session.RulesDatabase, result, ProtagonistId));
        }

        // ---- the ShellPanel summary line (Gate D) ----

        [Test]
        public void TheSummaryNamesHitPointsFeaturesAndProficienciesOfTheCharacter()
        {
            var model = NewModel();
            model.ClassRow.Select(new DefinitionId("class-fighter"));
            var character = ProtagonistFactory.Build(session.RulesDatabase, model.Build(), ProtagonistId);

            var summary = ShellPanel.Summarize(character, session.RulesDatabase);

            StringAssert.Contains("HP 12/12", summary);
            StringAssert.Contains($"Proficiencies: {character.Proficiencies.Count}", summary);
            var named = character.Features.Select(f => session.RulesDatabase.TryResolve<FeatureDefinition>(f, out var d) ? d.DisplayName : null).Where(n => !string.IsNullOrEmpty(n)).ToList();
            Assert.IsNotEmpty(named, "the fighter has features with display names");
            foreach (var name in named) StringAssert.Contains(name, summary, "every named feature is listed");
        }

        [Test]
        public void TheSummaryDiffersBetweenClassesSoTheClassIsObservable()
        {
            var model = NewModel();
            var summaries = new List<string>();
            for (var c = 0; c < model.ClassRow.Count; c++)
            {
                model.ClassRow.SelectIndex(c);
                summaries.Add(ShellPanel.Summarize(ProtagonistFactory.Build(session.RulesDatabase, model.Build(), ProtagonistId), session.RulesDatabase));
            }

            CollectionAssert.AllItemsAreUnique(summaries, "five classes, five different summaries");
            Assert.AreEqual("", ShellPanel.Summarize(null, session.RulesDatabase), "no character, no summary");
        }

        // ---- the authored Core scene ----

        private const string CorePath = "Assets/Scenes/Core.unity";

        [Test]
        public void TheAuthoredCoreSceneStartsAtTheTitleAndWiresBothScreensAndTheCreatorContent()
        {
            var scene = EditorSceneManager.OpenScene(CorePath, OpenSceneMode.Additive);
            try
            {
                var roots = scene.GetRootGameObjects();
                var boot = roots.SelectMany(r => r.GetComponentsInChildren<ShellBootstrap>(true)).Single();
                var so = new SerializedObject(boot);
                Assert.AreEqual((int)ShellStartMode.Title, so.FindProperty("startMode").enumValueIndex, "the real game starts at the title");
                var title = so.FindProperty("titleScreen").objectReferenceValue as ShellTitleScreen;
                var creator = so.FindProperty("creatorScreen").objectReferenceValue as CharacterCreatorScreen;
                Assert.IsNotNull(title, "title screen");
                Assert.IsNotNull(creator, "creator screen");
                Assert.IsNotNull(so.FindProperty("creatorPicks").objectReferenceValue, "picks TextAsset");
                Assert.IsNotNull(so.FindProperty("creatorClassTable").objectReferenceValue, "class table TextAsset");

                var creatorSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(BarrowsEndCreatorAuthor.PanelSettingsPath);
                Assert.IsNotNull(creatorSettings);
                Assert.AreSame(creatorSettings, title.GetComponent<UIDocument>().panelSettings, "the title uses the creator panel settings");
                Assert.AreSame(creatorSettings, creator.GetComponent<UIDocument>().panelSettings, "the creator uses the creator panel settings");
                Assert.AreEqual(10, creatorSettings.sortingOrder, "the title and creator draw above the gate ShellPanel");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void TheStartModeDefaultsToTheAuthoredPartySoNothingElseChangesBehaviour()
        {
            var go = new GameObject("ShellBootstrapDefaults");
            try
            {
                var so = new SerializedObject(go.AddComponent<ShellBootstrap>());
                Assert.AreEqual((int)ShellStartMode.AuthoredParty, so.FindProperty("startMode").enumValueIndex);
                Assert.IsNull(ShellBootstrap.StartModeOverride, "no test override leaks into other tests");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}

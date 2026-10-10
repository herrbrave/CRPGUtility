using System.Collections.Generic;
using System.Linq;
using CRPG.Actors;
using CRPG.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using VikingRaid.Appearance;
using VikingRaid.Appearance.Editor;

namespace VikingRaid.Tests.EditMode
{
    /// <summary>The creator UI on the NpcDefinition inspector (no 3D preview here: the test runner has no graphics device; the preview is proven by AppearancePreviewProbe).</summary>
    public sealed class ModelAppearanceCreatorTests
    {
        private ModelAppearanceCatalog catalog;
        private readonly List<Object> made = new();

        private static GameObject VikingBase => AssetDatabase.LoadAssetAtPath<GameObject>(ModelAppearanceFamilies.BasePrefabPath("Viking"));
        private static GameObject GoblinBase => AssetDatabase.LoadAssetAtPath<GameObject>(ModelAppearanceFamilies.BasePrefabPath("Goblin"));

        [OneTimeSetUp]
        public void LoadCatalog() => catalog = ModelAppearanceCatalogAuthor.LoadOrBuild();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in made) if (o != null) Object.DestroyImmediate(o);
            made.Clear();
        }

        private SerializedObject NewNpc(GameObject visualPrefab, params (string slot, string option)[] entries)
        {
            var npc = ScriptableObject.CreateInstance<NpcDefinition>(); made.Add(npc);
            var so = new SerializedObject(npc);
            so.FindProperty("visualPrefab").objectReferenceValue = visualPrefab;
            var list = so.FindProperty("appearance");
            list.arraySize = entries.Length;
            for (var i = 0; i < entries.Length; i++)
            {
                list.GetArrayElementAtIndex(i).FindPropertyRelative("layerSlot.value").stringValue = entries[i].slot;
                list.GetArrayElementAtIndex(i).FindPropertyRelative("option.value").stringValue = entries[i].option;
            }

            so.ApplyModifiedProperties();
            return so;
        }

        private static Dictionary<string, string> Stored(SerializedObject so)
        {
            so.Update();
            var list = so.FindProperty("appearance");
            return Enumerable.Range(0, list.arraySize).ToDictionary(
                i => list.GetArrayElementAtIndex(i).FindPropertyRelative("layerSlot.value").stringValue,
                i => list.GetArrayElementAtIndex(i).FindPropertyRelative("option.value").stringValue);
        }

        private ModelAppearanceCreatorElement Open(SerializedObject so) => new(so, withPreview: false);

        // ------------------------------------------------------------------ which NPCs it handles
        [Test]
        public void ItHandlesAnNpcWithNoPrefabAndOneWhosePrefabHasTheBinderButNotAnyOtherPrefab()
        {
            var handler = new ModelAppearanceCreatorEditor();
            var npc = ScriptableObject.CreateInstance<NpcDefinition>(); made.Add(npc);
            Assert.IsTrue(handler.Handles(npc), "no prefab yet: the creator offers the base prefab");

            var so = new SerializedObject(npc);
            so.FindProperty("visualPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Characters/Villagers/VikingNpcBase.prefab");
            so.ApplyModifiedProperties();
            Assert.IsTrue(handler.Handles(npc), "the base prefab carries the binder");

            so.FindProperty("visualPrefab").objectReferenceValue = catalog.MasterPrefab;
            so.ApplyModifiedProperties();
            Assert.IsFalse(handler.Handles(npc), "a plain model prefab has no binder: not ours");
            Assert.IsFalse(handler.Handles(null));
        }

        [Test]
        public void TheBasePrefabIsTheMasterRigWithTheBinderWiredToTheCatalog()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Characters/Villagers/VikingNpcBase.prefab");
            Assert.IsNotNull(prefab);
            Assert.IsTrue(prefab.TryGetComponent<ModelAppearanceBinder>(out var binder));
            Assert.AreSame(catalog, binder.Catalog);
            Assert.IsNotNull(prefab.GetComponent<CRPG.Gameplay.CharacterAgent>(), "a toolkit character wrapper");
            Assert.IsTrue(prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length >= 24, "every body, hair and fur is on the model");
        }

        // ------------------------------------------------------------------ in the real NpcDefinition inspector
        [Test]
        public void TheNpcInspectorShowsTheCreatorAndSwitchesToItsRowsOnceThePrefabIsAssigned()
        {
            var npc = ScriptableObject.CreateInstance<NpcDefinition>(); made.Add(npc);
            var editor = UnityEditor.Editor.CreateEditor(npc, typeof(CRPG.Editor.Npc.NpcDefinitionEditor)); made.Add(editor);
            var root = editor.CreateInspectorGUI();

            Assert.IsNotNull(root.Q<VisualElement>("model-appearance"), "the creator is in the inspector of an NPC with no prefab");
            Assert.IsNotNull(root.Q<Button>("use-base-prefab-viking"));

            var so = new SerializedObject(npc);
            so.FindProperty("visualPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(ModelAppearanceFamilies.BasePrefabPath("Viking"));
            so.ApplyModifiedProperties();
            editor.serializedObject.Update();   // the tracker fires after the inspector's own object has re-read the asset
            ((CRPG.Editor.Npc.NpcDefinitionEditor)editor).RebuildAppearanceExtensions();   // what the visualPrefab tracker does on the next poll
            Assert.IsNull(root.Q<Button>("use-base-prefab-viking"));
            Assert.IsNotNull(root.Q<Button>("tab-head"), "the rows replace the offer");
        }

        [Test]
        public void AnNpcWithSomeOtherPrefabGetsNoVikingCreator()
        {
            var npc = ScriptableObject.CreateInstance<NpcDefinition>(); made.Add(npc);
            var so = new SerializedObject(npc);
            so.FindProperty("visualPrefab").objectReferenceValue = catalog.MasterPrefab;
            so.ApplyModifiedProperties();
            var editor = UnityEditor.Editor.CreateEditor(npc, typeof(CRPG.Editor.Npc.NpcDefinitionEditor)); made.Add(editor);
            Assert.IsNull(editor.CreateInspectorGUI().Q<VisualElement>("model-appearance"));
        }

        // ------------------------------------------------------------------ no prefab
        [Test]
        public void WithNoPrefabItOffersTheBaseOneAndClickingAssignsIt()
        {
            var so = NewNpc(null);
            var element = Open(so);
            var button = element.Q<Button>("use-base-prefab-viking");
            Assert.IsNotNull(button, "the offer");
            Assert.IsNull(element.Model, "no rows until there is a prefab");

            element.UseBasePrefab("Viking");   // what the button calls
            so.Update();
            Assert.AreEqual(ModelAppearanceFamilies.BasePrefabPath("Viking"), AssetDatabase.GetAssetPath(so.FindProperty("visualPrefab").objectReferenceValue));
            Assert.IsTrue(new ModelAppearanceCreatorEditor().Handles((NpcDefinition)so.targetObject), "the creator still handles the NPC once it has the base prefab");
        }

        // ------------------------------------------------------------------ rows
        [Test]
        public void ItShowsTheThreeTabsAndTheRowsOfTheCurrentTabOnly()
        {
            var element = Open(NewNpc(VikingBase));
            Assert.IsNotNull(element.Q<Button>("tab-body"));
            Assert.IsNotNull(element.Q<Button>("tab-head"));
            Assert.IsNotNull(element.Q<Button>("tab-wear"));

            element.SelectTab("Body");
            CollectionAssert.AreEqual(new[] { "row-body", "row-look" }, element.Query<VisualElement>().Where(v => v.name != null && v.name.StartsWith("row-")).ToList().Select(v => v.name).ToArray());
            element.SelectTab("Head");
            CollectionAssert.AreEqual(new[] { "row-hair", "row-headwear", "row-face" }, element.Query<VisualElement>().Where(v => v.name != null && v.name.StartsWith("row-")).ToList().Select(v => v.name).ToArray());
            element.SelectTab("Wear");
            CollectionAssert.AreEqual(new[] { "row-cloak", "row-shoulders", "row-gear" }, element.Query<VisualElement>().Where(v => v.name != null && v.name.StartsWith("row-")).ToList().Select(v => v.name).ToArray());
        }

        [Test]
        public void TheRowReadsTitleAndValueAndTheArrowsRotateWithWraparound()
        {
            var element = Open(NewNpc(VikingBase));
            element.SelectTab("Head");
            Assert.AreEqual("None", element.Q<Label>("value-hair").text);

            element.Model.RowFor("hair").Advance(1);
            Assert.AreEqual(catalog.FindSlot("hair").options[0].title, element.Q<Label>("value-hair").text);
            element.Model.RowFor("hair").Advance(-2);   // None, then wrap to the last
            Assert.AreEqual(catalog.FindSlot("hair").options.Last().title, element.Q<Label>("value-hair").text);
        }

        [Test]
        public void ChangingARowWritesTheNpcsAppearanceAndNoneRemovesTheEntry()
        {
            var so = NewNpc(VikingBase);
            var element = Open(so);

            element.Model.RowFor("hair").Select(new DefinitionId("hair-viking-hair-02"));
            var stored = Stored(so);
            Assert.AreEqual("hair-viking-hair-02", stored["hair"]);
            Assert.AreEqual(catalog.FindSlot("body").defaultOptionId, stored["body"], "required rows are always stored");

            element.Model.RowFor("hair").SelectIndex(0);
            Assert.IsFalse(Stored(so).ContainsKey("hair"));
        }

        [Test]
        public void ExistingEntriesLoadIntoTheRowsAndUnknownIdsFallBack()
        {
            var so = NewNpc(VikingBase, ("body", "body-leader-female-01"), ("cloak", "cloak-viking-cape-02"), ("hair", "not-a-hair"), ("look", "nope"));
            var element = Open(so);
            Assert.AreEqual("body-leader-female-01", element.Model.RowFor("body").Selected.Id.Value);
            Assert.AreEqual("cloak-viking-cape-02", element.Model.RowFor("cloak").Selected.Id.Value);
            Assert.AreEqual("None", element.Model.RowFor("hair").Selected.Name, "an unknown option on an optional row is None");
            Assert.AreEqual(catalog.FindSlot("look").defaultOptionId, element.Model.RowFor("look").Selected.Id.Value, "an unknown option on a required row is its default");
        }

        [Test]
        public void LoadingFromTheAssetNeverRaisesChangedSoItCannotLoopBackIntoAWrite()
        {
            var model = new ModelAppearanceCreatorModel(catalog);
            var raised = 0;
            model.Changed += () => raised++;
            model.Load(new Dictionary<DefinitionId, DefinitionId> { [new DefinitionId("body")] = new DefinitionId("body-leader-male-01"), [new DefinitionId("hair")] = new DefinitionId("hair-viking-hair-02") });
            Assert.AreEqual(0, raised);
            Assert.AreEqual("body-leader-male-01", model.RowFor("body").Selected.Id.Value);
            model.RowFor("hair").Advance(1);
            Assert.AreEqual(1, raised, "an edit through the row does");
        }

        // ------------------------------------------------------------------ the goblin family and switching between families
        [Test]
        public void ItHandlesTheGoblinBasePrefabAndGivesItTheGoblinRows()
        {
            Assert.IsNotNull(GoblinBase, "run 'Viking Raid/Appearance/Author Goblin Base Prefab'");
            Assert.IsTrue(new ModelAppearanceCreatorEditor().Handles(NewNpc(GoblinBase).targetObject as NpcDefinition));

            var element = Open(NewNpc(GoblinBase));
            Assert.AreEqual("Goblin", element.Catalog.Family);
            Assert.IsNotNull(element.Q<Button>("tab-face")); Assert.IsNotNull(element.Q<Button>("tab-outfit")); Assert.IsNotNull(element.Q<Button>("tab-gear")); Assert.IsNotNull(element.Q<Button>("tab-colour"));
            Assert.IsNull(element.Q<Button>("tab-head"), "no Viking tabs");
            Assert.AreEqual("Goblin Appearance", element.Q<Label>().text);
            element.SelectTab("Face");
            CollectionAssert.AreEqual(new[] { "row-face", "row-hair", "row-facialhair" }, element.Query<VisualElement>().Where(v => v.name != null && v.name.StartsWith("row-")).ToList().Select(v => v.name).ToArray());
            element.SelectTab("Colour");
            CollectionAssert.AreEqual(new[] { "row-look", "row-build" }, element.Query<VisualElement>().Where(v => v.name != null && v.name.StartsWith("row-")).ToList().Select(v => v.name).ToArray());
        }

        [Test]
        public void ThePreviewFramesTheHeadOnEachFamiliesOwnHeadTab()
        {
            var goblin = Open(NewNpc(GoblinBase));
            goblin.SelectTab("Face"); Assert.IsTrue(goblin.HeadFramed, "the goblin's head tab is Face");
            goblin.SelectTab("Outfit"); Assert.IsFalse(goblin.HeadFramed);
            var viking = Open(NewNpc(VikingBase));
            viking.SelectTab("Head"); Assert.IsTrue(viking.HeadFramed);
            viking.SelectTab("Wear"); Assert.IsFalse(viking.HeadFramed);
        }

        [Test]
        public void AGoblinEditWritesGoblinIdsAndARandomizeProducesOnlyRealOptions()
        {
            var so = NewNpc(GoblinBase);
            var element = Open(so);
            element.Model.RowFor("torso").Select(new DefinitionId("torso-set-04"));
            Assert.AreEqual("torso-set-04", Stored(so)["torso"]);
            Assert.AreEqual("face-goblin-01", Stored(so)["face"], "required rows are always stored");

            element.Model.Randomize(77);
            var catalog = element.Catalog;
            foreach (var pair in Stored(so)) Assert.IsNotNull(catalog.FindSlot(pair.Key)?.Find(pair.Value), pair.Key + "=" + pair.Value);
        }

        [Test]
        public void WithNoPrefabEveryFamilyIsOfferedAndAClickAssignsThatFamilysBase()
        {
            var so = NewNpc(null);
            var element = Open(so);
            Assert.IsNotNull(element.Q<Button>("use-base-prefab-viking"));
            Assert.IsNotNull(element.Q<Button>("use-base-prefab-goblin"));
            element.UseBasePrefab("Goblin");
            so.Update();
            Assert.AreEqual(ModelAppearanceFamilies.BasePrefabPath("Goblin"), AssetDatabase.GetAssetPath(so.FindProperty("visualPrefab").objectReferenceValue));
        }

        [Test]
        public void SwitchingAnNpcToAnotherFamilyDropsTheOldFamilysEntriesButKeepsForeignAndUnknownOnes()
        {
            var so = NewNpc(GoblinBase, ("body", "body-leader-male-01"), ("headwear", "headwear-viking-helmet-01"), ("hair", "hair-viking-hair-02"), ("face", "face-viking-eyepatch-01"),
                ("torso", "torso-set-03"), ("sprite-skin", "skin-03"), ("cloak", "no-such-cloak-anywhere"), ("mystery-row", "mystery-option"));
            Open(so);
            var stored = Stored(so);
            CollectionAssert.DoesNotContain(stored.Keys, "body"); CollectionAssert.DoesNotContain(stored.Keys, "headwear");
            CollectionAssert.DoesNotContain(stored.Keys, "hair", "a Viking hair id in a row both families have is the other family's");
            CollectionAssert.DoesNotContain(stored.Keys, "face");
            Assert.AreEqual("torso-set-03", stored["torso"], "the goblin's own entry stays");
            Assert.AreEqual("skin-03", stored["sprite-skin"], "a foreign creator's data stays");
            CollectionAssert.DoesNotContain(stored.Keys, "cloak", "a row only the Viking catalog has is the other family's, whatever the option id");
            Assert.AreEqual("mystery-option", stored["mystery-row"], "a row nobody knows: kept, not guessed at");

            // and the other way round
            var vikingSo = NewNpc(VikingBase, ("torso", "torso-set-03"), ("face", "face-goblin-04"), ("body", "body-peasant-male-01"));
            Open(vikingSo);
            var viking = Stored(vikingSo);
            CollectionAssert.DoesNotContain(viking.Keys, "torso"); CollectionAssert.DoesNotContain(viking.Keys, "face");
            Assert.AreEqual("body-peasant-male-01", viking["body"]);
        }

        // ------------------------------------------------------------------ the human family and three families together
        private static GameObject HumanBase => AssetDatabase.LoadAssetAtPath<GameObject>(ModelAppearanceFamilies.BasePrefabPath("Human"));

        [Test]
        public void ItHandlesTheHumanBasePrefabAndGivesItTheHumanRowsAndTabs()
        {
            Assert.IsNotNull(HumanBase, "run 'Viking Raid/Appearance/Author Human Base Prefab'");
            Assert.IsTrue(new ModelAppearanceCreatorEditor().Handles(NewNpc(HumanBase).targetObject as NpcDefinition));
            var element = Open(NewNpc(HumanBase));
            Assert.AreEqual("Human", element.Catalog.Family);
            Assert.AreEqual("Human Appearance", element.Q<Label>().text);
            foreach (var tab in new[] { "tab-face", "tab-outfit", "tab-gear", "tab-colour" }) Assert.IsNotNull(element.Q<Button>(tab), tab);
            element.SelectTab("Face"); Assert.IsTrue(element.HeadFramed);
            CollectionAssert.AreEqual(new[] { "row-face", "row-hair", "row-facialhair" }, element.Query<VisualElement>().Where(v => v.name != null && v.name.StartsWith("row-")).ToList().Select(v => v.name).ToArray());
            element.SelectTab("Gear"); Assert.IsFalse(element.HeadFramed);
            CollectionAssert.AreEqual(new[] { "row-headwear", "row-faceware", "row-cloak", "row-gear" }, element.Query<VisualElement>().Where(v => v.name != null && v.name.StartsWith("row-")).ToList().Select(v => v.name).ToArray());
        }

        [Test]
        public void TheHumanColourTabOffersSkinAndHairColourFromTheLookWithASwatchPerTone()
        {
            var element = Open(NewNpc(HumanBase));
            element.SelectTab("Colour");
            CollectionAssert.AreEqual(new[] { "row-look", "row-build", "row-skin", "row-haircolour" }, element.Query<VisualElement>().Where(v => v.name != null && v.name.StartsWith("row-")).ToList().Select(v => v.name).ToArray());
            Assert.AreEqual("From look", element.Q<Label>("value-skin").text); Assert.AreEqual("From look", element.Model.RowFor("haircolour").Selected.Name);
            Assert.AreEqual("Starter 1", element.Q<Label>("value-look").text, "required rows are unchanged");
            var skin = element.Model.RowFor("skin");
            skin.Advance(1);
            Assert.AreEqual("Pale", skin.Selected.Name); Assert.IsTrue(skin.Selected.HasSwatch);
            Assert.AreEqual("skin-pale", element.Model.Choices[new DefinitionId("skin")].Value);
        }

        [Test]
        public void WithNoPrefabAllThreeFamiliesAreOffered()
        {
            var element = Open(NewNpc(null));
            foreach (var family in new[] { "viking", "goblin", "human" }) Assert.IsNotNull(element.Q<Button>("use-base-prefab-" + family), family);
        }

        [Test]
        public void SwitchingFromVikingToHumanKeepsTheSharedPolygonPiecesAndDropsTheVikingBodyChoices()
        {
            var so = NewNpc(HumanBase, ("body", "body-warrior-male-01"), ("headwear", "headwear-viking-helmet-horns-02"), ("cloak", "cloak-viking-cape-01"), ("hair", "hair-viking-hair-07"),
                ("face", "face-viking-eyepatch-01"), ("gear", "gear-war-camp-bags-02"), ("sprite-skin", "skin-03"));
            Open(so);
            var stored = Stored(so);
            Assert.AreEqual("headwear-viking-helmet-horns-02", stored["headwear"], "the Polygon helmet id is shared: kept");
            Assert.AreEqual("cloak-viking-cape-01", stored["cloak"]); Assert.AreEqual("hair-viking-hair-07", stored["hair"]); Assert.AreEqual("gear-war-camp-bags-02", stored["gear"]);
            CollectionAssert.DoesNotContain(stored.Keys, "body", "the Viking body row has no human counterpart");
            Assert.AreNotEqual("face-viking-eyepatch-01", stored.TryGetValue("face", out var face) ? face : null, "an eyepatch is not a human face");
            Assert.AreEqual("skin-03", stored["sprite-skin"], "a foreign creator's data stays");
        }

        [Test]
        public void SwitchingFromGoblinToHumanDropsTheGoblinPiecesOfSharedRows()
        {
            var so = NewNpc(HumanBase, ("face", "face-goblin-04"), ("hair", "hair-goblin-03"), ("torso", "torso-set-02"), ("outfit", "outfit-knight"), ("build", "build-heavy"));
            Open(so);
            var stored = Stored(so);
            CollectionAssert.DoesNotContain(stored.Keys.Where(k => k == "face"), "face"); // a goblin face id is not a human face
            CollectionAssert.DoesNotContain(stored.Keys, "torso");
            Assert.AreEqual("outfit-knight", stored["outfit"]);
            Assert.AreEqual("build-heavy", stored["build"], "the build id is shared by the goblin and human catalogs");
        }

        [Test]
        public void AHumanEditWritesHumanIdsAndTheRandomizerOnlyRollsRealOptions()
        {
            var so = NewNpc(HumanBase);
            var element = Open(so);
            element.Model.RowFor("outfit").Select(new DefinitionId("outfit-knight"));
            Assert.AreEqual("outfit-knight", Stored(so)["outfit"]);
            Assert.AreEqual("face-human-01", Stored(so)["face"], "required rows are always stored");
            element.Model.Randomize(55);
            foreach (var pair in Stored(so)) Assert.IsNotNull(element.Catalog.FindSlot(pair.Key)?.Find(pair.Value), pair.Key + "=" + pair.Value);
        }

        [Test]
        public void EntriesForRowsTheCatalogDoesNotOwnSurviveEveryWrite()
        {
            var so = NewNpc(VikingBase, ("sprite-skin", "skin-03"));
            var element = Open(so);
            element.Model.RowFor("hair").Advance(1);
            element.Model.Randomize(3);
            Assert.AreEqual("skin-03", Stored(so)["sprite-skin"]);
            element.Model.Reset();
            Assert.AreEqual("skin-03", Stored(so)["sprite-skin"]);
        }

        [Test]
        public void ResetGoesBackToTheDefaultBodyAndLookAndClearsTheRest()
        {
            var so = NewNpc(VikingBase, ("body", "body-peasant-male-02"), ("hair", "hair-viking-hair-02"), ("cloak", "cloak-viking-cape-01"));
            var element = Open(so);
            element.Model.Reset();
            var stored = Stored(so);
            CollectionAssert.AreEquivalent(new[] { "body", "look" }, stored.Keys.ToArray());
            Assert.AreEqual(catalog.FindSlot("body").defaultOptionId, stored["body"]);
        }

        [Test]
        public void RandomizeOnATabRollsOnlyThatTabAndTheSameSeedIsRepeatable()
        {
            var so = NewNpc(VikingBase, ("body", "body-leader-male-01"), ("look", "look-05-c"));
            var element = Open(so);
            element.SelectTab("Head");
            element.Model.Randomize(42, "Head");
            var first = Stored(so);
            Assert.AreEqual("body-leader-male-01", first["body"]);
            Assert.AreEqual("look-05-c", first["look"]);
            Assert.IsFalse(first.ContainsKey("cloak") || first.ContainsKey("shoulders") || first.ContainsKey("gear"), "the Wear rows were not rolled");

            element.Model.Reset();
            element.Model.Randomize(42, "Head");
            Assert.AreEqual(first.Where(p => p.Key != "body" && p.Key != "look").OrderBy(p => p.Key).ToList(), Stored(so).Where(p => p.Key != "body" && p.Key != "look").OrderBy(p => p.Key).ToList());
        }
    }
}

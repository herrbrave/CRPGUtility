using System.Collections.Generic;
using System.IO;
using System.Linq;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Dialogue;
using CRPG.Editor.Location;
using CRPG.Editor.Validation;
using CRPG.Gameplay.Scenes;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace CRPG.Tests.EditMode
{
    /// <summary>The editor half of NPC dialogue: authoring the dialogue on the spawn point (a new spawn point must not inherit the previous NPC's), the validator rules and the dialogue graph checks it leans on.</summary>
    public sealed class LocationDialogueEditorTests
    {
        private const string Folder = "Assets/__NpcDialogueEditorTests";
        private readonly List<Object> made = new();
        private LocationContent location;
        private GameObject visual;

        [SetUp]
        public void SetUp()
        {
            visual = new GameObject("visual");
            made.Add(visual);
            visual.AddComponent<CapsuleCollider>();
            var root = new GameObject("CRPG");
            made.Add(root);
            var host = new GameObject("Location");
            host.transform.SetParent(root.transform, false);
            location = host.AddComponent<LocationContent>();
        }

        [TearDown]
        public void TearDown()
        {
            Selection.objects = System.Array.Empty<Object>();
            if (AssetDatabase.IsValidFolder(Folder)) AssetDatabase.DeleteAsset(Folder);
            foreach (var o in made) if (o != null && !EditorUtility.IsPersistent(o)) Object.DestroyImmediate(o);
            made.Clear();
        }

        private NpcDefinition Npc(string id, string dialogueId = null, GameObject body = null)
        {
            var npc = ScriptableObject.CreateInstance<NpcDefinition>();
            made.Add(npc);
            var so = new SerializedObject(npc);
            so.FindProperty("id").stringValue = id;
            so.FindProperty("visualPrefab").objectReferenceValue = body != null ? body : visual;
            if (dialogueId != null) so.FindProperty("dialogueId").FindPropertyRelative("value").stringValue = dialogueId;
            so.ApplyModifiedPropertiesWithoutUndo();
            return npc;
        }

        private NpcSpawnPoint AddNpc(string id, NpcDefinition npc = null, Vector3 at = default)
        {
            LocationPlacement.AddSpawn(location, npc != null ? npc : Npc("npc-" + id), at, Quaternion.identity, id);
            var point = location.SpawnPoints.First(p => p.SpawnId == id);
            if (point.Anchor.parent != null && !made.Contains(point.Anchor.parent.gameObject)) made.Add(point.Anchor.parent.gameObject);
            return point;
        }

        private static SerializableGuid Guid(string seed) => SerializableGuid.FromString(seed);

        private DialogueDefinition Dialogue(string id, params DialogueNode[] nodes)
        {
            var dialogue = ScriptableObject.CreateInstance<DialogueDefinition>();
            made.Add(dialogue);
            TestDefinitionFactory.SetPrivateField(dialogue, "id", id);
            TestDefinitionFactory.SetPrivateField(dialogue, "nodes", new List<DialogueNode>(nodes));
            if (nodes.Length > 0) TestDefinitionFactory.SetPrivateField(dialogue, "startNodeId", nodes[0].NodeId);
            return dialogue;
        }

        private DialogueDefinition ValidDialogue(string id)
        {
            var end = DialogueTestFactory.CreateEnd("end");
            return Dialogue(id, DialogueTestFactory.CreateLine("start", "Elder", "Welcome.", "end"), end);
        }

        private void SetDialogue(NpcSpawnPoint point, DialogueDefinition dialogue) => point.Dialogue = dialogue;

        private IEnumerable<ValidationResult> Dialogues(bool deep = true) =>
            LocationValidator.Validate(location, null, deepChecks: deep).Where(r => r.Field.EndsWith(".Dialogue"));

        // ---------------------------------------------------------------- placement

        [Test]
        public void AddSpawn_GivesTheNewNpcNoDialogue_NotACopyOfThePreviousOne()
        {
            AddNpc("a");
            location.SpawnPoints[0].Dialogue = ValidDialogue("dlg-a");
            AddNpc("b", at: new Vector3(5f, 0f, 0f));

            Assert.IsNotNull(location.SpawnPoints[0].Dialogue);
            Assert.IsNull(location.SpawnPoints[1].Dialogue);
        }

        [Test]
        public void ASpawnPointsDialogue_CanBeAssignedAndCleared_AsOneUndoStep()
        {
            AddNpc("smith");
            var dialogue = ValidDialogue("dlg-smith");
            var spawn = new SpawnRef(location, 0);

            LocationNpcAccess.AssignDialogue(spawn, dialogue);
            Assert.AreSame(dialogue, location.SpawnPoints[0].Dialogue);
            Assert.AreSame(dialogue, LocationNpcAccess.SpawnDialogue(spawn));

            Undo.PerformUndo();
            Assert.IsNull(location.SpawnPoints[0].Dialogue, "one undo step");

            LocationNpcAccess.AssignDialogue(spawn, dialogue);
            LocationNpcAccess.AssignDialogue(spawn, null);
            Assert.IsNull(location.SpawnPoints[0].Dialogue);
        }

        [Test]
        public void CreatingASpawnDialogue_MakesAnAssetNamedAfterTheSpawnId_AssignsItAndNeverDuplicatesIt()
        {
            AssetDatabase.CreateFolder("Assets", "__NpcDialogueEditorTests");
            var npc = Npc("npc-smith");
            AssetDatabase.CreateAsset(npc, Folder + "/npc-smith.asset");
            AddNpc("hammer-smith", npc);
            var spawn = new SpawnRef(location, 0);

            var created = LocationNpcAccess.CreateDialogue(spawn);

            Assert.IsNotNull(created);
            Assert.AreEqual("dlg-hammer-smith", created.Id.Value);
            Assert.IsTrue(File.Exists(Path.Combine(Folder, "dlg-hammer-smith.asset")), "saved beside the NPC asset");
            Assert.AreSame(created, location.SpawnPoints[0].Dialogue, "assigned to the spawn point");
            Assert.IsFalse(npc.DialogueId.IsValid, "the shared NpcDefinition is not touched");
            Assert.AreSame(created, LocationNpcAccess.CreateDialogue(new SpawnRef(location, 0)), "a spawn point that has one gets it back");
            Assert.AreEqual(1, AssetDatabase.FindAssets("t:DialogueDefinition", new[] { Folder }).Length);
        }

        [Test]
        public void TheDrawerShowsADialogueField_AndItsDialogueButtonCreatesOnTheSpawnPoint()
        {
            AssetDatabase.CreateFolder("Assets", "__NpcDialogueEditorTests");
            var npc = Npc("npc-smith");
            AssetDatabase.CreateAsset(npc, Folder + "/npc-smith.asset");
            AddNpc("smith", npc);
            var so = new SerializedObject(location);
            var element = new NpcSpawnPointDrawer().CreatePropertyGUI(so.FindProperty("spawnPoints").GetArrayElementAtIndex(0));

            Assert.IsNotNull(element.Q("spawn-dialogue-field"));
            Assert.IsNotNull(element.Q<Button>("spawn-dialogue"));

            Assert.IsNull(location.SpawnPoints[0].Dialogue);
            LocationNpcAccess.CreateDialogue(new SpawnRef(location, 0));   // what the button's handler does before opening the graph
            Assert.IsNotNull(location.SpawnPoints[0].Dialogue);
        }

        // ---------------------------------------------------------------- validator

        [Test]
        public void NoDialogueAnywhere_IsSilent()
        {
            AddNpc("quiet");
            Assert.IsEmpty(Dialogues().ToList());
        }

        [Test]
        public void ADialogueIdWithNoSpawnDialogue_WarnsThatItOnlyWorksIfTheRulesetRegistersIt()
        {
            AddNpc("orm", Npc("npc-orm", dialogueId: "dlg-orm"));

            var finding = Dialogues().Single();
            Assert.AreEqual(ValidationSeverity.Warning, finding.Severity);
            StringAssert.Contains("ruleset", finding.Message);
        }

        [Test]
        public void AnEmptyDialogue_IsAWarningNotAnError_SoAFreshlyCreatedOneIsNotRed()
        {
            var point = AddNpc("smith");
            SetDialogue(point, Dialogue("dlg-empty"));

            var finding = Dialogues().Single();
            Assert.AreEqual(ValidationSeverity.Warning, finding.Severity);
            StringAssert.Contains("empty", finding.Message);
        }

        [Test]
        public void AValidDialogue_IsClean()
        {
            var point = AddNpc("smith");
            SetDialogue(point, ValidDialogue("dlg-smith"));

            Assert.IsEmpty(Dialogues().ToList());
        }

        [Test]
        public void ABrokenGraph_ReportsTheDialogueValidatorsErrors_OnlyInTheDeepCheck()
        {
            var point = AddNpc("smith");
            SetDialogue(point, Dialogue("dlg-bad", DialogueTestFactory.CreateLine("start", "Smith", "Hi.", "nowhere")));

            Assert.IsTrue(Dialogues(deep: true).Any(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("doesn't exist")), "a dangling reference is an Error");
            Assert.IsEmpty(Dialogues(deep: false).ToList(), "the per-row dot stays cheap");
        }

        [Test]
        public void ADialogueThatOpensAStore_IsAnError_BecauseNoStoreIsWired()
        {
            var point = AddNpc("trader");
            var open = new ActionNode();
            TestDefinitionFactory.SetPrivateField(open, "nodeId", Guid("open"));
            TestDefinitionFactory.SetPrivateField(open, "action", new OpenStoreAction());
            TestDefinitionFactory.SetPrivateField(open, "nextNodeId", Guid("end"));
            SetDialogue(point, Dialogue("dlg-shop", open, DialogueTestFactory.CreateEnd("end")));

            Assert.IsTrue(Dialogues().Any(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("store")));
        }

        [Test]
        public void ABodyWithoutACollider_IsAWarning_BecauseNothingCanBeClicked()
        {
            var bare = new GameObject("bare-body");
            made.Add(bare);
            var point = AddNpc("smith", Npc("npc-smith", body: bare));
            SetDialogue(point, ValidDialogue("dlg-smith"));

            Assert.IsTrue(Dialogues().Any(r => r.Severity == ValidationSeverity.Warning && r.Message.Contains("collider")));
        }

        [Test]
        public void TwoNpcsMayShareOneDialogueAsset()
        {
            var shared = ValidDialogue("dlg-villager");
            AddNpc("a");
            AddNpc("b", at: new Vector3(5f, 0f, 0f));
            location.SpawnPoints[0].Dialogue = shared;
            location.SpawnPoints[1].Dialogue = shared;

            Assert.IsEmpty(Dialogues().ToList());
        }

        // ---------------------------------------------------------------- DialogueValidator gaps

        [Test]
        public void DialogueValidator_AnActionNodeWithNoAction_IsAnError()
        {
            var action = new ActionNode();
            TestDefinitionFactory.SetPrivateField(action, "nodeId", Guid("a"));
            TestDefinitionFactory.SetPrivateField(action, "nextNodeId", Guid("end"));
            var dialogue = Dialogue("dlg-null-action", action, DialogueTestFactory.CreateEnd("end"));

            Assert.IsTrue(DialogueValidator.Validate(dialogue).Any(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("no action")));
        }

        [Test]
        public void DialogueValidator_ANullNodeEntry_IsReportedInsteadOfCrashing()
        {
            var dialogue = Dialogue("dlg-null-node", DialogueTestFactory.CreateEnd("end"), null);

            var results = DialogueValidator.Validate(dialogue);
            Assert.IsTrue(results.Any(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("missing")));
        }
    }
}

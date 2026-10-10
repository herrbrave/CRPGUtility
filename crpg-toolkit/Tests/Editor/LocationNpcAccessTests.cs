using System.Collections.Generic;
using System.IO;
using System.Linq;
using CRPG.Actors;
using CRPG.Dialogue;
using CRPG.Editor.Location;
using CRPG.Gameplay.Scenes;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CRPG.Tests.EditMode
{
    /// <summary>Getting from a placed NPC marker to the things an author edits next: its NpcDefinition and its dialogue.</summary>
    public sealed class LocationNpcAccessTests
    {
        private const string Folder = "Assets/__LocationNpcAccessTests";
        private readonly List<Object> made = new();
        private LocationContent location;
        private NpcDefinition smith;

        [SetUp]
        public void SetUp()
        {
            Directory.CreateDirectory(Folder);
            var root = new GameObject("CRPG"); made.Add(root);
            var host = new GameObject("Location"); host.transform.SetParent(root.transform, false);
            location = host.AddComponent<LocationContent>();
            smith = NpcAsset("npc-smith");
            LocationPlacement.AddSpawn(location, smith, Vector3.zero, Quaternion.identity);
            LocationPlacement.AddSpawn(location, NpcAsset("npc-guard"), Vector3.one * 3f, Quaternion.identity);
            made.Add(location.SpawnPoints[0].Anchor.parent.gameObject);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in made) if (o != null) Object.DestroyImmediate(o);
            made.Clear();
            Selection.activeObject = null;
            AssetDatabase.DeleteAsset(Folder);
        }

        private NpcDefinition NpcAsset(string id)
        {
            var npc = ScriptableObject.CreateInstance<NpcDefinition>();
            var so = new SerializedObject(npc);
            so.FindProperty("id").stringValue = id;
            so.FindProperty("displayName").stringValue = id.Replace("npc-", "");
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.CreateAsset(npc, $"{Folder}/{id}.asset");
            return npc;
        }

        private DialogueDefinition DialogueAsset(string id)
        {
            var dialogue = ScriptableObject.CreateInstance<DialogueDefinition>();
            var so = new SerializedObject(dialogue);
            so.FindProperty("id").stringValue = id;
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.CreateAsset(dialogue, $"{Folder}/{id}.asset");
            return dialogue;
        }

        [Test]
        public void AnAnchorMapsBackToItsLocationRowAndAStrangerDoesNot()
        {
            Assert.IsTrue(LocationNpcAccess.TryFindSpawn(location.SpawnPoints[1].Anchor, out var spawn));
            Assert.AreSame(location, spawn.Location); Assert.AreEqual(1, spawn.Index); Assert.AreEqual("guard", spawn.Point.SpawnId);
            var stranger = new GameObject("Stranger"); made.Add(stranger);
            Assert.IsFalse(LocationNpcAccess.TryFindSpawn(stranger.transform, out _));
            Assert.IsFalse(LocationNpcAccess.TryFindSpawn(null, out _));
        }

        [Test]
        public void ASelectedAnchorIsTheSelectedSpawn()
        {
            Selection.activeGameObject = location.SpawnPoints[0].Anchor.gameObject;
            Assert.IsTrue(LocationNpcAccess.TrySelectedSpawn(out var spawn));
            Assert.AreEqual("smith", spawn.Point.SpawnId);
            Selection.activeGameObject = location.gameObject;
            Assert.IsFalse(LocationNpcAccess.TrySelectedSpawn(out _), "the location itself is not an NPC");
            Selection.activeObject = null;
            Assert.IsFalse(LocationNpcAccess.TrySelectedSpawn(out _));
        }

        [Test]
        public void OpeningTheNpcSelectsItsDefinitionAsset()
        {
            LocationNpcAccess.SelectNpc(smith);
            Assert.AreSame(smith, Selection.activeObject);
        }

        [Test]
        public void AssigningADialogueStoresItsIdOnTheNpcAndResolvingFindsTheAssetAgain()
        {
            Assert.IsNull(LocationNpcAccess.ResolveDialogue(smith));
            var dialogue = DialogueAsset("dlg-smith-greeting");
            LocationNpcAccess.AssignDialogue(smith, dialogue);
            Assert.AreEqual("dlg-smith-greeting", smith.DialogueId.Value);
            Assert.AreSame(dialogue, LocationNpcAccess.ResolveDialogue(smith));
            LocationNpcAccess.AssignDialogue(smith, null);
            Assert.IsFalse(smith.DialogueId.IsValid); Assert.IsNull(LocationNpcAccess.ResolveDialogue(smith));
        }

        [Test]
        public void AssigningADialogueIsOneUndoStep()
        {
            var dialogue = DialogueAsset("dlg-undo");
            LocationNpcAccess.AssignDialogue(smith, dialogue);
            Undo.PerformUndo();
            Assert.IsFalse(smith.DialogueId.IsValid);
        }

        [Test]
        public void CreatingADialogueMakesAnAssetBesideTheNpcWithAReadableIdAssignsItAndNeverDuplicatesIt()
        {
            var created = LocationNpcAccess.CreateDialogue(smith);
            Assert.IsNotNull(created);
            Assert.AreEqual(Folder, Path.GetDirectoryName(AssetDatabase.GetAssetPath(created)).Replace('\\', '/'), "next to the NPC asset");
            Assert.AreEqual("dlg-smith", created.Id.Value);
            Assert.AreEqual("smith", created.DisplayName);
            Assert.AreEqual("dlg-smith", smith.DialogueId.Value); Assert.AreSame(created, LocationNpcAccess.ResolveDialogue(smith));
            Assert.AreSame(created, LocationNpcAccess.CreateDialogue(smith), "an NPC that already has a dialogue gets that one, not a second asset");
            Assert.AreEqual(1, AssetDatabase.FindAssets("t:DialogueDefinition", new[] { Folder }).Length);

            var other = NpcAsset("npc-smith-2");
            var otherDialogue = LocationNpcAccess.CreateDialogue(other);
            Assert.AreNotEqual(created.Id.Value, otherDialogue.Id.Value, "ids stay unique");
        }

        [Test]
        public void ACreatedDialogueNeverReusesAnIdAnotherDialogueAlreadyHas()
        {
            var taken = DialogueAsset("dlg-smith");   // an unrelated dialogue already owns the readable id
            var created = LocationNpcAccess.CreateDialogue(smith);
            Assert.AreNotSame(taken, created);
            Assert.AreEqual("dlg-smith-2", created.Id.Value, "the next free id, so both can be resolved by id");
            Assert.AreSame(created, LocationNpcAccess.ResolveDialogue(smith)); Assert.AreEqual("dlg-smith", taken.Id.Value);
        }

        [Test]
        public void ADialogueIdWithNoAssetIsReportedAsMissingNotCreatedSilently()
        {
            var so = new SerializedObject(smith); so.FindProperty("dialogueId").FindPropertyRelative("value").stringValue = "dlg-gone"; so.ApplyModifiedPropertiesWithoutUndo();
            Assert.IsNull(LocationNpcAccess.ResolveDialogue(smith));
            Assert.IsTrue(LocationNpcAccess.HasDanglingDialogue(smith), "an id is set but no DialogueDefinition has it");
            Assert.IsFalse(LocationNpcAccess.HasDanglingDialogue(NpcAsset("npc-quiet")), "no id: nothing dangling");
        }
    }
}

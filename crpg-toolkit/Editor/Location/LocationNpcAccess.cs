using System.Collections.Generic;
using System.IO;
using System.Linq;
using CRPG.Actors;
using CRPG.Dialogue;
using CRPG.Editor.Dialogue;
using CRPG.Gameplay.Scenes;
using UnityEditor;
using UnityEngine;

namespace CRPG.Editor.Location
{
    /// <summary>One spawn point of one open <see cref="LocationContent"/>.</summary>
    public readonly struct SpawnRef
    {
        public SpawnRef(LocationContent location, int index) { Location = location; Index = index; }
        public LocationContent Location { get; }
        public int Index { get; }
        public NpcSpawnPoint Point => Location.SpawnPoints[Index];
    }

    /// <summary>
    /// The shortcuts between a placed NPC and what an author edits next (used by the Scene-view quick panel and the inspector drawer): find the spawn point of a selected anchor, open the NpcDefinition,
    /// and manage its dialogue. Dialogue is a <see cref="DialogueDefinition"/> asset (edited in the dialogue graph window); the NPC stores only its id (<c>NpcDefinition.DialogueId</c>), so assigning writes that
    /// id and resolving searches the project for the asset that has it. NOTE: the toolkit does not turn <c>DialogueId</c> into a conversation by itself (<c>NpcSpawnService</c> documents that as left to the game's
    /// composition root); this class only authors the link.
    /// </summary>
    public static class LocationNpcAccess
    {
        private const string NpcPrefix = "npc-", DialoguePrefix = "dlg-";

        public static IEnumerable<LocationContent> OpenLocations() => Object.FindObjectsByType<LocationContent>(FindObjectsInactive.Include);

        public static bool TryFindSpawn(Transform anchor, out SpawnRef spawn)
        {
            spawn = default;
            if (anchor == null) return false;
            foreach (var location in OpenLocations())
                for (var i = 0; i < location.SpawnPoints.Count; i++)
                    if (location.SpawnPoints[i].Anchor == anchor) { spawn = new SpawnRef(location, i); return true; }
            return false;
        }

        /// <summary>The spawn point whose anchor is the selected object.</summary>
        public static bool TrySelectedSpawn(out SpawnRef spawn)
        {
            var selected = Selection.activeTransform;
            return TryFindSpawn(selected, out spawn);
        }

        public static void SelectNpc(NpcDefinition npc)
        {
            if (npc == null) return;
            Selection.activeObject = npc;
            EditorGUIUtility.PingObject(npc);
        }

        /// <summary>The DialogueDefinition asset whose id is the NPC's <c>DialogueId</c>, or null (no id, or no asset has it).</summary>
        public static DialogueDefinition ResolveDialogue(NpcDefinition npc)
        {
            if (npc == null || !npc.DialogueId.IsValid) return null;
            foreach (var guid in AssetDatabase.FindAssets("t:DialogueDefinition"))
            {
                var asset = AssetDatabase.LoadAssetAtPath<DialogueDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null && asset.Id.Equals(npc.DialogueId)) return asset;
            }

            return null;
        }

        /// <summary>An id is set on the NPC but no dialogue asset has it (it was deleted, or the id was typed by hand).</summary>
        public static bool HasDanglingDialogue(NpcDefinition npc) => npc != null && npc.DialogueId.IsValid && ResolveDialogue(npc) == null;

        /// <summary>Stores the dialogue's id on the NPC (null clears it), as one Undo step.</summary>
        public static void AssignDialogue(NpcDefinition npc, DialogueDefinition dialogue)
        {
            var serialized = new SerializedObject(npc);
            serialized.Update();
            serialized.FindProperty("dialogueId").FindPropertyRelative("value").stringValue = dialogue != null ? dialogue.Id.Value : "";
            serialized.ApplyModifiedProperties();
        }

        /// <summary>The NPC's dialogue, creating a new empty <c>dlg-&lt;npc&gt;</c> asset beside the NPC asset (and assigning it) when it has none. An NPC that already has one gets that one back.</summary>
        public static DialogueDefinition CreateDialogue(NpcDefinition npc)
        {
            var existing = ResolveDialogue(npc);
            if (existing != null) return existing;

            var raw = npc.Id.Value ?? "";
            var shortName = raw.StartsWith(NpcPrefix) ? raw.Substring(NpcPrefix.Length) : raw;
            if (string.IsNullOrEmpty(shortName)) shortName = npc.name.ToLowerInvariant().Replace(' ', '-');

            var taken = new HashSet<string>(AssetDatabase.FindAssets("t:DialogueDefinition").Select(g => AssetDatabase.LoadAssetAtPath<DialogueDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(a => a != null).Select(a => a.Id.Value));
            var id = DialoguePrefix + shortName;
            for (var n = 2; taken.Contains(id); n++) id = $"{DialoguePrefix}{shortName}-{n}";

            var dialogue = ScriptableObject.CreateInstance<DialogueDefinition>();
            var serialized = new SerializedObject(dialogue);
            serialized.FindProperty("id").stringValue = id;
            serialized.FindProperty("displayName").stringValue = npc.DisplayName;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            var npcPath = AssetDatabase.GetAssetPath(npc);
            var folder = string.IsNullOrEmpty(npcPath) ? "Assets" : Path.GetDirectoryName(npcPath).Replace('\\', '/');
            AssetDatabase.CreateAsset(dialogue, AssetDatabase.GenerateUniqueAssetPath($"{folder}/{id}.asset"));
            AssignDialogue(npc, dialogue);
            AssetDatabase.SaveAssets();
            return dialogue;
        }

        /// <summary>Opens the NPC's dialogue in the dialogue graph window, creating it first when the NPC has none.</summary>
        public static void OpenDialogue(NpcDefinition npc)
        {
            if (npc == null) return;
            var dialogue = CreateDialogue(npc);
            EditorGUIUtility.PingObject(dialogue);
            DialogueGraphWindow.Open(dialogue);
        }
    }
}

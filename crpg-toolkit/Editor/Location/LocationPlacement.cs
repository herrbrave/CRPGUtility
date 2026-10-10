using System;
using System.Collections.Generic;
using System.Linq;
using CRPG.Actors;
using CRPG.Gameplay.Scenes;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CRPG.Editor.Location
{
    /// <summary>
    /// The placement API behind the NPC placement tool (design <c>Docs/Plans/Toolkit-LocationTools.md</c>): add or remove one NPC spawn point of a <see cref="LocationContent"/> together with its anchor, as ONE Undo
    /// step, through the serialized fields (a prefab instance keeps its overrides). Pure editor code with no UI, so it is unit-tested. A <c>SpawnId</c> is permanent once a save has seen it (the saved record
    /// is keyed by it): this API only ever GENERATES ids, it never renames one.
    /// </summary>
    public static class LocationPlacement
    {
        /// <summary>The party's own spawn ids start with this (<c>VillageBootstrap</c> mints <c>hero-&lt;id&gt;</c>): a generated NPC id never does, and the validator warns about a typed one.</summary>
        public const string ReservedPartyPrefix = "hero-";

        public const string AnchorRootName = "NPCs";

        private const string NpcPrefix = "npc-";

        /// <summary>The next free <c>SpawnId</c> for a definition: its id without the <c>npc-</c> prefix (a prefix-less result that would start with the reserved party prefix keeps the <c>npc-</c>), then <c>-2</c>, <c>-3</c> ... Free among this
        /// location's spawn points and those of <paramref name="others"/> (the other open locations).</summary>
        public static string NextSpawnId(LocationContent location, NpcDefinition definition, IEnumerable<LocationContent> others = null)
        {
            var used = new HashSet<string>(location.SpawnPoints.Select(p => p.SpawnId));
            if (others != null) foreach (var other in others) if (other != null && other != location) foreach (var p in other.SpawnPoints) used.Add(p.SpawnId);

            var raw = definition.Id.Value ?? "";
            var baseId = raw.StartsWith(NpcPrefix) ? raw.Substring(NpcPrefix.Length) : raw;
            if (string.IsNullOrEmpty(baseId)) baseId = definition.name.ToLowerInvariant().Replace(' ', '-');
            if (baseId.StartsWith(ReservedPartyPrefix)) baseId = NpcPrefix + baseId;

            if (!used.Contains(baseId)) return baseId;
            for (var n = 2; ; n++)
                if (!used.Contains($"{baseId}-{n}")) return $"{baseId}-{n}";
        }

        /// <summary>Where new anchors go: the common parent of the existing anchors, else an existing sibling named <see cref="AnchorRootName"/> next to the location's host, else null (a new root is created on demand).</summary>
        public static Transform AnchorParent(LocationContent location)
        {
            var anchors = location.SpawnPoints.Select(p => p.Anchor).Where(a => a != null).ToList();
            if (anchors.Count > 0)
            {
                var parent = anchors[0].parent;
                if (parent != null && anchors.All(a => a.parent == parent)) return parent;
            }

            var host = location.transform;
            var siblings = host.parent;
            if (siblings != null) return siblings.Find(AnchorRootName);
            return location.gameObject.scene.GetRootGameObjects().Select(g => g.transform).FirstOrDefault(t => t.name == AnchorRootName);
        }

        private static Transform EnsureAnchorParent(LocationContent location)
        {
            var existing = AnchorParent(location);
            if (existing != null) return existing;
            var made = new GameObject(AnchorRootName);
            SceneManager.MoveGameObjectToScene(made, location.gameObject.scene);
            Undo.RegisterCreatedObjectUndo(made, "Create NPC anchor root");
            if (location.transform.parent != null) Undo.SetTransformParent(made.transform, location.transform.parent, "Create NPC anchor root");
            return made.transform;
        }

        /// <summary>Adds an NPC spawn point and its anchor at the pose (one Undo step) and returns its <c>SpawnId</c>: the typed one (an <see cref="ArgumentException"/> when this location already has it) or a generated one.</summary>
        public static string AddSpawn(LocationContent location, NpcDefinition definition, Vector3 position, Quaternion rotation, string spawnId = null)
        {
            if (location == null) throw new ArgumentNullException(nameof(location));
            if (definition == null) throw new ArgumentException("An NPC definition is required.", nameof(definition));
            if (!string.IsNullOrEmpty(spawnId) && location.SpawnPoints.Any(p => p.SpawnId == spawnId)) throw new ArgumentException($"This location already has a spawn point '{spawnId}'.", nameof(spawnId));

            var id = string.IsNullOrEmpty(spawnId) ? NextSpawnId(location, definition) : spawnId;
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Add NPC " + id);
            var group = Undo.GetCurrentGroup();

            var parent = EnsureAnchorParent(location);
            var anchor = new GameObject("Anchor_" + id);
            SceneManager.MoveGameObjectToScene(anchor, location.gameObject.scene);
            Undo.RegisterCreatedObjectUndo(anchor, "Create NPC anchor");
            Undo.SetTransformParent(anchor.transform, parent, "Create NPC anchor");
            anchor.transform.SetPositionAndRotation(position, rotation);

            var serialized = new SerializedObject(location);
            serialized.Update();
            var points = serialized.FindProperty("spawnPoints");
            points.InsertArrayElementAtIndex(points.arraySize);
            var element = points.GetArrayElementAtIndex(points.arraySize - 1);
            element.FindPropertyRelative("SpawnId").stringValue = id;   // InsertArrayElementAtIndex copies the previous element: every field is set
            element.FindPropertyRelative("Definition").objectReferenceValue = definition;
            element.FindPropertyRelative("Anchor").objectReferenceValue = anchor.transform;
            serialized.ApplyModifiedProperties();

            Undo.CollapseUndoOperations(group);
            return id;
        }

        /// <summary>Removes the spawn point at <paramref name="index"/> (one Undo step); with <paramref name="deleteAnchor"/> also its anchor, unless another entry still uses it or it is an asset.</summary>
        public static void RemoveSpawn(LocationContent location, int index, bool deleteAnchor)
        {
            var serialized = new SerializedObject(location);
            serialized.Update();
            var points = serialized.FindProperty("spawnPoints");
            if (index < 0 || index >= points.arraySize) throw new ArgumentOutOfRangeException(nameof(index));

            var anchor = points.GetArrayElementAtIndex(index).FindPropertyRelative("Anchor").objectReferenceValue as Transform;
            var shared = false;
            for (var i = 0; i < points.arraySize; i++)
                if (i != index && points.GetArrayElementAtIndex(i).FindPropertyRelative("Anchor").objectReferenceValue == anchor && anchor != null) shared = true;

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Remove NPC");
            var group = Undo.GetCurrentGroup();
            points.DeleteArrayElementAtIndex(index);
            serialized.ApplyModifiedProperties();
            if (deleteAnchor && anchor != null && !shared && !EditorUtility.IsPersistent(anchor)) Undo.DestroyObjectImmediate(anchor.gameObject);
            Undo.CollapseUndoOperations(group);
        }
    }
}

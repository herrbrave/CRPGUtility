using System.Collections.Generic;
using System.Linq;
using CRPG.Actors;
using CRPG.Editor.Location;
using CRPG.Editor.Validation;
using CRPG.Gameplay.Scenes;
using CRPG.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace VikingRaid.EditorTools.Village
{
    /// <summary>
    /// Viking Raid's use of the toolkit's location tools (<c>Docs/Plans/Toolkit-LocationTools.md</c>) on the village scene: the village's arrival point is <c>CRPG/PartyStart</c> (it has no scene travel
    /// destinations), so the validator is given that as the reachability origin. <see cref="Dogfood"/> is a one-shot batch check against the REAL scene (real NavMesh, real colliders and layers): it validates
    /// the location, runs the place-mode ray from above the party start, adds a spawn point in memory and validates again. It never saves the scene.
    /// </summary>
    public static class VillageLocationTools
    {
        public const string ScenePath = "Assets/Scenes/VikingVillage.unity";

        private static IEnumerable<Transform> PartyStart(LocationContent location)
        {
            var root = location.transform.parent;
            var start = root != null ? root.Find("PartyStart") : null;
            return start != null ? new[] { start } : System.Array.Empty<Transform>();
        }

        [MenuItem("Viking Raid/Village/Validate Location")]
        public static void ValidateMenu()
        {
            var locations = Object.FindObjectsByType<LocationContent>(FindObjectsInactive.Include);
            if (locations.Length == 0) { Debug.Log("[Village] open VikingVillage.unity first."); return; }
            LocationMenu.Report(LocationMenu.ValidateOpenLocations(new NavMeshService(), PartyStart), locations.Length);
        }

        /// <summary>Batch: <c>-executeMethod VikingRaid.EditorTools.Village.VillageLocationTools.Dogfood</c> (graphics not needed). Lines are prefixed <c>[LocationDogfood]</c>.</summary>
        public static void Dogfood()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Physics.SyncTransforms();
            var nav = new NavMeshService();
            var location = Object.FindFirstObjectByType<LocationContent>();
            Debug.Log($"[LocationDogfood] {location.SpawnPoints.Count} spawn point(s): " + string.Join(", ", location.SpawnPoints.Select(p => $"{p.SpawnId}@{p.Anchor.position:F1}")));

            var before = LocationMenu.ValidateOpenLocations(nav, PartyStart);
            Debug.Log($"[LocationDogfood] validation of the scene as authored: {before.Count} finding(s)" + (before.Count == 0 ? "" : "\n" + string.Join("\n", before)));

            var start = PartyStart(location).First().position;
            var walkable = PlaceNpcTool.WalkablePointAlong(new Ray(start + new Vector3(4f, 40f, 4f), Vector3.down), ~0, nav);
            Debug.Log($"[LocationDogfood] place-mode ray from above {start + new Vector3(4f, 0f, 4f):F1} (all layers): {(walkable.HasValue ? walkable.Value.ToString("F2") : "no walkable point")}");

            var layers = Physics.AllLayers;
            var hits = Physics.RaycastAll(new Ray(start + new Vector3(4f, 40f, 4f), Vector3.down), 200f, layers, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance).Select(h => $"{h.collider.name}(layer {LayerMask.LayerToName(h.collider.gameObject.layer)}) y={h.point.y:F1}");
            Debug.Log("[LocationDogfood] what the ray crosses, top to bottom: " + string.Join(" > ", hits));

            if (walkable.HasValue)
            {
                var definition = AssetDatabase.LoadAssetAtPath<NpcDefinition>("Assets/Content/Village/Npcs/npc-village-hild.asset");
                var id = LocationPlacement.AddSpawn(location, definition, walkable.Value, Quaternion.Euler(0f, 200f, 0f));
                var after = LocationMenu.ValidateOpenLocations(nav, PartyStart);
                Debug.Log($"[LocationDogfood] added '{id}' in memory (not saved): {after.Count} finding(s)" + (after.Count == 0 ? "" : "\n" + string.Join("\n", after)));
                Undo.PerformUndo();
                Debug.Log($"[LocationDogfood] after one undo: {location.SpawnPoints.Count} spawn point(s), anchor root children {location.transform.parent.Find("NPCs").childCount}");
            }

            EditorApplication.Exit(0);
        }
    }
}

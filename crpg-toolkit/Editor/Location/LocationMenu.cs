using System.Collections.Generic;
using System.Linq;
using CRPG.Editor.Validation;
using CRPG.Gameplay.Scenes;
using CRPG.Navigation;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

namespace CRPG.Editor.Location
{
    /// <summary>Menu commands for the location tools (<c>CRPG Toolkit/Location</c>).</summary>
    public static class LocationMenu
    {
        /// <summary>Validates every <see cref="LocationContent"/> in the open scenes (the others are the "other locations" for id uniqueness; each scene's <see cref="SceneTravelDestination"/>s are the arrival points
        /// for the reachability check) and returns the findings.</summary>
        public static List<ValidationResult> ValidateOpenLocations(INavMeshService nav, System.Func<LocationContent, IEnumerable<Transform>> originsFor = null)
        {
            var locations = Object.FindObjectsByType<LocationContent>(FindObjectsInactive.Include).OrderBy(l => l.gameObject.scene.name).ToList();
            var results = new List<ValidationResult>();
            foreach (var location in locations)
            {
                var origins = originsFor != null ? originsFor(location)
                    : Object.FindObjectsByType<SceneTravelDestination>(FindObjectsInactive.Include).Where(d => d.gameObject.scene == location.gameObject.scene).Select(d => d.Anchor);
                results.AddRange(LocationValidator.Validate(location, nav, locations, origins));
            }

            return results;
        }

        /// <summary>Logs the findings (errors and warnings, each pinging its object) and a summary line.</summary>
        public static void Report(List<ValidationResult> results, int locationCount)
        {
            foreach (var r in results)
            {
                var text = $"[Location] {r.AssetPath} {r.Field}: {r.Message} ({r.Fix})";
                if (r.Severity == ValidationSeverity.Error) Debug.LogError(text, r.Context); else Debug.LogWarning(text, r.Context);
            }

            Debug.Log($"[Location] validated {locationCount} location(s): {results.Count(r => r.Severity == ValidationSeverity.Error)} error(s), {results.Count(r => r.Severity == ValidationSeverity.Warning)} warning(s).");
        }

        [MenuItem("CRPG Toolkit/Location/Validate Open Locations")]
        public static void ValidateOpenLocationsMenu()
        {
            var count = Object.FindObjectsByType<LocationContent>(FindObjectsInactive.Include).Length;
            if (count == 0) { Debug.Log("[Location] no LocationContent in the open scenes."); return; }
            Report(ValidateOpenLocations(new NavMeshService()), count);
        }

        [MenuItem("CRPG Toolkit/Location/Place NPC Tool")]
        public static void ActivatePlaceTool()
        {
            var location = PlaceNpcTool.ResolveLocation();
            if (location == null) { Debug.LogWarning("[Location] no LocationContent in the open scenes: add one to a root object first."); return; }
            try { ToolManager.SetActiveTool<PlaceNpcTool>(); }
            catch (System.InvalidOperationException e) { Debug.LogWarning($"[Location] could not activate the Place NPC tool ({e.Message}). Pick it in the Scene view's Tools overlay (the custom tools list)."); }
        }
    }
}

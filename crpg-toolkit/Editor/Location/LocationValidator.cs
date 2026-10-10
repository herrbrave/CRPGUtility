using System.Collections.Generic;
using System.Linq;
using CRPG.Editor.Validation;
using CRPG.Gameplay.Scenes;
using CRPG.Navigation;
using UnityEditor;
using UnityEngine;

namespace CRPG.Editor.Location
{
    /// <summary>
    /// Author-time checks for a <see cref="LocationContent"/> (design <c>Docs/Plans/Toolkit-LocationTools.md</c>, Section 7). Severities follow what the runtime really does: the toolkit's <c>LocationInitializer</c>
    /// THROWS on a missing anchor (and rolls the whole location back); a duplicate <c>SpawnId</c> does not throw but silently corrupts (the second entry takes the restore path and orphans the first view); an
    /// empty or duplicate store or loot id silently shares one persisted state. Those are Errors; things that only look wrong in play are Warnings. Pure logic over an <see cref="INavMeshService"/> (the same
    /// abstraction the runtime uses, a fake in tests; null skips the walkability rules) so it runs from the inspector, a menu and a test. Never changes anything.
    /// </summary>
    public static class LocationValidator
    {
        /// <summary>An anchor farther than this from a point the navigation service can snap to is "off the walkable surface" (metres).</summary>
        public const float SurfaceTolerance = 0.5f;

        /// <summary>Two anchors closer than this overlap (metres).</summary>
        public const float MinSpacing = 0.8f;

        /// <param name="nav">Walkability queries; null skips the off-surface and reachability rules.</param>
        /// <param name="others">The other open locations, for ids that must be unique across the world.</param>
        /// <param name="reachabilityOrigins">Where the party arrives (scene travel destinations, a start point): an anchor none of them can reach is a Warning. None given: not checked.</param>
        public static List<ValidationResult> Validate(LocationContent location, INavMeshService nav, IEnumerable<LocationContent> others = null, IEnumerable<Transform> reachabilityOrigins = null)
        {
            var results = new List<ValidationResult>();
            var spawns = location.SpawnPoints;
            var origins = reachabilityOrigins?.Where(o => o != null).ToList() ?? new List<Transform>();
            var otherIds = new Dictionary<string, string>();
            if (others != null)
                foreach (var other in others)
                    if (other != null && other != location)
                        foreach (var p in other.SpawnPoints)
                            if (!string.IsNullOrEmpty(p.SpawnId)) otherIds[p.SpawnId] = other.name;

            void Add(ValidationSeverity severity, string field, string message, string fix, Object context) =>
                results.Add(new ValidationResult(severity, Path(location), field, message, fix, context ?? location));

            var firstRowOfId = new Dictionary<string, int>();
            for (var i = 0; i < spawns.Count; i++)
            {
                var point = spawns[i];
                var field = $"spawnPoints[{i}]";
                var anchor = point.Anchor;
                Object context = anchor != null ? anchor : location;

                if (string.IsNullOrEmpty(point.SpawnId)) Add(ValidationSeverity.Error, field + ".SpawnId", $"Row {i}: the SpawnId is empty.", "Give it a stable, unique id (the saved NPC record is keyed by it).", context);
                else
                {
                    if (firstRowOfId.TryGetValue(point.SpawnId, out var first))
                        Add(ValidationSeverity.Error, field + ".SpawnId", $"Row {i}: SpawnId '{point.SpawnId}' is a duplicate of row {first} (the second NPC would silently take over the first one's saved record).", "Give each spawn point its own id.", context);
                    else firstRowOfId[point.SpawnId] = i;

                    if (otherIds.TryGetValue(point.SpawnId, out var elsewhere))
                        Add(ValidationSeverity.Error, field + ".SpawnId", $"Row {i}: SpawnId '{point.SpawnId}' is also used in another location ('{elsewhere}'); ids are unique across the whole save, so one NPC would appear in both.", "Rename one of them before any save has seen it.", context);

                    if (point.SpawnId.StartsWith(LocationPlacement.ReservedPartyPrefix))
                        Add(ValidationSeverity.Warning, field + ".SpawnId", $"Row {i}: SpawnId '{point.SpawnId}' starts with '{LocationPlacement.ReservedPartyPrefix}', which the party's own spawn ids use.", "Use another prefix.", context);
                }

                if (point.Definition == null) Add(ValidationSeverity.Error, field + ".Definition", $"Row {i} ('{point.SpawnId}'): no NpcDefinition assigned.", "Assign an NpcDefinition.", context);
                else if (point.Definition.VisualPrefab == null) Add(ValidationSeverity.Error, field + ".Definition", $"Row {i} ('{point.SpawnId}'): '{point.Definition.name}' has no visual prefab, so the NPC would have no body.", "Assign a visual prefab on the NpcDefinition.", context);

                if (anchor == null) { Add(ValidationSeverity.Error, field + ".Anchor", $"Row {i} ('{point.SpawnId}'): the anchor is missing; the LocationInitializer would throw and the whole location would fail to load.", "Assign a scene transform (the placement tool creates one).", context); continue; }
                if (EditorUtility.IsPersistent(anchor)) { Add(ValidationSeverity.Error, field + ".Anchor", $"Row {i} ('{point.SpawnId}'): the anchor is a prefab asset, not a scene object (the NPC would spawn at the prefab's root position).", "Assign a transform from this scene.", context); continue; }
                if (anchor.gameObject.scene != location.gameObject.scene) { Add(ValidationSeverity.Error, field + ".Anchor", $"Row {i} ('{point.SpawnId}'): the anchor is in scene '{anchor.gameObject.scene.name}', not in the location's scene '{location.gameObject.scene.name}' (Unity does not save references across scenes).", "Move the anchor into the location's scene.", context); continue; }

                if (nav != null)
                {
                    if (!nav.TrySamplePosition(anchor.position, out var snapped, SurfaceTolerance))
                        Add(ValidationSeverity.Warning, field + ".Anchor", $"Row {i} ('{point.SpawnId}'): the anchor is not on the walkable surface (no walkable point within {SurfaceTolerance:0.0} m).", "Move it onto the walkable ground (the Snap button, or the placement tool).", anchor);
                    else if (origins.Count > 0 && !origins.Any(o => nav.TryCalculatePathLength(o.position, snapped, out _)))
                        Add(ValidationSeverity.Warning, field + ".Anchor", $"Row {i} ('{point.SpawnId}'): the anchor cannot be reached from any arrival point (an island, or a blocked path).", "Move it to ground the party can walk to.", anchor);
                }

                var bounds = location.CameraBoundsXZ;
                if (bounds.HasValue && !bounds.Value.Contains(new Vector2(anchor.position.x, anchor.position.z)))
                    Add(ValidationSeverity.Warning, field + ".Anchor", $"Row {i} ('{point.SpawnId}'): the anchor is outside the camera bounds.", "Move it inside, or widen the bounds.", anchor);

                for (var j = 0; j < i; j++)
                {
                    var other = spawns[j].Anchor;
                    if (other == null || EditorUtility.IsPersistent(other)) continue;
                    var distance = Vector3.Distance(other.position, anchor.position);
                    if (distance < MinSpacing)
                        Add(ValidationSeverity.Warning, field + ".Anchor", $"Rows {j} and {i} ('{spawns[j].SpawnId}' and '{point.SpawnId}'): the anchors are only {distance:0.00} m apart, so the NPCs overlap.", "Move one of them.", anchor);
                }
            }

            ValidateStores(location, results);
            ValidateLoot(location, results);
            return results;
        }

        private static void ValidateStores(LocationContent location, List<ValidationResult> results)
        {
            var seen = new HashSet<string>();
            for (var i = 0; i < location.Stores.Count; i++)
            {
                var store = location.Stores[i];
                var field = $"stores[{i}]";
                Object context = store.Adapter != null ? store.Adapter : location;
                if (string.IsNullOrEmpty(store.StoreId)) results.Add(new ValidationResult(ValidationSeverity.Error, Path(location), field + ".StoreId", $"Store {i}: the store id is empty (every store with an empty id would share one persisted stock).", "Give it a unique id.", context));
                else if (!seen.Add(store.StoreId)) results.Add(new ValidationResult(ValidationSeverity.Error, Path(location), field + ".StoreId", $"Store {i}: store id '{store.StoreId}' is used by more than one store (they would silently share one persisted stock).", "Give each store its own id.", context));
                if (store.Adapter == null) results.Add(new ValidationResult(ValidationSeverity.Warning, Path(location), field + ".Adapter", $"Store {i} ('{store.StoreId}'): no adapter assigned, so nothing in the scene opens this store.", "Assign the TalkInteractionAdapter.", context));
            }
        }

        private static void ValidateLoot(LocationContent location, List<ValidationResult> results)
        {
            var seen = new HashSet<string>();
            for (var i = 0; i < location.LootContainers.Count; i++)
            {
                var loot = location.LootContainers[i];
                var field = $"lootContainers[{i}]";
                Object context = loot.Adapter != null ? loot.Adapter : location;
                if (string.IsNullOrEmpty(loot.ContainerId)) results.Add(new ValidationResult(ValidationSeverity.Error, Path(location), field + ".ContainerId", $"Loot container {i}: the container id is empty (every container with an empty id would share one persisted state).", "Give it a unique id.", context));
                else if (!seen.Add(loot.ContainerId)) results.Add(new ValidationResult(ValidationSeverity.Error, Path(location), field + ".ContainerId", $"Loot container {i}: container id '{loot.ContainerId}' is used by more than one container (they would silently share one persisted state).", "Give each container its own id.", context));
                if (loot.Adapter == null) results.Add(new ValidationResult(ValidationSeverity.Warning, Path(location), field + ".Adapter", $"Loot container {i} ('{loot.ContainerId}'): no adapter assigned, so nothing in the scene opens this container.", "Assign the LootInteractionAdapter.", context));
            }
        }

        /// <summary>The scene name and hierarchy path of the location host, as the finding's "asset path".</summary>
        private static string Path(LocationContent location)
        {
            var names = new List<string>();
            for (var t = location.transform; t != null; t = t.parent) names.Add(t.name);
            names.Reverse();
            return $"{location.gameObject.scene.name}:{string.Join("/", names)}";
        }
    }
}

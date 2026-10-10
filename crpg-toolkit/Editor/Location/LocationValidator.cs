using System.Collections.Generic;
using System.Linq;
using CRPG.Actors;
using CRPG.Dialogue;
using CRPG.Editor.Validation;
using CRPG.Gameplay.Ambient;
using CRPG.Gameplay.Scenes;
using CRPG.Navigation;
using CRPG.Presentation.Animation;
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
        /// <param name="deepChecks">Also run the rules that need path queries, prefab inspection and dialogue graph validation (routine stop reachability, animation cues, the NavMesh motor, the dialogue graph, a body with no collider). The inspector's per-row status dot, which re-validates every 400 ms, passes false.</param>
        public static List<ValidationResult> Validate(LocationContent location, INavMeshService nav, IEnumerable<LocationContent> others = null, IEnumerable<Transform> reachabilityOrigins = null, bool deepChecks = true)
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
            var stopOwners = new Dictionary<Transform, int>();
            var storeOwners = new Dictionary<string, string>();
            for (var s = 0; s < location.Stores.Count; s++)
                if (!string.IsNullOrEmpty(location.Stores[s].StoreId)) storeOwners[location.Stores[s].StoreId] = $"store talk point {s}";
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

                ValidateRoutine(location, i, point, nav, deepChecks, stopOwners, results);
                ValidateDialogue(location, i, point, deepChecks, results);
                ValidateNpcStore(location, i, point, storeOwners, results);

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

        /// <summary>Distance within which a routine stop or wander centre counts as standing on the walkable surface (metres).</summary>
        public const float RoutineSurfaceTolerance = 1.5f;

        private static void ValidateRoutine(LocationContent location, int row, NpcSpawnPoint point, INavMeshService nav, bool deep, Dictionary<Transform, int> stopOwners, List<ValidationResult> results)
        {
            var routine = point.Routine;
            if (routine == null || !routine.IsActive) return;

            var anchor = point.Anchor;
            var who = $"Row {row} ('{point.SpawnId}')";
            var field = $"spawnPoints[{row}].Routine";
            void Add(ValidationSeverity severity, string suffix, string message, string fix, Object context) =>
                results.Add(new ValidationResult(severity, Path(location), field + suffix, $"{who}: {message}", fix, context ?? anchor));

            bool SceneRef(Transform t, string suffix, string what)
            {
                if (EditorUtility.IsPersistent(t)) { Add(ValidationSeverity.Error, suffix, $"{what} is a prefab asset, not a scene object.", "Assign a transform from this scene.", anchor); return false; }
                if (t.gameObject.scene != location.gameObject.scene) { Add(ValidationSeverity.Error, suffix, $"{what} is in scene '{t.gameObject.scene.name}', not in the location's scene.", "Move it into the location's scene.", t); return false; }
                return true;
            }

            void CheckSurface(Vector3 where, string suffix, string what, Object context)
            {
                if (!deep || nav == null) return;
                if (!nav.TrySamplePosition(where, out var snapped, RoutineSurfaceTolerance))
                    Add(ValidationSeverity.Warning, suffix, $"{what} is not on the walkable surface (no walkable point within {RoutineSurfaceTolerance:0.0} m).", "Move it onto walkable ground.", context);
                else if (anchor != null && nav.TrySamplePosition(anchor.position, out var home, SurfaceTolerance) && !nav.TryCalculatePathLength(home, snapped, out _))
                    Add(ValidationSeverity.Warning, suffix, $"{what} cannot be reached from the NPC's anchor (an island, or a blocked path).", "Move it to ground connected to the NPC.", context);
            }

            if (deep) CheckRoutineRig(point, Add);

            if (routine.Kind == NpcRoutineKind.Wander)
            {
                if (routine.AreaRadius <= 0f) Add(ValidationSeverity.Error, ".AreaRadius", "the wander radius must be greater than 0.", "Give the wander area a radius.", anchor);
                else if (routine.AreaRadius < 1f) Add(ValidationSeverity.Warning, ".AreaRadius", $"the wander radius is only {routine.AreaRadius:0.0} m, so the NPC will hardly move.", "Widen the area.", anchor);

                var centre = routine.AreaCentre != null ? routine.AreaCentre : anchor;
                if (routine.AreaCentre != null && !SceneRef(routine.AreaCentre, ".AreaCentre", "the wander centre")) return;
                CheckSurface(centre.position, ".AreaCentre", "the wander centre", centre);
                return;
            }

            // work loop
            var stops = routine.Stops;
            if (stops == null || stops.Count == 0) { Add(ValidationSeverity.Error, ".Stops", "a work loop has no stops.", "Add at least one stop (the Add stop button).", anchor); return; }

            for (var j = 0; j < stops.Count; j++)
            {
                var stop = stops[j];
                var sf = $".Stops[{j}]";
                if (stop == null || stop.Point == null) { Add(ValidationSeverity.Error, sf + ".Point", $"stop {j} has no point.", "Assign a transform or remove the stop.", anchor); continue; }
                if (!SceneRef(stop.Point, sf + ".Point", $"stop {j} ('{stop.Point.name}')")) continue;

                if (stopOwners.TryGetValue(stop.Point, out var owner) && owner != row)
                    Add(ValidationSeverity.Warning, sf + ".Point", $"stop {j} ('{stop.Point.name}') is also a stop of row {owner}: moving it moves both NPCs' work spots.", "Give each NPC its own stop transforms.", stop.Point);
                else stopOwners[stop.Point] = row;

                if (stop.Action == WorkStopAction.PickUp)
                {
                    if (stop.Prop == null) Add(ValidationSeverity.Error, sf + ".Prop", $"stop {j} picks something up but has no prop.", "Assign a Carried Prop asset.", stop.Point);
                    else if (stop.Prop.Prefab == null) Add(ValidationSeverity.Error, sf + ".Prop", $"stop {j}: the prop '{stop.Prop.name}' has no prefab.", "Assign a prefab on the Carried Prop asset.", stop.Point);
                }

                CheckSurface(stop.Point.position, sf + ".Point", $"stop {j} ('{stop.Point.name}')", stop.Point);
                if (deep) CheckCue(point, stop, j, sf, Add);
            }

            // Walk the loop twice: the second pass starts with whatever the first left in the hands, which is what the NPC really does.
            var holding = false;
            for (var pass = 0; pass < 2; pass++)
            {
                for (var j = 0; j < stops.Count; j++)
                {
                    var stop = stops[j];
                    if (stop == null) continue;
                    if (stop.Action == WorkStopAction.PickUp)
                    {
                        if (holding && pass == 1) Add(ValidationSeverity.Error, $".Stops[{j}]", $"stop {j} picks something up while the NPC is still carrying something (a pick-up needs a drop before it in the loop).", "Add a Drop stop before this one.", stop.Point);
                        holding = true;
                    }
                    else if (stop.Action == WorkStopAction.Drop)
                    {
                        if (!holding && pass == 1) Add(ValidationSeverity.Warning, $".Stops[{j}]", $"stop {j} drops something but the NPC is carrying nothing at that point.", "Add a PickUp stop before it, or change this stop.", stop.Point);
                        holding = false;
                    }
                }
            }
        }

        /// <summary>The dialogue rules of one spawn point. Cheap ones always run; the graph validation and the prefab collider check only when <paramref name="deep"/>.</summary>
        private static void ValidateDialogue(LocationContent location, int row, NpcSpawnPoint point, bool deep, List<ValidationResult> results)
        {
            var who = $"Row {row} ('{point.SpawnId}')";
            var field = $"spawnPoints[{row}].Dialogue";
            void Add(ValidationSeverity severity, string message, string fix, Object context) =>
                results.Add(new ValidationResult(severity, Path(location), field, $"{who}: {message}", fix, context ?? point.Anchor));

            var dialogue = point.Dialogue;
            if (dialogue == null)
            {
                if (point.Definition != null && point.Definition.DialogueId.IsValid)
                    Add(ValidationSeverity.Warning, $"the NPC has the dialogue id '{point.Definition.DialogueId.Value}' but this spawn point has no Dialogue: it only talks if the game's ruleset registers that asset.", "Assign the dialogue to the spawn point.", point.Anchor);
                return;
            }

            if (dialogue.Nodes.Count == 0) Add(ValidationSeverity.Warning, $"the dialogue '{dialogue.name}' is empty: the NPC has nothing to say.", "Open the dialogue graph and add a line.", dialogue);
            if (!deep) return;

            if (dialogue.Nodes.Count > 0)
                foreach (var finding in DialogueValidator.Validate(dialogue))
                    Add(finding.Severity, $"dialogue '{dialogue.name}': {finding.Message}", finding.Fix, dialogue);

            foreach (var node in dialogue.Nodes)
                if (node is ActionNode { Action: OpenStoreAction } && (point.Store == null || !point.Store.HasStore))
                    Add(ValidationSeverity.Error, $"dialogue '{dialogue.name}' opens a store, but this spawn point has no shop (the shop screen would get none).", "Add listings to the spawn point's Store, or remove the Open Store action.", dialogue);

            var prefab = point.Definition != null ? point.Definition.VisualPrefab : null;
            if (prefab != null && prefab.GetComponentInChildren<Collider>(true) == null)
                Add(ValidationSeverity.Warning, $"the visual prefab '{prefab.name}' has no collider, so nothing can be clicked to start the conversation.", "Add a collider to the NPC's prefab.", point.Anchor);

            // A Start Quest key that matches no quest asset in the project would silently start nothing. (A quest asset must also be registered in the game's ruleset: the editor cannot know that.)
            var questKeys = dialogue.Nodes.OfType<ActionNode>().Select(n => n.Action).OfType<StartQuestAction>().Select(a => a.QuestKey).Where(k => !string.IsNullOrEmpty(k)).Distinct().ToList();
            if (questKeys.Count > 0)
            {
                var known = new HashSet<string>(AssetDatabase.FindAssets("t:QuestDefinition").Select(g => AssetDatabase.LoadAssetAtPath<CRPG.Quests.QuestDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(q => q != null).Select(q => q.Id.Value));
                foreach (var key in questKeys.Where(k => !known.Contains(k)))
                    Add(ValidationSeverity.Warning, $"dialogue '{dialogue.name}' starts the quest '{key}', which matches no QuestDefinition asset in the project (and a quest must also be registered in the game's ruleset).", "Fix the key, or create and register the quest.", dialogue);
            }
        }

        /// <summary>The shop rules of one spawn point (cheap: no asset scans). The runtime also skips an item its ruleset cannot resolve, which the editor cannot know.</summary>
        private static void ValidateNpcStore(LocationContent location, int row, NpcSpawnPoint point, Dictionary<string, string> storeOwners, List<ValidationResult> results)
        {
            var store = point.Store;
            if (store == null || !store.HasStore) return;

            var who = $"Row {row} ('{point.SpawnId}')";
            var field = $"spawnPoints[{row}].Store";
            void Add(ValidationSeverity severity, string message, string fix) =>
                results.Add(new ValidationResult(severity, Path(location), field, $"{who}: {message}", fix, point.Anchor != null ? point.Anchor : location));

            var id = store.ResolveId(point.SpawnId);
            if (string.IsNullOrEmpty(id)) Add(ValidationSeverity.Error, "the shop has no id (the spawn id is empty too).", "Give the spawn point an id.");
            else if (storeOwners.TryGetValue(id, out var owner)) Add(ValidationSeverity.Error, $"the store id '{id}' is also used by {owner}: they would share one stock.", "Give each shop its own StoreId.");
            else storeOwners[id] = $"row {row}";

            for (var i = 0; i < store.Listings.Count; i++)
            {
                var listing = store.Listings[i];
                if (listing == null || listing.Item == null) { Add(ValidationSeverity.Error, $"listing {i} has no item.", "Assign an item or remove the listing."); continue; }
                if (listing.Item.BaseValue <= 0f) Add(ValidationSeverity.Warning, $"'{listing.Item.name}' is worth nothing (base value 0), so the shop would give it away.", "Set a base value on the item, or stock something else.");
                if (listing.Quantity == 0) Add(ValidationSeverity.Warning, $"listing {i} ('{listing.Item.name}') has stock 0: it is sold out from the start (-1 is unlimited).", "Set a quantity, or -1 for unlimited.");
            }

            if (store.BuyMultiplier < 0f || store.SellMultiplier < 0f) Add(ValidationSeverity.Error, "a price multiplier is negative.", "Use 0 or more.");

            var dialogue = point.Dialogue;
            var opensIt = dialogue != null && dialogue.Nodes.Any(n => n is ActionNode { Action: OpenStoreAction });
            if (dialogue == null) Add(ValidationSeverity.Warning, "this NPC has a shop but no dialogue, so nobody can reach it.", "Give it a dialogue with an Open Store action.");
            else if (!opensIt) Add(ValidationSeverity.Warning, $"this NPC has a shop but the dialogue '{dialogue.name}' never opens it.", "Add an Open Store action to the dialogue.");
        }

        /// <summary>The routine only works on a NavMesh-driven NPC.</summary>
        private static void CheckRoutineRig(NpcSpawnPoint point, System.Action<ValidationSeverity, string, string, string, Object> add)
        {
            var prefab = point.Definition != null ? point.Definition.VisualPrefab : null;
            if (prefab != null && prefab.GetComponentInChildren<NavMeshCharacterMotor>(true) == null)
                add(ValidationSeverity.Warning, "", $"the visual prefab '{prefab.name}' has no NavMeshCharacterMotor, so the routine cannot move this NPC (grid-driven NPCs are not supported).", "Use a NavMesh character prefab.", point.Anchor);
        }

        /// <summary>Warns when a stop names an animation the NPC's profile does not have (it would silently do nothing), or a looping one without the stance set that holds it.</summary>
        private static void CheckCue(NpcSpawnPoint point, WorkStop stop, int index, string field, System.Action<ValidationSeverity, string, string, string, Object> add)
        {
            if (!stop.CueId.IsValid) return;
            var prefab = point.Definition != null ? point.Definition.VisualPrefab : null;
            var animator = prefab != null ? prefab.GetComponentInChildren<CharacterPlayableAnimator>(true) : null;
            if (animator == null) return;

            var profile = new SerializedObject(animator).FindProperty("profile")?.objectReferenceValue as CharacterAnimationProfile;
            if (profile == null) { add(ValidationSeverity.Warning, field + ".CueId", $"stop {index} names the cue '{stop.CueId}' but the NPC's animator has no animation profile.", "Assign a profile on the prefab's CharacterPlayableAnimator.", stop.Point); return; }

            if (!profile.TryGetCue(stop.CueId, out var entry))
                add(ValidationSeverity.Warning, field + ".CueId", $"stop {index}: the cue '{stop.CueId}' is not in '{profile.name}', so nothing will play.", "Add the cue to the animation profile or pick another.", stop.Point);
            else if (entry.Loop && !profile.TryGetStanceLocomotion(stop.CueId, out _, out _))
                add(ValidationSeverity.Warning, field + ".CueId", $"stop {index}: '{stop.CueId}' is a looping cue, which is held as a stance, but '{profile.name}' has no stance set with that id (the NPC would just idle).", "Add a Stance Locomotion Set with this id, or make the cue a one-shot.", stop.Point);
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

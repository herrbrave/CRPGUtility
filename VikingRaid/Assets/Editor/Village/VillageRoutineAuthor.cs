using System.Collections.Generic;
using System.IO;
using System.Linq;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Editor.Location;
using CRPG.Gameplay.Ambient;
using CRPG.Gameplay.Scenes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace VikingRaid.EditorTools
{
    /// <summary>
    /// Gives the three demo villagers something to do (design <c>Docs/Plans/Toolkit-NpcRoutines.md</c>, Section 5): Orm works the dock, carrying a sack from the pile to the cart; Hild wanders the
    /// square; the elder potters about near his door. Idempotent and polite: a villager whose routine is already set (by hand, in the inspector) is left alone. Also adds the work animation cue to the
    /// shared hero profile and the carried sack asset.
    /// </summary>
    public static class VillageRoutineAuthor
    {
        public const string ScenePath = "Assets/Scenes/VikingVillage.unity";
        public const string ProfilePath = "Assets/Characters/Animation/HeroAnimationProfile.asset";
        public const string AnimationFbx = "Assets/ThirdParty/UAL/UAL1_Standard.fbx";
        public const string PropFolder = "Assets/Content/Village/Props";
        public const string SackPropPath = PropFolder + "/Prop_Sack.asset";
        public const string SackPrefabPath = "Assets/Synty/PolygonVikingRealm/Prefabs/Props/SM_Prop_Sack_02.prefab";
        public const string PickUpCue = "work-pickup";

        // The dock: a sack pile at the south end, the cart four metres up the quay. Stand-in spots are snapped onto the NavMesh; yaw is the way the worker faces while he works.
        private static readonly (Vector3 at, float yaw, WorkStopAction action, string label)[] DockStops =
        {
            (new Vector3(17.3f, -0.8f, 31.0f), 90f, WorkStopAction.PickUp, "pile"),
            (new Vector3(17.0f, -0.8f, 38.4f), 270f, WorkStopAction.Drop, "cart"),
        };

        [MenuItem("Viking Raid/Village/2. Author NPC Routines")]
        public static void Run()
        {
            foreach (var line in Author()) Debug.Log("[VillageRoutineAuthor] " + line);
        }

        public static List<string> Author()
        {
            var report = new List<string>();
            EnsureWorkCue(report);

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var sack = EnsureSackProp(report);   // after OpenScene: opening a scene unloads assets nothing references, which turns an earlier load into a fake null
            var location = Object.FindFirstObjectByType<LocationContent>();
            if (location == null) { report.Add("ERROR: the village scene has no LocationContent: run '1. Author Village Scene' first."); return report; }

            Configure(location, "village-orm", report, index => ConfigureDockWorker(location, index, sack, report));
            Configure(location, "village-hild", report, index => ConfigureWanderer(location, index, 7f, 3f, 8f));
            Configure(location, "village-elder", report, index => ConfigureWanderer(location, index, 3.5f, 5f, 12f));

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            report.Add("scene saved");
            return report;
        }

        private static void Configure(LocationContent location, string spawnId, List<string> report, System.Action<int> apply)
        {
            var index = location.SpawnPoints.ToList().FindIndex(p => p.SpawnId == spawnId);
            if (index < 0) { report.Add($"{spawnId}: no such spawn point; skipped."); return; }
            if (location.SpawnPoints[index].Routine.IsActive) { report.Add($"{spawnId}: already has a routine ({location.SpawnPoints[index].Routine.Kind}); kept."); return; }
            apply(index);
        }

        private static void ConfigureWanderer(LocationContent location, int index, float radius, float pauseMin, float pauseMax)
        {
            var so = new SerializedObject(location);
            var routine = so.FindProperty("spawnPoints").GetArrayElementAtIndex(index).FindPropertyRelative("Routine");
            routine.FindPropertyRelative("Kind").enumValueIndex = (int)NpcRoutineKind.Wander;
            routine.FindPropertyRelative("AreaRadius").floatValue = radius;
            routine.FindPropertyRelative("PauseMin").floatValue = pauseMin;
            routine.FindPropertyRelative("PauseMax").floatValue = pauseMax;
            so.ApplyModifiedProperties();
        }

        private static void ConfigureDockWorker(LocationContent location, int index, CarriedPropDefinition sack, List<string> report)
        {
            var so = new SerializedObject(location);
            so.FindProperty("spawnPoints").GetArrayElementAtIndex(index).FindPropertyRelative("Routine").FindPropertyRelative("Kind").enumValueIndex = (int)NpcRoutineKind.WorkLoop;
            so.ApplyModifiedProperties();

            var spawnId = location.SpawnPoints[index].SpawnId;
            for (var i = 0; i < DockStops.Length; i++)
            {
                var (at, yaw, action, label) = DockStops[i];
                var stop = LocationPlacement.AddStop(location, index);
                stop.name = $"Stop_{spawnId}_{label}";
                var spot = NavMesh.SamplePosition(at, out var hit, 2f, NavMesh.AllAreas) ? hit.position : at;
                if (spot == at) report.Add($"WARNING: no walkable ground within 2 m of the {label} stop at {at}: move '{stop.name}' by hand.");
                stop.SetPositionAndRotation(spot, Quaternion.Euler(0f, yaw, 0f));

                var so2 = new SerializedObject(location);
                var entry = so2.FindProperty("spawnPoints").GetArrayElementAtIndex(index).FindPropertyRelative("Routine").FindPropertyRelative("Stops").GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("Action").enumValueIndex = (int)action;
                entry.FindPropertyRelative("Seconds").floatValue = 1.5f;
                entry.FindPropertyRelative("CueId").FindPropertyRelative("value").stringValue = PickUpCue;
                entry.FindPropertyRelative("Prop").objectReferenceValue = action == WorkStopAction.PickUp ? sack : null;
                so2.ApplyModifiedProperties();
            }

            // A dock worker lives on the dock: the demo put Orm beside the party start, which is a long walk round the hill from the quay. He starts between his two stops.
            var anchor = location.SpawnPoints[index].Anchor;
            var middle = (DockStops[0].at + DockStops[DockStops.Length - 1].at) * 0.5f;
            if (anchor != null && !EditorUtility.IsPersistent(anchor) && NavMesh.SamplePosition(middle, out var home, 2f, NavMesh.AllAreas))
            {
                anchor.SetPositionAndRotation(home.position, Quaternion.Euler(0f, 90f, 0f));
                report.Add($"{spawnId}: anchor moved to the dock at {home.position}");
            }

            report.Add($"{spawnId}: dock worker, {DockStops.Length} stops");
        }

        private static void EnsureWorkCue(List<string> report)
        {
            var profile = AssetDatabase.LoadAssetAtPath<CharacterAnimationProfile>(ProfilePath);
            if (profile == null) { report.Add($"ERROR: {ProfilePath} not found."); return; }
            var hasCue = profile.TryGetCue(new DefinitionId(PickUpCue), out var existing);
            if (hasCue && existing.Clip != null && !existing.Clip.name.StartsWith("__preview__")) { report.Add($"cue '{PickUpCue}' already in the hero profile"); return; }

            // The FBX importer also adds a "__preview__<name>" copy of every clip: the real one is the other.
            var clip = AssetDatabase.LoadAllAssetsAtPath(AnimationFbx).OfType<AnimationClip>().FirstOrDefault(c => c.name == "Armature|PickUp_Table");
            if (clip == null) { report.Add($"ERROR: no PickUp_Table clip in {AnimationFbx}."); return; }

            var so = new SerializedObject(profile);
            var cues = so.FindProperty("cues");
            SerializedProperty entry = null;
            for (var i = 0; i < cues.arraySize && hasCue; i++)
                if (cues.GetArrayElementAtIndex(i).FindPropertyRelative("cueId").FindPropertyRelative("value").stringValue == PickUpCue) entry = cues.GetArrayElementAtIndex(i);
            if (entry == null)
            {
                cues.InsertArrayElementAtIndex(cues.arraySize);
                entry = cues.GetArrayElementAtIndex(cues.arraySize - 1);
            }

            entry.FindPropertyRelative("cueId").FindPropertyRelative("value").stringValue = PickUpCue;
            entry.FindPropertyRelative("clip").objectReferenceValue = clip;
            entry.FindPropertyRelative("crossfadeDuration").floatValue = 0.2f;
            entry.FindPropertyRelative("loop").boolValue = false;
            entry.FindPropertyRelative("referenceSpeed").floatValue = 0f;
            so.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
            report.Add($"added cue '{PickUpCue}' ({clip.name}, {clip.length:0.00} s) to the hero profile");
        }

        private static CarriedPropDefinition EnsureSackProp(List<string> report)
        {
            var existing = AssetDatabase.LoadAssetAtPath<CarriedPropDefinition>(SackPropPath);
            if (existing != null) { report.Add("sack prop already authored; kept (its grip is hand-tuned)"); return existing; }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SackPrefabPath);
            if (prefab == null) report.Add($"WARNING: {SackPrefabPath} is missing (is the Synty Viking Realm package imported?): the sack has no prefab.");

            Directory.CreateDirectory(PropFolder);
            var prop = ScriptableObject.CreateInstance<CarriedPropDefinition>();
            // A sack on the right shoulder, the classic dock-worker carry. The grip is tuned from renders (VillageRoutineTests.CaptureTheDockWorker).
            prop.Configure(prefab, HumanBodyBones.RightShoulder, new Vector3(0f, 0.15f, 0f), new Vector3(0f, 0f, 90f), 1f);
            AssetDatabase.CreateAsset(prop, SackPropPath);
            AssetDatabase.SaveAssets();
            report.Add("created " + SackPropPath);
            return prop;
        }
    }
}

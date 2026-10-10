using System.Linq;
using CRPG.Actors;
using CRPG.Core;
using UnityEditor;
using UnityEngine;

namespace VikingRaid.Appearance.Editor
{
    /// <summary>Diagnostic: which way does the body face (hip line against the root's forward, the definition <c>GameFacingTests</c> uses) for each skeleton-only master, unposed and with each idle clip sampled? Logs to the Editor log.</summary>
    public static class FacingProbe
    {
        private static float HipYaw(GameObject go)
        {
            var animator = go.GetComponentsInChildren<Animator>(true).First(a => a.avatar != null);
            var side = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg).position - animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg).position;
            side.y = 0f;
            var forward = go.transform.forward; forward.y = 0f;
            return Vector3.SignedAngle(forward.normalized, Vector3.Cross(side.normalized, Vector3.up).normalized, Vector3.up);
        }

        private static AnimationClip Idle(string profilePath)
        {
            var profile = AssetDatabase.LoadAssetAtPath<CharacterAnimationProfile>(profilePath);
            return profile != null && profile.TryGetCue(new DefinitionId("anim-idle"), out var cue) ? cue.Clip : null;
        }

        public static void Run()
        {
            var masters = new (string name, string path)[]
            {
                ("HumanMaster", HumanCatalogAuthor.MasterPath),
                ("GoblinMaster", "Assets/Appearance/Goblin/GoblinMaster.prefab"),
                ("VikingBody (Polygon)", "Assets/Synty/PolygonVikingRealm/Prefabs/Characters/SM_Chr_Warrior_Male_01.prefab"),
            };
            var clips = new (string name, AnimationClip clip)[] { ("hero idle (UAL)", Idle("Assets/Characters/Animation/HeroAnimationProfile.asset")), ("goblin idle", Idle("Assets/Characters/Animation/GoblinAnimationProfile.asset")) };
            foreach (var (name, path) in masters)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) { Debug.Log($"[Facing] {name}: missing"); continue; }
                var go = Object.Instantiate(prefab);
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                Debug.Log($"[Facing] {name}: unposed hip yaw {HipYaw(go):F1}");
                foreach (var (clipName, clip) in clips)
                {
                    clip.SampleAnimation(go, 0.25f);
                    var rootAfter = go.transform.eulerAngles.y;
                    go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    Debug.Log($"[Facing] {name} + {clipName}: hip yaw {HipYaw(go):F1} (the clip turned the root by {rootAfter:F1})");
                    go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                }

                Object.DestroyImmediate(go);
            }
        }
    }
}

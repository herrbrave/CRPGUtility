using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Gameplay;
using CRPG.Navigation;
using CRPG.Presentation.Animation;
using NUnit.Framework;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using VikingRaid.Game;

namespace VikingRaid.Tests
{
    /// <summary>Task 1 gate: every authored character prefab (Assets/Characters/Prefabs) is a working, ARMED wrapper. Each one is spawned on a
    /// real baked NavMesh and driven through the real animator: structure, rig import settings, weapons attached to a hand, standing and
    /// facing forward, every action cue completing, death lying flat, recovery. Also writes roster contact sheets (idle and mid-attack) to
    /// VikingRaid_Logs/spikes for visual review -- numbers cannot tell a weapon pointing through the palm from a good grip.</summary>
    public class RosterPrefabTests
    {
        private const int CellW = 256, CellH = 320, Cols = 5;
        private static string OutDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "VikingRaid_Logs", "spikes"));

        private readonly List<GameObject> spawned = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in spawned) if (go != null) Object.DestroyImmediate(go);
            spawned.Clear();
            NavMesh.RemoveAllNavMeshData();
        }

        private static IEnumerator Wait(float seconds)
        {
            var elapsed = 0f;
            do { elapsed += Time.deltaTime; yield return null; } while (elapsed < seconds);
        }

        private static List<string> PrefabPaths()
        {
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Characters/Prefabs" })
                .Select(UnityEditor.AssetDatabase.GUIDToAssetPath).OrderBy(p => p).ToList();
#else
            return new List<string>();
#endif
        }

        private static GameObject Load(string path)
        {
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
#else
            return null;
#endif
        }

        private static Texture2D Render(Camera cam, RenderTexture rt)
        {
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, request)) RenderPipeline.SubmitRenderRequest(cam, request); else cam.Render();
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            return tex;
        }

        [UnityTest]
        public IEnumerator EveryRosterPrefabIsAnArmedWorkingCharacter()
        {
#if UNITY_EDITOR
            UnityEditor.EditorSettings.asyncShaderCompilation = false;
#endif
            var paths = PrefabPaths();
            Assert.That(paths.Count, Is.GreaterThanOrEqualTo(15), "expected the full roster under Assets/Characters/Prefabs -- run 'Viking Raid/Task 1/Author Characters'");

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.localScale = new Vector3(8f, 1f, 8f);
            ground.isStatic = true;
            spawned.Add(ground);
            var surface = new GameObject("Surface").AddComponent<NavMeshSurface>();
            spawned.Add(surface.gameObject);
            surface.BuildNavMesh();
            var light = new GameObject("Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(40f, 150f, 0f);
            spawned.Add(light.gameObject);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.6f, 0.6f, 0.65f);

            var camGo = new GameObject("RosterCam");
            spawned.Add(camGo);
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.22f, 0.25f, 0.3f);
            cam.fieldOfView = 30f;
            var rt = new RenderTexture(CellW, CellH, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            cam.targetTexture = rt;

            var rows = Mathf.CeilToInt(paths.Count / (float)Cols);
            var idleSheet = new Texture2D(Cols * CellW, rows * CellH, TextureFormat.RGBA32, false);
            var attackSheet = new Texture2D(Cols * CellW, rows * CellH, TextureFormat.RGBA32, false);
            var failures = new List<string>();
            var warmed = false;

            for (var index = 0; index < paths.Count; index++)
            {
                var path = paths[index];
                var id = Path.GetFileNameWithoutExtension(path);
                var prefab = Load(path);
                var instance = Object.Instantiate(prefab, Vector3.zero, Quaternion.identity);
                spawned.Add(instance);
                yield return null;
                yield return null;

                void Fail(string what) => failures.Add($"{id}: {what}");

                var animator = instance.GetComponent<CharacterPlayableAnimator>();
                var cues = instance.GetComponent<ModelActionCues>();
                var navAgent = instance.GetComponent<NavMeshAgent>();
                if (instance.GetComponent<CharacterAgent>() == null) Fail("no CharacterAgent");
                if (instance.GetComponent<FacingController>() == null) Fail("no FacingController");
                if (cues == null) Fail("no ModelActionCues"); else if (cues.HitPointResourceId != new DefinitionId("resource-hp")) Fail("ModelActionCues has no hit-point resource id");
                if (navAgent == null || navAgent.speed <= 0f) Fail("no NavMeshAgent / zero speed");

                // Click-to-move and the ability targeting gate raycast ONLY the injected hittable mask: a character outside it can never be clicked or targeted.
                var offLayer = instance.GetComponentsInChildren<Transform>(true).Count(t => t.gameObject.layer != WorldLayers.Character);
                if (offLayer > 0) Fail($"{offLayer} objects are not on the Character layer");
                Physics.SyncTransforms();
                var chest = instance.transform.position + Vector3.up * 1f;
                if (!Physics.Raycast(chest + Vector3.up * 4f, Vector3.down, out var clickHit, 8f, WorldLayers.Hittable, QueryTriggerInteraction.Ignore) || !clickHit.collider.transform.IsChildOf(instance.transform))
                    Fail("a ray through the hittable mask does not hit this character (it cannot be clicked or targeted)");
                if (animator == null || !animator.IsPresentationBound) { Fail("animator not bound to the Model"); Object.DestroyImmediate(instance); continue; }

                var modelAnimator = instance.GetComponentsInChildren<Animator>().FirstOrDefault(a => a.avatar != null);
                if (modelAnimator == null || !modelAnimator.avatar.isHuman) { Fail("Model has no Humanoid Animator avatar"); Object.DestroyImmediate(instance); continue; }
                if (modelAnimator.applyRootMotion) Fail("Model Animator still applies root motion");
                if (modelAnimator.cullingMode != AnimatorCullingMode.AlwaysAnimate) Fail("Model Animator can cull animation off-screen");
                foreach (var extra in instance.GetComponentsInChildren<Animator>().Where(a => a != modelAnimator)) Fail($"extra Animator on '{extra.name}' (a weapon's?)");
                foreach (var collider in instance.GetComponentsInChildren<Collider>().Where(c => c.transform != instance.transform)) Fail($"collider on '{collider.name}' under the Model (weapons must be collider-free)");

                var rightHand = modelAnimator.GetBoneTransform(HumanBodyBones.RightHand);
                var leftHand = modelAnimator.GetBoneTransform(HumanBodyBones.LeftHand);
                var weapons = instance.GetComponentsInChildren<Transform>().Where(t => t.name.StartsWith("Weapon_")).ToList();
                if (weapons.Count == 0) Fail("no weapon attached");
                foreach (var weapon in weapons)
                {
                    var hand = weapon.name == "Weapon_R" ? rightHand : leftHand;
                    if (!weapon.IsChildOf(hand)) Fail($"{weapon.name} is not under the {(weapon.name == "Weapon_R" ? "right" : "left")} hand");
                    var renderers = weapon.GetComponentsInChildren<Renderer>();
                    if (renderers.Length == 0) { Fail($"{weapon.name} has no renderer"); continue; }
                    var bounds = renderers[0].bounds;
                    foreach (var r in renderers) bounds.Encapsulate(r.bounds);
                    if (Vector3.Distance(bounds.center, hand.position) > 1.4f) Fail($"{weapon.name} floats {Vector3.Distance(bounds.center, hand.position):F2} m from its hand");
                }

                var head = modelAnimator.GetBoneTransform(HumanBodyBones.Head);
                var leftFoot = modelAnimator.GetBoneTransform(HumanBodyBones.LeftFoot);
                var rightFoot = modelAnimator.GetBoneTransform(HumanBodyBones.RightFoot);
                var leftShoulder = modelAnimator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                var rightShoulder = modelAnimator.GetBoneTransform(HumanBodyBones.RightUpperArm);
                float HeadHeight() => head.position.y - Mathf.Min(leftFoot.position.y, rightFoot.position.y);
                float Facing()
                {
                    var side = rightShoulder.position - leftShoulder.position; side.y = 0f;
                    return Vector3.Dot(side.normalized, instance.transform.right);
                }

                yield return Wait(1.2f);
                var standing = HeadHeight();
                if (standing < 0.8f) Fail($"idle pose is too low ({standing:F2} m head above feet)");
                if (Facing() < 0.7f) Fail($"does not face forward at idle (facing {Facing():F2})");

                // camera: framed on the whole body, further back for tall characters
                var focus = new Vector3(0f, 0.95f, 0f);
                camGo.transform.position = focus + new Vector3(0f, 0.3f, 5.2f);
                camGo.transform.LookAt(focus);
                if (!warmed) { Object.DestroyImmediate(Render(cam, rt)); warmed = true; yield return null; } // first shader use renders white
                var idleTex = Render(cam, rt);
                idleSheet.SetPixels((index % Cols) * CellW, (rows - 1 - index / Cols) * CellH, CellW, CellH, idleTex.GetPixels());
                Object.DestroyImmediate(idleTex);

                // every combat cue completes (and the attack is captured mid-swing)
                foreach (var cue in new[] { "anim-attack-melee", "anim-shoot", "anim-hit" })
                {
                    AnimationOutcome? outcome = null;
                    var token = animator.RequestAction(new DefinitionId(cue), o => outcome = o);
                    if (!token.IsValid) { Fail($"cue {cue} rejected"); continue; }
                    var elapsed = 0f;
                    var captured = false;
                    while (outcome == null && elapsed < 4f)
                    {
                        elapsed += Time.deltaTime;
                        yield return null;
                        if (cue == "anim-attack-melee" && !captured && elapsed > 0.3f)
                        {
                            captured = true;
                            var attackTex = Render(cam, rt);
                            attackSheet.SetPixels((index % Cols) * CellW, (rows - 1 - index / Cols) * CellH, CellW, CellH, attackTex.GetPixels());
                            Object.DestroyImmediate(attackTex);
                        }
                    }

                    if (outcome != AnimationOutcome.Completed) Fail($"cue {cue} never completed");
                }

                animator.RequestTerminal(new DefinitionId("anim-death"));
                yield return Wait(2.5f);
                if (HeadHeight() > standing * 0.45f) Fail($"death pose is not lying down (head {HeadHeight():F2} m vs standing {standing:F2})");
                animator.Recover();
                yield return Wait(1.3f);
                if (HeadHeight() < standing * 0.7f) Fail($"did not stand back up after Recover ({HeadHeight():F2} vs {standing:F2})");
                if (Facing() < 0.7f) Fail($"faces the wrong way after Recover (facing {Facing():F2})");

                Debug.Log($"[Task1] {id}: standing head {standing:F2} m, weapons {weapons.Count}, avatar human");
                Object.DestroyImmediate(instance);
                yield return null;
            }

            Directory.CreateDirectory(OutDir);
            idleSheet.Apply();
            attackSheet.Apply();
            File.WriteAllBytes(Path.Combine(OutDir, "roster_idle.png"), idleSheet.EncodeToPNG());
            File.WriteAllBytes(Path.Combine(OutDir, "roster_attack.png"), attackSheet.EncodeToPNG());
            File.WriteAllText(Path.Combine(OutDir, "roster_order.txt"), string.Join("\n", paths.Select((p, i) => $"{i} r{i / Cols}c{i % Cols} {Path.GetFileNameWithoutExtension(p)}")));
            Object.DestroyImmediate(idleSheet);
            Object.DestroyImmediate(attackSheet);
            cam.targetTexture = null;
            rt.Release();
            Object.DestroyImmediate(rt);

            Assert.IsEmpty(failures, string.Join("\n", failures));
        }
    }
}

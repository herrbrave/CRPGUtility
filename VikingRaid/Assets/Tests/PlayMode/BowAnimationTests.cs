using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Presentation.Animation;
using NUnit.Framework;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;

namespace VikingRaid.Tests
{
    /// <summary>The Blender-authored archer clips on the real Synty characters, through the real animator: Bow_Idle for the hero archer, Bow_Draw
    /// for all archers. Judged numerically (a draw can look fine from one camera and still aim 20 degrees off): the LEGS keep facing forward (the
    /// chest turns ~45 degrees on purpose, so the shoulder line cannot be the facing test), the arrow line (string hand to bow hand) points along
    /// the character's forward at full draw, the string hand sits at the jaw, the bow arm is extended, and the bow stays near-vertical. A side
    /// strip is written to VikingRaid_Logs/spikes for visual review.</summary>
    public class BowAnimationTests
    {
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

        private static GameObject Load(string path)
        {
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
#else
            return null;
#endif
        }

        private sealed class Sample
        {
            public float T;
            public float LegsFacing, RightHandToHead, LeftHandForward, ArrowDot, BowVertical, BowArmReach;
            public Quaternion HandRot;
        }

        [UnityTest] public IEnumerator HeroArcher() => Scenario("Hero_Sigrid", expectBowIdle: true);
        [UnityTest] public IEnumerator GoblinArcher() => Scenario("Goblin_Archer_M", expectBowIdle: false);

        private IEnumerator Scenario(string prefabName, bool expectBowIdle)
        {
#if UNITY_EDITOR
            UnityEditor.EditorSettings.asyncShaderCompilation = false;
#endif
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

            var prefab = Load($"Assets/Characters/Prefabs/{prefabName}.prefab");
            Assert.IsNotNull(prefab, $"{prefabName}.prefab missing -- run 'Viking Raid/Task 1/Author Characters'");
            var instance = Object.Instantiate(prefab, Vector3.zero, Quaternion.identity);
            spawned.Add(instance);
            yield return null;
            yield return null;

            var animator = instance.GetComponent<CharacterPlayableAnimator>();
            Assert.IsTrue(animator.IsPresentationBound);
            var model = instance.GetComponentsInChildren<Animator>().First(a => a.avatar != null);
            Transform Bone(HumanBodyBones b) => model.GetBoneTransform(b);
            var rightHand = Bone(HumanBodyBones.RightHand);
            var leftHand = Bone(HumanBodyBones.LeftHand);
            var head = Bone(HumanBodyBones.Head);
            var chest = Bone(HumanBodyBones.UpperChest) ?? Bone(HumanBodyBones.Chest);
            var leftLeg = Bone(HumanBodyBones.LeftUpperLeg);
            var rightLeg = Bone(HumanBodyBones.RightUpperLeg);
            var leftShoulder = Bone(HumanBodyBones.LeftUpperArm);
            var bow = instance.GetComponentsInChildren<Transform>().First(t => t.name == "Weapon_L");

            yield return Wait(1.5f);
            var idleName = animator.CurrentClip != null ? animator.CurrentClip.name : "none";
            Debug.Log($"[Bow:{prefabName}] idle clip = {idleName}");
            if (expectBowIdle) StringAssert.Contains("Bow_Idle", idleName, "the hero archer should idle in the bow-ready stance");
            else StringAssert.DoesNotContain("Bow", idleName, "goblin archers keep their own idle");

            // camera for the strip: from the character's right side (+X in the world; characters face +Z)
            var camGo = new GameObject("BowCam");
            spawned.Add(camGo);
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.22f, 0.25f, 0.3f);
            cam.fieldOfView = 30f;
            camGo.transform.position = new Vector3(5.2f, 1.2f, 0.4f);
            camGo.transform.LookAt(new Vector3(0f, 1.1f, 0.4f));
            var rt = new RenderTexture(300, 360, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            cam.targetTexture = rt;
            Texture2D Shot()
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

            Object.DestroyImmediate(Shot()); // first shader use renders white
            yield return null;

            AnimationOutcome? outcome = null;
            var token = animator.RequestAction(new DefinitionId("anim-shoot"), o => outcome = o);
            Assert.IsTrue(token.IsValid, "anim-shoot rejected");
            var shotTimes = new[] { 0.10f, 0.30f, 0.55f, 0.85f, 1.00f, 1.15f, 1.35f };
            var shots = new List<Texture2D>();
            var samples = new List<Sample>();
            var t = 0f;
            var nextShot = 0;
            while (outcome == null && t < 4f)
            {
                t += Time.deltaTime;
                yield return null;

                var forward = instance.transform.forward;
                var right = instance.transform.right;
                var legs = rightLeg.position - leftLeg.position; legs.y = 0f;
                var arrow = leftHand.position - rightHand.position; arrow.y = 0f;
                var boxBowAxis = bow.TransformDirection(Vector3.up);
                samples.Add(new Sample
                {
                    T = t,
                    LegsFacing = Vector3.Dot(legs.normalized, right),
                    RightHandToHead = Vector3.Distance(rightHand.position, head.position),
                    LeftHandForward = Vector3.Dot(leftHand.position - chest.position, forward),
                    ArrowDot = Vector3.Dot(arrow.normalized, forward),
                    BowVertical = Mathf.Abs(boxBowAxis.normalized.y),
                    BowArmReach = Vector3.Distance(leftHand.position, leftShoulder.position),
                    HandRot = leftHand.rotation,
                });

                if (nextShot < shotTimes.Length && t >= shotTimes[nextShot]) { shots.Add(Shot()); nextShot++; }
            }

            Assert.AreEqual(AnimationOutcome.Completed, outcome, "the draw never completed");
            // the hold: the clip is 1.5 s; frames 19-28 are 0.60-0.90 s in (the crossfade adds a little)
            var hold = samples.Where(s => s.T >= 0.70f && s.T <= 0.90f).ToList();
            Assert.IsNotEmpty(hold);
            float Avg(System.Func<Sample, float> f) => hold.Average(f);
            Debug.Log($"[Bow:{prefabName}] duration {t:F2}s; HOLD legsFacing={Avg(s => s.LegsFacing):F2} rightHandToHead={Avg(s => s.RightHandToHead):F2} " +
                      $"leftHandForward={Avg(s => s.LeftHandForward):F2} arrowDot={Avg(s => s.ArrowDot):F2} bowVertical={Avg(s => s.BowVertical):F2} bowArmReach={Avg(s => s.BowArmReach):F2}; " +
                      $"min legsFacing over clip={samples.Min(s => s.LegsFacing):F2}");

            // Calibration: the grip rotation that would make the bow exactly vertical at full draw (weapon local Y = up, belly along +-forward).
            var mid = hold.OrderBy(s => Mathf.Abs(s.T - 0.8f)).First();
            foreach (var sign in new[] { 1f, -1f })
            {
                var want = Quaternion.LookRotation(instance.transform.forward * sign, Vector3.up);
                var grip = Quaternion.Inverse(mid.HandRot) * want;
                Debug.Log($"[Bow:{prefabName}] CALIBRATION belly {(sign > 0 ? "forward" : "toward archer")}: grip euler = {grip.eulerAngles:F0} (current {bow.localEulerAngles:F0})");
            }

            Directory.CreateDirectory(OutDir);
            var strip = new Texture2D(300 * shots.Count, 360, TextureFormat.RGBA32, false);
            for (var i = 0; i < shots.Count; i++) strip.SetPixels(i * 300, 0, 300, 360, shots[i].GetPixels());
            strip.Apply();
            File.WriteAllBytes(Path.Combine(OutDir, $"bow_{prefabName}_side.png"), strip.EncodeToPNG());
            foreach (var s in shots) Object.DestroyImmediate(s);
            Object.DestroyImmediate(strip);
            cam.targetTexture = null;
            rt.Release();
            Object.DestroyImmediate(rt);

            Assert.That(samples.Min(s => s.LegsFacing), Is.GreaterThan(0.9f), "the whole body turned during the draw (legs left the facing direction): root rotation is wrong");
            Assert.That(Avg(s => s.ArrowDot), Is.GreaterThan(0.93f), "the arrow line (string hand -> bow hand) does not point along the character's forward");
            Assert.That(Avg(s => s.LeftHandForward), Is.GreaterThan(0.4f), "the bow arm is not extended forward at full draw");
            Assert.That(Avg(s => s.RightHandToHead), Is.LessThan(0.30f), "the string hand is not drawn to the face");
            Assert.That(Avg(s => s.BowVertical), Is.GreaterThan(0.8f), "the bow is not near-vertical at full draw");
        }
    }
}

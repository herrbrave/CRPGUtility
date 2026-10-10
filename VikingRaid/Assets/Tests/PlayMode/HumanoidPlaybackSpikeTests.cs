using System.Collections;
using System.Collections.Generic;
using System.IO;
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
    /// <summary>Task 0 spike (b)/(c): the plan review's go/no-go. Plays Humanoid clips (neutral UAL locomotion + UAL combat clips for
    /// Vikings; Synty goblin locomotion + the same UAL combat clips for goblins) on Humanoid Synty characters through the REAL
    /// CharacterPlayableAnimator + PlayableCharacterAnimationDriver with continuous graph evaluation, and asserts the pose stays
    /// standing / actually moves / lies down on death / recovers. A single-frame sample or an AnimatorController would not count --
    /// the toolkit's own validator warns Humanoid avatars once corrupted this driver (that was Generic clips on a Humanoid Animator).
    /// Posture is judged from head height above the lowest foot, not from the Humanoid Hips bone: the Polygon rigs map Hips to a
    /// bone named "Root".</summary>
    public class HumanoidPlaybackSpikeTests
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

        private sealed class Rig
        {
            public CharacterPlayableAnimator Animator;
            public Animator ModelAnimator;
            public Transform Root, Head, RightHand, LeftFoot, RightFoot, LeftShoulder, RightShoulder;
            public float StandingHead;

            public float LowestFootY => Mathf.Min(LeftFoot.position.y, RightFoot.position.y);
            public float HeadHeight => Head.position.y - LowestFootY;
        }

        private IEnumerator Build(string wrapperName, System.Action<Rig> done)
        {
#if UNITY_EDITOR
            // Shader-graph materials compile asynchronously in the Editor and render as a flat white placeholder until
            // done; batch frames outrun real time, so a captured frame can land before that. Synchronous for the test.
            UnityEditor.EditorSettings.asyncShaderCompilation = false;
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Spike/Wrapper_{wrapperName}.prefab");
#else
            GameObject prefab = null;
#endif
            Assert.IsNotNull(prefab, $"Wrapper_{wrapperName}.prefab missing -- run 'Viking Raid/Spikes/Author Animation Spike'.");

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
            RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.6f);

            var instance = Object.Instantiate(prefab, Vector3.zero, Quaternion.identity);
            spawned.Add(instance);
            yield return null; // CharacterView.Start() fallback binds a nested-prefab Model one frame later
            yield return null;

            var rig = new Rig { Animator = instance.GetComponent<CharacterPlayableAnimator>(), Root = instance.transform };
            Assert.IsNotNull(rig.Animator, "wrapper has no CharacterPlayableAnimator");
            Assert.IsTrue(rig.Animator.IsPresentationBound, "presentation graph never bound to the Model's Animator");

            rig.ModelAnimator = instance.GetComponentInChildren<Animator>();
            Assert.IsNotNull(rig.ModelAnimator.avatar, "Model Animator has no avatar");
            Assert.IsTrue(rig.ModelAnimator.avatar.isHuman, "Model avatar is not Humanoid");
            rig.Head = rig.ModelAnimator.GetBoneTransform(HumanBodyBones.Head);
            rig.RightHand = rig.ModelAnimator.GetBoneTransform(HumanBodyBones.RightHand);
            rig.LeftFoot = rig.ModelAnimator.GetBoneTransform(HumanBodyBones.LeftFoot);
            rig.RightFoot = rig.ModelAnimator.GetBoneTransform(HumanBodyBones.RightFoot);
            rig.LeftShoulder = rig.ModelAnimator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            rig.RightShoulder = rig.ModelAnimator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            Assert.IsNotNull(rig.LeftShoulder); Assert.IsNotNull(rig.RightShoulder);
            Assert.IsNotNull(rig.Head); Assert.IsNotNull(rig.RightHand); Assert.IsNotNull(rig.LeftFoot); Assert.IsNotNull(rig.RightFoot);
            done(rig);
        }

        /// <summary>+1 when the character faces CharacterRoot forward, -1 when it faces backwards. Uses the shoulder line: a
        /// character facing +Z has its right shoulder at +X (Unity is left-handed).</summary>
        private static float Facing(Rig rig)
        {
            var right = (rig.RightShoulder.position - rig.LeftShoulder.position); right.y = 0f;
            return Vector3.Dot(right.normalized, rig.Root.right);
        }

        private static void AssertStanding(Rig rig, string when, float minFraction = 0.7f)
        {
            Assert.That(rig.HeadHeight, Is.GreaterThan(rig.StandingHead * minFraction), $"{when}: collapsed (head {rig.HeadHeight:F2} m above feet vs standing {rig.StandingHead:F2})");
            var model = rig.ModelAnimator.transform.position;
            var drift = Vector2.Distance(new Vector2(model.x, model.z), new Vector2(rig.Root.position.x, rig.Root.position.z));
            Assert.That(drift, Is.LessThan(0.1f), $"{when}: Model drifted {drift:F2} m from CharacterRoot (root motion?)");
        }

        private static void Capture(Rig rig, string label)
        {
            Directory.CreateDirectory(OutDir);
            var camGo = new GameObject("CaptureCam");
            try
            {
                var cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.22f, 0.25f, 0.3f);
                cam.fieldOfView = 30f;
                var rt = new RenderTexture(384, 384, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                cam.targetTexture = rt;
                var focus = new Vector3(rig.Root.position.x, rig.Root.position.y + 0.9f, rig.Root.position.z);
                camGo.transform.position = focus + new Vector3(0f, 0.3f, 5.0f);
                camGo.transform.LookAt(focus);
                var request = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
                if (RenderPipeline.SupportsRenderRequest(cam, request)) RenderPipeline.SubmitRenderRequest(cam, request); else cam.Render();
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                File.WriteAllBytes(Path.Combine(OutDir, $"anim_{label}.png"), tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                cam.targetTexture = null;
                rt.Release();
                Object.DestroyImmediate(rt);
            }
            finally { Object.DestroyImmediate(camGo); }
        }

        [UnityTest] public IEnumerator Viking() => Scenario("Viking", 1.3f);
        [UnityTest] public IEnumerator SidekickGoblin() => Scenario("SidekickGoblin", 0.9f);
        [UnityTest] public IEnumerator WarCampGoblin() => Scenario("WarCampGoblin", 0.9f);

        private IEnumerator Scenario(string wrapperName, float minStandingHead)
        {

            Rig rig = null;
            yield return Build(wrapperName, r => rig = r);
            Capture(rig, $"{wrapperName}_0warmup"); // first render compiles shaders (white placeholder); discard
            yield return null;

            // ---- idle ----
            yield return Wait(1.5f);
            rig.StandingHead = rig.HeadHeight;
            Debug.Log($"[Spike:{wrapperName}] idle clip={rig.Animator.CurrentClip?.name} head={rig.StandingHead:F2} m above feet (min {minStandingHead}) human={rig.ModelAnimator.avatar.isHuman} rootMotion={rig.ModelAnimator.applyRootMotion}");
            Capture(rig, $"{wrapperName}_1idle");
            Assert.IsNotNull(rig.Animator.CurrentClip, "no clip playing at idle");
            Assert.That(rig.StandingHead, Is.GreaterThan(minStandingHead), "idle pose is not a standing pose");
            AssertStanding(rig, "idle");
            Debug.Log($"[Spike:{wrapperName}] idle facing={Facing(rig):F2} (+1 = faces CharacterRoot forward)");
            Assert.That(Facing(rig), Is.GreaterThan(0.7f), "character faces the wrong way at idle (clip orientation not aligned with the rig)");

            // ---- attack (UAL clip retargeted) ----
            var idleHand = rig.RightHand.position;
            var maxHandTravel = 0f;
            AnimationOutcome? outcome = null;
            var token = rig.Animator.RequestAction(new DefinitionId("anim-attack-melee"), o => outcome = o);
            Assert.IsTrue(token.IsValid, "attack cue rejected");
            var captured = false;
            var elapsed = 0f;
            while (outcome == null && elapsed < 4f)
            {
                elapsed += Time.deltaTime;
                yield return null;
                maxHandTravel = Mathf.Max(maxHandTravel, Vector3.Distance(rig.RightHand.position, idleHand));
                AssertStanding(rig, $"attack t={elapsed:F2}", 0.5f); // a sword lunge legitimately drops the head to ~70%
                if (!captured && elapsed > 0.25f) { Capture(rig, $"{wrapperName}_2attack"); captured = true; }
            }
            Debug.Log($"[Spike:{wrapperName}] attack outcome={outcome} after {elapsed:F2}s, hand travelled {maxHandTravel:F2} m");
            Assert.AreEqual(AnimationOutcome.Completed, outcome, "attack never completed");
            Assert.That(maxHandTravel, Is.GreaterThan(0.25f), "attack clip did not visibly move the hand (not retargeted?)");

            // ---- hit reaction ----
            outcome = null;
            rig.Animator.RequestAction(new DefinitionId("anim-hit"), o => outcome = o);
            elapsed = 0f;
            while (outcome == null && elapsed < 3f) { elapsed += Time.deltaTime; yield return null; AssertStanding(rig, "hit", 0.5f); }
            Assert.AreEqual(AnimationOutcome.Completed, outcome, "hit cue never completed");

            // ---- locomotion ----
            var agent = rig.Root.GetComponent<NavMeshAgent>();
            Assert.IsTrue(agent.isOnNavMesh, "agent not on NavMesh");
            agent.SetDestination(new Vector3(10f, 0f, 0f));
            yield return Wait(1.2f);
            Debug.Log($"[Spike:{wrapperName}] moving={rig.Animator.PresentationState.IsMoving} clip={rig.Animator.CurrentClip?.name} speed={agent.velocity.magnitude:F2}");
            Assert.IsTrue(rig.Animator.PresentationState.IsMoving, "state never entered moving");
            var minZ = float.MaxValue; var maxZ = float.MinValue;
            elapsed = 0f;
            while (elapsed < 1.2f)
            {
                elapsed += Time.deltaTime; yield return null;
                var rel = rig.Root.InverseTransformPoint(rig.LeftFoot.position).z - rig.Root.InverseTransformPoint(rig.RightFoot.position).z;
                minZ = Mathf.Min(minZ, rel); maxZ = Mathf.Max(maxZ, rel);
            }
            Debug.Log($"[Spike:{wrapperName}] foot swing range {maxZ - minZ:F2} m; clip={rig.Animator.CurrentClip?.name}");
            AssertStanding(rig, "walking");
            Debug.Log($"[Spike:{wrapperName}] walking facing={Facing(rig):F2}");
            Assert.That(Facing(rig), Is.GreaterThan(0.5f), "character faces the wrong way while walking");
            Assert.That(maxZ - minZ, Is.GreaterThan(0.2f), "feet do not swing while walking (walk clip not playing/retargeted)");
            Capture(rig, $"{wrapperName}_3walk");
            agent.ResetPath();
            yield return Wait(1.0f);

            // ---- death (terminal) then recover ----
            rig.Animator.RequestTerminal(new DefinitionId("anim-death"));
            yield return Wait(3.0f);
            Debug.Log($"[Spike:{wrapperName}] dead head={rig.HeadHeight:F2} m above feet; lowest foot y={rig.LowestFootY:F2}");
            Capture(rig, $"{wrapperName}_4dead");
            Assert.That(rig.HeadHeight, Is.LessThan(rig.StandingHead * 0.45f), "death clip did not lay the character down");
            Assert.That(rig.Head.position.y - rig.Root.position.y, Is.LessThan(rig.StandingHead * 0.5f), "dead head is still held high (floating / root height not baked?)");
            Assert.IsTrue(rig.Animator.PresentationState.IsTerminal);

            rig.Animator.Recover();
            yield return Wait(1.5f);
            Assert.IsFalse(rig.Animator.PresentationState.IsTerminal);
            AssertStanding(rig, "recovered");
            Capture(rig, $"{wrapperName}_5recovered");
        }
    }
}

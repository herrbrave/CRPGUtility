using System;
using System.Collections.Generic;
using System.Linq;
using CRPG.Actors;
using CRPG.Core;
using UnityEditor;
using UnityEngine;

namespace VikingRaid.Appearance.Editor
{
    /// <summary>
    /// The creator's 3D view: the master rig in its own preview scene (<see cref="PreviewRenderUtility"/>), dressed by the same <see cref="ModelAppearanceApplier"/> the game uses, held in the hero's
    /// idle pose (sampled once, on the preview's private instance: nothing touches the project's assets or the open scene). Orbit with <see cref="Orbit"/>, zoom with <see cref="Zoom"/>.
    /// Disposing removes the preview scene.
    /// </summary>
    public sealed class ModelAppearancePreview : IDisposable
    {
        private const string HeroProfilePath = "Assets/Characters/Animation/HeroAnimationProfile.asset";
        private const string IdleCue = "anim-idle";

        private readonly ModelAppearanceCatalog catalog;
        private PreviewRenderUtility utility;
        private GameObject model;
        private AnimationClip idle;
        private bool head;

        public ModelAppearancePreview(ModelAppearanceCatalog catalog)
        {
            this.catalog = catalog;
            utility = new PreviewRenderUtility();
            utility.camera.fieldOfView = 28f;
            utility.camera.nearClipPlane = 0.03f;
            utility.camera.farClipPlane = 60f;
            utility.camera.clearFlags = CameraClearFlags.SolidColor;
            utility.camera.backgroundColor = new Color(0.2f, 0.22f, 0.26f);
            utility.ambientColor = new Color(0.55f, 0.55f, 0.6f);
            utility.lights[0].intensity = 1.15f; utility.lights[0].transform.rotation = Quaternion.Euler(35f, 150f, 0f);
            utility.lights[1].intensity = 0.8f; utility.lights[1].transform.rotation = Quaternion.Euler(335f, 330f, 0f);   // fill from behind-left so the back of a cape is not black

            model = UnityEngine.Object.Instantiate(catalog.MasterPrefab);
            model.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            utility.AddSingleGO(model);

            idle = catalog.PreviewIdleClip;
            if (idle == null)
            {
                var profile = AssetDatabase.LoadAssetAtPath<CharacterAnimationProfile>(HeroProfilePath);
                if (profile != null && profile.TryGetCue(new DefinitionId(IdleCue), out var cue)) idle = cue.Clip;
            }
        }

        public float Yaw { get; set; } = 0f;      // 0: the camera in front of the character
        public float Pitch { get; set; } = 6f;
        public float ZoomScale { get; private set; } = 1f;

        /// <summary>The model's root, for tests.</summary>
        public GameObject Model => model;

        /// <summary>Frame the head (the Head tab) or the whole figure.</summary>
        public void FocusHead(bool value) => head = value;

        public void Orbit(float deltaX, float deltaY)
        {
            Yaw -= deltaX * 0.6f;
            Pitch = Mathf.Clamp(Pitch + deltaY * 0.4f, -25f, 50f);
        }

        public void Zoom(float scroll) => ZoomScale = Mathf.Clamp(ZoomScale * (1f + scroll * 0.06f), 0.35f, 2.5f);

        /// <summary>Dresses the model; the idle pose is sampled afterwards so attachments follow the posed bones.</summary>
        public void Apply(IReadOnlyDictionary<DefinitionId, DefinitionId> choices)
        {
            if (model == null) return;
            ModelAppearanceApplier.Apply(model, catalog, choices, immediate: true);
            Pose();
        }

        private void Pose()
        {
            if (idle == null) return;
            try
            {
                idle.SampleAnimation(model, 0.25f);
                // a humanoid clip writes its root motion (here a 197 degree turn) onto the model's transform; in the game the root stays put (no root motion), so undo it
                model.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                FaceTheCamera();
            }
            catch (Exception e) { Debug.LogWarning("[ModelAppearancePreview] could not sample the idle pose; showing the bind pose. " + e.Message); idle = null; }
        }

        /// <summary>Editor sampling of a humanoid clip can leave the BODY turned (the UAL idle: ~165 degrees on the Sidekick rigs; in the running game the same clip leaves a human at -3 degrees). The hip line decides which way the
        /// body faces; the preview turns the model root so the character faces the camera whatever the clip did.</summary>
        private void FaceTheCamera()
        {
            var animator = model.GetComponentsInChildren<Animator>(true).FirstOrDefault(a => a.avatar != null && a.avatar.isHuman);
            if (animator == null) return;
            var left = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg); var right = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            if (left == null || right == null) return;
            var side = right.position - left.position; side.y = 0f;
            if (side.sqrMagnitude < 1e-6f) return;
            var facing = Vector3.Cross(side.normalized, Vector3.up);
            model.transform.rotation = Quaternion.Euler(0f, -Vector3.SignedAngle(Vector3.forward, facing.normalized, Vector3.up), 0f);
        }

        /// <summary>Renders one frame into <paramref name="rect"/> and returns the texture to draw (null if the model is gone).</summary>
        public Texture Render(Rect rect)
        {
            if (utility == null || model == null) return null;
            utility.BeginPreview(rect, GUIStyle.none);
            Frame();
            utility.Render(true);   // allow the scriptable render pipeline: the project is URP
            return utility.EndPreview();
        }

        private void Frame()
        {
            var renderers = model.GetComponentsInChildren<Renderer>().Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
            var bounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds(Vector3.up, Vector3.one);
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);

            Vector3 centre;
            float distance;
            var halfFov = utility.camera.fieldOfView * 0.5f * Mathf.Deg2Rad;
            var headBone = model.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == catalog.HeadBone);
            if (head && headBone != null)
            {
                centre = headBone.position + Vector3.up * 0.09f;
                distance = 0.46f / Mathf.Tan(halfFov);
            }
            else
            {
                centre = new Vector3(bounds.center.x, bounds.center.y, bounds.center.z);
                distance = bounds.size.y * 0.58f / Mathf.Tan(halfFov);
            }

            distance *= ZoomScale;
            var rotation = Quaternion.Euler(-Pitch, Yaw, 0f);   // positive pitch raises the camera
            var cam = utility.camera.transform;
            cam.position = centre + rotation * new Vector3(0f, 0f, distance);
            cam.rotation = Quaternion.LookRotation(centre - cam.position, Vector3.up);
        }

        public void Dispose()
        {
            if (utility != null) { utility.Cleanup(); utility = null; }   // also destroys the model added to the preview scene
            model = null;
        }
    }
}

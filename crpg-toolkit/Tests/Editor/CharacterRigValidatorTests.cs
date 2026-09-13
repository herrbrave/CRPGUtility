using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Editor.Character;
using CRPG.Editor.Validation;
using CRPG.Presentation;
using NUnit.Framework;
using UnityEngine;

namespace CRPG.Tests.EditMode
{
    /// <summary>Task 2.5 (Production Foundation): CharacterRigValidator's checks, exercised directly against
    /// synthetic specs -- no Editor GUI, no scene, matching the class's own "pure logic" design.</summary>
    public class CharacterRigValidatorTests
    {
        private static void SetField(object target, string fieldName, object value)
        {
            var type = target.GetType();
            while (type != null)
            {
                var field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (field != null) { field.SetValue(target, value); return; }
                type = type.BaseType;
            }

            throw new System.Exception($"Field '{fieldName}' not found on {target.GetType().Name} or its base types.");
        }

        // A Generic (not Humanoid) avatar is the CORRECT/valid case for this system -- see ValidateModel's own
        // remarks: a Humanoid avatar actively corrupts this Playables-based animation pipeline (confirmed via
        // a real, visually-reproduced bug, Phase 2 review follow-up). AvatarBuilder.BuildGenericAvatar works
        // from a bare Transform hierarchy without a real FBX import, and ValidateModel treats a missing Avatar
        // as an Error, so a "valid" fixture needs one.
        private static GameObject CreateValidModel()
        {
            var go = new GameObject("Model");
            var animator = go.AddComponent<Animator>();
            animator.avatar = AvatarBuilder.BuildGenericAvatar(go, string.Empty);
            return go;
        }

        // A real, nonzero-length clip -- see CreateZeroLengthClip's remarks: new AnimationClip() alone
        // actually defaults to length 1 (not 0, despite being empty), so a genuinely nonzero-vs-zero
        // distinction needs an explicit curve either way.
        private static AnimationClip CreateClipWithLength(float seconds = 1f)
        {
            var clip = new AnimationClip();
            clip.SetCurve(string.Empty, typeof(Transform), "localPosition.x", AnimationCurve.Linear(0f, 0f, seconds, 1f));
            return clip;
        }

        // Confirmed empirically (not assumed): new AnimationClip() defaults to length 1, not 0, so it cannot
        // stand in for a "zero-length clip" test case on its own -- a single keyframe at time 0 is what
        // actually produces a real zero-length clip.
        private static AnimationClip CreateZeroLengthClip()
        {
            var clip = new AnimationClip();
            var curve = new AnimationCurve();
            curve.AddKey(0f, 0f);
            clip.SetCurve(string.Empty, typeof(Transform), "localPosition.x", curve);
            return clip;
        }

        private static AnimationCueClip CreateCueClip(string cueId, AnimationClip clip)
        {
            var cue = new AnimationCueClip();
            SetField(cue, "cueId", new DefinitionId(cueId));
            SetField(cue, "clip", clip);
            return cue;
        }

        private static CharacterAnimationProfile CreateProfile(params string[] cueIds)
        {
            var profile = ScriptableObject.CreateInstance<CharacterAnimationProfile>();
            var cues = cueIds.Select(cueId => CreateCueClip(cueId, CreateClipWithLength())).ToList();
            SetField(profile, "cues", cues);
            return profile;
        }

        private static CharacterAnimationProfile CreateProfileWithCues(params AnimationCueClip[] cues)
        {
            var profile = ScriptableObject.CreateInstance<CharacterAnimationProfile>();
            SetField(profile, "cues", cues.ToList());
            return profile;
        }

        private static CharacterSetupSpec ValidSpec()
        {
            return new CharacterSetupSpec
            {
                ModelPrefab = CreateValidModel(),
                AnimationProfile = CreateProfile("anim-idle", "anim-moving"),
            };
        }

        [Test]
        public void AValidSpecProducesNoErrors()
        {
            var results = CharacterRigValidator.Validate(ValidSpec());
            Assert.IsFalse(results.Any(r => r.Severity == ValidationSeverity.Error), string.Join("\n", results));
        }

        [Test]
        public void MissingModelPrefabIsAnError()
        {
            var spec = ValidSpec();
            spec.ModelPrefab = null;

            var results = CharacterRigValidator.Validate(spec);

            Assert.IsTrue(results.Any(r => r.Severity == ValidationSeverity.Error && r.Field == nameof(spec.ModelPrefab)));
        }

        [Test]
        public void ModelWithNoAnimatorIsAnError()
        {
            var spec = ValidSpec();
            spec.ModelPrefab = new GameObject("NoAnimator");

            var results = CharacterRigValidator.Validate(spec);

            Assert.IsTrue(results.Any(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("Animator")));
        }

        [Test]
        public void MissingAnimationProfileIsAnError()
        {
            var spec = ValidSpec();
            spec.AnimationProfile = null;

            var results = CharacterRigValidator.Validate(spec);

            Assert.IsTrue(results.Any(r => r.Severity == ValidationSeverity.Error && r.Field == nameof(spec.AnimationProfile)));
        }

        [Test]
        public void MissingMandatoryIdleCueIsAnError()
        {
            var spec = ValidSpec();
            spec.AnimationProfile = CreateProfile("anim-moving"); // no idle

            var results = CharacterRigValidator.Validate(spec);

            Assert.IsTrue(results.Any(r => r.Severity == ValidationSeverity.Error && r.Field == nameof(spec.IdleCueId)));
        }

        [Test]
        public void MissingMandatoryMovingCueIsAnError()
        {
            var spec = ValidSpec();
            spec.AnimationProfile = CreateProfile("anim-idle"); // no moving

            var results = CharacterRigValidator.Validate(spec);

            Assert.IsTrue(results.Any(r => r.Severity == ValidationSeverity.Error && r.Field == nameof(spec.MovingCueId)));
        }

        [Test]
        public void MissingOptionalCueIsAWarningNotAnError()
        {
            var spec = ValidSpec();
            spec.OptionalCueIds.Add(new DefinitionId("anim-attack-melee"));

            var results = CharacterRigValidator.Validate(spec);

            Assert.IsFalse(results.Any(r => r.Severity == ValidationSeverity.Error), "A missing OPTIONAL cue must never block validation.");
            Assert.IsTrue(results.Any(r => r.Severity == ValidationSeverity.Warning && r.Field == nameof(spec.OptionalCueIds)));
        }

        [Test]
        public void MissingRigFamilyIsAWarningNotAnError()
        {
            // Opt-in, matching Task 2.4's EquipmentVisualBinder rig-family check.
            var spec = ValidSpec();
            spec.RigDefinition = null;

            var results = CharacterRigValidator.Validate(spec);

            Assert.IsFalse(results.Any(r => r.Severity == ValidationSeverity.Error && r.Field == nameof(spec.RigDefinition)));
            Assert.IsTrue(results.Any(r => r.Severity == ValidationSeverity.Warning && r.Field == nameof(spec.RigDefinition)));
        }

        [Test]
        public void DeclaringARigFamilyClearsTheWarning()
        {
            var spec = ValidSpec();
            var rig = ScriptableObject.CreateInstance<CharacterRigDefinition>();
            SetField(rig, "id", "rig-test");
            spec.RigDefinition = rig;

            var results = CharacterRigValidator.Validate(spec);

            Assert.IsFalse(results.Any(r => r.Field == nameof(spec.RigDefinition)));
        }

        [Test]
        public void AMissingRequiredSocketIsAnError()
        {
            var spec = ValidSpec();
            spec.RequiredSocketIds.Add(new DefinitionId("socket-right-hand"));

            var results = CharacterRigValidator.Validate(spec);

            Assert.IsTrue(results.Any(r => r.Severity == ValidationSeverity.Error && r.Field == nameof(spec.RequiredSocketIds)));
        }

        [Test]
        public void APresentRequiredSocketProducesNoError()
        {
            var spec = ValidSpec();
            var socket = new GameObject("socket-right-hand");
            socket.transform.SetParent(spec.ModelPrefab.transform);
            spec.RequiredSocketIds.Add(new DefinitionId("socket-right-hand"));

            var results = CharacterRigValidator.Validate(spec);

            Assert.IsFalse(results.Any(r => r.Field == nameof(spec.RequiredSocketIds)));
        }

        [Test]
        public void ADegenerateCapsuleIsAnError()
        {
            var spec = ValidSpec();
            spec.CapsuleRadius = 1f;
            spec.CapsuleHeight = 0.5f; // shorter than its own diameter

            var results = CharacterRigValidator.Validate(spec);

            Assert.IsTrue(results.Any(r => r.Severity == ValidationSeverity.Error && r.Field == nameof(spec.CapsuleHeight)));
        }

        [Test]
        public void ANonPositiveAgentSpeedIsAnError()
        {
            var spec = ValidSpec();
            spec.AgentSpeed = 0f;

            var results = CharacterRigValidator.Validate(spec);

            Assert.IsTrue(results.Any(r => r.Severity == ValidationSeverity.Error && r.Field == nameof(spec.AgentSpeed)));
        }

        // ---- R8 (Phase 2 correction pass): a matching cue id existing is not the same as it being playable ----

        [Test]
        public void AMandatoryCueWithANullClipIsAnError()
        {
            var spec = ValidSpec();
            spec.AnimationProfile = CreateProfileWithCues(
                CreateCueClip("anim-idle", null), // TryGetCue succeeds -- the id exists -- but there's nothing to play
                CreateCueClip("anim-moving", CreateClipWithLength()));

            var results = CharacterRigValidator.Validate(spec);

            Assert.IsTrue(results.Any(r => r.Severity == ValidationSeverity.Error && r.Field == nameof(spec.IdleCueId)),
                "A mandatory cue with no Clip assigned must be an error, not silently treated as satisfied.");
        }

        [Test]
        public void AMandatoryCueWithAZeroLengthClipIsAnError()
        {
            var spec = ValidSpec();
            spec.AnimationProfile = CreateProfileWithCues(
                CreateCueClip("anim-idle", CreateZeroLengthClip()),
                CreateCueClip("anim-moving", CreateClipWithLength()));

            var results = CharacterRigValidator.Validate(spec);

            Assert.IsTrue(results.Any(r => r.Severity == ValidationSeverity.Error && r.Field == nameof(spec.IdleCueId)));
        }

        [Test]
        public void AnOptionalCueWithANullClipIsAWarningNotAnError()
        {
            var spec = ValidSpec();
            spec.OptionalCueIds.Add(new DefinitionId("anim-attack-melee"));
            var cues = new List<AnimationCueClip>(spec.AnimationProfile.Cues) { CreateCueClip("anim-attack-melee", null) };
            spec.AnimationProfile = CreateProfileWithCues(cues.ToArray());

            var results = CharacterRigValidator.Validate(spec);

            Assert.IsFalse(results.Any(r => r.Severity == ValidationSeverity.Error), string.Join("\n", results));
            Assert.IsTrue(results.Any(r => r.Severity == ValidationSeverity.Warning && r.Field == nameof(spec.OptionalCueIds)));
        }

        [Test]
        public void DuplicateCueIdsInTheProfileAreAnError()
        {
            var spec = ValidSpec();
            var cues = new List<AnimationCueClip>(spec.AnimationProfile.Cues)
            {
                CreateCueClip("anim-idle", CreateClipWithLength()), // duplicates the existing "anim-idle" entry
            };
            spec.AnimationProfile = CreateProfileWithCues(cues.ToArray());

            var results = CharacterRigValidator.Validate(spec);

            Assert.IsTrue(results.Any(r => r.Severity == ValidationSeverity.Error && r.Field == "Cues"));
        }

        // ValidateModel's avatar.isHuman==true branch (a real, visually-reproduced bug -- see the validator's
        // own remarks) needs a genuinely Humanoid-imported Avatar to exercise; AvatarBuilder.BuildHumanAvatar
        // requires a fully populated HumanDescription bone mapping, not practically constructible from a bare
        // synthetic Transform hierarchy the way BuildGenericAvatar is. Verified instead via a live Editor
        // repro against real content (Docs/Verification/Gate-A-Results.md) rather than a synthetic EditMode
        // fixture; flagged here rather than silently assumed covered.
    }
}

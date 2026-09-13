using System.Collections.Generic;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Presentation;
using UnityEngine;

namespace CRPG.Editor.Character
{
    /// <summary>
    /// Task 2.5 (Production Foundation): the plain-data shape CharacterSetupWindow's fields,
    /// CharacterRigValidator's checks, and CharacterWrapperBuilder's prefab construction all share --
    /// Docs/Design/Production-Foundation.md sec.4.6's "accepts a model prefab, rig family, animation profile,
    /// capsule/navigation settings and socket mapping." Deliberately a plain C# class, not a ScriptableObject
    /// or MonoBehaviour, so validation/building logic stays testable without any Editor GUI or GameObject.
    /// </summary>
    public sealed class CharacterSetupSpec
    {
        public GameObject ModelPrefab;
        public CharacterRigDefinition RigDefinition;
        public CharacterAnimationProfile AnimationProfile;

        // Mandatory for baseline locomotion (design sec.4.6: "missing mandatory clips block the relevant
        // capability") -- defaults match CharacterPlayableAnimator's own field defaults.
        public DefinitionId IdleCueId = new("anim-idle");
        public DefinitionId MovingCueId = new("anim-moving");

        // Checked but never blocking -- a missing optional cue just means that capability falls back to no
        // visual (PlayableCharacterAnimationDriver's own missing-clip handling), not a validation failure.
        public List<DefinitionId> OptionalCueIds = new();

        // Semantic socket ids this rig is expected to expose (Task 2.4's Rigid attachment convention: a
        // Transform named exactly this id, anywhere in the model's hierarchy).
        public List<DefinitionId> RequiredSocketIds = new();

        public float CapsuleHeight = 1.8f;
        public float CapsuleRadius = 0.3f;
        public float CapsuleCenterY = 0.9f;

        public float AgentRadius = 0.3f;
        public float AgentHeight = 1.8f;
        public float AgentSpeed = 3.5f;
        public float AgentAcceleration = 8f;
        public float AgentAngularSpeed = 360f;
        public float AgentStoppingDistance = 0.1f;
    }
}

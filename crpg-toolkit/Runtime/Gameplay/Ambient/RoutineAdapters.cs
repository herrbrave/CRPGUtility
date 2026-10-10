using CRPG.Core;
using CRPG.Presentation.Animation;
using UnityEngine;

namespace CRPG.Gameplay.Ambient
{
    /// <summary>Plays routine cues on the character's <see cref="CharacterPlayableAnimator"/>. A looping cue is held
    /// as a stance (the profile needs a StanceLocomotionSet of the same id); a one-shot is requested once and its
    /// clip length is reported so the step is never cut short. The animator's completion callback is deliberately not
    /// used: the machine's timer ends the step, so an unbound driver or a model swap cannot hang a routine.</summary>
    internal sealed class AnimatorRoutineAnimation : IRoutineAnimation
    {
        private readonly CharacterAgent agent;
        private CharacterPlayableAnimator animator;
        private DefinitionId heldStance;

        public AnimatorRoutineAnimation(CharacterAgent agent) => this.agent = agent;

        private CharacterPlayableAnimator Animator
        {
            get
            {
                if (animator == null) animator = agent.GetComponentInChildren<CharacterPlayableAnimator>(true);
                return animator;
            }
        }

        public float Begin(DefinitionId cue)
        {
            var a = Animator;
            if (a == null || !a.TryGetCue(cue, out var entry)) return 0f;

            if (entry.Loop)
            {
                a.SetStance(cue);
                heldStance = cue;
                return 0f;
            }

            a.RequestAction(cue);
            return entry.Clip != null ? entry.Clip.length : 0f;
        }

        public void End()
        {
            var a = Animator;
            // Only release a stance that is still ours: a combat stance set meanwhile must survive.
            if (heldStance.IsValid && a != null && a.PresentationState.StanceId == heldStance) a.SetStance(DefinitionId.None);
            heldStance = DefinitionId.None;
        }
    }

    internal sealed class AgentRoutineFacing : IRoutineFacing
    {
        private readonly CharacterAgent agent;

        public AgentRoutineFacing(CharacterAgent agent) => this.agent = agent;

        public void Face(Vector3 direction) => agent.Facing?.RequestFacing(FacingPriority.Ambient, direction);
        public void Clear() => agent.Facing?.ClearFacing(FacingPriority.Ambient);
    }

    /// <summary>Puts a <see cref="CarriedPropDefinition"/> in a humanoid bone of the NPC's model. "Holding" is the
    /// prop object itself, so a swapped or destroyed model (which takes the prop with it) reads as empty hands.</summary>
    internal sealed class HandRoutineProp : IRoutineProp
    {
        private readonly CharacterAgent agent;
        private GameObject instance;

        public HandRoutineProp(CharacterAgent agent) => this.agent = agent;

        public bool Holding => instance != null;

        public bool Attach(CarriedPropDefinition prop)
        {
            if (Holding || prop == null || prop.Prefab == null) return false;

            var animator = agent.View != null ? agent.View.Animator : null;
            if (animator == null || !animator.isHuman)
            {
                Debug.LogWarning($"{agent.name}: cannot carry '{prop.name}': the model has no humanoid Animator.", agent);
                return false;
            }

            var bone = animator.GetBoneTransform(prop.Bone);
            if (bone == null)
            {
                Debug.LogWarning($"{agent.name}: cannot carry '{prop.name}': the rig has no {prop.Bone} bone.", agent);
                return false;
            }

            instance = Object.Instantiate(prop.Prefab, bone, false);
            instance.name = prop.name;
            instance.transform.localPosition = prop.LocalPosition;
            instance.transform.localRotation = Quaternion.Euler(prop.LocalEulerAngles);
            instance.transform.localScale = prop.Prefab.transform.localScale * prop.LocalScale;

            // Cosmetic only: a collider on a carried crate would block clicks and bump the NavMesh agents.
            foreach (var collider in instance.GetComponentsInChildren<Collider>(true)) Object.Destroy(collider);
            return true;
        }

        public void Detach()
        {
            if (instance != null) Object.Destroy(instance);
            instance = null;
        }
    }
}

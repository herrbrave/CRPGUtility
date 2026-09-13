using System.Collections.Generic;
using CRPG.Core;
using UnityEngine;

namespace CRPG.Actors
{
    /// <summary>One cue's actual presentation: which clip to crossfade to, how long the crossfade takes, and
    /// whether it loops (a persistent stance/locomotion state) or plays once and hands control back to
    /// whatever baseline was active before it (a one-shot action like a melee swing). See CharacterPlayableAnimator.</summary>
    [System.Serializable]
    public sealed class AnimationCueClip
    {
        [SerializeField] private DefinitionId cueId;
        [SerializeField] private AnimationClip clip;
        [SerializeField] private float crossfadeDuration = 0.2f;
        [SerializeField] private bool loop = true;

        // Task 2.3, purely additive (existing entries deserialize with this defaulting to 0). Only meaningful
        // for a looping locomotion cue: the real-world speed (m/s) this clip was authored/calibrated to look
        // correct at. 0 opts the cue out of speed blending entirely -- PlayableCharacterAnimationDriver plays
        // it at its authored rate, which is what every non-locomotion cue (idle, actions) wants.
        [SerializeField] private float referenceSpeed;

        public DefinitionId CueId => cueId;
        public AnimationClip Clip => clip;
        public float CrossfadeDuration => crossfadeDuration;
        public bool Loop => loop;
        public float ReferenceSpeed => referenceSpeed;
    }

    /// <summary>A stance's own idle/moving pair -- "Combat stance chooses a locomotion set; it does not
    /// freeze movement animation" (Docs/Design/Production-Foundation.md sec.4.4). Optional: a stance with no
    /// entry here simply falls back to the profile's default idle/moving cues.</summary>
    [System.Serializable]
    public sealed class StanceLocomotionSet
    {
        [SerializeField] private DefinitionId stanceId;
        [SerializeField] private DefinitionId idleCueId;
        [SerializeField] private DefinitionId movingCueId;

        public DefinitionId StanceId => stanceId;
        public DefinitionId IdleCueId => idleCueId;
        public DefinitionId MovingCueId => movingCueId;
    }

    /// <summary>
    /// Maps animation cue ids (bare DefinitionId tags, not registered/resolved through IRulesDatabase -- same
    /// "just a tag" treatment as a Timeline event key) to actual clips for one rig-compatible set of
    /// characters. A direct Inspector reference on a character prefab's CharacterPlayableAnimator, like an
    /// AnimatorController would have been -- picked at authoring time, not resolved by id at runtime. Sharing
    /// one profile across every character prefab that uses the same Humanoid rig is the whole point of
    /// Mecanim retargeting: a Human race and an Elf race can point at the same profile.
    /// </summary>
    [CreateAssetMenu(menuName = "CRPG Toolkit/Actors/Character Animation Profile")]
    public sealed class CharacterAnimationProfile : ScriptableObject
    {
        [SerializeField] private List<AnimationCueClip> cues = new();

        /// <summary>Every authored cue -- read-only enumeration for tooling (CharacterRigValidator's
        /// duplicate-cue-id and missing-clip checks) that needs to inspect the whole set, not just resolve one
        /// id at a time.</summary>
        public IReadOnlyList<AnimationCueClip> Cues => cues;

        // Task 2.2, purely additive (existing profile assets deserialize fine with this defaulting to an
        // empty list): per-stance locomotion overrides, so baseline Idle/Moving can pick different clips
        // while a stance is active without CharacterPlayableAnimator hardcoding stance-specific cue ids.
        [SerializeField] private List<StanceLocomotionSet> stanceLocomotionSets = new();

        public bool TryGetCue(DefinitionId cueId, out AnimationCueClip entry)
        {
            foreach (var candidate in cues)
            {
                if (candidate.CueId != cueId) continue;
                entry = candidate;
                return true;
            }

            entry = null;
            return false;
        }

        public bool TryGetStanceLocomotion(DefinitionId stanceId, out DefinitionId idleCueId, out DefinitionId movingCueId)
        {
            foreach (var set in stanceLocomotionSets)
            {
                if (set.StanceId != stanceId) continue;
                idleCueId = set.IdleCueId;
                movingCueId = set.MovingCueId;
                return true;
            }

            idleCueId = default;
            movingCueId = default;
            return false;
        }
    }
}

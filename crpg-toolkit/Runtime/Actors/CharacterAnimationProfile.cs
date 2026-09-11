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

        public DefinitionId CueId => cueId;
        public AnimationClip Clip => clip;
        public float CrossfadeDuration => crossfadeDuration;
        public bool Loop => loop;
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
    }
}

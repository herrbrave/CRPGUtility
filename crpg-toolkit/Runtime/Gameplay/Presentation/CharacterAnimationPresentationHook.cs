using CRPG.Abilities;
using CRPG.Core;
using CRPG.Gameplay.Actions;
using CRPG.Presentation.Animation;

namespace CRPG.Gameplay
{
    /// <summary>Task 4.5: the real, live implementation of <see cref="IActionPresentationHook"/> deferred by
    /// Task 4.3 -- bridges a committed ability to the source's bound <see cref="CharacterPlayableAnimator"/>.
    /// Lives in CRPG.Gameplay (not CRPG.Presentation, which cannot reference back into CRPG.Gameplay.Actions)
    /// since <see cref="CharacterAgent"/> already lives here too. No-ops (never throws, matches the
    /// interface's own "a missing cue can never soft-lock" contract) when the source has no live agent, no
    /// bound animator, or the ability's <see cref="AbilityDefinition.AnimationCueId"/> is
    /// <see cref="DefinitionId.None"/> -- unauthored content simply plays no action cue.</summary>
    public sealed class CharacterAnimationPresentationHook : IActionPresentationHook
    {
        public void RequestActionCue(RuntimeEntityId source, AbilityDefinition ability)
        {
            if (ability.AnimationCueId == DefinitionId.None) return;

            var agent = FindAgent(source);
            if (agent == null) return;

            var animator = agent.GetComponentInChildren<CharacterPlayableAnimator>(true);
            if (animator == null) return;

            animator.RequestAction(ability.AnimationCueId);
        }

        private static CharacterAgent FindAgent(RuntimeEntityId id)
        {
            foreach (var agent in CharacterAgent.AllLive)
            {
                if (agent != null && agent.EntityId == id) return agent;
            }

            return null;
        }
    }
}

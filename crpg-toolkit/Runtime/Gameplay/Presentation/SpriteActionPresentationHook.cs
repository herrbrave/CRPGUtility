using CRPG.Abilities;
using CRPG.Core;
using CRPG.Gameplay.Actions;
using CRPG.Presentation.Sprites;

namespace CRPG.Gameplay
{
    /// <summary>
    /// 2D campaign Task 2.6: the <see cref="IActionPresentationHook"/> for layered sprite characters, the counterpart of
    /// <see cref="CharacterAnimationPresentationHook"/>. When the source commits an attack or hostile ability (tags Attack or Hostile) its
    /// <see cref="SpriteActionCues"/> plays the attack cue (a hop and scale punch in place: the hook carries no target, so there is no
    /// lunge). Other abilities (healing, utility, movement) play nothing. It ignores <see cref="AbilityDefinition.AnimationCueId"/> (those ids
    /// name humanoid clips). Never throws: a null ability, a missing agent or a missing cues component are all no-ops. A game mixing
    /// humanoid and sprite characters composes two hooks itself (a scheduler takes exactly one); no composite exists yet.
    /// </summary>
    public sealed class SpriteActionPresentationHook : IActionPresentationHook
    {
        private const AbilityTag AttackCueTags = AbilityTag.Attack | AbilityTag.Hostile;

        public void RequestActionCue(RuntimeEntityId source, AbilityDefinition ability)
        {
            if (ability == null || (ability.Tags & AttackCueTags) == 0) return;

            var agent = FindAgent(source);
            if (agent == null) return;

            var cues = agent.GetComponentInChildren<SpriteActionCues>(true);
            if (cues != null) cues.PlayAttack();
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

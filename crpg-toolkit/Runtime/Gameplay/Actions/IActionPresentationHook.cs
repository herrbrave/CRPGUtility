using CRPG.Abilities;
using CRPG.Core;

namespace CRPG.Gameplay.Actions
{
    /// <summary>Task 4.3: optional, fire-and-forget presentation seam -- "connect presentation without letting
    /// clip completion own gameplay" (design sec.4.4). <see cref="AbilityActionExecution"/> calls this once, at
    /// the moment it commits, and never waits on it: Recover's duration is always the ability's own authored
    /// timer, never the animation's own completion. A missing hook, a missing cue, or a clip that never
    /// finishes therefore can never soft-lock a phase transition -- animation is a synchronization hint, not a
    /// gameplay dependency. A real implementation bridging to a live
    /// <c>CRPG.Presentation.Animation.CharacterPlayableAnimator</c> is deliberately deferred to Task 4.5, which
    /// already owns building the real tactical fixture this would animate.</summary>
    public interface IActionPresentationHook
    {
        void RequestActionCue(RuntimeEntityId source, AbilityDefinition ability);
    }
}

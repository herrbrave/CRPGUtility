using System;
using CRPG.Actors;
using UnityEngine;

namespace CRPG.Presentation.Animation
{
    /// <summary>
    /// Small adapter contract for binding an Animator, ticking blend/crossfade timing, and issuing clip
    /// playback -- CharacterPresentationState decides WHAT should currently be showing; a driver decides HOW
    /// to actually make an Animator show it. Playables is the only implementation today
    /// (<see cref="PlayableCharacterAnimationDriver"/>), per the design's "retain Playables as the reference
    /// backend"; this interface exists so CharacterPlayableAnimator's request-routing/state logic would not
    /// need to change if that ever stopped being true.
    /// </summary>
    public interface ICharacterAnimationDriver
    {
        bool IsBound { get; }

        void Bind(Animator animator);
        void Unbind();

        /// <summary>Advances crossfade/playback-completion timing. Call once per frame while bound; a no-op
        /// while unbound. Passing 0 (e.g. while the game is paused and Time.deltaTime is itself 0) must be a
        /// true no-op -- no crossfade/completion progress -- so playback resumes exactly where it left off
        /// once ticking with a real delta again.</summary>
        void Tick(float deltaTime);

        /// <summary>Reports the character's actual current movement speed (m/s), used to continuously scale
        /// the currently-playing LOOPING clip's own playback rate against its authored
        /// AnimationCueClip.ReferenceSpeed -- reduces foot sliding across the whole speed range instead of
        /// only distinguishing a binary idle/moving pair. Has no effect while a one-shot action (PlayOnce) is
        /// playing, and no effect for a cue authored with ReferenceSpeed 0 (opted out). Safe to call every
        /// frame regardless of bind state.</summary>
        void SetLocomotionSpeed(float actualSpeed);

        /// <summary>Crossfades to entry and holds it looping indefinitely until superseded by another
        /// PlayLoop/PlayOnce call. The very first clip played after Bind() starts at full weight immediately
        /// (nothing to fade from yet); every subsequent call crossfades using entry's authored duration.</summary>
        void PlayLoop(AnimationCueClip entry);

        /// <summary>Crossfades to entry once; invokes onComplete exactly once when its duration elapses.
        /// Missing art (a null Clip) or a zero-length clip completes immediately rather than soft-locking the
        /// caller (design sec.4.4). Superseding this with another PlayLoop/PlayOnce call before it completes
        /// does NOT invoke onComplete -- the caller (CharacterPresentationState, via its token) owns deciding
        /// whether that counts as a cancellation.</summary>
        void PlayOnce(AnimationCueClip entry, Action onComplete);
    }
}

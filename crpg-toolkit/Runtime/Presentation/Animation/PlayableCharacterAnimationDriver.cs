using System;
using System.Collections.Generic;
using CRPG.Actors;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace CRPG.Presentation.Animation
{
    /// <summary>
    /// Reference <see cref="ICharacterAnimationDriver"/>: an AnimationMixerPlayable graph built directly on
    /// Playables (no AnimatorController/state machine), bounded to <see cref="MaxPorts"/> simultaneous ports.
    /// A new cue always gets a fresh port; every port that was already contributing live weight at that
    /// instant (the previous target plus anything still mid-fade from an earlier interruption) joins one
    /// shared fade-out group, its start weight captured as its ACTUAL current weight, and the group's timeline
    /// restarts from zero. Because every fading port's start weight is captured fresh at the exact moment of
    /// interruption and the group shares one linear timeline with the incoming port, combined weight is
    /// mathematically exactly 1 at every instant -- not just eventually, and not merely bounded below the
    /// dominant port's weight (Phase 2 correction pass, R7: the old 2-port design deliberately kept only the
    /// single most-dominant outgoing port and discarded the rest, which is what "a bounded outgoing blend
    /// group is acceptable" (design sec.4.4) actually invites fixing, not settling for). If accepting a new
    /// port would exceed <see cref="MaxPorts"/> (only possible when interruptions arrive faster than any fade
    /// completes -- design sec.4.4's explicit "bounded... not ever-growing" allowance), the single weakest
    /// live contributor is snapped to zero and freed first; this is the one place normalization is
    /// deliberately sacrificed, and only in that pathological case.
    /// </summary>
    /// <remarks>Extracted from CharacterPlayableAnimator (Task 2.2) so graph/mixer/port mechanics are a
    /// standalone, swappable concern from WHAT should be playing (CharacterPresentationState) and WHO
    /// requests it (CharacterPlayableAnimator's event/identity routing).</remarks>
    public sealed class PlayableCharacterAnimationDriver : ICharacterAnimationDriver
    {
        private const float MinCrossfadeDuration = 0.0001f;

        // Task 2.3: bounds on how far actual-speed/reference-speed scaling can push a locomotion clip's
        // playback rate -- keeps a badly mismatched reference speed (or a momentary velocity spike) from
        // producing an absurdly sped-up/slowed-down clip instead of just imperfect foot contact.
        private const float MinLoopPlaybackRate = 0.5f;
        private const float MaxLoopPlaybackRate = 1.75f;

        // R7: how many simultaneous ports a rapid chain of interruptions can occupy before normalization is
        // sacrificed for the weakest contributor. 4 comfortably covers the named acceptance scenario
        // (attack -> hit -> death, i.e. at most 2-3 overlapping interruptions) without an ever-growing graph.
        private const int MaxPorts = 4;

        private readonly string graphName;

        private PlayableGraph graph;
        private AnimationMixerPlayable mixer;
        private readonly Playable[] portPlayables = new Playable[MaxPorts];

        private int activePort = -1; // -1 until the first clip is ever played
        private readonly List<int> fadingPorts = new();
        private readonly Dictionary<int, float> fadeStartWeights = new();
        private float crossfadeElapsed;
        private float crossfadeDuration;

        private bool isPlayingOnce;
        private float pendingElapsed;
        private float pendingClipLength;
        private Action pendingOnComplete;

        private float actualSpeed;
        private float loopReferenceSpeed;

        // A raw AnimationClipPlayable in a custom (non-Timeline) graph does NOT loop on its own regardless of
        // the source clip's own WrapMode -- its time keeps advancing (or holds) past the clip's length and the
        // pose simply freezes on the last frame. CharacterPlayableAnimator's RefreshBaseline only ever calls
        // PlayLoop ONCE per cue change (an explicit "already playing this cue, skip" cache), so nothing else
        // would ever re-trigger a looping cue -- confirmed live: a walk longer than the clip's own length froze
        // mid-stride while the character kept translating. Tracked per active port, from the authored
        // AnimationCueClip.Loop flag, and wrapped manually in Tick.
        private bool activePortLoops;

        public PlayableCharacterAnimationDriver(string graphName = "Character Animation")
        {
            this.graphName = graphName;
        }

        public bool IsBound => graph.IsValid();

        /// <summary>Diagnostic: which port currently holds the visually-dominant (incoming/target) clip.</summary>
        public int ActivePort => activePort;

        /// <summary>Diagnostic: the sum of every port's current mixer input weight. Exactly 1 whenever bound
        /// and at least one clip has ever played (see the class remarks for why this now holds continuously,
        /// not just eventually) -- 0 before the first clip or once unbound.</summary>
        public float CurrentBlendWeightSum
        {
            get
            {
                if (!graph.IsValid()) return 0f;
                var sum = 0f;
                for (var p = 0; p < MaxPorts; p++) sum += mixer.GetInputWeight(p);
                return sum;
            }
        }

        /// <summary>Diagnostic: one port's current mixer input weight.</summary>
        public float GetPortWeight(int port) => graph.IsValid() ? mixer.GetInputWeight(port) : 0f;

        /// <summary>Diagnostic: one port's current Playable speed (1 = authored rate).</summary>
        public double GetPortSpeed(int port) => portPlayables[port].IsValid() ? portPlayables[port].GetSpeed() : 1d;

        /// <summary>Diagnostic: one port's current evaluated time -- lets a test assert a looping clip's time
        /// actually wraps back toward zero past its own length, instead of climbing forever/freezing.</summary>
        public double GetPortTime(int port) => portPlayables[port].IsValid() ? portPlayables[port].GetTime() : 0d;

        /// <summary>Diagnostic: the actual AnimationClip currently connected to one port, or null if that
        /// port holds no live playable -- lets a test assert WHICH clip the graph is really showing (e.g.
        /// "resumed idle, not still on the attack swing's last frame") instead of only inferring it from
        /// weights.</summary>
        public AnimationClip GetPortClip(int port) =>
            portPlayables[port].IsValid() ? ((AnimationClipPlayable)portPlayables[port]).GetAnimationClip() : null;

        public void Bind(Animator animator)
        {
            if (animator == null) throw new ArgumentNullException(nameof(animator));

            Unbind();

            graph = PlayableGraph.Create(graphName);
            mixer = AnimationMixerPlayable.Create(graph, MaxPorts);
            var output = AnimationPlayableOutput.Create(graph, "Animation", animator);
            output.SetSourcePlayable(mixer);

            graph.Play();

            // A Humanoid Animator's own internal avatar/pose state can still be stale the instant a fresh
            // PlayableGraph output is first connected to it (confirmed empirically: a live character showed a
            // collapsed, wrong-looking pose from frame one -- pelvis at ground level with a mirrored rotation
            // -- that Animator.Rebind() immediately corrected, with no other change). Rebind() forces the
            // Animator to resync its internal Humanoid state against this Playables output before anything
            // ever gets evaluated through it, so the very first frame is correct instead of relying on
            // something else to eventually jolt it straight.
            animator.Rebind();
        }

        public void Unbind()
        {
            if (graph.IsValid()) graph.Destroy();

            for (var p = 0; p < MaxPorts; p++) portPlayables[p] = Playable.Null;
            activePort = -1;
            fadingPorts.Clear();
            fadeStartWeights.Clear();
            isPlayingOnce = false;
            pendingOnComplete = null;
            actualSpeed = 0f;
            loopReferenceSpeed = 0f;
            activePortLoops = false;
        }

        public void Tick(float deltaTime)
        {
            if (!graph.IsValid()) return;

            UpdateCrossfade(deltaTime);
            UpdateLoopPlaybackSpeed();
            UpdateLooping();

            if (!isPlayingOnce) return;

            pendingElapsed += deltaTime;
            if (pendingElapsed < pendingClipLength) return;

            isPlayingOnce = false;
            var callback = pendingOnComplete;
            pendingOnComplete = null;
            callback?.Invoke();
        }

        public void SetLocomotionSpeed(float actualSpeed) => this.actualSpeed = Mathf.Max(0f, actualSpeed);

        // Only the currently loop-targeted port is speed-scaled -- while a one-shot action is playing (or a
        // port is on its way out during a crossfade) it must keep its own authored rate, not chase locomotion
        // speed. Reference speed 0 (unauthored / non-locomotion cue) leaves the port at its authored rate.
        private void UpdateLoopPlaybackSpeed()
        {
            if (isPlayingOnce || loopReferenceSpeed <= 0f || activePort < 0 || !portPlayables[activePort].IsValid()) return;

            var rate = Mathf.Clamp(actualSpeed / loopReferenceSpeed, MinLoopPlaybackRate, MaxLoopPlaybackRate);
            portPlayables[activePort].SetSpeed(rate);
        }

        // Manual loop: once the active port's evaluated time reaches the clip's own length, wrap it back
        // toward zero rather than letting it sit on (or past) the final frame forever. Skipped for a one-shot
        // action (isPlayingOnce owns that clip's lifetime via its own elapsed-time completion callback) and
        // for any cue authored with Loop == false.
        private void UpdateLooping()
        {
            if (isPlayingOnce || !activePortLoops || activePort < 0 || !portPlayables[activePort].IsValid()) return;

            var clip = ((AnimationClipPlayable)portPlayables[activePort]).GetAnimationClip();
            if (clip == null || clip.length <= 0f) return;

            var time = portPlayables[activePort].GetTime();
            if (time >= clip.length) portPlayables[activePort].SetTime(time % clip.length);
        }

        public void PlayLoop(AnimationCueClip entry)
        {
            isPlayingOnce = false;
            pendingOnComplete = null;
            loopReferenceSpeed = entry?.ReferenceSpeed ?? 0f;
            PlayCue(entry);
        }

        public void PlayOnce(AnimationCueClip entry, Action onComplete)
        {
            var clipLength = entry?.Clip != null ? entry.Clip.length : 0f;
            var played = PlayCue(entry);

            if (!played || clipLength <= 0f)
            {
                // Missing art (no Clip) or a zero-length clip must not soft-lock the caller -- design sec.4.4.
                isPlayingOnce = false;
                pendingOnComplete = null;
                onComplete?.Invoke();
                return;
            }

            isPlayingOnce = true;
            pendingElapsed = 0f;
            pendingClipLength = clipLength;
            pendingOnComplete = onComplete;
        }

        private bool PlayCue(AnimationCueClip entry)
        {
            if (!graph.IsValid() || entry?.Clip == null) return false;

            if (activePort < 0)
            {
                PlayFirstClip(entry);
                return true;
            }

            // Every port currently holding live weight -- the previous target plus anything still mid-fade
            // from an earlier interruption -- captured with its ACTUAL current weight right now.
            var liveWeights = new Dictionary<int, float> { [activePort] = mixer.GetInputWeight(activePort) };
            foreach (var p in fadingPorts) liveWeights[p] = mixer.GetInputWeight(p);

            // Bounded collapse: only reachable when interruptions arrive faster than any fade completes.
            while (liveWeights.Count >= MaxPorts)
            {
                var weakest = -1;
                foreach (var p in liveWeights.Keys)
                    if (weakest < 0 || liveWeights[p] < liveWeights[weakest]) weakest = p;

                if (portPlayables[weakest].IsValid()) portPlayables[weakest].Destroy();
                portPlayables[weakest] = Playable.Null;
                mixer.SetInputWeight(weakest, 0f);
                liveWeights.Remove(weakest);
            }

            var incomingPort = -1;
            for (var p = 0; p < MaxPorts; p++)
            {
                if (liveWeights.ContainsKey(p)) continue;
                incomingPort = p;
                break;
            }

            var clipPlayable = AnimationClipPlayable.Create(graph, entry.Clip);
            portPlayables[incomingPort] = clipPlayable;
            graph.Connect(clipPlayable, 0, mixer, incomingPort);
            mixer.SetInputWeight(incomingPort, 0f); // a brand new clip identity always starts from nothing

            // Restart the shared fade timeline: every still-live port (captured above) becomes this
            // interruption's fade-out group, its start weight fixed at its true current value.
            fadingPorts.Clear();
            fadeStartWeights.Clear();
            foreach (var pair in liveWeights)
            {
                fadingPorts.Add(pair.Key);
                fadeStartWeights[pair.Key] = pair.Value;
            }

            activePort = incomingPort;
            activePortLoops = entry.Loop;
            crossfadeElapsed = 0f;
            crossfadeDuration = Mathf.Max(entry.CrossfadeDuration, MinCrossfadeDuration);

            return true;
        }

        private void PlayFirstClip(AnimationCueClip entry)
        {
            const int port = 0;
            var clipPlayable = AnimationClipPlayable.Create(graph, entry.Clip);
            portPlayables[port] = clipPlayable;
            graph.Connect(clipPlayable, 0, mixer, port);

            // Nothing to fade from yet -- start at full weight immediately rather than crossfading in from
            // silence/T-pose. Explicitly zero every other port too, rather than trusting the mixer's own
            // default input weight for ports nothing has ever targeted yet.
            for (var p = 0; p < MaxPorts; p++) mixer.SetInputWeight(p, p == port ? 1f : 0f);

            activePort = port;
            activePortLoops = entry.Loop;
            fadingPorts.Clear();
            fadeStartWeights.Clear();
        }

        private void UpdateCrossfade(float deltaTime)
        {
            if (fadingPorts.Count == 0) return;

            crossfadeElapsed += deltaTime;
            var t = Mathf.Clamp01(crossfadeElapsed / crossfadeDuration);
            mixer.SetInputWeight(activePort, t);

            if (t < 1f)
            {
                foreach (var p in fadingPorts) mixer.SetInputWeight(p, Mathf.Lerp(fadeStartWeights[p], 0f, t));
                return;
            }

            // Empirically confirmed Unity Playables quirk, not an assumption (Task 2.3): destroying a Playable
            // that is connected as a mixer input resets THAT input's weight back to a default of 1, even if it
            // was just explicitly set to 0 moments earlier in the same call. The zero must be applied AFTER
            // the destroy, not before it.
            foreach (var p in fadingPorts)
            {
                if (portPlayables[p].IsValid()) portPlayables[p].Destroy();
                portPlayables[p] = Playable.Null;
                mixer.SetInputWeight(p, 0f);
            }

            fadingPorts.Clear();
            fadeStartWeights.Clear();
        }
    }
}

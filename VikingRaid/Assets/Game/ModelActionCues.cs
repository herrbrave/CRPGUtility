using System;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Gameplay;
using CRPG.Presentation;
using CRPG.Presentation.Animation;
using UnityEngine;

namespace VikingRaid.Game
{
    /// <summary>
    /// Drives the hit / down / death / recover animation cues of a 3D model character from game events. The plan review found nothing in
    /// the toolkit does this for models (only the source's attack cue ever fires, via <c>CharacterAnimationPresentationHook</c>); the 2D
    /// campaign has <c>SpriteActionCues</c> for sprites. Lives on CharacterRoot (next to <see cref="CharacterPlayableAnimator"/>) so it
    /// survives a Model swap.
    /// <list type="bullet">
    /// <item><b>Hit</b>: <c>AfterDamage</c> requests the hit action after <see cref="HitDelaySeconds"/> -- damage is applied BEFORE the
    /// attacker's own swing starts (the presentation hook runs after Execute), so a delay lets the swing land first. Hits inside the delay
    /// collapse into one; hits on a downed character are ignored.</item>
    /// <item><b>Down</b>: <c>CreatureDroppedToZeroHP</c> / <c>CreatureDied</c> enter the terminal pose after
    /// <see cref="TerminalDelaySeconds"/> and cancel a pending hit. <b>Recover</b>: stabilised / condition removed AND HP above 0 -> stand up.</item>
    /// <item><b>Defeat</b> (<see cref="IDefeatPresentation"/>): <c>WorldSpawnService.MarkDefeated</c> defers destroying a defeated character
    /// until <see cref="BeginDefeat"/>'s callback. <see cref="CorpseLingerSeconds"/> &lt;= 0 means the corpse stays for good (it is made
    /// inert by the spawn service: colliders off, mover off, out of CharacterAgent.AllLive) -- atmospheric in a goblin camp.</item>
    /// </list>
    /// Self-binds from a bound <see cref="CharacterAgent"/> (WorldSpawnService.BindView binds the agent but knows nothing about this
    /// component); <see cref="Initialize"/> is the explicit entry point and follows the toolkit binding contract (identical rebind is a no-op,
    /// a different tuple while bound throws). Handlers only record timestamps -- every Unity call happens in <c>Update</c>.
    /// </summary>
    [DefaultExecutionOrder(10)]
    public sealed class ModelActionCues : MonoBehaviour, IDefeatPresentation
    {
        [SerializeField] private DefinitionId hitCueId = new("anim-hit");
        [SerializeField] private DefinitionId deathCueId = new("anim-death");
        [SerializeField] private float hitDelaySeconds = 0.35f;
        [SerializeField] private float terminalDelaySeconds = 0.35f;
        [Tooltip("Seconds the corpse lies after the death clip before the spawn service destroys it. 0 or less: it never does.")]
        [SerializeField] private float corpseLingerSeconds;
        [Tooltip("The ruleset's hit-point resource id (SRD5: resource-hp). Empty: the character never recovers from down.")]
        [SerializeField] private DefinitionId hitPointResourceId;

        private const float FallbackDeathClipSeconds = 1.5f;

        /// <summary>Optional: seconds until a blow from <c>source</c> LANDS on <c>target</c> (a melee swing, or an arrow's release plus flight). Null means the fixed delays below.
        /// Set by the game's effects layer so a hit reaction waits for an arrow to arrive.</summary>
        public static Func<RuntimeEntityId, RuntimeEntityId, float> LandingDelayRule { get; set; }
        private RuntimeEntityId lastStriker;

        private CharacterPlayableAnimator animator;
        private CombatCueEventBinding binding;
        private RuntimeEntityId character;
        private IGameEventBus eventBus;
        private ICharacterRegistry characters;
        private bool autoBindAttempted;

        private float pendingHitAt = -1f;
        private float pendingTerminalAt = -1f;
        private bool terminalRequested;
        private float terminalStartedAt = -1f;
        private Action defeatCallback;

        public DefinitionId HitCueId { get => hitCueId; set => hitCueId = value; }
        public DefinitionId DeathCueId { get => deathCueId; set => deathCueId = value; }
        public float HitDelaySeconds { get => hitDelaySeconds; set => hitDelaySeconds = value; }
        public float TerminalDelaySeconds { get => terminalDelaySeconds; set => terminalDelaySeconds = value; }
        public float CorpseLingerSeconds { get => corpseLingerSeconds; set => corpseLingerSeconds = value; }
        public DefinitionId HitPointResourceId { get => hitPointResourceId; set => hitPointResourceId = value; }
        public bool IsBound => eventBus != null;

        /// <summary>True from the moment a down/death was requested until Recover.</summary>
        public bool IsDown => terminalRequested;

        public void Initialize(RuntimeEntityId character, IGameEventBus eventBus, ICharacterRegistry characters)
        {
            if (this.eventBus != null)
            {
                if (this.character == character && ReferenceEquals(this.eventBus, eventBus) && ReferenceEquals(this.characters, characters)) return;

                throw new InvalidOperationException(
                    $"{name} is already bound to {this.character} -- call Unbind() before binding to a different character/session.");
            }

            if (!character.IsValid || eventBus == null || characters == null)
            {
                Debug.LogWarning($"[ModelActionCues] Cannot bind '{name}': it needs a valid character, event bus and character registry.", this);
                return;
            }

            this.character = character;
            this.eventBus = eventBus;
            this.characters = characters;
            animator = GetComponentInChildren<CharacterPlayableAnimator>(true);
            binding = new CombatCueEventBinding(character, eventBus, characters, hitPointResourceId, OnHit, OnDown, OnDown, OnRecovered, ctx => lastStriker = ctx.Source);

            if (CombatCueEventBinding.IsDownAtBind(character, characters, hitPointResourceId))
            {
                terminalRequested = true;
                pendingTerminalAt = Time.time; // no delay: nothing is being swung at a character that was already down
            }
        }

        public void Unbind()
        {
            binding?.Dispose();
            binding = null;
            character = RuntimeEntityId.None;
            eventBus = null;
            characters = null;
            pendingHitAt = pendingTerminalAt = -1f;
        }

        /// <summary>See <see cref="IDefeatPresentation"/>: plays the death and reports back after the death clip plus
        /// <see cref="CorpseLingerSeconds"/> -- or never, when the linger is 0 or less.</summary>
        public void BeginDefeat(Action onFinished)
        {
            if (onFinished == null) return;
            defeatCallback = onFinished;
            if (!terminalRequested) OnDown();
        }

        private void OnHit()
        {
            if (terminalRequested || pendingHitAt >= 0f) return;
            pendingHitAt = Time.time + Mathf.Max(0f, Landing(hitDelaySeconds));
        }

        private void OnDown()
        {
            pendingHitAt = -1f;
            if (terminalRequested) return;
            terminalRequested = true;
            pendingTerminalAt = Time.time + Mathf.Max(0f, Landing(terminalDelaySeconds));
        }

        private float Landing(float fallback) => lastStriker.IsValid && LandingDelayRule != null ? Mathf.Max(fallback, LandingDelayRule(lastStriker, character)) : fallback;

        private void OnRecovered()
        {
            pendingTerminalAt = -1f;
            pendingHitAt = -1f;
            terminalRequested = false;
            terminalStartedAt = -1f;
            animator?.Recover();
        }

        private void Update()
        {
            if (!autoBindAttempted && eventBus == null) TryAutoBind();
            if (animator == null) animator = GetComponentInChildren<CharacterPlayableAnimator>(true);
            if (animator == null) return;

            var now = Time.time;
            if (pendingHitAt >= 0f && now >= pendingHitAt)
            {
                pendingHitAt = -1f;
                animator.RequestAction(hitCueId); // OnDown clears a pending hit, so none is ever due once the character is going down
            }

            if (pendingTerminalAt >= 0f && now >= pendingTerminalAt)
            {
                pendingTerminalAt = -1f;
                terminalStartedAt = now;
                animator.RequestTerminal(deathCueId);
            }

            if (defeatCallback != null && terminalStartedAt >= 0f && corpseLingerSeconds > 0f)
            {
                var deathSeconds = animator.CurrentClip != null ? animator.CurrentClip.length : FallbackDeathClipSeconds;
                if (now - terminalStartedAt >= deathSeconds + corpseLingerSeconds)
                {
                    var callback = defeatCallback;
                    defeatCallback = null;
                    callback();
                }
            }
        }

        // WorldSpawnService binds the CharacterAgent but knows nothing about this component, so bind from it once it is ready.
        private void TryAutoBind()
        {
            var agent = GetComponent<CharacterAgent>();
            if (agent == null || !agent.IsBound) return;

            autoBindAttempted = true; // one attempt: MarkDefeated later Unbinds the agent and must not trigger a rebind
            Initialize(agent.EntityId, agent.Session.EventBus, agent.Session.Characters);
        }

        private void OnDestroy() => Unbind();
    }
}

using System;
using System.Collections.Generic;
using CRPG.Core;
using CRPG.Navigation;
using CRPG.Presentation;
using UnityEngine;

namespace CRPG.Gameplay
{
    /// <summary>
    /// Session/entity binding anchor on a spawned character's root GameObject -- not a stat container
    /// (Docs/Design/Production-Foundation.md sec.4.1's prefab contract: CharacterState lives in the owning
    /// GameSession's CharacterRegistry, keyed by <see cref="EntityId"/>). Other components on the same
    /// GameObject (interaction handlers, ...) resolve which entity/session they belong to via
    /// <c>GetComponent&lt;CharacterAgent&gt;()</c> instead of each separately carrying an id and a session
    /// reference.
    /// </summary>
    /// <remarks>
    /// Task 1.3: this class (CRPG.Gameplay) is allowed to reference <see cref="CRPG.Presentation.CharacterView"/>
    /// -- the design's dependency direction is Gameplay -&gt; Presentation -&gt; {Actors, Inventory, Core}. The
    /// reverse is forbidden: CharacterView must never reference CharacterAgent/GameSession, which is why View
    /// resolution happens here (this class reaching down to find it), not the other way around.
    /// </remarks>
    /// <remarks>
    /// Follows the same binding contract Task 1.1 established for CharacterPlayableAnimator/
    /// EquipmentVisualBinder: rebinding the identical (entityId, session) tuple is a no-op; binding to a
    /// different one while already bound is an error and requires an explicit <see cref="Unbind"/> first.
    /// </remarks>
    public sealed class CharacterAgent : MonoBehaviour
    {
        // Task 3.1: the pointer-input pipeline's drag-box selection needs to enumerate "every currently live,
        // selectable character" without a per-frame FindObjectsByType scan (the same "registered adapters, not
        // a scan" principle the design applies to UI pointer capture). CharacterAgent already exists on every
        // spawned character and already carries EntityId, so it's the natural self-registering source rather
        // than a separate marker component -- see SceneSelectableSource, the ISelectableSource this backs.
        private static readonly HashSet<CharacterAgent> liveAgents = new();

        /// <summary>Every currently enabled CharacterAgent in the scene. A live view -- do not cache the
        /// returned collection across frames.</summary>
        public static IReadOnlyCollection<CharacterAgent> AllLive => liveAgents;

        private void OnEnable() => liveAgents.Add(this);

        private void OnDisable() => liveAgents.Remove(this);

        public RuntimeEntityId EntityId { get; private set; } = RuntimeEntityId.None;
        public GameSession Session { get; private set; }

        /// <summary>This character's movement backend, resolved at bind time. Null if this GameObject has
        /// none (a non-mobile NPC, a placeholder fixture, ...) -- callers that need movement must check.</summary>
        public IMovable Motor { get; private set; }

        /// <summary>The art-independent wrapper managing this character's replaceable Model, resolved at bind
        /// time. Null if this GameObject has no VisualRoot/CharacterView (a state-only spawn with no
        /// instantiated art). See CharacterView's own remarks for why it never holds a reference back here.</summary>
        public CharacterView View { get; private set; }

        /// <summary>This character's rotation owner, resolved at bind time (Task 3.3: interaction requests
        /// claim FacingPriority.Interaction through this). Null if this GameObject has none.</summary>
        public FacingController Facing { get; private set; }

        public bool IsBound => Session != null;

        public void Bind(RuntimeEntityId entityId, GameSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            if (Session != null)
            {
                if (EntityId == entityId && ReferenceEquals(Session, session)) return;

                throw new InvalidOperationException(
                    $"{name} is already bound to {EntityId} -- call Unbind() before binding to a different character/session.");
            }

            EntityId = entityId;
            Session = session;
            Motor = GetComponent<IMovable>() ?? GetComponentInChildren<IMovable>();
            View = GetComponentInChildren<CharacterView>(true);
            Facing = GetComponent<FacingController>();
        }

        public void Unbind()
        {
            EntityId = RuntimeEntityId.None;
            Session = null;
            Motor = null;
            View = null;
            Facing = null;
        }

        private void OnDestroy() => Unbind();
    }
}

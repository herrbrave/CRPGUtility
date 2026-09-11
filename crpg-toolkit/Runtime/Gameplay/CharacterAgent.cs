using System;
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
    /// CharacterVisualEquipment: rebinding the identical (entityId, session) tuple is a no-op; binding to a
    /// different one while already bound is an error and requires an explicit <see cref="Unbind"/> first.
    /// </remarks>
    public sealed class CharacterAgent : MonoBehaviour
    {
        public RuntimeEntityId EntityId { get; private set; } = RuntimeEntityId.None;
        public GameSession Session { get; private set; }

        /// <summary>This character's movement backend, resolved at bind time. Null if this GameObject has
        /// none (a non-mobile NPC, a placeholder fixture, ...) -- callers that need movement must check.</summary>
        public IMovable Motor { get; private set; }

        /// <summary>The art-independent wrapper managing this character's replaceable Model, resolved at bind
        /// time. Null if this GameObject has no VisualRoot/CharacterView (a state-only spawn with no
        /// instantiated art). See CharacterView's own remarks for why it never holds a reference back here.</summary>
        public CharacterView View { get; private set; }

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
        }

        public void Unbind()
        {
            EntityId = RuntimeEntityId.None;
            Session = null;
            Motor = null;
            View = null;
        }

        private void OnDestroy() => Unbind();
    }
}

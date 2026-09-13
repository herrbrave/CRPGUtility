using System;
using CRPG.Core;
using UnityEngine;

namespace CRPG.Gameplay
{
    /// <summary>
    /// Unity-facing composition root: constructs one <see cref="GameSession"/>, exposes it to the rest of the
    /// scene, and disposes it on teardown. The formal "ruleset entrypoint receives a typed registration
    /// builder... the shipped composition root accepts that entrypoint via a Unity-serializable provider
    /// adapter" (Docs/Design/Production-Foundation.md sec.3) is Task 4.1's concern -- until that exists, this
    /// takes an already-built <see cref="IRulesDatabase"/>/<see cref="IDiceService"/> via
    /// <see cref="StartSession"/>, matching how every existing demo bootstrap constructs these by hand today.
    /// </summary>
    public sealed class GameSessionHost : MonoBehaviour
    {
        public GameSession Session { get; private set; }

        /// <summary>Constructs and takes ownership of a new GameSession. Throws if this host already has a
        /// live (non-disposed) session -- call <see cref="EndSession"/> first.</summary>
        public GameSession StartSession(IRulesDatabase rulesDatabase, IDiceService diceService)
        {
            if (Session is { IsDisposed: false })
                throw new InvalidOperationException($"{name} already has an active session -- call EndSession() before starting another.");

            Session = new GameSession(rulesDatabase, diceService);
            return Session;
        }

        /// <summary>Disposes the current session, if any. Idempotent.</summary>
        public void EndSession()
        {
            Session?.Dispose();
            Session = null;
        }

        /// <summary>Task 3.5: adopts an already-built session directly, without constructing one -- used by
        /// SessionLoadCoordinator's commit step, once the new session has already been fully built/restored
        /// and validated off to the side. Throws under the same "clear the old one first" guard StartSession
        /// uses; the coordinator is responsible for disposing the previous session itself before calling this
        /// (this method does not do it implicitly, since the coordinator also needs to despawn the previous
        /// session's world agents in a specific order around that disposal -- see SessionLoadCoordinator).</summary>
        public GameSession AdoptSession(GameSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (Session is { IsDisposed: false })
                throw new InvalidOperationException($"{name} already has an active session -- dispose it before adopting another.");

            Session = session;
            return Session;
        }

        private void OnDestroy() => EndSession();
    }
}

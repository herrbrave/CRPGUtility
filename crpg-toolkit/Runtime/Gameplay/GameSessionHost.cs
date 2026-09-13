using System;
using System.Collections.Generic;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Rules;
using UnityEngine;

namespace CRPG.Gameplay
{
    /// <summary>
    /// Unity-facing composition root: constructs one <see cref="GameSession"/>, exposes it to the rest of the
    /// scene, and disposes it on teardown. Two ways to start a session:
    /// <see cref="StartSession(IRulesDatabase, IDiceService)"/> takes an already-built rules database, matching
    /// how every existing demo bootstrap constructs one by hand today (unchanged, still the only path any
    /// current composition root uses); <see cref="StartSession(IRulesetDefinition, IDiceService, IReadOnlyList{Type})"/>
    /// is Task 4.1's new seam -- "a ruleset entrypoint receives a typed registration builder... validate
    /// required registrations before enabling input" (Docs/Design/Production-Foundation.md sec.3).
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

        /// <summary>Builds a fresh session, lets <paramref name="provider"/> register its content and policies
        /// against it, then validates every type in <paramref name="requiredPolicyTypes"/> was actually
        /// registered before exposing the session as <see cref="Session"/> -- on a validation failure the
        /// locally-built session/database/policy-registry are discarded without ever being assigned anywhere,
        /// so a failed start never leaves partially-registered content visible (mirrors
        /// <c>SessionLoadCoordinator</c>'s own build-then-validate-then-commit shape). <paramref
        /// name="requiredPolicyTypes"/> is the caller's own choice of what its game actually needs (e.g. a
        /// combat demo requires <c>IArmorClassRule</c>; a dialogue-only fixture might not) -- this method never
        /// hardcodes which policies matter.</summary>
        public GameSession StartSession(IRulesetDefinition provider, IDiceService diceService, IReadOnlyList<Type> requiredPolicyTypes = null)
        {
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            if (Session is { IsDisposed: false })
                throw new InvalidOperationException($"{name} already has an active session -- call EndSession() before starting another.");

            var rulesDatabase = new RulesDatabase();
            var candidate = new GameSession(rulesDatabase, diceService);
            try
            {
                var builder = new RulesetRegistrationBuilder(rulesDatabase, candidate.Characters, diceService, candidate.Policies);
                provider.Register(builder);
                candidate.Policies.ValidateRequired(requiredPolicyTypes);
            }
            catch
            {
                candidate.Dispose();
                throw;
            }

            Session = candidate;
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

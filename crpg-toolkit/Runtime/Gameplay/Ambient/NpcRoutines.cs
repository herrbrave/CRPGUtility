using System;
using CRPG.Gameplay.Scenes;
using CRPG.Navigation;
using UnityEngine;

namespace CRPG.Gameplay.Ambient
{
    /// <summary>
    /// Brings authored <see cref="NpcRoutineSpec"/>s to life. <see cref="AttachAll"/> is idempotent and keyed by spawn
    /// id: call it after a location's NPCs exist (the bootstrap after spawning, and after
    /// <see cref="LocationInitializer.Prepare"/> or a load, which always build FRESH views with no routine on them).
    /// An NPC that already has a runner, is defeated or has no routine is skipped.
    /// </summary>
    public static class NpcRoutines
    {
        /// <summary>Attaches a runner for <paramref name="spec"/> to one agent. Returns null (and does nothing) when
        /// the spec is Stand or the agent already has a runner. Safe on an inactive GameObject.</summary>
        public static NpcRoutineRunner Attach(
            CharacterAgent agent, NpcRoutineSpec spec, Transform anchor, string spawnId, INavMeshService nav = null, Action<string> log = null)
        {
            if (agent == null || spec == null || !spec.IsActive) return null;
            if (agent.TryGetComponent<NpcRoutineRunner>(out _)) return null;

            var runner = agent.gameObject.AddComponent<NpcRoutineRunner>();
            runner.Initialise(
                agent, spec.Resolve(anchor), Seed(spawnId, spec.Seed), nav ?? new NavMeshService(),
                log ?? (message => Debug.Log($"[routine {spawnId}] {message}", agent)));
            return runner;
        }

        /// <returns>How many runners this call attached.</returns>
        public static int AttachAll(ILocationContent content, WorldSpawnService spawner, INavMeshService nav = null)
        {
            if (content == null || spawner == null) return 0;

            var attached = 0;
            foreach (var point in content.SpawnPoints)
            {
                if (point == null || point.Routine == null || !point.Routine.IsActive || string.IsNullOrEmpty(point.SpawnId)) continue;
                if (!spawner.TryGetCharacterId(point.SpawnId, out var characterId, out _, out var defeated) || defeated) continue;

                CharacterAgent agent = null;
                foreach (var owned in spawner.OwnedAgents)
                {
                    if (owned != null && owned.EntityId == characterId) { agent = owned; break; }
                }

                if (agent == null) continue;
                if (agent.Session != null && agent.Session.Party.IsMember(agent.EntityId)) continue;   // recruited: no longer a townsperson
                if (Attach(agent, point.Routine, point.Anchor, point.SpawnId, nav) != null) attached++;
            }

            return attached;
        }

        /// <summary>FNV-1a of the spawn id (string.GetHashCode is not stable between runtimes) mixed with the authored
        /// seed, so an NPC wanders the same way every run unless a designer changes the seed.</summary>
        public static int Seed(string spawnId, int authoredSeed)
        {
            unchecked
            {
                var hash = (uint)2166136261;
                if (spawnId != null)
                {
                    foreach (var c in spawnId)
                    {
                        hash ^= c;
                        hash *= 16777619;
                    }
                }

                return (int)hash ^ authoredSeed;
            }
        }
    }
}

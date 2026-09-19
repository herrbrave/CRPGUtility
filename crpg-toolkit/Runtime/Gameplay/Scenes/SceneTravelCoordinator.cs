using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CRPG.Camera;
using CRPG.Core;
using CRPG.Navigation;
using CRPG.Party;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CRPG.Gameplay.Scenes
{
    /// <summary>
    /// Task 3.6: in-fiction travel between locations. A persistent Core scene (GameSessionHost, party
    /// CharacterAgents, camera, input, UI) is never unloaded; only a location scene (geometry/NavMesh/NPCs/
    /// interactables) loads/unloads additively. Party members reposition via <see cref="IMovable.Warp"/> (same
    /// primitive as TeleportInteractionAdapter) and destination slots are validated through
    /// <see cref="INavMeshService"/>, so the movement backend is swappable -- they are never moved between
    /// scenes and never destroyed/rebuilt. The SAME GameSession/
    /// WorldSpawnService instance persists across the whole trip (only a cross-scene SAVE LOAD, see
    /// SessionLoadCoordinator, constructs a fresh one).
    /// <para>
    /// Driven as a coroutine by a Core-scene-owned MonoBehaviour runner (never by the outgoing location's own
    /// TravelInteractionAdapter -- that GameObject is destroyed as part of the very transition it would be
    /// driving). Assumes the composition root maintains "the active scene is always the current location scene"
    /// as a standing invariant outside of a transition (Core is loaded but never made active) -- this is what
    /// lets ReleaseSpawnsInScene safely target "whatever the active scene was when travel began" as the
    /// outgoing location.
    /// </para>
    /// </summary>
    public sealed class SceneTravelCoordinator
    {
        private readonly GameSession session;
        private readonly WorldSpawnService worldSpawnService;
        private readonly PartyState party;
        private readonly WorldTransitionGate gate;
        private readonly CameraRigState cameraRig;
        private readonly IFormationSolver formationSolver;
        private readonly float formationSpacing;
        private readonly float placementSampleMaxDistance;
        private readonly INavMeshService navMeshService;

        public SceneTravelCoordinator(
            GameSession session,
            WorldSpawnService worldSpawnService,
            PartyState party,
            WorldTransitionGate gate,
            CameraRigState cameraRig = null,
            IFormationSolver formationSolver = null,
            float formationSpacing = 2f,
            float placementSampleMaxDistance = 5f,
            INavMeshService navMeshService = null)
        {
            this.navMeshService = navMeshService ?? new NavMeshService();
            this.session = session ?? throw new ArgumentNullException(nameof(session));
            this.worldSpawnService = worldSpawnService ?? throw new ArgumentNullException(nameof(worldSpawnService));
            this.party = party ?? throw new ArgumentNullException(nameof(party));
            this.gate = gate ?? throw new ArgumentNullException(nameof(gate));
            this.cameraRig = cameraRig;
            this.formationSolver = formationSolver ?? new WedgeFormationSolver();
            this.formationSpacing = formationSpacing;
            this.placementSampleMaxDistance = placementSampleMaxDistance;
        }

        public IEnumerator TravelCoroutine(string targetScenePath, string destinationId, Action<SceneTravelResult> onFinished)
        {
            if (gate.IsBusy)
            {
                onFinished(SceneTravelResult.Fail(SceneTravelOutcome.BlockedByLoad, $"World transition already in progress ({gate.BusyReason})."));
                yield break;
            }

            gate.BeginBusy("scene-travel");
            var oldScene = SceneManager.GetActiveScene();

            AsyncOperation loadOp = null;
            try
            {
                loadOp = SceneManager.LoadSceneAsync(targetScenePath, LoadSceneMode.Additive);
            }
            catch (Exception ex)
            {
                Fail(SceneTravelOutcome.SceneLoadFailed, ex.Message, onFinished, default);
                yield break;
            }

            if (loadOp == null)
            {
                Fail(SceneTravelOutcome.SceneLoadFailed, $"Scene '{targetScenePath}' could not be loaded (missing from Build Settings?).", onFinished, default);
                yield break;
            }

            yield return loadOp;
            // One defensive settle frame before touching NavMesh queries or scene objects in the freshly
            // (additively) loaded scene. An earlier version of this comment claimed NavMeshAgent.Warp needs a
            // tick to recognize newly added NavMeshData; a later in-process investigation could not reproduce
            // that (same-frame BuildNavMesh/AddData + Warp both succeed), so the original failure's real cause
            // (likely the loaded scene's own OnEnable hooks not having run yet when this coroutine resumed) is
            // unconfirmed. Kept because it is harmless and the original symptom was real.
            yield return null;

            var targetScene = SceneManager.GetSceneByPath(targetScenePath);
            if (!targetScene.IsValid())
            {
                Fail(SceneTravelOutcome.SceneLoadFailed, $"Scene '{targetScenePath}' loaded but could not be resolved.", onFinished, default);
                yield break;
            }

            var destination = FindDestination(targetScene, destinationId);
            if (destination == null)
            {
                Fail(SceneTravelOutcome.DestinationNotFound, $"No SceneTravelDestination '{destinationId}' found in '{targetScenePath}'.", onFinished, targetScene);
                yield break;
            }

            var content = FindLocationContent(targetScene);

            var members = party.Members;
            var positions = formationSolver.Solve(destination.Anchor.position, destination.Anchor.forward, members.Count, formationSpacing);

            // Keep the SAMPLED point, not the raw slot: warping to a raw slot that is merely near the mesh
            // makes NavMeshAgent silently relocate the character to the mesh next frame, so travel would
            // report success while several nearby slots collapse onto the same edge point.
            var placements = new Vector3[positions.Count];
            for (var i = 0; i < positions.Count; i++)
            {
                if (!navMeshService.TrySamplePosition(positions[i], out placements[i], placementSampleMaxDistance))
                {
                    Fail(SceneTravelOutcome.PlacementValidationFailed, $"Destination slot {i} for '{destinationId}' is not on a valid walkable surface.", onFinished, targetScene);
                    yield break;
                }
            }

            if (content != null)
            {
                try
                {
                    LocationInitializer.Prepare(content, session, worldSpawnService, targetScene);
                }
                catch (Exception ex)
                {
                    Fail(SceneTravelOutcome.LocationPrepareFailed, ex.Message, onFinished, targetScene);
                    yield break;
                }
            }

            var originalTransforms = new Dictionary<RuntimeEntityId, (Vector3 position, Quaternion rotation)>();
            foreach (var memberId in members)
            {
                var agent = FindAgent(memberId);
                if (agent != null) originalTransforms[memberId] = (agent.transform.position, agent.transform.rotation);
            }

            var facing = destination.Anchor.forward.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(destination.Anchor.forward)
                : Quaternion.identity;

            // Every member a warp was ATTEMPTED for, including the one that fails: a backend's failed Warp may
            // still have moved that mover (raw NavMeshAgent.Warp does), so rollback must not assume the failing
            // member stayed put.
            var attempted = new List<CharacterAgent>();
            var repositionFailed = false;
            for (var i = 0; i < members.Count; i++)
            {
                var agent = FindAgent(members[i]);
                var motor = agent != null ? agent.Motor : null;
                if (agent != null) attempted.Add(agent);
                if (motor == null || !motor.Warp(placements[i]))
                {
                    repositionFailed = true;
                    break;
                }

                agent.transform.rotation = facing;
            }

            if (repositionFailed)
            {
                foreach (var agent in attempted)
                {
                    if (!originalTransforms.TryGetValue(agent.EntityId, out var original)) continue;
                    agent.Motor?.Warp(original.position);
                    agent.transform.rotation = original.rotation;
                }

                Fail(SceneTravelOutcome.PartyRepositionFailed, "Failed to reposition one or more party members at the destination; rolled back.", onFinished, targetScene);
                yield break;
            }

            // Commit. Releases the OLD location's own NPCs only -- never the party (Core scene) and never the
            // NEW location's NPCs LocationInitializer.Prepare just staged (they're in targetScene, not oldScene).
            worldSpawnService.ReleaseSpawnsInScene(oldScene);

            SceneManager.SetActiveScene(targetScene);
            if (cameraRig != null && content != null) cameraRig.SetBounds(content.CameraBoundsXZ);
            SceneManager.UnloadSceneAsync(oldScene);

            gate.EndBusy();
            onFinished(SceneTravelResult.Ok());
        }

        /// <summary>Unloads targetScene (if it was ever loaded), clears the gate, and reports failure -- the
        /// shared cleanup path for every failure branch above the reposition step. The old scene/session is
        /// never touched by any of these branches.</summary>
        private void Fail(SceneTravelOutcome outcome, string message, Action<SceneTravelResult> onFinished, Scene targetScene)
        {
            if (targetScene.IsValid()) SceneManager.UnloadSceneAsync(targetScene);
            gate.EndBusy();
            onFinished(SceneTravelResult.Fail(outcome, message));
        }

        private static SceneTravelDestination FindDestination(Scene scene, string destinationId) =>
            UnityEngine.Object.FindObjectsByType<SceneTravelDestination>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(d => d.gameObject.scene == scene && d.DestinationId == destinationId);

        private static ILocationContent FindLocationContent(Scene scene) =>
            UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .OfType<ILocationContent>()
                .FirstOrDefault(c => ((MonoBehaviour)c).gameObject.scene == scene);

        private static CharacterAgent FindAgent(RuntimeEntityId id) =>
            CharacterAgent.AllLive.FirstOrDefault(a => a != null && a.EntityId == id);
    }
}

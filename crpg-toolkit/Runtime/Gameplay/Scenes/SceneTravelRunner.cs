using System.Collections;
using UnityEngine;

namespace CRPG.Gameplay.Scenes
{
    /// <summary>Task 3.6: lives in the persistent Core scene -- the shared coroutine host for BOTH a
    /// SceneTravelCoordinator.TravelCoroutine and a SessionLoadCoordinator.LoadCoroutine (see
    /// SessionLoadRequestAdapter), since neither's coroutine can safely run on a GameObject that might be
    /// destroyed as part of the very transition it's driving (the outgoing location's own
    /// TravelInteractionAdapter, in Travel's case).</summary>
    public sealed class SceneTravelRunner : MonoBehaviour
    {
        public SceneTravelCoordinator Coordinator { get; set; }

        public void Travel(string targetScenePath, string destinationId, System.Action<SceneTravelResult> onFinished) =>
            StartCoroutine(Coordinator.TravelCoroutine(targetScenePath, destinationId, onFinished));

        public Coroutine Run(IEnumerator routine) => StartCoroutine(routine);
    }
}

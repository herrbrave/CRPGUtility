using UnityEngine;
using UnityEngine.AI;

namespace CRPG.Navigation
{
    /// <summary>Reference <see cref="IMovable"/> backed by a NavMeshAgent on the same GameObject.</summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class NavMeshCharacterMotor : MonoBehaviour, IMovable
    {
        [SerializeField] private float arrivalThreshold = 0.1f;

        private NavMeshAgent agent;

        private void Awake() => agent = GetComponent<NavMeshAgent>();

        public void SetDestination(Vector3 destination) => agent.SetDestination(destination);

        public void Stop() => agent.ResetPath();

        public bool HasArrived => !agent.pathPending && agent.remainingDistance <= Mathf.Max(arrivalThreshold, agent.stoppingDistance);

        public float RemainingDistance => agent.pathPending ? float.PositiveInfinity : agent.remainingDistance;
    }
}

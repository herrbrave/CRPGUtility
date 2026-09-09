using UnityEngine;

namespace CRPG.Navigation
{
    /// <summary>Whatever executes movement for one entity toward a destination -- currently backed by a
    /// NavMeshAgent (<see cref="NavMeshCharacterMotor"/>), but higher-level code (Exploration, Combat) depends
    /// only on this interface so a different movement implementation could replace it without touching them.</summary>
    public interface IMovable
    {
        void SetDestination(Vector3 destination);
        void Stop();
        bool HasArrived { get; }
        float RemainingDistance { get; }
    }
}

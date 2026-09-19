using UnityEngine.AI;

namespace CRPG.Navigation
{
    /// <summary>Whatever makes a doorway/portcullis obstruct or admit movement for one movement backend --
    /// a carving NavMeshObstacle for the NavMesh backend (<see cref="NavMeshObstacleWalkabilityBlocker"/>), a set of
    /// blocked grid cells for the grid backend. Lets interaction adapters toggle passability without knowing
    /// which backend is active.</summary>
    public interface IWalkabilityBlocker
    {
        bool IsBlocking { get; }

        /// <summary>true = obstructs movement, false = passable.</summary>
        void SetBlocking(bool blocking);
    }

    /// <summary>NavMesh-backend <see cref="IWalkabilityBlocker"/>: a disabled obstacle doesn't carve, so
    /// "blocking" is exactly <c>obstacle.enabled</c>. A plain wrapper (not a component) so an existing door
    /// whose serialized NavMeshObstacle reference is already authored keeps working with no scene edits.</summary>
    public sealed class NavMeshObstacleWalkabilityBlocker : IWalkabilityBlocker
    {
        private readonly NavMeshObstacle obstacle;

        public NavMeshObstacleWalkabilityBlocker(NavMeshObstacle obstacle) => this.obstacle = obstacle;

        public bool IsBlocking => obstacle != null && obstacle.enabled;

        public void SetBlocking(bool blocking)
        {
            if (obstacle != null) obstacle.enabled = blocking;
        }
    }
}

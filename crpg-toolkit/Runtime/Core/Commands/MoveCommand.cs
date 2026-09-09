using UnityEngine;

namespace CRPG.Core
{
    public readonly struct MoveCommand
    {
        public MoveCommand(Vector3 destination, Vector3 facing)
        {
            Destination = destination;
            Facing = facing;
        }

        public Vector3 Destination { get; }
        public Vector3 Facing { get; }
    }
}

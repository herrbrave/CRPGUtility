using System;
using UnityEngine;

namespace VikingRaid.Game
{
    /// <summary>
    /// Typed handles to everything the game loop needs from the authored scene, so Task 4 never finds objects by name. Lives on the scene's <c>Anchors</c> root and is
    /// filled by the world authoring script from <see cref="WorldLayout"/>. Pure data: positions the party and goblins start a fight on, the trigger volume that begins it,
    /// the checkpoint a wipe returns to, and a camera framing hint.
    /// </summary>
    public sealed class WorldAnchors : MonoBehaviour
    {
        [Serializable]
        public sealed class EncounterAnchors
        {
            public string id;
            public string displayName;
            public string[] goblinUnitIds;
            /// <summary>Where the four heroes stand when the fight starts (the formation the balance simulation assumed). Task 4 walks the party here on trigger.</summary>
            public Transform[] partyStage;
            /// <summary>One per entry of <see cref="goblinUnitIds"/>, same order; goblins stand here idle until the fight starts.</summary>
            public Transform[] goblinSpawns;
            /// <summary>Four slots (party order) a wipe returns the party to; all on the road, well before the trigger.</summary>
            public Transform[] checkpointSlots;
            /// <summary>Box trigger across the road on the Trigger layer; ignore it while a fight is running.</summary>
            public BoxCollider trigger;
            /// <summary>The middle of the arena and the heading the road runs: where a tactical camera should look.</summary>
            public Transform cameraTarget;
            public float roadYawDegrees;
        }

        public Transform[] startSlots;
        public EncounterAnchors[] encounters;
        public Transform victoryCamera;
    }
}

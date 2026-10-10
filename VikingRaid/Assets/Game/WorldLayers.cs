using UnityEngine;

namespace VikingRaid.Game
{
    /// <summary>
    /// The physics layers Viking Raid uses and the masks built from them. One definition for the authoring scripts, the game and the tests.
    /// Why each mask is what it is (all of it matters because the toolkit takes masks as injected constructor arguments, with no defaults):
    /// <list type="bullet">
    /// <item><b>Hittable</b> (click-to-move AND the ability targeting gate raycast only this): ground, characters, scenery. NOT obstacles/trees -- a click on a tree trunk
    /// should become a move to the nearest walkable point, not be swallowed.</item>
    /// <item><b>SightBlocker</b> (<c>PhysicsLineOfSightProvider</c>): obstacles only. Ground must not block (a bump would block every eye-height shot); canopies have no collider.</item>
    /// <item><b>NavMesh</b>: ground plus obstacles (obstacles carve walls; their tops are forced Not Walkable by a NavMeshModifier). Scenery is deliberately absent so the
    /// forest floor outside the road is not walkable.</item>
    /// </list>
    /// </summary>
    public static class WorldLayers
    {
        public const int Ground = 8, Obstacle = 9, Trigger = 10, Character = 11, Scenery = 12;

        public static readonly string[] Names = { "Ground", "Obstacle", "Trigger", "Character", "Scenery" };

        public static int Index(int layer) => layer;

        public static LayerMask Hittable => (1 << Ground) | (1 << Character) | (1 << Scenery);
        public static LayerMask SightBlocker => 1 << Obstacle;
        public static LayerMask NavMesh => (1 << Ground) | (1 << Obstacle);
        public static LayerMask TriggerOnly => 1 << Trigger;

        public static void SetRecursive(GameObject root, int layer)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        }
    }
}

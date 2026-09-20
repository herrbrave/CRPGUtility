using UnityEngine;

namespace BarrowsEnd
{
    /// <summary>
    /// Barrow's End's physics layers (declared in ProjectSettings/TagManager.asset; a shippable package cannot own project layer names, so the game
    /// owns them and passes them to the toolkit). <b>Ground</b> holds each location's ground plane: the click router raycasts it. <b>WorldObstacle</b> holds
    /// baked wall boxes: line of sight is tested against it and the router's mask EXCLUDES it. (Not the same thing as the sorting layers of the same names.)
    /// </summary>
    public static class BarrowsEndLayers
    {
        public const int Ground = 8;
        public const int WorldObstacle = 9;

        public static int GroundMask => 1 << Ground;
        public static int ObstacleMask => 1 << WorldObstacle;

        /// <summary>What the click router should be able to hit: everything except baked wall boxes.</summary>
        public static int RouterMask => ~ObstacleMask;

        /// <summary>What line-of-sight checks (PhysicsLineOfSightProvider) should treat as blocking.</summary>
        public static int LineOfSightMask => ObstacleMask;
    }
}

namespace CRPG.Core
{
    /// <summary>
    /// What an observer can see by facing alone: a forward cone (a half angle in degrees and a range in metres) plus a short all-round awareness circle. A cone half angle of 180 or more is all-round, a range of 0 or less is unlimited.
    /// The SRD has no cones: a creature in a fight sees all round at any distance (<see cref="Panoramic"/>), so cones belong to unalerted creatures (sneaking, surprise), which the game opts into by supplying a narrower profile.
    /// </summary>
    public readonly struct VisionProfile
    {
        public VisionProfile(float coneHalfAngle, float coneRange, float awarenessRadius)
        {
            ConeHalfAngle = coneHalfAngle;
            ConeRange = coneRange;
            AwarenessRadius = awarenessRadius;
        }

        public float ConeHalfAngle { get; }
        public float ConeRange { get; }
        public float AwarenessRadius { get; }

        /// <summary>The SRD's sight: all round, no range limit.</summary>
        public static VisionProfile Panoramic => new(180f, 0f, 0f);

        /// <summary>An unalerted watcher: a 120 degree fan out to 20 m and a 2 m circle round itself.</summary>
        public static VisionProfile Watchful => new(60f, 20f, 2f);
    }
}

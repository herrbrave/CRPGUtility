namespace CRPG.Core
{
    /// <summary>Which way an entity is looking, as a yaw in degrees (0 faces +Z, 90 faces +X). Vision cones read it; the game supplies it from the actor's transform.</summary>
    public interface IFacingProvider
    {
        float GetFacingYaw(RuntimeEntityId entity);
    }
}

using UnityEngine;

namespace CRPG.Core
{
    /// <summary>The light level at a world point. Lives in CRPG.Core beside <see cref="IPositionProvider"/> and <see cref="ILineOfSightProvider"/> for the same reason: perception needs it and no higher assembly can host it. What lights a place (ambient, zones, torches) is the game's decision, so nothing ships a default.</summary>
    public interface ILightProvider
    {
        LightLevel LightAt(Vector3 point);
    }
}

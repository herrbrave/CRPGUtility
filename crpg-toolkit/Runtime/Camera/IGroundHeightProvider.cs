using UnityEngine;

namespace CRPG.Camera
{
    /// <summary>Reserved seam for the design's "terrain handling" -- lets <see cref="CameraRigState"/> keep
    /// its focus point at ground level on uneven terrain, without this assembly ever depending on
    /// UnityEngine.Terrain or any specific ground representation. No real implementation ships with the
    /// package (there is no Terrain content anywhere in this project to design or test one against); a game
    /// with real terrain provides its own. This is explicitly a height-only seam -- it does not address
    /// camera-vs-geometry obstruction/occlusion, which is a separate, larger, still-deferred concern.</summary>
    public interface IGroundHeightProvider
    {
        bool TryGetGroundHeight(Vector3 xzPosition, out float height);
    }
}

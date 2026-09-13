using System;

namespace CRPG.Saving
{
    /// <summary>UI-facing seam for requesting a load, implemented by CRPG.Gameplay's SessionLoadCoordinator.
    /// Same cycle-avoidance reasoning as <see cref="ISaveRequestPort"/>. Callback-based (not async/await, which
    /// nothing in this codebase uses elsewhere) since a cross-scene load is inherently multi-frame -- onFinished
    /// fires exactly once, whether the load resolved synchronously (same-scene) or across several frames
    /// (cross-scene).</summary>
    public interface ILoadRequestPort
    {
        bool IsLoading { get; }
        void RequestLoad(string path, Action<LoadOutcomeReport> onFinished);
    }
}

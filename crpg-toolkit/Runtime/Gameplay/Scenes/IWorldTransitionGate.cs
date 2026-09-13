namespace CRPG.Gameplay.Scenes
{
    /// <summary>Task 3.6: the single shared "is the world mid-transition" flag -- one WorldTransitionGate
    /// instance is constructed once by the composition root and handed to SafePointGate, SceneTravelCoordinator,
    /// and SessionLoadCoordinator (including its already-existing same-scene path) alike, as a REQUIRED
    /// dependency, not an optional one. Two independently-optional flags (one per subsystem) would not actually
    /// cross-guard each other -- a same-scene load could start while a travel is mid-flight, or vice versa,
    /// with neither side aware of the other. A single shared gate closes that hole by construction.</summary>
    public interface IWorldTransitionGate
    {
        bool IsBusy { get; }
        string BusyReason { get; }
    }
}

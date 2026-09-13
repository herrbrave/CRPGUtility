using System;

namespace CRPG.Gameplay.Scenes
{
    /// <summary>Concrete IWorldTransitionGate -- constructed once by the composition root and shared by
    /// SceneTravelCoordinator and SessionLoadCoordinator (both call BeginBusy/EndBusy around their own
    /// transaction) and read by SafePointGate/PlayerCommandRouter. Not reentrant by design -- BeginBusy throws
    /// if already busy, since the two coordinators cross-checking IsBusy before calling BeginBusy is exactly
    /// what's supposed to prevent this from ever happening; a throw here means that guard was bypassed, a real
    /// bug worth surfacing loudly rather than silently overwriting the in-progress reason.</summary>
    public sealed class WorldTransitionGate : IWorldTransitionGate
    {
        public bool IsBusy { get; private set; }
        public string BusyReason { get; private set; }

        public void BeginBusy(string reason)
        {
            if (IsBusy) throw new InvalidOperationException($"WorldTransitionGate is already busy ({BusyReason}) -- cannot also begin '{reason}'.");

            IsBusy = true;
            BusyReason = reason;
        }

        public void EndBusy()
        {
            IsBusy = false;
            BusyReason = null;
        }
    }
}

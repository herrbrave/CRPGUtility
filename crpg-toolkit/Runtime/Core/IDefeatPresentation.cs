using System;

namespace CRPG.Core
{
    /// <summary>
    /// Lives in CRPG.Core so a presentation component (CRPG.Presentation) can implement it while the spawn service (CRPG.Gameplay) finds it.
    /// Optional seam on a spawned character's view: "I play my own defeat before I am removed". Without it,
    /// WorldSpawnService.MarkDefeated deactivates and destroys the GameObject synchronously, INSIDE the CreatureDied event
    /// publish (the event bus invokes handlers synchronously and TacticalEncounterController calls MarkDefeated from its CreatureDied
    /// handler), so no death presentation could ever render. A view that implements this is instead made inert, given the chance to
    /// play, and destroyed when it reports back.
    /// <para>The implementer MUST invoke <paramref name="onFinished"/> exactly once (immediately is fine); the spawn service does not time
    /// it out, so a presentation that never finishes leaves the GameObject alive. See the KNOWN LIMITATIONS note in
    /// WorldSpawnService.MarkDefeated (CRPG.Gameplay).</para>
    /// </summary>
    public interface IDefeatPresentation
    {
        void BeginDefeat(Action onFinished);
    }
}

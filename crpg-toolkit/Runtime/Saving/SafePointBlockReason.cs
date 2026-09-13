namespace CRPG.Saving
{
    /// <summary>Task 3.5: "Safe-point save requests explain why combat/action/dialogue/transition blocks
    /// saving" (Docs/Design/Production-Foundation.md sec.7). Lives in CRPG.Saving (not CRPG.Gameplay, where
    /// SafePointGate itself lives) so a UI-facing SaveRequestResult (Task 3.6) can carry one without CRPG.UI
    /// needing to reference CRPG.Gameplay -- CRPG.Gameplay already references CRPG.UI, so the reverse would be
    /// a cycle. "Action"/tactical-orchestration blocking has no real concept to check yet (Task 4.3's job) --
    /// not fabricated here.</summary>
    public enum SafePointBlockReason
    {
        None,
        CombatActive,
        InteractionInProgress,
        LoadInProgress,
        SceneTransitionInProgress,
    }
}

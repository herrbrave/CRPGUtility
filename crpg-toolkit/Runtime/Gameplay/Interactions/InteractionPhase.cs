namespace CRPG.Gameplay.Interactions
{
    /// <summary>Where one in-flight <see cref="InteractionRequest"/> is in the shared lifecycle (Docs/Design/
    /// Production-Foundation.md: approach -> revalidate -> execute -> release). There is no separate "idle"
    /// phase -- a request only exists while it is active.</summary>
    public enum InteractionPhase
    {
        /// <summary>Moving toward the target's interaction point. Motion (not Interaction) owns facing for the
        /// whole of this phase, so a detour around an obstacle isn't fought by a premature facing claim.</summary>
        Approaching,

        /// <summary>Stationary at the interaction point, claiming FacingPriority.Interaction and waiting for
        /// the turn to complete before the verb actually executes.</summary>
        TurningToFace,

        /// <summary>The adapter's BeginExecute has been called; waiting on its completion callback.</summary>
        Executing
    }
}

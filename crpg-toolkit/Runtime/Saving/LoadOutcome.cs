namespace CRPG.Saving
{
    /// <summary>Moved here from CRPG.Gameplay's SessionLoadCoordinator (Task 3.6) -- pure data, no
    /// Gameplay-type references, so a UI-facing <see cref="LoadOutcomeReport"/>/<see cref="ILoadRequestPort"/>
    /// never needs to reference CRPG.Gameplay directly. The full LoadResult (which DOES carry live
    /// GameSession/WorldSpawnService references) stays in CRPG.Gameplay -- only the composition root, never a
    /// UI presenter, needs those.</summary>
    public enum LoadOutcome
    {
        Success,
        LoadAlreadyInProgress,
        FileNotFound,
        IoError,
        Corrupt,
        UnsupportedNewerVersion,
        NoMigrationPath,
        SceneMismatch,
        ContentValidationFailed,
        ViewBindingFailed,
        SceneLoadFailed,
        WorldTransitionBusy,
    }
}

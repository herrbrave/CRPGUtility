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

        /// <summary>Task 5.2: the save's non-empty Header.RulesetId doesn't match the ruleset the loader was
        /// given to reconstruct the session with.</summary>
        RulesetMismatch,

        /// <summary>Task 5.2: the ruleset provider's Register() didn't register every policy type the loader
        /// requires (mirrors GameSessionHost.StartSession's own RulesetValidationException case, surfaced here
        /// as a LoadOutcome instead of a thrown exception since Load/LoadCoroutine never throw on legitimate
        /// content problems).</summary>
        RulesetValidationFailed,
    }
}

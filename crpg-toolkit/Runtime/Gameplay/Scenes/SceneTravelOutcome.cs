namespace CRPG.Gameplay.Scenes
{
    public enum SceneTravelOutcome
    {
        Success,
        AlreadyInProgress,
        BlockedByLoad,
        SceneLoadFailed,
        DestinationNotFound,
        LocationPrepareFailed,
        PlacementValidationFailed,
        PartyRepositionFailed,
    }
}

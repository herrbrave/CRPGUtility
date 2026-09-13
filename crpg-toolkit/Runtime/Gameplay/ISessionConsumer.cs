namespace CRPG.Gameplay
{
    /// <summary>Task 3.6: implemented by any composition-root-held object that caches session-scoped
    /// references (presenters, InteractionController, SafePointGate, interaction adapters) and needs to swap
    /// them whenever the live session changes. Makes "re-wiring a consumer after a load is the composition
    /// root's job" (Task 3.5's own documented caveat on SessionLoadCoordinator) an explicit, single call site
    /// (see SessionConsumerRegistry) instead of an ad hoc per-caller reminder.</summary>
    public interface ISessionConsumer
    {
        void RebindSession(GameSession session, WorldSpawnService worldSpawnService);
    }
}

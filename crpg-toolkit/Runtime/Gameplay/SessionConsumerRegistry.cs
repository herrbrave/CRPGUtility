using System.Collections.Generic;

namespace CRPG.Gameplay
{
    /// <summary>Task 3.6: owned by the Core-scene composition root. Every ISessionConsumer it constructs
    /// (presenters, controllers, adapters) registers here; the composition root calls RebindAll exactly once,
    /// immediately after any successful load (same-scene or cross-scene) hands back a new GameSession/
    /// WorldSpawnService -- the single call site that replaces "re-wiring is the composition root's job, good
    /// luck" with something concrete and easy to get right.</summary>
    public sealed class SessionConsumerRegistry
    {
        private readonly List<ISessionConsumer> consumers = new();

        public void Register(ISessionConsumer consumer) => consumers.Add(consumer);
        public void Unregister(ISessionConsumer consumer) => consumers.Remove(consumer);

        public void RebindAll(GameSession session, WorldSpawnService worldSpawnService)
        {
            foreach (var consumer in consumers) consumer.RebindSession(session, worldSpawnService);
        }
    }
}

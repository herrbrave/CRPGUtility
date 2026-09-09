using System.Collections.Generic;

namespace CRPG.Core
{
    /// <summary>One occurrence of a <see cref="GameEvent"/>. <see cref="Data"/> is a loose payload bag
    /// (a roll result, a damage amount, ...) rather than a subclass per event -- it's a transient in-memory
    /// notification, never persisted, so the flexibility outweighs the lost type safety here.</summary>
    public sealed class GameEventContext
    {
        public GameEventContext(GameEvent gameEvent, RuntimeEntityId source = default, RuntimeEntityId target = default)
        {
            Event = gameEvent;
            Source = source;
            Target = target;
        }

        public GameEvent Event { get; }
        public RuntimeEntityId Source { get; }
        public RuntimeEntityId Target { get; }
        public Dictionary<string, object> Data { get; } = new();
    }
}

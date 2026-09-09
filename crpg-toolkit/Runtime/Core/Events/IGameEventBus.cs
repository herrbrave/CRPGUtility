using System;

namespace CRPG.Core
{
    public interface IGameEventBus
    {
        void Subscribe(GameEvent gameEvent, Action<GameEventContext> handler);
        void Unsubscribe(GameEvent gameEvent, Action<GameEventContext> handler);
        void Publish(GameEventContext context);
    }
}

using System;
using System.Collections.Generic;

namespace CRPG.Core
{
    public sealed class GameEventBus : IGameEventBus
    {
        private readonly Dictionary<GameEvent, Action<GameEventContext>> handlersByEvent = new();

        public void Subscribe(GameEvent gameEvent, Action<GameEventContext> handler)
        {
            handlersByEvent.TryGetValue(gameEvent, out var existing);
            handlersByEvent[gameEvent] = existing + handler;
        }

        public void Unsubscribe(GameEvent gameEvent, Action<GameEventContext> handler)
        {
            if (!handlersByEvent.TryGetValue(gameEvent, out var existing)) return;
            handlersByEvent[gameEvent] = existing - handler;
        }

        public void Publish(GameEventContext context)
        {
            if (handlersByEvent.TryGetValue(context.Event, out var handlers)) handlers?.Invoke(context);
        }
    }
}

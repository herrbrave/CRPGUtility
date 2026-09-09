using CRPG.Core;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    public class GameEventBusTests
    {
        [Test]
        public void PublishInvokesSubscribedHandlerForMatchingEvent()
        {
            var bus = new GameEventBus();
            GameEventContext received = null;
            bus.Subscribe(GameEvent.TurnStarted, context => received = context);

            var published = new GameEventContext(GameEvent.TurnStarted);
            bus.Publish(published);

            Assert.AreSame(published, received);
        }

        [Test]
        public void PublishDoesNotInvokeHandlersForOtherEvents()
        {
            var bus = new GameEventBus();
            var invoked = false;
            bus.Subscribe(GameEvent.TurnStarted, _ => invoked = true);

            bus.Publish(new GameEventContext(GameEvent.TurnEnded));

            Assert.IsFalse(invoked);
        }

        [Test]
        public void UnsubscribeStopsFurtherNotifications()
        {
            var bus = new GameEventBus();
            var callCount = 0;
            void Handler(GameEventContext _) => callCount++;

            bus.Subscribe(GameEvent.ShortRest, Handler);
            bus.Publish(new GameEventContext(GameEvent.ShortRest));
            bus.Unsubscribe(GameEvent.ShortRest, Handler);
            bus.Publish(new GameEventContext(GameEvent.ShortRest));

            Assert.AreEqual(1, callCount);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Presentation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CRPG.Tests.EditMode
{
    /// <summary>The presentation-neutral event mapping shared by the sprite and 3D model cues (<c>SpriteCueEventBindingTests</c> cover it end to end through the sprite state).</summary>
    public class CombatCueEventBindingTests
    {
        private static readonly DefinitionId HitPoints = new("resource-hp");

        private CharacterRegistry registry;
        private GameEventBus bus;
        private CharacterState character;
        private RuntimeEntityId id;
        private ResourceState hp;
        private readonly List<string> calls = new();
        private CombatCueEventBinding binding;

        [SetUp]
        public void SetUp()
        {
            registry = new CharacterRegistry();
            bus = new GameEventBus();
            character = new CharacterState(RuntimeEntityId.NewId());
            character.AddResource(new ResourceState(HitPoints, max: 20));
            registry.Register(character);
            id = character.Id;
            character.TryGetResource(HitPoints, out hp);
            calls.Clear();
            binding = Create();
        }

        [TearDown]
        public void TearDown() => binding?.Dispose();

        private CombatCueEventBinding Create(DefinitionId hitPointId = default, Action<GameEventContext> onContext = null, Action onHit = null, bool noRecovered = false)
        {
            if (!hitPointId.IsValid) hitPointId = HitPoints;
            return new CombatCueEventBinding(id, bus, registry, hitPointId,
                onHit ?? (() => calls.Add("hit")), () => calls.Add("down"), () => calls.Add("died"), noRecovered ? null : () => calls.Add("recovered"), onContext);
        }

        private void Publish(GameEvent e, RuntimeEntityId target, RuntimeEntityId? source = null) =>
            bus.Publish(new GameEventContext(e, source ?? RuntimeEntityId.NewId(), target));

        [Test]
        public void EachEventMapsToItsCallbackForTheTargetCharacterOnly()
        {
            var other = RuntimeEntityId.NewId();
            Publish(GameEvent.AfterDamage, other);
            Publish(GameEvent.CreatureDroppedToZeroHP, other);
            Publish(GameEvent.CreatureDied, other);
            Assert.IsEmpty(calls, "events for another character are ignored");

            Publish(GameEvent.AfterDamage, id);
            Publish(GameEvent.CreatureDroppedToZeroHP, id);
            Publish(GameEvent.CreatureDied, id);

            CollectionAssert.AreEqual(new[] { "hit", "down", "died" }, calls);
        }

        [Test]
        public void RecoveredFiresOnBothStabilizedAndConditionRemovedButOnlyWithHitPointsAboveZero()
        {
            hp.Current = 0;
            Publish(GameEvent.CreatureStabilized, id);
            Publish(GameEvent.ConditionRemoved, id);
            Assert.IsEmpty(calls, "three successful death saves leave the character stable at 0 HP, not up");

            hp.Current = 4;
            Publish(GameEvent.CreatureStabilized, id);
            Publish(GameEvent.ConditionRemoved, id);
            CollectionAssert.AreEqual(new[] { "recovered", "recovered" }, calls);
        }

        [Test]
        public void WithoutAHitPointIdOrAnUnknownCharacterRecoveredNeverFires()
        {
            binding.Dispose();
            binding = new CombatCueEventBinding(id, bus, registry, default, null, null, null, () => calls.Add("recovered"));
            Publish(GameEvent.CreatureStabilized, id);

            var stranger = RuntimeEntityId.NewId();
            using var strangerBinding = new CombatCueEventBinding(stranger, bus, registry, HitPoints, null, null, null, () => calls.Add("recovered"));
            Publish(GameEvent.ConditionRemoved, stranger);

            Assert.IsEmpty(calls, "do not guess");
        }

        [Test]
        public void OnContextSeesTheEventForHitDownAndDiedBeforeTheCallbackButNotForRecovered()
        {
            binding.Dispose();
            var order = new List<string>();
            RuntimeEntityId lastSource = default;
            binding = new CombatCueEventBinding(id, bus, registry, HitPoints, () => order.Add("hit"), () => order.Add("down"), () => order.Add("died"), () => order.Add("recovered"),
                ctx => { order.Add("ctx:" + ctx.Event); lastSource = ctx.Source; });
            var striker = RuntimeEntityId.NewId();

            Publish(GameEvent.AfterDamage, id, striker);
            Assert.AreEqual(striker, lastSource, "the consumer learns WHO struck");
            Publish(GameEvent.CreatureDroppedToZeroHP, id);
            Publish(GameEvent.CreatureDied, id);
            hp.Current = 3;
            Publish(GameEvent.CreatureStabilized, id);

            CollectionAssert.AreEqual(new[] { "ctx:AfterDamage", "hit", "ctx:CreatureDroppedToZeroHP", "down", "ctx:CreatureDied", "died", "recovered" }, order);
        }

        [Test]
        public void ANullCallbackSkipsOnContextToo()
        {
            binding.Dispose();
            var contextCalls = 0;
            binding = new CombatCueEventBinding(id, bus, registry, HitPoints, null, null, null, null, _ => contextCalls++);

            Publish(GameEvent.AfterDamage, id);
            Publish(GameEvent.CreatureDied, id);

            Assert.AreEqual(0, contextCalls);
        }

        [Test]
        public void AThrowingOnContextSkipsTheCallbackAndIsLoggedNotPropagated()
        {
            binding.Dispose();
            binding = Create(onContext: _ => throw new InvalidOperationException("boom"));
            LogAssert.Expect(LogType.Exception, new Regex("boom"));

            Assert.DoesNotThrow(() => Publish(GameEvent.AfterDamage, id), "a presentation fault must never fault the damage being applied");
            Assert.IsEmpty(calls, "onContext and the callback share one guard: a throw skips the callback");
        }

        [Test]
        public void AThrowingCallbackDoesNotStopLaterSubscribersOfTheSamePublish()
        {
            binding.Dispose();
            binding = Create(onHit: () => throw new InvalidOperationException("boom"));
            var later = 0;
            bus.Subscribe(GameEvent.AfterDamage, _ => later++);
            LogAssert.Expect(LogType.Exception, new Regex("boom"));

            Publish(GameEvent.AfterDamage, id);

            Assert.AreEqual(1, later);
        }

        [Test]
        public void DisposeStopsEveryEventAndIsIdempotent()
        {
            binding.Dispose();
            binding.Dispose();

            Publish(GameEvent.AfterDamage, id);
            Publish(GameEvent.CreatureDied, id);

            Assert.IsEmpty(calls);
        }

        [Test]
        public void DisposingFromAnEarlierHandlerOfTheSamePublishStopsTheBinding()
        {
            binding.Dispose();
            var shared = new GameEventBus();
            CombatCueEventBinding late = null;
            shared.Subscribe(GameEvent.AfterDamage, _ => late.Dispose());   // subscribed BEFORE the binding: runs first, inside the publish
            late = new CombatCueEventBinding(id, shared, registry, HitPoints, () => calls.Add("hit"), null, null, null);

            shared.Publish(new GameEventContext(GameEvent.AfterDamage, RuntimeEntityId.NewId(), id));

            Assert.IsEmpty(calls, "the bus already captured the invocation list; the disposed flag is what stops the callback");
        }

        [Test]
        public void IsDownAtBindIsTrueForDeathSaveStateOrZeroHitPointsAndFalseOtherwise()
        {
            Assert.IsFalse(CombatCueEventBinding.IsDownAtBind(id, registry, HitPoints));

            hp.Current = 0;
            Assert.IsTrue(CombatCueEventBinding.IsDownAtBind(id, registry, HitPoints), "stable at 0 HP has no death-save state but is down");
            Assert.IsFalse(CombatCueEventBinding.IsDownAtBind(id, registry, default), "no hit-point id: only death-save state counts");

            hp.Current = 5;
            character.DeathSaves = new DeathSaveState();
            Assert.IsTrue(CombatCueEventBinding.IsDownAtBind(id, registry, HitPoints));

            Assert.IsFalse(CombatCueEventBinding.IsDownAtBind(RuntimeEntityId.NewId(), registry, HitPoints));
            Assert.IsFalse(CombatCueEventBinding.IsDownAtBind(id, null, HitPoints));
        }

        [Test]
        public void ANullBusIsRejected()
        {
            Assert.Throws<ArgumentNullException>(() => new CombatCueEventBinding(id, null, registry, HitPoints, null, null, null, null));
        }
    }
}

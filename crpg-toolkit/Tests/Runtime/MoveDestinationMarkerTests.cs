using System.Collections;
using System.Reflection;
using CRPG.Core;
using CRPG.Presentation.Sprites;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CRPG.Tests.PlayMode
{
    /// <summary>2D-Campaign-ExplorationUX-Followups.md task item 6: MoveDestinationMarker's Update-driven
    /// show/reposition/hide timer (no Coroutine, finding 12/S4) and its external transition-suppression hook
    /// (finding 13), against a real Move event from a plain fake IPlayerCommandSource.</summary>
    public class MoveDestinationMarkerTests
    {
        private GameObject go;

        private sealed class FakeCommandSource : IPlayerCommandSource
        {
            public event System.Action<SelectCommand> Select;
            public event System.Action<MoveCommand> Move;
            public event System.Action<InteractCommand> Interact;
            public event System.Action Cancel;

            public void RaiseMove(MoveCommand command) => Move?.Invoke(command);
        }

        [TearDown]
        public void TearDown()
        {
            if (go != null) Object.DestroyImmediate(go);
        }

        private static void SetField(object target, string fieldName, object value) =>
            target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);

        private MoveDestinationMarker CreateMarker(float visibleSeconds)
        {
            go = new GameObject("Marker");
            go.AddComponent<SpriteRenderer>();
            var marker = go.AddComponent<MoveDestinationMarker>();
            SetField(marker, "visibleSeconds", visibleSeconds);
            return marker;
        }

        [UnityTest]
        public IEnumerator AMoveCommandShowsTheMarkerAtTheDestination()
        {
            var marker = CreateMarker(5f);
            var source = new FakeCommandSource();
            marker.Initialize(source);
            yield return null;

            var destination = new Vector3(3f, 0f, 4f);
            source.RaiseMove(new MoveCommand(destination, Vector3.forward));
            yield return null;

            var renderer = go.GetComponent<SpriteRenderer>();
            Assert.IsTrue(renderer.enabled);
            Assert.AreEqual(destination + new Vector3(0f, 0.02f, 0f), go.transform.position);
        }

        [UnityTest]
        public IEnumerator ASecondMoveBeforeTheTimerElapsesRepositionsInsteadOfStacking()
        {
            var marker = CreateMarker(1f);
            var source = new FakeCommandSource();
            marker.Initialize(source);
            yield return null;

            source.RaiseMove(new MoveCommand(new Vector3(1f, 0f, 1f), Vector3.forward));
            yield return null;
            yield return null;

            var second = new Vector3(9f, 0f, 9f);
            source.RaiseMove(new MoveCommand(second, Vector3.forward)); // restarts the timer, doesn't stack
            yield return null;

            var renderer = go.GetComponent<SpriteRenderer>();
            Assert.IsTrue(renderer.enabled);
            Assert.AreEqual(second + new Vector3(0f, 0.02f, 0f), go.transform.position);
        }

        [UnityTest]
        public IEnumerator TheMarkerHidesItselfOnceItsTimerElapsesWithNoExternalCleanup()
        {
            var marker = CreateMarker(0.05f);
            var source = new FakeCommandSource();
            marker.Initialize(source);
            yield return null;

            source.RaiseMove(new MoveCommand(Vector3.zero, Vector3.forward));
            var renderer = go.GetComponent<SpriteRenderer>();
            Assert.IsTrue(renderer.enabled);

            yield return new WaitForSeconds(0.2f);
            Assert.IsFalse(renderer.enabled);
        }

        [UnityTest]
        public IEnumerator ASuppressedWorldTransitionHidesTheMarkerImmediately()
        {
            var marker = CreateMarker(5f);
            var source = new FakeCommandSource();
            marker.Initialize(source);
            var suppressed = false;
            marker.IsWorldTransitioning = () => suppressed;
            yield return null;

            source.RaiseMove(new MoveCommand(Vector3.zero, Vector3.forward));
            yield return null;
            var renderer = go.GetComponent<SpriteRenderer>();
            Assert.IsTrue(renderer.enabled);

            suppressed = true;
            yield return null;

            Assert.IsFalse(renderer.enabled);
        }
    }
}

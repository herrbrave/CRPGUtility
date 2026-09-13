using CRPG.Core;
using CRPG.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace CRPG.Tests.PlayMode
{
    /// <summary>
    /// Task 1.2 (Production Foundation): covers exactly the two GameSessionTests.cs (EditMode) cases that
    /// depend on a MonoBehaviour's OnDestroy callback actually firing -- confirmed empirically that
    /// DestroyImmediate's OnDestroy callback never runs in headless -batchmode EditMode (no active Play
    /// session), even after polling many editor ticks with EditorApplication.QueuePlayerLoopUpdate. A real
    /// Play session (PlayMode tests) delivers it synchronously -- the same constraint Task 1.1's
    /// CharacterBindingTests already worked within for CharacterPlayableAnimator/EquipmentVisualBinder's
    /// own OnDestroy-driven unbind.
    /// </summary>
    public class GameSessionDestructionTests
    {
        private GameObject go;

        [TearDown]
        public void TearDown()
        {
            if (go != null) UnityEngine.Object.DestroyImmediate(go);
        }

        [Test]
        public void DestroyingTheHostDisposesItsSession()
        {
            go = new GameObject("TestSessionHost");
            var host = go.AddComponent<GameSessionHost>();
            var session = host.StartSession(new RulesDatabase(), new DiceService(new SeededRandomSource(1)));

            UnityEngine.Object.DestroyImmediate(go);
            go = null;

            Assert.IsTrue(session.IsDisposed);
        }

        [Test]
        public void DestroyingTheGameObjectUnbindsTheAgent()
        {
            go = new GameObject("TestCharacterAgent");
            var agent = go.AddComponent<CharacterAgent>();
            var session = new GameSession(new RulesDatabase(), new DiceService(new SeededRandomSource(1)));
            agent.Bind(RuntimeEntityId.NewId(), session);

            UnityEngine.Object.DestroyImmediate(go);
            go = null;

            Assert.IsFalse(agent.IsBound);
        }
    }
}

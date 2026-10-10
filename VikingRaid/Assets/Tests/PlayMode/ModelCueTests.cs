using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Presentation.Animation;
using NUnit.Framework;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;
using VikingRaid.Game;

namespace VikingRaid.Tests
{
    /// <summary>ModelActionCues against the REAL CharacterPlayableAnimator/driver on a real Humanoid wrapper, driven by real
    /// GameEvents -- the gap the plan review found: nothing drives hit / down / death / recover cues in 3D.</summary>
    public class ModelActionCuesTests
    {
        private static readonly DefinitionId Hp = new("resource-hp");
        private readonly List<GameObject> spawned = new();
        private readonly GameEventBus bus = new();
        private readonly CharacterRegistry registry = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in spawned) if (go != null) UnityEngine.Object.DestroyImmediate(go);
            spawned.Clear();
            NavMesh.RemoveAllNavMeshData();
        }

        private sealed class Subject
        {
            public GameObject Go;
            public ModelActionCues Cues;
            public CharacterPlayableAnimator Animator;
            public CharacterState State;
            public RuntimeEntityId Id;
        }

        private static IEnumerator Wait(float seconds)
        {
            var elapsed = 0f;
            do { elapsed += Time.deltaTime; yield return null; } while (elapsed < seconds);
        }

        private IEnumerator Spawn(Action<Subject> done, int startHp = 10, bool initialize = true)
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.localScale = new Vector3(4f, 1f, 4f);
            ground.isStatic = true;
            spawned.Add(ground);
            var surface = new GameObject("Surface").AddComponent<NavMeshSurface>();
            spawned.Add(surface.gameObject);
            surface.BuildNavMesh();

#if UNITY_EDITOR
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Characters/Prefabs/Hero_Bjorn.prefab");
#else
            GameObject prefab = null;
#endif
            Assert.IsNotNull(prefab, "Hero_Bjorn.prefab missing -- run 'Viking Raid/Task 1/Author Characters'.");

            var s = new Subject { Id = RuntimeEntityId.NewId() };
            s.State = new CharacterState(s.Id);
            s.State.AddResource(new ResourceState(Hp, 10, startHp));
            registry.Register(s.State);

            s.Go = UnityEngine.Object.Instantiate(prefab, Vector3.zero, Quaternion.identity);
            spawned.Add(s.Go);
            s.Cues = s.Go.GetComponent<ModelActionCues>();
            Assert.IsNotNull(s.Cues, "the authored hero prefab must carry ModelActionCues");
            Assert.AreEqual(Hp, s.Cues.HitPointResourceId, "the authored prefab must be configured for the SRD5 hit-point resource");
            s.Cues.HitDelaySeconds = 0.15f;
            s.Cues.TerminalDelaySeconds = 0.15f;
            s.Animator = s.Go.GetComponent<CharacterPlayableAnimator>();
            yield return null;
            yield return null;
            Assert.IsTrue(s.Animator.IsPresentationBound);
            if (initialize) s.Cues.Initialize(s.Id, bus, registry);
            done(s);
        }

        private void Publish(GameEvent e, Subject s) => bus.Publish(new GameEventContext(e, default, s.Id));

        [UnityTest]
        public IEnumerator DamageRequestsAHitReactionAfterTheDelay()
        {
            Subject s = null;
            yield return Spawn(x => s = x);
            yield return Wait(0.5f); // settle into idle

            Publish(GameEvent.AfterDamage, s);
            Assert.IsFalse(s.Animator.PresentationState.IsActionActive, "hit must be delayed so the attacker's swing lands first");
            yield return Wait(0.05f); // well inside the 0.15 s delay, after Update has run several times
            Assert.IsFalse(s.Animator.PresentationState.IsActionActive, "hit reaction started before its delay elapsed");

            var sawHit = false;
            var elapsed = 0f;
            while (elapsed < 1f) { elapsed += Time.deltaTime; yield return null; sawHit |= s.Animator.PresentationState.IsActionActive; }
            Assert.IsTrue(sawHit, "no hit reaction ever played");
            Assert.IsFalse(s.Animator.PresentationState.IsTerminal);
        }

        [UnityTest]
        public IEnumerator DroppedToZeroEntersTerminalAfterDelayAndCancelsAPendingHit()
        {
            Subject s = null;
            yield return Spawn(x => s = x);
            yield return Wait(0.5f);

            Publish(GameEvent.AfterDamage, s);
            Publish(GameEvent.CreatureDroppedToZeroHP, s);
            Assert.IsFalse(s.Animator.PresentationState.IsTerminal, "terminal pose must wait for the delay");
            yield return Wait(0.05f);
            Assert.IsFalse(s.Animator.PresentationState.IsTerminal, "terminal pose started before its delay elapsed");
            Assert.IsFalse(s.Animator.PresentationState.IsActionActive, "a pending hit must not play once the character is going down");

            yield return Wait(0.6f);
            Assert.IsTrue(s.Animator.PresentationState.IsTerminal);
            Assert.IsFalse(s.Animator.PresentationState.IsActionActive, "the pending hit reaction should have been cancelled");
            StringAssert.Contains("Death01", s.Animator.CurrentClip.name);
        }

        [UnityTest]
        public IEnumerator APendingHitNeverPlaysOnceTheCharacterIsGoingDown()
        {
            Subject s = null;
            yield return Spawn(x => s = x);
            s.Cues.HitDelaySeconds = 0.1f;
            s.Cues.TerminalDelaySeconds = 0.5f; // the hit delay elapses well before the terminal pose starts
            yield return Wait(0.5f);

            Publish(GameEvent.AfterDamage, s);
            Publish(GameEvent.CreatureDroppedToZeroHP, s);
            var elapsed = 0f;
            while (elapsed < 0.4f)
            {
                elapsed += Time.deltaTime;
                yield return null;
                Assert.IsFalse(s.Animator.PresentationState.IsActionActive, $"a hit reaction played at t={elapsed:F2} although the character was already going down");
            }
        }

        [UnityTest]
        public IEnumerator HitsAfterDownAreIgnored()
        {
            Subject s = null;
            yield return Spawn(x => s = x);
            Publish(GameEvent.CreatureDroppedToZeroHP, s);
            yield return Wait(0.6f);
            Publish(GameEvent.AfterDamage, s);
            Publish(GameEvent.AfterDamage, s);
            yield return Wait(0.6f);
            Assert.IsTrue(s.Animator.PresentationState.IsTerminal);
            StringAssert.Contains("Death01", s.Animator.CurrentClip.name, "a downed character must keep lying down");
        }

        [UnityTest]
        public IEnumerator StableAtZeroHpStaysDownAndStandsUpOnceHpIsAboveZero()
        {
            Subject s = null;
            yield return Spawn(x => s = x);
            Publish(GameEvent.CreatureDroppedToZeroHP, s);
            s.State.Resources[Hp].Current = 0;
            yield return Wait(0.6f);

            Publish(GameEvent.CreatureStabilized, s);
            yield return Wait(0.3f);
            Assert.IsTrue(s.Animator.PresentationState.IsTerminal, "stable at 0 HP is still down");

            s.State.Resources[Hp].Current = 3;
            Publish(GameEvent.ConditionRemoved, s);
            yield return Wait(0.8f);
            Assert.IsFalse(s.Animator.PresentationState.IsTerminal);
            StringAssert.Contains("Idle", s.Animator.CurrentClip.name, "should be back to the idle baseline");
        }

        [UnityTest]
        public IEnumerator DiedPublishedBeforeDroppedToZeroStillEndsTerminalOnce()
        {
            Subject s = null;
            yield return Spawn(x => s = x);
            Publish(GameEvent.CreatureDied, s);            // massive damage: DeathSaveService raises this first
            Publish(GameEvent.CreatureDroppedToZeroHP, s); // ... then this
            Publish(GameEvent.AfterDamage, s);
            yield return Wait(0.8f);
            Assert.IsTrue(s.Animator.PresentationState.IsTerminal);
            StringAssert.Contains("Death01", s.Animator.CurrentClip.name);
        }

        [UnityTest]
        public IEnumerator BeginDefeatWithALingerReportsBackAfterTheDeathHasPlayed()
        {
            Subject s = null;
            yield return Spawn(x => s = x);
            s.Cues.CorpseLingerSeconds = 0.4f;
            var finished = 0;
            s.Cues.BeginDefeat(() => finished++);
            Assert.AreEqual(0, finished, "must not report back immediately");

            var elapsed = 0f;
            while (finished == 0 && elapsed < 6f) { elapsed += Time.deltaTime; yield return null; }
            Assert.AreEqual(1, finished, "defeat presentation never finished");
            Assert.IsTrue(s.Animator.PresentationState.IsTerminal);
            yield return Wait(0.5f);
            Assert.AreEqual(1, finished, "must report exactly once");
        }

        [UnityTest]
        public IEnumerator BeginDefeatWithNoLingerLeavesTheCorpseInPlace()
        {
            Subject s = null;
            yield return Spawn(x => s = x);
            s.Cues.CorpseLingerSeconds = 0f; // 0 = the corpse stays
            var finished = 0;
            s.Cues.BeginDefeat(() => finished++);
            yield return Wait(4f);
            Assert.AreEqual(0, finished);
            Assert.IsTrue(s.Animator.PresentationState.IsTerminal);
        }

        [UnityTest]
        public IEnumerator ACharacterAlreadyAtZeroHpWhenBoundStartsDown()
        {
            Subject s = null;
            yield return Spawn(x => s = x, startHp: 0, initialize: false);
            s.Cues.Initialize(s.Id, bus, registry);
            yield return Wait(0.3f);
            Assert.IsTrue(s.Animator.PresentationState.IsTerminal, "a saved-while-down character must appear down immediately");
        }

        [UnityTest]
        public IEnumerator InitializeFollowsTheBindingContract()
        {
            Subject s = null;
            yield return Spawn(x => s = x);
            Assert.DoesNotThrow(() => s.Cues.Initialize(s.Id, bus, registry), "identical rebind is a no-op");
            Assert.Throws<InvalidOperationException>(() => s.Cues.Initialize(RuntimeEntityId.NewId(), bus, registry));
            s.Cues.Unbind();
            Publish(GameEvent.CreatureDroppedToZeroHP, s);
            yield return Wait(0.5f);
            Assert.IsFalse(s.Animator.PresentationState.IsTerminal, "an unbound component must not react");
        }
    }
}

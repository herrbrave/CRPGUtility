using System.Collections.Generic;
using CRPG.Core;
using CRPG.Gameplay;
using CRPG.Gameplay.Ambient;
using CRPG.Gameplay.Scenes;
using NUnit.Framework;
using UnityEngine;

namespace CRPG.Tests.EditMode
{
    public sealed class NpcRoutinesAttachTests
    {
        private sealed class Content : ILocationContent
        {
            public readonly List<NpcSpawnPoint> Points = new();
            public IReadOnlyList<NpcSpawnPoint> SpawnPoints => Points;
            public IReadOnlyList<StoreContentPoint> Stores => new List<StoreContentPoint>();
            public IReadOnlyList<LootContentPoint> LootContainers => new List<LootContentPoint>();
            public Rect? CameraBoundsXZ => null;
        }

        private readonly List<Object> toDestroy = new();
        private GameSession session;
        private WorldSpawnService spawner;

        [SetUp]
        public void SetUp()
        {
            session = new GameSession(new RulesDatabase(), new DiceService(new SeededRandomSource(1)));
            spawner = new WorldSpawnService(session);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in toDestroy) if (o != null) Object.DestroyImmediate(o);
            toDestroy.Clear();
            session.Dispose();
        }

        private NpcSpawnPoint SpawnPoint(string spawnId, NpcRoutineKind kind, bool spawn = true)
        {
            var definition = TestDefinitionFactory.CreateNpcDefinition($"npc-{spawnId}", spawnId);
            var visual = new GameObject($"{spawnId}-visual");
            toDestroy.Add(visual);
            TestDefinitionFactory.SetPrivateField(definition, "visualPrefab", visual);
            toDestroy.Add(definition);

            var anchor = new GameObject($"{spawnId}-anchor");
            toDestroy.Add(anchor);

            if (spawn)
            {
                var agent = spawner.SpawnNpc(spawnId, definition, anchor.transform.position, anchor.transform.rotation);
                toDestroy.Add(agent.gameObject);
            }

            return new NpcSpawnPoint { SpawnId = spawnId, Definition = definition, Anchor = anchor.transform, Routine = new NpcRoutineSpec { Kind = kind } };
        }

        private static NpcRoutineRunner RunnerOf(WorldSpawnService spawner, string spawnId)
        {
            spawner.TryGetCharacterId(spawnId, out var id, out _, out _);
            foreach (var agent in spawner.OwnedAgents)
            {
                if (agent.EntityId == id) return agent.GetComponent<NpcRoutineRunner>();
            }

            return null;
        }

        [Test]
        public void AttachAll_AttachesOnlyToNpcsWithARoutine()
        {
            var content = new Content();
            content.Points.Add(SpawnPoint("wanderer", NpcRoutineKind.Wander));
            content.Points.Add(SpawnPoint("stander", NpcRoutineKind.Stand));

            Assert.AreEqual(1, NpcRoutines.AttachAll(content, spawner));
            Assert.IsNotNull(RunnerOf(spawner, "wanderer"));
            Assert.IsNull(RunnerOf(spawner, "stander"));
        }

        [Test]
        public void AttachAll_IsIdempotent()
        {
            var content = new Content();
            content.Points.Add(SpawnPoint("wanderer", NpcRoutineKind.Wander));

            Assert.AreEqual(1, NpcRoutines.AttachAll(content, spawner));
            var first = RunnerOf(spawner, "wanderer");

            Assert.AreEqual(0, NpcRoutines.AttachAll(content, spawner));
            Assert.AreSame(first, RunnerOf(spawner, "wanderer"));
            Assert.AreEqual(1, first.GetComponents<NpcRoutineRunner>().Length);
        }

        [Test]
        public void AttachAll_SkipsSpawnPointsThatWereNeverSpawned_AndNullEntries()
        {
            var content = new Content();
            content.Points.Add(SpawnPoint("ghost", NpcRoutineKind.Wander, spawn: false));
            content.Points.Add(null);
            content.Points.Add(new NpcSpawnPoint { SpawnId = "", Routine = new NpcRoutineSpec { Kind = NpcRoutineKind.Wander } });
            content.Points.Add(new NpcSpawnPoint { SpawnId = "no-routine", Routine = null });

            Assert.AreEqual(0, NpcRoutines.AttachAll(content, spawner));
        }

        [Test]
        public void AttachAll_SkipsADefeatedNpc()
        {
            var content = new Content();
            content.Points.Add(SpawnPoint("fallen", NpcRoutineKind.Wander));
            spawner.TryGetCharacterId("fallen", out var id, out _, out _);
            spawner.MarkDefeated(id);

            Assert.AreEqual(0, NpcRoutines.AttachAll(content, spawner));
        }

        [Test]
        public void AttachAll_WorksOnAnInactiveView()
        {
            var content = new Content();
            content.Points.Add(SpawnPoint("sleeper", NpcRoutineKind.Wander));
            spawner.TryGetCharacterId("sleeper", out var id, out _, out _);
            foreach (var agent in spawner.OwnedAgents) if (agent.EntityId == id) agent.gameObject.SetActive(false);

            Assert.AreEqual(1, NpcRoutines.AttachAll(content, spawner));
        }

        [Test]
        public void AttachAll_HandlesNullArguments()
        {
            Assert.AreEqual(0, NpcRoutines.AttachAll(null, spawner));
            Assert.AreEqual(0, NpcRoutines.AttachAll(new Content(), null));
        }

        [Test]
        public void Seed_IsStableAcrossRuns_AndDiffersPerNpcAndPerAuthoredSeed()
        {
            // Pinned values: if these change, every wandering NPC in every game silently walks differently.
            Assert.AreEqual(NpcRoutines.Seed("dock-worker", 0), NpcRoutines.Seed("dock-worker", 0));
            Assert.AreNotEqual(NpcRoutines.Seed("dock-worker", 0), NpcRoutines.Seed("fisher", 0));
            Assert.AreNotEqual(NpcRoutines.Seed("dock-worker", 0), NpcRoutines.Seed("dock-worker", 1));
            Assert.AreEqual(unchecked((int)0xE40C292C), NpcRoutines.Seed("a", 0), "FNV-1a 32-bit of \"a\"");
            Assert.AreEqual(unchecked((int)0x811C9DC5), NpcRoutines.Seed(null, 0), "FNV offset basis");
        }

        [Test]
        public void AmbientSuspension_IsHeldByAnyOwner_AndReleasedByTheLast()
        {
            var suspension = new AmbientSuspension();
            var npc = RuntimeEntityId.NewId();
            var a = new object();
            var b = new object();

            Assert.IsFalse(suspension.IsSuspended(npc));
            suspension.Suspend(npc, a);
            suspension.Suspend(npc, b);
            suspension.Suspend(npc, b); // twice by the same owner is still one hold
            Assert.IsTrue(suspension.IsSuspended(npc));

            suspension.Resume(npc, a);
            Assert.IsTrue(suspension.IsSuspended(npc), "b still holds it");

            suspension.Resume(npc, b);
            Assert.IsFalse(suspension.IsSuspended(npc));
        }

        [Test]
        public void AmbientSuspension_ResumingSomeoneElsesHold_DoesNothing()
        {
            var suspension = new AmbientSuspension();
            var npc = RuntimeEntityId.NewId();
            suspension.Suspend(npc, new object());

            suspension.Resume(npc, new object());
            suspension.Resume(RuntimeEntityId.NewId(), new object());

            Assert.IsTrue(suspension.IsSuspended(npc));
        }

        [Test]
        public void AmbientSuspension_IgnoresAnInvalidNpcOrOwner()
        {
            var suspension = new AmbientSuspension();
            Assert.DoesNotThrow(() => suspension.Suspend(RuntimeEntityId.None, new object()));
            Assert.DoesNotThrow(() => suspension.Suspend(RuntimeEntityId.NewId(), null));
            Assert.IsFalse(suspension.IsSuspended(RuntimeEntityId.None));
        }

        [Test]
        public void ANewSpawnPoint_DefaultsToStand_SoOldScenesAreUnchanged()
        {
            var point = new NpcSpawnPoint();
            Assert.IsNotNull(point.Routine);
            Assert.AreEqual(NpcRoutineKind.Stand, point.Routine.Kind);
            Assert.IsFalse(point.Routine.IsActive);
        }

        [Test]
        public void FacingPriority_AmbientRanksAboveMotionAndBelowEverythingElse()
        {
            Assert.Greater(FacingPriority.Ambient, FacingPriority.Motion);
            Assert.Less(FacingPriority.Ambient, FacingPriority.Interaction);
            Assert.Less(FacingPriority.Interaction, FacingPriority.CommittedAction);
            Assert.Less(FacingPriority.CommittedAction, FacingPriority.Terminal);
        }
    }
}

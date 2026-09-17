using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CRPG.Abilities;
using CRPG.Actors;
using CRPG.Combat;
using CRPG.Core;
using CRPG.Gameplay;
using CRPG.Gameplay.Actions;
using CRPG.Navigation;
using CRPG.Rules.SRD5;
using NUnit.Framework;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

namespace CRPG.Tests.Fixtures.TacticalEncounterVerification
{
    /// <summary>
    /// A deterministic, controlled scenario proving the sample's real Opportunity Attack content actually
    /// fires when a target leaves melee range -- NOT relying on emergent AI behavior to happen to produce this
    /// (two melee AIs closing and attacking do not necessarily ever leave each other's threatened area, so a
    /// free-running encounter could finish without a single reaction ever firing). Drives a real
    /// MoveActionExecution across the threat boundary directly via ActionScheduler, exactly the mechanism
    /// TacticalPlayerMoveAdapter uses for a real player-driven move.
    /// </summary>
    public class TacticalReactionTests
    {
        private readonly List<GameObject> spawned = new();
        private NavMeshSurface bakeSurface;

        [TearDown]
        public void TearDown()
        {
            foreach (var go in spawned)
            {
                if (go != null) Object.DestroyImmediate(go);
            }

            spawned.Clear();
            NavMesh.RemoveAllNavMeshData();
        }

        private GameObject SpawnMover(RuntimeEntityId id, Vector3 position)
        {
            var go = new GameObject($"Mover-{id}");
            var navAgent = go.AddComponent<NavMeshAgent>();
            navAgent.speed = 6f;
            navAgent.acceleration = 40f;
            go.AddComponent<NavMeshCharacterMotor>();
            spawned.Add(go);

            go.transform.position = position;
            navAgent.Warp(position);
            return go;
        }

        [UnityTest]
        public IEnumerator OpportunityAttackFiresWhenTheMoverLeavesMeleeRange()
        {
            var groundGo = GameObject.CreatePrimitive(PrimitiveType.Plane);
            groundGo.transform.localScale = new Vector3(4f, 1f, 4f);
            spawned.Add(groundGo);
            var surfaceGo = new GameObject("NavMeshSurface");
            spawned.Add(surfaceGo);
            bakeSurface = surfaceGo.AddComponent<NavMeshSurface>();
            bakeSurface.BuildNavMesh();
            yield return null;

            var content = TacticalEncounterTestContent.BuildFullSampleContent();
            var rulesDatabase = content.RulesDatabase;

            var characters = new CharacterRegistry();

            var reactorId = RuntimeEntityId.NewId();
            var reactor = new CharacterState(reactorId);
            reactor.AddResource(new ResourceState(content.HitPointsId, 20));
            reactor.AddResource(new ResourceState(content.ActionId, 1));
            reactor.AddResource(new ResourceState(content.ReactionId, 1));
            reactor.GrantFeature(content.ReactiveStrikes.Id);
            characters.Register(reactor);

            var moverId = RuntimeEntityId.NewId();
            var mover = new CharacterState(moverId);
            mover.AddResource(new ResourceState(content.HitPointsId, 20));
            mover.AddResource(new ResourceState(content.ActionId, 1));
            mover.AddResource(new ResourceState(content.MovementId, 10));
            characters.Register(mover);

            var reactorGo = SpawnMover(reactorId, new Vector3(0f, 0f, 0f));
            var moverGo = SpawnMover(moverId, new Vector3(1f, 0f, 0f)); // starts well within melee range (2).

            var positions = new TestPositionProvider();
            positions.Track(reactorId, reactorGo.transform);
            positions.Track(moverId, moverGo.transform);

            var combatLog = new RecordingCombatLog();
            var abilityResolution = new AbilityResolutionService(
                characters, new DiceService(new SeededRandomSource(1)), combatLog: combatLog, positions: positions,
                attackBonusRule: new DefaultAttackBonusRule(characters, rulesDatabase),
                armorClassRule: new DefaultArmorClassRule(characters, rulesDatabase, content.StrengthId),
                rulesDatabase: rulesDatabase);

            var reactionService = new ReactionService(characters, abilityResolution, new FirstEligibleReactionProvider(), content.Reaction);
            var movementResolution = new MovementResolutionService(
                characters, positions, new NoOpMovementController(), combatLog, reactionService, rulesDatabase, threatRange: TacticalEncounterTestContent.MeleeRange);

            var reactionsBefore = combatLog.Entries.OfType<AttackRollLogEntry>().Count();

            // Real navigation: walk the mover from inside the reactor's threat range (1) to well outside it
            // (10) -- a real MoveActionExecution/NavMeshAgent traversal, not a teleport, so the threat-boundary
            // crossing is genuinely detected mid-move exactly as it would be for a real player click.
            var execution = new MoveActionExecution(
                moverId, moverGo.GetComponent<NavMeshCharacterMotor>(), movementResolution, new NavMeshService(),
                characters, positions, content.MovementId, new Vector3(10f, 0f, 0f), new[] { reactorId });

            var elapsed = 0f;
            while (elapsed < 10f && execution.Outcome == null)
            {
                execution.Tick(Time.deltaTime);
                elapsed += Time.deltaTime;
                yield return null;
            }

            Assert.IsNotNull(execution.Outcome, "The move never completed within the simulated time budget.");

            var attackEntries = combatLog.Entries.OfType<AttackRollLogEntry>().ToList();
            Assert.Greater(attackEntries.Count, reactionsBefore, "Expected a real attack-roll log entry from the reactor's Opportunity Attack.");
            Assert.AreEqual(reactorId, attackEntries[0].Source, "The reaction's attack roll must come from the reactor, not the mover.");

            characters.TryGetCharacter(reactorId, out var reactorAfter);
            reactorAfter.TryGetResource(content.ReactionId, out var reactionAfter);
            Assert.AreEqual(0, reactionAfter.Current, "The reactor's Reaction resource must be spent by the reaction firing.");
        }
    }
}

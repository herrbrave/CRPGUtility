using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CRPG.Abilities;
using CRPG.AI;
using CRPG.Actors;
using CRPG.Combat;
using CRPG.Core;
using CRPG.Gameplay;
using CRPG.Gameplay.Combat;
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
    /// The full, real 2-vs-2 encounter proof: real baked NavMesh, real navigation, the sample's own real
    /// authored "Foe Warrior" BehaviorSetDefinition/UtilityAIDecisionProvider driving BOTH factions (legitimate
    /// for an automated test even though the real scene only uses AI for the Foes side -- this proves the
    /// authored AI content itself works, not the scene's player/AI split, which is closed by the live human
    /// review instead), real rolled initiative (SRD5's DefaultInitiativeRule), real attack rolls/damage. Ends
    /// with a real win (one faction's Combatants count reaches zero) and asserts real combat actually happened
    /// along the way, not just "someone eventually vanished somehow."
    /// </summary>
    public class TacticalFullEncounterTests
    {
        private static readonly DefinitionId HeroesFaction = new("faction-heroes");
        private static readonly DefinitionId FoesFaction = new("faction-foes");

        private readonly List<GameObject> spawned = new();
        private GameSession session;
        private WorldSpawnService worldSpawnService;
        private TacticalEncounterTestContent.SampleContent content;

        [SetUp]
        public void SetUp()
        {
            // BuildFullSampleContent's own RulesDatabase already has every piece of the sample's real content
            // registered -- reused directly as the session's database rather than registering it a second time.
            content = TacticalEncounterTestContent.BuildFullSampleContent();

            // Own fresh GameSession -- genuinely isolated from anything else running, never the loaded scene's
            // own live session (see the plan's isolation rationale: sharing an event bus/registry with a
            // second live TacticalEncounterController risks real cross-talk).
            session = new GameSession(content.RulesDatabase, new DiceService(new SeededRandomSource(1)));
            worldSpawnService = new WorldSpawnService(session);
        }

        [TearDown]
        public void TearDown()
        {
            session?.Dispose();
            foreach (var go in spawned)
            {
                if (go != null) Object.DestroyImmediate(go);
            }

            spawned.Clear();
            NavMesh.RemoveAllNavMeshData();
        }

        private RuntimeEntityId SpawnCombatant(string spawnId, Vector3 position, DefinitionId faction)
        {
            var character = new CharacterState(RuntimeEntityId.NewId());
            character.AddResource(new ResourceState(content.HitPointsId, 20));
            character.AddResource(new ResourceState(content.ActionId, 1));
            character.AddResource(new ResourceState(content.ReactionId, 1));
            character.AddResource(new ResourceState(content.MovementId, 8));
            character.GrantFeature(content.ReactiveStrikes.Id);
            character.LearnAbility(content.MeleeStrike.Id);
            session.Characters.Register(character);

            var template = new GameObject($"Combatant-{spawnId}");
            var navAgent = template.AddComponent<NavMeshAgent>();
            navAgent.speed = 6f;
            navAgent.acceleration = 40f;
            template.AddComponent<NavMeshCharacterMotor>();

            var instance = worldSpawnService.BindRestoredView(spawnId, character.Id, default, template, position, Quaternion.identity, faction, startInactive: false);
            Object.DestroyImmediate(template);
            spawned.Add(instance);
            instance.GetComponent<NavMeshAgent>().Warp(position);

            return character.Id;
        }

        [UnityTest]
        public IEnumerator FullEncounterResolvesToARealWin()
        {
            var groundGo = GameObject.CreatePrimitive(PrimitiveType.Plane);
            groundGo.transform.localScale = new Vector3(4f, 1f, 4f);
            spawned.Add(groundGo);
            var wallGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wallGo.transform.position = new Vector3(0f, 1f, 0f);
            wallGo.transform.localScale = new Vector3(10f, 2f, 1f);
            spawned.Add(wallGo);
            var surfaceGo = new GameObject("NavMeshSurface");
            spawned.Add(surfaceGo);
            var surface = surfaceGo.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.All;
            surface.BuildNavMesh();
            yield return null;

            var hero1 = SpawnCombatant("hero-1", new Vector3(-3f, 0f, -8f), HeroesFaction);
            var hero2 = SpawnCombatant("hero-2", new Vector3(3f, 0f, -8f), HeroesFaction);
            var foe1 = SpawnCombatant("foe-1", new Vector3(-3f, 0f, 8f), FoesFaction);
            var foe2 = SpawnCombatant("foe-2", new Vector3(3f, 0f, 8f), FoesFaction);

            var positions = new TestPositionProvider();
            foreach (var id in new[] { hero1, hero2, foe1, foe2 })
                positions.Track(id, FindAgentTransform(id));

            var combatLog = new RecordingCombatLog();
            var abilityResolution = new AbilityResolutionService(
                session.Characters, session.DiceService, combatLog: combatLog, eventBus: session.EventBus, positions: positions,
                attackBonusRule: new DefaultAttackBonusRule(session.Characters, session.RulesDatabase),
                armorClassRule: new DefaultArmorClassRule(session.Characters, session.RulesDatabase, content.StrengthId),
                rulesDatabase: session.RulesDatabase);

            // The sample's own deliberate scope decision (see the plan): 0 HP is immediate death, not SRD5's
            // full death-save/stabilization loop -- a small, composition-root-local bridge, matching
            // TacticalEncounterController's own doc comment that whoever owns the encounter subscribes to
            // CreatureDied itself.
            session.EventBus.Subscribe(GameEvent.CreatureDroppedToZeroHP,
                ctx => session.EventBus.Publish(new GameEventContext(GameEvent.CreatureDied, ctx.Source, ctx.Target)));

            var movementController = new NoOpMovementController();
            var reactionService = new ReactionService(session.Characters, abilityResolution, new FirstEligibleReactionProvider(), content.Reaction);
            var movementResolution = new MovementResolutionService(
                session.Characters, positions, movementController, combatLog, reactionService, session.RulesDatabase, threatRange: TacticalEncounterTestContent.MeleeRange);
            var turnResources = new TurnResourceSet(new[] { content.Action, content.Reaction, content.Movement });
            var encounterService = new CombatEncounterService(
                session.Characters, new DefaultInitiativeRule(session.Characters, session.RulesDatabase, session.DiceService), turnResources, session.EventBus, combatLog);

            var controller = new TacticalEncounterController(
                session.Characters, session.EventBus, positions, abilityResolution, movementResolution, new NavMeshService(),
                encounterService, worldSpawnService,
                new[] { (hero1, HeroesFaction), (hero2, HeroesFaction), (foe1, FoesFaction), (foe2, FoesFaction) });

            var heroBrain = new UtilityAIDecisionProvider(content.FoeWarrior, session.RulesDatabase, session.Characters, abilityResolution, positions, new SeededRandomSource(2), combatLog: combatLog);
            var foeBrain = new UtilityAIDecisionProvider(content.FoeWarrior, session.RulesDatabase, session.Characters, abilityResolution, positions, new SeededRandomSource(3), combatLog: combatLog);
            var heroDriver = new TacticalAiTurnDriver(controller, session.Characters, heroBrain, HeroesFaction);
            var foeDriver = new TacticalAiTurnDriver(controller, session.Characters, foeBrain, FoesFaction);

            bool WinConditionReached()
            {
                var encounter = controller.EncounterService.Encounter;
                if (encounter == null) return true;
                var heroesAlive = false;
                var foesAlive = false;
                foreach (var c in encounter.Combatants)
                {
                    if (c.Faction == HeroesFaction) heroesAlive = true;
                    else if (c.Faction == FoesFaction) foesAlive = true;
                }

                return !heroesAlive || !foesAlive;
            }

            var elapsed = 0f;
            const float maxSimulatedSeconds = 30f;
            while (elapsed < maxSimulatedSeconds && !WinConditionReached())
            {
                controller.Tick(Time.deltaTime);
                heroDriver.Tick(Time.deltaTime);
                foeDriver.Tick(Time.deltaTime);
                elapsed += Time.deltaTime;
                yield return null;
            }

            Assert.IsTrue(WinConditionReached(), $"Combat never reached a win condition within {maxSimulatedSeconds} simulated seconds.");

            var finalEncounter = controller.EncounterService.Encounter;
            int heroesLeft = 0, foesLeft = 0;
            if (finalEncounter != null)
            {
                foreach (var c in finalEncounter.Combatants)
                {
                    if (c.Faction == HeroesFaction) heroesLeft++;
                    else if (c.Faction == FoesFaction) foesLeft++;
                }
            }

            Assert.IsTrue(heroesLeft == 0 || foesLeft == 0, "One whole faction must have been eliminated.");

            var attackEntries = combatLog.Entries.OfType<AttackRollLogEntry>().ToList();
            Assert.Greater(attackEntries.Count, 0, "Expected real attack-roll log entries from the authored Melee Strike ability -- not just an encounter that resolved somehow.");
            Assert.IsTrue(attackEntries.Any(e => e.IsHit), "Expected at least one real hit among the attack rolls (a run with zero hits at all would be an implausible fluke worth investigating, not a pass).");

            controller.Dispose();
        }

        private static Transform FindAgentTransform(RuntimeEntityId id)
        {
            foreach (var agent in CharacterAgent.AllLive)
            {
                if (agent != null && agent.EntityId == id) return agent.transform;
            }

            return null;
        }
    }
}

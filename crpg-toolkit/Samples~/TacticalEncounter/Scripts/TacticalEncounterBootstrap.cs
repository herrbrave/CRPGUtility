using System;
using System.Collections.Generic;
using CRPG.Abilities;
using CRPG.AI;
using CRPG.Actors;
using CRPG.Combat;
using CRPG.Core;
using CRPG.Gameplay;
using CRPG.Gameplay.Combat;
using CRPG.InputSystem;
using CRPG.Navigation;
using CRPG.Rules;
using CRPG.Rules.SRD5;
using CRPG.UI;
using UnityEngine;

namespace CRPG.Samples.TacticalEncounter
{
    /// <summary>
    /// The TacticalEncounter sample's composition root: a real 2-Hero-vs-2-Foe combat encounter proving the
    /// package's real combat/AI/reaction machinery end to end. Starts a real ruleset-aware session against the
    /// package's own shipped SRD5RulesetProvider (same "real consumer" precedent as CharacterCreator/Task 5.2's
    /// Fate ruleset), registers the sample's own real authored combat content (Melee Strike, Opportunity
    /// Attack, Reactive Strikes, the "Foe Warrior" Behavior/BehaviorSet), spawns Heroes (player-controlled,
    /// real click-to-target-and-attack via AbilityTargetingGate) and Foes (AI-controlled via the real
    /// UtilityAIDecisionProvider driving the sample's own authored Behavior content), and starts combat through
    /// the real rolled-initiative path.
    /// <para>
    /// Deliberate scope decision (see the task's own plan): no DeathSaveService/ConcentrationService, no
    /// spellcasting -- a creature dropped to 0 HP dies immediately (the small bridge below), keeping "victory"
    /// unambiguous and this sample focused on turn/attack/reaction/AI mechanics rather than SRD5's own
    /// death-and-dying subsystem (a real, separately-scoped engine gap found while designing this: a
    /// DeathSaveService-stabilized combatant has nothing generic stopping it from resuming ordinary actions at
    /// 0 HP once its DeathSaves clears).
    /// </para>
    /// </summary>
    public sealed class TacticalEncounterBootstrap : MonoBehaviour
    {
        private static readonly DefinitionId HeroesFaction = new("faction-heroes");
        private static readonly DefinitionId FoesFaction = new("faction-foes");
        private const float MeleeRange = 2f;

        [SerializeField] private SRD5RulesetProvider rulesetProvider;
        [SerializeField] private StatDefinition strengthStat;
        [SerializeField] private ResourceDefinition hitPointsResource;
        [SerializeField] private ResourceDefinition actionResource;
        [SerializeField] private ResourceDefinition reactionResource;

        [SerializeField] private AbilityDefinition meleeStrike;
        [SerializeField] private AbilityDefinition opportunityAttack;
        [SerializeField] private FeatureDefinition reactiveStrikes;
        [SerializeField] private ResourceDefinition movementResource;
        [SerializeField] private CRPG.AI.BehaviorDefinition closeDistanceBehavior;
        [SerializeField] private CRPG.AI.BehaviorDefinition meleeStrikeNearestBehavior;
        [SerializeField] private BehaviorSetDefinition foeWarriorBehaviorSet;

        [SerializeField] private GameObject heroPrefab;
        [SerializeField] private GameObject foePrefab;
        [SerializeField] private Transform[] heroSpawnAnchors;
        [SerializeField] private Transform[] foeSpawnAnchors;

        [SerializeField] private InputSystemCommandSource inputSource;
        [SerializeField] private HudRoot hudRoot;
        [SerializeField] private AbilityBarView abilityBarView;
        [SerializeField] private TurnOrderView turnOrderView;
        [SerializeField] private CombatLogPanel combatLogPanel;

        private GameSessionHost host;
        private WorldSpawnService worldSpawnService;
        private TacticalEncounterController controller;
        private TacticalAiTurnDriver foeDriver;
        private TacticalPlayerMoveAdapter moveAdapter;
        private TacticalCombatUIBridge uiBridge;
        private SampleCombatLog combatLog;
        private bool encounterEnded;

        public GameSession Session { get; private set; }

        private void Start()
        {
            combatLog = new SampleCombatLog();

            var requiredPolicies = new[]
            {
                typeof(IArmorClassRule), typeof(IAttackBonusRule), typeof(IInitiativeRule),
                typeof(ISavingThrowRule), typeof(IDamageResistanceRule),
            };
            host = gameObject.AddComponent<GameSessionHost>();
            var session = host.StartSession(rulesetProvider, new DiceService(new SeededRandomSource(1)), requiredPolicies);
            Session = session;

            // IRulesDatabase is deliberately read-only (content LOOKUP only -- see its own doc comment); the
            // concrete RulesDatabase RulesetSessionFactory builds internally still exposes real registration,
            // which is exactly what a composition root legitimately needs to add its own content on top of a
            // ruleset-aware session start (the same situation any consumer adding house-rule content would
            // face). Cast rather than falling back to the older StartSession(IRulesDatabase, IDiceService)
            // overload + hand-constructing SRD5 policy rules -- this keeps session.Policies real and resolved
            // by the ruleset provider itself, which AbilityResolutionService is wired from below.
            ((RulesDatabase)session.RulesDatabase).RegisterRange(new DefinitionAsset[]
            {
                meleeStrike, opportunityAttack, reactiveStrikes, movementResource,
                closeDistanceBehavior, meleeStrikeNearestBehavior, foeWarriorBehaviorSet,
            });

            worldSpawnService = new WorldSpawnService(session);

            var heroes = new List<RuntimeEntityId>();
            for (var i = 0; i < heroSpawnAnchors.Length; i++)
                heroes.Add(SpawnCombatant($"hero-{i}", heroPrefab, heroSpawnAnchors[i].position, HeroesFaction, hitPointsMax: 20));

            var foes = new List<RuntimeEntityId>();
            for (var i = 0; i < foeSpawnAnchors.Length; i++)
                foes.Add(SpawnCombatant($"foe-{i}", foePrefab, foeSpawnAnchors[i].position, FoesFaction, hitPointsMax: 16));

            var positions = new LiveCharacterPositionProvider();

            var abilityResolution = new AbilityResolutionService(
                session.Characters, session.DiceService, combatLog: combatLog, eventBus: session.EventBus, positions: positions,
                attackBonusRule: session.Policies.Get<IAttackBonusRule>(),
                armorClassRule: session.Policies.Get<IArmorClassRule>(),
                savingThrowRule: session.Policies.Get<ISavingThrowRule>(),
                damageResistanceRule: session.Policies.Get<IDamageResistanceRule>(),
                rulesDatabase: session.RulesDatabase);

            // Deliberate scope decision (see class remarks): 0 HP is immediate death, not SRD5's full
            // death-save/stabilization loop. Matches TacticalEncounterController's own doc comment that
            // whoever owns the encounter subscribes to CreatureDied itself.
            session.EventBus.Subscribe(GameEvent.CreatureDroppedToZeroHP,
                ctx => session.EventBus.Publish(new GameEventContext(GameEvent.CreatureDied, ctx.Source, ctx.Target)));

            var reactionService = new ReactionService(session.Characters, abilityResolution, new FirstEligibleReactionProvider(), reactionResource);
            var movementResolution = new MovementResolutionService(
                session.Characters, positions, new SceneMovementController(), combatLog, reactionService, session.RulesDatabase, threatRange: MeleeRange);
            var turnResources = new TurnResourceSet(new[] { actionResource, reactionResource, movementResource });
            var encounterService = new CombatEncounterService(
                session.Characters, session.Policies.Get<IInitiativeRule>(), turnResources, session.EventBus, combatLog);

            var combatants = new List<(RuntimeEntityId Id, DefinitionId Faction)>();
            foreach (var id in heroes) combatants.Add((id, HeroesFaction));
            foreach (var id in foes) combatants.Add((id, FoesFaction));

            controller = new TacticalEncounterController(
                session.Characters, session.EventBus, positions, abilityResolution, movementResolution, new NavMeshService(),
                encounterService, worldSpawnService, combatants, combatLog: combatLog);

            var foeBrain = new UtilityAIDecisionProvider(foeWarriorBehaviorSet, session.RulesDatabase, session.Characters, abilityResolution, positions, new SeededRandomSource(2), combatLog: combatLog);
            foeDriver = new TacticalAiTurnDriver(controller, session.Characters, foeBrain, FoesFaction);

            moveAdapter = new TacticalPlayerMoveAdapter(controller, movementResource.Id, HeroesFaction);
            inputSource.Move += moveAdapter.HandleMove;

            var targetingGate = new AbilityTargetingGate(controller.Scheduler, abilityResolution, ~0);
            inputSource.TargetingGate = targetingGate;

            var turnOrderPresenter = new TurnOrderPresenter(encounterService);
            turnOrderView.Initialize(turnOrderPresenter, hudRoot.CreateRegion(HudCorner.TopRight, 320f), session.EventBus);

            var abilityBarPresenter = new AbilityBarPresenter(session.Characters, session.RulesDatabase, abilityResolution);
            abilityBarView.Initialize(abilityBarPresenter, hudRoot.CreateRegion(HudCorner.BottomRight, 420f));

            uiBridge = new TacticalCombatUIBridge(controller, targetingGate, abilityBarView, session.EventBus, HeroesFaction);

            combatLogPanel.Initialize(combatLog, session.Characters, hudRoot.CreateRegion(HudCorner.TopLeft, 260f));
            for (var i = 0; i < heroes.Count; i++) combatLogPanel.Track(heroes[i], $"Hero {i + 1}", hitPointsResource.Id);
            for (var i = 0; i < foes.Count; i++) combatLogPanel.Track(foes[i], $"Foe {i + 1}", hitPointsResource.Id);

            session.EventBus.Subscribe(GameEvent.CombatEnded, _ => OnCombatEnded());

            Debug.Log("[TacticalEncounter] Ready -- click a Hero's Melee Strike then a Foe to attack, or click the ground to move.");
        }

        private void OnCombatEnded()
        {
            if (encounterEnded) return;
            encounterEnded = true;

            var heroesAlive = false;
            var foesAlive = false;

            // Determine the winner from whichever faction still has a live CharacterAgent -- the encounter's
            // own Combatants list is already cleared by the time CombatEnded fires (Encounter is null).
            foreach (var agent in CharacterAgent.AllLive)
            {
                if (agent == null || !agent.IsBound) continue;
                if (!worldSpawnService.TryGetSpawnId(agent.EntityId, out var spawnId)) continue;
                if (spawnId.StartsWith("hero-", StringComparison.Ordinal)) heroesAlive = true;
                else if (spawnId.StartsWith("foe-", StringComparison.Ordinal)) foesAlive = true;
            }

            combatLogPanel.ShowResult(heroesAlive && !foesAlive ? "Victory!" : !heroesAlive && foesAlive ? "Defeat." : "Combat over.");
        }

        private RuntimeEntityId SpawnCombatant(string spawnId, GameObject prefab, Vector3 position, DefinitionId faction, int hitPointsMax)
        {
            var characterId = RuntimeEntityId.NewId();
            var character = new CharacterState(characterId);
            character.Stats.SetScore(strengthStat.Id, 15);
            character.AddResource(new ResourceState(hitPointsResource.Id, hitPointsMax));
            character.AddResource(new ResourceState(actionResource.Id, 1));
            character.AddResource(new ResourceState(reactionResource.Id, 1));
            character.AddResource(new ResourceState(movementResource.Id, 8));
            character.GrantFeature(reactiveStrikes.Id);
            character.LearnAbility(meleeStrike.Id);
            Session.Characters.Register(character);

            worldSpawnService.BindRestoredView(spawnId, characterId, default, prefab, position, Quaternion.identity, faction, startInactive: false);
            return characterId;
        }

        /// <summary>A trivial IPositionProvider reading live CharacterAgent transforms -- IPositionProvider has
        /// no shipped production implementation in the package (only test-only fakes), so every real
        /// composition root writes its own small one.</summary>
        private sealed class LiveCharacterPositionProvider : IPositionProvider
        {
            public Vector3 GetPosition(RuntimeEntityId entity)
            {
                foreach (var agent in CharacterAgent.AllLive)
                {
                    if (agent != null && agent.EntityId == entity) return agent.transform.position;
                }

                return Vector3.zero;
            }
        }

        /// <summary>MovementResolutionService's IMovementController is only consulted for its own legacy
        /// MoveToward path -- this sample exclusively uses the real per-frame MoveActionExecution path (via
        /// ActionScheduler), so this is a no-op, matching the same precedent test fixtures already use.</summary>
        private sealed class SceneMovementController : IMovementController
        {
            public float TryMoveTowards(RuntimeEntityId entity, Vector3 destination, float maxDistance) => 0f;
        }

        private void Update()
        {
            if (controller == null || encounterEnded) return;

            controller.Tick(Time.deltaTime);
            foeDriver?.Tick(Time.deltaTime);

            CheckWinCondition();
        }

        private void CheckWinCondition()
        {
            var encounter = controller.EncounterService.Encounter;
            if (encounter == null) return; // Combat hasn't started yet, or already ended.

            var heroesAlive = false;
            var foesAlive = false;
            foreach (var combatant in encounter.Combatants)
            {
                if (combatant.Faction == HeroesFaction) heroesAlive = true;
                else if (combatant.Faction == FoesFaction) foesAlive = true;
            }

            if (heroesAlive && foesAlive) return; // Both sides still standing -- fight continues.

            controller.EncounterService.EndCombat();
        }

        private void OnDestroy()
        {
            controller?.Dispose();
            host?.EndSession();
        }
    }
}

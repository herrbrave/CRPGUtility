using System.Collections.Generic;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Gameplay;
using CRPG.Gameplay.Combat;
using CRPG.InputSystem;
using CRPG.Navigation;
using CRPG.Rules.SRD5;
using CRPG.Saving;
using CRPG.UI;
using UnityEngine;

// Dogfood-only demo bootstrap -- not part of the shipped package. Task 4.5 (Gate C): the real tactical-combat
// composition root, modeled on AdventureSliceBootstrap's real session/spawn/input shape fused with
// AICombatDemoBootstrap's turn/combat orchestration -- but every action goes through the real
// TacticalEncounterController/ActionScheduler, never MovementResolutionService.MoveToward/
// AbilityResolutionService.Execute directly. NEVER modeled on OakhavenDemoBootstrap.
public sealed class TacticalSliceBootstrap : MonoBehaviour
{
    private static readonly DefinitionId HeroesFaction = new("faction-heroes-tacticalslice");
    private static readonly DefinitionId GoblinsFaction = new("faction-goblins-tacticalslice");

    [Header("Content")]
    [SerializeField] private TacticalSliceContentBundle content;

    [Header("Party / Enemies")]
    [SerializeField] private GameObject maleHumanoidPrefab;
    [SerializeField] private GameObject femaleHumanoidPrefab;
    [SerializeField] private Transform[] heroSpawnAnchors;
    [SerializeField] private Transform[] goblinSpawnAnchors;

    [Header("Input")]
    [SerializeField] private InputSystemCommandSource inputSource;
    [SerializeField] private LayerMask hittableMask = ~0;

    [Header("UI")]
    [SerializeField] private HudRoot hudRoot;
    [SerializeField] private TurnOrderView turnOrderView;
    [SerializeField] private AbilityBarView abilityBarView;

    private RulesDatabase rulesDatabase;
    private GameSessionHost host;
    private GameSession session;
    private WorldSpawnService worldSpawnService;
    private TacticalEncounterController controller;
    private TacticalAiTurnDriver aiDriver;
    private TacticalPlayerMoveAdapter moveAdapter;
    private AbilityTargetingGate targetingGate;
    private TacticalCombatUIBridge uiBridge;
    private DeathSaveService deathSaveService;
    private ConcentrationService concentrationService;
    private SaveRequestService saveRequestService;
    private bool combatEnded;

    private void Start()
    {
        if (content == null)
        {
            Debug.LogError("[TacticalSliceBootstrap] No TacticalSliceContentBundle assigned.");
            return;
        }

        BuildContent();

        host = gameObject.AddComponent<GameSessionHost>();
        session = host.StartSession(rulesDatabase, new DiceService(new SeededRandomSource(System.Environment.TickCount)));
        worldSpawnService = new WorldSpawnService(session);

        var heroes = SpawnParty();
        var goblins = SpawnGoblins();
        WireEncounter(heroes, goblins);
        WireInput();
        WireUi();

        Debug.Log("[TacticalSliceBootstrap] Ready.");
    }

    private void BuildContent()
    {
        rulesDatabase = new RulesDatabase();
        rulesDatabase.RegisterRange(content.FighterContent.Definitions);
        rulesDatabase.RegisterRange(content.Definitions);
        rulesDatabase.Register(content.FireBolt);
        rulesDatabase.Register(content.HealingWord);
        rulesDatabase.Register(content.Bless);
        rulesDatabase.Register(content.Command);
    }

    /// <summary>Grants everything a combatant needs to actually cast/attempt its abilities -- see Task 4.5's
    /// "content readiness checklist": SpellcastingStatId, KnownAbilities, every authored cost resource with
    /// real max/current, and the Reactive Strikes feature for melee-capable combatants.</summary>
    private RuntimeEntityId SpawnCombatant(string spawnId, Transform anchor, GameObject prefab, DefinitionId faction, int strength, int dexterity, bool isSpellcaster)
    {
        var character = new CharacterState(RuntimeEntityId.NewId()) { Level = 3 };
        character.Stats.SetScore(content.FighterContent.Strength.Id, strength);
        character.Stats.SetScore(content.FighterContent.Dexterity.Id, dexterity);
        character.Stats.SetScore(content.FighterContent.Wisdom.Id, isSpellcaster ? 16 : 10);
        character.AddResource(new ResourceState(content.FighterContent.HitPoints.Id, max: 20));
        character.AddResource(new ResourceState(content.FighterContent.Action.Id, max: 1));
        character.AddResource(new ResourceState(content.FighterContent.ReactionResource.Id, max: 1));
        character.AddResource(new ResourceState(content.Movement.Id, max: 6));

        character.GrantFeature(content.ReactiveStrikes.Id);
        character.LearnAbility(content.MeleeStrike.Id);

        if (isSpellcaster)
        {
            character.SpellcastingStatId = content.FighterContent.Wisdom.Id;
            character.LearnAbility(content.FireBolt.Id);
            character.LearnAbility(content.HealingWord.Id);
            character.LearnAbility(content.Bless.Id);
            character.LearnAbility(content.Command.Id);
            foreach (var abilityCostResource in SpellResourcesFor(content))
                character.AddResource(new ResourceState(abilityCostResource.Id, max: 4));
        }

        session.Characters.Register(character);

        var position = anchor != null ? anchor.position : Vector3.zero;
        var rotation = anchor != null ? anchor.rotation : Quaternion.identity;
        var agent = worldSpawnService.BindRestoredView(spawnId, character.Id, default, prefab, position, rotation, faction, startInactive: false);

        return character.Id;
    }

    /// <summary>Every distinct ResourceDefinition FireBolt/HealingWord/Bless/Command's own authored Costs
    /// reference, besides Action -- read directly off the real assets rather than hardcoded, so a future
    /// content change to any of these four spells' costs doesn't silently leave a caster unable to afford
    /// them.</summary>
    private static IEnumerable<CRPG.Actors.ResourceDefinition> SpellResourcesFor(TacticalSliceContentBundle bundle)
    {
        var seen = new HashSet<DefinitionId>();
        foreach (var ability in new[] { bundle.FireBolt, bundle.HealingWord, bundle.Bless, bundle.Command })
        {
            foreach (var cost in ability.Costs)
            {
                if (seen.Add(cost.Resource.Id)) yield return cost.Resource;
            }
        }
    }

    private List<(RuntimeEntityId Id, DefinitionId Faction)> SpawnParty()
    {
        var heroes = new List<(RuntimeEntityId, DefinitionId)>();
        var prefabs = new[] { maleHumanoidPrefab, femaleHumanoidPrefab, maleHumanoidPrefab, femaleHumanoidPrefab };
        for (var i = 0; i < heroSpawnAnchors.Length; i++)
        {
            var id = SpawnCombatant($"hero-{i + 1}", heroSpawnAnchors[i], prefabs[i % prefabs.Length], HeroesFaction,
                strength: 16, dexterity: 12, isSpellcaster: i == 3); // the 4th hero is the party's caster
            heroes.Add((id, HeroesFaction));
        }

        return heroes;
    }

    private List<(RuntimeEntityId Id, DefinitionId Faction)> SpawnGoblins()
    {
        var goblins = new List<(RuntimeEntityId, DefinitionId)>();
        for (var i = 0; i < goblinSpawnAnchors.Length; i++)
        {
            var id = SpawnCombatant($"goblin-{i + 1}", goblinSpawnAnchors[i], maleHumanoidPrefab, GoblinsFaction,
                strength: 10, dexterity: 14, isSpellcaster: i == 7); // the 8th goblin is the pack's shaman
            goblins.Add((id, GoblinsFaction));
        }

        return goblins;
    }

    private void WireEncounter(List<(RuntimeEntityId Id, DefinitionId Faction)> heroes, List<(RuntimeEntityId Id, DefinitionId Faction)> goblins)
    {
        var positions = new SceneEntityPositionProvider();
        foreach (var agent in CharacterAgent.AllLive) positions.Register(agent.EntityId, agent.transform);

        var encounterService = new CRPG.Combat.CombatEncounterService(
            session.Characters, new DefaultInitiativeRule(session.Characters, rulesDatabase, session.DiceService),
            new CRPG.Combat.TurnResourceSet(new[] { content.FighterContent.Action, content.FighterContent.ReactionResource, content.Movement }),
            session.EventBus);

        var factionProvider = new CRPG.Combat.EncounterFactionProvider(encounterService);
        var lineOfSight = new PhysicsLineOfSightProvider(hittableMask);
        var attackBonusRule = new DefaultAttackBonusRule(session.Characters, rulesDatabase);
        var armorClassRule = new DefaultArmorClassRule(session.Characters, rulesDatabase, content.FighterContent.Dexterity.Id);
        var savingThrowRule = new DefaultSavingThrowRule(session.Characters, rulesDatabase);

        var abilityResolution = new CRPG.Abilities.AbilityResolutionService(
            session.Characters, session.DiceService, combatLog: null, eventBus: session.EventBus, positions: positions,
            attackBonusRule: attackBonusRule, armorClassRule: armorClassRule, savingThrowRule: savingThrowRule,
            rulesDatabase: rulesDatabase, factionProvider: factionProvider, lineOfSightProvider: lineOfSight);
        abilityResolutionForUi = abilityResolution;

        var reactionService = new CRPG.Combat.ReactionService(
            session.Characters, abilityResolution, new CRPG.Combat.FirstEligibleReactionProvider(), content.FighterContent.ReactionResource);
        var movementResolution = new CRPG.Combat.MovementResolutionService(
            session.Characters, positions, positions, combatLog: null, reactionService: reactionService, rulesDatabase: rulesDatabase);

        var presentationHook = new CharacterAnimationPresentationHook();
        controller = new TacticalEncounterController(
            session.Characters, session.EventBus, positions, abilityResolution, movementResolution, new NavMeshService(),
            encounterService, worldSpawnService, Combine(heroes, goblins), presentationHook);

        deathSaveService = new DeathSaveService(
            session.Characters, session.DiceService, session.EventBus, content.FighterContent.HitPoints.Id,
            FindUnconsciousCondition(), combatLog: null);
        concentrationService = new ConcentrationService(
            session.Characters, session.DiceService, savingThrowRule, session.EventBus, content.FighterContent.Wisdom.Id);

        aiDriver = new TacticalAiTurnDriver(
            controller, session.Characters,
            new CRPG.AI.UtilityAIDecisionProvider(BuildGoblinBehaviorSet(), rulesDatabase, session.Characters, abilityResolution, positions, new SeededRandomSource(1)),
            GoblinsFaction, deathSaveService.RollDeathSave);

        targetingGate = new AbilityTargetingGate(controller.Scheduler, abilityResolution, hittableMask);
        moveAdapter = new TacticalPlayerMoveAdapter(controller, content.Movement.Id, HeroesFaction);

        var safePointGate = new SafePointGate(combat: controller.EncounterService);
        saveRequestService = new SaveRequestService(session, worldSpawnService, safePointGate);
    }

    private static DefinitionId FindUnconsciousCondition() => new("condition-unconscious");

    private CRPG.AI.BehaviorSetDefinition BuildGoblinBehaviorSet()
    {
        // Minimal inline behavior set: close distance, then Melee Strike whichever enemy is nearest -- the
        // same "Close Distance + Attack" pairing AICombatDemoBootstrap's own real content already proves works
        // against a shared AbilityResolutionService, now driven through the real ActionScheduler instead.
        var closeDistance = ScriptableObject.CreateInstance<CRPG.AI.BehaviorDefinition>();
        SetField(closeDistance, "id", "behavior-tacticalslice-close");
        SetField(closeDistance, "displayName", "Close Distance");
        SetField(closeDistance, "kind", CRPG.AI.BehaviorKind.Move);
        SetField(closeDistance, "movementResourceId", content.Movement.Id);
        SetField(closeDistance, "moveTargetRelation", CRPG.Abilities.TargetRelation.Enemy);
        SetField(closeDistance, "stopDistance", 1.5f);
        SetField(closeDistance, "considerations", new List<CRPG.AI.Consideration>());

        var attack = ScriptableObject.CreateInstance<CRPG.AI.BehaviorDefinition>();
        SetField(attack, "id", "behavior-tacticalslice-attack");
        SetField(attack, "displayName", "Melee Strike Nearest");
        SetField(attack, "abilityId", content.MeleeStrike.Id);
        SetField(attack, "considerations", new List<CRPG.AI.Consideration>());

        var set = ScriptableObject.CreateInstance<CRPG.AI.BehaviorSetDefinition>();
        SetField(set, "id", "behaviorset-tacticalslice-goblins");
        SetField(set, "displayName", "Goblins");
        SetField(set, "behaviorIds", new List<DefinitionId> { closeDistance.Id, attack.Id });

        rulesDatabase.Register(closeDistance);
        rulesDatabase.Register(attack);
        return set;
    }

    private static void SetField(object target, string fieldName, object value)
    {
        var type = target.GetType();
        while (type != null)
        {
            var field = type.GetField(fieldName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly);
            if (field != null) { field.SetValue(target, value); return; }
            type = type.BaseType;
        }

        Debug.LogError($"[TacticalSliceBootstrap] Field '{fieldName}' not found on {target.GetType().Name}.");
    }

    private static List<(RuntimeEntityId Id, DefinitionId Faction)> Combine(
        List<(RuntimeEntityId Id, DefinitionId Faction)> a, List<(RuntimeEntityId Id, DefinitionId Faction)> b)
    {
        var combined = new List<(RuntimeEntityId, DefinitionId)>(a);
        combined.AddRange(b);
        return combined;
    }

    private void WireInput()
    {
        if (inputSource == null) return;
        inputSource.Move += moveAdapter.HandleMove;
        inputSource.TargetingGate = targetingGate;
    }

    private void WireUi()
    {
        if (hudRoot != null)
        {
            var namesByCharacter = new Dictionary<RuntimeEntityId, string>();
            var turnOrderPresenter = new TurnOrderPresenter(controller.EncounterService, namesByCharacter);
            if (turnOrderView != null) turnOrderView.Initialize(turnOrderPresenter, hudRoot.CreateRegion(HudCorner.TopLeft, width: 500f), session.EventBus);

            var abilityBarPresenter = new AbilityBarPresenter(session.Characters, rulesDatabase, abilityResolutionForUi);
            if (abilityBarView != null)
            {
                abilityBarView.Initialize(abilityBarPresenter, hudRoot.CreateRegion(HudCorner.BottomLeft, width: 500f));
                uiBridge = new TacticalCombatUIBridge(controller, targetingGate, abilityBarView, session.EventBus, HeroesFaction, deathSaveService.RollDeathSave);
            }
        }
    }

    // AbilityBarPresenter needs the SAME IAbilityResolutionService instance the scheduler/controller use --
    // WireEncounter assigns this; WireUi reads it. A field rather than a return value since WireEncounter's
    // own local also feeds several other collaborators constructed in the same method.
    private CRPG.Abilities.IAbilityResolutionService abilityResolutionForUi;

    private void Update()
    {
        controller?.Tick(Time.deltaTime);
        aiDriver?.Tick(Time.deltaTime);

        if (!combatEnded) CheckWinCondition();
    }

    private void CheckWinCondition()
    {
        var encounter = controller.EncounterService.Encounter;
        if (encounter == null) return;

        var heroesAlive = false;
        var goblinsAlive = false;
        foreach (var combatant in encounter.Combatants)
        {
            if (combatant.Faction == HeroesFaction) heroesAlive = true;
            else if (combatant.Faction == GoblinsFaction) goblinsAlive = true;
        }

        if (heroesAlive && goblinsAlive) return;

        combatEnded = true;
        controller.EncounterService.EndCombat();
        Debug.Log(heroesAlive ? "[TacticalSliceBootstrap] Heroes are victorious!" : "[TacticalSliceBootstrap] Goblins are victorious!");

        var result = saveRequestService.RequestSave("tactical-slice-post-combat");
        Debug.Log($"[TacticalSliceBootstrap] Post-combat save: {result.Outcome}");
    }

    private void OnDestroy()
    {
        uiBridge?.Dispose();
        controller?.Dispose();
        host?.EndSession();
    }
}

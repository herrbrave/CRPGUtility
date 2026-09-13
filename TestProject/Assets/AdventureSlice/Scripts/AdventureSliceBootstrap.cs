using System.Collections;
using System.Collections.Generic;
using CRPG.Actors;
using CRPG.Camera;
using CRPG.Core;
using CRPG.Dialogue;
using CRPG.Exploration;
using CRPG.Gameplay;
using CRPG.Gameplay.Interactions;
using CRPG.Gameplay.Interactions.Adapters;
using CRPG.Gameplay.Scenes;
using CRPG.Inventory;
using CRPG.InputSystem;
using CRPG.Navigation;
using CRPG.Party;
using CRPG.Quests;
using CRPG.Saving;
using CRPG.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

// Dogfood-only tooling -- not part of the shipped package.
//
// Task 3.6: the AdventureSlice composition root, living in the persistent Core scene. Modeled on
// InteractionLabHarness's and CharacterLabHarness's precedent (live session wiring only happens at Play time,
// scene authoring can't reach into it) -- NEVER on OakhavenDemoBootstrap, which stays permanently off-limits
// per a standing project decision. Boots straight into the starting location (Overworld) additively, then
// exposes RequestTravel/the Save presenter for the rest of Gate B's playable loop.
public sealed class AdventureSliceBootstrap : MonoBehaviour
{
    [Header("Party")]
    [SerializeField] private GameObject[] partyMemberPrefabs;
    [SerializeField] private Transform initialSpawnAnchor;
    [SerializeField] private ResourceDefinition hitPointsResource;

    [Header("Content")]
    [SerializeField] private ItemDefinition currencyItem;
    [SerializeField] private List<DialogueDefinition> dialogues;
    [SerializeField] private List<QuestDefinition> quests;
    // Any other content whose DefinitionId can end up in a save (a store/loot listing the player actually
    // buys/loots, a quest reward item, an NPC spawn) must be registered here too -- ContentValidator resolves
    // every id in a save against this RulesDatabase, and a definition that's only ever referenced from inside
    // an authored scene component (StoreListingSeed, LootItemSeed, ...) is otherwise never registered at all.
    [SerializeField] private List<ItemDefinition> otherItems;
    [SerializeField] private List<NpcDefinition> npcDefinitions;

    [Header("Scenes")]
    [SerializeField] private string startingLocationScenePath;
    [SerializeField] private string startingDestinationId;

    [Header("Input / Camera")]
    [SerializeField] private InputSystemCommandSource inputSource;
    [SerializeField] private IsometricCameraRig cameraRig;
    [SerializeField] private PartyCameraFollowBehaviour cameraFollow;

    [Header("UI")]
    [SerializeField] private HudRoot hudRoot;
    [SerializeField] private PartyView partyView;
    [SerializeField] private InventoryView inventoryView;
    [SerializeField] private QuestLogView questLogView;
    [SerializeField] private SaveView saveView;

    private GameSessionHost host;
    private WorldSpawnService worldSpawnService;
    private RulesDatabase rulesDatabase;
    private WorldTransitionGate transitionGate;
    private SceneTravelCoordinator travelCoordinator;
    private SceneTravelRunner travelRunner;
    private SessionLoadCoordinator loadCoordinator;
    private InteractionControllerBehaviour interactionHost;
    private PartyExplorationController explorationController;
    private NavMeshService navMeshService;

    private PartyPresenter partyPresenter;
    private InventoryPresenter inventoryPresenter;
    private QuestLogPresenter questLogPresenter;
    private QuestService questService;
    private EquipmentService equipmentService;
    private GameSession currentSession;

    // Task 3.6: each view's HudRoot region is created exactly once in WireUi and reused on every later
    // rebind (OnLoadFinished) -- calling CreateRegion again on a load would leak a duplicate region per load.
    private VisualElement partyRegion;
    private VisualElement questLogRegion;

    private readonly Dictionary<RuntimeEntityId, GameObject> partyMemberVisualPrefabs = new();

    private void Start() => StartCoroutine(BootCoroutine());

    private IEnumerator BootCoroutine()
    {
        var coreScene = gameObject.scene;

        rulesDatabase = new RulesDatabase();
        rulesDatabase.Register(hitPointsResource);
        rulesDatabase.Register(currencyItem);
        foreach (var dialogue in dialogues) rulesDatabase.Register(dialogue);
        foreach (var quest in quests) rulesDatabase.Register(quest);
        if (otherItems != null) foreach (var item in otherItems) rulesDatabase.Register(item);
        if (npcDefinitions != null) foreach (var npc in npcDefinitions) rulesDatabase.Register(npc);

        host = gameObject.AddComponent<GameSessionHost>();
        var session = host.StartSession(rulesDatabase, new DiceService(new SeededRandomSource(1)));
        currentSession = session;
        worldSpawnService = new WorldSpawnService(session);
        equipmentService = new EquipmentService(session.Characters, rulesDatabase, session.Inventory, session.EventBus);
        questService = new QuestService(session.Quests, session.Timeline);
        foreach (var quest in quests) questService.StartQuest(quest);

        transitionGate = new WorldTransitionGate();
        navMeshService = new NavMeshService();

        var travelRunnerGo = new GameObject("SceneTravelRunner");
        travelRunnerGo.transform.SetParent(transform);
        travelRunner = travelRunnerGo.AddComponent<SceneTravelRunner>();
        travelCoordinator = new SceneTravelCoordinator(session, worldSpawnService, session.Party, transitionGate, cameraRig != null ? cameraRig.Rig : null);
        travelRunner.Coordinator = travelCoordinator;

        loadCoordinator = new SessionLoadCoordinator(gate: transitionGate);

        // Cold boot: no "old location" to travel from, so this goes straight to LocationInitializer/placement
        // rather than through SceneTravelCoordinator (which assumes an outgoing location to release/unload).
        // Loaded BEFORE SpawnParty deliberately: instantiating a party member's prefab enables its
        // NavMeshAgent synchronously (Instantiate itself runs Awake/OnEnable before any later SetActive(false)
        // could ever prevent it -- the same "too late" gotcha already on file elsewhere in this project), and
        // with no NavMesh loaded anywhere yet, that OnEnable logs a real "Failed to create agent because there
        // is no valid NavMesh" warning. Spawning the party only once a real NavMesh already exists avoids this
        // at the source rather than trying to out-race Instantiate's own synchronous lifecycle calls.
        yield return SceneManager.LoadSceneAsync(startingLocationScenePath, LoadSceneMode.Additive);
        var startingScene = SceneManager.GetSceneByPath(startingLocationScenePath);
        SceneManager.SetActiveScene(startingScene);
        yield return null; // NavMesh readiness -- see SceneTravelCoordinator's own remarks on this same wait.

        SpawnParty(session, coreScene);
        WireInputAndInteraction(session);
        WireUi(session);

        if (inputSource != null) inputSource.TransitionGate = transitionGate;

        var content = FindLocationContent(startingScene);
        if (content != null)
        {
            LocationInitializer.Prepare(content, session, worldSpawnService, startingScene);
            if (cameraRig != null) cameraRig.Rig.SetBounds(content.CameraBoundsXZ);
        }

        WireTalkAdapters(session);
        WireTravelAdapters();

        PlaceInitialParty(session, startingScene);

        // Re-snap AFTER the party is warped to its real starting position -- cameraFollow.Initialize ran
        // earlier (in WireInputAndInteraction, alongside input/interaction wiring) while party members were
        // still at their pre-Overworld spawn offsets, so its own SnapToActiveCharacter() call would otherwise
        // have snapped to the wrong place.
        if (cameraFollow != null) cameraFollow.Initialize(session.Party, new SceneSelectableSource());

        Debug.Log("[AdventureSlice] Ready.");
    }

    private void SpawnParty(GameSession session, Scene coreScene)
    {
        for (var i = 0; i < partyMemberPrefabs.Length; i++)
        {
            // Fixed authored fixture roster: visual lookup must survive a process relaunch.
            // Runtime-generated IDs only work for same-process saves.
            var characterId = RuntimeEntityId.FromString("ad00000000000000000000000000000" + (i + 1).ToString("x"));
            var character = new CharacterState(characterId);
            if (hitPointsResource != null) character.AddResource(new ResourceState(hitPointsResource.Id, max: 20, current: 20));
            session.Characters.Register(character);
            session.Party.AddMember(characterId);

            var prefab = partyMemberPrefabs[i];
            partyMemberVisualPrefabs[characterId] = prefab;

            var position = initialSpawnAnchor != null ? initialSpawnAnchor.position + new Vector3(i * 1.5f, 0f, 0f) : new Vector3(i * 1.5f, 0f, 0f);
            var instance = worldSpawnService.BindRestoredView(characterId.ToString(), characterId, default, prefab, position, Quaternion.identity, default, startInactive: false);
            SceneManager.MoveGameObjectToScene(instance, coreScene);
        }
    }

    private void PlaceInitialParty(GameSession session, Scene startingScene)
    {
        var destinations = FindObjectsByType<SceneTravelDestination>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        SceneTravelDestination start = null;
        foreach (var d in destinations)
        {
            if (d.gameObject.scene == startingScene && d.DestinationId == startingDestinationId) { start = d; break; }
        }

        if (start == null) return;

        var members = session.Party.Members;
        var positions = new WedgeFormationSolver().Solve(start.Anchor.position, start.Anchor.forward, members.Count, spacing: 2f);
        for (var i = 0; i < members.Count; i++)
        {
            var agent = FindCharacterAgent(members[i]);
            if (agent == null) continue;

            var navAgent = agent.GetComponent<UnityEngine.AI.NavMeshAgent>();
            navAgent?.Warp(positions[i]);
            agent.transform.rotation = start.Anchor.rotation;
        }
    }

    private void WireInputAndInteraction(GameSession session)
    {
        if (interactionHost != null)
        {
            interactionHost.Controller.Dispose();
            Destroy(interactionHost.gameObject);
        }
        var interactionHostGo = new GameObject("InteractionControllerHost");
        interactionHostGo.transform.SetParent(transform);
        interactionHost = interactionHostGo.AddComponent<InteractionControllerBehaviour>();
        interactionHost.Initialize(inputSource, session.Party, session);
        // Simple, robust integration for a fixture: recheck every quest's progress, and re-wire any
        // TalkInteractionAdapter now in the loaded scenes (a travel may have just brought a new location's
        // merchant into existence), after anything happens -- talk/loot/travel/etc all end here -- rather than
        // wiring a bespoke per-action hook for each.
        interactionHost.Controller.RequestEnded += _ =>
        {
            foreach (var quest in quests) questService.RefreshProgress(quest);
            WireTalkAdapters(session);
            WireTravelAdapters();
            inventoryView?.Refresh();
            questLogView?.Refresh();
        };

        var movementService = new PartyMovementService(navMeshService ?? new NavMeshService(), new SceneSelectableSource());
        explorationController = new PartyExplorationController(session.Party, movementService, new WedgeFormationSolver(), inputSource);
        foreach (var memberId in session.Party.Members)
        {
            var agent = FindCharacterAgent(memberId);
            if (agent?.Motor != null) explorationController.RegisterMotor(memberId, agent.Motor);
        }

        if (cameraFollow != null) cameraFollow.Initialize(session.Party, new SceneSelectableSource());
    }

    private void WireUi(GameSession session)
    {
        if (hudRoot != null) hudRoot.GetComponent<UnityEngine.UIElements.UIDocument>().rootVisualElement.Clear();
        var names = new Dictionary<RuntimeEntityId, string>();
        var i = 0;
        foreach (var memberId in session.Party.Members) names[memberId] = $"Hero {++i}";

        partyPresenter = new PartyPresenter(session.Party, session.Characters, hitPointsResource, names);
        if (partyView != null && hudRoot != null)
        {
            partyRegion = hudRoot.CreateRegion(HudCorner.TopLeft, width: 460f);
            partyView.Initialize(partyPresenter, partyRegion);
        }

        inventoryPresenter = new InventoryPresenter(session.Inventory, rulesDatabase, equipmentService);
        if (inventoryView != null && hudRoot != null)
        {
            inventoryView.Initialize(inventoryPresenter, hudRoot.CreateRegion(HudCorner.TopRight, width: 700f), session.EventBus);
            inventoryView.FocusedCharacter = session.Party.ActiveCharacter;
        }

        questLogPresenter = new QuestLogPresenter(session.Quests, rulesDatabase);
        if (questLogView != null && hudRoot != null)
        {
            questLogRegion = hudRoot.CreateRegion(HudCorner.BottomLeft, width: 460f);
            questLogView.Initialize(questLogPresenter, questLogRegion, session.EventBus);
        }

        if (saveView != null && hudRoot != null)
        {
            var saveDirectory = System.IO.Path.Combine(Application.persistentDataPath, "AdventureSlice");
            System.IO.Directory.CreateDirectory(saveDirectory);

            var safePointGate = new SafePointGate(interactions: interactionHost.Controller, loadCoordinator: loadCoordinator, transitionGate: transitionGate);
            var saveRequestService = new SaveRequestService(session, worldSpawnService, safePointGate);
            var loadRequestAdapter = new SessionLoadRequestAdapter(
                loadCoordinator, travelRunner, rulesDatabase, session.DiceService, host,
                () => worldSpawnService, () => gameObject.scene, () => partyMemberVisualPrefabs,
                OnLoadFinished);

            var savePresenter = new SavePresenter(saveRequestService, loadRequestAdapter, saveDirectory);
            saveView.Initialize(savePresenter, hudRoot.CreateRegion(HudCorner.BottomRight, width: 520f));
        }
    }

    /// <summary>A load discards the old session/world entirely and adopts a brand-new one -- every consumer
    /// that cached session-scoped state must be rebound, exactly like the composition root's own initial wiring
    /// (SessionLoadCoordinator's own documented caveat). Travel, by contrast, keeps the SAME session/party alive
    /// throughout and needs none of this.</summary>
    private void OnLoadFinished(LoadResult result)
    {
        if (result.Outcome != LoadOutcome.Success) return;

        worldSpawnService = result.WorldSpawnService;
        var session = result.Session;
        currentSession = session;

        partyPresenter.Rebind(session.Party, session.Characters);
        inventoryPresenter.Rebind(session.Inventory, rulesDatabase, new EquipmentService(session.Characters, rulesDatabase, session.Inventory, session.EventBus));
        equipmentService = new EquipmentService(session.Characters, rulesDatabase, session.Inventory, session.EventBus);
        questLogPresenter.Rebind(session.Quests, rulesDatabase);
        questService = new QuestService(session.Quests, session.Timeline);

        explorationController?.Dispose();
        travelCoordinator = new SceneTravelCoordinator(session, worldSpawnService, session.Party, transitionGate, cameraRig != null ? cameraRig.Rig : null);
        travelRunner.Coordinator = travelCoordinator;

        WireInputAndInteraction(session);
        WireTalkAdapters(session);
        WireTravelAdapters();
        if (inventoryView != null) inventoryView.FocusedCharacter = session.Party.ActiveCharacter;

        // Recreate every session-bound port/subscription, including SaveRequestService and its gate.
        // Reusing the old SavePresenter otherwise saves the disposed session after the first load.
        WireUi(session);

        Debug.Log("[AdventureSlice] Load complete, all consumers rebound.");
    }

    /// <summary>TalkInteractionAdapter's session dependencies (Party/Characters/RulesDatabase/Timeline/
    /// Inventory/DiceService/CurrencyItemId) can't be authored at scene-authoring time -- no live session
    /// exists yet. LocationInitializer only wires an adapter's Store half (StoreContentPoint); this covers the
    /// rest, for every TalkInteractionAdapter currently loaded, idempotently.</summary>
    private void WireTalkAdapters(GameSession session)
    {
        var skillCheckRule = new CRPG.Rules.SRD5.DefaultSkillCheckRule(session.Characters, session.RulesDatabase);
        foreach (var adapter in FindObjectsByType<TalkInteractionAdapter>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            adapter.Party = session.Party;
            adapter.Characters = session.Characters;
            adapter.RulesDatabase = session.RulesDatabase;
            adapter.Timeline = session.Timeline;
            adapter.Inventory = session.Inventory;
            adapter.DiceService = session.DiceService;
            adapter.CurrencyItemId = currencyItem != null ? currencyItem.Id : default;
            adapter.SkillCheckRule = skillCheckRule;
        }
    }

    /// <summary>Same category of gap as WireTalkAdapters -- TravelInteractionAdapter.Runner/Gate can't be
    /// authored at scene-authoring time either (Runner is a Core-scene-only object; Gate is constructed at
    /// boot). Wires every TravelInteractionAdapter currently loaded, idempotently.</summary>
    private void WireTravelAdapters()
    {
        foreach (var adapter in FindObjectsByType<TravelInteractionAdapter>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            adapter.Runner = travelRunner;
            adapter.Gate = transitionGate;
        }
    }

    private static ILocationContent FindLocationContent(Scene scene)
    {
        foreach (var candidate in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (candidate is ILocationContent content && candidate.gameObject.scene == scene) return content;
        }

        return null;
    }

    private static CharacterAgent FindCharacterAgent(RuntimeEntityId id)
    {
        foreach (var agent in CharacterAgent.AllLive)
        {
            if (agent != null && agent.EntityId == id) return agent;
        }

        return null;
    }

    private void OnDestroy() => host?.EndSession();
}

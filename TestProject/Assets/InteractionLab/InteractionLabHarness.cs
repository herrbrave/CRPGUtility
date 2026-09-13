using System.Collections.Generic;
using CRPG.Core;
using CRPG.Gameplay;
using CRPG.Gameplay.Interactions;
using CRPG.Gameplay.Interactions.Adapters;
using CRPG.Inventory;
using CRPG.UI;
using UnityEngine;

// Dogfood-only tooling (see third_party_assets / TestProject conventions) -- not part of the shipped package.
//
// Task 3.3's manual-QA proof of the shared interaction lifecycle, mirroring CharacterLabHarness's own role for
// Gate A: builds a real GameSession/PlayerCommandRouter/InteractionControllerBehaviour at Play time (scene
// authoring can't wire live session objects into TalkInteractionAdapter/LootInteractionAdapter -- those only
// exist once Play starts), spawns a real character, and lets a reviewer click each fixture and watch the full
// approach/turn/execute/log lifecycle. There is no camera-follow system here (a fixed camera, matching
// CharacterLab's own precedent) -- TeleportInteractionAdapter's Teleported event has nothing to listen for it
// in this scene, which is the point: that seam is reserved, not silently broken.
public sealed class InteractionLabHarness : MonoBehaviour
{
    [SerializeField] private GameObject characterPrefab;
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private Camera mainCamera;
    [SerializeField] private TalkInteractionAdapter talkAdapter;
    [SerializeField] private LootInteractionAdapter lootAdapter;
    [SerializeField] private ItemDefinition seedLootItem;

    private GameSession session;
    private PlayerCommandRouter router;
    private InteractionControllerBehaviour interactionHost;
    private CharacterAgent performerAgent;

    private readonly List<string> logLines = new();

    private void Start()
    {
        var rulesDatabase = new RulesDatabase();
        session = new GameSession(rulesDatabase, new DiceService(new SeededRandomSource(1)));

        var characterGo = Instantiate(characterPrefab, spawnPoint != null ? spawnPoint.position : Vector3.zero, Quaternion.identity);
        performerAgent = characterGo.GetComponent<CharacterAgent>();
        performerAgent.Bind(RuntimeEntityId.NewId(), session);
        session.Party.AddMember(performerAgent.EntityId);

        // Live session dependencies can only be wired now -- they don't exist at scene-authoring (edit) time.
        if (talkAdapter != null)
        {
            talkAdapter.Party = session.Party;
            talkAdapter.Characters = session.Characters;
            talkAdapter.RulesDatabase = session.RulesDatabase;
            talkAdapter.Timeline = session.Timeline;
            talkAdapter.Inventory = session.Inventory;
            talkAdapter.DiceService = session.DiceService;
            talkAdapter.SkillCheckRule = new CRPG.Rules.SRD5.DefaultSkillCheckRule(session.Characters, session.RulesDatabase);
        }

        // LootContainerState is deliberately plain runtime C# state (not [SerializeField]) -- an item added to
        // it at scene-authoring (edit) time would be lost the moment the scene reloads for Play, since Unity
        // only persists serialized fields. Seed it here, at actual runtime, instead.
        if (lootAdapter != null)
        {
            lootAdapter.Inventory = session.Inventory;
            if (seedLootItem != null) lootAdapter.Container.AddItem(new ItemInstance(RuntimeEntityId.NewId(), seedLootItem.Id));
        }

        router = new PlayerCommandRouter(new SceneSelectableSource(), new UIToolkitPointerCapture(), ~0);
        router.Interact += cmd => Log($"Interact requested on {cmd.Target}.");
        router.Move += cmd =>
        {
            Log($"Move to {cmd.Destination}.");
            performerAgent.Motor?.SetDestination(cmd.Destination);
        };
        router.Cancel += () =>
        {
            Log("Cancel.");
            performerAgent.Motor?.Stop();
        };

        var hostGo = new GameObject("InteractionControllerHost");
        hostGo.transform.SetParent(transform);
        interactionHost = hostGo.AddComponent<InteractionControllerBehaviour>();
        interactionHost.Initialize(router, session.Party, session);
        interactionHost.Controller.RequestEnded += outcome => Log($"Interaction ended: {outcome}.");

        Log("InteractionLab ready. Click a fixture to interact; Escape or right-click cancels.");
    }

    private void Update()
    {
        if (mainCamera == null || router == null) return;

        if (Input.GetMouseButtonDown(0))
        {
            var ray = mainCamera.ScreenPointToRay(Input.mousePosition);
            router.HandlePointerDown(Input.mousePosition, ray);
        }

        if (Input.GetMouseButton(0)) router.HandlePointerMove(Input.mousePosition);

        if (Input.GetMouseButtonUp(0))
        {
            var ray = mainCamera.ScreenPointToRay(Input.mousePosition);
            router.HandlePointerUp(Input.mousePosition, ray, mainCamera, Input.GetKey(KeyCode.LeftShift));
        }

        if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape)) router.HandleCancel();
    }

    private void OnDestroy() => session?.Dispose();

    private void Log(string message)
    {
        Debug.Log($"[InteractionLab] {message}");
        logLines.Add(message);
        if (logLines.Count > 10) logLines.RemoveAt(0);
    }

    private void OnGUI()
    {
        GUILayout.BeginArea(new Rect(10, 10, 560, 240), GUI.skin.box);
        GUILayout.Label("Interaction Lab -- click a fixture to interact, Esc/right-click cancels", BoldLabelStyle());
        GUILayout.Space(6);
        foreach (var line in logLines) GUILayout.Label(line);
        GUILayout.EndArea();
    }

    private static GUIStyle BoldLabelStyle() => new(GUI.skin.label) { fontStyle = FontStyle.Bold };
}

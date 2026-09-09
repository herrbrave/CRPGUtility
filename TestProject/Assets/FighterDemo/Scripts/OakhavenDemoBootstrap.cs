using System.Linq;
using CRPG.Actors;
using CRPG.Camera;
using CRPG.Core;
using CRPG.Exploration;
using CRPG.Inventory;
using CRPG.Navigation;
using CRPG.Party;
using CRPG.Quests;
using CRPG.Rules;
using UnityEngine;
using UnityEngine.AI;

// Dogfood-only demo script (Oakhaven demo) -- builds the live game-state objects and wires them onto whatever
// the scene-builder (OakhavenContentAuthor.BuildOakhavenDemoScene) already placed in the scene: the player
// GameObject (tag "Player"), every DialogueTriggerZone, and the IsometricCameraRig. Mirrors CampDemoBootstrap/
// StoreDemoBootstrap's "construct everything in Start(), no singletons" shape.
public sealed class OakhavenDemoBootstrap : MonoBehaviour
{
    [SerializeField] private OakhavenContentBundle content;

    private IsometricCameraRig cameraRig;
    private Transform playerTransform;

    private void Start()
    {
        if (content == null)
        {
            Debug.LogError("[OakhavenDemoBootstrap] No OakhavenContentBundle assigned.");
            return;
        }

        var rulesDatabase = new RulesDatabase();
        rulesDatabase.RegisterRange(content.FighterContent.Definitions);
        rulesDatabase.RegisterRange(content.Definitions);

        var registry = new CharacterRegistry();
        var inventory = new PartyInventoryState();
        var timeline = new Timeline();
        var questLog = new QuestLog();
        var diceService = new DiceService(new SeededRandomSource(System.Environment.TickCount));
        var questService = new QuestService(questLog, timeline);

        var fighter = BuildFighter(rulesDatabase, content.FighterContent);
        fighter.GrantFeature(content.SoldierTrainingFeature.Id);
        registry.Register(fighter);
        EquipStarterGear(fighter, inventory, content.FighterContent);
        inventory.AddItem(new ItemInstance(RuntimeEntityId.NewId(), content.FighterContent.GoldPiece.Id, 50));

        var party = new PartyState(new[] { fighter.Id });

        var griffStore = new StoreInventoryState(buyPriceMultiplier: 1.5f, sellPriceMultiplier: 0.4f);
        griffStore.AddListing(content.FighterContent.HealingPotion.Id, quantity: 3);
        griffStore.AddListing(content.FighterContent.Longsword.Id);
        griffStore.AddListing(content.FighterContent.ChainShirt.Id);

        void RefreshQuests()
        {
            if (timeline.HasOccurred("quest-lost-heir-accepted")) questService.StartQuest(content.QuestLostHeir);
            if (timeline.HasOccurred("quest-goblin-menace-accepted")) questService.StartQuest(content.QuestGoblinMenace);
            questService.RefreshProgress(content.QuestLostHeir);
            questService.RefreshProgress(content.QuestGoblinMenace);
        }

        foreach (var trigger in FindObjectsByType<DialogueTriggerZone>(FindObjectsSortMode.None))
        {
            trigger.Party = party;
            trigger.Characters = registry;
            trigger.RulesDatabase = rulesDatabase;
            trigger.Timeline = timeline;
            trigger.Inventory = inventory;
            trigger.DiceService = diceService;
            trigger.Completed += RefreshQuests;

            if (trigger.name == "GriffTrigger") trigger.Store = griffStore;
        }

        var playerGo = GameObject.FindWithTag("Player");
        if (playerGo != null)
        {
            playerTransform = playerGo.transform;
            var motor = playerGo.GetComponent<NavMeshCharacterMotor>();
            if (motor == null) motor = playerGo.AddComponent<NavMeshCharacterMotor>();

            var commandSource = gameObject.GetComponent<MouseClickCommandSource>();
            if (commandSource == null) commandSource = gameObject.AddComponent<MouseClickCommandSource>();

            var movementService = new PartyMovementService(new NavMeshService());
            var explorationController = new PartyExplorationController(party, movementService, new ClusterFormationSolver(), commandSource);
            explorationController.RegisterMotor(fighter.Id, motor);
        }

        cameraRig = FindFirstObjectByType<IsometricCameraRig>();
        if (cameraRig != null && playerTransform != null) cameraRig.Rig.FocusOn(playerTransform.position);
    }

    // IsometricCameraRig has no built-in follow behavior (CRPGCameraSettings.FollowSelection is a
    // config flag nothing currently reads) -- the demo needs the camera to track the player itself.
    private void Update()
    {
        if (cameraRig != null && playerTransform != null) cameraRig.Rig.FocusOn(playerTransform.position);
    }

    private static CharacterState BuildFighter(IRulesDatabase rulesDatabase, FighterSampleContentBundle content)
    {
        var buildService = new CharacterBuildService(rulesDatabase);
        var plan = new CharacterBuildPlan
        {
            Race = content.Race,
            Class = content.CharacterClass,
            Subclass = content.Subclass,
            Background = content.Background,
            Level = 3,
        };

        plan.AbilityScores.SetScore(content.Strength.Id, 16);
        plan.AbilityScores.SetScore(content.Dexterity.Id, 12);
        plan.AbilityScores.SetScore(content.Constitution.Id, 14);
        plan.AbilityScores.SetScore(content.Intelligence.Id, 10);
        plan.AbilityScores.SetScore(content.Wisdom.Id, 12);
        plan.AbilityScores.SetScore(content.Charisma.Id, 8);

        buildService.RefreshPendingChoices(plan);
        while (plan.PendingChoices.Count > 0)
        {
            var choice = plan.PendingChoices[0];
            plan.ResolveChoice(choice.Id, choice.Options.Take(choice.Count).ToList());
            buildService.RefreshPendingChoices(plan);
        }

        var character = buildService.Build(plan, RuntimeEntityId.NewId());
        character.AddResource(new ResourceState(content.HitPoints.Id, max: 28));
        character.AddResource(new ResourceState(content.Action.Id, max: 1));
        character.AddResource(new ResourceState(content.BonusAction.Id, max: 1));
        character.AddResource(new ResourceState(content.ReactionResource.Id, max: 1));
        return character;
    }

    private static void EquipStarterGear(CharacterState character, PartyInventoryState inventory, FighterSampleContentBundle content)
    {
        var sword = new ItemInstance(RuntimeEntityId.NewId(), content.Longsword.Id);
        var armor = new ItemInstance(RuntimeEntityId.NewId(), content.ChainShirt.Id);
        inventory.AddItem(sword, ItemLocation.Equipped(character.Id, content.MainHandSlot.Id));
        inventory.AddItem(armor, ItemLocation.Equipped(character.Id, content.ArmorSlot.Id));
    }
}

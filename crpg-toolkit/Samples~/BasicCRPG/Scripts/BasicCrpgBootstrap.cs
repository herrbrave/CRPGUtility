using CRPG.Actors;
using CRPG.Core;
using CRPG.Exploration;
using CRPG.Gameplay;
using CRPG.Gameplay.Interactions;
using CRPG.Gameplay.Interactions.Adapters;
using CRPG.InputSystem;
using CRPG.Inventory;
using CRPG.Navigation;
using CRPG.Party;
using UnityEngine;

namespace CRPG.Samples.BasicCRPG
{
    /// <summary>
    /// The BasicCRPG sample's composition root: the smallest real wiring of a GameSession, a movable party, and
    /// one interactable prop -- click a party member to select it (the whole party starts selected by default),
    /// click the ground to move, click the treasure chest to loot it. No ruleset-specific content, no save/
    /// load, no combat -- CharacterCreator and TacticalEncounter cover those. Every dependency here is wired
    /// through the package's real public API, exactly as a consumer's own composition root would.
    /// </summary>
    public sealed class BasicCrpgBootstrap : MonoBehaviour
    {
        [SerializeField] private GameObject[] partyMemberPrefabs;
        [SerializeField] private Transform initialSpawnAnchor;
        [SerializeField] private ItemDefinition treasureItem;
        [SerializeField] private InputSystemCommandSource inputSource;
        [SerializeField] private LootInteractionAdapter treasureAdapter;

        private GameSessionHost host;

        /// <summary>Exposed (unlike the private-locals convention TestProject's own dogfood harnesses use) so
        /// a consumer extending this sample -- or an automated check verifying it -- can reach the real session
        /// this bootstrap wired, rather than needing to reconstruct it.</summary>
        public GameSession Session { get; private set; }

        private void Start()
        {
            var rulesDatabase = new RulesDatabase();
            rulesDatabase.Register(treasureItem);

            host = gameObject.AddComponent<GameSessionHost>();
            var session = host.StartSession(rulesDatabase, new DiceService(new SeededRandomSource(1)));
            Session = session;

            SpawnParty(session);

            treasureAdapter.Inventory = session.Inventory;
            treasureAdapter.Container.AddItem(new ItemInstance(RuntimeEntityId.NewId(), treasureItem.Id));

            var movementService = new PartyMovementService(new NavMeshService(), new SceneSelectableSource());
            var explorationController = new PartyExplorationController(session.Party, movementService, new WedgeFormationSolver(), inputSource);
            foreach (var memberId in session.Party.Members)
            {
                var agent = FindCharacterAgent(memberId);
                if (agent?.Motor != null) explorationController.RegisterMotor(memberId, agent.Motor);
            }

            var interactionHostGo = new GameObject("InteractionControllerHost");
            interactionHostGo.transform.SetParent(transform);
            var interactionHost = interactionHostGo.AddComponent<InteractionControllerBehaviour>();
            interactionHost.Initialize(inputSource, session.Party, session);

            // LootInteractionAdapter only ever touches data (session.Inventory) -- nothing about a successful
            // loot changes anything visible on its own. Without this, the chest looks and behaves identically
            // whether full or empty, which reads as "nothing happened" to anyone actually watching the demo
            // (confirmed live -- a person clicking the chest saw the character walk over and stop, with no
            // indication anything had occurred).
            interactionHost.Controller.RequestEnded += outcome =>
            {
                if (outcome != InteractionOutcome.Completed) return;
                var renderer = treasureAdapter.GetComponent<Renderer>();
                if (renderer != null) renderer.material.color = Color.gray;
                Debug.Log("[BasicCRPG] Treasure Coin looted!");
            };

            Debug.Log("[BasicCRPG] Ready -- click the ground to move, click the chest to loot it.");
        }

        private void SpawnParty(GameSession session)
        {
            for (var i = 0; i < partyMemberPrefabs.Length; i++)
            {
                var characterId = RuntimeEntityId.NewId();
                session.Characters.Register(new CharacterState(characterId));
                session.Party.AddMember(characterId);

                var offset = new Vector3(i * 1.5f, 0f, 0f);
                var position = initialSpawnAnchor != null ? initialSpawnAnchor.position + offset : offset;
                var instance = Instantiate(partyMemberPrefabs[i], position, Quaternion.identity);
                instance.GetComponent<CharacterAgent>().Bind(characterId, session);
            }
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
}

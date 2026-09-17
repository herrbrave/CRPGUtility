using CRPG.Actors;
using CRPG.Core;
using CRPG.Exploration;
using CRPG.Gameplay;
using CRPG.InputSystem;
using CRPG.Navigation;
using CRPG.Party;
using CRPG.Rules.SRD5;
using UnityEngine;

namespace CRPG.Samples.CharacterCreator
{
    /// <summary>
    /// The CharacterCreator sample's composition root: starts a real ruleset-aware session against the
    /// package's own shipped <see cref="SRD5RulesetProvider"/> asset (the same "real consumer" path Task 5.2's
    /// Fate ruleset uses), wires <see cref="CharacterCreatorView"/> to drive the actual
    /// <see cref="CharacterBuildPlan"/>/<see cref="CharacterBuildService"/> engine machinery, and -- once the
    /// player finishes creating a character -- is the SOLE owner of turning that plan into a real, playable
    /// character: build the CharacterState, register it, spawn the one shipped Hero prefab, bind it, and
    /// enable click-to-move. The View never touches the session/registration/spawn directly; this keeps
    /// "who builds/registers/spawns" to exactly one place, called at most once (<see cref="hasBuilt"/>).
    /// </summary>
    public sealed class CharacterCreatorBootstrap : MonoBehaviour
    {
        [SerializeField] private SRD5RulesetProvider rulesetProvider;
        [SerializeField] private GameObject heroPrefab;
        [SerializeField] private Transform initialSpawnAnchor;
        [SerializeField] private InputSystemCommandSource inputSource;
        [SerializeField] private CharacterCreatorView view;

        private GameSessionHost host;
        private PartyExplorationController explorationController;
        private bool hasBuilt;

        /// <summary>Exposed for the same reason BasicCrpgBootstrap exposes its own Session -- a consumer
        /// extending this sample, or an automated check, can reach the real session without reconstructing it.</summary>
        public GameSession Session { get; private set; }

        private void Start()
        {
            host = gameObject.AddComponent<GameSessionHost>();
            var session = host.StartSession(rulesetProvider, new DiceService(new SeededRandomSource(1)));
            Session = session;

            var buildService = new CharacterBuildService(session.RulesDatabase);
            view.Initialize(session.RulesDatabase, buildService, plan => OnCreateCharacter(plan, buildService));

            Debug.Log("[CharacterCreator] Ready -- create your character.");
        }

        private void OnCreateCharacter(CharacterBuildPlan plan, CharacterBuildService buildService)
        {
            if (hasBuilt) return; // The View already disables its own button after one click; this is the
            hasBuilt = true;      // authoritative guard against ever building/spawning a second character.

            var characterId = RuntimeEntityId.NewId();
            var character = buildService.Build(plan, characterId);

            Session.Characters.Register(character);
            Session.Party.AddMember(characterId);

            var position = initialSpawnAnchor != null ? initialSpawnAnchor.position : Vector3.zero;
            var instance = Instantiate(heroPrefab, position, Quaternion.identity);
            instance.GetComponent<CharacterAgent>().Bind(characterId, Session);

            EnableExploration();
            view.ShowCreatedCharacter(character, plan);

            Debug.Log($"[CharacterCreator] {plan.Race.DisplayName} {plan.Class.DisplayName} created and spawned.");
        }

        private void EnableExploration()
        {
            var movementService = new PartyMovementService(new NavMeshService(), new SceneSelectableSource());
            explorationController = new PartyExplorationController(Session.Party, movementService, new WedgeFormationSolver(), inputSource);
            foreach (var memberId in Session.Party.Members)
            {
                var agent = FindCharacterAgent(memberId);
                if (agent?.Motor != null) explorationController.RegisterMotor(memberId, agent.Motor);
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

        private void OnDestroy()
        {
            explorationController?.Dispose();
            host?.EndSession();
        }
    }
}

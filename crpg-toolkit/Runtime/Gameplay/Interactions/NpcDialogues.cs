using System;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Dialogue;
using CRPG.Gameplay.Interactions.Adapters;
using CRPG.Gameplay.Scenes;
using CRPG.Rules;
using UnityEngine;
using UnityEngine.UIElements;

namespace CRPG.Gameplay.Interactions
{
    /// <summary>What a game supplies so every talkable NPC can run a conversation. Only <see cref="PanelSettings"/> is required; everything else is for dialogues that use checks, rests, saves, recruiting or quests.</summary>
    public sealed class NpcDialogueOptions
    {
        /// <summary>The panel the conversation's UIDocument uses (required).</summary>
        public PanelSettings PanelSettings;

        /// <summary>How close the performer must come to start talking (metres, at least <see cref="WorldInteractable.MinCharacterRange"/>).</summary>
        public float InteractionRange = 1.8f;

        public DefinitionId CurrencyItemId;

        /// <summary>The conversation panel look: true (default) is a self-contained dark panel with light text; false keeps the default look that a game's own USS skin paints over (Barrow's End).</summary>
        public bool PlainStyle = true;

        /// <summary>The ruleset's skill-check policy for dialogues with a CheckNode (null = such a dialogue cannot roll). Called once per session wiring.</summary>
        public Func<GameSession, ISkillCheckRule> SkillCheckRuleFactory;

        /// <summary>The rest service for dialogues with a long-rest action (null = none).</summary>
        public Func<GameSession, RestService> RestServiceFactory;

        public Action RequestSave;
        public Action<string> RequestRecruit;

        /// <summary>Called by a dialogue's Recruit action with the spawn id of the NPC that is talking and the action's authored key. Wins over <see cref="RequestRecruit"/> (which only gets the key, for games that
        /// build a separate companion). Typically <c>NpcRecruitment.Queue(spawnId)</c>.</summary>
        public Action<string, string> RequestRecruitSpawn;

        /// <summary>Called by a dialogue's Start Quest action with its authored quest key. Wins over <see cref="Quests"/> when both are set.</summary>
        public Action<string> RequestStartQuest;

        /// <summary>The session's quest tracker: when set (and not overridden by <see cref="RequestStartQuest"/>), a dialogue's Start Quest action starts the quest through it.</summary>
        public QuestTracker Quests;
    }

    /// <summary>
    /// Makes spawned NPCs talkable. <see cref="AttachAll"/> (call it after a location's NPCs exist, after <c>LocationInitializer.Prepare</c> and after a load, like <c>NpcRoutines.AttachAll</c>) gives every NPC whose
    /// spawn point has a dialogue a <see cref="WorldInteractable"/> (its own transform is the anchor; a click on it is an Interact; the approach completes in range) and a <see cref="TalkInteractionAdapter"/>, and (re)wires the
    /// session dependencies of every such adapter. Wiring is repeated on every call, never skipped: a session swap that keeps the views would otherwise leave an adapter pointing at a disposed session. Adding the components
    /// happens once. While a conversation is open the NPC turns to the speaker.
    /// </summary>
    public static class NpcDialogues
    {
        /// <summary>The dialogue of a spawn point: its own reference, else the definition's <c>DialogueId</c> resolved through the ruleset (works only for a game that registers its dialogues).</summary>
        public static DialogueDefinition ResolveDialogue(NpcSpawnPoint point, GameSession session)
        {
            if (point == null) return null;
            if (point.Dialogue != null) return point.Dialogue;

            var id = point.Definition != null ? point.Definition.DialogueId : DefinitionId.None;
            return id.IsValid && session != null && session.RulesDatabase.TryResolve<DialogueDefinition>(id, out var found) ? found : null;
        }

        /// <returns>How many NPCs this call made talkable (components added); NPCs that already were are only re-wired.</returns>
        public static int AttachAll(ILocationContent content, WorldSpawnService spawner, GameSession session, NpcDialogueOptions options)
        {
            if (content == null || spawner == null || session == null) return 0;
            if (options == null || options.PanelSettings == null)
            {
                Debug.LogError("[NpcDialogues] a PanelSettings is required to show conversations; NPCs will not be talkable.");
                return 0;
            }

            var added = 0;
            foreach (var point in content.SpawnPoints)
            {
                if (point == null || string.IsNullOrEmpty(point.SpawnId)) continue;
                var dialogue = ResolveDialogue(point, session);
                if (dialogue == null) continue;
                if (!spawner.TryGetCharacterId(point.SpawnId, out var characterId, out _, out var defeated) || defeated) continue;

                CharacterAgent agent = null;
                foreach (var owned in spawner.OwnedAgents)
                {
                    if (owned != null && owned.EntityId == characterId) { agent = owned; break; }
                }

                if (agent == null) continue;
                if (session.Party.IsMember(characterId)) continue;   // recruited: it is a companion now, not a townsperson

                if (agent.TryGetComponent<TalkInteractionAdapter>(out var existing))
                {
                    existing.Dialogue = dialogue;
                    Wire(existing, session, options, point.SpawnId);
                    ApplyStore(existing, point, session, options);
                    continue;
                }

                if (agent.GetComponent<IInteractionAdapter>() != null)
                {
                    Debug.LogWarning($"[NpcDialogues] '{point.SpawnId}' already has an interaction adapter; a WorldInteractable supports exactly one, so it is not made talkable.", agent);
                    continue;
                }

                // Interactable first (the adapter requires it and registers against it), then the adapter, then configure: WorldInteractable resolves its adapter again on Configure.
                var interactable = agent.gameObject.AddComponent<WorldInteractable>();
                var adapter = agent.gameObject.AddComponent<TalkInteractionAdapter>();
                interactable.ConfigureForCharacter(agent.transform, options.InteractionRange);
                adapter.Dialogue = dialogue;
                Wire(adapter, session, options, point.SpawnId);
                ApplyStore(adapter, point, session, options);
                agent.gameObject.AddComponent<NpcConversationFacing>().Initialise(adapter);
                added++;
            }

            return added;
        }

        /// <summary>Binds the NPC's shop (created in the session if it is not there yet; an existing one keeps its depleted stock) to its talk adapter, or clears it when the spawn point has none.</summary>
        private static void ApplyStore(TalkInteractionAdapter adapter, NpcSpawnPoint point, GameSession session, NpcDialogueOptions options)
        {
            adapter.Store = NpcStores.Ensure(point, session);
            if (adapter.Store != null && !options.CurrencyItemId.IsValid)
                Debug.LogWarning($"[NpcDialogues] '{point.SpawnId}' has a shop but NpcDialogueOptions.CurrencyItemId is not set: every purchase would fail with \"not enough gold\".", adapter);
        }

        /// <summary>Pushes the session's services and the game's hooks into a talk adapter (the part Barrow's End hand-rolls in <c>WireTalkAdapters</c>).</summary>
        /// <param name="spawnId">Which NPC this adapter belongs to: it is passed to <see cref="NpcDialogueOptions.RequestRecruitSpawn"/> (a dialogue's Recruit action only authors a key).</param>
        public static void Wire(TalkInteractionAdapter adapter, GameSession session, NpcDialogueOptions options, string spawnId = null)
        {
            adapter.PanelSettings = options.PanelSettings;
            adapter.PlainStyle = options.PlainStyle;
            adapter.Party = session.Party;
            adapter.Characters = session.Characters;
            adapter.RulesDatabase = session.RulesDatabase;
            adapter.Timeline = session.Timeline;
            adapter.Inventory = session.Inventory;
            adapter.DiceService = session.DiceService;
            adapter.SkillCheckRule = options.SkillCheckRuleFactory?.Invoke(session);
            // A long rest works out of the box: the service is generic (characters, rules, event bus). What else a rest should do (revive a downed hero, cure a condition) is the game's: react to GameEvent.LongRest.
            adapter.RestService = options.RestServiceFactory?.Invoke(session) ?? new RestService(session.Characters, session.RulesDatabase, session.EventBus);
            adapter.CurrencyItemId = options.CurrencyItemId;
            adapter.RequestSave = options.RequestSave;
            adapter.RequestRecruit = options.RequestRecruitSpawn != null && !string.IsNullOrEmpty(spawnId) ? key => options.RequestRecruitSpawn(spawnId, key) : options.RequestRecruit;
            adapter.RequestStartQuest = options.RequestStartQuest ?? (options.Quests != null && ReferenceEquals(options.Quests.Session, session) ? key => options.Quests.Start(key) : null);
        }
    }

    /// <summary>Turns a talking NPC toward the party member it is talking to for the length of the conversation (<see cref="FacingPriority.Interaction"/>), and always lets go again: on completion, on cancellation and on teardown.</summary>
    public sealed class NpcConversationFacing : MonoBehaviour
    {
        private TalkInteractionAdapter adapter;
        private CharacterAgent agent;
        private bool claimed;

        internal void Initialise(TalkInteractionAdapter talk)
        {
            adapter = talk;
            agent = GetComponent<CharacterAgent>();
            adapter.ConversationStarted += OnStarted;
            adapter.ConversationEnded += OnEnded;
        }

        private void OnStarted(RuntimeEntityId performer)
        {
            if (agent == null || agent.Facing == null) return;

            foreach (var other in CharacterAgent.AllLive)
            {
                if (other == null || other.EntityId != performer) continue;
                agent.Facing.RequestFacing(FacingPriority.Interaction, other.transform.position - transform.position);
                claimed = true;
                return;
            }
        }

        private void OnEnded(RuntimeEntityId performer) => Release();

        private void Release()
        {
            if (!claimed) return;
            claimed = false;
            if (agent != null && agent.Facing != null) agent.Facing.ClearFacing(FacingPriority.Interaction);
        }

        private void OnDisable() => Release();

        private void OnDestroy()
        {
            Release();
            if (adapter == null) return;
            adapter.ConversationStarted -= OnStarted;
            adapter.ConversationEnded -= OnEnded;
        }
    }
}

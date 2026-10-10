using System;
using System.Collections.Generic;
using System.Linq;
using CRPG.Abilities;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Exploration;
using CRPG.Gameplay;
using CRPG.Gameplay.Interactions;
using CRPG.Inventory;
using CRPG.Navigation;
using CRPG.Navigation.Grid;
using CRPG.Party;
using CRPG.Rules.SRD5;
using UnityEngine;

namespace BarrowsEnd.Gate
{
    /// <summary>
    /// Everything in the gate scene that holds a reference to the CURRENT <see cref="GameSession"/>: the death-save service, the equipment
    /// service, ability resolution, the party exploration controller, the interaction controller and the CreatureDied -> MarkDefeated
    /// hook. A load builds a brand-new session (and destroys every old agent), so <see cref="Rebind"/> tears the previous set down and
    /// builds a fresh one against the new session -- one call at start-up and one after each successful load.
    /// <para>Deliberately the ONE <see cref="ISessionConsumer"/> for the gate (rather than one per service): the services are built and
    /// discarded together, so a single rebuild is simpler and cannot get out of step with itself.</para>
    /// <para>Engine gaps this works around, in glue rather than in the toolkit (recorded in the Task 2.7 plan): <c>HealEffect</c> only restored hit
    /// points and <c>CharacterState.RemoveCondition</c> publishes nothing, so <see cref="HealAndStand"/> publishes ConditionRemoved itself (HealEffect now publishes AfterHeal and the
    /// toolkit's DeathSaveService can revive on it with reviveOnHeal; this debug gate scene keeps its own full-heal path);
    /// <c>DeathSaveService</c> only advances on damage or an explicit roll (there is no turn loop here), hence <see cref="RollDeathSave"/>;
    /// nothing outside <c>TacticalEncounterController</c> calls <c>MarkDefeated</c>, so this subscribes CreatureDied to it.</para>
    /// </summary>
    public sealed class GateSessionServices : ISessionConsumer
    {
        public static readonly DefinitionId HitPoints = new("resource-hp");
        public static readonly DefinitionId Unconscious = new("condition-unconscious");

        private readonly IPlayerCommandSource commandSource;
        private readonly InteractionControllerBehaviour interactionHost;
        private readonly GridPathfindingService pathfinding;
        private readonly SpriteActionPresentationHook hook = new();

        private PartyExplorationController exploration;
        private IGameEventBus subscribedBus;
        private Action<GameEventContext> onDied;

        public GateSessionServices(IPlayerCommandSource commandSource, InteractionControllerBehaviour interactionHost, GridPathfindingService pathfinding)
        {
            this.commandSource = commandSource ?? throw new ArgumentNullException(nameof(commandSource));
            this.interactionHost = interactionHost;
            this.pathfinding = pathfinding ?? throw new ArgumentNullException(nameof(pathfinding));
        }

        public GameSession Session { get; private set; }
        public WorldSpawnService Spawner { get; private set; }
        public EquipmentService Equipment { get; private set; }
        public AbilityResolutionService Abilities { get; private set; }
        public DeathSaveService DeathSaves { get; private set; }
        public int RebindCount { get; private set; }

        /// <summary>The character the panel acts on: the party's active (selected) member.</summary>
        public RuntimeEntityId SelectedId => Session?.Party.ActiveCharacter ?? RuntimeEntityId.None;

        public void RebindSession(GameSession session, WorldSpawnService worldSpawnService) => Rebind(session, worldSpawnService);

        public void Rebind(GameSession session, WorldSpawnService spawner)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (spawner == null) throw new ArgumentNullException(nameof(spawner));

            Teardown();

            Session = session;
            Spawner = spawner;
            spawner.GridNavigation = pathfinding;
            RebindCount++;

            // Null faction provider and positions on purpose: the debug abilities are authored to work between anyone (see BarrowsEndGearAuthor).
            Abilities = new AbilityResolutionService(session.Characters, session.DiceService, eventBus: session.EventBus, rulesDatabase: session.RulesDatabase);
            // Task 6.1: only real party members use SRD5.1's death-save loop -- a hostile (a Giant Rat, etc.)
            // just dies outright at 0 HP, its real SRD5.1 behavior, found live once a real hostile-vs-party
            // encounter first existed in BarrowsEnd (DeathSaveService.cs's own doc comment has the full story).
            DeathSaves = new DeathSaveService(session.Characters, session.DiceService, session.EventBus, HitPoints, Unconscious,
                usesDeathSaves: id => session.Party.Members.Contains(id),
                canDie: id => PartyCanDie || !session.Party.Members.Contains(id));
            Equipment = new EquipmentService(session.Characters, session.RulesDatabase, session.Inventory, session.EventBus);

            subscribedBus = session.EventBus;
            onDied = ctx => spawner.MarkDefeated(ctx.Target);
            subscribedBus.Subscribe(GameEvent.CreatureDied, onDied);

            var movement = new PartyMovementService(pathfinding, new SceneSelectableSource());
            // Downed (dying) and dead members cannot be walked around. The party has no permadeath, so a fight's end stabilises the downed;
            // a member at 0 hit points here is either mid-fight (combat owns movement) or dead from before that rule (revived by a rest).
            exploration = new PartyExplorationController(session.Party, movement, new WedgeFormationSolver(), commandSource,
                canMove: id => CanMove(session, id));
            foreach (var member in session.Party.Members)
            {
                var agent = FindAgent(member);
                if (agent?.Motor != null) exploration.RegisterMotor(member, agent.Motor);
            }

            interactionHost?.Initialize(commandSource, session.Party, session);
        }

        /// <summary>The Phase 2 debug gate scene keeps SRD permadeath (its tests kill and remove members); the campaign shell turns this off:
        /// a downed member there stays downed until the fight ends, never dies for good.</summary>
        public bool PartyCanDie { get; set; } = true;

        private static bool CanMove(GameSession session, RuntimeEntityId id)
        {
            if (!session.Characters.TryGetCharacter(id, out var character)) return true;
            if (character.DeathSaves != null) return false;
            return !character.TryGetResource(HitPoints, out var hp) || hp.Current > 0;
        }

        private void Teardown()
        {
            exploration?.Dispose();
            exploration = null;
            if (subscribedBus != null && onDied != null) subscribedBus.Unsubscribe(GameEvent.CreatureDied, onDied);
            subscribedBus = null;
            onDied = null;
        }

        public void Dispose() => Teardown();

        /// <summary>Task 6.1: registers ONE member's motor with the live <see cref="PartyExplorationController"/>
        /// outside of <see cref="Rebind"/> -- needed because a mid-session recruit (<c>ShellBootstrap.
        /// RecruitCompanion</c>) never goes through a full session rebuild. Takes the motor directly (the
        /// caller's own just-created instance), not looked up via <see cref="FindAgent"/>: <c>CharacterAgent.
        /// AllLive</c> is a HashSet with deferred Destroy, so a stale/about-to-be-destroyed agent could still be
        /// findable for one frame if this looked it up instead (a real gap the Opus review caught in the v1
        /// design).</summary>
        public void RegisterMemberMotor(RuntimeEntityId id, IMovable motor) => exploration?.RegisterMotor(id, motor);

        public static CharacterAgent FindAgent(RuntimeEntityId id)
        {
            foreach (var agent in CharacterAgent.AllLive)
            {
                if (agent != null && agent.EntityId == id) return agent;
            }

            return null;
        }

        // ---------------------------------------------------------------------------------------------
        // Panel actions. Each returns a short human-readable line (or null on nothing to say).
        // ---------------------------------------------------------------------------------------------

        /// <summary>Plays the attack cue through the real <see cref="IActionPresentationHook"/> (the same call an ActionScheduler makes).</summary>
        public string PlayAttack(RuntimeEntityId id)
        {
            if (!Session.RulesDatabase.TryResolve<AbilityDefinition>(new DefinitionId("ability-debug-hit"), out var ability)) return "no attack ability";
            hook.RequestActionCue(id, ability);
            return "attack";
        }

        /// <summary>Runs a debug ability (see BarrowsEndGearAuthor) through the real resolution + damage + death-save path. The character is
        /// its own source: the abilities target Any, cost nothing and ignore range, so a debug panel needs no second combatant.</summary>
        public string Damage(RuntimeEntityId target, string abilityId)
        {
            if (!Session.RulesDatabase.TryResolve<AbilityDefinition>(new DefinitionId(abilityId), out var ability)) return $"unknown ability {abilityId}";
            var result = Abilities.Execute(target, ability, new[] { target });
            return result.Succeeded ? ability.DisplayName : $"{ability.DisplayName} rejected: {result.RejectionReason}";
        }

        public string RollDeathSave(RuntimeEntityId id)
        {
            if (!Session.Characters.TryGetCharacter(id, out var character)) return "no such character";
            if (character.DeathSaves == null) return "not dying";
            DeathSaves.RollDeathSave(id);
            return character.DeathSaves == null ? "death save resolved" : $"death saves {character.DeathSaves.Successes} ok / {character.DeathSaves.Failures} failed";
        }

        /// <summary>Full heal, clears death-save state and unconsciousness, and publishes ConditionRemoved (which the sprite cues listen to).</summary>
        public string HealAndStand(RuntimeEntityId id)
        {
            if (!Session.Characters.TryGetCharacter(id, out var character)) return "no such character";
            if (character.TryGetResource(HitPoints, out var hp)) hp.Current = hp.Max;
            character.DeathSaves = null;
            character.RemoveConditions(c => c.ConditionId == Unconscious);
            Session.EventBus.Publish(new GameEventContext(GameEvent.ConditionRemoved, id, id));
            return "healed";
        }

        /// <summary>Toggles an item on the character: unequips it if it is the one worn in its slot, else equips a fresh instance (which
        /// displaces whatever was in the slot).</summary>
        public string ToggleEquip(RuntimeEntityId character, string itemDefinitionId)
        {
            var definitionId = new DefinitionId(itemDefinitionId);
            if (!Session.RulesDatabase.TryResolve<ItemDefinition>(definitionId, out var definition)) return $"unknown item {itemDefinitionId}";

            var slot = definition.EquipmentSlotId;
            if (Session.Inventory.TryGetEquippedItem(character, slot, out var worn) && worn.DefinitionId == definitionId)
            {
                Equipment.Unequip(worn.InstanceId);
                return $"unequipped {definition.DisplayName}";
            }

            var instance = new ItemInstance(RuntimeEntityId.NewId(), definitionId);
            Session.Inventory.AddItem(instance);
            Equipment.Equip(instance.InstanceId, character, slot);
            return $"equipped {definition.DisplayName}";
        }

        public string ToggleDoor(UnityEngine.Component door)
        {
            var adapter = door != null ? door.GetComponent<CRPG.Gameplay.Interactions.Adapters.UseDoorInteractionAdapter>() : null;
            if (adapter == null) return "no door";
            adapter.BeginExecute(SelectedId, _ => { });
            return adapter.IsOpen ? "door opened" : "door closed";
        }
    }
}

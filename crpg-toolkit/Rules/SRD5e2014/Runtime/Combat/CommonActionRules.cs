using System;
using System.Collections.Generic;
using System.Linq;
using CRPG.Actors;
using CRPG.Combat;
using CRPG.Core;

namespace CRPG.Rules.SRD5
{
    /// <summary>Encounter-owned common-action rules. Queries are pure; only a real attack roll consumes Help.</summary>
    public sealed class CommonActionRules : IDisposable
    {
        private static readonly DefinitionId Dexterity = new("stat-dexterity");
        private static readonly DefinitionId Blinded = new("condition-blinded");
        private static readonly DefinitionId Invisible = new("condition-invisible");
        private readonly ICharacterRegistry characters;
        private readonly IRulesDatabase rules;
        private readonly IGameEventBus events;
        private readonly IPositionProvider positions;
        private readonly ILineOfSightProvider sight;
        private readonly IPerceptionQuery perception;
        private readonly IMovementSpeedRule speedRule;
        private readonly IReadOnlyList<RuntimeEntityId> combatants;
        private readonly ICombatLog log;
        private readonly Action<GameEventContext> onConditionApplied;
        private readonly Action<GameEventContext> onDropped;
        private readonly Action<GameEventContext> onDied;
        private readonly Action<GameEventContext> onTurnEnded;
        private readonly Action<GameEventContext> onCombatEnded;
        private bool disposed;

        public CommonActionRules(ICharacterRegistry characters, IRulesDatabase rules, IGameEventBus events,
            IPositionProvider positions, ILineOfSightProvider sight, IMovementSpeedRule speedRule,
            IReadOnlyList<RuntimeEntityId> combatants, ICombatLog log = null, IPerceptionQuery perception = null)
        {
            this.characters = characters;
            this.rules = rules;
            this.events = events;
            this.positions = positions;
            this.sight = sight;
            this.perception = perception;
            this.speedRule = speedRule;
            this.combatants = combatants;
            this.log = log;
            this.onConditionApplied = context => RemoveInvalidDodge(context.Target);
            this.onDropped = context => Remove(context.Target, c => c.ConditionId == CommonActions.DodgingId);
            this.onDied = context => RemoveSource(context.Target);
            this.onTurnEnded = context => ClearDash(context.Source);
            this.onCombatEnded = _ => Clear();
            events.Subscribe(GameEvent.ConditionApplied, this.onConditionApplied);
            events.Subscribe(GameEvent.CreatureDroppedToZeroHP, this.onDropped);
            events.Subscribe(GameEvent.CreatureDied, this.onDied);
            events.Subscribe(GameEvent.TurnEnded, this.onTurnEnded);
            events.Subscribe(GameEvent.CombatEnded, this.onCombatEnded);
        }

        public AdvantageState DodgeAgainst(RuntimeEntityId attacker, RuntimeEntityId target) =>
            IsDodging(target) && CanSee(target, attacker) ? AdvantageState.Disadvantage : AdvantageState.Normal;

        public AdvantageState DodgeSave(RuntimeEntityId character, DefinitionId stat) =>
            stat == Dexterity && IsDodging(character) ? AdvantageState.Advantage : AdvantageState.Normal;

        public AdvantageState HelpAdvantage(RuntimeEntityId attacker, RuntimeEntityId target) =>
            MatchingHelpOwners(attacker, target).Count > 0 ? AdvantageState.Advantage : AdvantageState.Normal;

        public void OnAttackRolled(RuntimeEntityId attacker, RuntimeEntityId target)
        {
            HashSet<EffectOwnerId> consumed = MatchingHelpOwners(attacker, target);
            if (consumed.Count == 0) return;
            foreach (RuntimeEntityId id in this.combatants)
                Remove(id, c => CommonActions.IsTurnCondition(c.ConditionId) && consumed.Contains(c.OwnerId));
        }

        /// <summary>Also catches direct speed changes; normal condition application invalidates Dodge synchronously.</summary>
        public void Tick()
        {
            foreach (RuntimeEntityId id in this.combatants) RemoveInvalidDodge(id);
        }

        private bool IsDodging(RuntimeEntityId id) =>
            !this.disposed && this.characters.TryGetCharacter(id, out CharacterState character)
            && character.HasCondition(CommonActions.DodgingId) && CanDodge(character);

        private bool CanDodge(CharacterState character)
        {
            if (character.DeathSaves != null || !character.TryGetResource(CommonActions.MovementId, out ResourceState movement)) return false;
            if (this.speedRule.ComputeEffectiveSpeed(character.Id, movement.Max) <= 0f) return false;
            foreach (ConditionInstance condition in character.Conditions)
            {
                if (this.rules.TryResolve<ConditionDefinition>(condition.ConditionId, out ConditionDefinition definition) && definition.PreventsActions) return false;
            }
            return true;
        }

        private bool CanSee(RuntimeEntityId observer, RuntimeEntityId target) =>
            this.perception != null ? this.perception.CanSee(observer, target) : this.characters.TryGetCharacter(observer, out CharacterState viewer) && !viewer.HasCondition(Blinded)
            && this.characters.TryGetCharacter(target, out CharacterState seen) && !seen.HasCondition(Invisible)
            && this.sight.HasLineOfSight(this.positions.GetPosition(observer), this.positions.GetPosition(target));

        private HashSet<EffectOwnerId> MatchingHelpOwners(RuntimeEntityId attacker, RuntimeEntityId target)
        {
            HashSet<EffectOwnerId> result = new();
            if (this.disposed || !this.characters.TryGetCharacter(attacker, out CharacterState ally)
                || !this.characters.TryGetCharacter(target, out CharacterState enemy)) return result;
            foreach (ConditionInstance help in ally.Conditions)
            {
                if (help.ConditionId != CommonActions.HelpedId || !help.OwnerId.IsValid) continue;
                if (enemy.Conditions.Any(c => c.ConditionId == CommonActions.DistractedId && c.OwnerId == help.OwnerId)) result.Add(help.OwnerId);
            }
            return result;
        }

        private void RemoveInvalidDodge(RuntimeEntityId id)
        {
            if (this.characters.TryGetCharacter(id, out CharacterState character) && character.HasCondition(CommonActions.DodgingId) && !CanDodge(character))
                Remove(id, c => c.ConditionId == CommonActions.DodgingId);
        }

        private void RemoveSource(RuntimeEntityId source)
        {
            foreach (RuntimeEntityId id in this.combatants)
                Remove(id, c => CommonActions.IsTurnCondition(c.ConditionId) && (c.Source == source || c.Target == source));
            ClearDash(source);
        }

        private void ClearDash(RuntimeEntityId id)
        {
            if (this.characters.TryGetCharacter(id, out CharacterState character) && character.TryGetResource(CommonActions.MovementId, out ResourceState movement))
                movement.ClearTemporaryMaximum();
        }

        private void Remove(RuntimeEntityId id, Predicate<ConditionInstance> predicate)
        {
            if (!this.characters.TryGetCharacter(id, out CharacterState character)) return;
            foreach (ConditionInstance removed in character.RemoveConditions(predicate))
            {
                this.log?.Log(new ConditionLogEntry(removed.Source, removed.Target, removed.ConditionId, applied: false));
                this.events.Publish(new GameEventContext(GameEvent.ConditionRemoved, removed.Source, removed.Target));
            }
        }

        private void Clear()
        {
            foreach (RuntimeEntityId id in this.combatants)
            {
                Remove(id, c => CommonActions.IsTurnCondition(c.ConditionId));
                ClearDash(id);
            }
        }

        public void Dispose()
        {
            if (this.disposed) return;
            this.disposed = true;
            this.events.Unsubscribe(GameEvent.ConditionApplied, this.onConditionApplied);
            this.events.Unsubscribe(GameEvent.CreatureDroppedToZeroHP, this.onDropped);
            this.events.Unsubscribe(GameEvent.CreatureDied, this.onDied);
            this.events.Unsubscribe(GameEvent.TurnEnded, this.onTurnEnded);
            this.events.Unsubscribe(GameEvent.CombatEnded, this.onCombatEnded);
            Clear();
        }
    }
}

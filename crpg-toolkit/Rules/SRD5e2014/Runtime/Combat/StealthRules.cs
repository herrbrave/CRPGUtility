using System;
using System.Collections.Generic;
using CRPG.Abilities;
using CRPG.Actors;
using CRPG.Core;

namespace CRPG.Rules.SRD5
{
    /// <summary>
    /// Hiding under the 2014 SRD, encounter-owned like <see cref="CommonActionRules"/>. A creature can hide from a hostile it is not seen by (heavily obscured, behind cover, outside its sight, or a blind observer); one Dexterity (Stealth) check is rolled and
    /// compared with each such observer's passive Perception (the hider wins ties). The total stands until the hider is found: it is kept on the hider with the set of observers who have since spotted it, so an observer who arrives later (reinforcements, a sweeping
    /// cone) is compared against the same total, and a later Search action can contest it. Leaving cover is the failure: <see cref="Tick"/> adds an observer that can plainly see the hider to that set, and a hider seen by every hostile is revealed.
    /// Attacking, casting something hostile or at another creature, and (house rule) being damaged reveal the hider to everyone. All state clears at the end of combat. The perception query asks <see cref="IsHiddenFrom"/>, so a hider reads as unseen to the observers it fooled
    /// and the unseen-attacker advantage and the AI's target filter follow without more wiring.
    /// </summary>
    public sealed class StealthRules : IStealthService, IDisposable
    {
        public const string StealthKey = "Skill:Stealth";
        public const string PerceptionKey = "Skill:Perception";
        public static readonly DefinitionId HiddenId = new("condition-hidden");
        private static readonly DefinitionId HitPoints = new("resource-hp");

        private sealed class State
        {
            public int Total;
            public readonly HashSet<RuntimeEntityId> RevealedTo = new();
        }

        private readonly ICharacterRegistry characters;
        private readonly IRulesDatabase rules;
        private readonly IGameEventBus events;
        private readonly Srd5PerceptionQuery perception;
        private readonly ISkillCheckRule skills;
        private readonly IDiceService dice;
        private readonly IFactionProvider factions;
        private readonly IReadOnlyList<RuntimeEntityId> combatants;
        private readonly ICombatLog log;
        private readonly Dictionary<RuntimeEntityId, State> hiders = new();
        private readonly Action<GameEventContext> onResolved, onDamaged, onDropped, onTurnStarted, onCombatEnded;
        private bool disposed;

        public StealthRules(ICharacterRegistry characters, IRulesDatabase rules, IGameEventBus events, Srd5PerceptionQuery perception, ISkillCheckRule skills, IDiceService dice,
            IFactionProvider factions, IReadOnlyList<RuntimeEntityId> combatants, ICombatLog log = null)
        {
            this.characters = characters;
            this.rules = rules;
            this.events = events;
            this.perception = perception;
            this.skills = skills;
            this.dice = dice;
            this.factions = factions;
            this.combatants = combatants;
            this.log = log;
            this.onResolved = OnAbilityResolved;
            this.onDamaged = context => Reveal(context.Target);
            this.onDropped = context => Reveal(context.Target);
            this.onTurnStarted = _ => Tick();
            this.onCombatEnded = _ => Clear();
            events.Subscribe(GameEvent.AbilityResolved, onResolved);
            events.Subscribe(GameEvent.AfterDamage, onDamaged);
            events.Subscribe(GameEvent.CreatureDroppedToZeroHP, onDropped);
            events.Subscribe(GameEvent.CreatureDied, onDropped);
            events.Subscribe(GameEvent.TurnStarted, onTurnStarted);
            events.Subscribe(GameEvent.CombatEnded, onCombatEnded);
            perception.Hidden = this;
        }

        public bool IsHidden(RuntimeEntityId hider) => !disposed && hiders.ContainsKey(hider);

        /// <summary>The Stealth total a hider is standing on, 0 when it is not hiding.</summary>
        public int TotalOf(RuntimeEntityId hider) => hiders.TryGetValue(hider, out var state) ? state.Total : 0;

        public bool CanAttemptHide(RuntimeEntityId hider, out string reason)
        {
            reason = null;
            var any = false;
            foreach (var observer in Observers(hider))
            {
                any = true;
                if (!Sees(perception.BaseSightOf(observer, hider))) return true;
            }

            reason = any ? "Everything nearby can see you clearly. Find cover or darkness, or get out of sight." : "There is no one to hide from.";
            return false;
        }

        public HideOutcome Hide(RuntimeEntityId hider)
        {
            var fooled = new List<RuntimeEntityId>();
            var seen = new List<RuntimeEntityId>();
            var bonus = skills.ComputeSkillCheckBonus(hider, StealthKey);
            var advantage = skills.ComputeAdvantage(hider, StealthKey);
            var roll = dice.Roll(new RollRequest(skills.CheckDie, RollPurpose.AbilityCheck, advantage, source: hider,
                modifiers: bonus != 0 ? new List<RollModifier> { new RollModifier(StealthKey, bonus) } : null));
            log?.Log(new RollLogEntry(roll));
            var total = (int)roll.Total;

            foreach (var observer in Observers(hider))
            {
                var sight = perception.BaseSightOf(observer, hider);
                if (Sees(sight)) { seen.Add(observer); continue; }
                if (Blind(observer) || total >= PassivePerception(observer)) fooled.Add(observer); else seen.Add(observer);
            }

            Reveal(hider);
            if (fooled.Count == 0) return new HideOutcome(total, fooled, seen);

            var state = new State { Total = total };
            foreach (var observer in seen) state.RevealedTo.Add(observer);
            hiders[hider] = state;
            if (characters.TryGetCharacter(hider, out var character))
            {
                character.ApplyCondition(new ConditionInstance(HiddenId, hider, hider));
                log?.Log(new ConditionLogEntry(hider, hider, HiddenId, applied: true));
                events.Publish(new GameEventContext(GameEvent.ConditionApplied, hider, hider));
            }

            return new HideOutcome(total, fooled, seen);
        }

        public bool IsHiddenFrom(RuntimeEntityId hider, RuntimeEntityId observer)
        {
            if (disposed || hider == observer || !hiders.TryGetValue(hider, out var state) || state.RevealedTo.Contains(observer)) return false;
            if (!Hostile(hider, observer)) return false;
            if (Sees(perception.BaseSightOf(observer, hider))) return false;   // out in the open now: Tick will add the observer to the set that has found the hider
            return Blind(observer) || state.Total >= PassivePerception(observer);
        }

        /// <summary>Rechecks every hider: an observer that can plainly see the hider has found it; a hider found by every hostile is revealed. Call it at turn starts (done here), after movement and when light or doors change.</summary>
        public void Tick()
        {
            if (disposed || hiders.Count == 0) return;
            foreach (var hider in new List<RuntimeEntityId>(hiders.Keys))
            {
                var state = hiders[hider];
                var allFound = true; var any = false;
                foreach (var observer in Observers(hider))
                {
                    any = true;
                    if (!state.RevealedTo.Contains(observer) && Sees(perception.BaseSightOf(observer, hider))) state.RevealedTo.Add(observer);
                    if (!state.RevealedTo.Contains(observer)) allFound = false;
                }

                if (!any || allFound) Reveal(hider);
            }
        }

        public void Reveal(RuntimeEntityId hider)
        {
            if (!hiders.Remove(hider)) return;
            if (!characters.TryGetCharacter(hider, out var character)) return;
            foreach (var removed in character.RemoveConditions(c => c.ConditionId == HiddenId))
            {
                log?.Log(new ConditionLogEntry(removed.Source, removed.Target, removed.ConditionId, applied: false));
                events.Publish(new GameEventContext(GameEvent.ConditionRemoved, removed.Source, removed.Target));
            }
        }

        /// <summary>Called when an attack roll has been made (hit or miss): the attacker has given its position away.</summary>
        public void OnAttackRolled(RuntimeEntityId attacker) => Reveal(attacker);

        private void OnAbilityResolved(GameEventContext context)
        {
            if (!hiders.ContainsKey(context.Source)) return;
            if (!context.Data.TryGetValue("ability", out var value) || value is not AbilityDefinition ability) return;
            var targets = context.Data.TryGetValue("targets", out var list) ? list as IReadOnlyList<RuntimeEntityId> : null;
            if (Reveals(ability, context.Source, targets)) Reveal(context.Source);
        }

        /// <summary>The allow-list the design settled on: movement and the common actions (Dash, Disengage, Dodge, Help, Hide itself) never give a hider away; an attack, a hostile ability, or a spell aimed at another creature does.</summary>
        private static bool Reveals(AbilityDefinition ability, RuntimeEntityId source, IReadOnlyList<RuntimeEntityId> targets)
        {
            if ((ability.Tags & (AbilityTag.Attack | AbilityTag.Hostile)) != 0) return true;
            if ((ability.Tags & AbilityTag.Spell) == 0 || targets == null) return false;
            foreach (var target in targets) if (target != source) return true;
            return false;
        }

        private IEnumerable<RuntimeEntityId> Observers(RuntimeEntityId hider)
        {
            foreach (var id in combatants)
            {
                if (id == hider || !Hostile(hider, id) || !Aware(id)) continue;
                yield return id;
            }
        }

        private bool Hostile(RuntimeEntityId a, RuntimeEntityId b) =>
            factions.TryGetFaction(a, out var fa) && factions.TryGetFaction(b, out var fb) && fa != fb;

        /// <summary>Conscious and able to notice: not down, not dead, not asleep or paralysed.</summary>
        private bool Aware(RuntimeEntityId id)
        {
            if (!characters.TryGetCharacter(id, out var character) || character.DeathSaves != null) return false;
            if (character.TryGetResource(HitPoints, out var hp) && hp.Current <= 0) return false;
            foreach (var condition in character.Conditions)
                if (rules.TryResolve<ConditionDefinition>(condition.ConditionId, out var definition) && definition.PreventsActions) return false;
            return true;
        }

        private bool Blind(RuntimeEntityId observer) => characters.TryGetCharacter(observer, out var character) && character.HasCondition(new DefinitionId("condition-blinded"));

        private int PassivePerception(RuntimeEntityId observer) => PassiveScore.From(skills.ComputeSkillCheckBonus(observer, PerceptionKey));

        private static bool Sees(Visibility sight) => Srd5PerceptionQuery.Sees(sight);

        private void Clear()
        {
            foreach (var hider in new List<RuntimeEntityId>(hiders.Keys)) Reveal(hider);
        }

        public void Dispose()
        {
            if (disposed) return;
            Clear();
            disposed = true;
            if (perception.Hidden == this) perception.Hidden = null;
            events.Unsubscribe(GameEvent.AbilityResolved, onResolved);
            events.Unsubscribe(GameEvent.AfterDamage, onDamaged);
            events.Unsubscribe(GameEvent.CreatureDroppedToZeroHP, onDropped);
            events.Unsubscribe(GameEvent.CreatureDied, onDropped);
            events.Unsubscribe(GameEvent.TurnStarted, onTurnStarted);
            events.Unsubscribe(GameEvent.CombatEnded, onCombatEnded);
        }
    }
}

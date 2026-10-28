using System;
using System.Collections.Generic;
using CRPG.Abilities;
using CRPG.Actors;
using CRPG.Core;

namespace CRPG.Rules.SRD5
{
    /// <summary>
    /// SRD 5e Sneak Attack as an <see cref="IAttackRiderRule"/>: once per turn, +1d6 per two levels (rounded up) on a weapon attack that hit, when the attack had advantage (flanking is advantage) or another enemy of the target stands next to it, but never with disadvantage. The SRD asks for a finesse or ranged
    /// weapon; that is <see cref="RequireFinesseOrRanged"/>, OFF by default (the forgiving version this game plays: any weapon attack can sneak, so a rogue who picks up a cutlass keeps the feature). A spell attack never qualifies, and only a character who HAS <see cref="FeatureId"/> gets it. "Once per turn" is the attacker's own turn: it comes back when their next turn starts (and when a fight begins or ends). Self-subscribes to the event bus, like the ruleset's other turn-driven services.
    /// </summary>
    public sealed class SneakAttackRule : IAttackRiderRule, IDisposable
    {
        public static readonly DefinitionId FeatureId = new("feature-sneak-attack");
        public const string Label = "Sneak Attack";

        private readonly ICharacterRegistry characters;
        private readonly IGameEventBus eventBus;
        private readonly HashSet<RuntimeEntityId> spentThisTurn = new();
        private readonly Action<GameEventContext> onTurnStarted, onCombatBoundary;

        public SneakAttackRule(ICharacterRegistry characters, IGameEventBus eventBus = null)
        {
            this.characters = characters;
            this.eventBus = eventBus;
            onTurnStarted = context => spentThisTurn.Remove(context.Source);
            onCombatBoundary = _ => spentThisTurn.Clear();
            if (eventBus == null) return;
            eventBus.Subscribe(GameEvent.TurnStarted, onTurnStarted);
            eventBus.Subscribe(GameEvent.CombatStarted, onCombatBoundary);
            eventBus.Subscribe(GameEvent.CombatEnded, onCombatBoundary);
        }

        /// <summary>The strict SRD condition: only a finesse or ranged weapon qualifies. Off (the default): any weapon attack does.</summary>
        public bool RequireFinesseOrRanged { get; set; }

        /// <summary>The game's answer to "is another enemy of the target next to it", set while a fight is on; null (the default) means only advantage qualifies.</summary>
        public IAllyAdjacencyQuery AllyAdjacency { get; set; }

        public bool TryGetRider(in AttackRiderQuery attack, out AttackRider rider)
        {
            if (!TryPreviewRider(attack, out rider)) return false;
            spentThisTurn.Add(attack.Attacker);
            return true;
        }

        /// <summary>Queries the same eligibility and unspent allowance as a hit, without consuming it.
        /// Use the live fight's rule so repeated previews respect the attacker's current turn.</summary>
        public bool TryPreviewRider(in AttackRiderQuery attack, out AttackRider rider)
        {
            rider = default;
            if (!characters.TryGetCharacter(attack.Attacker, out var attacker) || !HasFeature(attacker)) return false;
            if (attack.Ability == null || !IsEligibleWeapon(attack.Ability.Tags, RequireFinesseOrRanged)) return false;
            if (attack.Advantage == AdvantageState.Disadvantage) return false;
            var qualifies = attack.Advantage == AdvantageState.Advantage || (AllyAdjacency != null && AllyAdjacency.IsAllyAdjacentToTarget(attack.Attacker, attack.Target));
            if (!qualifies || spentThisTurn.Contains(attack.Attacker)) return false;

            rider = new AttackRider(new DiceExpression((attacker.Level + 1) / 2, 6, 0), Label);
            return true;
        }

        private static bool HasFeature(CharacterState character)
        {
            for (var i = 0; i < character.Features.Count; i++) if (character.Features[i] == FeatureId) return true;
            return false;
        }

        // a weapon attack (never a spell), and with the strict SRD rule a finesse or ranged one
        private static bool IsEligibleWeapon(AbilityTag tags, bool strict) =>
            (tags & AbilityTag.Weapon) != 0 && (tags & AbilityTag.Spell) == 0 && (!strict || (tags & (AbilityTag.Finesse | AbilityTag.Ranged)) != 0);

        public void Dispose()
        {
            if (eventBus == null) return;
            eventBus.Unsubscribe(GameEvent.TurnStarted, onTurnStarted);
            eventBus.Unsubscribe(GameEvent.CombatStarted, onCombatBoundary);
            eventBus.Unsubscribe(GameEvent.CombatEnded, onCombatBoundary);
        }
    }
}

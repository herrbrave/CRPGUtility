using System.Collections.Generic;
using System.Linq;
using CRPG.Actors;
using CRPG.Core;

namespace VikingRaid.Game
{
    public enum FightOutcome { Ongoing, Won, Lost }

    /// <summary>
    /// The one definition of "down" and the rules that follow from it (Task 4 design, review findings 2, 7 and 15). Heroes never die: at 0 HP they fall, roll death saves on their turn and,
    /// after three successes, become STABLE -- the toolkit then clears <c>DeathSaves</c> but leaves HP at 0 and the Unconscious condition on. So "down" can never be "DeathSaves != null":
    /// a stabilised hero would be treated as up, get a normal turn and be walked around the arena as a corpse. <b>Down means HP &lt;= 0, everywhere</b>: the turn skip, the ability bar,
    /// movement, and the win/defeat check. A hero is revived only by a heal that brings HP above 0 (the toolkit's DeathSaveService, built with reviveOnHeal, clears DeathSaves and Unconscious and publishes
    /// CreatureStabilized, which the model cues listen to to stand the character up) or by the full heal after a won fight.
    /// </summary>
    public static class PartyVitals
    {
        public static readonly DefinitionId Hp = new(GameDesign.Hp);
        public static readonly DefinitionId Unconscious = new("condition-unconscious");

        public static bool IsDown(CharacterState character) => character != null && character.TryGetResource(Hp, out var hp) && hp.Current <= 0;

        public static float HpFraction(CharacterState character) => character != null && character.TryGetResource(Hp, out var hp) && hp.Max > 0 ? (float)hp.Current / hp.Max : 0f;

        /// <summary>The between-fights rule: everyone is healed to full and revived, downed or not.</summary>
        public static void FullHealAndRevive(CharacterState character, IGameEventBus bus)
        {
            if (character == null) return;
            if (character.TryGetResource(Hp, out var hp)) hp.RestoreToFull();
            character.DeathSaves = null;
            var hadFlag = character.HasCondition(Unconscious);
            character.RemoveConditions(c => c.ConditionId == Unconscious);
            if (hadFlag) bus?.Publish(new GameEventContext(GameEvent.ConditionRemoved, default, character.Id));
        }

        /// <summary>Won when no hostile has hit points left; Lost when every hero is down (a downed party cannot be saved: only a heal revives, and nobody is left to cast one).</summary>
        public static FightOutcome Evaluate(IEnumerable<CharacterState> party, IEnumerable<CharacterState> hostiles)
        {
            if (!party.Any(p => !IsDown(p))) return FightOutcome.Lost;
            if (!hostiles.Any(h => !IsDown(h))) return FightOutcome.Won;
            return FightOutcome.Ongoing;
        }
    }
}

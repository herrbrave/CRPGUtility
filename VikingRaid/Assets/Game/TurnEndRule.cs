using System;
using System.Collections.Generic;
using System.Linq;
using CRPG.Actors;
using CRPG.Core;
using UnityEngine;

namespace VikingRaid.Game
{
    /// <summary>
    /// When a player's turn ends by itself (review finding 3). BarrowsEnd ended the turn the moment the Action was spent, which here would throw away a bonus action used AFTER attacking
    /// (Second Wind, Rallying Verse): the heroes' bonus abilities are real choices. So the turn auto-ends when the Action is spent AND the bonus action is also spent OR no bonus ability
    /// would do anything useful. Otherwise the player ends the turn with the toolkit's End Turn button (move only, bonus only, or nothing all need it).
    /// Useful means: Second Wind -- hit points below max and a use left; Rallying Verse -- a use left and an ally (a downed one included) below max within its 8 m range.
    /// Reckless Attack is never useful once the Action is spent (it only affects attacks).
    /// </summary>
    public static class TurnEndRule
    {
        public const float VerseRange = 8f;

        public static bool BonusIsUseful(CharacterState hero, IEnumerable<CharacterState> party, Func<RuntimeEntityId, Vector3> position)
        {
            if (hero == null || PartyVitals.IsDown(hero)) return false;
            if (!hero.TryGetResource(new DefinitionId(GameDesign.BonusAction), out var bonus) || bonus.Current <= 0) return false;

            if (hero.TryGetResource(new DefinitionId(GameDesign.SecondWindUses), out var secondWind) && secondWind.Current > 0
                && hero.TryGetResource(PartyVitals.Hp, out var hp) && hp.Current < hp.Max) return true;

            if (hero.TryGetResource(new DefinitionId(GameDesign.VerseUses), out var verse) && verse.Current > 0)
            {
                var here = position(hero.Id);
                foreach (var ally in party)
                {
                    if (ally == hero || !ally.TryGetResource(PartyVitals.Hp, out var allyHp) || allyHp.Current >= allyHp.Max) continue;
                    if (Vector3.Distance(here, position(ally.Id)) <= VerseRange) return true;
                }
            }

            return false;
        }

        public static bool ShouldAutoEnd(CharacterState hero, IEnumerable<CharacterState> party, Func<RuntimeEntityId, Vector3> position)
        {
            if (hero == null) return true;
            if (PartyVitals.IsDown(hero)) return true;
            var actionSpent = !hero.TryGetResource(new DefinitionId(GameDesign.Action), out var action) || action.Current <= 0;
            if (!actionSpent) return false;
            return !BonusIsUseful(hero, party, position);
        }
    }
}

using System.Collections.Generic;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Party;

namespace CRPG.Dialogue
{
    /// <summary>Resolves who rolls a CheckNode, and whether the resulting roll(s) succeed, per its CheckPerformer policy.</summary>
    public static class DialogueCheckResolver
    {
        /// <summary>Everyone who needs to roll for this performer policy. Every policy but EntireParty resolves
        /// to exactly one roller. Only BestPartyMember actually needs <paramref name="skillCheckRule"/> (to
        /// compare each member's bonus); it may be null for every other policy.</summary>
        public static IReadOnlyList<RuntimeEntityId> ResolveCheckers(
            CheckPerformer performer, RuntimeEntityId npcId, PartyState party, ICharacterRegistry characters,
            ISkillCheckRule skillCheckRule, string queryKey)
        {
            switch (performer)
            {
                case CheckPerformer.ActiveCharacter:
                    return new[] { party.ActiveCharacter };
                case CheckPerformer.SpecificSpeaker:
                    return new[] { npcId };
                case CheckPerformer.EntireParty:
                    return party.Members;
                case CheckPerformer.BestPartyMember:
                default:
                    return new[] { ResolveBestPartyMember(party, characters, skillCheckRule, queryKey) };
            }
        }

        /// <summary>Every policy but EntireParty succeeds/fails on its single roll; EntireParty succeeds if at
        /// least half the party's individual rolls meet the DC (5e's "group check" convention).</summary>
        public static bool Succeeds(CheckPerformer performer, IReadOnlyList<RollResult> rolls, int difficultyClass)
        {
            if (performer != CheckPerformer.EntireParty) return rolls[0].Total >= difficultyClass;

            var successes = 0;
            foreach (var roll in rolls)
                if (roll.Total >= difficultyClass) successes++;

            return successes * 2 >= rolls.Count;
        }

        private static RuntimeEntityId ResolveBestPartyMember(
            PartyState party, ICharacterRegistry characters, ISkillCheckRule skillCheckRule, string queryKey)
        {
            if (skillCheckRule == null)
                throw new System.InvalidOperationException("CheckPerformer.BestPartyMember requires a SkillCheckRule.");

            var best = RuntimeEntityId.None;
            var bestModifier = int.MinValue;

            foreach (var memberId in party.Members)
            {
                if (!characters.TryGetCharacter(memberId, out _)) continue;

                var value = skillCheckRule.ComputeSkillCheckBonus(memberId, queryKey);
                if (value > bestModifier)
                {
                    bestModifier = value;
                    best = memberId;
                }
            }

            return best;
        }
    }
}

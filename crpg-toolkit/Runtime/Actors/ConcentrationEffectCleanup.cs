using System.Collections.Generic;
using System.Linq;
using CRPG.Core;

namespace CRPG.Actors
{
    /// <summary>Task 4.4: the one real "break concentration" operation, shared by every trigger point so the
    /// sweep-owned-conditions logic exists exactly once. Lives in generic CRPG.Actors (not the SRD assembly) --
    /// it only touches ICharacterRegistry/CharacterState/events already available here, and "a channeled effect
    /// that owns conditions on its targets, breakable, cleans up after itself" isn't an SRD5-exclusive idea even
    /// though "concentration" as a named mechanic is. Callers: AbilityResolutionService.Execute (a new
    /// Concentration-tagged cast replacing an old one) and Rules.SRD5.ConcentrationService (a failed Constitution
    /// save, or the concentrating character dying).</summary>
    public static class ConcentrationEffectCleanup
    {
        /// <summary>Idempotent -- a character who isn't currently concentrating is a safe no-op (no events
        /// published at all), so callers never need to guard this themselves.</summary>
        public static void Break(
            RuntimeEntityId characterId, CharacterState character, ICharacterRegistry characters,
            IGameEventBus eventBus, ICombatLog combatLog = null)
        {
            if (!character.ConcentratingOnAbilityId.IsValid) return;

            var ownerId = character.ConcentrationEffectOwnerId;
            var targets = character.ConcentrationAffectedTargets.ToList();
            character.ClearConcentration();

            if (ownerId.IsValid)
            {
                foreach (var targetId in targets)
                {
                    if (!characters.TryGetCharacter(targetId, out var target)) continue;

                    foreach (var removed in target.RemoveConditionsWithOwner(ownerId))
                    {
                        combatLog?.Log(new ConditionLogEntry(removed.Source, removed.Target, removed.ConditionId, applied: false));
                        eventBus?.Publish(new GameEventContext(GameEvent.ConditionRemoved, removed.Source, removed.Target));
                    }
                }
            }

            eventBus?.Publish(new GameEventContext(GameEvent.ConcentrationBroken, characterId, characterId));
        }
    }
}

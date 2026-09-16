using CRPG.Combat;
using CRPG.Consumer.Fate;
using CRPG.Core;
using CRPG.Gameplay;

namespace CRPG.Consumer.Game
{
    /// <summary>
    /// The named, consumer-local translation <see cref="FateAttackEffect.OnTakenOut"/> calls into --
    /// deliberately never publishes <c>GameEvent.CreatureDied</c> (real Fate Condensed "Taken Out" is not
    /// death; the attacker narrates the actual fate). Does exactly what
    /// <c>TacticalEncounterController.OnCreatureDied</c> does internally for a real death, reused here because
    /// this game's combat doesn't go through that controller's own CreatureDied-only path at all.
    /// </summary>
    public sealed class FateConflictService : System.IDisposable
    {
        private readonly WorldSpawnService worldSpawnService;
        private readonly CombatEncounterService encounterService;

        public FateConflictService(WorldSpawnService worldSpawnService, CombatEncounterService encounterService)
        {
            this.worldSpawnService = worldSpawnService;
            this.encounterService = encounterService;
            FateAttackEffect.OnTakenOut = HandleTakenOut;
        }

        private void HandleTakenOut(RuntimeEntityId target)
        {
            worldSpawnService.MarkDefeated(target);
            encounterService.Encounter?.RemoveCombatant(target);
        }

        public void Dispose() => FateAttackEffect.OnTakenOut = null;
    }
}

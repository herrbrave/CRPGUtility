using CRPG.Combat;
using CRPG.Consumer.Fate;
using CRPG.Core;
using CRPG.Gameplay;
using CRPG.Gameplay.Combat;

namespace CRPG.Consumer.Game
{
    /// <summary>
    /// The named, consumer-local translation <see cref="FateAttackEffect.OnTakenOut"/> calls into --
    /// deliberately never publishes <c>GameEvent.CreatureDied</c> (real Fate Condensed "Taken Out" is not
    /// death; the attacker narrates the actual fate). Does exactly what a real death does to the encounter,
    /// through the toolkit's <c>TacticalEncounterController.Defeat</c> (the static overload: this game runs
    /// combat on a bare <c>CombatEncounterService</c> and never builds the controller).
    /// </summary>
    public sealed class FateConflictService : System.IDisposable
    {
        private WorldSpawnService worldSpawnService;
        private readonly CombatEncounterService encounterService;

        public FateConflictService(WorldSpawnService worldSpawnService, CombatEncounterService encounterService)
        {
            this.worldSpawnService = worldSpawnService;
            this.encounterService = encounterService;
            FateAttackEffect.OnTakenOut = HandleTakenOut;
        }

        /// <summary>A load replaces the spawn service; a Taken Out after it must mark the NEW one, not the discarded one this service was built with.</summary>
        public void Rebind(WorldSpawnService newWorldSpawnService) => worldSpawnService = newWorldSpawnService;

        private void HandleTakenOut(RuntimeEntityId target) => TacticalEncounterController.Defeat(worldSpawnService, encounterService, target);

        public void Dispose() => FateAttackEffect.OnTakenOut = null;
    }
}

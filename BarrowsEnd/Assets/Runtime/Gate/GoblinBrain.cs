using CRPG.AI;
using CRPG.Core;

namespace BarrowsEnd.Gate
{
    /// <summary>The Glimmering Caverns' goblin AI: close to melee range, then slash. Same runtime-built shape as
    /// <see cref="GiantRatBrain"/> (see <see cref="HostileBrainKit"/>).</summary>
    public static class GoblinBrain
    {
        public const string BehaviorSetId = "behaviorset-goblin";

        // Duplicated from BarrowsEndGearAuthor.GoblinSlashAbilityId (Editor-only, not reachable from Runtime).
        private const string GoblinSlashAbilityId = "ability-goblin-slash";

        public static BehaviorSetDefinition BuildAndRegister(IRulesDatabase rulesDatabase)
        {
            if (rulesDatabase.TryResolve<BehaviorSetDefinition>(new DefinitionId(BehaviorSetId), out var existing))
                return existing;

            return HostileBrainKit.BuildMeleeSet((RulesDatabase)rulesDatabase, BehaviorSetId, "Goblin", GoblinSlashAbilityId, stopDistance: 1.2f);
        }
    }
}

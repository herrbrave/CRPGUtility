using CRPG.AI;
using CRPG.Core;

namespace BarrowsEnd.Gate
{
    /// <summary>Maps a hostile NpcDefinition id to the runtime-built behavior set that drives it. One place to
    /// extend as new enemy types arrive.</summary>
    public static class HostileBrains
    {
        private static BehaviorSetDefinition Existing(IRulesDatabase rulesDatabase, string setId) =>
            rulesDatabase.TryResolve<BehaviorSetDefinition>(new DefinitionId(setId), out var set) ? set : null;

        public static BehaviorSetDefinition For(string npcDefinitionId, IRulesDatabase rulesDatabase)
        {
            switch (npcDefinitionId)
            {
                case "npc-giant-rat": return GiantRatBrain.BuildAndRegister(rulesDatabase);
                case "npc-goblin": return GoblinBrain.BuildAndRegister(rulesDatabase);
                case "npc-skeleton-thrall":
                    return Existing(rulesDatabase, "behaviorset-skeleton-thrall")
                           ?? HostileBrainKit.BuildMeleeSet((RulesDatabase)rulesDatabase, "behaviorset-skeleton-thrall", "Skeleton Thrall", "ability-thrall-claw", stopDistance: 1.2f);
                case "npc-necromancer":
                    return Existing(rulesDatabase, "behaviorset-necromancer")
                           ?? HostileBrainKit.BuildRangedSet((RulesDatabase)rulesDatabase, "behaviorset-necromancer", "Necromancer", "ability-bone-staff", "ability-necrotic-bolt", rangedRange: 6f, stopDistance: 5f);
                default: throw new System.ArgumentException($"No hostile brain is defined for '{npcDefinitionId}'.", nameof(npcDefinitionId));
            }
        }
    }
}

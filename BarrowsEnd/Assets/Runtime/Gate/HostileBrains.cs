using CRPG.AI;
using CRPG.Core;

namespace BarrowsEnd.Gate
{
    /// <summary>Maps a hostile NpcDefinition id to the runtime-built behavior set that drives it. One place to
    /// extend as new enemy types arrive.</summary>
    public static class HostileBrains
    {
        public static BehaviorSetDefinition For(string npcDefinitionId, IRulesDatabase rulesDatabase)
        {
            switch (npcDefinitionId)
            {
                case "npc-giant-rat": return GiantRatBrain.BuildAndRegister(rulesDatabase);
                case "npc-goblin": return GoblinBrain.BuildAndRegister(rulesDatabase);
                default: throw new System.ArgumentException($"No hostile brain is defined for '{npcDefinitionId}'.", nameof(npcDefinitionId));
            }
        }
    }
}

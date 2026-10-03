using CRPG.AI;
using CRPG.Core;

namespace BarrowsEnd.Gate
{
    /// <summary>The palisade-breach tutorial's hostile AI: close to melee range, then bite. Built at runtime via
    /// <see cref="HostileBrainKit"/> (no authored asset -- this content has no need to be hand-tunable) and
    /// registered so <see cref="UtilityAIDecisionProvider"/> can resolve the behavior ids by DefinitionId.</summary>
    public static class GiantRatBrain
    {
        public const string BehaviorSetId = "behaviorset-giant-rat";

        // Duplicated from BarrowsEndGearAuthor.RatBiteAbilityId (Editor-only, not reachable from Runtime) --
        // the project's usual Editor/Runtime id-duplication convention.
        private const string RatBiteAbilityId = "ability-rat-bite";

        /// <summary>Idempotent: returns the already-registered set on a second call in the same session.
        /// Downcasts to the concrete <see cref="RulesDatabase"/> its own <c>Register</c> needs -- the runtime type
        /// is always <c>RulesDatabase</c> (<c>RulesetSessionFactory.BuildAndValidate</c> is the only place a
        /// session's database is constructed).</summary>
        public static BehaviorSetDefinition BuildAndRegister(IRulesDatabase rulesDatabase)
        {
            if (rulesDatabase.TryResolve<BehaviorSetDefinition>(new DefinitionId(BehaviorSetId), out var existing))
                return existing;

            return HostileBrainKit.BuildMeleeSet((RulesDatabase)rulesDatabase, BehaviorSetId, "Giant Rat", RatBiteAbilityId, stopDistance: 1.2f);
        }
    }
}

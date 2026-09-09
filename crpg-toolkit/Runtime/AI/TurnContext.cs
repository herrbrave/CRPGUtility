using System.Collections.Generic;
using CRPG.Core;

namespace CRPG.AI
{
    /// <summary>
    /// What a Brain needs to decide one turn. PotentialEnemies/PotentialAllies are supplied by whoever's
    /// driving the encounter rather than resolved here -- CRPG.Combat has no Team/Faction concept today
    /// (Combatant is just an id + initiative), so "who's on which side" is knowledge the encounter's own
    /// setup already has and this deliberately doesn't duplicate.
    /// </summary>
    public sealed class TurnContext
    {
        public TurnContext(
            RuntimeEntityId self,
            IReadOnlyList<RuntimeEntityId> potentialEnemies,
            IReadOnlyList<RuntimeEntityId> potentialAllies,
            InfluenceMap influenceMap = null)
        {
            Self = self;
            PotentialEnemies = potentialEnemies;
            PotentialAllies = potentialAllies;
            InfluenceMap = influenceMap;
        }

        public RuntimeEntityId Self { get; }
        public IReadOnlyList<RuntimeEntityId> PotentialEnemies { get; }
        public IReadOnlyList<RuntimeEntityId> PotentialAllies { get; }

        /// <summary>Null if this encounter has no spatial hazards to reason about.</summary>
        public InfluenceMap InfluenceMap { get; }
    }
}

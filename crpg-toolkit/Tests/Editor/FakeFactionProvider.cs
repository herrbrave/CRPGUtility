using System.Collections.Generic;
using CRPG.Core;

namespace CRPG.Tests.EditMode
{
    /// <summary>Minimal IFactionProvider for tests -- no CombatEncounter/WorldSpawnService involved.</summary>
    internal sealed class FakeFactionProvider : IFactionProvider
    {
        private readonly Dictionary<RuntimeEntityId, DefinitionId> factions = new();

        public void Set(RuntimeEntityId entity, DefinitionId faction) => factions[entity] = faction;

        public bool TryGetFaction(RuntimeEntityId entity, out DefinitionId faction) => factions.TryGetValue(entity, out faction);
    }
}

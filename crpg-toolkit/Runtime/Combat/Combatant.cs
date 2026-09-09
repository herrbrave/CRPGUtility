using CRPG.Core;

namespace CRPG.Combat
{
    public sealed class Combatant
    {
        public Combatant(RuntimeEntityId id, int initiative, DefinitionId faction = default)
        {
            Id = id;
            Initiative = initiative;
            Faction = faction;
        }

        public RuntimeEntityId Id { get; }
        public int Initiative { get; }

        /// <summary>A bare content-authored tag ("faction-party", "faction-goblins", ...), same convention as
        /// Timeline's Key or FeatureModifier's QueryKey -- not an enum, since a real encounter can have more
        /// than two sides. Defaults to None, which <see cref="CombatEncounter.GetEnemiesOf"/>/GetAlliesOf treat
        /// like any other faction (everyone with the same untagged default counts as allied to each other).</summary>
        public DefinitionId Faction { get; }
    }
}

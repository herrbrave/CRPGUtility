using System.Collections.Generic;

namespace CRPG.Core
{
    /// <summary>
    /// An ability was used: logged once per execution, before any of its rolls or damage, naming the ability, the caster and every STRIKE it will make (one entry per creature, repeated for a spread ability's extra projectiles). A presentation reads this to start the cast effect and launch one projectile
    /// per strike, and the entries that follow (attack rolls, saves, damage, heals) to know what hit. <see cref="CombatLogEntry.Target"/> is the first strike's creature.
    /// </summary>
    public sealed class AbilityUsedLogEntry : CombatLogEntry
    {
        public AbilityUsedLogEntry(RuntimeEntityId source, string abilityId, IReadOnlyList<RuntimeEntityId> targets)
            : base(source, targets != null && targets.Count > 0 ? targets[0] : default)
        {
            AbilityId = abilityId ?? string.Empty;
            Targets = targets ?? System.Array.Empty<RuntimeEntityId>();
        }

        public string AbilityId { get; }
        public IReadOnlyList<RuntimeEntityId> Targets { get; }

        public override string Summary() => $"{Source} uses {AbilityId}.";
    }
}

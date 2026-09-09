using System;

namespace CRPG.Core
{
    /// <summary>
    /// Base for a structured, machine-readable log entry. Gameplay code should always log through
    /// <see cref="ICombatLog"/> with one of these rather than calling Debug.Log, so the UI can render
    /// (and let the player click into) individual rolls and modifiers instead of parsing strings.
    /// </summary>
    /// <remarks>
    /// <see cref="RollLogEntry"/>, <see cref="DamageLogEntry"/>, <see cref="ConditionLogEntry"/>,
    /// <see cref="DeathLogEntry"/> and <see cref="TurnLogEntry"/> cover what the Combat phase actually
    /// produces; a ResourceLogEntry can be added the same way once something needs to log resource spends
    /// distinctly from the effect that caused them.
    /// </remarks>
    public abstract class CombatLogEntry
    {
        protected CombatLogEntry(RuntimeEntityId source, RuntimeEntityId target)
        {
            Timestamp = DateTime.UtcNow;
            Source = source;
            Target = target;
        }

        public DateTime Timestamp { get; }
        public RuntimeEntityId Source { get; }
        public RuntimeEntityId Target { get; }

        public abstract string Summary();
    }
}

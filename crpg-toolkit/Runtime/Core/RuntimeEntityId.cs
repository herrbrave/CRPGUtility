using System;

namespace CRPG.Core
{
    /// <summary>
    /// Identifies a runtime entity (a spawned character, a loot container, ...), as opposed to
    /// <see cref="DefinitionId"/> which identifies authored content. Generated at spawn time, not persisted
    /// as authored data.
    /// </summary>
    /// <remarks>Named <c>RuntimeEntityId</c> rather than the shorter <c>EntityId</c> because Unity 6.6 added
    /// its own <c>UnityEngine.EntityId</c>, which would otherwise collide (ambiguous reference) in every file
    /// that needs both a UnityEngine type and this one -- an extremely common combination in this codebase.</remarks>
    [Serializable]
    public readonly struct RuntimeEntityId : IEquatable<RuntimeEntityId>
    {
        private readonly string value;

        private RuntimeEntityId(string value)
        {
            this.value = value;
        }

        public static readonly RuntimeEntityId None = new RuntimeEntityId(null);

        public static RuntimeEntityId NewId() => new RuntimeEntityId(Guid.NewGuid().ToString("N"));

        /// <summary>Reconstructs an id previously produced by <see cref="ToString"/> -- for save/load, where an
        /// entity's identity must survive the round trip rather than being freshly generated.</summary>
        public static RuntimeEntityId FromString(string value) => new RuntimeEntityId(string.IsNullOrEmpty(value) ? null : value);

        public bool IsValid => !string.IsNullOrEmpty(value);

        public bool Equals(RuntimeEntityId other) => string.Equals(value, other.value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is RuntimeEntityId other && Equals(other);
        public override int GetHashCode() => value?.GetHashCode() ?? 0;
        public override string ToString() => value ?? string.Empty;

        public static bool operator ==(RuntimeEntityId a, RuntimeEntityId b) => a.Equals(b);
        public static bool operator !=(RuntimeEntityId a, RuntimeEntityId b) => !a.Equals(b);
    }
}

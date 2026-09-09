using System;
using UnityEngine;

namespace CRPG.Core
{
    /// <summary>
    /// A stable identifier for an authored <see cref="IDefinition"/> asset. Saves, mods and DLC reference
    /// content by this value, never by a direct ScriptableObject reference, so assets can be renamed,
    /// moved or reimported without breaking save compatibility.
    /// </summary>
    [Serializable]
    public struct DefinitionId : IEquatable<DefinitionId>
    {
        [SerializeField] private string value;

        public DefinitionId(string value)
        {
            this.value = value;
        }

        public static readonly DefinitionId None = new DefinitionId(null);

        public readonly string Value => value;
        public readonly bool IsValid => !string.IsNullOrEmpty(value);

        public readonly bool Equals(DefinitionId other) => string.Equals(value, other.value, StringComparison.Ordinal);
        public override readonly bool Equals(object obj) => obj is DefinitionId other && Equals(other);
        public override readonly int GetHashCode() => value?.GetHashCode() ?? 0;
        public override readonly string ToString() => value ?? string.Empty;

        public static bool operator ==(DefinitionId a, DefinitionId b) => a.Equals(b);
        public static bool operator !=(DefinitionId a, DefinitionId b) => !a.Equals(b);
    }
}

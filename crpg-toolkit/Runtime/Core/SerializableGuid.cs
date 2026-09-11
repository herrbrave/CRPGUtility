using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace CRPG.Core
{
    /// <summary>
    /// A Unity-serializable GUID stored as two longs rather than a string -- comparing two longs-pairs is a
    /// fixed-cost struct comparison, not a length-dependent character-by-character string compare, which
    /// matters for any id compared as often as a dialogue node link is walked during a conversation (and, in
    /// the future, anywhere else in the toolkit currently using a string-backed id, e.g. RuntimeEntityId).
    /// System.Guid itself isn't used directly as the field type because Unity's serializer can't write into it
    /// (no accessible layout it understands) -- this wraps one instead, converting at the edges.
    /// <para>Not a `readonly struct` and these are real (non-readonly) fields, for the same reason
    /// DefinitionId/DiceExpression aren't either -- Unity's serializer needs to write into them directly.</para>
    /// </summary>
    [Serializable]
    public struct SerializableGuid : IEquatable<SerializableGuid>
    {
        [SerializeField] private long low;
        [SerializeField] private long high;

        private SerializableGuid(long low, long high)
        {
            this.low = low;
            this.high = high;
        }

        public static readonly SerializableGuid Empty = default;

        /// <summary>A fresh, random id -- the normal way to create one. Call this once, at authoring time, when
        /// something new is created (e.g. a graph editor adding a node); never in a type's own constructor,
        /// since Unity's deserializer also runs that constructor when loading an *existing* object from disk,
        /// which would silently reassign a new id to already-authored content on every load.</summary>
        public static SerializableGuid NewGuid() => FromGuid(Guid.NewGuid());

        /// <summary>Deterministic: the same seed always produces the same id. Not for normal authoring (that's
        /// NewGuid) -- for deterministic test fixtures (so a test can still write a readable "line-1" and get a
        /// stable id back) and one-time migrations (converting old free-form string ids to real GUIDs while
        /// preserving which nodes pointed at which).</summary>
        public static SerializableGuid FromString(string seed)
        {
            if (string.IsNullOrEmpty(seed)) return Empty;

            using var md5 = MD5.Create();
            var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(seed));
            return FromGuid(new Guid(hash));
        }

        /// <summary>Wraps an existing System.Guid as-is (no hashing) -- for Editor tooling that needs to read
        /// back the exact value currently stored in a SerializedProperty's raw long fields, not generate a new
        /// or seeded one. Prefer NewGuid()/FromString() for actually creating an id.</summary>
        public static SerializableGuid FromGuid(Guid guid)
        {
            var bytes = guid.ToByteArray();
            return new SerializableGuid(BitConverter.ToInt64(bytes, 0), BitConverter.ToInt64(bytes, 8));
        }

        public readonly bool IsValid => low != 0 || high != 0;

        public readonly Guid ToGuid()
        {
            var bytes = new byte[16];
            BitConverter.GetBytes(low).CopyTo(bytes, 0);
            BitConverter.GetBytes(high).CopyTo(bytes, 8);
            return new Guid(bytes);
        }

        public readonly bool Equals(SerializableGuid other) => low == other.low && high == other.high;
        public override readonly bool Equals(object obj) => obj is SerializableGuid other && Equals(other);
        public override readonly int GetHashCode() => HashCode.Combine(low, high);
        public override readonly string ToString() => IsValid ? ToGuid().ToString("N") : string.Empty;

        public static bool operator ==(SerializableGuid a, SerializableGuid b) => a.Equals(b);
        public static bool operator !=(SerializableGuid a, SerializableGuid b) => !a.Equals(b);
    }
}

using System;

namespace CRPG.Core
{
    /// <summary>
    /// Identifies one movement request so a caller (or the mover itself) can tell whether a later status
    /// check is still about the request it issued, versus one that's since been superseded by a newer
    /// destination. Deliberately NOT globally unique the way RuntimeEntityId is -- this only ever needs to
    /// answer "is this still the current request," never survive a save, so a simple process-lifetime counter
    /// is enough. "Retargeting a move cancels only the replaced request; stale completion cannot stop a newer
    /// order" (Docs/Design/Production-Foundation.md sec.4.3) is exactly the property this id exists to prove.
    /// </summary>
    public readonly struct MoveRequestId : IEquatable<MoveRequestId>
    {
        private readonly long value;

        private MoveRequestId(long value) => this.value = value;

        public static readonly MoveRequestId None = default;

        private static long counter;

        public static MoveRequestId NewId() => new(++counter);

        public bool IsValid => value != 0;

        public bool Equals(MoveRequestId other) => value == other.value;
        public override bool Equals(object obj) => obj is MoveRequestId other && Equals(other);
        public override int GetHashCode() => value.GetHashCode();
        public override string ToString() => value == 0 ? "MoveRequestId.None" : $"MoveRequestId({value})";

        public static bool operator ==(MoveRequestId a, MoveRequestId b) => a.Equals(b);
        public static bool operator !=(MoveRequestId a, MoveRequestId b) => !a.Equals(b);
    }
}

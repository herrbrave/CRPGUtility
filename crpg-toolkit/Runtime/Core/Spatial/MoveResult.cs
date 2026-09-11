namespace CRPG.Core
{
    /// <summary>The immediate outcome of issuing one movement request -- returned synchronously by
    /// CRPG.Navigation.IMovable.SetDestination so a "the destination is unreachable/the mover is off-mesh or
    /// disabled" rejection is a structured value a click handler can branch on, never an exception (Docs/Design/
    /// Production-Foundation.md sec.4.3: "structured command failures, not exceptions leaking to a player
    /// click handler"). A freshly-accepted request reads Pending with a fresh RequestId; a rejected one reads
    /// Invalid with RequestId.None and leaves whatever was previously in flight untouched.</summary>
    public readonly struct MoveResult
    {
        public MoveStatus Status { get; }
        public MoveRequestId RequestId { get; }

        public MoveResult(MoveStatus status, MoveRequestId requestId)
        {
            Status = status;
            RequestId = requestId;
        }

        public static MoveResult Invalid() => new(MoveStatus.Invalid, MoveRequestId.None);
    }
}

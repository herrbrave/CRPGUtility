namespace CRPG.Core
{
    /// <summary>
    /// Outcome of one movement request, exactly the vocabulary Docs/Design/Production-Foundation.md sec.4.3
    /// specifies: "Movement results distinguish Pending, Moving, Arrived, Blocked, Invalid, Cancelled. Partial
    /// paths do not count as successful arrival at the requested destination." A terminal status (Arrived/
    /// Blocked/Invalid/Cancelled) persists until a new request replaces it -- there is no separate "idle"
    /// value; "nothing requested yet" also reads as Arrived (the character is exactly where it should be).
    /// </summary>
    public enum MoveStatus
    {
        /// <summary>A path is being computed; not yet known whether it succeeds.</summary>
        Pending,

        /// <summary>Actively moving toward the destination along a complete path.</summary>
        Moving,

        /// <summary>Reached the requested destination via a complete path.</summary>
        Arrived,

        /// <summary>Stopped at the closest reachable point along a partial path -- the requested destination
        /// itself was not reached. Never reported as Arrived.</summary>
        Blocked,

        /// <summary>The request was rejected outright (off-mesh/disabled mover, unreachable target) --
        /// whatever request was previously in flight, if any, is unaffected.</summary>
        Invalid,

        /// <summary>The request was explicitly cancelled (e.g. CRPG.Navigation.IMovable.Stop) before reaching
        /// a natural Arrived/Blocked outcome.</summary>
        Cancelled,
    }
}

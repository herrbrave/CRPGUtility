namespace CRPG.Core
{
    /// <summary>
    /// Hiding, as the Hide ability sees it. Lives in Core so <c>AbilityExecutionContext</c> can carry it (the ability assembly cannot reference the ruleset that implements it); null on a context whose game has no stealth.
    /// <see cref="CanAttemptHide"/> says whether hiding is possible at all right now and, if not, why in words a player can read.
    /// </summary>
    public interface IStealthService : IHiddenState
    {
        bool CanAttemptHide(RuntimeEntityId hider, out string reason);
        HideOutcome Hide(RuntimeEntityId hider);
        void Reveal(RuntimeEntityId hider);
    }
}

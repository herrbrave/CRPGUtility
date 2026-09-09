namespace CRPG.Actors
{
    /// <summary>When a resource restores to full via RestService. "Both" covers cases like Second Wind/Action
    /// Surge -- "regain this after a short or long rest."</summary>
    public enum RestTrigger
    {
        None,
        ShortRest,
        LongRest,
        Both
    }
}

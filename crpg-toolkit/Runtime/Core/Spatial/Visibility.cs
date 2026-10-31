namespace CRPG.Core
{
    /// <summary>How one creature sees another. <see cref="Unseen"/> is geometry or condition (blocked, outside the cone, blind observer, invisible target); the two obscured steps come from light, as the SRD's lightly and heavily obscured areas.</summary>
    public enum Visibility
    {
        Clear,
        LightlyObscured,
        HeavilyObscured,
        Unseen,
    }
}

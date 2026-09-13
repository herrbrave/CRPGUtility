namespace CRPG.UI
{
    /// <summary>Optional localization adapter a game implementer can wire in. Nothing in this package implements
    /// it -- views resolve through <see cref="TextKeyResolver"/>, which falls back to <see cref="TextKey.Fallback"/>
    /// when no localizer is present or the key isn't recognized.</summary>
    public interface ITextLocalizer
    {
        bool TryResolve(TextKey key, out string resolved);
    }
}

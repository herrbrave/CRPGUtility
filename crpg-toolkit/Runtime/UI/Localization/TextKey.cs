namespace CRPG.UI
{
    /// <summary>A UI-facing piece of text that carries both a stable lookup key (for an optional
    /// <see cref="ITextLocalizer"/>) and a fallback string to show when no localizer is wired up, or the
    /// localizer doesn't recognize the key. Presenters build these instead of resolving a display string
    /// directly, so a game implementer can add localization later without touching presenter logic.</summary>
    public readonly struct TextKey
    {
        public TextKey(string key, string fallback)
        {
            Key = key;
            Fallback = fallback;
        }

        public string Key { get; }
        public string Fallback { get; }

        public static TextKey Literal(string fallback) => new(null, fallback);
    }
}

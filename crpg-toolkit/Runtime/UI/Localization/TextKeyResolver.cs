namespace CRPG.UI
{
    /// <summary>Single place views resolve a <see cref="TextKey"/> through -- wiring a real
    /// <see cref="ITextLocalizer"/> in later only touches this call, never every view.</summary>
    public static class TextKeyResolver
    {
        public static string Resolve(TextKey key, ITextLocalizer localizer)
        {
            if (localizer != null && key.Key != null && localizer.TryResolve(key, out var resolved)) return resolved;
            return key.Fallback;
        }
    }
}

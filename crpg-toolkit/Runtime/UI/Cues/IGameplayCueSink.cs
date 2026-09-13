namespace CRPG.UI
{
    /// <summary>Optional audio/VFX dispatch seam. Presenters take one and call <see cref="Raise"/> at real
    /// command call sites; nothing in this package implements it.</summary>
    public interface IGameplayCueSink
    {
        void Raise(GameplayCue cue);
    }
}

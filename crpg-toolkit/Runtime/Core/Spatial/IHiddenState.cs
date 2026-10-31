namespace CRPG.Core
{
    /// <summary>Whether a creature is hidden from a particular observer. The perception query asks it so a hidden creature reads as unseen; the ruleset's stealth rules answer it.</summary>
    public interface IHiddenState
    {
        bool IsHiddenFrom(RuntimeEntityId hider, RuntimeEntityId observer);
    }
}

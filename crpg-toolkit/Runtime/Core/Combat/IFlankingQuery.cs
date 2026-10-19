namespace CRPG.Core
{
    /// <summary>Whether an attacker is FLANKING its target (the optional 5e rule: an ally of the attacker on the other side of the target). An attack rule that supports flanking asks this when it works out advantage; the answer
    /// needs the live battlefield (who stands where, who is an ally), which a ruleset cannot know when it registers its policies, so the game supplies it per fight through <see cref="IFlankingConfigurable"/>.</summary>
    public interface IFlankingQuery
    {
        bool IsFlanking(RuntimeEntityId attacker, RuntimeEntityId target);
    }

    /// <summary>Implemented by an attack rule that can take its flanking source from the game (set it when a fight starts, null it when the fight ends).</summary>
    public interface IFlankingConfigurable
    {
        IFlankingQuery Flanking { get; set; }
    }
}

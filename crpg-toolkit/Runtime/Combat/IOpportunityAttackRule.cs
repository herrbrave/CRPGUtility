using CRPG.Core;

namespace CRPG.Combat
{
    /// <summary>The ruleset owns eligibility and attacks; movement owns the crossing and interruption point.</summary>
    public interface IOpportunityAttackRule
    {
        bool CanProvoke(RuntimeEntityId mover);
        bool CanReact(RuntimeEntityId reactor, RuntimeEntityId mover);
        float ReachOf(RuntimeEntityId reactor);
        bool Resolve(RuntimeEntityId reactor, RuntimeEntityId mover);
    }
}

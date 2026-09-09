using CRPG.Core;

namespace CRPG.Combat
{
    /// <summary>A moment where a reactor could interrupt with a reaction ability -- an actor leaving a
    /// threatened area, taking damage, being targeted, ... whatever the trigger source decides warrants one.</summary>
    public sealed class ReactionOpportunity
    {
        public ReactionOpportunity(GameEvent trigger, RuntimeEntityId reactor, RuntimeEntityId instigator)
        {
            Trigger = trigger;
            Reactor = reactor;
            Instigator = instigator;
        }

        public GameEvent Trigger { get; }
        public RuntimeEntityId Reactor { get; }
        public RuntimeEntityId Instigator { get; }
    }
}

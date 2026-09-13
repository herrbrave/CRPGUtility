using CRPG.Combat;
using CRPG.Gameplay.Interactions;
using CRPG.Gameplay.Scenes;
using CRPG.Saving;

namespace CRPG.Gameplay
{
    /// <summary>
    /// combat/interactions/loadCoordinator are all optional -- a scene without combat/interactions wired up, or
    /// with no load coordinator at all, is still safe to save. InteractionController.CurrentRequest already
    /// spans the whole dialogue/store/loot/door/teleport lifecycle (TalkInteractionAdapter holds its request
    /// open for the entire Executing phase, for as long as its ConversationController is running) -- confirmed
    /// directly, not assumed, so no separate "is a conversation open" check is needed here.
    /// <para>
    /// Task 3.6: transitionGate is the same shared IWorldTransitionGate SceneTravelCoordinator and
    /// SessionLoadCoordinator both check before starting their own transaction -- those two constructors
    /// require it (that mutual cross-guard is the property that actually matters: travel can't start during a
    /// load or vice versa). SafePointGate's own reference stays optional, like its other three -- a scene that
    /// never wires scene travel at all is still safe to save without one; the real safety invariant doesn't
    /// depend on SafePointGate specifically knowing about it, only on the two coordinators sharing one gate.
    /// </para>
    /// </summary>
    public sealed class SafePointGate
    {
        private readonly CombatEncounterService combat;
        private readonly InteractionController interactions;
        private readonly SessionLoadCoordinator loadCoordinator;
        private readonly IWorldTransitionGate transitionGate;

        public SafePointGate(CombatEncounterService combat = null, InteractionController interactions = null, SessionLoadCoordinator loadCoordinator = null, IWorldTransitionGate transitionGate = null)
        {
            this.combat = combat;
            this.interactions = interactions;
            this.loadCoordinator = loadCoordinator;
            this.transitionGate = transitionGate;
        }

        public SafePointBlockReason CheckSafeToSave()
        {
            if (transitionGate is { IsBusy: true }) return SafePointBlockReason.SceneTransitionInProgress;
            if (loadCoordinator != null && loadCoordinator.IsLoading) return SafePointBlockReason.LoadInProgress;
            if (combat?.Encounter != null) return SafePointBlockReason.CombatActive;
            if (interactions?.CurrentRequest != null) return SafePointBlockReason.InteractionInProgress;
            return SafePointBlockReason.None;
        }
    }
}

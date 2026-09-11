using CRPG.Core;

namespace CRPG.Actors
{
    /// <summary>
    /// The single funnel for "play this animation" regardless of who's asking -- player input and an AI
    /// Behavior both call <see cref="RequestAnimation"/> the same way, mirroring how a player's and an AI's
    /// combat actions already both resolve through one TurnDecision-consuming service rather than two parallel
    /// code paths. Deliberately dumb: records the cue on <see cref="CharacterState"/> and publishes it, with no
    /// priority/interruption/queueing logic -- the latest request always wins. Not combat-specific (lives here
    /// in CRPG.Actors, not CRPG.Combat), since animation cues matter outside combat turns too (dialogue
    /// gestures, exploration).
    /// </summary>
    public sealed class AnimationOrderService
    {
        private readonly ICharacterRegistry characters;
        private readonly IGameEventBus eventBus;

        public AnimationOrderService(ICharacterRegistry characters, IGameEventBus eventBus = null)
        {
            this.characters = characters;
            this.eventBus = eventBus;
        }

        public void RequestAnimation(RuntimeEntityId character, DefinitionId cueId)
        {
            if (!characters.TryGetCharacter(character, out var characterState)) return;

            characterState.CurrentAnimationCueId = cueId;

            var context = new GameEventContext(GameEvent.AnimationCueChanged, character, character);
            context.Data["CueId"] = cueId;
            eventBus?.Publish(context);
        }
    }
}

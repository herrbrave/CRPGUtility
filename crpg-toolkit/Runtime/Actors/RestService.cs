using System.Collections.Generic;
using CRPG.Core;

namespace CRPG.Actors
{
    /// <summary>
    /// Restores every resource on each given party member whose <see cref="RestTrigger"/> matches the rest
    /// type, then publishes one ShortRest/LongRest event for the whole party. Rests are party-scoped, not
    /// per-character (design conversation, 2026-09-07): one action, everyone rests together. Short Rest is
    /// meant to be callable from anywhere with no gating; Long Rest is meant to be called only from behind a
    /// Camp screen (CRPG.UI) -- this service doesn't enforce that distinction itself, since "where the button
    /// lives" is a presentation concern, not a rule.
    /// </summary>
    public sealed class RestService
    {
        private readonly ICharacterRegistry characters;
        private readonly IRulesDatabase rulesDatabase;
        private readonly IGameEventBus eventBus;

        public RestService(ICharacterRegistry characters, IRulesDatabase rulesDatabase, IGameEventBus eventBus = null)
        {
            this.characters = characters;
            this.rulesDatabase = rulesDatabase;
            this.eventBus = eventBus;
        }

        public void Rest(IEnumerable<RuntimeEntityId> party, GameEvent restType)
        {
            foreach (var memberId in party)
            {
                if (!characters.TryGetCharacter(memberId, out var character)) continue;

                foreach (var resource in rulesDatabase.All<ResourceDefinition>())
                {
                    if (!Matches(resource.RestTrigger, restType)) continue;
                    if (character.TryGetResource(resource.Id, out var state)) state.RestoreToFull();
                }
            }

            eventBus?.Publish(new GameEventContext(restType));
        }

        private static bool Matches(RestTrigger trigger, GameEvent restType) => trigger switch
        {
            RestTrigger.Both => restType == GameEvent.ShortRest || restType == GameEvent.LongRest,
            RestTrigger.ShortRest => restType == GameEvent.ShortRest,
            RestTrigger.LongRest => restType == GameEvent.LongRest,
            _ => false,
        };
    }
}

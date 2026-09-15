using System.Collections.Generic;
using CRPG.Abilities;
using CRPG.Actors;
using CRPG.Core;

namespace CRPG.UI
{
    /// <summary>
    /// Task 4.5: pure data layer for a tactical ability bar -- lists a character's known/prepared/
    /// feature-granted abilities (excluding Reaction-tagged ones, which are never player-chosen from a bar;
    /// they resolve automatically through <c>ReactionService</c>) with each one's target-independent
    /// readiness via <see cref="IAbilityResolutionService.CanAttempt"/> -- never a second, separately
    /// maintained "can afford" check.
    /// </summary>
    public sealed class AbilityBarPresenter
    {
        private ICharacterRegistry characters;
        private IRulesDatabase rulesDatabase;
        private IAbilityResolutionService abilityResolution;

        public AbilityBarPresenter(ICharacterRegistry characters, IRulesDatabase rulesDatabase, IAbilityResolutionService abilityResolution)
        {
            this.characters = characters;
            this.rulesDatabase = rulesDatabase;
            this.abilityResolution = abilityResolution;
        }

        /// <summary>True once a character has actually failed onto death saves (SRD5's "unconscious and
        /// dying") -- a UI should show a "Roll Death Save" control instead of the ordinary ability bar.</summary>
        public bool IsIncapacitated(RuntimeEntityId character) =>
            characters.TryGetCharacter(character, out var state) && state.DeathSaves != null;

        public IReadOnlyList<AbilityBarEntrySummary> GetAbilities(RuntimeEntityId character)
        {
            var entries = new List<AbilityBarEntrySummary>();
            if (!characters.TryGetCharacter(character, out var state)) return entries;

            var abilityIds = new HashSet<DefinitionId>();
            foreach (var id in state.KnownAbilities) abilityIds.Add(id);
            foreach (var id in state.PreparedAbilities) abilityIds.Add(id);
            foreach (var featureId in state.Features)
            {
                if (rulesDatabase.TryResolve<FeatureDefinition>(featureId, out var feature))
                {
                    foreach (var grantedId in feature.GrantedAbilityIds) abilityIds.Add(grantedId);
                }
            }

            foreach (var abilityId in abilityIds)
            {
                if (!rulesDatabase.TryResolve<AbilityDefinition>(abilityId, out var ability)) continue;
                if ((ability.Tags & AbilityTag.Reaction) != 0) continue;

                var result = abilityResolution.CanAttempt(character, ability);
                entries.Add(new AbilityBarEntrySummary(ability, result.IsValid, result.IsValid ? null : result.Message ?? result.Reason.ToString()));
            }

            return entries;
        }

        /// <summary>Called by the composition root if the underlying session's collaborators are ever
        /// replaced (a new encounter/session) -- mirrors PartyPresenter/QuestLogPresenter's own Rebind
        /// precedent.</summary>
        public void Rebind(ICharacterRegistry characters, IRulesDatabase rulesDatabase, IAbilityResolutionService abilityResolution)
        {
            this.characters = characters;
            this.rulesDatabase = rulesDatabase;
            this.abilityResolution = abilityResolution;
        }
    }
}

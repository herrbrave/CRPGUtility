using System;
using System.Collections.Generic;
using System.Linq;
using CRPG.Core;

namespace CRPG.Actors
{
    /// <summary>
    /// An in-progress character build. Internally this isn't a linear wizard (spec §9): setting Race/Class/
    /// Background and calling <see cref="CharacterBuildService.RefreshPendingChoices"/> can surface new
    /// <see cref="PendingChoices"/> (a chosen subrace can itself introduce a spell choice, etc.), which keep
    /// getting resolved and refreshed until <see cref="IsComplete"/>.
    /// </summary>
    public sealed class CharacterBuildPlan
    {
        private readonly List<ChoiceRequirement> pendingChoices = new();
        private readonly List<ResolvedChoice> resolvedChoices = new();

        public RaceDefinition Race { get; set; }
        public ClassDefinition Class { get; set; }
        public SubclassDefinition Subclass { get; set; }
        public BackgroundDefinition Background { get; set; }
        public int Level { get; set; } = 1;
        public StatBlock AbilityScores { get; } = new();

        private readonly Dictionary<DefinitionId, DefinitionId> appearance = new();

        /// <summary>2D campaign: the chosen look (layer slot -> option id), copied onto the built character.</summary>
        public IReadOnlyDictionary<DefinitionId, DefinitionId> Appearance => appearance;

        /// <summary>An invalid slot or option is ignored; use <see cref="ClearAppearance"/> to remove an entry.</summary>
        public void SetAppearance(DefinitionId slot, DefinitionId option)
        {
            if (!slot.IsValid || !option.IsValid) return;
            appearance[slot] = option;
        }

        public void ClearAppearance(DefinitionId slot) => appearance.Remove(slot);

        public IReadOnlyList<ChoiceRequirement> PendingChoices => pendingChoices;
        public IReadOnlyList<ResolvedChoice> ResolvedChoices => resolvedChoices;
        public bool IsComplete => pendingChoices.Count == 0;

        public void AddPendingChoice(ChoiceRequirement requirement)
        {
            if (resolvedChoices.Any(r => r.Requirement.Id == requirement.Id)) return;
            if (pendingChoices.Any(c => c.Id == requirement.Id)) return;
            pendingChoices.Add(requirement);
        }

        public void ResolveChoice(string requirementId, IReadOnlyList<DefinitionId> selections)
        {
            var requirement = pendingChoices.FirstOrDefault(c => c.Id == requirementId);
            if (requirement == null)
                throw new InvalidOperationException($"No pending choice '{requirementId}'.");
            if (selections.Count != requirement.Count)
                throw new InvalidOperationException($"Choice '{requirementId}' requires exactly {requirement.Count} selection(s).");

            pendingChoices.Remove(requirement);
            resolvedChoices.Add(new ResolvedChoice(requirement, selections));
        }
    }
}

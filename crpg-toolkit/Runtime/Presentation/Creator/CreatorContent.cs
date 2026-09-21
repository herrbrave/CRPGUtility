using System;
using System.Collections.Generic;
using System.Linq;
using CRPG.Core;

namespace CRPG.Presentation.Creator
{
    /// <summary>One entry a creator row cycles through: an id (a sprite catalog option id, a class id, an item id) and the name shown.</summary>
    public readonly struct CreatorOption
    {
        public CreatorOption(DefinitionId id, string name)
        {
            Id = id;
            Name = name ?? "";
        }

        public DefinitionId Id { get; }
        public string Name { get; }
    }

    /// <summary>One appearance row of the creator (e.g. Hair): the layer slot it drives, its title, and the options it cycles.</summary>
    public sealed class CreatorAppearanceRowSpec
    {
        public CreatorAppearanceRowSpec(DefinitionId layerSlot, string title, IEnumerable<CreatorOption> options)
        {
            LayerSlot = layerSlot;
            Title = title ?? "";
            Options = (options ?? Enumerable.Empty<CreatorOption>()).ToArray();
        }

        public DefinitionId LayerSlot { get; }
        public string Title { get; }
        public IReadOnlyList<CreatorOption> Options { get; }
    }

    /// <summary>The auto-resolved answer to one level-1 choice requirement (matched by the requirement's string id).</summary>
    public sealed class CreatorChoiceSpec
    {
        public CreatorChoiceSpec(string requirementId, IEnumerable<DefinitionId> options)
        {
            RequirementId = requirementId ?? "";
            Options = (options ?? Enumerable.Empty<DefinitionId>()).ToArray();
        }

        public string RequirementId { get; }
        public IReadOnlyList<DefinitionId> Options { get; }
    }

    /// <summary>What the creator knows about one selectable class beyond the ruleset: its starter weapons (option Id = item id), the
    /// ability-score priority order, and the level-1 choice defaults. Consumer data, not rules-derived.</summary>
    public sealed class CreatorClassSpec
    {
        public CreatorClassSpec(DefinitionId classId, IEnumerable<CreatorOption> weapons, IEnumerable<DefinitionId> scoreOrder, IEnumerable<CreatorChoiceSpec> choiceDefaults)
        {
            ClassId = classId;
            Weapons = (weapons ?? Enumerable.Empty<CreatorOption>()).ToArray();
            ScoreOrder = (scoreOrder ?? Enumerable.Empty<DefinitionId>()).ToArray();
            ChoiceDefaults = (choiceDefaults ?? Enumerable.Empty<CreatorChoiceSpec>()).ToArray();
        }

        public DefinitionId ClassId { get; }
        public IReadOnlyList<CreatorOption> Weapons { get; }
        public IReadOnlyList<DefinitionId> ScoreOrder { get; }
        public IReadOnlyList<CreatorChoiceSpec> ChoiceDefaults { get; }
    }

    /// <summary>Everything a <see cref="CharacterCreatorModel"/> needs that is content rather than rules: the fixed race/background/level,
    /// the standard array, the appearance rows and the selectable classes. Plain data; a consumer builds it from its own files.</summary>
    public sealed class CreatorContent
    {
        public CreatorContent(DefinitionId race, DefinitionId background, int level, IEnumerable<int> standardArray,
            IEnumerable<CreatorAppearanceRowSpec> appearanceRows, IEnumerable<CreatorClassSpec> classes)
        {
            Race = race;
            Background = background;
            Level = level;
            StandardArray = (standardArray ?? Enumerable.Empty<int>()).ToArray();
            AppearanceRows = (appearanceRows ?? Enumerable.Empty<CreatorAppearanceRowSpec>()).ToArray();
            Classes = (classes ?? Enumerable.Empty<CreatorClassSpec>()).ToArray();
        }

        public DefinitionId Race { get; }
        public DefinitionId Background { get; }
        public int Level { get; }
        public IReadOnlyList<int> StandardArray { get; }
        public IReadOnlyList<CreatorAppearanceRowSpec> AppearanceRows { get; }
        public IReadOnlyList<CreatorClassSpec> Classes { get; }
    }
}

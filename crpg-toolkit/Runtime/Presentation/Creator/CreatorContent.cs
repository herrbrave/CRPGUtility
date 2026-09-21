using System;
using System.Collections.Generic;
using System.Linq;
using CRPG.Core;
using UnityEngine;

namespace CRPG.Presentation.Creator
{
    /// <summary>One entry a creator row cycles through: an id (a sprite catalog option id, a class id, a filter id), the name shown, and optionally a
    /// swatch colour a view can draw beside the name (hair colour).</summary>
    public readonly struct CreatorOption
    {
        public CreatorOption(DefinitionId id, string name)
        {
            Id = id;
            Name = name ?? "";
            Swatch = default;
            HasSwatch = false;
        }

        public CreatorOption(DefinitionId id, string name, Color32 swatch)
        {
            Id = id;
            Name = name ?? "";
            Swatch = swatch;
            HasSwatch = true;
        }

        public DefinitionId Id { get; }
        public string Name { get; }
        public Color32 Swatch { get; }
        public bool HasSwatch { get; }
    }

    /// <summary>One appearance row of the creator (e.g. Skin): the layer slot it drives, its title, and the options it cycles.</summary>
    public class CreatorAppearanceRowSpec
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

    /// <summary>One value of a filter row (a hair colour): its id and name, a swatch, and the layer options that colour offers, in style order.</summary>
    public sealed class CreatorFilter
    {
        public CreatorFilter(string id, string name, Color32 swatch, IEnumerable<DefinitionId> options)
        {
            Id = id ?? "";
            Name = name ?? "";
            Swatch = swatch;
            Options = (options ?? Enumerable.Empty<DefinitionId>()).ToArray();
        }

        public string Id { get; }
        public string Name { get; }
        public Color32 Swatch { get; }
        public IReadOnlyList<DefinitionId> Options { get; }
    }

    /// <summary>
    /// An appearance slot chosen in two steps: a FILTER row (Hair Colour, each value with a swatch) decides which options the STYLE row (Hair Style) shows.
    /// Every filter must list the same number of options in the same style order (the model reports and skips a filter that does not), so changing the
    /// filter keeps the style index: the same shape in a new colour. Contributes TWO rows to the creator, filter first, and one entry to the appearance map
    /// (the chosen style option). Derives from <see cref="CreatorAppearanceRowSpec"/> so a single ordered list still defines row order; the base
    /// <c>Options</c> are the first filter's, named by <see cref="StyleNames"/>.
    /// </summary>
    public sealed class CreatorFilteredAppearanceRowSpec : CreatorAppearanceRowSpec
    {
        public CreatorFilteredAppearanceRowSpec(DefinitionId layerSlot, string filterKey, string filterTitle, IEnumerable<CreatorFilter> filters, string styleTitle, IEnumerable<string> styleNames)
            : base(layerSlot, styleTitle, StyleOptions(filters, styleNames, 0))
        {
            FilterKey = filterKey ?? "";
            FilterTitle = filterTitle ?? "";
            Filters = (filters ?? Enumerable.Empty<CreatorFilter>()).ToArray();
            StyleNames = (styleNames ?? Enumerable.Empty<string>()).ToArray();
        }

        /// <summary>The filter row's key (the style row's key is the layer slot id).</summary>
        public string FilterKey { get; }
        public string FilterTitle { get; }
        public IReadOnlyList<CreatorFilter> Filters { get; }
        public IReadOnlyList<string> StyleNames { get; }

        /// <summary>The style options for filter <paramref name="filterIndex"/>: that filter's option ids, named by the shared style names.</summary>
        public IReadOnlyList<CreatorOption> StyleOptionsFor(int filterIndex) => StyleOptions(Filters, StyleNames, filterIndex);

        private static CreatorOption[] StyleOptions(IEnumerable<CreatorFilter> filters, IEnumerable<string> styleNames, int filterIndex)
        {
            var list = (filters ?? Enumerable.Empty<CreatorFilter>()).ToArray();
            if (filterIndex < 0 || filterIndex >= list.Length) return new CreatorOption[0];
            var names = (styleNames ?? Enumerable.Empty<string>()).ToArray();
            var options = list[filterIndex].Options;
            var result = new CreatorOption[options.Count];
            for (var i = 0; i < result.Length; i++) result[i] = new CreatorOption(options[i], i < names.Length ? names[i] : $"Style {i + 1}");
            return result;
        }
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

    /// <summary>What the creator knows about one selectable class beyond the ruleset: its STARTING KIT (option Id = item id, Name = the name shown; the
    /// armor that is the class's outfit, then the weapon), the ability-score priority order, and the level-1 choice defaults. Consumer data, not rules-derived.</summary>
    public sealed class CreatorClassSpec
    {
        public CreatorClassSpec(DefinitionId classId, IEnumerable<CreatorOption> startingItems, IEnumerable<DefinitionId> scoreOrder, IEnumerable<CreatorChoiceSpec> choiceDefaults)
        {
            ClassId = classId;
            StartingItems = (startingItems ?? Enumerable.Empty<CreatorOption>()).ToArray();
            ScoreOrder = (scoreOrder ?? Enumerable.Empty<DefinitionId>()).ToArray();
            ChoiceDefaults = (choiceDefaults ?? Enumerable.Empty<CreatorChoiceSpec>()).ToArray();
        }

        public DefinitionId ClassId { get; }
        public IReadOnlyList<CreatorOption> StartingItems { get; }
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

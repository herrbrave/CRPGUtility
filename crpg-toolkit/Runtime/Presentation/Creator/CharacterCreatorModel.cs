using System;
using System.Collections.Generic;
using System.Linq;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Inventory;
using CRPG.Presentation.Equipment;
using CRPG.Presentation.Sprites;

namespace CRPG.Presentation.Creator
{
    /// <summary>
    /// The 2D character creator's logic, with no UI in it: the rows a screen shows (the appearance rows, then Class), a live <see cref="Preview"/> the screen
    /// paints, the class's <see cref="StartingItems"/>, and <see cref="Build"/>, which turns the current selection into a complete <see cref="CharacterBuildPlan"/>.
    /// A view only renders <see cref="Rows"/> and forwards clicks to <see cref="CreatorRow.Advance"/>.
    /// <para>A <see cref="CreatorFilteredAppearanceRowSpec"/> (Hair) contributes two rows: a filter row (Hair Colour, options carry swatches) and a style row keyed by
    /// the layer slot. Changing the colour swaps the style row's options for that colour's and KEEPS the style position (every colour lists the same shapes in the
    /// same order). Only the style row reaches the appearance map, so saves and NPCs still hold a single hair option id.</para>
    /// <para>The class fixes the kit (armor, weapon). The preview is a real <see cref="SpriteLayerState"/> over the world's own slot set, fed the same way the world
    /// feeds a character's: the chosen look through <see cref="SpriteLayerState.ApplyAppearance"/>, and every kit item as EQUIPMENT through the shared
    /// <see cref="SpriteEquipmentPaints"/> (the exact code <see cref="SpriteEquipmentBinder"/> uses), keyed by each item's own slot, so equipment-beats-appearance,
    /// hides and priorities behave in the preview exactly as in the world. A class change clears the old kit's keys and applies the new kit.</para>
    /// <para><see cref="Build"/> always starts from a FRESH plan: a reused plan would keep the old class's resolved choices (and the features they grant) and would
    /// surface nothing the second time.</para>
    /// </summary>
    public sealed class CharacterCreatorModel
    {
        public const string ClassRowKey = "class";
        private const int MaxChoiceRounds = 8;

        private sealed class FilteredRows
        {
            public CreatorFilteredAppearanceRowSpec Spec;
            public List<CreatorFilter> Filters;
            public CreatorRow FilterRow;
            public CreatorRow StyleRow;
        }

        private readonly IRulesDatabase rules;
        private readonly CreatorContent content;
        private readonly SpriteCatalogSource source;
        private readonly List<CreatorRow> rows = new();
        private readonly List<(CreatorRow Row, DefinitionId Slot)> appearanceRows = new(); // plain rows and style rows: what reaches the appearance map
        private readonly List<FilteredRows> filtered = new();
        private readonly Dictionary<DefinitionId, CreatorClassSpec> classSpecs = new();
        private readonly List<LayerPaint> paints = new();
        private readonly HashSet<DefinitionId> kitKeys = new();
        private bool suppress;
        private Action<string> report;

        /// <param name="report">Receives every problem the model degrades around (an unknown class, an unknown kit item, a bad filter). It never logs itself.</param>
        public CharacterCreatorModel(IRulesDatabase rules, CreatorContent content, Action<string> report = null)
        {
            this.rules = rules ?? throw new ArgumentNullException(nameof(rules));
            this.content = content ?? throw new ArgumentNullException(nameof(content));
            this.report = report;
            source = new SpriteCatalogSource(rules);

            Preview = new SpriteLayerState(SpriteLayerCompositor.DefaultSlots()) { Report = Say };

            foreach (var spec in content.AppearanceRows)
            {
                if (spec is CreatorFilteredAppearanceRowSpec filteredSpec) AddFiltered(filteredSpec);
                else AddPlain(spec);
            }

            var classOptions = new List<CreatorOption>();
            foreach (var spec in content.Classes)
            {
                if (!rules.TryResolve<ClassDefinition>(spec.ClassId, out var definition))
                {
                    Say($"The creator offers class '{spec.ClassId.Value}', which is not in the ruleset; skipped.");
                    continue;
                }

                if (classSpecs.ContainsKey(spec.ClassId))
                {
                    Say($"The creator lists class '{spec.ClassId.Value}' twice; the first entry wins.");
                    continue;
                }

                classSpecs[spec.ClassId] = spec;
                classOptions.Add(new CreatorOption(spec.ClassId, definition.DisplayName));
            }

            ClassRow = new CreatorRow(ClassRowKey, "Class", classOptions);
            rows.Add(ClassRow);

            foreach (var row in rows)
            {
                var captured = row;
                row.Changed += () => OnRowChanged(captured);
            }

            RefreshPreview();
        }

        /// <summary>The rows in screen order: the appearance rows (a filtered spec gives its filter row then its style row), then Class.</summary>
        public IReadOnlyList<CreatorRow> Rows => rows;

        public CreatorRow ClassRow { get; }

        /// <summary>The selected class's starting kit in order (armor, then weapon; option Id = item id, Name = the name shown); empty when the class has none.</summary>
        public IReadOnlyList<CreatorOption> StartingItems =>
            ClassRow.Count > 0 && classSpecs.TryGetValue(ClassRow.Selected.Id, out var spec) ? spec.StartingItems : Array.Empty<CreatorOption>();

        /// <summary>The live assembled character; repaint from it whenever <see cref="Changed"/> fires (its <c>Version</c> only moves on a real change).</summary>
        public SpriteLayerState Preview { get; }

        /// <summary>Where the model (and the preview state it owns) reports problems it degraded around; null discards them.</summary>
        public Action<string> Report
        {
            get => report;
            set => report = value;
        }

        /// <summary>Raised once after any row change (a colour change that also swaps the style options, or a class change that also swaps the kit, is still one notification).</summary>
        public event Action Changed;

        public CreatorRow RowFor(string key) => rows.FirstOrDefault(r => r.Key == key);

        private void Say(string message) => report?.Invoke(message);

        private void AddPlain(CreatorAppearanceRowSpec spec)
        {
            var row = new CreatorRow(spec.LayerSlot.Value, spec.Title, spec.Options);
            rows.Add(row);
            appearanceRows.Add((row, spec.LayerSlot));
        }

        private void AddFiltered(CreatorFilteredAppearanceRowSpec spec)
        {
            var expected = spec.Filters.Count > 0 ? spec.Filters[0].Options.Count : 0;
            var usable = new List<CreatorFilter>();
            foreach (var filter in spec.Filters)
            {
                if (expected > 0 && filter.Options.Count == expected) usable.Add(filter);
                else Say($"Filter '{filter.Id}' of '{spec.FilterKey}' lists {filter.Options.Count} option(s) where '{(spec.Filters.Count > 0 ? spec.Filters[0].Id : "")}' lists {expected}; skipped.");
            }

            if (usable.Count == 0)
            {
                Say($"'{spec.FilterKey}' has no usable filter; it contributes no rows.");
                return;
            }

            var filterRow = new CreatorRow(spec.FilterKey, spec.FilterTitle, usable.Select(f => new CreatorOption(new DefinitionId(f.Id), f.Name, f.Swatch)));
            var styleRow = new CreatorRow(spec.LayerSlot.Value, spec.Title, NamedOptions(usable[0], spec.StyleNames));
            rows.Add(filterRow);
            rows.Add(styleRow);
            appearanceRows.Add((styleRow, spec.LayerSlot));
            filtered.Add(new FilteredRows { Spec = spec, Filters = usable, FilterRow = filterRow, StyleRow = styleRow });
        }

        private static IEnumerable<CreatorOption> NamedOptions(CreatorFilter filter, IReadOnlyList<string> styleNames)
        {
            for (var i = 0; i < filter.Options.Count; i++)
                yield return new CreatorOption(filter.Options[i], i < styleNames.Count ? styleNames[i] : $"Style {i + 1}");
        }

        private FilteredRows FilteredFor(CreatorRow filterRow) => filtered.FirstOrDefault(f => f.FilterRow == filterRow);

        // Changing the colour swaps the style options for that colour's and keeps the style position (the same shape in a new colour).
        private void RebuildStyle(FilteredRows f)
        {
            var was = suppress;
            suppress = true; // the style row's own Changed must not trigger a second refresh/notification
            f.StyleRow.ReplaceOptions(NamedOptions(f.Filters[f.FilterRow.Index], f.Spec.StyleNames), keepIndex: true);
            suppress = was;
        }

        private void OnRowChanged(CreatorRow row)
        {
            if (suppress) return;
            var f = FilteredFor(row);
            if (f != null) RebuildStyle(f);
            RefreshPreview();
            Changed?.Invoke();
        }

        // ---------------------------------------------------------------------------------------------
        // Preview
        // ---------------------------------------------------------------------------------------------

        private void RefreshPreview()
        {
            var look = new Dictionary<DefinitionId, DefinitionId>();
            foreach (var (row, slot) in appearanceRows)
                if (row.Count > 0) look[slot] = row.Selected.Id;
            Preview.ApplyAppearance(look, source);

            ApplyKit();
        }

        // Every kit item is worn through the SAME helper the world's binder uses, keyed by the item's own slot. A key the new kit does not use is cleared.
        private void ApplyKit()
        {
            var wanted = new HashSet<DefinitionId>();
            foreach (var option in StartingItems)
            {
                if (!rules.TryResolve<ItemDefinition>(option.Id, out var item) || !item.EquipmentSlotId.IsValid)
                {
                    Say($"Kit item '{option.Id.Value}' is not an equippable item in the ruleset; the preview does not show it.");
                    continue;
                }

                if (!SpriteEquipmentPaints.TryResolveSpritePaints(rules, option.Id, paints, out var profile)) continue; // no sprite profile: it draws nothing, as in the world

                Preview.SetEquipment(item.EquipmentSlotId, profile.SpritePriority, paints, profile.HiddenLayerSlots);
                wanted.Add(item.EquipmentSlotId);
            }

            foreach (var key in kitKeys)
                if (!wanted.Contains(key)) Preview.ClearEquipment(key);
            kitKeys.Clear();
            foreach (var key in wanted) kitKeys.Add(key);
        }

        // ---------------------------------------------------------------------------------------------
        // Randomize
        // ---------------------------------------------------------------------------------------------

        /// <summary>Picks every row at random from <paramref name="seed"/> (deterministic): the appearance rows in order (a colour, then the style within it), then the
        /// Class, whose kit follows. One notification when done.</summary>
        public void Randomize(int seed)
        {
            var random = new System.Random(seed);
            suppress = true;
            try
            {
                foreach (var row in rows)
                {
                    if (row == ClassRow || row.Count == 0) continue;
                    row.SelectIndex(random.Next(row.Count));
                    var f = FilteredFor(row);
                    if (f != null) RebuildStyle(f); // the style row that follows must offer the chosen colour's options
                }

                if (ClassRow.Count > 0) ClassRow.SelectIndex(random.Next(ClassRow.Count));
            }
            finally
            {
                suppress = false;
            }

            RefreshPreview();
            Changed?.Invoke();
        }

        // ---------------------------------------------------------------------------------------------
        // Build
        // ---------------------------------------------------------------------------------------------

        /// <summary>Turns the current selection into a complete character build: race, class, background and level from the content, the six
        /// ability scores from the class's priority order over the standard array, the chosen look, and every level-1 choice answered from the
        /// class's defaults; the starting kit travels in the result. Throws <see cref="InvalidOperationException"/> naming the problem if anything cannot be resolved
        /// (a missing or wrong-sized default, a default that is not one of the requirement's options, an unknown race). Registers and spawns nothing.</summary>
        public CreatorResult Build()
        {
            if (ClassRow.Count == 0) throw new InvalidOperationException("The creator has no selectable class.");

            var classId = ClassRow.Selected.Id;
            var spec = classSpecs[classId];

            if (!rules.TryResolve<RaceDefinition>(content.Race, out var race))
                throw new InvalidOperationException($"Race '{content.Race.Value}' is not in the ruleset.");
            if (!rules.TryResolve<BackgroundDefinition>(content.Background, out var background))
                throw new InvalidOperationException($"Background '{content.Background.Value}' is not in the ruleset.");
            if (!rules.TryResolve<ClassDefinition>(classId, out var classDefinition))
                throw new InvalidOperationException($"Class '{classId.Value}' is not in the ruleset.");
            if (spec.ScoreOrder.Count != content.StandardArray.Count)
                throw new InvalidOperationException(
                    $"Class '{classId.Value}' orders {spec.ScoreOrder.Count} ability scores but the standard array has {content.StandardArray.Count} values.");

            var plan = new CharacterBuildPlan { Race = race, Class = classDefinition, Background = background, Level = content.Level };
            for (var i = 0; i < spec.ScoreOrder.Count; i++) plan.AbilityScores.SetScore(spec.ScoreOrder[i], content.StandardArray[i]);
            foreach (var (row, slot) in appearanceRows)
                if (row.Count > 0) plan.SetAppearance(slot, row.Selected.Id);

            var service = new CharacterBuildService(rules);

            // A resolved selection re-enters feature collection (a fighting style is a feature) and can surface FURTHER choices, so answer in
            // rounds until nothing is pending. The bound only stops a ruleset that keeps surfacing choices forever.
            for (var round = 0; round < MaxChoiceRounds; round++)
            {
                service.RefreshPendingChoices(plan);

                // A snapshot: ResolveChoice removes from the very list PendingChoices exposes.
                var pending = plan.PendingChoices.ToList();
                if (pending.Count == 0) break;

                foreach (var requirement in pending)
                {
                    var answer = spec.ChoiceDefaults.FirstOrDefault(d => d.RequirementId == requirement.Id);
                    if (answer == null)
                        throw new InvalidOperationException($"Class '{classId.Value}' has no default for choice '{requirement.Id}'.");
                    if (answer.Options.Count != requirement.Count)
                        throw new InvalidOperationException(
                            $"Class '{classId.Value}': the default for '{requirement.Id}' has {answer.Options.Count} option(s) but the choice needs {requirement.Count}.");
                    foreach (var option in answer.Options)
                        if (!requirement.Options.Contains(option))
                            throw new InvalidOperationException($"Class '{classId.Value}': the default for '{requirement.Id}' includes '{option.Value}', which is not one of its options.");

                    plan.ResolveChoice(requirement.Id, answer.Options.ToList());
                }
            }

            service.RefreshPendingChoices(plan);
            if (!plan.IsComplete)
                throw new InvalidOperationException(
                    $"Class '{classId.Value}' still has unresolved choices after its defaults: {string.Join(", ", plan.PendingChoices.Select(r => r.Id))}.");

            return new CreatorResult(plan, classId, StartingItems.Select(i => i.Id));
        }
    }
}

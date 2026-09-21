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
    /// The 2D character creator's logic, with no UI in it: the rows a screen shows (the appearance rows, then Weapon, then Class), a live
    /// <see cref="Preview"/> the screen paints, and <see cref="Build"/>, which turns the current selection into a complete
    /// <see cref="CharacterBuildPlan"/>. A view only renders <see cref="Rows"/> and forwards clicks to <see cref="CreatorRow.Advance"/>.
    /// <para>The preview is a real <see cref="SpriteLayerState"/> over the world's own slot set, fed the same way the world feeds a
    /// character's: the chosen look through <see cref="SpriteLayerState.ApplyAppearance"/>, the chosen weapon as EQUIPMENT through the
    /// shared <see cref="SpriteEquipmentPaints"/> (the exact code <see cref="SpriteEquipmentBinder"/> uses), so equipment-beats-appearance,
    /// hides and priorities behave in the preview exactly as in the world.</para>
    /// <para>Changing the Class row rebuilds the Weapon row from that class's starter weapons, keeping the current weapon when the new
    /// class still offers it. <see cref="Build"/> always starts from a FRESH plan: a reused plan would keep the old class's resolved
    /// choices (and the features they grant) and would surface nothing the second time.</para>
    /// </summary>
    public sealed class CharacterCreatorModel
    {
        public const string WeaponRowKey = "weapon";
        public const string ClassRowKey = "class";
        private const int MaxChoiceRounds = 8;

        private readonly IRulesDatabase rules;
        private readonly CreatorContent content;
        private readonly SpriteCatalogSource source;
        private readonly List<CreatorRow> rows = new();
        private readonly List<(CreatorRow Row, DefinitionId Slot)> appearanceRows = new();
        private readonly Dictionary<DefinitionId, CreatorClassSpec> classSpecs = new();
        private readonly List<LayerPaint> paints = new();
        private DefinitionId weaponKey;
        private bool suppress;
        private Action<string> report;

        /// <param name="report">Receives every problem the model degrades around (an unknown class, an unknown item). It never logs itself.</param>
        public CharacterCreatorModel(IRulesDatabase rules, CreatorContent content, Action<string> report = null)
        {
            this.rules = rules ?? throw new ArgumentNullException(nameof(rules));
            this.content = content ?? throw new ArgumentNullException(nameof(content));
            this.report = report;
            source = new SpriteCatalogSource(rules);

            Preview = new SpriteLayerState(SpriteLayerCompositor.DefaultSlots()) { Report = Say };

            foreach (var spec in content.AppearanceRows)
            {
                var row = new CreatorRow(spec.LayerSlot.Value, spec.Title, spec.Options);
                rows.Add(row);
                appearanceRows.Add((row, spec.LayerSlot));
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
            WeaponRow = new CreatorRow(WeaponRowKey, "Weapon", WeaponsFor(ClassRow.Selected.Id));
            rows.Add(WeaponRow);
            rows.Add(ClassRow);

            foreach (var row in rows)
            {
                var captured = row;
                row.Changed += () => OnRowChanged(captured);
            }

            RefreshPreview();
        }

        /// <summary>The rows in screen order: one per appearance row, then Weapon, then Class.</summary>
        public IReadOnlyList<CreatorRow> Rows => rows;

        public CreatorRow WeaponRow { get; }
        public CreatorRow ClassRow { get; }

        /// <summary>The live assembled character; repaint from it whenever <see cref="Changed"/> fires (its <c>Version</c> only moves on a real change).</summary>
        public SpriteLayerState Preview { get; }

        /// <summary>Where the model (and the preview state it owns) reports problems it degraded around; null discards them.</summary>
        public Action<string> Report
        {
            get => report;
            set => report = value;
        }

        /// <summary>Raised once after any row change (a class change that also rebuilds the weapon list is still one notification).</summary>
        public event Action Changed;

        public CreatorRow RowFor(string key) => rows.FirstOrDefault(r => r.Key == key);

        private void Say(string message) => report?.Invoke(message);

        private IEnumerable<CreatorOption> WeaponsFor(DefinitionId classId) =>
            classId.IsValid && classSpecs.TryGetValue(classId, out var spec) ? spec.Weapons : Enumerable.Empty<CreatorOption>();

        private void OnRowChanged(CreatorRow row)
        {
            if (suppress) return;
            if (row == ClassRow) RebuildWeapons();
            RefreshPreview();
            Changed?.Invoke();
        }

        private void RebuildWeapons()
        {
            var was = suppress;
            suppress = true; // the weapon row's own Changed must not trigger a second refresh/notification
            WeaponRow.ReplaceOptions(WeaponsFor(ClassRow.Selected.Id));
            suppress = was;
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

            ApplyWeapon();
        }

        private void ApplyWeapon()
        {
            if (WeaponRow.Count == 0)
            {
                ClearWeapon();
                return;
            }

            var itemId = WeaponRow.Selected.Id;
            if (!rules.TryResolve<ItemDefinition>(itemId, out var item) || !item.EquipmentSlotId.IsValid)
            {
                Say($"Weapon '{itemId.Value}' is not an equippable item in the ruleset; the preview shows no weapon.");
                ClearWeapon();
                return;
            }

            if (!SpriteEquipmentPaints.TryResolveSpritePaints(rules, itemId, paints, out var profile))
            {
                ClearWeapon(); // no sprite profile: this weapon draws nothing, which is what the world would show too
                return;
            }

            var key = item.EquipmentSlotId;
            if (weaponKey.IsValid && weaponKey != key) Preview.ClearEquipment(weaponKey);
            Preview.SetEquipment(key, profile.SpritePriority, paints, profile.HiddenLayerSlots);
            weaponKey = key;
        }

        private void ClearWeapon()
        {
            if (!weaponKey.IsValid) return;
            Preview.ClearEquipment(weaponKey);
            weaponKey = default;
        }

        // ---------------------------------------------------------------------------------------------
        // Randomize
        // ---------------------------------------------------------------------------------------------

        /// <summary>Picks every row at random from <paramref name="seed"/> (deterministic): the appearance rows first, then the Class, then a
        /// weapon from that class's list. One notification when done.</summary>
        public void Randomize(int seed)
        {
            var random = new System.Random(seed);
            suppress = true;
            try
            {
                foreach (var (row, _) in appearanceRows)
                    if (row.Count > 0) row.SelectIndex(random.Next(row.Count));

                if (ClassRow.Count > 0) ClassRow.SelectIndex(random.Next(ClassRow.Count));
                RebuildWeapons();
                if (WeaponRow.Count > 0) WeaponRow.SelectIndex(random.Next(WeaponRow.Count));
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
        /// class's defaults. Throws <see cref="InvalidOperationException"/> naming the problem if anything cannot be resolved (a missing or
        /// wrong-sized default, a default that is not one of the requirement's options, an unknown race). Registers and spawns nothing.</summary>
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

            return new CreatorResult(plan, classId, WeaponRow.Count > 0 ? WeaponRow.Selected.Id : DefinitionId.None);
        }
    }
}

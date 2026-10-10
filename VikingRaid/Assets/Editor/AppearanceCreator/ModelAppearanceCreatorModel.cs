using System;
using System.Collections.Generic;
using System.Linq;
using CRPG.Core;
using CRPG.Presentation.Creator;

namespace VikingRaid.Appearance.Editor
{
    /// <summary>
    /// The creator's state without any UI: one <see cref="CreatorRow"/> per catalog row (an optional row has "None" first, a required row does not), the choices they hold, and the operations the
    /// buttons call. <see cref="Changed"/> is raised for edits made THROUGH the model (a row advanced, Randomize, Reset); <see cref="Load"/> replaces the state quietly (it is how an outside change, an
    /// undo or an edit in the raw foldout, reaches the creator, and writing that back would loop).
    /// </summary>
    public sealed class ModelAppearanceCreatorModel
    {
        public const string NoneName = "None";

        private readonly ModelAppearanceCatalog catalog;
        private readonly List<CreatorRow> rows = new();
        private bool quiet;

        public ModelAppearanceCreatorModel(ModelAppearanceCatalog catalog)
        {
            this.catalog = catalog;
            foreach (var slot in catalog.Slots)
            {
                var options = new List<CreatorOption>();
                if (!slot.required) options.Add(new CreatorOption(DefinitionId.None, string.IsNullOrEmpty(slot.noneTitle) ? NoneName : slot.noneTitle));
                options.AddRange(slot.options.Select(o => o.swatch.a > 0 ? new CreatorOption(new DefinitionId(o.id), o.title, o.swatch) : new CreatorOption(new DefinitionId(o.id), o.title)));
                var row = new CreatorRow(slot.id, slot.title, options);
                row.Changed += () => { if (!quiet) Changed?.Invoke(); };
                rows.Add(row);
            }

            Reset(quietly: true);
        }

        public ModelAppearanceCatalog Catalog => catalog;
        public IReadOnlyList<CreatorRow> Rows => rows;
        public event Action Changed;

        public CreatorRow RowFor(string slotId) => rows.FirstOrDefault(r => r.Key == slotId);

        /// <summary>The non-"None" choices, keyed by row.</summary>
        public Dictionary<DefinitionId, DefinitionId> Choices
        {
            get
            {
                var result = new Dictionary<DefinitionId, DefinitionId>();
                foreach (var row in rows)
                    if (row.Count > 0 && row.Selected.Id.IsValid) result[new DefinitionId(row.Key)] = row.Selected.Id;
                return result;
            }
        }

        /// <summary>Replaces every row from stored (slot, option) pairs, without raising <see cref="Changed"/>. A missing or unknown option means the row's default (required) or None (optional).</summary>
        public void Load(IEnumerable<KeyValuePair<DefinitionId, DefinitionId>> stored)
        {
            var wanted = new Dictionary<string, string>();
            foreach (var pair in stored ?? Enumerable.Empty<KeyValuePair<DefinitionId, DefinitionId>>()) wanted[pair.Key.Value ?? ""] = pair.Value.Value;
            Quietly(() =>
            {
                foreach (var slot in catalog.Slots)
                {
                    var row = RowFor(slot.id);
                    wanted.TryGetValue(slot.id, out var id);
                    if (string.IsNullOrEmpty(id) || slot.Find(id) == null) id = slot.required ? slot.defaultOptionId : null;
                    Select(row, slot, id);
                }
            });
        }

        /// <summary>Required rows to their defaults, optional rows to None.</summary>
        public void Reset() => Reset(quietly: false);

        private void Reset(bool quietly)
        {
            void Do() { foreach (var slot in catalog.Slots) Select(RowFor(slot.id), slot, slot.required ? slot.defaultOptionId : null); }
            if (quietly) Quietly(Do); else { Do(); Changed?.Invoke(); }
        }

        /// <summary>Rolls every row, or only the rows on <paramref name="tab"/> (the others keep their choice). Raises <see cref="Changed"/> once.</summary>
        public void Randomize(int seed, string tab = null)
        {
            var rolled = ModelAppearanceRandomizer.Roll(catalog, seed, tab, Choices);
            Quietly(() =>
            {
                foreach (var slot in catalog.Slots)
                    Select(RowFor(slot.id), slot, rolled.TryGetValue(new DefinitionId(slot.id), out var id) ? id.Value : null);
            });
            Changed?.Invoke();
        }

        private static void Select(CreatorRow row, ModelAppearanceSlot slot, string optionId)
        {
            if (string.IsNullOrEmpty(optionId)) { row.SelectIndex(0); return; }   // optional: None is first; required rows always have a default
            row.Select(new DefinitionId(optionId));
        }

        private void Quietly(Action action)
        {
            var before = quiet;
            quiet = true;
            try { action(); } finally { quiet = before; }
        }
    }
}

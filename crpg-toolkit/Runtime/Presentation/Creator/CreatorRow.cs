using System;
using System.Collections.Generic;
using System.Linq;
using CRPG.Core;

namespace CRPG.Presentation.Creator
{
    /// <summary>
    /// One `< Title: Value >` row of the character creator: an ordered option list and a selected index that wraps in both
    /// directions. What a view binds to. An empty row is legal (a class with no starter weapon): <see cref="Count"/> is 0,
    /// <see cref="Selected"/> is the default option and every mutator is a quiet no-op.
    /// </summary>
    public sealed class CreatorRow
    {
        private CreatorOption[] options;

        public CreatorRow(string key, string title, IEnumerable<CreatorOption> options)
        {
            Key = key ?? "";
            Title = title ?? "";
            this.options = (options ?? Enumerable.Empty<CreatorOption>()).ToArray();
        }

        /// <summary>Stable identity for a view/test to find a row: the layer-slot id for appearance rows, <c>weapon</c> or <c>class</c>.</summary>
        public string Key { get; }
        public string Title { get; }

        public IReadOnlyList<CreatorOption> Options => options;
        public int Count => options.Length;
        public int Index { get; private set; }

        /// <summary>The selected option; the default value when the row is empty.</summary>
        public CreatorOption Selected => options.Length == 0 ? default : options[Index];

        /// <summary>Raised after the selection or the option list really changed (never for a no-op).</summary>
        public event Action Changed;

        /// <summary>Moves the selection by <paramref name="delta"/> with wraparound (any magnitude, either sign; a full lap is a
        /// no-op). <c>Advance(1)</c> is "next", <c>Advance(-1)</c> "previous", and a large delta is a page jump through a long row.
        /// Returns true if the selection changed.</summary>
        public bool Advance(int delta)
        {
            if (options.Length == 0) return false;
            var next = (int)(((long)Index + delta) % options.Length);
            if (next < 0) next += options.Length;
            return SelectIndex(next);
        }

        /// <summary>Selects by index; out of range is ignored. Returns true if the selection changed.</summary>
        public bool SelectIndex(int index)
        {
            if (index < 0 || index >= options.Length || index == Index) return false;
            Index = index;
            Changed?.Invoke();
            return true;
        }

        /// <summary>Selects the option with this id; an unknown id is ignored. Returns true if the selection changed.</summary>
        public bool Select(DefinitionId id)
        {
            for (var i = 0; i < options.Length; i++)
                if (options[i].Id == id) return SelectIndex(i);
            return false;
        }

        /// <summary>Swaps the option list. By default the current option is kept if the new list still offers it (matched by id), else the first is chosen
        /// (the Weapon-style case). With <paramref name="keepIndex"/> the selection stays at the same POSITION instead, clamped to the new length (the style row
        /// when the colour changes: the ids differ per colour, the shape at that position is the same). Raises <see cref="Changed"/> once if the list or the
        /// selection changed, never for an identical list.</summary>
        internal void ReplaceOptions(IEnumerable<CreatorOption> replacement, bool keepIndex = false)
        {
            var next = (replacement ?? Enumerable.Empty<CreatorOption>()).ToArray();
            var keep = options.Length == 0 ? default : options[Index].Id;
            var sameList = next.Length == options.Length;
            for (var i = 0; sameList && i < next.Length; i++)
                if (next[i].Id != options[i].Id) sameList = false;
            if (sameList) return;

            var newIndex = 0;
            if (keepIndex)
            {
                newIndex = next.Length == 0 ? 0 : Math.Min(Index, next.Length - 1);
            }
            else if (keep.IsValid)
            {
                for (var i = 0; i < next.Length; i++)
                    if (next[i].Id == keep) { newIndex = i; break; }
            }

            options = next;
            Index = newIndex;
            Changed?.Invoke();
        }
    }
}

using System;
using System.Collections.Generic;
using CRPG.Core;
using UnityEngine;

namespace CRPG.Presentation.Sprites
{
    /// <summary>A sprite and the tint to draw it with.</summary>
    public readonly struct SpriteOption
    {
        public SpriteOption(Sprite sprite, Color tint)
        {
            Sprite = sprite;
            Tint = tint;
        }

        public Sprite Sprite { get; }
        public Color Tint { get; }
    }

    /// <summary>One slot an equipment set paints.</summary>
    public readonly struct LayerPaint
    {
        public LayerPaint(DefinitionId slot, SpriteOption option)
        {
            Slot = slot;
            Option = option;
        }

        public DefinitionId Slot { get; }
        public SpriteOption Option { get; }
    }

    /// <summary>What one slot shows right now. <see cref="Visible"/> is false for an empty or hidden slot.</summary>
    public readonly struct EffectiveLayer
    {
        public EffectiveLayer(DefinitionId slot, int slotIndex, Sprite sprite, Color tint, bool visible)
        {
            Slot = slot;
            SlotIndex = slotIndex;
            Sprite = sprite;
            Tint = tint;
            Visible = visible;
        }

        public DefinitionId Slot { get; }
        public int SlotIndex { get; }
        public Sprite Sprite { get; }
        public Color Tint { get; }
        public bool Visible { get; }
    }

    /// <summary>
    /// Pure logic for a layered sprite character: which sprite shows in each draw slot, given the character's appearance,
    /// the equipment it wears, and what that equipment hides. Creates no Unity objects, so it is EditMode-testable and a
    /// UI backend (a creator preview or paper doll rendered into a panel) can reuse it without renderers.
    /// <para>Per slot, in draw order: hidden (any equipment set lists the slot) beats everything and shows nothing;
    /// else the paint from the equipment set with the highest priority (ties: smaller ordinal key), with its own tint;
    /// else the appearance sprite; else empty. Equipment is keyed by INVENTORY slot (the <c>SlotId</c> that
    /// EquipmentService publishes), so unequipping restores whatever lay beneath.</para>
    /// </summary>
    public sealed class SpriteLayerState
    {
        private sealed class EquipmentSet
        {
            public int Priority;
            public bool[] HasPaint;
            public Sprite[] Sprites;
            public Color[] Tints;
            public bool[] Hides;

            public bool SameAs(EquipmentSet other)
            {
                if (Priority != other.Priority) return false;
                for (var i = 0; i < HasPaint.Length; i++)
                {
                    if (HasPaint[i] != other.HasPaint[i] || Hides[i] != other.Hides[i]) return false;
                    if (HasPaint[i] && (Sprites[i] != other.Sprites[i] || !ColorsEqual(Tints[i], other.Tints[i]))) return false;
                }

                return true;
            }
        }

        private readonly DefinitionId[] slots;
        private readonly Dictionary<DefinitionId, int> slotIndex = new();
        private readonly Sprite[] appearanceSprites;
        private readonly Color[] appearanceTints;
        private readonly Dictionary<DefinitionId, EquipmentSet> equipment = new();
        private readonly EffectiveLayer[] layers;
        private int version;
        private int builtVersion = -1;

        public SpriteLayerState(IReadOnlyList<DefinitionId> slotOrder)
        {
            if (slotOrder == null || slotOrder.Count == 0) throw new ArgumentException("A layer state needs at least one slot.", nameof(slotOrder));
            if (slotOrder.Count > SpriteSortingContract.MaxSlotsPerObject)
                throw new ArgumentException($"At most {SpriteSortingContract.MaxSlotsPerObject} slots are supported (got {slotOrder.Count}).", nameof(slotOrder));

            slots = new DefinitionId[slotOrder.Count];
            for (var i = 0; i < slots.Length; i++)
            {
                var slot = slotOrder[i];
                if (!slot.IsValid) throw new ArgumentException($"Slot {i} has no id.", nameof(slotOrder));
                if (!slotIndex.TryAdd(slot, i)) throw new ArgumentException($"Duplicate slot '{slot.Value}'.", nameof(slotOrder));
                slots[i] = slot;
            }

            appearanceSprites = new Sprite[slots.Length];
            appearanceTints = new Color[slots.Length];
            layers = new EffectiveLayer[slots.Length];
        }

        /// <summary>Diagnostics (unknown slot, missing sprite, duplicate paint). Optional; invoked as <c>Report?.Invoke</c>.</summary>
        public Action<string> Report { get; set; }

        public int SlotCount => slots.Length;
        public DefinitionId SlotAt(int index) => slots[index];
        public bool TryGetSlotIndex(DefinitionId slot, out int index) => slotIndex.TryGetValue(slot, out index);

        /// <summary>Increments on every mutation that can change the output; an identical re-set does not.</summary>
        public int Version => version;

        /// <summary>One entry per slot in draw order (back to front), refreshed in place: the SAME array instance every
        /// time. A mutation while a caller is enumerating yields mixed-generation data, not an exception.</summary>
        public IReadOnlyList<EffectiveLayer> Layers
        {
            get
            {
                if (builtVersion != version) Rebuild();
                return layers;
            }
        }

        public void SetAppearance(DefinitionId slot, SpriteOption option)
        {
            if (!slotIndex.TryGetValue(slot, out var i))
            {
                Report?.Invoke($"Unknown layer slot '{slot.Value}'; appearance ignored.");
                return;
            }

            if (option.Sprite == null)
            {
                Report?.Invoke($"No sprite for slot '{slot.Value}'; the layer is left empty.");
                ClearAppearance(slot);
                return;
            }

            if (appearanceSprites[i] == option.Sprite && ColorsEqual(appearanceTints[i], option.Tint)) return;
            appearanceSprites[i] = option.Sprite;
            appearanceTints[i] = option.Tint;
            version++;
        }

        public void ClearAppearance(DefinitionId slot)
        {
            if (!slotIndex.TryGetValue(slot, out var i) || appearanceSprites[i] == null) return;
            appearanceSprites[i] = null;
            appearanceTints[i] = default;
            version++;
        }

        public void ClearAllAppearance()
        {
            var changed = false;
            for (var i = 0; i < slots.Length; i++)
            {
                if (appearanceSprites[i] == null) continue;
                appearanceSprites[i] = null;
                appearanceTints[i] = default;
                changed = true;
            }

            if (changed) version++;
        }

        /// <summary>Sets what the item worn in inventory slot <paramref name="key"/> paints and hides, replacing that key's
        /// previous set atomically. Either list may be null or empty (a hides-only or paints-only set is legal).</summary>
        public void SetEquipment(DefinitionId key, int priority, IReadOnlyList<LayerPaint> paints, IReadOnlyList<DefinitionId> hiddenSlots)
        {
            var set = new EquipmentSet
            {
                Priority = priority,
                HasPaint = new bool[slots.Length],
                Sprites = new Sprite[slots.Length],
                Tints = new Color[slots.Length],
                Hides = new bool[slots.Length],
            };

            if (paints != null)
            {
                foreach (var paint in paints)
                {
                    if (!slotIndex.TryGetValue(paint.Slot, out var i))
                    {
                        Report?.Invoke($"Equipment '{key.Value}' paints unknown layer slot '{paint.Slot.Value}'; ignored.");
                        continue;
                    }

                    if (paint.Option.Sprite == null)
                    {
                        Report?.Invoke($"Equipment '{key.Value}' has no sprite for slot '{paint.Slot.Value}'; ignored.");
                        continue;
                    }

                    if (set.HasPaint[i]) Report?.Invoke($"Equipment '{key.Value}' paints slot '{paint.Slot.Value}' more than once; the last paint wins.");
                    set.HasPaint[i] = true;
                    set.Sprites[i] = paint.Option.Sprite;
                    set.Tints[i] = paint.Option.Tint;
                }
            }

            if (hiddenSlots != null)
            {
                foreach (var hidden in hiddenSlots)
                {
                    if (!slotIndex.TryGetValue(hidden, out var i))
                    {
                        Report?.Invoke($"Equipment '{key.Value}' hides unknown layer slot '{hidden.Value}'; ignored.");
                        continue;
                    }

                    set.Hides[i] = true;
                }
            }

            if (equipment.TryGetValue(key, out var existing) && existing.SameAs(set)) return;
            equipment[key] = set;
            version++;
        }

        /// <summary>Removes the set for <paramref name="key"/>. A silent no-op for an unknown key (unequipping an item with
        /// no sprite profile is normal).</summary>
        public void ClearEquipment(DefinitionId key)
        {
            if (equipment.Remove(key)) version++;
        }

        /// <summary>Copies the visible layers, in draw order, into <paramref name="destination"/> (cleared first).</summary>
        public void CopyVisibleTo(List<EffectiveLayer> destination)
        {
            destination.Clear();
            foreach (var layer in Layers)
                if (layer.Visible) destination.Add(layer);
        }

        private void Rebuild()
        {
            for (var i = 0; i < slots.Length; i++)
            {
                var hidden = false;
                EquipmentSet winner = null;
                DefinitionId winnerKey = default;
                foreach (var pair in equipment)
                {
                    var set = pair.Value;
                    if (set.Hides[i]) hidden = true;
                    if (!set.HasPaint[i]) continue;
                    if (winner == null || set.Priority > winner.Priority
                        || (set.Priority == winner.Priority && string.CompareOrdinal(pair.Key.Value, winnerKey.Value) < 0))
                    {
                        winner = set;
                        winnerKey = pair.Key;
                    }
                }

                var sprite = winner != null ? winner.Sprites[i] : appearanceSprites[i];
                var tint = winner != null ? winner.Tints[i] : appearanceTints[i];
                layers[i] = new EffectiveLayer(slots[i], i, sprite, tint, !hidden && sprite != null);
            }

            builtVersion = version;
        }

        // Exact component compare: Color's == is an epsilon compare and would swallow a deliberate tiny tint change.
        private static bool ColorsEqual(Color a, Color b) => a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;
    }
}

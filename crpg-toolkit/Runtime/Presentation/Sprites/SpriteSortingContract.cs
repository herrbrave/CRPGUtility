using UnityEngine;

namespace CRPG.Presentation.Sprites
{
    /// <summary>
    /// The shared world-Z to draw-order contract for the top-down 2D presentation: characters, props and tall walls on
    /// the <see cref="WorldLayer"/> sorting layer sort against each other by world Z; floor tiles live on
    /// <see cref="GroundLayer"/> at a fixed low order and are never sorted.
    /// <para>Each 1/8-unit row <c>k</c> owns the order band <c>[-16k, -16k + 15]</c>. Order <c>-16k</c> (offset 0) is the
    /// row's PROP lane; a character's layers occupy offsets <c>1..15</c>. The row behind (higher Z) tops out at
    /// <c>-16k - 1</c>, strictly below this row's prop lane, so nothing behind can draw through a prop or character in
    /// front, at every slot count up to <see cref="MaxSlotsPerObject"/>.</para>
    /// <para><b>Precondition: camera yaw 0.</b> At pitch 90 with yaw 0 screen-up is world +Z, so higher Z is further away
    /// and draws behind. Nothing in the camera settings enforces yaw today (only pitch is clamped); a rotated camera needs
    /// a locked yaw or a different formula.</para>
    /// <para>A prop taller than one cell is sorted per occupied cell: author one sprite per cell, or give its
    /// <see cref="SpriteDepthSorter"/> an explicit anchor at its ground-footprint row (the default anchor is the sprite's
    /// centre).</para>
    /// </summary>
    public static class SpriteSortingContract
    {
        public const string GroundLayer = "Ground";
        public const string WorldLayer = "World";

        public const int StepsPerUnit = 8;
        public const int OrdersPerRow = 16;
        public const int MaxSlotsPerObject = 15;
        public const int PropBias = 0;
        public const float MaxWorldZ = 256f;

        private const int MinOrder = short.MinValue;
        private const int MaxOrder = short.MaxValue;

        /// <summary>The order-band base for an object at <paramref name="worldZ"/>. Clamped in float space first (no int
        /// overflow for a huge finite Z); NaN/Infinity map to 0. <paramref name="clamped"/> is true whenever the input
        /// was outside the supported range or non-finite.</summary>
        public static int BaseOrder(float worldZ, out bool clamped)
        {
            if (!float.IsFinite(worldZ))
            {
                clamped = true;
                return 0;
            }

            var z = Mathf.Clamp(worldZ, -MaxWorldZ, MaxWorldZ);
            clamped = z != worldZ;
            var order = -Mathf.RoundToInt(z * StepsPerUnit) * OrdersPerRow;
            return Mathf.Clamp(order, MinOrder, MaxOrder - (OrdersPerRow - 1));
        }

        public static int BaseOrder(float worldZ) => BaseOrder(worldZ, out _);

        /// <summary>A concrete sorting order for <paramref name="offset"/> inside a band (characters: 1 + slot; props: the
        /// prop lane, <see cref="PropBias"/>). Always within int16, whatever the inputs.</summary>
        public static int OrderFor(int baseOrder, int offset) =>
            (int)System.Math.Clamp((long)baseOrder + offset, MinOrder, MaxOrder);

        /// <summary>The order for slot <paramref name="slotIndex"/> of a character whose band starts at <paramref name="baseOrder"/>.</summary>
        public static int SlotOrder(int baseOrder, int slotIndex) => OrderFor(baseOrder, 1 + slotIndex);
    }
}

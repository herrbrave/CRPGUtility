using UnityEngine;

namespace CRPG.Presentation.Sprites
{
    /// <summary>
    /// Depth-sorts a sprite that NEVER MOVES (a tree, a wall, a door frame painted onto a tilemap) against the characters on the World sorting layer:
    /// it computes its <see cref="SpriteSortingContract"/> order once, in Start, from its own world Z (the prop lane), writes it to its SpriteRenderer
    /// and then disables itself, so a map with thousands of props pays nothing per frame. A moving object needs <see cref="SpriteDepthSorter"/> instead.
    /// Start (not Awake) so an object a Tilemap instantiates has already been positioned at its cell. Call <see cref="Resort"/> if a static object is
    /// deliberately moved. Like the moving sorter it assumes camera yaw 0 and |Z| within the sortable range, and warns once if clamped.
    /// </summary>
    [DefaultExecutionOrder(50)]
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class StaticSpriteSorter : MonoBehaviour
    {
        [Tooltip("Added to the band base. Leave at the prop lane (0).")]
        [SerializeField] private int orderOffset = SpriteSortingContract.PropBias;

        private SpriteRenderer spriteRenderer;

        public int OrderOffset { get => orderOffset; set => orderOffset = value; }

        private void Start()
        {
            Resort();
            enabled = false; // static: never touched again
        }

        /// <summary>Recomputes the order from the current world Z (for a static object that was moved on purpose).</summary>
        public void Resort()
        {
            if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
            var z = transform.position.z;
            var band = SpriteSortingContract.BaseOrder(z, out var clamped);
            if (clamped)
                Debug.LogWarning($"[StaticSpriteSorter] '{name}' is at world Z {z}, outside +-{SpriteSortingContract.MaxWorldZ}: its depth order is clamped and it will sort wrongly against neighbours.", this);

            spriteRenderer.sortingLayerName = SpriteSortingContract.WorldLayer;
            spriteRenderer.sortingOrder = SpriteSortingContract.OrderFor(band, orderOffset);
        }
    }
}

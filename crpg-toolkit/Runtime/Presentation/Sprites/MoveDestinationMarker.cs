using System;
using CRPG.Core;
using UnityEngine;

namespace CRPG.Presentation.Sprites
{
    /// <summary>
    /// A single, always-active ground marker shown at the raw destination of the most recent
    /// <see cref="MoveCommand"/> (2D-Campaign-ExplorationUX-Followups.md D5) -- the user's explicit choice of
    /// the raw click point over a resolved/snapped destination signal (finding 10), so this needs no new
    /// <see cref="PlayerCommandRouter"/> API; it subscribes directly to the existing <see cref="IPlayerCommandSource.Move"/>
    /// event, which is already reachable from <c>CRPG.Core</c> (no assembly-cycle issue, unlike
    /// <see cref="IDragSelectionFeedback"/>'s own situation).
    /// <para>An <c>Update</c>-driven countdown, not a Coroutine (finding 12/S4): a Coroutine on a
    /// deactivated-then-reactivated object is a real lifecycle trap, and a new <see cref="MoveCommand"/>
    /// superseding an in-flight one would need its old Coroutine explicitly stopped. A single always-active
    /// instance with a plain timer sidesteps both -- there is also never more than one marker to pool.</para>
    /// <para>Drawn on <see cref="SpriteSortingContract.GroundLayer"/>, ABOVE the Ground tilemap's own flat
    /// order-0 (finding 11) -- <see cref="SpriteSortingContract.WorldLayer"/> is for characters/props and
    /// already hosts the Objects tilemap at a flat order 0, which could draw over a marker placed there. The
    /// sprite is rotated to lie flat (<c>Euler(90,0,0)</c>), matching this project's own flat-ground-visual
    /// convention (<c>BarrowsEndLocationAuthor.cs</c>), since a default <see cref="SpriteRenderer"/> lies
    /// edge-on to the pitch-90 camera and would be invisible otherwise.</para>
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class MoveDestinationMarker : MonoBehaviour
    {
        [Tooltip("How long the marker stays visible after a Move command, in seconds.")]
        [SerializeField] private float visibleSeconds = 1.5f;

        [Tooltip("Left null to fall back to a small procedural ring (D6: checked live against the project's " +
                 "own art library first; this fallback exists for whenever nothing fits, not as the final look).")]
        [SerializeField] private Sprite markerSprite;

        private SpriteRenderer spriteRenderer;
        private IPlayerCommandSource source;
        private float remainingSeconds;

        /// <summary>Set by the composition root (BarrowsEnd wiring, D7): <c>CRPG.Presentation</c> cannot
        /// reference <c>CRPG.Gameplay.Scenes.IWorldTransitionGate</c> directly (Presentation sits below
        /// Gameplay in the assembly graph), so a plain live check stands in for it here instead of the
        /// interface itself. Checked every frame the marker is visible; null (the default) never suppresses.
        /// Suppressing cancels the marker outright (not just a pause) -- resuming display of a destination that
        /// may belong to a scene that's no longer loaded would be worse than simply not showing it again.</summary>
        public Func<bool> IsWorldTransitioning { get; set; }

        private void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            spriteRenderer.sprite = markerSprite != null ? markerSprite : GetFallbackSprite();
            transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            spriteRenderer.sortingLayerName = SpriteSortingContract.GroundLayer;
            spriteRenderer.sortingOrder = 1; // above the Ground tilemap's own flat order-0
            spriteRenderer.enabled = false;
        }

        private void OnDestroy()
        {
            if (source != null) source.Move -= OnMove;
        }

        /// <summary>Wires this marker to a live command source -- call once from the composition root (D7).
        /// Safe to call again with a different source (re-binds cleanly, unsubscribing from the old one first).</summary>
        public void Initialize(IPlayerCommandSource commandSource)
        {
            if (source != null) source.Move -= OnMove;
            source = commandSource ?? throw new ArgumentNullException(nameof(commandSource));
            source.Move += OnMove;
        }

        private void OnMove(MoveCommand command)
        {
            // A small lift above the ground plane avoids z-fighting with the flat Ground tilemap beneath it,
            // matching BarrowsEndLocationAuthor's own flat-ground-visual convention (its "Visual" child, 0.02).
            transform.position = command.Destination + new Vector3(0f, 0.02f, 0f);
            remainingSeconds = visibleSeconds;
            spriteRenderer.enabled = true;
        }

        private void Update()
        {
            if (!spriteRenderer.enabled) return;

            if (IsWorldTransitioning?.Invoke() ?? false)
            {
                remainingSeconds = 0f;
                spriteRenderer.enabled = false;
                return;
            }

            remainingSeconds -= Time.deltaTime;
            if (remainingSeconds <= 0f) spriteRenderer.enabled = false;
        }

        private static Sprite GetFallbackSprite()
        {
            const int size = 32;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var center = new Vector2((size - 1) / 2f, (size - 1) / 2f);
            var outerRadius = size / 2f - 1f;
            var innerRadius = outerRadius - 3f;
            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var distance = Vector2.Distance(new Vector2(x, y), center);
                    var onRing = distance <= outerRadius && distance >= innerRadius;
                    pixels[y * size + x] = new Color32(90, 220, 110, (byte)(onRing ? 230 : 0));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            // pixelsPerUnit == size: the ring is exactly 1x1 world units, roughly one grid cell.
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }
    }
}

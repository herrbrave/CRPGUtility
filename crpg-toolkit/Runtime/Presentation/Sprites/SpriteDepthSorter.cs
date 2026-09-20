using UnityEngine;

namespace CRPG.Presentation.Sprites
{
    /// <summary>
    /// Y-sorts a sprite character (or a prop) against everything else on the World sorting layer by the world Z of its
    /// ANCHOR -- never the card's own position (the card carries the prefab's world-Y offset and the rock tilt, both of
    /// which move it in world Z at pitch 90 and would jitter its own order). Anchor: an explicit transform; else the parent
    /// of the <see cref="CharacterView"/> (CharacterRoot); else this object's own transform (the right choice for a prop).
    /// See <see cref="SpriteSortingContract"/> for the formula and its camera-yaw-0 precondition.
    /// <para>Drives a <see cref="SpriteLayerCompositor"/> on this object or a child (via its sorting base), or else a lone
    /// <see cref="SpriteRenderer"/> on this object (a prop).</para>
    /// </summary>
    [DefaultExecutionOrder(50)]
    public sealed class SpriteDepthSorter : MonoBehaviour
    {
        [Tooltip("Optional. The transform whose world Z decides the order. Set for a tall prop, at its ground-footprint row.")]
        [SerializeField] private Transform anchor;
        [Tooltip("Added to the band base. Leave at the prop lane (0).")]
        [SerializeField] private int orderOffset = SpriteSortingContract.PropBias;

        private Transform resolvedAnchor;
        private SpriteLayerCompositor compositor;
        private SpriteRenderer single;
        private int lastOrder = int.MinValue;
        private bool warnedClamp;
        private bool yawChecked;

        public Transform Anchor { get => anchor; set => anchor = value; }
        public int OrderOffset { get => orderOffset; set => orderOffset = value; }

        private void LateUpdate()
        {
            CheckCameraYawOnce();

            var source = ResolveAnchor();
            var z = source.position.z;
            if (!float.IsFinite(z)) return;

            if (compositor == null && single == null)
            {
                compositor = GetComponentInChildren<SpriteLayerCompositor>(true);
                if (compositor == null) single = GetComponent<SpriteRenderer>();
                if (compositor == null && single == null) return;
            }

            var band = SpriteSortingContract.BaseOrder(z, out var clamped);
            if (clamped && !warnedClamp)
            {
                warnedClamp = true;
                Debug.LogWarning($"[SpriteDepthSorter] World Z {z} on '{name}' is outside the sortable range (+-{SpriteSortingContract.MaxWorldZ}); its draw order is clamped.", this);
            }

            var order = SpriteSortingContract.OrderFor(band, orderOffset);
            if (compositor != null)
            {
                compositor.SortingBase = order;
            }
            else if (single != null && order != lastOrder)
            {
                single.sortingOrder = order;
                lastOrder = order;
            }
        }

        private Transform ResolveAnchor()
        {
            if (anchor != null) return anchor;
            if (resolvedAnchor != null) return resolvedAnchor;

            var view = GetComponentInParent<CharacterView>(true);
            if (view != null) resolvedAnchor = view.transform.parent != null ? view.transform.parent : view.transform;
            else resolvedAnchor = transform;
            return resolvedAnchor;
        }

        private void CheckCameraYawOnce()
        {
            if (yawChecked) return;
            var cam = Camera.main;
            if (cam == null) return;

            yawChecked = true;
            var yaw = Mathf.DeltaAngle(0f, cam.transform.eulerAngles.y);
            if (Mathf.Abs(yaw) > 1f)
                Debug.LogWarning($"[SpriteDepthSorter] The main camera's yaw is {yaw:F0} degrees; the sprite sort contract assumes yaw 0, so draw order will be wrong.", this);
        }
    }
}

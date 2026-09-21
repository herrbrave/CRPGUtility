using UnityEngine;

namespace CRPG.Navigation.Grid.Tilemaps
{
    /// <summary>
    /// Written onto a location's <c>Baked</c> root by the location baker: the <see cref="TileFingerprint"/> of the painted tilemaps at the moment they were baked.
    /// Walkability, wall colliders and camera bounds all come from the bake, so painting a tile afterwards silently leaves them out of date (an object you placed
    /// stays walk-through). This component makes that loud: in the editor and in development builds it compares the tilemaps as they are now with the baked
    /// fingerprint when the location loads, and logs an error naming the fix. It never changes anything and does nothing in a release build.
    /// </summary>
    public sealed class BakeFingerprint : MonoBehaviour
    {
        [SerializeField] private string value;
        [SerializeField] private UnityEngine.Grid grid;

        /// <summary>The fingerprint recorded when this location was baked.</summary>
        public string Value => value;

        public void Record(UnityEngine.Grid bakedGrid)
        {
            grid = bakedGrid;
            value = TileFingerprint.Compute(bakedGrid);
        }

        /// <summary>True when the painted tilemaps still match what was baked. False (with the reason) when they changed, or when there is nothing to compare.</summary>
        public bool IsCurrent(out string message)
        {
            if (grid == null || string.IsNullOrEmpty(value))
            {
                message = $"'{ParentName()}' has a bake with no recorded fingerprint: bake it again.";
                return false;
            }

            if (TileFingerprint.Compute(grid) == value)
            {
                message = null;
                return true;
            }

            message = $"'{ParentName()}' has painted tiles that changed since it was last baked, so its walkability, wall colliders and camera bounds are out of date " +
                      "(a new object would still be walk-through). Bake it again: Barrows End > Location > Bake Selected Location.";
            return false;
        }

        private string ParentName() => transform.parent != null ? transform.parent.name : name;

        private void Start()
        {
            if (!Application.isEditor && !Debug.isDebugBuild) return; // a check for whoever is authoring, never for players
            if (!IsCurrent(out var message)) Debug.LogError("[BakeFingerprint] " + message, this);
        }
    }
}

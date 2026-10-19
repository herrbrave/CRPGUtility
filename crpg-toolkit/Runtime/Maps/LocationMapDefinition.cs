using System.Collections.Generic;
using UnityEngine;

namespace CRPG.Maps
{
    /// <summary>
    /// The authored background image of one location's map and the exact world rectangle that image
    /// represents. Shared by the minimap and the area map. Holds nothing else: landmark positions live in scene
    /// anchors (see the Gameplay-side binding), and nothing here is captured or rebuilt at runtime.
    /// </summary>
    /// <remarks>
    /// <see cref="TextureWorldBoundsXZ"/> is authoritative: Rect.x is world X, Rect.y is world Z. It is the
    /// rectangle the image was baked or painted to cover, not the gameplay camera's bounds. Its aspect ratio
    /// must match the texture's; <see cref="Validate"/> reports a mismatch (for example a square capture paired
    /// with an unexpanded, non-square rectangle) instead of letting the map distort or misplace markers.
    /// </remarks>
    [CreateAssetMenu(menuName = "CRPG/Location Map Definition", fileName = "LocationMap")]
    public sealed class LocationMapDefinition : ScriptableObject
    {
        /// <summary>Relative tolerance when comparing image aspect to world-rectangle aspect.</summary>
        public const float AspectTolerance = 0.001f;

        [SerializeField] private string locationId;
        [SerializeField] private string title;
        [SerializeField] private string titleKey;
        [SerializeField] private Texture2D background;
        [SerializeField] private Rect textureWorldBoundsXZ;
        [SerializeField] private string captureSource;

        /// <summary>Stable id of the location this map describes; also namespaces landmark marker keys.</summary>
        public string LocationId => locationId;

        /// <summary>Display title shown on both views.</summary>
        public string Title => title;

        /// <summary>Optional localization key for the title, following the existing localization convention.</summary>
        public string TitleKey => titleKey;

        /// <summary>The shared north-up background image. Borrowed: a view never destroys it.</summary>
        public Texture2D Background => background;

        /// <summary>World rectangle covered by <see cref="Background"/> (Rect.x = world X, Rect.y = world Z).</summary>
        public Rect TextureWorldBoundsXZ => textureWorldBoundsXZ;

        /// <summary>Editor provenance, e.g. the scene path the image was baked from; empty for hand-painted art.</summary>
        public string CaptureSource => captureSource;

        /// <summary>Builds a definition in memory -- for tests and for editor tooling to populate before saving.</summary>
        public static LocationMapDefinition Create(string locationId, string title, Texture2D background,
            Rect textureWorldBoundsXZ, string titleKey = null, string captureSource = null)
        {
            var definition = CreateInstance<LocationMapDefinition>();
            definition.locationId = locationId;
            definition.title = title;
            definition.titleKey = titleKey;
            definition.background = background;
            definition.textureWorldBoundsXZ = textureWorldBoundsXZ;
            definition.captureSource = captureSource;
            return definition;
        }

        /// <summary>Appends a human-readable line per problem and returns true when there are none: a
        /// non-empty location id, a non-null image, positive finite bounds, and image/world aspect parity.</summary>
        public bool Validate(List<string> problems)
        {
            var before = problems.Count;
            var name = string.IsNullOrEmpty(locationId) ? "(unnamed)" : locationId;

            if (string.IsNullOrWhiteSpace(locationId))
                problems.Add("Location map has no location id.");

            var b = textureWorldBoundsXZ;
            var boundsOk = IsFinite(b.x) && IsFinite(b.y) && IsFinite(b.width) && IsFinite(b.height)
                           && b.width > 0f && b.height > 0f;
            if (!boundsOk)
                problems.Add($"Location map '{name}' needs finite world bounds with positive width and height (got {b}).");

            if (background == null)
            {
                problems.Add($"Location map '{name}' has no background image.");
            }
            else if (boundsOk && background.width > 0 && background.height > 0)
            {
                var imageAspect = (float)background.width / background.height;
                var worldAspect = b.width / b.height;
                if (Mathf.Abs(imageAspect - worldAspect) > AspectTolerance * worldAspect)
                    problems.Add($"Location map '{name}': image is {background.width}x{background.height} " +
                                 $"(aspect {imageAspect:0.####}) but its world bounds are {b.width}x{b.height} m " +
                                 $"(aspect {worldAspect:0.####}). The image would be distorted; re-bake with the expanded " +
                                 "square rectangle or supply bounds matching the image.");
            }

            return problems.Count == before;
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}

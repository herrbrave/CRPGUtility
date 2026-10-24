using System;
using System.Collections.Generic;
using CRPG.Maps;
using UnityEngine;

namespace CRPG.Gameplay.Scenes
{
    /// <summary>One fixed place worth marking on the map -- a service, an exit, a landmark. Its position is the
    /// scene <see cref="Anchor"/>'s, never a duplicated number, so moving the anchor in the editor moves the marker.
    /// Not for NPCs: a landmark describes a place, so a wandering villager must not drag a pin around.</summary>
    [Serializable]
    public sealed class MapLandmark
    {
        [Tooltip("Stable id, unique within this location. Part of the marker's identity -- do not rename casually.")]
        public string Id;

        [Tooltip("Shown on hover in the area map.")]
        public string Label;

        [Tooltip("Where the marker sits. Required.")]
        public Transform Anchor;

        [Tooltip("Optional icon. Empty uses the default landmark symbol.")]
        public Sprite Icon;

        public Color Tint = Color.white;

        [Tooltip("The silhouette drawn when there is no icon: a kind of place gets its own shape. Diamond is the default symbol.")]
        public MapGlyphShape Shape = MapGlyphShape.Diamond;

        public string DisplayLabel => string.IsNullOrEmpty(Label) ? Id : Label;
    }

    /// <summary>
    /// Scene binding for a location's map: the background <see cref="LocationMapDefinition"/> plus the authored
    /// landmark anchors. A sibling of <see cref="LocationContent"/> on the location GameObject -- deliberately not
    /// part of <see cref="ILocationContent"/>, so existing locations are unaffected. A location without one has no
    /// map UI at all.
    /// </summary>
    public sealed class LocationMapContent : MonoBehaviour
    {
        [SerializeField] private LocationMapDefinition definition;
        [SerializeField] private List<MapLandmark> landmarks = new();

        public LocationMapDefinition Definition => definition;
        public IReadOnlyList<MapLandmark> Landmarks => landmarks;

        /// <summary>Runtime/test setup for content built in code; scene content sets these in the Inspector.</summary>
        public void Configure(LocationMapDefinition mapDefinition, IEnumerable<MapLandmark> mapLandmarks)
        {
            definition = mapDefinition;
            landmarks = mapLandmarks == null ? new List<MapLandmark>() : new List<MapLandmark>(mapLandmarks);
        }

        /// <summary>Appends a line per authoring problem and returns true when there are none: an invalid or
        /// missing definition, empty or duplicate landmark ids, missing anchors, and anchors outside the image.
        /// Nothing is silently corrected -- an out-of-image anchor is the designer's call (move the anchor or
        /// re-bake), never an automatic enlargement of the bounds.</summary>
        public bool Validate(List<string> problems)
        {
            var before = problems.Count;

            if (definition == null)
            {
                problems.Add($"'{name}' has a LocationMapContent with no map definition assigned.");
            }
            else
            {
                definition.Validate(problems);
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < landmarks.Count; i++)
            {
                var landmark = landmarks[i];
                if (landmark == null)
                {
                    problems.Add($"Map landmark #{i} is empty.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(landmark.Id))
                {
                    problems.Add($"Map landmark #{i} ('{landmark.Label}') has no id.");
                    continue;
                }

                if (!seen.Add(landmark.Id))
                    problems.Add($"Map landmark id '{landmark.Id}' is used more than once.");

                if (landmark.Anchor == null)
                {
                    problems.Add($"Map landmark '{landmark.Id}' has no anchor Transform.");
                    continue;
                }

                if (definition != null && IsUsableBounds(definition.TextureWorldBoundsXZ))
                {
                    var p = landmark.Anchor.position;
                    var bounds = definition.TextureWorldBoundsXZ;
                    if (p.x < bounds.xMin || p.x > bounds.xMax || p.z < bounds.yMin || p.z > bounds.yMax)
                        problems.Add($"Map landmark '{landmark.Id}' is outside the map image (anchor at x={p.x:0.#}, z={p.z:0.#}; " +
                                     $"image covers x {bounds.xMin:0.#}..{bounds.xMax:0.#}, z {bounds.yMin:0.#}..{bounds.yMax:0.#}).");
                }
            }

            return problems.Count == before;
        }

        /// <summary>Throws with every authoring problem listed. A configured but invalid map must fail
        /// initialization loudly, not fall back to a blank map or world-origin coordinates.</summary>
        public void EnsureValid()
        {
            var problems = new List<string>();
            if (Validate(problems)) return;
            throw new InvalidOperationException("Location map is not valid:\n - " + string.Join("\n - ", problems));
        }

        private static bool IsUsableBounds(Rect r) =>
            !float.IsNaN(r.x) && !float.IsNaN(r.y) && !float.IsNaN(r.width) && !float.IsNaN(r.height)
            && !float.IsInfinity(r.width) && !float.IsInfinity(r.height) && r.width > 0f && r.height > 0f;
    }
}

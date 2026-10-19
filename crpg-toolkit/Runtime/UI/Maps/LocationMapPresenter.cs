using System;
using System.Collections.Generic;
using CRPG.Maps;
using UnityEngine;

namespace CRPG.UI
{
    /// <summary>
    /// The state behind the location map: one shared marker snapshot sampled from the source, and two independent
    /// viewport states over the same image -- the minimap (follows the active character) and the area map
    /// (fit / pan / zoom). Plain C#: the HUD ticks it with a clock and tells it about user intent.
    /// </summary>
    /// <remarks>
    /// Sampling runs at a fixed rate (<see cref="SampleInterval"/>, 20 Hz by default) against the caller's clock
    /// so tests control it. Both views read <see cref="Markers"/> -- there is exactly one snapshot per sample, so
    /// the minimap and the area map can never show different positions. When the active character is
    /// temporarily unavailable the minimap holds its last valid centre (<see cref="FocusAvailable"/> goes false);
    /// before any valid focus has been seen it stays at the authored image centre, never at the world origin.
    /// </remarks>
    public sealed class LocationMapPresenter
    {
        private readonly IMapMarkerSource source;
        private readonly List<MapMarkerSnapshot> markers = new();
        private float nextSampleTime = float.NegativeInfinity;
        private Vector2 lastFocus;
        private bool hasObservedFocus;

        public LocationMapPresenter(LocationMapDefinition definition, IMapMarkerSource source,
            MapViewportOptions minimapOptions = null, MapViewportOptions areaOptions = null)
        {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            this.source = source ?? throw new ArgumentNullException(nameof(source));

            var problems = new List<string>();
            if (!definition.Validate(problems))
                throw new InvalidOperationException("Location map is not valid:\n - " + string.Join("\n - ", problems));

            Minimap = new MapViewportState(definition.TextureWorldBoundsXZ, minimapOptions ?? MapViewportOptions.Minimap());
            Area = new MapViewportState(definition.TextureWorldBoundsXZ, areaOptions ?? MapViewportOptions.Area());
        }

        public LocationMapDefinition Definition { get; }
        public MapViewportState Minimap { get; }
        public MapViewportState Area { get; }

        /// <summary>The shared snapshot from the most recent sample. Owned and reused by the presenter.</summary>
        public IReadOnlyList<MapMarkerSnapshot> Markers => markers;

        /// <summary>False while the active character cannot be located (destroyed, not spawned): the minimap is
        /// then holding its last centre and the HUD shows a neutral unavailable state.</summary>
        public bool FocusAvailable { get; private set; } = true;

        /// <summary>True once a valid active-character position has been observed.</summary>
        public bool HasObservedFocus => hasObservedFocus;

        public float SampleInterval { get; set; } = 1f / 20f;

        /// <summary>Raised after each sample so views can re-lay-out.</summary>
        public event Action Sampled;

        /// <summary>Samples if <see cref="SampleInterval"/> has elapsed since the last sample. Returns whether it
        /// did. Pass an unscaled clock so the map keeps updating if the game's time scale is changed.</summary>
        public bool Tick(float time)
        {
            if (time < nextSampleTime) return false;
            nextSampleTime = time + SampleInterval;
            SampleNow();
            return true;
        }

        /// <summary>Samples immediately (opening the area map, tests).</summary>
        public void SampleNow()
        {
            source.Collect(markers);

            if (source.TryGetFocusXZ(out var focus) && IsFinite(focus))
            {
                lastFocus = focus;
                hasObservedFocus = true;
                FocusAvailable = true;
                Minimap.CenterOn(focus);
            }
            else
            {
                FocusAvailable = false;
            }

            Sampled?.Invoke();
        }

        /// <summary>Fit Location: the whole image, centred, at the fitted scale.</summary>
        public void FitArea() => Area.Fit();

        /// <summary>Recenter on Active Character: keeps the current area zoom, subject to the area clamp. A no-op
        /// until the active character has been located at least once.</summary>
        public void RecenterAreaOnActive()
        {
            if (hasObservedFocus) Area.CenterOn(lastFocus);
        }

        private static bool IsFinite(Vector2 v) =>
            !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsInfinity(v.x) && !float.IsInfinity(v.y);
    }
}

using System.Collections.Generic;
using CRPG.Core;
using CRPG.Gameplay;
using CRPG.Maps;
using UnityEngine;

namespace CRPG.Tests.EditMode
{
    /// <summary>Position source for tests: set where an entity "is" without any scene or CharacterAgent.</summary>
    internal sealed class FakeSelectableSource : ISelectableSource
    {
        private readonly Dictionary<RuntimeEntityId, Vector3> positions = new();

        public void Set(RuntimeEntityId id, Vector3 position) => positions[id] = position;
        public void Remove(RuntimeEntityId id) => positions.Remove(id);

        public IEnumerable<SelectableEntity> GetSelectables()
        {
            foreach (var pair in positions) yield return new SelectableEntity(pair.Key, pair.Value);
        }
    }

    /// <summary>Marker source for presenter/view tests: hand it markers and an optional focus.</summary>
    internal sealed class FakeMarkerSource : IMapMarkerSource
    {
        public readonly List<MapMarkerSnapshot> Markers = new();
        public Vector2? Focus;
        public int CollectCount;

        public void Collect(List<MapMarkerSnapshot> buffer)
        {
            CollectCount++;
            buffer.Clear();
            buffer.AddRange(Markers);
        }

        public bool TryGetFocusXZ(out Vector2 focusXZ)
        {
            focusXZ = Focus ?? default;
            return Focus.HasValue;
        }
    }

    internal static class MapTestSupport
    {
        /// <summary>A 2:1 image over world x -100..100, z -50..50 (the doc's projection fixture rectangle).</summary>
        public static readonly Rect WideBounds = new Rect(-100f, -50f, 200f, 100f);

        public static LocationMapDefinition WideDefinition(List<Object> owner, string id = "test-location")
        {
            var texture = new Texture2D(64, 32);
            owner.Add(texture);
            var definition = LocationMapDefinition.Create(id, "Test Location", texture, WideBounds);
            owner.Add(definition);
            return definition;
        }
    }
}

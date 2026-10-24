using CRPG.Core;
using UnityEngine;

namespace CRPG.Maps
{
    public enum MapMarkerKind
    {
        Party,
        Landmark,
        /// <summary>A mark the player dropped on the map: numbered, removable, never a place of the world.</summary>
        Pin,
    }

    /// <summary>The silhouette of a landmark that has no icon. Each is a distinct outline (never distinguished by colour alone);
    /// <see cref="Diamond"/> is the original default symbol.</summary>
    public enum MapGlyphShape
    {
        Diamond,
        Square,
        Circle,
        Ring,
        Triangle,
        Hexagon,
        Star,
        Cross,
        Pin,
    }

    /// <summary>How a party marker is drawn. Never conveyed by colour alone: each state also differs in shape
    /// (filled outlined dot / ring / quieter dot).</summary>
    public enum MapPartyMarkerState
    {
        Unselected,
        Selected,
        Active,
    }

    /// <summary>
    /// One marker as the views see it: a stable key, where it is, and how to draw it. A plain value type so a
    /// source can refill a caller-owned buffer every sample without allocating.
    /// </summary>
    public readonly struct MapMarkerSnapshot
    {
        private MapMarkerSnapshot(string key, MapMarkerKind kind, string label, Vector2 positionXZ,
            MapPartyMarkerState partyState, Sprite icon, Color tint, MapGlyphShape shape = MapGlyphShape.Diamond, int number = 0)
        {
            Shape = shape;
            Number = number;
            Key = key;
            Kind = kind;
            Label = label;
            PositionXZ = positionXZ;
            PartyState = partyState;
            Icon = icon;
            Tint = tint;
        }

        /// <summary>Stable identity (see <see cref="MapMarkerKeys"/>); views reuse a visual per key.</summary>
        public string Key { get; }
        public MapMarkerKind Kind { get; }
        public string Label { get; }

        /// <summary>World (x, z); the marker's world height is not part of a single-floor map.</summary>
        public Vector2 PositionXZ { get; }

        /// <summary>Meaningful for <see cref="MapMarkerKind.Party"/> only.</summary>
        public MapPartyMarkerState PartyState { get; }

        /// <summary>Optional landmark icon; null means the documented default landmark symbol.</summary>
        public Sprite Icon { get; }

        /// <summary>Landmark tint; party markers ignore it.</summary>
        public Color Tint { get; }

        /// <summary>The silhouette drawn when there is no <see cref="Icon"/> (landmarks and pins; party markers ignore it).</summary>
        public MapGlyphShape Shape { get; }

        /// <summary>A pin's number (1-based); zero for everything else.</summary>
        public int Number { get; }

        public static MapMarkerSnapshot ForParty(RuntimeEntityId id, string label, Vector2 positionXZ, MapPartyMarkerState state) =>
            new MapMarkerSnapshot(MapMarkerKeys.Party(id), MapMarkerKind.Party, label, positionXZ, state, null, Color.white);

        public static MapMarkerSnapshot ForLandmark(string locationId, string landmarkId, string label,
            Vector2 positionXZ, Sprite icon = null, Color? tint = null, MapGlyphShape shape = MapGlyphShape.Diamond) =>
            new MapMarkerSnapshot(MapMarkerKeys.Landmark(locationId, landmarkId), MapMarkerKind.Landmark, label,
                positionXZ, MapPartyMarkerState.Unselected, icon, tint ?? Color.white, shape);

        /// <summary>A player's pin: keyed by location and number, drawn as a numbered pin.</summary>
        public static MapMarkerSnapshot ForPin(string locationId, int number, Vector2 positionXZ, Color? tint = null) =>
            new MapMarkerSnapshot(MapMarkerKeys.Pin(locationId, number), MapMarkerKind.Pin, "Pin " + number, positionXZ,
                MapPartyMarkerState.Unselected, null, tint ?? new Color(0.95f, 0.35f, 0.3f, 1f), MapGlyphShape.Pin, number);
    }

    /// <summary>Namespaced marker keys so a party member's id and a landmark's id can never collide, and the
    /// character identity is always the existing <see cref="RuntimeEntityId"/> -- never a second one.</summary>
    public static class MapMarkerKeys
    {
        public static string Party(RuntimeEntityId id) => "party:" + id;
        public static string Landmark(string locationId, string landmarkId) => "landmark:" + locationId + ":" + landmarkId;
        public static string Pin(string locationId, int number) => "pin:" + locationId + ":" + number;
    }
}

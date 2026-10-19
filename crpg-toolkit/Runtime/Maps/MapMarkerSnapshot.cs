using CRPG.Core;
using UnityEngine;

namespace CRPG.Maps
{
    public enum MapMarkerKind
    {
        Party,
        Landmark,
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
            MapPartyMarkerState partyState, Sprite icon, Color tint)
        {
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

        public static MapMarkerSnapshot ForParty(RuntimeEntityId id, string label, Vector2 positionXZ, MapPartyMarkerState state) =>
            new MapMarkerSnapshot(MapMarkerKeys.Party(id), MapMarkerKind.Party, label, positionXZ, state, null, Color.white);

        public static MapMarkerSnapshot ForLandmark(string locationId, string landmarkId, string label,
            Vector2 positionXZ, Sprite icon = null, Color? tint = null) =>
            new MapMarkerSnapshot(MapMarkerKeys.Landmark(locationId, landmarkId), MapMarkerKind.Landmark, label,
                positionXZ, MapPartyMarkerState.Unselected, icon, tint ?? Color.white);
    }

    /// <summary>Namespaced marker keys so a party member's id and a landmark's id can never collide, and the
    /// character identity is always the existing <see cref="RuntimeEntityId"/> -- never a second one.</summary>
    public static class MapMarkerKeys
    {
        public static string Party(RuntimeEntityId id) => "party:" + id;
        public static string Landmark(string locationId, string landmarkId) => "landmark:" + locationId + ":" + landmarkId;
    }
}

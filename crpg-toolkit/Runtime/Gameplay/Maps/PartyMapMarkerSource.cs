using System;
using System.Collections.Generic;
using CRPG.Core;
using CRPG.Gameplay.Scenes;
using CRPG.Maps;
using CRPG.Party;
using UnityEngine;

namespace CRPG.Gameplay
{
    /// <summary>
    /// The location map's <see cref="IMapMarkerSource"/>: one marker per live party member (identity and
    /// active/selected state from <see cref="PartyState"/>, position from a location-scoped
    /// <see cref="ISelectableSource"/>), plus the bound location's authored landmarks.
    /// </summary>
    /// <remarks>
    /// Membership, selection and display labels are cached and rebuilt only when <see cref="PartyState.Changed"/> fires, so a
    /// recruit, removal or active-character switch shows up on the next sample; positions are always live. A
    /// party member with no entry in the position source (destroyed, not yet spawned, standing in another
    /// location) produces no marker and, if it is the active character, makes <see cref="TryGetFocusXZ"/> report
    /// "unavailable" instead of inventing a position. Dispose to unsubscribe.
    /// </remarks>
    public sealed class PartyMapMarkerSource : IMapMarkerSource, IDisposable
    {
        private readonly struct Member
        {
            public Member(RuntimeEntityId id, MapPartyMarkerState state, string label)
            {
                Id = id;
                State = state;
                Label = label;
            }

            public RuntimeEntityId Id { get; }
            public MapPartyMarkerState State { get; }
            public string Label { get; }
        }

        private readonly PartyState party;
        private readonly ISelectableSource positions;
        private readonly LocationMapContent content;
        private readonly Func<RuntimeEntityId, string> labelResolver;
        private readonly List<Member> members = new();
        private readonly Dictionary<RuntimeEntityId, Vector3> livePositions = new();
        private bool membersDirty = true;
        private bool disposed;
        private bool hasFocus;
        private Vector2 focus;

        public PartyMapMarkerSource(PartyState party, ISelectableSource positions, LocationMapContent content,
            Func<RuntimeEntityId, string> labelResolver = null)
        {
            this.party = party ?? throw new ArgumentNullException(nameof(party));
            this.positions = positions ?? throw new ArgumentNullException(nameof(positions));
            this.content = content;
            this.labelResolver = labelResolver;
            party.Changed += MarkDirty;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            party.Changed -= MarkDirty;
        }

        private void MarkDirty() => membersDirty = true;

        public void Collect(List<MapMarkerSnapshot> buffer)
        {
            buffer.Clear();
            hasFocus = false;
            if (disposed) return;

            if (membersDirty) RebuildMembers();

            livePositions.Clear();
            foreach (var entity in positions.GetSelectables()) livePositions[entity.Id] = entity.WorldPosition;

            var active = party.ActiveCharacter;
            foreach (var member in members)
            {
                if (!livePositions.TryGetValue(member.Id, out var world)) continue;

                var xz = new Vector2(world.x, world.z);
                buffer.Add(MapMarkerSnapshot.ForParty(member.Id, member.Label, xz, member.State));
                if (active.IsValid && member.Id == active)
                {
                    focus = xz;
                    hasFocus = true;
                }
            }

            if (content == null || content.Definition == null) return;

            var locationId = content.Definition.LocationId;
            var landmarks = content.Landmarks;
            for (var i = 0; i < landmarks.Count; i++)
            {
                var landmark = landmarks[i];
                if (landmark == null || landmark.Anchor == null || string.IsNullOrEmpty(landmark.Id)) continue;

                var p = landmark.Anchor.position;
                buffer.Add(MapMarkerSnapshot.ForLandmark(locationId, landmark.Id, landmark.DisplayLabel,
                    new Vector2(p.x, p.z), landmark.Icon, landmark.Tint));
            }
        }

        /// <summary>The active character's position as of the most recent <see cref="Collect"/>.</summary>
        public bool TryGetFocusXZ(out Vector2 focusXZ)
        {
            focusXZ = focus;
            return hasFocus;
        }

        private void RebuildMembers()
        {
            membersDirty = false;
            members.Clear();

            var active = party.ActiveCharacter;
            foreach (var id in party.Members)
            {
                var state = active.IsValid && id == active ? MapPartyMarkerState.Active
                    : party.IsSelected(id) ? MapPartyMarkerState.Selected
                    : MapPartyMarkerState.Unselected;
                members.Add(new Member(id, state, labelResolver?.Invoke(id)));
            }
        }
    }
}

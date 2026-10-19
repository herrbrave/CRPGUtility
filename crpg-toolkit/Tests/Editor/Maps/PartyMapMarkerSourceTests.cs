using System.Collections.Generic;
using System.Linq;
using CRPG.Core;
using CRPG.Gameplay;
using CRPG.Gameplay.Scenes;
using CRPG.Maps;
using CRPG.Party;
using NUnit.Framework;
using UnityEngine;

namespace CRPG.Tests.EditMode
{
    public class PartyMapMarkerSourceTests
    {
        private readonly List<Object> created = new();
        private PartyState party;
        private FakeSelectableSource positions;
        private RuntimeEntityId a, b, c;

        [SetUp]
        public void SetUp()
        {
            a = RuntimeEntityId.NewId();
            b = RuntimeEntityId.NewId();
            c = RuntimeEntityId.NewId();
            party = new PartyState(new[] { a, b });
            positions = new FakeSelectableSource();
            positions.Set(a, new Vector3(10f, 5f, 20f));
            positions.Set(b, new Vector3(-30f, 0f, 4f));
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in created) if (o != null) Object.DestroyImmediate(o);
            created.Clear();
        }

        private static List<MapMarkerSnapshot> Sample(IMapMarkerSource source)
        {
            var buffer = new List<MapMarkerSnapshot>();
            source.Collect(buffer);
            return buffer;
        }

        private static MapMarkerSnapshot Find(List<MapMarkerSnapshot> markers, string key) => markers.Single(m => m.Key == key);

        private LocationMapContent Content(params (string id, float x, float z)[] landmarks)
        {
            var go = new GameObject("Location");
            created.Add(go);
            var content = go.AddComponent<LocationMapContent>();
            var list = new List<MapLandmark>();
            foreach (var (id, x, z) in landmarks)
            {
                var anchor = new GameObject("anchor-" + id);
                anchor.transform.position = new Vector3(x, 1f, z);
                created.Add(anchor);
                list.Add(new MapLandmark { Id = id, Label = id.ToUpperInvariant(), Anchor = anchor.transform });
            }

            content.Configure(MapTestSupport.WideDefinition(created, "village"), list);
            return content;
        }

        [Test]
        public void EveryLivePartyMemberGetsOneMarkerKeyedByTheirRuntimeEntityId()
        {
            using var source = new PartyMapMarkerSource(party, positions, null);

            var markers = Sample(source);

            Assert.AreEqual(2, markers.Count);
            Assert.AreEqual(new Vector2(10f, 20f), Find(markers, MapMarkerKeys.Party(a)).PositionXZ);
            Assert.AreEqual(new Vector2(-30f, 4f), Find(markers, MapMarkerKeys.Party(b)).PositionXZ);
        }

        [Test]
        public void PositionIgnoresHeight()
        {
            positions.Set(a, new Vector3(10f, 9999f, 20f));
            using var source = new PartyMapMarkerSource(party, positions, null);
            Assert.AreEqual(new Vector2(10f, 20f), Find(Sample(source), MapMarkerKeys.Party(a)).PositionXZ);
        }

        [Test]
        public void ActiveSelectedAndUnselectedAreDistinguished()
        {
            party.AddMember(c);
            positions.Set(c, Vector3.zero);
            party.SelectMany(new[] { a, b }); // a active (first selected), b selected, c not selected
            using var source = new PartyMapMarkerSource(party, positions, null);

            var markers = Sample(source);

            Assert.AreEqual(MapPartyMarkerState.Active, Find(markers, MapMarkerKeys.Party(a)).PartyState);
            Assert.AreEqual(MapPartyMarkerState.Selected, Find(markers, MapMarkerKeys.Party(b)).PartyState);
            Assert.AreEqual(MapPartyMarkerState.Unselected, Find(markers, MapMarkerKeys.Party(c)).PartyState);
        }

        [Test]
        public void ChangingTheActiveCharacterIsReflectedOnTheNextSample()
        {
            using var source = new PartyMapMarkerSource(party, positions, null);
            Sample(source);

            party.SetActive(b);
            var markers = Sample(source);

            Assert.AreEqual(MapPartyMarkerState.Active, Find(markers, MapMarkerKeys.Party(b)).PartyState);
            Assert.AreEqual(MapPartyMarkerState.Selected, Find(markers, MapMarkerKeys.Party(a)).PartyState);
        }

        [Test]
        public void ARecruitedMemberAppearsAndARemovedOneDisappears()
        {
            using var source = new PartyMapMarkerSource(party, positions, null);
            Assert.AreEqual(2, Sample(source).Count);

            party.AddMember(c);
            positions.Set(c, new Vector3(1f, 0f, 1f));
            Assert.AreEqual(3, Sample(source).Count);

            party.RemoveMember(a);
            var markers = Sample(source);
            Assert.AreEqual(2, markers.Count);
            Assert.IsFalse(markers.Any(m => m.Key == MapMarkerKeys.Party(a)));
        }

        [Test]
        public void MarkersFollowLivePositionsWithoutAnyPartyChange()
        {
            using var source = new PartyMapMarkerSource(party, positions, null);
            Sample(source);

            positions.Set(a, new Vector3(50f, 0f, -25f));

            Assert.AreEqual(new Vector2(50f, -25f), Find(Sample(source), MapMarkerKeys.Party(a)).PositionXZ);
        }

        [Test]
        public void ADestroyedOrAbsentActorProducesNoMarker()
        {
            using var source = new PartyMapMarkerSource(party, positions, null);
            positions.Remove(b);

            var markers = Sample(source);

            Assert.AreEqual(1, markers.Count);
            Assert.IsFalse(markers.Any(m => m.Key == MapMarkerKeys.Party(b)));
        }

        [Test]
        public void AMemberStandingInAnotherLocationProducesNoMarker()
        {
            // The scoped position source simply does not list members in other scenes.
            positions.Remove(a);
            using var source = new PartyMapMarkerSource(party, positions, null);
            Assert.IsFalse(Sample(source).Any(m => m.Key == MapMarkerKeys.Party(a)));
        }

        [Test]
        public void ANonPartyEntityInThePositionSourceIsNotMarked()
        {
            positions.Set(c, new Vector3(3f, 0f, 3f)); // an NPC: live in the scene, not in the party
            using var source = new PartyMapMarkerSource(party, positions, null);
            Assert.IsFalse(Sample(source).Any(m => m.Key == MapMarkerKeys.Party(c)));
        }

        [Test]
        public void FocusIsTheActiveCharactersPosition()
        {
            using var source = new PartyMapMarkerSource(party, positions, null);
            Sample(source);

            Assert.IsTrue(source.TryGetFocusXZ(out var focus));
            Assert.AreEqual(new Vector2(10f, 20f), focus);
        }

        [Test]
        public void FocusIsUnavailableWhenTheActiveActorIsMissingAndNeverFabricated()
        {
            using var source = new PartyMapMarkerSource(party, positions, null);
            positions.Remove(a); // a is the active character

            Sample(source);

            Assert.IsFalse(source.TryGetFocusXZ(out var focus));
            Assert.AreEqual(Vector2.zero, focus); // no meaningful value; callers must check the bool
        }

        [Test]
        public void FocusIsUnavailableWhenThePartyHasNoActiveCharacter()
        {
            party.ClearSelection();
            using var source = new PartyMapMarkerSource(party, positions, null);
            Sample(source);
            Assert.IsFalse(source.TryGetFocusXZ(out _));
        }

        [Test]
        public void FocusIsRecomputedByEachSampleNotLatched()
        {
            using var source = new PartyMapMarkerSource(party, positions, null);
            positions.Remove(a);
            Sample(source);
            Assert.IsFalse(source.TryGetFocusXZ(out _));

            positions.Set(a, new Vector3(1f, 0f, 2f));
            Sample(source);
            Assert.IsTrue(source.TryGetFocusXZ(out var focus));
            Assert.AreEqual(new Vector2(1f, 2f), focus);
        }

        [Test]
        public void LandmarksAreIncludedWithNamespacedKeysAndAnchorPositions()
        {
            var content = Content(("dock", 40f, -10f), ("alehouse", -20f, 30f));
            using var source = new PartyMapMarkerSource(party, positions, content);

            var markers = Sample(source);

            var dock = Find(markers, MapMarkerKeys.Landmark("village", "dock"));
            Assert.AreEqual(MapMarkerKind.Landmark, dock.Kind);
            Assert.AreEqual(new Vector2(40f, -10f), dock.PositionXZ);
            Assert.AreEqual("DOCK", dock.Label);
            Assert.AreEqual(4, markers.Count); // two party members + two landmarks
        }

        [Test]
        public void ALandmarkFollowsItsAnchorButNotAnyNpc()
        {
            var content = Content(("dock", 40f, -10f));
            using var source = new PartyMapMarkerSource(party, positions, content);
            positions.Set(c, new Vector3(40f, 0f, -10f)); // an NPC wandering at the dock changes nothing

            content.Landmarks[0].Anchor.position = new Vector3(45f, 0f, -12f);
            var markers = Sample(source);

            Assert.AreEqual(new Vector2(45f, -12f), Find(markers, MapMarkerKeys.Landmark("village", "dock")).PositionXZ);
            Assert.AreEqual(3, markers.Count);
        }

        [Test]
        public void ALandmarkWithADestroyedAnchorIsSkippedNotPlacedAtTheOrigin()
        {
            var content = Content(("dock", 40f, -10f), ("gone", 1f, 1f));
            using var source = new PartyMapMarkerSource(party, positions, content);
            Object.DestroyImmediate(content.Landmarks[1].Anchor.gameObject);

            var markers = Sample(source);

            Assert.IsFalse(markers.Any(m => m.Key == MapMarkerKeys.Landmark("village", "gone")));
            Assert.IsTrue(markers.Any(m => m.Key == MapMarkerKeys.Landmark("village", "dock")));
        }

        [Test]
        public void PartyAndLandmarkKeysNeverCollide()
        {
            var content = Content((a.ToString(), 0f, 0f)); // a landmark whose id equals a character's id string
            using var source = new PartyMapMarkerSource(party, positions, content);

            var keys = Sample(source).Select(m => m.Key).ToList();

            CollectionAssert.AllItemsAreUnique(keys);
            Assert.AreEqual(3, keys.Count);
        }

        [Test]
        public void ReusesTheCallersBufferAndClearsItEachTime()
        {
            using var source = new PartyMapMarkerSource(party, positions, null);
            var buffer = new List<MapMarkerSnapshot>();

            source.Collect(buffer);
            source.Collect(buffer);

            Assert.AreEqual(2, buffer.Count);
        }

        [Test]
        public void LabelsComeFromTheResolverAndAreCached()
        {
            var calls = 0;
            using var source = new PartyMapMarkerSource(party, positions, null, id => { calls++; return id == a ? "Ragna" : "Orm"; });

            Assert.AreEqual("Ragna", Find(Sample(source), MapMarkerKeys.Party(a)).Label);
            var afterFirst = calls;
            Sample(source);
            Sample(source);

            Assert.AreEqual(afterFirst, calls, "labels must not be re-resolved on every sample");
        }

        [Test]
        public void DisposeStopsListeningToThePartyAndStopsProducingMarkers()
        {
            var source = new PartyMapMarkerSource(party, positions, null);
            Sample(source);

            source.Dispose();
            source.Dispose(); // idempotent

            Assert.AreEqual(0, Sample(source).Count);
            Assert.IsFalse(source.TryGetFocusXZ(out _));
        }

        [Test]
        public void RequiresAPartyAndAPositionSource()
        {
            Assert.Throws<System.ArgumentNullException>(() => new PartyMapMarkerSource(null, positions, null));
            Assert.Throws<System.ArgumentNullException>(() => new PartyMapMarkerSource(party, null, null));
        }
    }
}

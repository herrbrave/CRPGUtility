using System.Collections.Generic;
using CRPG.Core;
using CRPG.Maps;
using NUnit.Framework;
using UnityEngine;

namespace CRPG.Tests.EditMode
{
    public class LocationMapDefinitionTests
    {
        private readonly List<Object> created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in created) if (o != null) Object.DestroyImmediate(o);
            created.Clear();
        }

        private Texture2D Texture(int width, int height)
        {
            var texture = new Texture2D(width, height);
            created.Add(texture);
            return texture;
        }

        private LocationMapDefinition Definition(string id, Texture2D texture, Rect bounds)
        {
            var definition = LocationMapDefinition.Create(id, "Title", texture, bounds);
            created.Add(definition);
            return definition;
        }

        private static List<string> Problems(LocationMapDefinition definition, out bool valid)
        {
            var problems = new List<string>();
            valid = definition.Validate(problems);
            return problems;
        }

        [Test]
        public void ACompleteDefinitionIsValid()
        {
            var definition = Definition("village", Texture(64, 64), new Rect(-50f, -50f, 100f, 100f));
            var problems = Problems(definition, out var valid);
            Assert.IsTrue(valid, string.Join("; ", problems));
            Assert.IsEmpty(problems);
        }

        [Test]
        public void CreateCarriesEveryField()
        {
            var texture = Texture(8, 4);
            var bounds = new Rect(-4f, 10f, 20f, 10f);
            var definition = LocationMapDefinition.Create("loc", "A Place", texture, bounds, "key.place", "Assets/Scenes/A.unity");
            created.Add(definition);

            Assert.AreEqual("loc", definition.LocationId);
            Assert.AreEqual("A Place", definition.Title);
            Assert.AreEqual("key.place", definition.TitleKey);
            Assert.AreSame(texture, definition.Background);
            Assert.AreEqual(bounds, definition.TextureWorldBoundsXZ);
            Assert.AreEqual("Assets/Scenes/A.unity", definition.CaptureSource);
        }

        [Test]
        public void ANonSquareImageMatchingNonSquareBoundsIsValid()
        {
            Problems(Definition("wide", Texture(200, 100), new Rect(0f, 0f, 400f, 200f)), out var valid);
            Assert.IsTrue(valid);
        }

        [Test]
        public void ASquareImagePairedWithNonSquareBoundsIsRejected()
        {
            // The classic bake bug: a square capture but the unexpanded, non-square playable rectangle.
            var problems = Problems(Definition("v", Texture(2048, 2048), new Rect(-100f, -50f, 200f, 100f)), out var valid);
            Assert.IsFalse(valid);
            Assert.AreEqual(1, problems.Count);
            StringAssert.Contains("aspect", problems[0]);
        }

        [Test]
        public void ASlightlyOffAspectWithinToleranceIsAccepted()
        {
            Problems(Definition("v", Texture(1000, 1000), new Rect(0f, 0f, 100f, 100.05f)), out var valid);
            Assert.IsTrue(valid);
        }

        [Test]
        public void ANullImageIsRejected()
        {
            var problems = Problems(Definition("v", null, new Rect(0f, 0f, 10f, 10f)), out var valid);
            Assert.IsFalse(valid);
            Assert.That(problems, Has.Some.Contains("background"));
        }

        [Test]
        public void EmptyOrWhitespaceLocationIdIsRejected()
        {
            Problems(Definition("", Texture(4, 4), new Rect(0f, 0f, 10f, 10f)), out var emptyValid);
            Problems(Definition("  ", Texture(4, 4), new Rect(0f, 0f, 10f, 10f)), out var blankValid);
            Problems(Definition(null, Texture(4, 4), new Rect(0f, 0f, 10f, 10f)), out var nullValid);
            Assert.IsFalse(emptyValid);
            Assert.IsFalse(blankValid);
            Assert.IsFalse(nullValid);
        }

        [Test]
        public void NonPositiveOrNonFiniteBoundsAreRejected()
        {
            foreach (var bounds in new[]
            {
                new Rect(0f, 0f, 0f, 10f),
                new Rect(0f, 0f, 10f, 0f),
                new Rect(0f, 0f, -10f, -10f),
                new Rect(float.NaN, 0f, 10f, 10f),
                new Rect(0f, 0f, float.PositiveInfinity, 10f),
                default(Rect),
            })
            {
                var problems = Problems(Definition("v", Texture(4, 4), bounds), out var valid);
                Assert.IsFalse(valid, $"bounds {bounds} should be rejected");
                Assert.That(problems, Has.Some.Contains("bounds"));
            }
        }

        [Test]
        public void SeveralProblemsAreAllReportedAndAppendedToTheCallersList()
        {
            var problems = new List<string> { "existing" };
            var valid = Definition("", null, default(Rect)).Validate(problems);
            Assert.IsFalse(valid);
            Assert.AreEqual("existing", problems[0]);
            Assert.GreaterOrEqual(problems.Count, 4); // id, bounds, image (+ the existing line)
        }

        [Test]
        public void ValidatingAValidDefinitionLeavesAnExistingListUntouched()
        {
            var problems = new List<string> { "existing" };
            var valid = Definition("v", Texture(4, 4), new Rect(0f, 0f, 1f, 1f)).Validate(problems);
            Assert.IsTrue(valid);
            Assert.AreEqual(1, problems.Count);
        }
    }

    public class MapMarkerTests
    {
        [Test]
        public void PartyAndLandmarkKeysAreNamespacedSoTheyCannotCollide()
        {
            var id = RuntimeEntityId.FromString("abc");
            Assert.AreEqual("party:abc", MapMarkerKeys.Party(id));
            Assert.AreEqual("landmark:village:abc", MapMarkerKeys.Landmark("village", "abc"));
            Assert.AreNotEqual(MapMarkerKeys.Party(id), MapMarkerKeys.Landmark("village", "abc"));
        }

        [Test]
        public void LandmarkIdsAreScopedByLocation()
        {
            Assert.AreNotEqual(MapMarkerKeys.Landmark("a", "dock"), MapMarkerKeys.Landmark("b", "dock"));
        }

        [Test]
        public void PartySnapshotUsesTheRuntimeEntityIdAsItsIdentity()
        {
            var id = RuntimeEntityId.NewId();
            var snapshot = MapMarkerSnapshot.ForParty(id, "Ragna", new Vector2(3f, -4f), MapPartyMarkerState.Active);

            Assert.AreEqual("party:" + id, snapshot.Key);
            Assert.AreEqual(MapMarkerKind.Party, snapshot.Kind);
            Assert.AreEqual("Ragna", snapshot.Label);
            Assert.AreEqual(new Vector2(3f, -4f), snapshot.PositionXZ);
            Assert.AreEqual(MapPartyMarkerState.Active, snapshot.PartyState);
        }

        [Test]
        public void LandmarkSnapshotDefaultsToNoIconAndWhiteTint()
        {
            var snapshot = MapMarkerSnapshot.ForLandmark("village", "dock", "Dock", new Vector2(1f, 2f));

            Assert.AreEqual("landmark:village:dock", snapshot.Key);
            Assert.AreEqual(MapMarkerKind.Landmark, snapshot.Kind);
            Assert.IsNull(snapshot.Icon);
            Assert.AreEqual(Color.white, snapshot.Tint);
        }

        [Test]
        public void LandmarkSnapshotKeepsAnExplicitTint()
        {
            var snapshot = MapMarkerSnapshot.ForLandmark("v", "inn", "Alehouse", Vector2.zero, tint: Color.red);
            Assert.AreEqual(Color.red, snapshot.Tint);
        }
    }
}

using System;
using System.Collections.Generic;
using CRPG.Gameplay.Scenes;
using CRPG.Maps;
using NUnit.Framework;
using UnityEngine;

namespace CRPG.Tests.EditMode
{
    public class LocationMapContentTests
    {
        private readonly List<UnityEngine.Object> created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in created) if (o != null) UnityEngine.Object.DestroyImmediate(o);
            created.Clear();
        }

        private Transform Anchor(float x, float z)
        {
            var go = new GameObject("anchor");
            go.transform.position = new Vector3(x, 3f, z);
            created.Add(go);
            return go.transform;
        }

        private LocationMapContent Content(params MapLandmark[] landmarks)
        {
            var go = new GameObject("Location");
            created.Add(go);
            var content = go.AddComponent<LocationMapContent>();
            content.Configure(MapTestSupport.WideDefinition(created), landmarks);
            return content;
        }

        private static MapLandmark Landmark(string id, Transform anchor, string label = null) =>
            new MapLandmark { Id = id, Label = label, Anchor = anchor };

        [Test]
        public void ValidContentHasNoProblems()
        {
            var content = Content(Landmark("dock", Anchor(10f, 10f), "Dock"), Landmark("alehouse", Anchor(-20f, 5f)));
            var problems = new List<string>();
            Assert.IsTrue(content.Validate(problems), string.Join("; ", problems));
            Assert.DoesNotThrow(content.EnsureValid);
        }

        [Test]
        public void ContentWithNoLandmarksIsValid()
        {
            Assert.IsTrue(Content().Validate(new List<string>()));
        }

        [Test]
        public void MissingDefinitionIsReported()
        {
            var go = new GameObject("Location");
            created.Add(go);
            var content = go.AddComponent<LocationMapContent>();
            var problems = new List<string>();
            Assert.IsFalse(content.Validate(problems));
            Assert.That(problems, Has.Some.Contains("no map definition"));
        }

        [Test]
        public void InvalidDefinitionProblemsArePropagated()
        {
            var go = new GameObject("Location");
            created.Add(go);
            var content = go.AddComponent<LocationMapContent>();
            var texture = new Texture2D(8, 8);
            created.Add(texture);
            // Square image, non-square world rectangle: the unexpanded-bounds bake bug.
            var bad = LocationMapDefinition.Create("loc", "Loc", texture, MapTestSupport.WideBounds);
            created.Add(bad);
            content.Configure(bad, null);

            var problems = new List<string>();
            Assert.IsFalse(content.Validate(problems));
            Assert.That(problems, Has.Some.Contains("aspect"));
        }

        [Test]
        public void DuplicateLandmarkIdsAreRejected()
        {
            var content = Content(Landmark("dock", Anchor(0f, 0f)), Landmark("dock", Anchor(5f, 5f)));
            var problems = new List<string>();
            Assert.IsFalse(content.Validate(problems));
            Assert.That(problems, Has.Some.Contains("'dock' is used more than once"));
        }

        [Test]
        public void EmptyLandmarkIdIsRejected()
        {
            var content = Content(Landmark("", Anchor(0f, 0f), "Nameless"));
            var problems = new List<string>();
            Assert.IsFalse(content.Validate(problems));
            Assert.That(problems, Has.Some.Contains("no id"));
        }

        [Test]
        public void MissingAnchorIsAnAuthoringError()
        {
            var content = Content(Landmark("dock", null));
            var problems = new List<string>();
            Assert.IsFalse(content.Validate(problems));
            Assert.That(problems, Has.Some.Contains("no anchor"));
        }

        [Test]
        public void AnchorOutsideTheImageIsReportedNotSilentlyAbsorbed()
        {
            var content = Content(Landmark("far", Anchor(500f, 0f)));
            var problems = new List<string>();
            Assert.IsFalse(content.Validate(problems));
            Assert.That(problems, Has.Some.Contains("outside the map image"));
            Assert.AreEqual(MapTestSupport.WideBounds, content.Definition.TextureWorldBoundsXZ, "bounds must not be enlarged");
        }

        [Test]
        public void AnchorExactlyOnTheImageEdgeIsInside()
        {
            var content = Content(Landmark("edge", Anchor(100f, -50f)));
            Assert.IsTrue(content.Validate(new List<string>()));
        }

        [Test]
        public void AnchorHeightDoesNotAffectTheBoundsCheck()
        {
            var tall = Anchor(0f, 0f);
            tall.position = new Vector3(0f, 9000f, 0f);
            Assert.IsTrue(Content(Landmark("tower", tall)).Validate(new List<string>()));
        }

        [Test]
        public void EnsureValidThrowsListingEveryProblem()
        {
            var content = Content(Landmark("a", null), Landmark("a", Anchor(900f, 0f)));
            var ex = Assert.Throws<InvalidOperationException>(content.EnsureValid);
            StringAssert.Contains("no anchor", ex.Message);
            StringAssert.Contains("more than once", ex.Message);
        }

        [Test]
        public void DisplayLabelFallsBackToTheId()
        {
            Assert.AreEqual("Dock", Landmark("dock", null, "Dock").DisplayLabel);
            Assert.AreEqual("dock", Landmark("dock", null).DisplayLabel);
        }

        [Test]
        public void ValidatingDoesNotChangeTheAuthoredLandmarks()
        {
            var content = Content(Landmark("dock", Anchor(1f, 2f), "Dock"));
            content.Validate(new List<string>());
            Assert.AreEqual(1, content.Landmarks.Count);
            Assert.AreEqual("dock", content.Landmarks[0].Id);
        }
    }
}

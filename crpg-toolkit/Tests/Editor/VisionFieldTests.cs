using System.Linq;
using CRPG.Core;
using CRPG.Navigation;
using CRPG.Rules;
using CRPG.Rules.SRD5;
using CRPG.Actors;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CRPG.Tests.EditMode
{
    /// <summary>The navmesh projection of sight: the walkable cells of an area, which of them an observer watches, and the point sight query the cells ask.</summary>
    public sealed class VisionFieldTests
    {
        private static WalkableCells Floor(Rect area, float cellSize = 1f, System.Func<Vector3, bool> walkable = null) =>
            WalkableCells.Build(area, cellSize, 0f, centre => walkable == null || walkable(centre) ? centre : (Vector3?)null);

        [Test]
        public void TheCellsCoverTheAreaAndSkipWhatHasNoFloor()
        {
            var cells = Floor(new Rect(0f, 0f, 4f, 3f), walkable: c => !(c.x > 1f && c.x < 2f && c.z > 1f && c.z < 2f));
            Assert.That(cells.Columns, Is.EqualTo(4)); Assert.That(cells.Rows, Is.EqualTo(3)); Assert.That(cells.Count, Is.EqualTo(11), "twelve slots, one of them a pit");
            Assert.That(cells.IndexAt(new Vector3(0.2f, 0f, 0.2f)), Is.GreaterThanOrEqualTo(0));
            Assert.That(cells.IndexAt(new Vector3(1.5f, 0f, 1.5f)), Is.EqualTo(-1), "no floor");
            Assert.That(cells.IndexAt(new Vector3(9f, 0f, 9f)), Is.EqualTo(-1), "outside");
            Assert.That(cells.PointOf(cells.IndexAt(new Vector3(3.5f, 0f, 2.5f))), Is.EqualTo(new Vector3(3.5f, 0f, 2.5f)));
        }

        [Test]
        public void ARebuildMarksTheCellsTheQuestionSaysAreWatchedAndCountsThem()
        {
            var field = new VisionField(Floor(new Rect(0f, 0f, 10f, 1f)));
            Assert.That(field.Version, Is.Zero);
            field.Rebuild(p => p.x < 3f);
            Assert.That(field.Version, Is.EqualTo(1)); Assert.That(field.WatchedCount, Is.EqualTo(3));
            Assert.That(field.IsWatched(new Vector3(2.5f, 0f, 0.5f)), Is.True); Assert.That(field.IsWatched(new Vector3(3.5f, 0f, 0.5f)), Is.False);
            Assert.That(field.IsWatched(new Vector3(50f, 0f, 0f)), Is.False, "off the floor nobody stands, so nobody is watched there");
            field.Rebuild(p => false);
            Assert.That(field.WatchedCount, Is.Zero); Assert.That(field.Version, Is.EqualTo(2)); Assert.That(field.IsWatched(new Vector3(1.5f, 0f, 0.5f)), Is.False);
        }

        [Test]
        public void ThePointQueryUsesTheObserversSightAndTheLightAtThePoint()
        {
            var rules = new RulesDatabase();
            var provider = AssetDatabase.LoadAssetAtPath<SRD5RulesetProvider>("Packages/com.crpgtoolkit.core/Rules/SRD5e2014/Content/SRD5RulesetProvider.asset");
            rules.RegisterRange(provider.Content);
            var characters = new CharacterRegistry(rules);
            var observer = new CharacterState(RuntimeEntityId.NewId()); characters.Register(observer);
            var positions = new FakePositionProvider(); positions.Set(observer.Id, Vector3.zero);
            var sight = new FakeLineOfSightProvider();
            var dark = false;
            var light = new LambdaLight(p => dark && p.z > 5f ? LightLevel.Dark : LightLevel.Bright);
            var query = new Srd5PerceptionQuery(characters, rules, positions, sight, light: light);

            Assert.That(query.SightOfPoint(observer.Id, new Vector3(0f, 0f, 8f)), Is.EqualTo(Visibility.Clear));
            dark = true;
            Assert.That(query.SightOfPoint(observer.Id, new Vector3(0f, 0f, 8f)), Is.EqualTo(Visibility.HeavilyObscured));
            Assert.That(query.SightOfPoint(observer.Id, new Vector3(0f, 0f, 2f)), Is.EqualTo(Visibility.Clear));
            sight.AlwaysBlocked = true;
            Assert.That(query.SightOfPoint(observer.Id, new Vector3(0f, 0f, 2f)), Is.EqualTo(Visibility.Unseen));
            sight.AlwaysBlocked = false; observer.ApplyCondition(new ConditionInstance(new DefinitionId("condition-blinded"), observer.Id, observer.Id));
            Assert.That(query.SightOfPoint(observer.Id, new Vector3(0f, 0f, 2f)), Is.EqualTo(Visibility.Unseen), "a blind observer watches nothing");

            // the whole picture: the cells the observer sees clearly
            observer.RemoveConditions(c => true); dark = true;
            var field = new VisionField(Floor(new Rect(-5f, 0f, 10f, 12f)));
            field.Rebuild(p => Srd5PerceptionQuery.Sees(query.SightOfPoint(observer.Id, p)));
            Assert.That(field.IsWatched(new Vector3(0.5f, 0f, 3.5f)), Is.True, "lit and in front");
            Assert.That(field.IsWatched(new Vector3(0.5f, 0f, 9.5f)), Is.False, "in the dark patch: not watched, a place to hide");
            Assert.That(Enumerable.Range(0, field.Cells.Count).Count(field.IsWatchedCell), Is.EqualTo(field.WatchedCount));
        }

        private sealed class LambdaLight : ILightProvider
        {
            private readonly System.Func<Vector3, LightLevel> level;
            public LambdaLight(System.Func<Vector3, LightLevel> level) { this.level = level; }
            public LightLevel LightAt(Vector3 point) => level(point);
        }
    }
}

using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VikingRaid.Game;

namespace VikingRaid.Tests.EditMode
{
    /// <summary>The world is authored from ~70 hand-picked Synty prefabs. Every path in <see cref="WorldAssets"/> must load as a prefab, so a rename or an incomplete
    /// import fails here rather than as a null reference halfway through authoring.</summary>
    public class WorldAssetsTests
    {
        [Test]
        public void EveryWorldAssetPathLoadsAsAPrefab()
        {
            var paths = WorldAssets.All.ToList();
            Assert.Greater(paths.Count, 60, "the table looks empty");
            var missing = paths.Where(p => AssetDatabase.LoadAssetAtPath<GameObject>(p) == null).ToList();
            Assert.IsEmpty(missing, "missing prefabs:\n" + string.Join("\n", missing));
        }

        [Test]
        public void NoPathIsListedTwice()
        {
            var paths = WorldAssets.All.ToList();
            CollectionAssert.AreEquivalent(paths.Distinct().ToList(), paths, "a prefab is listed in two roles");
        }
    }
}

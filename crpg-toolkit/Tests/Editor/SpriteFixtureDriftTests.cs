using System.Collections.Generic;
using System.Linq;
using CRPG.Editor.Character;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CRPG.Tests.EditMode
{
    /// <summary>
    /// 2D campaign Task 2.5: the PlayMode suite loads checked-in sprite character fixtures (a PlayMode assembly cannot run the Editor
    /// builder). This guard rebuilds the wrapper and the card with the same spec and fails if the checked-in copies no longer have the
    /// same hierarchy and components, so a builder change cannot silently leave the fixtures stale.
    /// </summary>
    public class SpriteFixtureDriftTests
    {
        private const string ScratchFolder = "Assets/Temp/SpriteFixtureDriftTests";
        private const string CharacterFixture = "Assets/Resources/Fixtures/SpriteCharacter.prefab";
        private const string CardFixture = "Assets/Resources/Fixtures/SpriteCard.prefab";

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(ScratchFolder)) AssetDatabase.DeleteAsset(ScratchFolder);
            const string parent = "Assets/Temp";
            if (AssetDatabase.IsValidFolder(parent) && AssetDatabase.FindAssets(string.Empty, new[] { parent }).Length == 0) AssetDatabase.DeleteAsset(parent);
        }

        private static CharacterSetupSpec FixtureSpec() => new() { Movement = MovementBackend.Grid, Presentation = PresentationKind.Sprite };

        /// <summary>Hierarchy path + component type names for every object, in a stable order.</summary>
        private static List<string> Signature(GameObject root)
        {
            var lines = new List<string>();
            void Walk(Transform t, string path)
            {
                var here = path.Length == 0 ? "<root>" : path + "/" + t.name; // the root takes its asset name, which legitimately differs
                var components = t.GetComponents<Component>().Where(c => c != null).Select(c => c.GetType().Name).OrderBy(n => n);
                lines.Add(here + " [" + string.Join(",", components) + "]");
                foreach (Transform child in t) Walk(child, here);
            }

            Walk(root.transform, string.Empty);
            return lines;
        }

        [Test]
        public void TheCheckedInWrapperFixtureMatchesAFreshBuild()
        {
            var fixture = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterFixture);
            Assert.IsNotNull(fixture, $"{CharacterFixture} is missing -- run 'CRPG Toolkit/Characters/Author Sprite Character Test Fixtures'.");
            Directory_Ensure();
            var fresh = CharacterWrapperBuilder.BuildOrUpdate(FixtureSpec(), ScratchFolder + "/Fresh.prefab");

            CollectionAssert.AreEqual(Signature(fresh), Signature(fixture), "the fixture is stale: re-run the fixture author menu item");
        }

        [Test]
        public void TheCheckedInCardFixtureMatchesAFreshCard()
        {
            var fixture = AssetDatabase.LoadAssetAtPath<GameObject>(CardFixture);
            Assert.IsNotNull(fixture, $"{CardFixture} is missing -- run 'CRPG Toolkit/Characters/Author Sprite Character Test Fixtures'.");
            var card = SpriteCardBuilder.Build(FixtureSpec());
            try { CollectionAssert.AreEqual(Signature(card), Signature(fixture), "the card fixture is stale: re-run the fixture author menu item"); }
            finally { Object.DestroyImmediate(card); }
        }

        private static void Directory_Ensure()
        {
            if (AssetDatabase.IsValidFolder(ScratchFolder)) return;
            if (!AssetDatabase.IsValidFolder("Assets/Temp")) AssetDatabase.CreateFolder("Assets", "Temp");
            AssetDatabase.CreateFolder("Assets/Temp", "SpriteFixtureDriftTests");
        }
    }
}

using System.Linq;
using BarrowsEnd.Editor;
using BarrowsEnd.Shell;
using CRPG.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace BarrowsEnd.Tests
{
    /// <summary>Task 4.2: the creator's panel settings and dev scene are authored idempotently and configured exactly as the pixel-exact preview needs.</summary>
    public class CreatorScreenAuthoringTests
    {
        [Test]
        public void ThePanelSettingsCarryTheThemeAllAtlasFiltersAndConstantPixelScaleOne()
        {
            var settings = BarrowsEndCreatorAuthor.AuthorPanelSettings();

            Assert.IsNotNull(settings.themeStyleSheet, "the runtime theme is assigned explicitly");
            Assert.AreEqual(PanelScaleMode.ConstantPixelSize, settings.scaleMode, "fractional panel scaling would blur the integer-scaled sprites");
            Assert.AreEqual(1f, settings.scale);
            Assert.AreEqual(BarrowsEndCreatorAuthor.AllAtlasFilters, settings.dynamicAtlasSettings.activeFilters, "no atlas re-filtering of the sprite textures");
        }

        [Test]
        public void AuthoringTwiceKeepsTheGuidAndRestoresAnyHandEditedSetting()
        {
            var first = BarrowsEndCreatorAuthor.AuthorPanelSettings();
            var guid = AssetDatabase.AssetPathToGUID(BarrowsEndCreatorAuthor.PanelSettingsPath);

            first.scaleMode = PanelScaleMode.ScaleWithScreenSize; // a hand edit (or a Unity default) must not survive the next authoring run
            first.scale = 2f;
            EditorUtility.SetDirty(first);
            AssetDatabase.SaveAssets();
            var second = BarrowsEndCreatorAuthor.AuthorPanelSettings();

            Assert.AreEqual(guid, AssetDatabase.AssetPathToGUID(BarrowsEndCreatorAuthor.PanelSettingsPath), "asset GUID is stable");
            Assert.AreEqual(PanelScaleMode.ConstantPixelSize, second.scaleMode);
            Assert.AreEqual(1f, second.scale);
        }

        [Test]
        public void TheDevSceneHostsTheScreenWiredToTheRealContentAndAuthorsIdempotently()
        {
            BarrowsEndCreatorAuthor.Author();
            var guid = AssetDatabase.AssetPathToGUID(BarrowsEndCreatorAuthor.ScenePath);
            BarrowsEndCreatorAuthor.Author();
            Assert.AreEqual(guid, AssetDatabase.AssetPathToGUID(BarrowsEndCreatorAuthor.ScenePath), "scene GUID is stable");

            var scene = EditorSceneManager.OpenScene(BarrowsEndCreatorAuthor.ScenePath, OpenSceneMode.Additive);
            try
            {
                var roots = scene.GetRootGameObjects();
                var document = roots.SelectMany(r => r.GetComponentsInChildren<UIDocument>(true)).Single();
                var screen = roots.SelectMany(r => r.GetComponentsInChildren<CharacterCreatorScreen>(true)).Single();
                var bootstrap = roots.SelectMany(r => r.GetComponentsInChildren<CreatorPreviewBootstrap>(true)).Single();

                Assert.AreSame(AssetDatabase.LoadAssetAtPath<PanelSettings>(BarrowsEndCreatorAuthor.PanelSettingsPath), document.panelSettings);
                var so = new SerializedObject(bootstrap);
                Assert.IsNotNull(so.FindProperty("ruleset").objectReferenceValue, "ruleset");
                Assert.IsNotNull(so.FindProperty("picks").objectReferenceValue, "picks TextAsset");
                Assert.IsNotNull(so.FindProperty("classTable").objectReferenceValue, "class table TextAsset");
                Assert.AreSame(screen, so.FindProperty("screen").objectReferenceValue);
                Assert.AreSame(document.gameObject, screen.gameObject, "the screen sits on the UIDocument's object");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}

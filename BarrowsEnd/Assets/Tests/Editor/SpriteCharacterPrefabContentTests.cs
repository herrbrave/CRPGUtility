using System.Linq;
using BarrowsEnd.Editor;
using CRPG.Gameplay;
using CRPG.Navigation;
using CRPG.Navigation.Grid;
using CRPG.Presentation;
using CRPG.Presentation.Equipment;
using CRPG.Presentation.Sprites;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BarrowsEnd.Tests
{
    /// <summary>Task 2.5: the campaign's base sprite character prefab, built through the toolkit's CharacterWrapperBuilder.</summary>
    public class SpriteCharacterPrefabContentTests
    {
        private GameObject prefab;

        [OneTimeSetUp]
        public void Author()
        {
            BarrowsEndCharacterAuthor.Author();
            prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BarrowsEndCharacterAuthor.SpriteCharacterPath);
        }

        [Test]
        public void ThePrefabExistsWithExactlyOneGridMoverAndTheSpriteComponentSet()
        {
            Assert.IsNotNull(prefab);
            Assert.AreEqual(1, prefab.GetComponentsInChildren<MonoBehaviour>(true).Count(m => m is IMovable));
            Assert.IsNotNull(prefab.GetComponent<GridCharacterMotor>());
            Assert.IsNotNull(prefab.GetComponent<CharacterAgent>());
            Assert.IsNotNull(prefab.GetComponent<FacingController>());
            Assert.IsNotNull(prefab.GetComponent<SpriteEquipmentBinder>());
            Assert.IsNotNull(prefab.GetComponent<SpriteAppearanceBinder>());
            Assert.IsNull(prefab.GetComponentInChildren<UnityEngine.AI.NavMeshAgent>(true));
            Assert.IsNull(prefab.GetComponentInChildren<CRPG.Presentation.Animation.CharacterPlayableAnimator>(true));
            Assert.IsNull(prefab.GetComponent<EquipmentVisualBinder>());

            var box = prefab.GetComponent<BoxCollider>();
            Assert.AreEqual(new Vector3(0.7f, 1f, 0.7f), box.size);
        }

        [Test]
        public void TheCardIsVisualRootsFirstChildAndTheLayerChildrenMatchTheCompositorSlots()
        {
            var visualRoot = prefab.transform.Find("VisualRoot");
            Assert.IsNotNull(visualRoot.GetComponent<CharacterView>());
            Assert.AreEqual("Model", visualRoot.GetChild(0).name);

            var layers = prefab.transform.Find("VisualRoot/Model/RockPivot/Layers");
            var expected = SpriteLayerCompositor.DefaultSlots().Select(s => "Layer_" + s.Value);
            CollectionAssert.AreEqual(expected, layers.Cast<Transform>().Select(t => t.name));
            Assert.AreEqual("World", layers.GetComponent<SpriteLayerCompositor>().SortingLayerName);
        }

        [Test]
        public void RebuildingKeepsTheAssetGuid()
        {
            var guid = AssetDatabase.AssetPathToGUID(BarrowsEndCharacterAuthor.SpriteCharacterPath);

            BarrowsEndCharacterAuthor.Author();

            Assert.AreEqual(guid, AssetDatabase.AssetPathToGUID(BarrowsEndCharacterAuthor.SpriteCharacterPath));
            Assert.AreEqual(1, AssetDatabase.LoadAssetAtPath<GameObject>(BarrowsEndCharacterAuthor.SpriteCharacterPath).GetComponents<GridCharacterMotor>().Length);
        }
    }
}

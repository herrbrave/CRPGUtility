using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using CRPG.Core;
using CRPG.Presentation;
using CRPG.Presentation.Sprites;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CRPG.Tests.PlayMode
{
    /// <summary>2D campaign Task 2.2: SpriteDepthSorter -- anchor-based y-sort of characters and props.</summary>
    public class SpriteDepthSorterTests
    {
        private readonly List<Object> made = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in made) if (o != null) Object.DestroyImmediate(o);
            made.Clear();
        }

        private Sprite NewSprite(string name = "s")
        {
            var texture = new Texture2D(4, 4) { name = name };
            var sprite = Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
            sprite.name = name;
            made.Add(texture);
            made.Add(sprite);
            return sprite;
        }

        private GameObject Make(string name, Transform parent = null)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            made.Add(go);
            return go;
        }

        private static void SetField(object target, string field, object value) =>
            target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

        private sealed class Character
        {
            public Transform Root, VisualRoot, Card;
            public SpriteLayerCompositor Compositor;
            public SpriteDepthSorter Sorter;
        }

        /// <summary>CharacterRoot -> VisualRoot (CharacterView) -> Card (compositor + sorter), the wrapper hierarchy.</summary>
        private Character MakeCharacter(string name, Vector3 position, int slotCount = 0)
        {
            var root = Make(name);
            root.transform.position = position;
            var visual = Make("VisualRoot", root.transform);
            visual.AddComponent<CharacterView>();
            var card = Make("Card", visual.transform);
            card.SetActive(false);
            var compositor = card.AddComponent<SpriteLayerCompositor>();
            if (slotCount > 0)
                SetField(compositor, "slots", Enumerable.Range(0, slotCount).Select(i => new DefinitionId("s" + i)).ToList());
            var sorter = card.AddComponent<SpriteDepthSorter>();
            card.SetActive(true);
            return new Character { Root = root.transform, VisualRoot = visual.transform, Card = card.transform, Compositor = compositor, Sorter = sorter };
        }

        private static int MinOrder(Character c) => c.Compositor.Renderers.Min(r => r.sortingOrder);
        private static int MaxOrder(Character c) => c.Compositor.Renderers.Max(r => r.sortingOrder);

        private Sprite Prop(string name, Vector3 position, out SpriteRenderer renderer, Transform anchor = null)
        {
            var go = Make(name);
            go.transform.position = position;
            renderer = go.AddComponent<SpriteRenderer>();
            var sprite = NewSprite(name);
            renderer.sprite = sprite;
            var sorter = go.AddComponent<SpriteDepthSorter>();
            if (anchor != null) sorter.Anchor = anchor;
            return sprite;
        }

        [UnityTest]
        public IEnumerator TheLowerZCharacterDrawsInFrontAndMovingFlipsItByTheNextLateUpdate()
        {
            var front = MakeCharacter("Front", new Vector3(0f, 0f, 3f));
            var back = MakeCharacter("Back", new Vector3(1f, 0f, 4f));
            yield return null;

            Assert.Greater(MinOrder(front), MaxOrder(back), "lower Z is in front");

            front.Root.position = new Vector3(0f, 0f, 5f); // now behind
            yield return null;
            Assert.Greater(MinOrder(back), MaxOrder(front), "the flip lands by the next LateUpdate");
        }

        [UnityTest]
        public IEnumerator TheCardsOwnOffsetAndTheRockCannotChangeTheOrder()
        {
            var c = MakeCharacter("Char", new Vector3(0f, 0f, 3f));
            yield return null;
            var before = c.Compositor.Renderers.Select(r => r.sortingOrder).ToArray();

            c.Card.position += new Vector3(0f, 0.02f, 5f); // the card moves in world Z at pitch 90 (offset, rock, hop)
            c.Card.rotation = Quaternion.Euler(90f, 0f, 20f);
            yield return null;

            CollectionAssert.AreEqual(before, c.Compositor.Renderers.Select(r => r.sortingOrder).ToArray());
        }

        [UnityTest]
        public IEnumerator TheAnchorIsTheCharacterViewsParentWhereverTheSorterSits()
        {
            var expected = SpriteSortingContract.BaseOrder(7f);

            // On the card (default), on VisualRoot (the view itself), and on CharacterRoot (no view above it).
            var onCard = MakeCharacter("OnCard", new Vector3(0f, 0f, 7f));

            var onVisual = Make("OnVisualRoot");
            onVisual.transform.position = new Vector3(2f, 0f, 7f);
            var visual = Make("VisualRoot", onVisual.transform);
            visual.AddComponent<CharacterView>();
            var visualCompositor = Make("Layers", visual.transform).AddComponent<SpriteLayerCompositor>();
            visual.AddComponent<SpriteDepthSorter>();

            var onRoot = Make("OnRoot");
            onRoot.transform.position = new Vector3(4f, 0f, 7f);
            var rootChild = Make("Layers", Make("VisualRoot", onRoot.transform).transform).AddComponent<SpriteLayerCompositor>();
            onRoot.AddComponent<SpriteDepthSorter>();

            yield return null;

            Assert.AreEqual(expected, onCard.Compositor.SortingBase);
            Assert.AreEqual(expected, visualCompositor.SortingBase);
            Assert.AreEqual(expected, rootChild.SortingBase);
        }

        [UnityTest]
        public IEnumerator AnExplicitAnchorOverridesAndADestroyedOneFallsBackWithoutThrowing()
        {
            var c = MakeCharacter("Char", new Vector3(0f, 0f, 3f));
            var anchor = Make("Anchor");
            anchor.transform.position = new Vector3(0f, 0f, 9f);
            c.Sorter.Anchor = anchor.transform;
            yield return null;
            Assert.AreEqual(SpriteSortingContract.BaseOrder(9f), c.Compositor.SortingBase);

            Object.DestroyImmediate(anchor);
            yield return null;
            Assert.AreEqual(SpriteSortingContract.BaseOrder(3f), c.Compositor.SortingBase, "back to the CharacterRoot anchor");
        }

        [UnityTest]
        public IEnumerator ALoneSpriteRendererPropUsesItsOwnTransformAndTheLaneAndSitsBetweenRows()
        {
            var prop = Prop("Crate", new Vector3(3f, 0f, 5.5f), out var crate);
            var sameRow = MakeCharacter("SameRow", new Vector3(5f, 0f, 5.5f));
            var rowBehind = MakeCharacter("RowBehind", new Vector3(5f, 0f, 6.5f), SpriteSortingContract.MaxSlotsPerObject);
            var rowInFront = MakeCharacter("RowInFront", new Vector3(5f, 0f, 4.5f));
            yield return null;

            Assert.AreEqual(SpriteSortingContract.OrderFor(SpriteSortingContract.BaseOrder(5.5f), SpriteSortingContract.PropBias), crate.sortingOrder);
            Assert.Less(crate.sortingOrder, MinOrder(sameRow), "a prop draws behind a character in its own row");
            Assert.Greater(crate.sortingOrder, MaxOrder(rowBehind), "and in front of EVERY layer of a full 15-slot character in the row behind");
            Assert.Less(crate.sortingOrder, MinOrder(rowInFront), "and behind the row in front");
            Assert.IsNotNull(prop);
        }

        [UnityTest]
        public IEnumerator ATallPropAuthoredAsOneSpritePerCellSortsAgainstACharacterBetweenAndBeside()
        {
            // The approved spike's two-cell pillar: two one-cell sprites at Z 6.5 (upper cell) and 5.5 (lower cell).
            Prop("PillarTop", new Vector3(6.5f, 0f, 6.5f), out var top);
            Prop("PillarBottom", new Vector3(6.5f, 0f, 5.5f), out var bottom);
            var between = MakeCharacter("Between", new Vector3(6.5f, 0f, 6.0f));
            var besideTop = MakeCharacter("BesideTop", new Vector3(7.5f, 0f, 6.5f));
            yield return null;

            Assert.Less(top.sortingOrder, MinOrder(between), "the character is in front of the upper (further) cell");
            Assert.Greater(bottom.sortingOrder, MaxOrder(between), "and behind the lower (nearer) cell");
            Assert.Less(top.sortingOrder, MinOrder(besideTop), "a same-row character is in front of that row's prop");
        }

        [UnityTest]
        public IEnumerator ATallPropWithAnExplicitFootprintAnchorSortsByThatRow()
        {
            var anchor = Make("Footprint");
            anchor.transform.position = new Vector3(0f, 0f, 5.5f);
            Prop("Tall", new Vector3(0f, 0f, 6.0f), out var tall, anchor.transform); // sprite centre is a half cell higher
            yield return null;

            Assert.AreEqual(SpriteSortingContract.OrderFor(SpriteSortingContract.BaseOrder(5.5f), SpriteSortingContract.PropBias), tall.sortingOrder);
        }

        [UnityTest]
        public IEnumerator AnExtremeZWarnsOnceAndStaysInsideInt16ForCharactersAndProps()
        {
            var c = MakeCharacter("Far", new Vector3(0f, 0f, 1e6f));
            Prop("FarProp", new Vector3(0f, 0f, -1e6f), out var prop);
            LogAssert.Expect(LogType.Warning, new Regex("outside the sortable range"));
            LogAssert.Expect(LogType.Warning, new Regex("outside the sortable range"));
            yield return null;
            yield return null;
            yield return null;

            LogAssert.NoUnexpectedReceived(); // one warning per sorter, never per frame
            Assert.GreaterOrEqual(MinOrder(c), short.MinValue);
            Assert.LessOrEqual(MaxOrder(c), short.MaxValue);
            Assert.GreaterOrEqual(prop.sortingOrder, short.MinValue);
            Assert.LessOrEqual(prop.sortingOrder, short.MaxValue);
        }

        [UnityTest]
        public IEnumerator AStateChangeAndAMoveOnTheSameFrameLandTogether()
        {
            var c = MakeCharacter("Char", new Vector3(0f, 0f, 3f));
            yield return null;

            c.Compositor.State.SetAppearance(new DefinitionId("layer-body"), new SpriteOption(NewSprite(), Color.white));
            c.Root.position = new Vector3(0f, 0f, 8f);
            yield return null;

            var expectedBase = SpriteSortingContract.BaseOrder(8f);
            for (var i = 0; i < c.Compositor.Renderers.Count; i++)
                Assert.AreEqual(SpriteSortingContract.SlotOrder(expectedBase, i), c.Compositor.Renderers[i].sortingOrder);
            Assert.IsNotNull(c.Compositor.Renderers[0].sprite, "and the state change was applied that same frame");
        }

        [UnityTest]
        public IEnumerator AMainCameraYawOffZeroWarnsOnceAndYawZeroDoesNot()
        {
            var camGo = Make("Main Camera");
            camGo.tag = "MainCamera";
            camGo.AddComponent<UnityEngine.Camera>(); // CRPG.Camera is a namespace
            camGo.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            MakeCharacter("Char", Vector3.zero);
            yield return null;
            yield return null;
            LogAssert.NoUnexpectedReceived();

            camGo.transform.rotation = Quaternion.Euler(90f, 45f, 0f);
            var second = MakeCharacter("Second", Vector3.zero);
            LogAssert.Expect(LogType.Warning, new Regex("yaw is 45"));
            yield return null;
            yield return null;
            LogAssert.NoUnexpectedReceived();
            Assert.IsNotNull(second);
        }
    }
}

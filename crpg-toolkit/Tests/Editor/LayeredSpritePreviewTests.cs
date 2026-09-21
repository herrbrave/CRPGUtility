using System.Collections.Generic;
using CRPG.Core;
using CRPG.Presentation.Creator;
using CRPG.Presentation.Sprites;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace CRPG.Tests.EditMode
{
    /// <summary>
    /// 2D campaign Task 4.1: the structure of <see cref="LayeredSpritePreview"/>. There is no panel or layout pass in EditMode, so every
    /// assertion reads the INLINE <c>style.*</c> values, never <c>resolvedStyle</c>. Real rendering is proven by the BarrowsEnd PlayMode spike.
    /// </summary>
    public class LayeredSpritePreviewTests
    {
        private static readonly DefinitionId Body = new("layer-body");
        private static readonly DefinitionId Hair = new("layer-hair");
        private static readonly DefinitionId Weapon = new("layer-weapon");

        private readonly List<Object> made = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in made) if (o != null) Object.DestroyImmediate(o);
            made.Clear();
        }

        private Sprite NewSprite(string name)
        {
            var texture = new Texture2D(16, 16) { name = name };
            var sprite = Sprite.Create(texture, new Rect(0, 0, 16, 16), new Vector2(0.5f, 0.5f), 16f);
            made.Add(texture);
            made.Add(sprite);
            return sprite;
        }

        private static SpriteLayerState NewState() => new(new[] { Body, Hair, Weapon });

        [Test]
        public void BindingCreatesOneImagePerSlotInDrawOrderFillingTheSquare()
        {
            var preview = new LayeredSpritePreview();
            preview.Bind(NewState());

            Assert.AreEqual(3, preview.LayerImages.Count);
            Assert.AreEqual(3, preview.childCount);
            for (var i = 0; i < 3; i++)
            {
                Assert.AreSame(preview.LayerImages[i], preview[i], "children are in slot (draw) order");
                Assert.AreEqual(Position.Absolute, preview.LayerImages[i].style.position.value);
                Assert.AreEqual(ScaleMode.StretchToFill, preview.LayerImages[i].scaleMode, "explicit, so a tight-mesh sprite still maps 1:1");
                Assert.AreEqual(PickingMode.Ignore, preview.LayerImages[i].pickingMode);
            }
        }

        [Test]
        public void RefreshMapsSpriteTintAndVisibilityFromTheState()
        {
            var body = NewSprite("body");
            var weapon = NewSprite("weapon");
            var state = NewState();
            state.SetAppearance(Body, new SpriteOption(body, new Color(0.5f, 0.6f, 0.7f, 1f)));
            state.SetEquipment(new DefinitionId("slot-mainhand"), 1, new[] { new LayerPaint(Weapon, new SpriteOption(weapon, Color.red)) }, new[] { Hair });
            var preview = new LayeredSpritePreview();
            preview.Bind(state); // Bind refreshes

            Assert.AreSame(body, preview.LayerImages[0].sprite);
            Assert.AreEqual(new Color(0.5f, 0.6f, 0.7f, 1f), preview.LayerImages[0].tintColor);
            Assert.AreEqual(DisplayStyle.Flex, preview.LayerImages[0].style.display.value);

            Assert.AreEqual(DisplayStyle.None, preview.LayerImages[1].style.display.value, "no sprite: hidden");
            Assert.AreSame(weapon, preview.LayerImages[2].sprite);
            Assert.AreEqual(Color.red, preview.LayerImages[2].tintColor);
        }

        [Test]
        public void AHiddenLayerIsNotDisplayedEvenWithAnAppearanceSprite()
        {
            var state = NewState();
            state.SetAppearance(Hair, new SpriteOption(NewSprite("hair"), Color.white));
            var preview = new LayeredSpritePreview();
            preview.Bind(state);
            Assert.AreEqual(DisplayStyle.Flex, preview.LayerImages[1].style.display.value);

            state.SetEquipment(new DefinitionId("slot-head"), 1, null, new[] { Hair });
            preview.Refresh();

            Assert.AreEqual(DisplayStyle.None, preview.LayerImages[1].style.display.value);
        }

        [Test]
        public void RefreshFollowsTheStateAfterItChangesButOnlyWhenTheVersionMoved()
        {
            var first = NewSprite("first");
            var second = NewSprite("second");
            var state = NewState();
            state.SetAppearance(Body, new SpriteOption(first, Color.white));
            var preview = new LayeredSpritePreview();
            preview.Bind(state);

            state.SetAppearance(Body, new SpriteOption(second, Color.white));
            Assert.AreSame(first, preview.LayerImages[0].sprite, "nothing is applied until Refresh");
            preview.Refresh();
            Assert.AreSame(second, preview.LayerImages[0].sprite);

            preview.LayerImages[0].sprite = first; // tamper: an unchanged state must not be re-applied
            preview.Refresh();
            Assert.AreSame(first, preview.LayerImages[0].sprite, "same Version, no re-apply");
        }

        [Test]
        public void ScaleSetsTheSquaresPixelSizeAndReSizesImmediatelyEvenAfterBind()
        {
            var preview = new LayeredSpritePreview();
            Assert.AreEqual(4, preview.Scale, "default");
            Assert.AreEqual(16, preview.CanvasPixels);
            Assert.AreEqual(64f, preview.style.width.value.value);
            Assert.AreEqual(64f, preview.style.height.value.value);

            preview.Bind(NewState());
            preview.Scale = 3;
            Assert.AreEqual(48f, preview.style.width.value.value);
            Assert.AreEqual(48f, preview.style.height.value.value);

            preview.Scale = 0;
            Assert.AreEqual(1, preview.Scale, "clamped: a fractional or zero size would blur the pixels");
            Assert.AreEqual(16f, preview.style.width.value.value);

            preview.CanvasPixels = 32;
            preview.Scale = 2;
            Assert.AreEqual(64f, preview.style.width.value.value);
        }

        [Test]
        public void RebindingReplacesTheImagesAndBindingNullClearsThem()
        {
            var preview = new LayeredSpritePreview();
            preview.Bind(NewState());
            preview.Bind(new SpriteLayerState(new[] { Body, Hair }));
            Assert.AreEqual(2, preview.LayerImages.Count);
            Assert.AreEqual(2, preview.childCount, "the old images are gone, not accumulated");

            preview.Bind(null);
            Assert.AreEqual(0, preview.LayerImages.Count);
            Assert.AreEqual(0, preview.childCount);
            Assert.DoesNotThrow(() => preview.Refresh());
        }
    }
}

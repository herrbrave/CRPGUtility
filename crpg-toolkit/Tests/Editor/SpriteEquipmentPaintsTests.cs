using System.Collections.Generic;
using CRPG.Core;
using CRPG.Inventory;
using CRPG.Presentation.Equipment;
using CRPG.Presentation.Sprites;
using NUnit.Framework;
using UnityEngine;

namespace CRPG.Tests.EditMode
{
    /// <summary>2D campaign Task 4.1: the item -> visual profile -> layer paints resolution shared by SpriteEquipmentBinder (the world) and the creator preview.</summary>
    public class SpriteEquipmentPaintsTests
    {
        private static readonly DefinitionId Weapon = new("layer-weapon");
        private static readonly DefinitionId Hair = new("layer-hair");

        private readonly List<Object> made = new();
        private RulesDatabase rules;

        [SetUp]
        public void SetUp() => rules = new RulesDatabase();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in made) if (o != null) Object.DestroyImmediate(o);
            made.Clear();
        }

        private Sprite NewSprite(string name)
        {
            var texture = new Texture2D(4, 4) { name = name };
            var sprite = Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
            made.Add(texture);
            made.Add(sprite);
            return sprite;
        }

        private EquipmentVisualProfile Profile(string id, EquipmentVisualKind kind, int priority = 0, IEnumerable<SpriteLayerEntry> layers = null, IEnumerable<DefinitionId> hidden = null)
        {
            var profile = ScriptableObject.CreateInstance<EquipmentVisualProfile>();
            made.Add(profile);
            TestDefinitionFactory.SetPrivateField(profile, "id", id);
            TestDefinitionFactory.SetPrivateField(profile, "kind", kind);
            TestDefinitionFactory.SetPrivateField(profile, "spritePriority", priority);
            TestDefinitionFactory.SetPrivateField(profile, "spriteLayers", new List<SpriteLayerEntry>(layers ?? new SpriteLayerEntry[0]));
            TestDefinitionFactory.SetPrivateField(profile, "hiddenLayerSlots", new List<DefinitionId>(hidden ?? new DefinitionId[0]));
            rules.Register(profile);
            return profile;
        }

        private ItemDefinition Item(string id, EquipmentVisualProfile profile)
        {
            var item = TestDefinitionFactory.CreateItemDefinition(id, id, equipmentSlotId: new DefinitionId("slot-mainhand"));
            made.Add(item);
            if (profile != null) TestDefinitionFactory.SetPrivateField(item, "equipmentVisualProfileId", profile.Id);
            rules.Register(item);
            return item;
        }

        [Test]
        public void ASpriteProfilePaintsItsLayersAndReturnsTheProfileForItsPriorityAndHides()
        {
            var sprite = NewSprite("sword");
            var profile = Profile("visual-sword", EquipmentVisualKind.Sprite, 7, new[] { new SpriteLayerEntry(Weapon, sprite, Color.red) }, new[] { Hair });
            var item = Item("item-sword", profile);
            var paints = new List<LayerPaint> { new(Hair, default) }; // stale content must be cleared

            Assert.IsTrue(SpriteEquipmentPaints.TryResolveSpritePaints(rules, item.Id, paints, out var resolved));

            Assert.AreSame(profile, resolved);
            Assert.AreEqual(1, paints.Count, "the list is cleared first");
            Assert.AreEqual(Weapon, paints[0].Slot);
            Assert.AreSame(sprite, paints[0].Option.Sprite);
            Assert.AreEqual(Color.red, paints[0].Option.Tint);
            Assert.AreEqual(7, resolved.SpritePriority);
            CollectionAssert.AreEqual(new[] { Hair }, new List<DefinitionId>(resolved.HiddenLayerSlots));
        }

        [Test]
        public void ANullLayerEntryIsSkipped()
        {
            var profile = Profile("visual-x", EquipmentVisualKind.Sprite, 0, new SpriteLayerEntry[] { null, new(Weapon, NewSprite("s"), Color.white) });
            var item = Item("item-x", profile);
            var paints = new List<LayerPaint>();

            Assert.IsTrue(SpriteEquipmentPaints.TryResolveSpritePaints(rules, item.Id, paints, out _));
            Assert.AreEqual(1, paints.Count);
        }

        [TestCase(EquipmentVisualKind.None)]
        [TestCase(EquipmentVisualKind.Rigid)]
        [TestCase(EquipmentVisualKind.Skinned)]
        public void ANonSpriteProfileClearsTheSlotButStillReturnsTheProfile(EquipmentVisualKind kind)
        {
            var item = Item("item-y", Profile("visual-y", kind));
            var paints = new List<LayerPaint>();

            Assert.IsFalse(SpriteEquipmentPaints.TryResolveSpritePaints(rules, item.Id, paints, out var profile));
            Assert.IsNotNull(profile, "a caller can tell 'a 3D profile on a sprite character' (warn) from 'no visual' (quiet)");
            Assert.AreEqual(kind, profile.Kind);
            Assert.IsEmpty(paints);
        }

        [Test]
        public void AnItemWithNoProfileOrAnUnknownItemClearsTheSlotWithANullProfile()
        {
            var bare = Item("item-bare", null);
            var paints = new List<LayerPaint>();

            Assert.IsFalse(SpriteEquipmentPaints.TryResolveSpritePaints(rules, bare.Id, paints, out var profile));
            Assert.IsNull(profile);

            Assert.IsFalse(SpriteEquipmentPaints.TryResolveSpritePaints(rules, new DefinitionId("item-nope"), paints, out profile));
            Assert.IsNull(profile);
        }
    }
}

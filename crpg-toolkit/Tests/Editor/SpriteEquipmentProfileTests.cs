using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CRPG.Core;
using CRPG.Editor.Equipment;
using CRPG.Editor.Validation;
using CRPG.Presentation.Equipment;
using NUnit.Framework;
using UnityEngine;

namespace CRPG.Tests.EditMode
{
    /// <summary>2D campaign Task 2.3: the sprite data on EquipmentVisualProfile, its custom-inspector relevance rule, and the
    /// sprite profile validator.</summary>
    public class SpriteEquipmentProfileTests
    {
        private static readonly DefinitionId Weapon = new("layer-weapon");
        private static readonly DefinitionId Hair = new("layer-hair");
        private static readonly DefinitionId Helmet = new("layer-helmet");

        private readonly List<Object> made = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in made) if (o != null) Object.DestroyImmediate(o);
            made.Clear();
        }

        private static void SetField(object target, string fieldName, object value)
        {
            var type = target.GetType();
            while (type != null)
            {
                var field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (field != null) { field.SetValue(target, value); return; }
                type = type.BaseType;
            }

            throw new System.Exception($"Field '{fieldName}' not found on {target.GetType().Name}.");
        }

        private Sprite NewSprite()
        {
            var texture = new Texture2D(4, 4);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
            made.Add(texture);
            made.Add(sprite);
            return sprite;
        }

        private EquipmentVisualProfile Profile(EquipmentVisualKind kind, IEnumerable<SpriteLayerEntry> layers = null, IEnumerable<DefinitionId> hidden = null)
        {
            var profile = ScriptableObject.CreateInstance<EquipmentVisualProfile>();
            made.Add(profile);
            SetField(profile, "id", "visual-test");
            SetField(profile, "kind", kind);
            SetField(profile, "spriteLayers", layers != null ? layers.ToList() : new List<SpriteLayerEntry>());
            SetField(profile, "hiddenLayerSlots", hidden != null ? hidden.ToList() : new List<DefinitionId>());
            return profile;
        }

        private static List<ValidationResult> Run(EquipmentVisualProfile profile, params DefinitionId[] known) =>
            SpriteEquipmentProfileValidator.Validate(profile, "Assets/test.asset", known.Length > 0 ? known : null);

        // ---------------------------------------------------------------------------------------------
        // Profile data
        // ---------------------------------------------------------------------------------------------

        [Test]
        public void TheSpriteKindIsAppendedWithoutChangingTheExistingSerializedValues()
        {
            Assert.AreEqual(0, (int)EquipmentVisualKind.None);
            Assert.AreEqual(1, (int)EquipmentVisualKind.Rigid);
            Assert.AreEqual(2, (int)EquipmentVisualKind.Skinned);
            Assert.AreEqual(3, (int)EquipmentVisualKind.Sprite);
        }

        [Test]
        public void ANewProfileHasEmptyNonNullSpriteListsAndPriorityZero()
        {
            var profile = ScriptableObject.CreateInstance<EquipmentVisualProfile>();
            made.Add(profile);

            Assert.IsNotNull(profile.SpriteLayers);
            Assert.AreEqual(0, profile.SpriteLayers.Count);
            Assert.IsNotNull(profile.HiddenLayerSlots);
            Assert.AreEqual(0, profile.HiddenLayerSlots.Count);
            Assert.AreEqual(0, profile.SpritePriority);
        }

        [Test]
        public void ALayerEntryConstructorAndDefaultTint()
        {
            var sprite = NewSprite();
            var entry = new SpriteLayerEntry(Weapon, sprite, Color.red);

            Assert.AreEqual(Weapon, entry.layerSlot);
            Assert.AreSame(sprite, entry.sprite);
            Assert.AreEqual(Color.red, entry.tint);
            Assert.AreEqual(Color.white, new SpriteLayerEntry().tint, "the parameterless entry (what a first inspector entry builds) is opaque white");
        }

        // ---------------------------------------------------------------------------------------------
        // Custom inspector relevance
        // ---------------------------------------------------------------------------------------------

        [TestCase("kind", EquipmentVisualKind.None, true)]
        [TestCase("id", EquipmentVisualKind.Sprite, true)]
        [TestCase("m_Script", EquipmentVisualKind.Rigid, true)]
        [TestCase("spriteLayers", EquipmentVisualKind.Sprite, true)]
        [TestCase("spritePriority", EquipmentVisualKind.Sprite, true)]
        [TestCase("hiddenLayerSlots", EquipmentVisualKind.Sprite, true)]
        [TestCase("visualPrefab", EquipmentVisualKind.Sprite, false)]
        [TestCase("hiddenBodyRegions", EquipmentVisualKind.Sprite, false)]
        [TestCase("spriteLayers", EquipmentVisualKind.Skinned, false)]
        [TestCase("hiddenLayerSlots", EquipmentVisualKind.Rigid, false)]
        [TestCase("socketId", EquipmentVisualKind.Rigid, true)]
        [TestCase("localScale", EquipmentVisualKind.Rigid, true)]
        [TestCase("boneMap", EquipmentVisualKind.Rigid, false)]
        [TestCase("boneMap", EquipmentVisualKind.Skinned, true)]
        [TestCase("socketId", EquipmentVisualKind.Skinned, false)]
        [TestCase("bodyRegionId", EquipmentVisualKind.Skinned, true)]
        [TestCase("visualPrefab", EquipmentVisualKind.None, false)]
        [TestCase("spriteLayers", EquipmentVisualKind.None, false)]
        public void TheInspectorShowsOnlyTheFieldsRelevantToTheKind(string field, EquipmentVisualKind kind, bool expected) =>
            Assert.AreEqual(expected, EquipmentVisualProfileEditor.IsFieldRelevant(field, kind));

        [Test]
        public void EveryFieldOfTheProfileIsAccountedForByTheInspectorRule()
        {
            // Guards against adding a serialized field to the profile and forgetting to decide which kinds show it: every
            // field declared on EquipmentVisualProfile must be relevant to at least one kind (or be the kind selector).
            var declared = typeof(EquipmentVisualProfile).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Select(f => f.Name).ToList();
            var kinds = new[] { EquipmentVisualKind.None, EquipmentVisualKind.Rigid, EquipmentVisualKind.Skinned, EquipmentVisualKind.Sprite };
            foreach (var field in declared)
            {
                if (field == "kind") continue;
                Assert.IsTrue(kinds.Any(k => EquipmentVisualProfileEditor.IsFieldRelevant(field, k)) && !kinds.All(k => EquipmentVisualProfileEditor.IsFieldRelevant(field, k)),
                    $"'{field}' should be shown for some kinds and hidden for others");
            }
        }

        // ---------------------------------------------------------------------------------------------
        // Validator
        // ---------------------------------------------------------------------------------------------

        [Test]
        public void ACleanSpriteProfileIsSilent()
        {
            var profile = Profile(EquipmentVisualKind.Sprite, new[] { new SpriteLayerEntry(Helmet, NewSprite(), Color.white) }, new[] { Hair });
            Assert.IsEmpty(Run(profile));
        }

        [Test]
        public void ALayerWithNoSpriteIsAnError()
        {
            var profile = Profile(EquipmentVisualKind.Sprite, new[] { new SpriteLayerEntry(Weapon, null, Color.white) });
            var results = Run(profile);
            Assert.AreEqual(1, results.Count(r => r.Severity == ValidationSeverity.Error && r.Field.EndsWith(".sprite")));
        }

        [Test]
        public void AnInvalidOrUnknownLayerSlotIsAnErrorAndACustomKnownSetIsHonoured()
        {
            var invalid = Profile(EquipmentVisualKind.Sprite, new[] { new SpriteLayerEntry(default, NewSprite(), Color.white) });
            Assert.IsTrue(Run(invalid).Any(r => r.Severity == ValidationSeverity.Error && r.Field.EndsWith(".layerSlot")));

            var typo = Profile(EquipmentVisualKind.Sprite, new[] { new SpriteLayerEntry(new DefinitionId("layer-cloths"), NewSprite(), Color.white) });
            Assert.IsTrue(Run(typo).Any(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("layer-cloths")));

            var custom = Profile(EquipmentVisualKind.Sprite, new[] { new SpriteLayerEntry(new DefinitionId("layer-cape"), NewSprite(), Color.white) });
            Assert.IsTrue(Run(custom).Any(r => r.Severity == ValidationSeverity.Error), "unknown to the default slots");
            Assert.IsEmpty(Run(custom, new DefinitionId("layer-cape")), "known once the game's slot list includes it");
        }

        [Test]
        public void APaintedSlotListedTwiceIsAnError()
        {
            var sprite = NewSprite();
            var profile = Profile(EquipmentVisualKind.Sprite, new[] { new SpriteLayerEntry(Weapon, sprite, Color.white), new SpriteLayerEntry(Weapon, sprite, Color.white) });
            Assert.AreEqual(1, Run(profile).Count(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("more than once")));
        }

        [Test]
        public void HidingASlotItAlsoPaintsIsAnError()
        {
            var profile = Profile(EquipmentVisualKind.Sprite, new[] { new SpriteLayerEntry(Helmet, NewSprite(), Color.white) }, new[] { Helmet });
            Assert.IsTrue(Run(profile).Any(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("paint nothing")));
        }

        [Test]
        public void ATransparentTintAndAnUnknownHiddenSlotAreWarnings()
        {
            var transparent = Profile(EquipmentVisualKind.Sprite, new[] { new SpriteLayerEntry(Weapon, NewSprite(), new Color(1f, 1f, 1f, 0f)) });
            Assert.IsTrue(Run(transparent).Any(r => r.Severity == ValidationSeverity.Warning && r.Field.EndsWith(".tint")));

            var hidesUnknown = Profile(EquipmentVisualKind.Sprite, new[] { new SpriteLayerEntry(Weapon, NewSprite(), Color.white) }, new[] { new DefinitionId("layer-wigs") });
            Assert.IsTrue(Run(hidesUnknown).Any(r => r.Severity == ValidationSeverity.Warning && r.Message.Contains("layer-wigs")));
        }

        [Test]
        public void ASpriteProfileWithNothingToDoIsAWarning()
        {
            var results = Run(Profile(EquipmentVisualKind.Sprite));
            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(ValidationSeverity.Warning, results[0].Severity);
        }

        [Test]
        public void SpriteFieldsOnANonSpriteProfileAreAWarningTheKindLeftAtNoneTrap()
        {
            var profile = Profile(EquipmentVisualKind.None, new[] { new SpriteLayerEntry(Weapon, NewSprite(), Color.white) });
            var results = Run(profile);

            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(ValidationSeverity.Warning, results[0].Severity);
            StringAssert.Contains("Sprite", results[0].Fix);

            Assert.IsEmpty(Run(Profile(EquipmentVisualKind.Rigid)), "an ordinary 3D profile is not this validator's business");
        }

        [Test]
        public void ThreeDFieldsOnASpriteProfileAreAWarning()
        {
            var profile = Profile(EquipmentVisualKind.Sprite, new[] { new SpriteLayerEntry(Weapon, NewSprite(), Color.white) });
            SetField(profile, "socketId", new DefinitionId("socket-right-hand"));

            var results = Run(profile);

            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(ValidationSeverity.Warning, results[0].Severity);
            StringAssert.Contains("3D", results[0].Message);
        }

        [Test]
        public void ANullProfileIsToleratedAndSilent() => Assert.IsEmpty(SpriteEquipmentProfileValidator.Validate(null, "x"));
    }
}

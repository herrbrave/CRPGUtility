using System;
using System.Collections.Generic;
using System.Linq;
using CRPG.Core;
using CRPG.Presentation.Sprites;
using NUnit.Framework;
using UnityEngine;

namespace CRPG.Tests.EditMode
{
    /// <summary>2D campaign Task 2.2: SpriteLayerState -- appearance + equipment (keyed by inventory slot) + hides
    /// resolved into effective layers, with no renderers.</summary>
    public class SpriteLayerStateTests
    {
        private static readonly DefinitionId Body = new("layer-body");
        private static readonly DefinitionId Feet = new("layer-feet");
        private static readonly DefinitionId Clothes = new("layer-clothes");
        private static readonly DefinitionId Hair = new("layer-hair");
        private static readonly DefinitionId Helmet = new("layer-helmet");
        private static readonly DefinitionId Weapon = new("layer-weapon");
        private static readonly DefinitionId MainHand = new("slot-main-hand");
        private static readonly DefinitionId OffHand = new("slot-off-hand");
        private static readonly DefinitionId Head = new("slot-head");
        private static readonly DefinitionId Chest = new("slot-chest");

        private readonly List<UnityEngine.Object> made = new();
        private readonly List<string> reports = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in made) if (o != null) UnityEngine.Object.DestroyImmediate(o);
            made.Clear();
            reports.Clear();
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

        private static IReadOnlyList<DefinitionId> Slots() => new[] { Body, Feet, Clothes, Hair, Helmet, Weapon };

        private SpriteLayerState NewState()
        {
            var state = new SpriteLayerState(Slots());
            state.Report = reports.Add;
            return state;
        }

        private static SpriteOption Opt(Sprite s, Color? tint = null) => new(s, tint ?? Color.white);
        private static LayerPaint Paint(DefinitionId slot, Sprite s, Color? tint = null) => new(slot, Opt(s, tint));
        private static EffectiveLayer LayerOf(SpriteLayerState state, DefinitionId slot) => state.Layers.First(l => l.Slot == slot);

        // -----------------------------------------------------------------------------------------------
        // Construction
        // -----------------------------------------------------------------------------------------------

        [Test]
        public void ConstructorRejectsBadSlotLists()
        {
            Assert.Throws<ArgumentException>(() => new SpriteLayerState(null));
            Assert.Throws<ArgumentException>(() => new SpriteLayerState(Array.Empty<DefinitionId>()));
            Assert.Throws<ArgumentException>(() => new SpriteLayerState(new[] { Body, default }));
            Assert.Throws<ArgumentException>(() => new SpriteLayerState(new[] { Body, Feet, Body }));
            var tooMany = Enumerable.Range(0, SpriteSortingContract.MaxSlotsPerObject + 1).Select(i => new DefinitionId("s" + i)).ToArray();
            Assert.Throws<ArgumentException>(() => new SpriteLayerState(tooMany));
        }

        [Test]
        public void TheSlotCapIsAcceptedAndOrderIsPreserved()
        {
            var atCap = Enumerable.Range(0, SpriteSortingContract.MaxSlotsPerObject).Select(i => new DefinitionId("s" + i)).ToArray();
            var state = new SpriteLayerState(atCap);

            Assert.AreEqual(atCap.Length, state.SlotCount);
            for (var i = 0; i < atCap.Length; i++)
            {
                Assert.AreEqual(atCap[i], state.SlotAt(i));
                Assert.AreEqual(atCap[i], state.Layers[i].Slot);
                Assert.AreEqual(i, state.Layers[i].SlotIndex);
            }
        }

        // -----------------------------------------------------------------------------------------------
        // Appearance
        // -----------------------------------------------------------------------------------------------

        [Test]
        public void AppearanceSetsAndClears()
        {
            var state = NewState();
            var body = NewSprite("body");

            Assert.IsFalse(LayerOf(state, Body).Visible, "empty at first");

            state.SetAppearance(Body, Opt(body, Color.red));
            Assert.IsTrue(LayerOf(state, Body).Visible);
            Assert.AreSame(body, LayerOf(state, Body).Sprite);
            Assert.AreEqual(Color.red, LayerOf(state, Body).Tint);

            state.ClearAppearance(Body);
            Assert.IsFalse(LayerOf(state, Body).Visible);

            state.SetAppearance(Body, Opt(body));
            state.SetAppearance(Hair, Opt(NewSprite("hair")));
            state.ClearAllAppearance();
            Assert.IsFalse(state.Layers.Any(l => l.Visible));
        }

        [Test]
        public void ANullSpriteAndAnUnknownSlotAreReportedNotThrown()
        {
            var state = NewState();

            state.SetAppearance(Body, new SpriteOption(null, Color.white));
            state.SetAppearance(new DefinitionId("layer-wings"), Opt(NewSprite()));

            Assert.AreEqual(2, reports.Count);
            Assert.IsFalse(state.Layers.Any(l => l.Visible));
        }

        [Test]
        public void AStateWithNoReportHandlerNeverThrows()
        {
            var state = new SpriteLayerState(Slots()); // Report deliberately not set
            Assert.DoesNotThrow(() =>
            {
                state.SetAppearance(Body, new SpriteOption(null, Color.white));
                state.SetAppearance(new DefinitionId("nope"), Opt(NewSprite()));
                state.SetEquipment(MainHand, 0, new[] { Paint(new DefinitionId("nope"), NewSprite()) }, new[] { new DefinitionId("nope") });
            });
        }

        // -----------------------------------------------------------------------------------------------
        // Equipment
        // -----------------------------------------------------------------------------------------------

        [Test]
        public void EquipmentOverridesAppearanceAndClearingRestoresIt()
        {
            var state = NewState();
            var bare = NewSprite("bare-weapon");
            var sword = NewSprite("sword");
            state.SetAppearance(Weapon, Opt(bare));

            state.SetEquipment(MainHand, 0, new[] { Paint(Weapon, sword, Color.green) }, null);
            Assert.AreSame(sword, LayerOf(state, Weapon).Sprite);
            Assert.AreEqual(Color.green, LayerOf(state, Weapon).Tint, "the paint's own tint applies");

            state.ClearEquipment(MainHand);
            Assert.AreSame(bare, LayerOf(state, Weapon).Sprite, "unequipping restores the appearance underneath");
        }

        [Test]
        public void TheHigherPriorityPaintWinsAndRemovingTheWinnerRestoresTheOtherInEitherOrder()
        {
            var lo = NewSprite("lo");
            var hi = NewSprite("hi");

            foreach (var removeHighFirst in new[] { true, false })
            {
                var state = NewState();
                state.SetEquipment(MainHand, 1, new[] { Paint(Weapon, lo) }, null);
                state.SetEquipment(OffHand, 5, new[] { Paint(Weapon, hi) }, null);
                Assert.AreSame(hi, LayerOf(state, Weapon).Sprite);

                if (removeHighFirst)
                {
                    state.ClearEquipment(OffHand);
                    Assert.AreSame(lo, LayerOf(state, Weapon).Sprite);
                    state.ClearEquipment(MainHand);
                }
                else
                {
                    state.ClearEquipment(MainHand);
                    Assert.AreSame(hi, LayerOf(state, Weapon).Sprite, "the winner stays when the loser leaves");
                    state.ClearEquipment(OffHand);
                }

                Assert.IsFalse(LayerOf(state, Weapon).Visible);
            }
        }

        [Test]
        public void EqualPriorityTiesGoToTheSmallerOrdinalKeyRegardlessOfSetOrder()
        {
            var a = NewSprite("main");
            var b = NewSprite("off");
            var sets = new[] { (MainHand, a), (OffHand, b) };

            // Every permutation of insertion order (the bind/rebind shape: 2.3 re-sets keys in dictionary order).
            foreach (var order in new[] { new[] { 0, 1 }, new[] { 1, 0 } })
            {
                var state = NewState();
                foreach (var i in order) state.SetEquipment(sets[i].Item1, 3, new[] { Paint(Weapon, sets[i].Item2) }, null);
                Assert.AreSame(a, LayerOf(state, Weapon).Sprite, "slot-main-hand < slot-off-hand ordinally");
            }
        }

        [Test]
        public void OneSetPaintingSeveralSlotsAppearsAndLeavesAsAUnit()
        {
            var state = NewState();
            var tunic = NewSprite("tunic");
            var boots = NewSprite("boots");

            state.SetEquipment(Chest, 0, new[] { Paint(Clothes, tunic), Paint(Feet, boots) }, null);
            Assert.AreSame(tunic, LayerOf(state, Clothes).Sprite);
            Assert.AreSame(boots, LayerOf(state, Feet).Sprite);

            state.ClearEquipment(Chest);
            Assert.IsFalse(LayerOf(state, Clothes).Visible);
            Assert.IsFalse(LayerOf(state, Feet).Visible);
        }

        [Test]
        public void SetEquipmentReplacesTheKeysPreviousSetAtomically()
        {
            var state = NewState();
            var oldHat = NewSprite("old");
            var newBoots = NewSprite("new");

            state.SetEquipment(Head, 0, new[] { Paint(Helmet, oldHat) }, new[] { Hair });
            state.SetEquipment(Head, 0, new[] { Paint(Feet, newBoots) }, null);

            Assert.IsFalse(LayerOf(state, Helmet).Visible, "the old paint is gone, no leftovers");
            Assert.AreSame(newBoots, LayerOf(state, Feet).Sprite);
            state.SetAppearance(Hair, Opt(NewSprite("hair")));
            Assert.IsTrue(LayerOf(state, Hair).Visible, "the old hide is gone too");
        }

        [Test]
        public void ADuplicateSlotInOnePaintListLastWinsAndIsReported()
        {
            var state = NewState();
            var first = NewSprite("first");
            var last = NewSprite("last");

            state.SetEquipment(MainHand, 0, new[] { Paint(Weapon, first), Paint(Weapon, last) }, null);

            Assert.AreSame(last, LayerOf(state, Weapon).Sprite);
            Assert.AreEqual(1, reports.Count);
        }

        [Test]
        public void HidesOnlyAndPaintsOnlySetsAreLegalAndClearingAnUnknownKeyIsSilent()
        {
            var state = NewState();
            state.SetAppearance(Hair, Opt(NewSprite("hair")));

            state.SetEquipment(Head, 0, null, new[] { Hair });
            Assert.IsFalse(LayerOf(state, Hair).Visible);

            state.SetEquipment(MainHand, 0, new[] { Paint(Weapon, NewSprite()) }, Array.Empty<DefinitionId>());
            Assert.IsTrue(LayerOf(state, Weapon).Visible);

            var version = state.Version;
            state.ClearEquipment(new DefinitionId("slot-never-equipped"));
            Assert.AreEqual(version, state.Version, "no bump");
            Assert.AreEqual(0, reports.Count, "no report");
        }

        // -----------------------------------------------------------------------------------------------
        // Hides
        // -----------------------------------------------------------------------------------------------

        [Test]
        public void AHelmetHidesHairAndRemovingItBringsTheHairBack()
        {
            var state = NewState();
            var hair = NewSprite("hair");
            var helm = NewSprite("helm");
            state.SetAppearance(Hair, Opt(hair));

            state.SetEquipment(Head, 0, new[] { Paint(Helmet, helm) }, new[] { Hair });
            Assert.IsFalse(LayerOf(state, Hair).Visible);
            Assert.IsTrue(LayerOf(state, Helmet).Visible);

            state.ClearEquipment(Head);
            Assert.IsTrue(LayerOf(state, Hair).Visible);
            Assert.AreSame(hair, LayerOf(state, Hair).Sprite);
        }

        [Test]
        public void TwoHidersReleaseTheSlotOnlyWhenTheLastIsGoneInEitherOrder()
        {
            foreach (var removeHeadFirst in new[] { true, false })
            {
                var state = NewState();
                state.SetAppearance(Hair, Opt(NewSprite("hair")));
                state.SetEquipment(Head, 0, null, new[] { Hair });
                state.SetEquipment(Chest, 0, null, new[] { Hair });

                state.ClearEquipment(removeHeadFirst ? Head : Chest);
                Assert.IsFalse(LayerOf(state, Hair).Visible, "still hidden by the other");
                state.ClearEquipment(removeHeadFirst ? Chest : Head);
                Assert.IsTrue(LayerOf(state, Hair).Visible);
            }
        }

        [Test]
        public void ANonHidingSetNeverReleasesAnotherSetsHideWhateverTheSetOrder()
        {
            foreach (var hiderFirst in new[] { true, false })
            {
                var state = NewState();
                state.SetAppearance(Hair, Opt(NewSprite("hair")));
                if (hiderFirst) state.SetEquipment(Head, 0, null, new[] { Hair });
                state.SetEquipment(MainHand, 0, new[] { Paint(Weapon, NewSprite("sword")) }, null);
                state.SetEquipment(Chest, 0, new[] { Paint(Clothes, NewSprite("tunic")) }, null);
                if (!hiderFirst) state.SetEquipment(Head, 0, null, new[] { Hair });

                Assert.IsFalse(LayerOf(state, Hair).Visible, $"hider {(hiderFirst ? "first" : "last")}");
            }
        }

        [Test]
        public void ASetThatHidesItsOwnPaintedSlotPaintsNothing()
        {
            var state = NewState();
            state.SetEquipment(Head, 0, new[] { Paint(Helmet, NewSprite()) }, new[] { Helmet });
            Assert.IsFalse(LayerOf(state, Helmet).Visible);
        }

        // -----------------------------------------------------------------------------------------------
        // Tint / version / output identity
        // -----------------------------------------------------------------------------------------------

        [Test]
        public void ASubEpsilonTintChangeStillBumpsTheVersion()
        {
            var state = NewState();
            var body = NewSprite();
            state.SetAppearance(Body, Opt(body, new Color(0.5f, 0.5f, 0.5f, 1f)));
            var version = state.Version;

            state.SetAppearance(Body, Opt(body, new Color(0.5f + 1e-7f, 0.5f, 0.5f, 1f)));

            Assert.Greater(state.Version, version);
        }

        [Test]
        public void IdenticalRepeatedSetsAreNoOpsAndEveryRealChangeBumpsTheVersion()
        {
            var state = NewState();
            var body = NewSprite();
            var paints = new[] { Paint(Weapon, NewSprite("sword")) };

            var v0 = state.Version;
            state.SetAppearance(Body, Opt(body));
            var v1 = state.Version;
            Assert.Greater(v1, v0);
            state.SetAppearance(Body, Opt(body));
            Assert.AreEqual(v1, state.Version, "identical appearance re-set");

            state.SetEquipment(MainHand, 2, paints, null);
            var v2 = state.Version;
            Assert.Greater(v2, v1);
            state.SetEquipment(MainHand, 2, paints, null);
            Assert.AreEqual(v2, state.Version, "an identical set is a full no-op");

            state.SetEquipment(MainHand, 3, paints, null);
            Assert.Greater(state.Version, v2, "a priority change is a real change");

            state.ClearAppearance(Feet);
            var v3 = state.Version;
            state.ClearAppearance(Feet);
            state.ClearAllAppearance();
            Assert.AreEqual(v3 + 1, state.Version, "clearing something that is set bumps once; clearing nothing does not");
        }

        [Test]
        public void LayersIsTheSameInstanceAcrossReadsAndRefreshesInPlace()
        {
            var state = NewState();
            var first = state.Layers;
            Assert.AreSame(first, state.Layers);

            state.SetAppearance(Body, Opt(NewSprite()));
            Assert.AreSame(first, state.Layers, "no per-change allocation either");
            Assert.IsTrue(first[0].Visible, "the same instance shows the refreshed data");
        }

        [Test]
        public void MutatingWhileEnumeratingDoesNotThrow()
        {
            var state = NewState();
            state.SetAppearance(Body, Opt(NewSprite()));
            var body = NewSprite("again");

            Assert.DoesNotThrow(() =>
            {
                foreach (var layer in state.Layers)
                {
                    state.SetAppearance(Feet, Opt(body));
                    _ = state.Layers;
                }
            });
        }

        [Test]
        public void CopyVisibleToYieldsOnlyVisibleLayersInDrawOrder()
        {
            var state = NewState();
            state.SetAppearance(Hair, Opt(NewSprite("hair")));
            state.SetAppearance(Body, Opt(NewSprite("body")));
            state.SetEquipment(MainHand, 0, new[] { Paint(Weapon, NewSprite("sword")) }, null);

            var visible = new List<EffectiveLayer> { default }; // proves it clears first
            state.CopyVisibleTo(visible);

            CollectionAssert.AreEqual(new[] { Body, Hair, Weapon }, visible.Select(l => l.Slot));
        }
    }
}

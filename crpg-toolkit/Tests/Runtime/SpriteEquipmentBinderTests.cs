using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Gameplay;
using CRPG.Inventory;
using CRPG.Presentation.Equipment;
using CRPG.Presentation.Sprites;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace CRPG.Tests.PlayMode
{
    /// <summary>
    /// 2D campaign Task 2.3: SpriteEquipmentBinder against the REAL EquipmentService, GameEventBus, PartyInventoryState and
    /// RulesDatabase -- equip/unequip/replace, reconciliation, the inactive-load subscription gap, a compositor destroyed by a
    /// model swap, the Initialize contract, and the WorldSpawnService wiring.
    /// </summary>
    public class SpriteEquipmentBinderTests
    {
        private static readonly DefinitionId MainHand = new("slot-main-hand");
        private static readonly DefinitionId OffHand = new("slot-off-hand");
        private static readonly DefinitionId Head = new("slot-head");
        private static readonly DefinitionId Chest = new("slot-chest");
        private static readonly DefinitionId Weapon = new("layer-weapon");
        private static readonly DefinitionId Hair = new("layer-hair");
        private static readonly DefinitionId Helmet = new("layer-helmet");
        private static readonly DefinitionId Clothes = new("layer-clothes");
        private const int WeaponIndex = 6; // layer-weapon in the compositor's default slot order

        [DefaultExecutionOrder(1000)]
        private sealed class PresentedSpriteProbe : MonoBehaviour
        {
            public SpriteRenderer Target;
            public readonly List<Sprite> Seen = new();
            private void LateUpdate() { if (Target != null) Seen.Add(Target.enabled ? Target.sprite : null); }
        }

        private sealed class World
        {
            public RulesDatabase Rules = new();
            public GameEventBus Bus = new();
            public PartyInventoryState Inventory = new();
            public EquipmentService Service;
            public RuntimeEntityId Character = RuntimeEntityId.NewId();
        }

        private readonly List<Object> made = new();
        private int serial;

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

            throw new Exception($"Field '{fieldName}' not found on {target.GetType().Name}.");
        }

        private T Track<T>(T o) where T : Object { made.Add(o); return o; }

        private Sprite NewSprite(string name = "s")
        {
            var texture = Track(new Texture2D(4, 4) { name = name });
            var sprite = Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
            sprite.name = name;
            return Track(sprite);
        }

        private World NewWorld()
        {
            var world = new World();
            world.Service = new EquipmentService(new CharacterRegistry(), world.Rules, world.Inventory, world.Bus);
            return world;
        }

        private EquipmentVisualProfile SpriteProfile(World world, int priority, IEnumerable<SpriteLayerEntry> layers, IEnumerable<DefinitionId> hidden = null,
            EquipmentVisualKind kind = EquipmentVisualKind.Sprite)
        {
            var profile = Track(ScriptableObject.CreateInstance<EquipmentVisualProfile>());
            SetField(profile, "id", "visual-" + serial++);
            SetField(profile, "kind", kind);
            SetField(profile, "spriteLayers", layers.ToList());
            SetField(profile, "spritePriority", priority);
            SetField(profile, "hiddenLayerSlots", hidden != null ? hidden.ToList() : new List<DefinitionId>());
            world.Rules.Register(profile);
            return profile;
        }

        private ItemInstance Item(World world, DefinitionId slot, EquipmentVisualProfile profile)
        {
            var def = Track(ScriptableObject.CreateInstance<ItemDefinition>());
            SetField(def, "id", "item-" + serial++);
            SetField(def, "equipmentSlotId", slot);
            if (profile != null) SetField(def, "equipmentVisualProfileId", profile.Id);
            world.Rules.Register(def);
            var instance = new ItemInstance(RuntimeEntityId.NewId(), def.Id);
            world.Inventory.AddItem(instance);
            return instance;
        }

        private static SpriteLayerEntry Layer(DefinitionId slot, Sprite sprite) => new(slot, sprite, Color.white);

        private sealed class Rig
        {
            public GameObject Root;
            public SpriteLayerCompositor Compositor;
            public SpriteEquipmentBinder Binder;
            public SpriteLayerState State => Compositor.State;
        }

        private Rig NewRig(World world, bool initialize = true, bool active = true)
        {
            var root = Track(new GameObject("SpriteCharacter"));
            root.SetActive(false);
            var compositor = root.AddComponent<SpriteLayerCompositor>();
            var binder = root.AddComponent<SpriteEquipmentBinder>();
            if (active) root.SetActive(true);
            if (initialize) binder.Initialize(world.Character, world.Rules, world.Inventory, world.Bus);
            return new Rig { Root = root, Compositor = compositor, Binder = binder };
        }

        private static EffectiveLayer LayerOf(SpriteLayerState state, DefinitionId slot) => state.Layers.First(l => l.Slot == slot);

        private static IEnumerator Frames(int count)
        {
            for (var i = 0; i < count; i++) yield return null;
        }

        // ---------------------------------------------------------------------------------------------
        // Equip / unequip / replace
        // ---------------------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator EquipPaintsTheNamedLayersAndUnequipRestoresTheAppearanceBeneath()
        {
            var world = NewWorld();
            var rig = NewRig(world);
            var bare = NewSprite("bare");
            var sword = NewSprite("sword");
            rig.State.SetAppearance(Weapon, new SpriteOption(bare, Color.white));
            var item = Item(world, MainHand, SpriteProfile(world, 0, new[] { Layer(Weapon, sword) }));
            yield return null;

            world.Service.Equip(item.InstanceId, world.Character, MainHand);
            Assert.AreSame(sword, LayerOf(rig.State, Weapon).Sprite);
            Assert.IsFalse(rig.State.Layers.Where(l => l.Slot != Weapon).Any(l => l.Visible), "only the named layer is painted");

            world.Service.Unequip(item.InstanceId);
            Assert.AreSame(bare, LayerOf(rig.State, Weapon).Sprite, "unequipping restores the appearance beneath");
        }

        [UnityTest]
        public IEnumerator SameSlotReplacementThroughTheRealServicePresentsNoEmptyFrameAndSendsTheOldItemBackToTheInventory()
        {
            var world = NewWorld();
            var rig = NewRig(world);
            var probe = rig.Root.AddComponent<PresentedSpriteProbe>();
            yield return null;
            probe.Target = rig.Compositor.Renderers[WeaponIndex];
            var sword = NewSprite("sword");
            var axe = NewSprite("axe");
            var a = Item(world, MainHand, SpriteProfile(world, 0, new[] { Layer(Weapon, sword) }));
            var b = Item(world, MainHand, SpriteProfile(world, 0, new[] { Layer(Weapon, axe) }));
            world.Service.Equip(a.InstanceId, world.Character, MainHand);
            yield return Frames(3);
            Assert.AreSame(sword, probe.Seen.Last());

            var displaced = world.Service.Equip(b.InstanceId, world.Character, MainHand);
            yield return Frames(3);

            Assert.AreSame(a, displaced);
            Assert.IsFalse(world.Inventory.GetLocation(a.InstanceId).IsEquipped);
            Assert.AreSame(axe, probe.Seen.Last());
            var firstSword = probe.Seen.IndexOf(sword);
            var firstAxe = probe.Seen.IndexOf(axe);
            Assert.IsFalse(probe.Seen.Skip(firstSword).Take(firstAxe - firstSword).Any(s => s == null), "sword to axe, never through empty");
        }

        [UnityTest]
        public IEnumerator AGenuineUnequipThenALaterEquipDoesPresentTheBareFrameByDesign()
        {
            var world = NewWorld();
            var rig = NewRig(world);
            var probe = rig.Root.AddComponent<PresentedSpriteProbe>();
            yield return null;
            probe.Target = rig.Compositor.Renderers[WeaponIndex];
            var sword = NewSprite("sword");
            var axe = NewSprite("axe");
            var a = Item(world, MainHand, SpriteProfile(world, 0, new[] { Layer(Weapon, sword) }));
            var b = Item(world, MainHand, SpriteProfile(world, 0, new[] { Layer(Weapon, axe) }));
            world.Service.Equip(a.InstanceId, world.Character, MainHand);
            yield return Frames(2);

            world.Service.Unequip(a.InstanceId);
            yield return Frames(2);
            Assert.IsNull(probe.Seen.Last(), "the slot really is bare in between");

            world.Service.Equip(b.InstanceId, world.Character, MainHand);
            yield return Frames(2);
            Assert.AreSame(axe, probe.Seen.Last());
        }

        [UnityTest]
        public IEnumerator ADirectBusEquipWithNoPrecedingUnequipReplacesTheSlotCleanly()
        {
            var world = NewWorld();
            var rig = NewRig(world);
            var sword = NewSprite("sword");
            var axe = NewSprite("axe");
            var a = Item(world, MainHand, SpriteProfile(world, 0, new[] { Layer(Weapon, sword) }));
            var b = Item(world, MainHand, SpriteProfile(world, 0, new[] { Layer(Weapon, axe) }));
            world.Service.Equip(a.InstanceId, world.Character, MainHand);
            yield return null;

            var context = new GameEventContext(GameEvent.ItemEquipped, world.Character, world.Character);
            context.Data["ItemDefinitionId"] = b.DefinitionId;
            context.Data["SlotId"] = MainHand;
            world.Bus.Publish(context);

            Assert.AreSame(axe, LayerOf(rig.State, Weapon).Sprite);
        }

        // ---------------------------------------------------------------------------------------------
        // Reconciliation, ordering, subscription timing
        // ---------------------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator GearAlreadyWornBeforeTheBinderIsInitializedShowsAtOnce()
        {
            var world = NewWorld();
            var sword = NewSprite("sword");
            var item = Item(world, MainHand, SpriteProfile(world, 0, new[] { Layer(Weapon, sword) }));
            world.Service.Equip(item.InstanceId, world.Character, MainHand); // "load" happened first
            var rig = NewRig(world);
            yield return null;

            Assert.AreSame(sword, LayerOf(rig.State, Weapon).Sprite);
            Assert.IsTrue(rig.Compositor.Renderers[WeaponIndex].enabled);
        }

        [UnityTest]
        public IEnumerator EqualPriorityItemsPaintingOneLayerGiveTheSameWinnerWhicheverWasAddedFirst()
        {
            var winners = new List<string>();
            foreach (var mainFirst in new[] { true, false })
            {
                var world = NewWorld();
                var mainSprite = NewSprite("main");
                var offSprite = NewSprite("off");
                var mainProfile = SpriteProfile(world, 5, new[] { Layer(Weapon, mainSprite) });
                var offProfile = SpriteProfile(world, 5, new[] { Layer(Weapon, offSprite) });
                ItemInstance main, off;
                if (mainFirst) { main = Item(world, MainHand, mainProfile); off = Item(world, OffHand, offProfile); }
                else { off = Item(world, OffHand, offProfile); main = Item(world, MainHand, mainProfile); }
                world.Service.Equip(off.InstanceId, world.Character, OffHand);
                world.Service.Equip(main.InstanceId, world.Character, MainHand);

                var rig = NewRig(world); // bind AFTER loading: reconciliation decides
                yield return null;
                winners.Add(LayerOf(rig.State, Weapon).Sprite.name);
            }

            Assert.AreEqual(new[] { "main", "main" }, winners.ToArray(), "slot-main-hand < slot-off-hand ordinally, independent of insertion order");
        }

        [UnityTest]
        public IEnumerator AHigherPriorityWinsOverALowerOne()
        {
            var world = NewWorld();
            var rig = NewRig(world);
            var lo = NewSprite("lo");
            var hi = NewSprite("hi");
            var a = Item(world, MainHand, SpriteProfile(world, 1, new[] { Layer(Weapon, lo) }));
            var b = Item(world, OffHand, SpriteProfile(world, 9, new[] { Layer(Weapon, hi) }));
            world.Service.Equip(a.InstanceId, world.Character, MainHand);
            world.Service.Equip(b.InstanceId, world.Character, OffHand);
            yield return null;

            Assert.AreSame(hi, LayerOf(rig.State, Weapon).Sprite);
        }

        [UnityTest]
        public IEnumerator EventsFiredWhileTheInstanceWasInactiveAreNotLostWhenItIsActivated()
        {
            // The real load path (WorldSpawnService startInactive) deactivates the instance, initializes, and activates later.
            var world = NewWorld();
            var rig = NewRig(world, initialize: false, active: false);
            var sword = NewSprite("sword");
            var item = Item(world, MainHand, SpriteProfile(world, 0, new[] { Layer(Weapon, sword) }));
            rig.Binder.Initialize(world.Character, world.Rules, world.Inventory, world.Bus); // inactive: subscription deferred

            world.Service.Equip(item.InstanceId, world.Character, MainHand); // fires before OnEnable
            rig.Root.SetActive(true);
            yield return null;

            Assert.AreSame(sword, LayerOf(rig.State, Weapon).Sprite);
        }

        [UnityTest]
        public IEnumerator ADisabledBinderStillTracksGearAndShowsItOnReEnable()
        {
            var world = NewWorld();
            var rig = NewRig(world);
            yield return null;
            rig.Binder.enabled = false;
            var sword = NewSprite("sword");
            var item = Item(world, MainHand, SpriteProfile(world, 0, new[] { Layer(Weapon, sword) }));

            world.Service.Equip(item.InstanceId, world.Character, MainHand);
            Assert.AreSame(sword, LayerOf(rig.State, Weapon).Sprite, "the subscription is deliberately kept while disabled");
            rig.Binder.enabled = true;
            yield return null;
            Assert.AreSame(sword, LayerOf(rig.State, Weapon).Sprite);
        }

        // ---------------------------------------------------------------------------------------------
        // Compositor lifetime and explicit targets
        // ---------------------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator AModelSwapThatDestroysTheCompositorGetsTheGearReappliedToTheNewOne()
        {
            var world = NewWorld();
            var root = Track(new GameObject("CharacterRoot"));
            var binder = root.AddComponent<SpriteEquipmentBinder>();
            var oldModel = new GameObject("Model");
            oldModel.transform.SetParent(root.transform, false);
            var oldCompositor = oldModel.AddComponent<SpriteLayerCompositor>();
            var oldState = oldCompositor.State;
            binder.Initialize(world.Character, world.Rules, world.Inventory, world.Bus);
            var sword = NewSprite("sword");
            var item = Item(world, MainHand, SpriteProfile(world, 0, new[] { Layer(Weapon, sword) }));
            world.Service.Equip(item.InstanceId, world.Character, MainHand);
            yield return Frames(2);
            Assert.AreSame(sword, LayerOf(oldState, Weapon).Sprite);

            Object.DestroyImmediate(oldModel); // what CharacterView.SetModel does
            var newModel = Track(new GameObject("Model"));
            newModel.transform.SetParent(root.transform, false);
            var newCompositor = newModel.AddComponent<SpriteLayerCompositor>();
            yield return Frames(3);

            Assert.AreSame(sword, LayerOf(newCompositor.State, Weapon).Sprite, "the new model carries the character's gear with no equip event");
            world.Service.Unequip(item.InstanceId);
            Assert.IsFalse(LayerOf(newCompositor.State, Weapon).Visible, "and later events reach the new state");
        }

        [UnityTest]
        public IEnumerator SetTargetMovesTheGearToTheNewStateAndBackToTheCompositor()
        {
            var world = NewWorld();
            var rig = NewRig(world);
            var sword = NewSprite("sword");
            var item = Item(world, MainHand, SpriteProfile(world, 0, new[] { Layer(Weapon, sword) }));
            world.Service.Equip(item.InstanceId, world.Character, MainHand);
            yield return null;

            var preview = new SpriteLayerState(SpriteLayerCompositor.DefaultSlots());
            rig.Binder.SetTarget(preview);
            Assert.AreSame(sword, LayerOf(preview, Weapon).Sprite);
            Assert.IsFalse(LayerOf(rig.State, Weapon).Visible, "cleared on the previous target");

            rig.Binder.SetTarget(null);
            Assert.AreSame(sword, LayerOf(rig.State, Weapon).Sprite);
            Assert.IsFalse(LayerOf(preview, Weapon).Visible);
        }

        [UnityTest]
        public IEnumerator NoCompositorAndNoTargetWarnsOnceAndNeverThrows()
        {
            var world = NewWorld();
            var root = Track(new GameObject("Bare"));
            var binder = root.AddComponent<SpriteEquipmentBinder>();
            LogAssert.Expect(LogType.Warning, new Regex("no SpriteLayerCompositor"));
            binder.Initialize(world.Character, world.Rules, world.Inventory, world.Bus);
            var item = Item(world, MainHand, SpriteProfile(world, 0, new[] { Layer(Weapon, NewSprite()) }));

            Assert.DoesNotThrow(() => world.Service.Equip(item.InstanceId, world.Character, MainHand));
            yield return Frames(5);

            LogAssert.NoUnexpectedReceived();
        }

        // ---------------------------------------------------------------------------------------------
        // Profiles that are not sprite profiles, malformed events
        // ---------------------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator ItemsWithNoUsableSpriteProfileClearTheSlotWithoutThrowingOrLogging()
        {
            var world = NewWorld();
            var rig = NewRig(world);
            var sword = NewSprite("sword");
            var sworded = Item(world, MainHand, SpriteProfile(world, 0, new[] { Layer(Weapon, sword) }));
            var plain = Item(world, MainHand, null);                                                    // no profile id
            var none = Item(world, MainHand, SpriteProfile(world, 0, new SpriteLayerEntry[0], kind: EquipmentVisualKind.None));
            yield return null;

            world.Service.Equip(sworded.InstanceId, world.Character, MainHand);
            Assert.IsTrue(LayerOf(rig.State, Weapon).Visible);
            world.Service.Equip(plain.InstanceId, world.Character, MainHand);
            Assert.IsFalse(LayerOf(rig.State, Weapon).Visible, "an item with no profile leaves the slot bare");

            world.Service.Equip(sworded.InstanceId, world.Character, MainHand);
            world.Service.Equip(none.InstanceId, world.Character, MainHand);
            Assert.IsFalse(LayerOf(rig.State, Weapon).Visible, "a None profile too");

            // Unknown item definition, a missing SlotId, and a None SlotId are all ignored.
            var unknown = new GameEventContext(GameEvent.ItemEquipped, world.Character, world.Character);
            unknown.Data["ItemDefinitionId"] = new DefinitionId("item-nobody-registered");
            unknown.Data["SlotId"] = MainHand;
            Assert.DoesNotThrow(() => world.Bus.Publish(unknown));
            var noSlot = new GameEventContext(GameEvent.ItemEquipped, world.Character, world.Character);
            noSlot.Data["ItemDefinitionId"] = sworded.DefinitionId;
            Assert.DoesNotThrow(() => world.Bus.Publish(noSlot));
            var noneSlot = new GameEventContext(GameEvent.ItemEquipped, world.Character, world.Character);
            noneSlot.Data["ItemDefinitionId"] = sworded.DefinitionId;
            noneSlot.Data["SlotId"] = DefinitionId.None;
            Assert.DoesNotThrow(() => world.Bus.Publish(noneSlot));
            Assert.IsFalse(rig.State.Layers.Any(l => l.Visible));

            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator AThreeDProfileOnASpriteCharacterClearsTheSlotAndWarnsOncePerProfile()
        {
            var world = NewWorld();
            var rig = NewRig(world);
            var rigid = Item(world, MainHand, SpriteProfile(world, 0, new SpriteLayerEntry[0], kind: EquipmentVisualKind.Rigid));
            yield return null;

            LogAssert.Expect(LogType.Warning, new Regex("Rigid profile"));
            world.Service.Equip(rigid.InstanceId, world.Character, MainHand);
            world.Service.Unequip(rigid.InstanceId);
            world.Service.Equip(rigid.InstanceId, world.Character, MainHand);

            Assert.IsFalse(rig.State.Layers.Any(l => l.Visible));
            LogAssert.NoUnexpectedReceived(); // exactly one warning for the profile
        }

        [UnityTest]
        public IEnumerator APaintWithANullSpriteDropsThatPaintReportsItAndStillAppliesTheRest()
        {
            var world = NewWorld();
            var rig = NewRig(world);
            var tunic = NewSprite("tunic");
            var item = Item(world, Chest, SpriteProfile(world, 0, new[] { Layer(Weapon, null), Layer(Clothes, tunic) }));
            yield return null;

            LogAssert.Expect(LogType.Warning, new Regex("has no sprite"));
            world.Service.Equip(item.InstanceId, world.Character, Chest);

            Assert.IsFalse(LayerOf(rig.State, Weapon).Visible);
            Assert.AreSame(tunic, LayerOf(rig.State, Clothes).Sprite);
        }

        // ---------------------------------------------------------------------------------------------
        // Hides
        // ---------------------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator AHelmetHidesHairAndTwoHidersReleaseItOnlyWhenBothAreGoneInEitherOrder()
        {
            foreach (var removeHelmetFirst in new[] { true, false })
            {
                var world = NewWorld();
                var rig = NewRig(world);
                var hair = NewSprite("hair");
                rig.State.SetAppearance(Hair, new SpriteOption(hair, Color.white));
                var helmet = Item(world, Head, SpriteProfile(world, 30, new[] { Layer(Helmet, NewSprite("helm")) }, new[] { Hair }));
                var hood = Item(world, Chest, SpriteProfile(world, 20, new[] { Layer(Clothes, NewSprite("hood")) }, new[] { Hair }));
                yield return null;

                world.Service.Equip(helmet.InstanceId, world.Character, Head);
                Assert.IsFalse(LayerOf(rig.State, Hair).Visible);
                world.Service.Equip(hood.InstanceId, world.Character, Chest);

                world.Service.Unequip((removeHelmetFirst ? helmet : hood).InstanceId);
                Assert.IsFalse(LayerOf(rig.State, Hair).Visible, "still hidden by the other item");
                world.Service.Unequip((removeHelmetFirst ? hood : helmet).InstanceId);
                Assert.AreSame(hair, LayerOf(rig.State, Hair).Sprite);
                Assert.IsTrue(LayerOf(rig.State, Hair).Visible);
            }
        }

        // ---------------------------------------------------------------------------------------------
        // Filtering and the Initialize contract
        // ---------------------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator AnotherCharactersGearChangesNothing()
        {
            var world = NewWorld();
            var rig = NewRig(world);
            var other = RuntimeEntityId.NewId();
            var item = Item(world, MainHand, SpriteProfile(world, 0, new[] { Layer(Weapon, NewSprite()) }));
            yield return null;

            world.Service.Equip(item.InstanceId, other, MainHand);

            Assert.IsFalse(rig.State.Layers.Any(l => l.Visible));
        }

        [UnityTest]
        public IEnumerator InitializingWithANoneCharacterWarnsStaysUnboundAndNeverReconcilesTheSharedInventory()
        {
            var world = NewWorld();
            var rig = NewRig(world, initialize: false);
            var shared = Item(world, MainHand, SpriteProfile(world, 0, new[] { Layer(Weapon, NewSprite("shared")) })); // in the shared inventory
            yield return null;

            LogAssert.Expect(LogType.Warning, new Regex("Cannot bind"));
            rig.Binder.Initialize(RuntimeEntityId.None, world.Rules, world.Inventory, world.Bus);

            Assert.IsFalse(rig.Binder.IsBound);
            Assert.IsFalse(rig.State.Layers.Any(l => l.Visible), "a shared-inventory item must not be painted onto the character");
            Assert.IsNotNull(shared);
        }

        [UnityTest]
        public IEnumerator TheInitializeContractMatchesTheThreeDBinder()
        {
            var world = NewWorld();
            var rig = NewRig(world);
            yield return null;

            // Identical tuple: no-op (and no duplicate subscription -- proven below by Unbind removing ALL listening).
            rig.Binder.Initialize(world.Character, world.Rules, world.Inventory, world.Bus);
            Assert.Throws<InvalidOperationException>(() => rig.Binder.Initialize(RuntimeEntityId.NewId(), world.Rules, world.Inventory, world.Bus));

            var sword = NewSprite("sword");
            var appearance = NewSprite("appearance");
            rig.State.SetAppearance(Hair, new SpriteOption(appearance, Color.white));
            var item = Item(world, MainHand, SpriteProfile(world, 0, new[] { Layer(Weapon, sword) }));
            world.Service.Equip(item.InstanceId, world.Character, MainHand);
            Assert.IsTrue(LayerOf(rig.State, Weapon).Visible);

            rig.Binder.Unbind();
            Assert.IsFalse(LayerOf(rig.State, Weapon).Visible, "Unbind removes exactly the equipment...");
            Assert.AreSame(appearance, LayerOf(rig.State, Hair).Sprite, "...and leaves the appearance alone");
            Assert.IsFalse(rig.Binder.IsBound);

            world.Service.Unequip(item.InstanceId);
            world.Service.Equip(item.InstanceId, world.Character, MainHand);
            Assert.IsFalse(LayerOf(rig.State, Weapon).Visible, "no listener remains after Unbind (a duplicate subscription would still paint)");

            rig.Binder.Unbind(); // idempotent
            Assert.DoesNotThrow(() => rig.Binder.Initialize(world.Character, world.Rules, world.Inventory, world.Bus));
            Assert.IsTrue(LayerOf(rig.State, Weapon).Visible, "rebinding reconciles from the inventory");
        }

        // ---------------------------------------------------------------------------------------------
        // The 3D binder meeting a Sprite profile
        // ---------------------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator TheThreeDBinderIgnoresASpriteProfileWithOneAccurateWarningPerProfile()
        {
            var world = NewWorld();
            var root = Track(new GameObject("ThreeDCharacter"));
            var binder = root.AddComponent<EquipmentVisualBinder>();
            new GameObject("Hips").transform.SetParent(root.transform);
            binder.RefreshBoneMap(null);
            binder.Initialize(world.Character, world.Rules, world.Inventory, world.Bus);
            var item = Item(world, MainHand, SpriteProfile(world, 0, new[] { Layer(Weapon, NewSprite()) }));
            yield return null;

            LogAssert.Expect(LogType.Warning, new Regex("is a Sprite profile; sprite characters use SpriteEquipmentBinder"));
            world.Service.Equip(item.InstanceId, world.Character, MainHand);
            world.Service.Unequip(item.InstanceId);
            world.Service.Equip(item.InstanceId, world.Character, MainHand);
            yield return null;

            Assert.IsNull(binder.GetEquippedVisual(MainHand), "a sprite item draws nothing on a 3D character");
            LogAssert.NoUnexpectedReceived(); // one warning for the profile, not one per equip
        }

        // ---------------------------------------------------------------------------------------------
        // WorldSpawnService wiring
        // ---------------------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator BindRestoredViewInitializesTheBinderOnAnInactiveInstanceAndGearShowsOnceItIsActivated()
        {
            var rules = new RulesDatabase();
            var hostGo = Track(new GameObject("Host"));
            var host = hostGo.AddComponent<GameSessionHost>();
            var world = new World { Rules = rules };
            var sword = NewSprite("sword");
            var axe = NewSprite("axe");
            var swordItem = Item(world, MainHand, SpriteProfile(world, 0, new[] { Layer(Weapon, sword) }));
            var axeItem = Item(world, OffHand, SpriteProfile(world, 0, new[] { Layer(Clothes, axe) }));

            var session = host.StartSession(rules, new DiceService(new SeededRandomSource(1)));
            var characterId = RuntimeEntityId.NewId();
            session.Characters.Register(new CharacterState(characterId));
            session.Party.AddMember(characterId);
            foreach (var item in new[] { swordItem, axeItem }) session.Inventory.AddItem(item);
            var service = new EquipmentService(session.Characters, rules, session.Inventory, session.EventBus);
            service.Equip(swordItem.InstanceId, characterId, MainHand); // already worn when the view binds (the load shape)

            var template = Track(new GameObject("Template"));
            template.SetActive(false);
            var model = new GameObject("Model");
            model.transform.SetParent(template.transform, false);
            model.AddComponent<SpriteLayerCompositor>();
            model.AddComponent<SpriteEquipmentBinder>();

            var spawner = new WorldSpawnService(session);
            var instance = spawner.BindRestoredView(characterId.ToString(), characterId, default, template, Vector3.zero, Quaternion.identity, default, startInactive: true);
            Track(instance);
            yield return null;
            Assert.IsFalse(instance.activeSelf, "staged inactive");

            instance.SetActive(true);
            yield return Frames(2);
            var state = instance.GetComponentInChildren<SpriteLayerCompositor>().State;
            Assert.AreSame(sword, LayerOf(state, Weapon).Sprite, "gear worn at bind time shows");

            service.Equip(axeItem.InstanceId, characterId, OffHand);
            Assert.AreSame(axe, LayerOf(state, Clothes).Sprite, "and later real equips update it");
        }
    }
}

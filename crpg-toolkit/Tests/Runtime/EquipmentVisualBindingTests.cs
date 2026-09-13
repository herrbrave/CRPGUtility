using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Inventory;
using CRPG.Presentation;
using CRPG.Presentation.Equipment;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CRPG.Tests.PlayMode
{
    /// <summary>
    /// Task 2.4 (Production Foundation): the Rigid/Skinned/body-region-hiding matrix Docs/Design/
    /// Production-Foundation.md sec.4.5 describes, exercised directly against EquipmentVisualBinder -- the
    /// scenarios named explicitly by the implementation plan (missing/duplicate bone paths, incompatible
    /// rig-family rejection, two garments hiding the same region, removal order, load-before-view-bind,
    /// visual failure retaining the known-good appearance) rather than only what today's real Peasant content
    /// happens to exercise (which never overlaps regions and never mismatches rig families).
    /// </summary>
    public class EquipmentVisualBindingTests
    {
        private GameObject characterGo;
        private readonly List<GameObject> spawnedPrefabs = new();

        [TearDown]
        public void TearDown()
        {
            if (characterGo != null) UnityEngine.Object.DestroyImmediate(characterGo);
            foreach (var prefab in spawnedPrefabs)
                if (prefab != null) UnityEngine.Object.DestroyImmediate(prefab);
            spawnedPrefabs.Clear();
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

            throw new System.Exception($"Field '{fieldName}' not found on {target.GetType().Name} or its base types.");
        }

        private GameObject Track(GameObject go)
        {
            spawnedPrefabs.Add(go);
            return go;
        }

        /// <summary>A bare CharacterRoot with a small named-bone hierarchy (standing in for a real skeleton)
        /// and a named socket, then bound via RefreshBoneMap -- everything EquipmentVisualBinder needs to
        /// resolve both Rigid sockets and Skinned target paths.</summary>
        private EquipmentVisualBinder CreateBoundCharacter(CharacterRigDefinition rig = null, bool bindSkeleton = true)
        {
            characterGo = new GameObject("CharacterRoot");
            var equipment = characterGo.AddComponent<EquipmentVisualBinder>();

            var hips = new GameObject("Hips");
            hips.transform.SetParent(characterGo.transform);
            var spine = new GameObject("Spine");
            spine.transform.SetParent(hips.transform);
            var hand = new GameObject("LeftHand");
            hand.transform.SetParent(spine.transform);
            var socket = new GameObject("socket-right-hand");
            socket.transform.SetParent(hand.transform);

            if (bindSkeleton) equipment.RefreshBoneMap(rig);
            return equipment;
        }

        private GameObject CreateGarmentWithBone(string boneName, out Transform bone)
        {
            var garment = Track(new GameObject("Garment"));
            var boneGo = new GameObject(boneName);
            boneGo.transform.SetParent(garment.transform);
            bone = boneGo.transform;

            var renderer = garment.AddComponent<SkinnedMeshRenderer>();
            renderer.bones = new[] { bone };
            renderer.rootBone = bone;
            return garment;
        }

        private static RigBindingMap CreateMap(DefinitionId id, DefinitionId rigFamilyId, params (string Source, string Target)[] entries)
        {
            var map = ScriptableObject.CreateInstance<RigBindingMap>();
            SetField(map, "id", id.Value);
            SetField(map, "rigFamilyId", rigFamilyId);

            var list = new List<RigBindingMap.BonePathEntry>();
            foreach (var (source, target) in entries)
            {
                var entry = new RigBindingMap.BonePathEntry();
                SetField(entry, "sourcePath", source);
                SetField(entry, "targetPath", target);
                list.Add(entry);
            }

            SetField(map, "bonePaths", list);
            return map;
        }

        private static EquipmentVisualProfile CreateSkinnedProfile(DefinitionId id, GameObject prefab, RigBindingMap map,
            DefinitionId bodyRegionId = default, IEnumerable<DefinitionId> hiddenRegions = null)
        {
            var profile = ScriptableObject.CreateInstance<EquipmentVisualProfile>();
            SetField(profile, "id", id.Value);
            SetField(profile, "kind", EquipmentVisualKind.Skinned);
            SetField(profile, "visualPrefab", prefab);
            SetField(profile, "boneMap", map);
            SetField(profile, "bodyRegionId", bodyRegionId);
            SetField(profile, "hiddenBodyRegions", hiddenRegions != null ? new List<DefinitionId>(hiddenRegions) : new List<DefinitionId>());
            return profile;
        }

        private static EquipmentVisualProfile CreateRigidProfile(DefinitionId id, GameObject prefab, DefinitionId socketId)
        {
            var profile = ScriptableObject.CreateInstance<EquipmentVisualProfile>();
            SetField(profile, "id", id.Value);
            SetField(profile, "kind", EquipmentVisualKind.Rigid);
            SetField(profile, "visualPrefab", prefab);
            SetField(profile, "socketId", socketId);
            SetField(profile, "localScale", Vector3.one);
            SetField(profile, "hiddenBodyRegions", new List<DefinitionId>());
            return profile;
        }

        private static ItemDefinition CreateItem(DefinitionId id, DefinitionId slotId, EquipmentVisualProfile profile)
        {
            var item = ScriptableObject.CreateInstance<ItemDefinition>();
            SetField(item, "id", id.Value);
            SetField(item, "equipmentSlotId", slotId);
            SetField(item, "equipmentVisualProfileId", profile.Id);
            return item;
        }

        private static GameEventContext EquipContext(RuntimeEntityId character, DefinitionId itemDefinitionId, DefinitionId slotId)
        {
            var context = new GameEventContext(GameEvent.ItemEquipped, character, character);
            context.Data["ItemDefinitionId"] = itemDefinitionId;
            context.Data["SlotId"] = slotId;
            return context;
        }

        private static GameEventContext UnequipContext(RuntimeEntityId character, DefinitionId slotId)
        {
            var context = new GameEventContext(GameEvent.ItemUnequipped, character, character);
            context.Data["SlotId"] = slotId;
            return context;
        }

        // ---- Rigid attachment ----

        [Test]
        public void RigidAttachmentBindsToTheNamedSocket()
        {
            var equipment = CreateBoundCharacter();
            var rulesDatabase = new RulesDatabase();
            var bus = new GameEventBus();
            var slotId = new DefinitionId("slot-mainhand");
            var weapon = Track(new GameObject("Weapon"));
            var profile = CreateRigidProfile(new DefinitionId("visual-weapon"), weapon, new DefinitionId("socket-right-hand"));
            var item = CreateItem(new DefinitionId("item-weapon"), slotId, profile);
            rulesDatabase.Register(profile);
            rulesDatabase.Register(item);
            var character = RuntimeEntityId.NewId();
            equipment.Initialize(character, rulesDatabase, new PartyInventoryState(), bus);

            bus.Publish(EquipContext(character, item.Id, slotId));

            var socket = characterGo.transform.Find("Hips/Spine/LeftHand/socket-right-hand");
            Assert.AreEqual(1, socket.childCount, "The weapon must be instantiated under the named socket Transform.");
        }

        [Test]
        public void RigidAttachmentWithAMissingSocketFailsWithoutInstantiatingAnything()
        {
            var equipment = CreateBoundCharacter();
            var rulesDatabase = new RulesDatabase();
            var bus = new GameEventBus();
            var slotId = new DefinitionId("slot-mainhand");
            var weapon = Track(new GameObject("Weapon"));
            var profile = CreateRigidProfile(new DefinitionId("visual-missing-socket"), weapon, new DefinitionId("socket-does-not-exist"));
            var item = CreateItem(new DefinitionId("item-weapon-bad-socket"), slotId, profile);
            rulesDatabase.Register(profile);
            rulesDatabase.Register(item);
            var character = RuntimeEntityId.NewId();
            equipment.Initialize(character, rulesDatabase, new PartyInventoryState(), bus);
            var transformCountBefore = characterGo.GetComponentsInChildren<Transform>(true).Length;

            Assert.DoesNotThrow(() => bus.Publish(EquipContext(character, item.Id, slotId)));

            Assert.AreEqual(transformCountBefore, characterGo.GetComponentsInChildren<Transform>(true).Length,
                "A missing socket must not instantiate anything under the character.");
        }

        // ---- Skinned attachment: missing/duplicate paths, before touching any renderer ----

        [Test]
        public void SkinnedAttachmentRebindsRendererBonesByExplicitPath()
        {
            var equipment = CreateBoundCharacter();
            var rulesDatabase = new RulesDatabase();
            var bus = new GameEventBus();
            var slotId = new DefinitionId("slot-body");
            var garment = CreateGarmentWithBone("GarmentSpine", out var garmentBone);
            var map = CreateMap(new DefinitionId("map-body"), DefinitionId.None, ("GarmentSpine", "Hips/Spine"));
            var profile = CreateSkinnedProfile(new DefinitionId("visual-body"), garment, map);
            var item = CreateItem(new DefinitionId("item-body"), slotId, profile);
            rulesDatabase.Register(map);
            rulesDatabase.Register(profile);
            rulesDatabase.Register(item);
            var character = RuntimeEntityId.NewId();
            equipment.Initialize(character, rulesDatabase, new PartyInventoryState(), bus);

            bus.Publish(EquipContext(character, item.Id, slotId));

            var instantiatedRenderer = characterGo.GetComponentInChildren<SkinnedMeshRenderer>(true);
            Assert.IsNotNull(instantiatedRenderer);
            var expectedTarget = characterGo.transform.Find("Hips/Spine");
            Assert.AreSame(expectedTarget, instantiatedRenderer.bones[0], "The renderer's bone must be rebound to the CHARACTER's target Transform, not left pointing at the garment's own copy.");
            Assert.AreSame(expectedTarget, instantiatedRenderer.rootBone);
        }

        [Test]
        public void SkinnedAttachmentRejectsAMissingTargetBonePathBeforeTouchingAnyRenderer()
        {
            var equipment = CreateBoundCharacter();
            var rulesDatabase = new RulesDatabase();
            var bus = new GameEventBus();
            var slotId = new DefinitionId("slot-body");
            var garment = CreateGarmentWithBone("GarmentSpine", out _);
            var originalBoneRef = garment.GetComponentInChildren<SkinnedMeshRenderer>().bones[0];
            var map = CreateMap(new DefinitionId("map-missing-target"), DefinitionId.None, ("GarmentSpine", "Hips/DoesNotExist"));
            var profile = CreateSkinnedProfile(new DefinitionId("visual-missing-target"), garment, map);
            var item = CreateItem(new DefinitionId("item-missing-target"), slotId, profile);
            rulesDatabase.Register(map);
            rulesDatabase.Register(profile);
            rulesDatabase.Register(item);
            var character = RuntimeEntityId.NewId();
            equipment.Initialize(character, rulesDatabase, new PartyInventoryState(), bus);

            Assert.DoesNotThrow(() => bus.Publish(EquipContext(character, item.Id, slotId)));

            Assert.IsNull(characterGo.GetComponentInChildren<SkinnedMeshRenderer>(true), "A missing target bone path must reject the whole binding before instantiating anything under the character.");
            Assert.AreSame(originalBoneRef, garment.GetComponentInChildren<SkinnedMeshRenderer>().bones[0], "The garment's own (never-instantiated) template must be untouched.");
        }

        [Test]
        public void SkinnedAttachmentRejectsAMissingSourceBonePathBeforeTouchingAnyRenderer()
        {
            var equipment = CreateBoundCharacter();
            var rulesDatabase = new RulesDatabase();
            var bus = new GameEventBus();
            var slotId = new DefinitionId("slot-body");
            var garment = CreateGarmentWithBone("GarmentSpine", out _);
            var map = CreateMap(new DefinitionId("map-missing-source"), DefinitionId.None, ("BoneNotOnGarment", "Hips/Spine"));
            var profile = CreateSkinnedProfile(new DefinitionId("visual-missing-source"), garment, map);
            var item = CreateItem(new DefinitionId("item-missing-source"), slotId, profile);
            rulesDatabase.Register(map);
            rulesDatabase.Register(profile);
            rulesDatabase.Register(item);
            var character = RuntimeEntityId.NewId();
            equipment.Initialize(character, rulesDatabase, new PartyInventoryState(), bus);

            Assert.DoesNotThrow(() => bus.Publish(EquipContext(character, item.Id, slotId)));

            Assert.IsNull(characterGo.GetComponentInChildren<SkinnedMeshRenderer>(true), "A missing source bone path must reject the whole binding.");
        }

        [Test]
        public void SkinnedAttachmentRejectsADuplicateTargetBonePath()
        {
            var equipment = CreateBoundCharacter();
            var rulesDatabase = new RulesDatabase();
            var bus = new GameEventBus();
            var slotId = new DefinitionId("slot-body");
            var garment = CreateGarmentWithBone("GarmentSpine", out _);
            var map = CreateMap(new DefinitionId("map-duplicate-target"), DefinitionId.None,
                ("GarmentSpine", "Hips/Spine"), ("GarmentSpine2", "Hips/Spine"));
            var profile = CreateSkinnedProfile(new DefinitionId("visual-duplicate-target"), garment, map);
            var item = CreateItem(new DefinitionId("item-duplicate-target"), slotId, profile);
            rulesDatabase.Register(map);
            rulesDatabase.Register(profile);
            rulesDatabase.Register(item);
            var character = RuntimeEntityId.NewId();
            equipment.Initialize(character, rulesDatabase, new PartyInventoryState(), bus);

            Assert.DoesNotThrow(() => bus.Publish(EquipContext(character, item.Id, slotId)));

            Assert.IsNull(characterGo.GetComponentInChildren<SkinnedMeshRenderer>(true), "A structurally-invalid (duplicate-target) map must be rejected before touching any renderer.");
        }

        [Test]
        public void SkinnedAttachmentRejectsAnIncompatibleRigFamily()
        {
            var characterRig = CreateRig("rig-superhero");
            var equipment = CreateBoundCharacter(characterRig);
            var rulesDatabase = new RulesDatabase();
            var bus = new GameEventBus();
            var slotId = new DefinitionId("slot-body");
            var garment = CreateGarmentWithBone("GarmentSpine", out _);
            var map = CreateMap(new DefinitionId("map-wrong-family"), new DefinitionId("rig-wrong-family"), ("GarmentSpine", "Hips/Spine"));
            var profile = CreateSkinnedProfile(new DefinitionId("visual-wrong-family"), garment, map);
            var item = CreateItem(new DefinitionId("item-wrong-family"), slotId, profile);
            rulesDatabase.Register(map);
            rulesDatabase.Register(profile);
            rulesDatabase.Register(item);
            var character = RuntimeEntityId.NewId();
            equipment.Initialize(character, rulesDatabase, new PartyInventoryState(), bus);

            Assert.DoesNotThrow(() => bus.Publish(EquipContext(character, item.Id, slotId)));

            Assert.IsNull(characterGo.GetComponentInChildren<SkinnedMeshRenderer>(true), "A rig-family mismatch must reject the binding before touching any renderer.");
        }

        [Test]
        public void SkinnedAttachmentSucceedsWhenNeitherSideDeclaresARigFamily()
        {
            // Opt-in check (matches CharacterRigDefinition's own "not yet consumed by any validator" starting
            // point): DefinitionId.None on either side must not spuriously reject real content that simply
            // hasn't authored rig families yet.
            var equipment = CreateBoundCharacter(rig: null);
            var rulesDatabase = new RulesDatabase();
            var bus = new GameEventBus();
            var slotId = new DefinitionId("slot-body");
            var garment = CreateGarmentWithBone("GarmentSpine", out _);
            var map = CreateMap(new DefinitionId("map-no-family"), DefinitionId.None, ("GarmentSpine", "Hips/Spine"));
            var profile = CreateSkinnedProfile(new DefinitionId("visual-no-family"), garment, map);
            var item = CreateItem(new DefinitionId("item-no-family"), slotId, profile);
            rulesDatabase.Register(map);
            rulesDatabase.Register(profile);
            rulesDatabase.Register(item);
            var character = RuntimeEntityId.NewId();
            equipment.Initialize(character, rulesDatabase, new PartyInventoryState(), bus);

            bus.Publish(EquipContext(character, item.Id, slotId));

            Assert.IsNotNull(characterGo.GetComponentInChildren<SkinnedMeshRenderer>(true));
        }

        private static CharacterRigDefinition CreateRig(string id)
        {
            var rig = ScriptableObject.CreateInstance<CharacterRigDefinition>();
            SetField(rig, "id", id);
            return rig;
        }

        // ---- Body-region hiding, reference-counted ----

        [UnityTest]
        public IEnumerator TwoGarmentsHidingTheSameRegionOnlyRevealItWhenTheLastOneIsRemoved()
        {
            var equipment = CreateBoundCharacter();
            var rulesDatabase = new RulesDatabase();
            var bus = new GameEventBus();
            var legsRegion = new DefinitionId("region-legs");
            var character = RuntimeEntityId.NewId();
            equipment.Initialize(character, rulesDatabase, new PartyInventoryState(), bus);

            // "trousers" occupies region-legs; a long "robe" and a "cloak" both separately hide region-legs.
            var trousersSlot = new DefinitionId("slot-legs");
            var trousersMap = CreateMap(new DefinitionId("map-trousers"), DefinitionId.None, ("B", "Hips/Spine"));
            var trousersProfile = CreateSkinnedProfile(new DefinitionId("visual-trousers"), CreateGarmentWithBone("B", out _), trousersMap, bodyRegionId: legsRegion);
            var trousers = CreateItem(new DefinitionId("item-trousers"), trousersSlot, trousersProfile);

            var robeSlot = new DefinitionId("slot-robe");
            var robeMap = CreateMap(new DefinitionId("map-robe"), DefinitionId.None, ("B", "Hips/Spine"));
            var robeProfile = CreateSkinnedProfile(new DefinitionId("visual-robe"), CreateGarmentWithBone("B", out _), robeMap, hiddenRegions: new[] { legsRegion });
            var robe = CreateItem(new DefinitionId("item-robe"), robeSlot, robeProfile);

            var cloakSlot = new DefinitionId("slot-cloak");
            var cloakMap = CreateMap(new DefinitionId("map-cloak"), DefinitionId.None, ("B", "Hips/Spine"));
            var cloakProfile = CreateSkinnedProfile(new DefinitionId("visual-cloak"), CreateGarmentWithBone("B", out _), cloakMap, hiddenRegions: new[] { legsRegion });
            var cloak = CreateItem(new DefinitionId("item-cloak"), cloakSlot, cloakProfile);

            foreach (var m in new[] { trousersMap, robeMap, cloakMap }) rulesDatabase.Register(m);
            foreach (var p in new[] { trousersProfile, robeProfile, cloakProfile }) rulesDatabase.Register(p);
            foreach (var i in new[] { trousers, robe, cloak }) rulesDatabase.Register(i);

            bus.Publish(EquipContext(character, trousers.Id, trousersSlot));
            Assert.IsTrue(equipment.GetEquippedVisual(trousersSlot).activeSelf, "Trousers must be visible before anything hides region-legs.");

            bus.Publish(EquipContext(character, robe.Id, robeSlot));
            Assert.IsFalse(equipment.GetEquippedVisual(trousersSlot).activeSelf, "The robe hides region-legs -- trousers must now be hidden.");

            bus.Publish(EquipContext(character, cloak.Id, cloakSlot));
            Assert.IsFalse(equipment.GetEquippedVisual(trousersSlot).activeSelf, "A second hider must not change anything -- still hidden.");

            bus.Publish(UnequipContext(character, robeSlot)); // remove the FIRST hider first
            Assert.IsFalse(equipment.GetEquippedVisual(trousersSlot).activeSelf, "Removing one of two hiders must not reveal the region -- the cloak still covers it.");

            bus.Publish(UnequipContext(character, cloakSlot)); // remove the LAST hider
            yield return null; // R5: the pending-removal flush that actually applies the region-effect decrement runs in LateUpdate
            Assert.IsTrue(equipment.GetEquippedVisual(trousersSlot).activeSelf, "Only removing the LAST hider must reveal the region again.");
        }

        [UnityTest]
        public IEnumerator RemovalOrderDoesNotMatterForRevealingAHiddenRegion()
        {
            var equipment = CreateBoundCharacter();
            var rulesDatabase = new RulesDatabase();
            var bus = new GameEventBus();
            var region = new DefinitionId("region-torso");
            var character = RuntimeEntityId.NewId();
            equipment.Initialize(character, rulesDatabase, new PartyInventoryState(), bus);

            var targetSlot = new DefinitionId("slot-torso");
            var targetMap = CreateMap(new DefinitionId("map-torso-target-2"), DefinitionId.None, ("B", "Hips/Spine"));
            var targetProfile = CreateSkinnedProfile(new DefinitionId("visual-torso-target-2"), CreateGarmentWithBone("B", out _), targetMap, bodyRegionId: region);
            var targetItem = CreateItem(new DefinitionId("item-torso-target-2"), targetSlot, targetProfile);

            var hiderASlot = new DefinitionId("slot-hider-a-2");
            var hiderAMap = CreateMap(new DefinitionId("map-hider-a-2"), DefinitionId.None, ("B", "Hips/Spine"));
            var hiderAProfile = CreateSkinnedProfile(new DefinitionId("visual-hider-a-2"), CreateGarmentWithBone("B", out _), hiderAMap, hiddenRegions: new[] { region });
            var hiderA = CreateItem(new DefinitionId("item-hider-a-2"), hiderASlot, hiderAProfile);

            var hiderBSlot = new DefinitionId("slot-hider-b-2");
            var hiderBMap = CreateMap(new DefinitionId("map-hider-b-2"), DefinitionId.None, ("B", "Hips/Spine"));
            var hiderBProfile = CreateSkinnedProfile(new DefinitionId("visual-hider-b-2"), CreateGarmentWithBone("B", out _), hiderBMap, hiddenRegions: new[] { region });
            var hiderB = CreateItem(new DefinitionId("item-hider-b-2"), hiderBSlot, hiderBProfile);

            foreach (var m in new[] { targetMap, hiderAMap, hiderBMap }) rulesDatabase.Register(m);
            foreach (var p in new[] { targetProfile, hiderAProfile, hiderBProfile }) rulesDatabase.Register(p);
            foreach (var i in new[] { targetItem, hiderA, hiderB }) rulesDatabase.Register(i);

            bus.Publish(EquipContext(character, targetItem.Id, targetSlot));
            bus.Publish(EquipContext(character, hiderA.Id, hiderASlot));
            bus.Publish(EquipContext(character, hiderB.Id, hiderBSlot));

            // Remove the SECOND-equipped hider first this time (opposite order from the test above).
            bus.Publish(UnequipContext(character, hiderBSlot));
            Assert.IsFalse(equipment.GetEquippedVisual(targetSlot).activeSelf, "hiderA still covers the region.");

            bus.Publish(UnequipContext(character, hiderASlot));
            yield return null; // R5: the pending-removal flush that actually applies the region-effect decrement runs in LateUpdate
            Assert.IsTrue(equipment.GetEquippedVisual(targetSlot).activeSelf, "Removing the last hider, regardless of which was equipped first, must reveal the region.");
        }

        // ---- Reconciliation ordering: load vs. view-bind ----

        [Test]
        public void LoadBeforeViewBindDefersUntilASkeletonIsBound()
        {
            characterGo = new GameObject("CharacterRoot");
            var hips = new GameObject("Hips");
            hips.transform.SetParent(characterGo.transform);
            var spine = new GameObject("Spine");
            spine.transform.SetParent(hips.transform);
            var equipment = characterGo.AddComponent<EquipmentVisualBinder>(); // no RefreshBoneMap yet -- no CharacterView has bound a Model

            var rulesDatabase = new RulesDatabase();
            var inventory = new PartyInventoryState();
            var characters = new CharacterRegistry();
            var equipmentService = new EquipmentService(characters, rulesDatabase, inventory);
            var slotId = new DefinitionId("slot-body");
            var map = CreateMap(new DefinitionId("map-load-before-bind"), DefinitionId.None, ("B", "Hips/Spine"));
            var profile = CreateSkinnedProfile(new DefinitionId("visual-load-before-bind"), CreateGarmentWithBone("B", out _), map);
            var item = CreateItem(new DefinitionId("item-load-before-bind"), slotId, profile);
            rulesDatabase.Register(map);
            rulesDatabase.Register(profile);
            rulesDatabase.Register(item);
            var character = RuntimeEntityId.NewId();
            var instance = new ItemInstance(RuntimeEntityId.NewId(), item.Id);
            inventory.AddItem(instance);
            equipmentService.Equip(instance.InstanceId, character, slotId); // "load" -- inventory state already reflects this equip

            Assert.DoesNotThrow(() => equipment.Initialize(character, rulesDatabase, inventory, new GameEventBus()));
            Assert.IsNull(characterGo.GetComponentInChildren<SkinnedMeshRenderer>(true), "With no skeleton bound yet, nothing should be instantiated -- and nothing should throw.");

            equipment.RefreshBoneMap(null); // "view bind" happens afterward

            Assert.IsNotNull(characterGo.GetComponentInChildren<SkinnedMeshRenderer>(true), "Once a skeleton binds, reconciliation must pick up the already-equipped item from inventory.");
        }

        [Test]
        public void ViewBindBeforeLoadReconcilesAlreadyEquippedItemsOnInitialize()
        {
            var equipment = CreateBoundCharacter(); // skeleton bound first ("view bind")

            var rulesDatabase = new RulesDatabase();
            var inventory = new PartyInventoryState();
            var characters = new CharacterRegistry();
            var equipmentService = new EquipmentService(characters, rulesDatabase, inventory);
            var slotId = new DefinitionId("slot-body");
            var map = CreateMap(new DefinitionId("map-view-before-load"), DefinitionId.None, ("GarmentSpine", "Hips/Spine"));
            var profile = CreateSkinnedProfile(new DefinitionId("visual-view-before-load"), CreateGarmentWithBone("GarmentSpine", out _), map);
            var item = CreateItem(new DefinitionId("item-view-before-load"), slotId, profile);
            rulesDatabase.Register(map);
            rulesDatabase.Register(profile);
            rulesDatabase.Register(item);
            var character = RuntimeEntityId.NewId();
            var instance = new ItemInstance(RuntimeEntityId.NewId(), item.Id);
            inventory.AddItem(instance);
            equipmentService.Equip(instance.InstanceId, character, slotId); // "load" happens before Initialize

            equipment.Initialize(character, rulesDatabase, inventory, new GameEventBus());

            Assert.IsNotNull(characterGo.GetComponentInChildren<SkinnedMeshRenderer>(true), "Initialize's own reconciliation must pick up items already equipped in inventory.");
        }

        // ---- Visual failure retains the known-good appearance ----

        [Test]
        public void AFailedReplacementLeavesThePreviousValidVisualInPlace()
        {
            var equipment = CreateBoundCharacter();
            var rulesDatabase = new RulesDatabase();
            var bus = new GameEventBus();
            var slotId = new DefinitionId("slot-body");
            var character = RuntimeEntityId.NewId();
            equipment.Initialize(character, rulesDatabase, new PartyInventoryState(), bus);

            var goodMap = CreateMap(new DefinitionId("map-good"), DefinitionId.None, ("GarmentSpine", "Hips/Spine"));
            var goodProfile = CreateSkinnedProfile(new DefinitionId("visual-good"), CreateGarmentWithBone("GarmentSpine", out _), goodMap);
            var goodItem = CreateItem(new DefinitionId("item-good"), slotId, goodProfile);
            rulesDatabase.Register(goodMap);
            rulesDatabase.Register(goodProfile);
            rulesDatabase.Register(goodItem);

            bus.Publish(EquipContext(character, goodItem.Id, slotId));
            var goodVisual = characterGo.GetComponentInChildren<SkinnedMeshRenderer>(true);
            Assert.IsNotNull(goodVisual);

            var badMap = CreateMap(new DefinitionId("map-bad"), DefinitionId.None, ("GarmentSpine", "Hips/DoesNotExist"));
            var badProfile = CreateSkinnedProfile(new DefinitionId("visual-bad"), CreateGarmentWithBone("GarmentSpine", out _), badMap);
            var badItem = CreateItem(new DefinitionId("item-bad"), slotId, badProfile);
            rulesDatabase.Register(badMap);
            rulesDatabase.Register(badProfile);
            rulesDatabase.Register(badItem);

            // Same slot -- a naive implementation would remove the old visual before validating the new one.
            Assert.DoesNotThrow(() => bus.Publish(EquipContext(character, badItem.Id, slotId)));

            var visualsAfter = characterGo.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Assert.AreEqual(1, visualsAfter.Length, "The known-good visual must still be present; the invalid replacement must not have removed it.");
            Assert.AreSame(goodVisual, visualsAfter[0]);
        }

        // Phase 2 correction pass (R5): the test above only ever publishes a direct ItemEquipped with no
        // preceding ItemUnequipped for the slot -- that is NOT how a real same-slot replacement actually
        // happens. EquipmentService.Equip publishes ItemUnequipped for the displaced occupant FIRST, then
        // ItemEquipped for the replacement, both synchronously within one call. These tests go through the
        // real service instead of hand-built events, which is what actually exercises (and, before this
        // correction, would have failed to prove) "retains the known-good appearance."

        private static (EquipmentService service, PartyInventoryState inventory) CreateRealEquipmentService(RulesDatabase rulesDatabase, GameEventBus bus)
        {
            var inventory = new PartyInventoryState();
            var characters = new CharacterRegistry();
            return (new EquipmentService(characters, rulesDatabase, inventory, bus), inventory);
        }

        [Test]
        public void RealServiceReplacementWithAnInvalidNewItemRetainsTheKnownGoodVisual()
        {
            var equipment = CreateBoundCharacter();
            var rulesDatabase = new RulesDatabase();
            var bus = new GameEventBus();
            var (equipmentService, inventory) = CreateRealEquipmentService(rulesDatabase, bus);
            var slotId = new DefinitionId("slot-body");
            var character = RuntimeEntityId.NewId();
            equipment.Initialize(character, rulesDatabase, inventory, bus);

            var goodMap = CreateMap(new DefinitionId("map-real-good"), DefinitionId.None, ("GarmentSpine", "Hips/Spine"));
            var goodProfile = CreateSkinnedProfile(new DefinitionId("visual-real-good"), CreateGarmentWithBone("GarmentSpine", out _), goodMap);
            var goodItem = CreateItem(new DefinitionId("item-real-good"), slotId, goodProfile);
            rulesDatabase.Register(goodMap);
            rulesDatabase.Register(goodProfile);
            rulesDatabase.Register(goodItem);
            var goodInstance = new ItemInstance(RuntimeEntityId.NewId(), goodItem.Id);
            inventory.AddItem(goodInstance);
            equipmentService.Equip(goodInstance.InstanceId, character, slotId);
            var goodVisual = characterGo.GetComponentInChildren<SkinnedMeshRenderer>(true);
            Assert.IsNotNull(goodVisual);

            var badMap = CreateMap(new DefinitionId("map-real-bad"), DefinitionId.None, ("GarmentSpine", "Hips/DoesNotExist"));
            var badProfile = CreateSkinnedProfile(new DefinitionId("visual-real-bad"), CreateGarmentWithBone("GarmentSpine", out _), badMap);
            var badItem = CreateItem(new DefinitionId("item-real-bad"), slotId, badProfile);
            rulesDatabase.Register(badMap);
            rulesDatabase.Register(badProfile);
            rulesDatabase.Register(badItem);
            var badInstance = new ItemInstance(RuntimeEntityId.NewId(), badItem.Id);
            inventory.AddItem(badInstance);

            // The real replacement path: EquipmentService.Equip publishes Unequipped(good) THEN Equipped(bad)
            // for the same slot, synchronously, within this one call.
            Assert.DoesNotThrow(() => equipmentService.Equip(badInstance.InstanceId, character, slotId));

            var visualsAfter = characterGo.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Assert.AreEqual(1, visualsAfter.Length, "The known-good visual must survive a REAL same-slot replacement with invalid content.");
            Assert.AreSame(goodVisual, visualsAfter[0]);

            // Gameplay equip state is intentionally decoupled from visual success: EquipmentService's own
            // policy always applies regardless of whether the visual bound -- confirming the bad item DID
            // become the gameplay-equipped occupant, even while its visual is rejected.
            Assert.IsTrue(inventory.GetLocation(badInstance.InstanceId).IsEquipped);
            Assert.AreEqual(slotId, inventory.GetLocation(badInstance.InstanceId).Slot);
        }

        [UnityTest]
        public IEnumerator RealServiceReplacementWithAValidNewItemSwapsToTheNewVisualAndReleasesTheOldOneByEndOfFrame()
        {
            var equipment = CreateBoundCharacter();
            var rulesDatabase = new RulesDatabase();
            var bus = new GameEventBus();
            var (equipmentService, inventory) = CreateRealEquipmentService(rulesDatabase, bus);
            var slotId = new DefinitionId("slot-body");
            var character = RuntimeEntityId.NewId();
            equipment.Initialize(character, rulesDatabase, inventory, bus);

            var mapA = CreateMap(new DefinitionId("map-real-a"), DefinitionId.None, ("GarmentSpine", "Hips/Spine"));
            var profileA = CreateSkinnedProfile(new DefinitionId("visual-real-a"), CreateGarmentWithBone("GarmentSpine", out _), mapA);
            var itemA = CreateItem(new DefinitionId("item-real-a"), slotId, profileA);
            rulesDatabase.Register(mapA);
            rulesDatabase.Register(profileA);
            rulesDatabase.Register(itemA);
            var instanceA = new ItemInstance(RuntimeEntityId.NewId(), itemA.Id);
            inventory.AddItem(instanceA);
            equipmentService.Equip(instanceA.InstanceId, character, slotId);
            var visualA = equipment.GetEquippedVisual(slotId);
            Assert.IsNotNull(visualA);

            var mapB = CreateMap(new DefinitionId("map-real-b"), DefinitionId.None, ("GarmentSpine", "Hips/Spine"));
            var profileB = CreateSkinnedProfile(new DefinitionId("visual-real-b"), CreateGarmentWithBone("GarmentSpine", out _), mapB);
            var itemB = CreateItem(new DefinitionId("item-real-b"), slotId, profileB);
            rulesDatabase.Register(mapB);
            rulesDatabase.Register(profileB);
            rulesDatabase.Register(itemB);
            var instanceB = new ItemInstance(RuntimeEntityId.NewId(), itemB.Id);
            inventory.AddItem(instanceB);
            equipmentService.Equip(instanceB.InstanceId, character, slotId);

            var visualB = equipment.GetEquippedVisual(slotId);
            Assert.IsNotNull(visualB);
            Assert.AreNotSame(visualA, visualB, "The slot must now show the NEW visual.");
            Assert.IsTrue(visualA != null, "The old visual must not have been destroyed yet -- it's still pending until end of frame (in case something else reclaims it first).");

            yield return null; // let LateUpdate flush the now-genuinely-abandoned old visual

            Assert.IsTrue(visualA == null, "The old visual must be destroyed once the frame confirms nothing reclaimed it.");
        }

        [UnityTest]
        public IEnumerator AGenuineUnequipWithNoReplacementDestroysTheVisualByEndOfFrame()
        {
            var equipment = CreateBoundCharacter();
            var rulesDatabase = new RulesDatabase();
            var bus = new GameEventBus();
            var (equipmentService, inventory) = CreateRealEquipmentService(rulesDatabase, bus);
            var slotId = new DefinitionId("slot-body");
            var character = RuntimeEntityId.NewId();
            equipment.Initialize(character, rulesDatabase, inventory, bus);

            var map = CreateMap(new DefinitionId("map-real-unequip"), DefinitionId.None, ("GarmentSpine", "Hips/Spine"));
            var profile = CreateSkinnedProfile(new DefinitionId("visual-real-unequip"), CreateGarmentWithBone("GarmentSpine", out _), map);
            var item = CreateItem(new DefinitionId("item-real-unequip"), slotId, profile);
            rulesDatabase.Register(map);
            rulesDatabase.Register(profile);
            rulesDatabase.Register(item);
            var instance = new ItemInstance(RuntimeEntityId.NewId(), item.Id);
            inventory.AddItem(instance);
            equipmentService.Equip(instance.InstanceId, character, slotId);
            var visual = equipment.GetEquippedVisual(slotId);
            Assert.IsNotNull(visual);

            equipmentService.Unequip(instance.InstanceId); // no replacement follows

            Assert.IsNull(equipment.GetEquippedVisual(slotId), "The tracking dictionary must reflect the slot as empty immediately.");
            Assert.IsTrue(visual != null, "The GameObject itself must not be destroyed yet -- still pending in case of a same-frame replacement.");

            yield return null; // let LateUpdate flush it -- nothing reclaimed it, so it's a genuine unequip

            Assert.IsTrue(visual == null, "A genuine unequip with no replacement must actually destroy the visual by end of frame.");
        }

        // ---- R6: full renderer-bone coverage ----

        [Test]
        public void SkinnedAttachmentRejectsARendererBoneNotCoveredByTheMap()
        {
            var equipment = CreateBoundCharacter();
            var rulesDatabase = new RulesDatabase();
            var bus = new GameEventBus();
            var slotId = new DefinitionId("slot-body");
            var character = RuntimeEntityId.NewId();
            equipment.Initialize(character, rulesDatabase, new PartyInventoryState(), bus);

            // Two bones on the renderer, but the map only covers one -- the OLD behavior would have silently
            // left the second bone pointed at the garment's own (unrebound) copy instead of rejecting the
            // whole binding.
            var garment = Track(new GameObject("Garment"));
            var boneA = new GameObject("BoneA");
            boneA.transform.SetParent(garment.transform);
            var boneB = new GameObject("BoneB");
            boneB.transform.SetParent(garment.transform);
            var renderer = garment.AddComponent<SkinnedMeshRenderer>();
            renderer.bones = new[] { boneA.transform, boneB.transform };
            renderer.rootBone = boneA.transform;

            var map = CreateMap(new DefinitionId("map-incomplete-coverage"), DefinitionId.None, ("BoneA", "Hips"));
            var profile = CreateSkinnedProfile(new DefinitionId("visual-incomplete-coverage"), garment, map);
            var item = CreateItem(new DefinitionId("item-incomplete-coverage"), slotId, profile);
            rulesDatabase.Register(map);
            rulesDatabase.Register(profile);
            rulesDatabase.Register(item);

            Assert.DoesNotThrow(() => bus.Publish(EquipContext(character, item.Id, slotId)));

            Assert.IsNull(characterGo.GetComponentInChildren<SkinnedMeshRenderer>(true),
                "A renderer bone not covered by the map must reject the whole binding, not leave that one bone half-bound.");
        }
    }
}

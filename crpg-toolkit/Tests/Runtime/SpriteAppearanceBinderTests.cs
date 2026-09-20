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
    /// <summary>2D campaign Task 2.4: SpriteAppearanceBinder applying CharacterState.Appearance through catalogs, and the
    /// WorldSpawnService wiring on the spawn and restore paths.</summary>
    public class SpriteAppearanceBinderTests
    {
        private static readonly DefinitionId Body = new("layer-body");
        private static readonly DefinitionId Hair = new("layer-hair");
        private static readonly DefinitionId Helmet = new("layer-helmet");
        private static readonly DefinitionId Head = new("slot-head");

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

        private Sprite NewSprite(string name)
        {
            var texture = Track(new Texture2D(4, 4) { name = name });
            var sprite = Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
            sprite.name = name;
            return Track(sprite);
        }

        private SpriteLayerCatalog Catalog(RulesDatabase rules, DefinitionId slot, params (string option, Sprite sprite)[] options)
        {
            var catalog = Track(ScriptableObject.CreateInstance<SpriteLayerCatalog>());
            SetField(catalog, "id", "catalog-" + slot.Value + "-" + serial++);
            SetField(catalog, "layerSlot", slot);
            SetField(catalog, "options", options.Select(o => new SpriteOptionEntry(new DefinitionId(o.option), o.option, o.sprite, Color.white)).ToList());
            rules.Register(catalog);
            return catalog;
        }

        private sealed class Rig
        {
            public GameObject Root;
            public SpriteLayerCompositor Compositor;
            public SpriteAppearanceBinder Binder;
            public SpriteLayerState State => Compositor.State;
        }

        private Rig NewRig()
        {
            var root = Track(new GameObject("SpriteCharacter"));
            var compositor = root.AddComponent<SpriteLayerCompositor>();
            var binder = root.AddComponent<SpriteAppearanceBinder>();
            return new Rig { Root = root, Compositor = compositor, Binder = binder };
        }

        private static EffectiveLayer LayerOf(SpriteLayerState state, DefinitionId slot) => state.Layers.First(l => l.Slot == slot);

        private static IEnumerator Frames(int count)
        {
            for (var i = 0; i < count; i++) yield return null;
        }

        // ---------------------------------------------------------------------------------------------
        // Binder
        // ---------------------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator AppearanceIsAppliedAtInitializeAndRefreshPicksUpLaterChanges()
        {
            var rules = new RulesDatabase();
            var body = NewSprite("body");
            var hairA = NewSprite("hair-a");
            var hairB = NewSprite("hair-b");
            Catalog(rules, Body, ("body-1", body));
            Catalog(rules, Hair, ("hair-a", hairA), ("hair-b", hairB));
            var registry = new CharacterRegistry();
            var id = RuntimeEntityId.NewId();
            var character = new CharacterState(id);
            character.SetAppearance(Body, new DefinitionId("body-1"));
            character.SetAppearance(Hair, new DefinitionId("hair-a"));
            registry.Register(character);
            var rig = NewRig();

            rig.Binder.Initialize(id, registry, rules);
            yield return null;

            Assert.AreSame(body, LayerOf(rig.State, Body).Sprite);
            Assert.AreSame(hairA, LayerOf(rig.State, Hair).Sprite);
            Assert.IsTrue(rig.Compositor.Renderers[0].enabled, "and the compositor presents it");

            character.SetAppearance(Hair, new DefinitionId("hair-b"));
            yield return Frames(2);
            Assert.AreSame(hairA, LayerOf(rig.State, Hair).Sprite, "there is no subscription: a change after bind is not shown until Refresh");

            rig.Binder.Refresh();
            Assert.AreSame(hairB, LayerOf(rig.State, Hair).Sprite);
        }

        [UnityTest]
        public IEnumerator AnUnknownOptionLeavesThatLayerEmptyReportsOnceAndTheRestStillApply()
        {
            var rules = new RulesDatabase();
            var body = NewSprite("body");
            Catalog(rules, Body, ("body-1", body));
            var registry = new CharacterRegistry();
            var id = RuntimeEntityId.NewId();
            var character = new CharacterState(id);
            character.SetAppearance(Body, new DefinitionId("body-1"));
            character.SetAppearance(Hair, new DefinitionId("hair-was-renamed")); // no hair catalog at all
            registry.Register(character);
            var rig = NewRig();

            LogAssert.Expect(LogType.Warning, new Regex("hair-was-renamed.*unknown"));
            rig.Binder.Initialize(id, registry, rules);
            yield return Frames(3);

            Assert.AreSame(body, LayerOf(rig.State, Body).Sprite);
            Assert.IsFalse(LayerOf(rig.State, Hair).Visible);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator AModelSwapThatDestroysTheCompositorGetsTheAppearanceOnTheNewOne()
        {
            var rules = new RulesDatabase();
            var body = NewSprite("body");
            Catalog(rules, Body, ("body-1", body));
            var registry = new CharacterRegistry();
            var id = RuntimeEntityId.NewId();
            var character = new CharacterState(id);
            character.SetAppearance(Body, new DefinitionId("body-1"));
            registry.Register(character);

            var root = Track(new GameObject("CharacterRoot"));
            var binder = root.AddComponent<SpriteAppearanceBinder>();
            var oldModel = new GameObject("Model");
            oldModel.transform.SetParent(root.transform, false);
            oldModel.AddComponent<SpriteLayerCompositor>();
            binder.Initialize(id, registry, rules);
            yield return Frames(2);

            Object.DestroyImmediate(oldModel);
            var newModel = Track(new GameObject("Model"));
            newModel.transform.SetParent(root.transform, false);
            var newCompositor = newModel.AddComponent<SpriteLayerCompositor>();
            yield return Frames(3);

            Assert.AreSame(body, LayerOf(newCompositor.State, Body).Sprite);
        }

        [UnityTest]
        public IEnumerator TheInitializeContractAndUnbindLeavingLayersAlone()
        {
            var rules = new RulesDatabase();
            var body = NewSprite("body");
            Catalog(rules, Body, ("body-1", body));
            var registry = new CharacterRegistry();
            var id = RuntimeEntityId.NewId();
            var character = new CharacterState(id);
            character.SetAppearance(Body, new DefinitionId("body-1"));
            registry.Register(character);
            var rig = NewRig();

            LogAssert.Expect(LogType.Warning, new Regex("Cannot bind"));
            rig.Binder.Initialize(RuntimeEntityId.None, registry, rules);
            Assert.IsFalse(rig.Binder.IsBound);

            rig.Binder.Initialize(id, registry, rules);
            Assert.IsTrue(rig.Binder.IsBound);
            rig.Binder.Initialize(id, registry, rules); // identical: no-op
            Assert.Throws<InvalidOperationException>(() => rig.Binder.Initialize(RuntimeEntityId.NewId(), registry, rules));
            yield return null;

            rig.Binder.Unbind();
            Assert.IsFalse(rig.Binder.IsBound);
            Assert.AreSame(body, LayerOf(rig.State, Body).Sprite, "the binder does not own the appearance, so unbinding leaves it");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator ACharacterMissingFromTheRegistryWarnsOnceAndAppliesNothing()
        {
            var rules = new RulesDatabase();
            Catalog(rules, Body, ("body-1", NewSprite("body")));
            var rig = NewRig();

            LogAssert.Expect(LogType.Warning, new Regex("is not in the registry"));
            rig.Binder.Initialize(RuntimeEntityId.NewId(), new CharacterRegistry(), rules);
            yield return Frames(3);

            Assert.IsFalse(rig.State.Layers.Any(l => l.Visible));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator NoCompositorAndNoTargetWarnsOnceAndNeverThrows()
        {
            var rules = new RulesDatabase();
            var registry = new CharacterRegistry();
            var id = RuntimeEntityId.NewId();
            registry.Register(new CharacterState(id));
            var root = Track(new GameObject("Bare"));
            var binder = root.AddComponent<SpriteAppearanceBinder>();

            LogAssert.Expect(LogType.Warning, new Regex("no SpriteLayerCompositor"));
            binder.Initialize(id, registry, rules);
            Assert.DoesNotThrow(() => binder.Refresh());
            yield return Frames(5);

            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator SetTargetAppliesToAnExplicitStateForARendererlessPreview()
        {
            var rules = new RulesDatabase();
            var body = NewSprite("body");
            Catalog(rules, Body, ("body-1", body));
            var registry = new CharacterRegistry();
            var id = RuntimeEntityId.NewId();
            var character = new CharacterState(id);
            character.SetAppearance(Body, new DefinitionId("body-1"));
            registry.Register(character);
            var root = Track(new GameObject("Bare"));
            var binder = root.AddComponent<SpriteAppearanceBinder>();
            var preview = new SpriteLayerState(SpriteLayerCompositor.DefaultSlots());

            binder.Initialize(id, registry, rules);
            binder.SetTarget(preview);
            yield return null;

            Assert.AreSame(body, LayerOf(preview, Body).Sprite);
            LogAssert.NoUnexpectedReceived();
        }

        // ---------------------------------------------------------------------------------------------
        // WorldSpawnService wiring
        // ---------------------------------------------------------------------------------------------

        private GameObject NewTemplate(bool withEquipmentBinder = false)
        {
            var template = Track(new GameObject("Template"));
            template.SetActive(false);
            var model = new GameObject("Model");
            model.transform.SetParent(template.transform, false);
            model.AddComponent<SpriteLayerCompositor>();
            model.AddComponent<SpriteAppearanceBinder>();
            if (withEquipmentBinder) model.AddComponent<SpriteEquipmentBinder>();
            return template;
        }

        private (GameSession session, RulesDatabase rules, WorldSpawnService spawner) NewSession()
        {
            var rules = new RulesDatabase();
            var hostGo = Track(new GameObject("Host"));
            var host = hostGo.AddComponent<GameSessionHost>();
            var session = host.StartSession(rules, new DiceService(new SeededRandomSource(1)));
            return (session, rules, new WorldSpawnService(session));
        }

        [UnityTest]
        public IEnumerator SpawnNpcOfAnNpcWithAuthoredAppearanceShowsIt()
        {
            var (session, rules, spawner) = NewSession();
            var body = NewSprite("body");
            var hair = NewSprite("hair");
            Catalog(rules, Body, ("body-1", body));
            Catalog(rules, Hair, ("hair-1", hair));

            var definition = Track(ScriptableObject.CreateInstance<NpcDefinition>());
            SetField(definition, "id", "npc-appearance-test");
            SetField(definition, "displayName", "Test NPC");
            SetField(definition, "visualPrefab", NewTemplate());
            SetField(definition, "appearance", new List<AppearanceEntry> { new(Body, new DefinitionId("body-1")), new(Hair, new DefinitionId("hair-1")) });

            var agent = spawner.SpawnNpc("spawn-appearance", definition, Vector3.zero, Quaternion.identity);
            Track(agent.gameObject);
            yield return null;

            var state = agent.GetComponentInChildren<SpriteLayerCompositor>(true).State;
            Assert.AreSame(body, LayerOf(state, Body).Sprite);
            Assert.AreSame(hair, LayerOf(state, Hair).Sprite);
        }

        [UnityTest]
        public IEnumerator BindRestoredViewOfARestoredCharacterShowsItsSavedAppearanceOnceActivated()
        {
            var (session, rules, spawner) = NewSession();
            var body = NewSprite("body");
            Catalog(rules, Body, ("body-1", body));
            var id = RuntimeEntityId.NewId();
            var character = new CharacterState(id);
            character.SetAppearance(Body, new DefinitionId("body-1")); // what SaveService.Restore produced
            session.Characters.Register(character);
            session.Party.AddMember(id);

            var instance = spawner.BindRestoredView(id.ToString(), id, default, NewTemplate(), Vector3.zero, Quaternion.identity, default, startInactive: true);
            Track(instance);
            yield return null;
            Assert.IsFalse(instance.activeSelf, "staged inactive");

            instance.SetActive(true);
            yield return Frames(2);

            Assert.AreSame(body, LayerOf(instance.GetComponentInChildren<SpriteLayerCompositor>().State, Body).Sprite);
        }

        [UnityTest]
        public IEnumerator AppearanceAndEquipmentCoexistAHelmetHidesHairAndTheHairReturnsWhenItIsRemoved()
        {
            var (session, rules, spawner) = NewSession();
            var hair = NewSprite("hair");
            var helm = NewSprite("helm");
            Catalog(rules, Hair, ("hair-1", hair));

            var profile = Track(ScriptableObject.CreateInstance<EquipmentVisualProfile>());
            SetField(profile, "id", "visual-helm");
            SetField(profile, "kind", EquipmentVisualKind.Sprite);
            SetField(profile, "spriteLayers", new List<SpriteLayerEntry> { new(Helmet, helm, Color.white) });
            SetField(profile, "hiddenLayerSlots", new List<DefinitionId> { Hair });
            rules.Register(profile);
            var itemDef = Track(ScriptableObject.CreateInstance<ItemDefinition>());
            SetField(itemDef, "id", "item-helm");
            SetField(itemDef, "equipmentSlotId", Head);
            SetField(itemDef, "equipmentVisualProfileId", profile.Id);
            rules.Register(itemDef);

            var id = RuntimeEntityId.NewId();
            var character = new CharacterState(id);
            character.SetAppearance(Hair, new DefinitionId("hair-1"));
            session.Characters.Register(character);
            session.Party.AddMember(id);
            var item = new ItemInstance(RuntimeEntityId.NewId(), itemDef.Id);
            session.Inventory.AddItem(item);
            var service = new EquipmentService(session.Characters, rules, session.Inventory, session.EventBus);

            var instance = spawner.BindRestoredView(id.ToString(), id, default, NewTemplate(withEquipmentBinder: true), Vector3.zero, Quaternion.identity, default, startInactive: false);
            Track(instance);
            instance.SetActive(true);
            yield return Frames(2);
            var state = instance.GetComponentInChildren<SpriteLayerCompositor>().State;
            Assert.IsTrue(LayerOf(state, Hair).Visible);

            service.Equip(item.InstanceId, id, Head);
            Assert.IsFalse(LayerOf(state, Hair).Visible, "the helmet hides the hair");
            Assert.AreSame(helm, LayerOf(state, Helmet).Sprite);

            service.Unequip(item.InstanceId);
            Assert.AreSame(hair, LayerOf(state, Hair).Sprite, "and the appearance is still there beneath");
        }
    }
}

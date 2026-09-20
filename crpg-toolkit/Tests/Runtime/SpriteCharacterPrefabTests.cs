using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Gameplay;
using CRPG.Inventory;
using CRPG.Navigation;
using CRPG.Navigation.Grid;
using CRPG.Presentation;
using CRPG.Presentation.Animation;
using CRPG.Presentation.Equipment;
using CRPG.Presentation.Sprites;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace CRPG.Tests.PlayMode
{
    /// <summary>
    /// 2D campaign Task 2.5: the whole sprite-character stack on the REAL Grid + Sprite wrapper prefab (built by
    /// CharacterWrapperBuilder, checked in under Resources/Fixtures) -- movement, rocking, sorting, billboard, click targeting, cell
    /// occupancy, appearance, equipment and a CharacterView model swap -- plus the CharacterView fixes.
    /// </summary>
    public class SpriteCharacterPrefabTests
    {
        private static readonly Vector3 Origin = new(6000f, 0f, 10f); // |z| must stay inside the sortable range (256)
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
            Time.timeScale = 1f;
        }

        private T Track<T>(T o) where T : Object { made.Add(o); return o; }

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

        private static GameObject Fixture(string name)
        {
            var prefab = Resources.Load<GameObject>("Fixtures/" + name);
            Assert.IsNotNull(prefab, $"Resources/Fixtures/{name}.prefab is missing -- run 'CRPG Toolkit/Characters/Author Sprite Character Test Fixtures'.");
            return prefab;
        }

        private Sprite NewSprite(string name)
        {
            var texture = Track(new Texture2D(4, 4) { name = name });
            var sprite = Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
            sprite.name = name;
            return Track(sprite);
        }

        private static IEnumerator Frames(int count)
        {
            for (var i = 0; i < count; i++) yield return null;
        }

        // ---------------------------------------------------------------------------------------------
        // CharacterView fixes
        // ---------------------------------------------------------------------------------------------

        private (GameObject root, CharacterPlayableAnimator animator, CharacterView view) NewHumanoidShell(bool extraChild = false)
        {
            var root = Track(new GameObject("CharacterRoot"));
            var animator = root.AddComponent<CharacterPlayableAnimator>();
            var visualRoot = new GameObject("VisualRoot");
            visualRoot.transform.SetParent(root.transform, false);
            var model = new GameObject("Model");
            model.AddComponent<Animator>();
            model.transform.SetParent(visualRoot.transform, false);
            if (extraChild) new GameObject("ConsumerChild").transform.SetParent(visualRoot.transform, false);
            var view = visualRoot.AddComponent<CharacterView>(); // Awake binds GetChild(0)
            return (root, animator, view);
        }

        private GameObject TemplateWith(bool animator)
        {
            var template = Track(new GameObject(animator ? "AnimatedTemplate" : "CardTemplate"));
            template.SetActive(false);
            if (animator) template.AddComponent<Animator>();
            return template;
        }

        [UnityTest]
        public IEnumerator SwappingToAModelWithNoAnimatorUnbindsThePresentationAndSwappingBackRebindsIt()
        {
            var (_, animator, view) = NewHumanoidShell();
            Assert.IsTrue(animator.IsPresentationBound, "test setup: the initial Model's animator is bound");

            view.SetModel(TemplateWith(animator: false));
            yield return null; // the old Model's Destroy is deferred
            Assert.IsNull(view.Animator);
            Assert.IsFalse(animator.IsPresentationBound, "the old Model's animation graph must not stay bound to a Model that has no Animator");

            view.SetModel(TemplateWith(animator: true));
            yield return null;
            Assert.IsNotNull(view.Animator);
            Assert.IsTrue(animator.IsPresentationBound);
        }

        [UnityTest]
        public IEnumerator SetModelPutsTheNewModelFirstEvenWithAnotherChildUnderVisualRoot()
        {
            var (_, _, view) = NewHumanoidShell(extraChild: true);

            view.SetModel(TemplateWith(animator: true));

            Assert.AreSame(view.Model, view.transform.GetChild(0).gameObject, "GetChild(0) is the new Model even before the old one's deferred Destroy");
            yield return null;
            Assert.AreEqual("ConsumerChild", view.transform.GetChild(view.transform.childCount - 1).name);
        }

        // ---------------------------------------------------------------------------------------------
        // The prefab on a real grid
        // ---------------------------------------------------------------------------------------------

        private sealed class Arena
        {
            public WalkabilityGrid Grid;
            public GridPathfindingService Nav;
        }

        private Arena NewArena(int width = 12, int depth = 6)
        {
            var arena = new Arena
            {
                Grid = new WalkabilityGrid(Origin, 1f, width, depth, new List<GridCoordinate>()),
                Nav = new GridPathfindingService(),
            };
            arena.Nav.SetActiveGrid(arena.Grid);
            var floor = Track(new GameObject("Floor"));
            var box = floor.AddComponent<BoxCollider>();
            box.size = new Vector3(width, 0.1f, depth);
            floor.transform.position = Origin + new Vector3(width * 0.5f, -0.05f, depth * 0.5f);
            return arena;
        }

        private GameObject Spawn(Arena arena, int cellX, int cellZ)
        {
            var instance = Track(Object.Instantiate(Fixture("SpriteCharacter"), arena.Grid.ToWorld(new GridCoordinate(cellX, cellZ)), Quaternion.identity));
            var motor = instance.GetComponent<GridCharacterMotor>();
            motor.Pathfinding = arena.Nav;
            return instance;
        }

        [UnityTest]
        public IEnumerator TheRealPrefabWalksAcrossCellsRocksWhileMovingAndSettlesLevel()
        {
            var arena = NewArena();
            var instance = Spawn(arena, 1, 2);
            var motor = instance.GetComponent<GridCharacterMotor>();
            var driver = instance.GetComponentInChildren<RockingMotionDriver>();
            yield return null;

            var result = motor.SetDestination(arena.Grid.ToWorld(new GridCoordinate(9, 2)));
            Assert.AreNotEqual(MoveStatus.Invalid, result.Status);
            var sawRocking = false;
            var elapsed = 0f;
            do
            {
                elapsed += Time.deltaTime;
                yield return null;
                if (driver.Motion.IsMoving && driver.Motion.Amplitude > 0.5f) sawRocking = true;
            } while (elapsed < 30f && motor.Snapshot.Status != MoveStatus.Arrived);

            Assert.AreEqual(MoveStatus.Arrived, motor.Snapshot.Status);
            Assert.IsTrue(sawRocking, "the real prefab's rocking driver found the grid motor and rocked");
            Assert.Less(Vector3.Distance(instance.transform.position, arena.Grid.ToWorld(new GridCoordinate(9, 2))), 0.05f);

            elapsed = 0f;
            while (elapsed < 2f) { elapsed += Time.deltaTime; yield return null; }
            Assert.IsFalse(driver.Motion.IsMoving);
            Assert.Less(Quaternion.Angle(driver.RockPivot.localRotation, Quaternion.identity), 0.01f, "level once settled");
        }

        [UnityTest]
        public IEnumerator TheColliderIsAOneCellBoxThatNeverSpansMoreThanACellEvenWhileTurning()
        {
            var arena = NewArena();
            var instance = Spawn(arena, 1, 1);
            var motor = instance.GetComponent<GridCharacterMotor>();
            var box = instance.GetComponent<BoxCollider>();
            var size = box.size;
            var center = box.center;
            yield return null;

            motor.SetDestination(arena.Grid.ToWorld(new GridCoordinate(8, 4))); // a diagonal move: FacingController turns the root
            var elapsed = 0f;
            var maxWidth = 0f;
            do
            {
                elapsed += Time.deltaTime;
                yield return null;
                Physics.SyncTransforms();
                maxWidth = Mathf.Max(maxWidth, box.bounds.size.x, box.bounds.size.z);
                Assert.AreEqual(size, box.size, "the collider's local size is never touched");
                Assert.AreEqual(center, box.center);
            } while (elapsed < 30f && motor.Snapshot.Status != MoveStatus.Arrived);

            Assert.LessOrEqual(maxWidth, 1.0f, "even at 45 degrees the 0.7-of-a-cell box's world AABB stays within one cell width");
        }

        [UnityTest]
        public IEnumerator ATopDownRayHitsTheCharactersBoxAndResolvesItsCharacterAgent()
        {
            var arena = NewArena();
            var instance = Spawn(arena, 4, 3);
            yield return null;
            Physics.SyncTransforms();

            var origin = arena.Grid.ToWorld(new GridCoordinate(4, 3)) + Vector3.up * 10f;
            Assert.IsTrue(Physics.Raycast(origin, Vector3.down, out var hit, 20f), "the ray hits something");

            Assert.AreSame(instance.GetComponent<BoxCollider>(), hit.collider, "the character's box is nearer than the floor, so it is the closest hit");
            Assert.IsNotNull(hit.collider.GetComponentInParent<CharacterAgent>(), "the same lookup PlayerCommandRouter uses");
            Assert.IsTrue(Physics.Raycast(arena.Grid.ToWorld(new GridCoordinate(8, 3)) + Vector3.up * 10f, Vector3.down, out var empty, 20f));
            Assert.AreNotSame(instance.GetComponent<BoxCollider>(), empty.collider, "an empty cell hits the floor");
        }

        [UnityTest]
        public IEnumerator TheLowerZInstanceDrawsInFrontAndTheCardFollowsAPitchNinetyCamera()
        {
            var arena = NewArena();
            var camGo = Track(new GameObject("Main Camera"));
            camGo.tag = "MainCamera";
            camGo.AddComponent<UnityEngine.Camera>();
            camGo.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            var front = Spawn(arena, 3, 1);
            var back = Spawn(arena, 4, 3);
            yield return Frames(2);

            int Min(GameObject g) => g.GetComponentInChildren<SpriteLayerCompositor>().Renderers.Min(r => r.sortingOrder);
            int Max(GameObject g) => g.GetComponentInChildren<SpriteLayerCompositor>().Renderers.Max(r => r.sortingOrder);
            Assert.Greater(Min(front), Max(back), "lower Z is in front");

            var card = front.transform.Find("VisualRoot/Model");
            Assert.Less(Quaternion.Angle(card.rotation, camGo.transform.rotation), 0.01f, "the billboard faces the camera");
            Assert.AreEqual(0.02f, card.position.y - front.transform.position.y, 0.001f, "and the card sits at its world-Y lift");
        }

        [UnityTest]
        public IEnumerator TwoCharactersCannotEndUpInTheSameCell()
        {
            var arena = NewArena(width: 10, depth: 1); // a one-cell-wide corridor: no way around
            var standing = Spawn(arena, 5, 0);
            var walker = Spawn(arena, 0, 0);
            yield return null;

            walker.GetComponent<GridCharacterMotor>().SetDestination(arena.Grid.ToWorld(new GridCoordinate(5, 0)));
            var elapsed = 0f;
            while (elapsed < 10f)
            {
                elapsed += Time.deltaTime;
                yield return null;
                var status = walker.GetComponent<GridCharacterMotor>().Snapshot.Status;
                if (status is MoveStatus.Arrived or MoveStatus.Blocked) break;
            }

            var standingCell = arena.Grid.ToGrid(standing.transform.position);
            var walkerCell = arena.Grid.ToGrid(walker.transform.position);
            Assert.AreNotEqual(standingCell, walkerCell, "the real prefab's motor holds its cell against another character");
        }

        // ---------------------------------------------------------------------------------------------
        // Spawn, appearance, equipment, model swap
        // ---------------------------------------------------------------------------------------------

        private (GameSession session, RulesDatabase rules, WorldSpawnService spawner) NewSession()
        {
            var rules = new RulesDatabase();
            var host = Track(new GameObject("Host")).AddComponent<GameSessionHost>();
            var session = host.StartSession(rules, new DiceService(new SeededRandomSource(1)));
            return (session, rules, new WorldSpawnService(session));
        }

        private void Catalog(RulesDatabase rules, DefinitionId slot, params (string option, Sprite sprite)[] options)
        {
            var catalog = Track(ScriptableObject.CreateInstance<SpriteLayerCatalog>());
            SetField(catalog, "id", "catalog-" + slot.Value + "-" + serial++);
            SetField(catalog, "layerSlot", slot);
            SetField(catalog, "options", options.Select(o => new SpriteOptionEntry(new DefinitionId(o.option), o.option, o.sprite, Color.white)).ToList());
            rules.Register(catalog);
        }

        private static EffectiveLayer LayerOf(SpriteLayerState state, DefinitionId slot) => state.Layers.First(l => l.Slot == slot);

        [UnityTest]
        public IEnumerator AppearanceAndGearShowOnTheRealPrefabAndSurviveAModelSwap()
        {
            var (session, rules, spawner) = NewSession();
            var body = NewSprite("body");
            var hair = NewSprite("hair");
            var helm = NewSprite("helm");
            Catalog(rules, Body, ("body-1", body));
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
            character.SetAppearance(Body, new DefinitionId("body-1"));
            character.SetAppearance(Hair, new DefinitionId("hair-1"));
            session.Characters.Register(character);
            session.Party.AddMember(id);
            var item = new ItemInstance(RuntimeEntityId.NewId(), itemDef.Id);
            session.Inventory.AddItem(item);
            var service = new EquipmentService(session.Characters, rules, session.Inventory, session.EventBus);
            service.Equip(item.InstanceId, id, Head); // worn before the view binds: the load shape

            var instance = spawner.BindRestoredView(id.ToString(), id, default, Fixture("SpriteCharacter"), Vector3.zero, Quaternion.identity, default, startInactive: true);
            Track(instance);
            yield return null;
            Assert.IsFalse(instance.activeSelf, "staged inactive");
            instance.SetActive(true);
            yield return Frames(2);

            var view = instance.GetComponentInChildren<CharacterView>();
            var state = instance.GetComponentInChildren<SpriteLayerCompositor>().State;
            Assert.AreSame(body, LayerOf(state, Body).Sprite, "appearance shows");
            Assert.AreSame(helm, LayerOf(state, Helmet).Sprite, "gear worn at bind time shows");
            Assert.IsFalse(LayerOf(state, Hair).Visible, "and its hide applies");

            view.SetModel(Fixture("SpriteCard")); // swap the whole card
            yield return Frames(3);                // Destroy is deferred: two compositors exist for a frame; the binders re-resolve after

            var newState = instance.GetComponentInChildren<SpriteLayerCompositor>().State;
            Assert.AreNotSame(state, newState, "a genuinely new compositor");
            Assert.AreSame(body, LayerOf(newState, Body).Sprite, "the appearance is on the new card");
            Assert.AreSame(helm, LayerOf(newState, Helmet).Sprite, "and so is the gear");
            Assert.AreEqual(1, instance.GetComponentsInChildren<SpriteLayerCompositor>(true).Length, "the old card is gone");

            Assert.AreEqual("Model", view.transform.GetChild(0).name);
            Assert.AreSame(view.Model, view.transform.GetChild(0).gameObject);
            Assert.AreEqual(0.02f, view.Model.transform.position.y - instance.transform.position.y, 0.001f, "the lift lives on VisualRoot, so the swapped-in card is still lifted");

            service.Unequip(item.InstanceId);
            Assert.AreSame(hair, LayerOf(newState, Hair).Sprite, "later events reach the new card");
        }

        [UnityTest]
        public IEnumerator SpawnNpcWorksWithTheSamePrefabAndItsAuthoredAppearance()
        {
            var (session, rules, spawner) = NewSession();
            var body = NewSprite("body");
            Catalog(rules, Body, ("body-1", body));
            var definition = Track(ScriptableObject.CreateInstance<NpcDefinition>());
            SetField(definition, "id", "npc-prefab-test");
            SetField(definition, "displayName", "Prefab NPC");
            SetField(definition, "visualPrefab", Fixture("SpriteCharacter"));
            SetField(definition, "appearance", new List<AppearanceEntry> { new(Body, new DefinitionId("body-1")) });

            var agent = spawner.SpawnNpc("spawn-prefab", definition, Vector3.zero, Quaternion.identity);
            Track(agent.gameObject);
            yield return Frames(2);

            Assert.AreSame(body, LayerOf(agent.GetComponentInChildren<SpriteLayerCompositor>().State, Body).Sprite);
            Assert.IsNotNull(agent.GetComponent<GridCharacterMotor>());
        }
    }
}

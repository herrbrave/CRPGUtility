using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using CRPG.Core;
using CRPG.Presentation.Sprites;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CRPG.Tests.PlayMode
{
    /// <summary>2D campaign Task 2.2: SpriteLayerCompositor -- one renderer per slot, refreshed only in LateUpdate.</summary>
    public class SpriteLayerCompositorTests
    {
        private static readonly DefinitionId Body = new("layer-body");
        private static readonly DefinitionId Hair = new("layer-hair");
        private static readonly DefinitionId Weapon = new("layer-weapon");
        private static readonly DefinitionId MainHand = new("slot-main-hand");

        /// <summary>Samples one renderer's sprite in LateUpdate after everything else (rendering happens after all
        /// LateUpdates), so it sees exactly what would be presented.</summary>
        [DefaultExecutionOrder(1000)]
        private sealed class PresentedSpriteProbe : MonoBehaviour
        {
            public SpriteRenderer Target;
            public readonly List<Sprite> Seen = new();
            private void LateUpdate() { if (Target != null) Seen.Add(Target.enabled ? Target.sprite : null); }
        }

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

        private SpriteLayerCompositor NewCompositor(string name = "Character")
        {
            var go = Make(name);
            return go.AddComponent<SpriteLayerCompositor>(); // Awake runs immediately (active object)
        }

        private static void SetField(object target, string field, object value) =>
            target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

        [UnityTest]
        public IEnumerator OneRendererPerSlotNamedParentedAtIdentityAndOrderedByOffset()
        {
            var compositor = NewCompositor();
            yield return null;

            var slots = SpriteLayerCompositor.DefaultSlots();
            Assert.AreEqual(slots.Count, compositor.Renderers.Count);
            for (var i = 0; i < slots.Count; i++)
            {
                var sr = compositor.Renderers[i];
                Assert.AreEqual("Layer_" + slots[i].Value, sr.name);
                Assert.AreSame(compositor.transform, sr.transform.parent);
                Assert.AreEqual(Vector3.zero, sr.transform.localPosition);
                Assert.AreEqual(Quaternion.identity, sr.transform.localRotation);
                Assert.AreEqual(Vector3.one, sr.transform.localScale);
                Assert.AreEqual(SpriteSortingContract.SlotOrder(0, i), sr.sortingOrder);
                Assert.AreEqual(SpriteSortingContract.WorldLayer, sr.sortingLayerName);
            }
        }

        [UnityTest]
        public IEnumerator ALayersRootThatIsNotTheCompositorItselfParentsTheRenderers()
        {
            var go = Make("Character");
            go.SetActive(false);
            var compositor = go.AddComponent<SpriteLayerCompositor>();
            var root = Make("Layers", go.transform).transform;
            SetField(compositor, "layersRoot", root);
            go.SetActive(true);
            yield return null;

            Assert.IsTrue(compositor.Renderers.All(r => r.transform.parent == root));
            Assert.AreEqual(0, compositor.transform.Cast<Transform>().Count(t => t.name.StartsWith("Layer_")));
        }

        [UnityTest]
        public IEnumerator PrefabAuthoredLayerChildrenAreReusedNotDoubled()
        {
            var go = Make("Character");
            go.SetActive(false);
            var authored = Make("Layer_layer-body", go.transform);
            authored.AddComponent<SpriteRenderer>();
            authored.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            go.AddComponent<SpriteLayerCompositor>();
            go.SetActive(true);
            yield return null;

            var compositor = go.GetComponent<SpriteLayerCompositor>();
            Assert.AreSame(authored.GetComponent<SpriteRenderer>(), compositor.Renderers[0]);
            Assert.AreEqual(compositor.Renderers.Count, go.transform.childCount, "no duplicate child");
            Assert.AreEqual(new Vector3(0f, 0.5f, 0f), authored.transform.localPosition, "an authored layer keeps its placement");
        }

        [UnityTest]
        public IEnumerator TheDefaultSpriteMaterialIsDoubleSided()
        {
            // The flat top-down cards face away from the camera, so the material must be Cull Off. URP's Sprite-Unlit-Default
            // and the native Sprites/Default fallback both are; anything else (a Lit sprite shader) would render nothing.
            var compositor = NewCompositor();
            yield return null;

            foreach (var sr in compositor.Renderers)
            {
                Assert.IsNotNull(sr.sharedMaterial, "renderer has a material");
                StringAssert.Contains("Sprite", sr.sharedMaterial.shader.name);
                Assert.That(sr.sharedMaterial.shader.name, Does.Not.Contain("Lit").Or.Contain("Unlit"), "an unlit, double-sided default");
            }
        }

        [UnityTest]
        public IEnumerator StateChangesReachTheRenderersAtTheNextLateUpdate()
        {
            var compositor = NewCompositor();
            yield return null;
            var body = NewSprite("body");
            var bodyRenderer = compositor.Renderers[0];

            compositor.State.SetAppearance(Body, new SpriteOption(body, Color.red));
            Assert.IsNull(bodyRenderer.sprite, "nothing is presented until LateUpdate");
            yield return null;

            Assert.AreSame(body, bodyRenderer.sprite);
            Assert.AreEqual(Color.red, bodyRenderer.color);
            Assert.IsTrue(bodyRenderer.enabled);
            Assert.AreEqual(compositor.State.Version, compositor.AppliedVersion);

            compositor.State.SetAppearance(Hair, new SpriteOption(NewSprite("hair"), Color.white));
            compositor.State.SetEquipment(new DefinitionId("slot-head"), 0, null, new[] { Hair });
            yield return null;
            Assert.IsFalse(compositor.Renderers[3].enabled, "a hidden slot's renderer is disabled");

            compositor.State.ClearAllAppearance();
            compositor.State.ClearEquipment(new DefinitionId("slot-head"));
            yield return null;
            Assert.IsNull(bodyRenderer.sprite, "an empty slot shows no sprite");
            Assert.IsFalse(bodyRenderer.enabled);
        }

        [UnityTest]
        public IEnumerator AnUnequipThenEquipInOneFrameIsNeverPresentedHalfDone()
        {
            var compositor = NewCompositor();
            var probe = compositor.gameObject.AddComponent<PresentedSpriteProbe>();
            var weaponRenderer = compositor.Renderers[6]; // layer-weapon
            probe.Target = weaponRenderer;
            var sword = NewSprite("sword");
            var axe = NewSprite("axe");
            compositor.State.SetEquipment(MainHand, 0, new[] { new LayerPaint(Weapon, new SpriteOption(sword, Color.white)) }, null);
            yield return null;
            yield return null;
            Assert.AreSame(sword, probe.Seen.Last());

            // What EquipmentService.Equip does synchronously: ItemUnequipped(old), then ItemEquipped(new). No yield in between.
            compositor.State.ClearEquipment(MainHand);
            compositor.State.SetEquipment(MainHand, 0, new[] { new LayerPaint(Weapon, new SpriteOption(axe, Color.white)) }, null);
            yield return null;
            yield return null;

            Assert.AreSame(axe, probe.Seen.Last());
            var firstAxe = probe.Seen.IndexOf(axe);
            Assert.IsFalse(probe.Seen.Take(firstAxe).Skip(probe.Seen.IndexOf(sword)).Any(s => s == null),
                "the presented sprite went straight from the sword to the axe, never through empty");
        }

        [UnityTest]
        public IEnumerator SortingBaseAppliesImmediatelyToEveryRendererAndSurvivesARebuild()
        {
            var compositor = NewCompositor();
            yield return null;

            compositor.SortingBase = -768;
            for (var i = 0; i < compositor.Renderers.Count; i++)
                Assert.AreEqual(-768 + 1 + i, compositor.Renderers[i].sortingOrder, "applied at once, no frame wait");

            compositor.State.SetAppearance(Body, new SpriteOption(NewSprite(), Color.white));
            yield return null;
            for (var i = 0; i < compositor.Renderers.Count; i++)
                Assert.AreEqual(-768 + 1 + i, compositor.Renderers[i].sortingOrder, "still applied after a rebuild");
        }

        [UnityTest]
        public IEnumerator TheRealWorldLayerDoesNotWarnAndAMissingOneWarnsOnceAndFallsBackToDefault()
        {
            var ok = NewCompositor("Ok");
            yield return null;
            LogAssert.NoUnexpectedReceived();
            Assert.AreEqual(SpriteSortingContract.WorldLayer, ok.Renderers[0].sortingLayerName);

            var go = Make("Missing");
            go.SetActive(false);
            var missing = go.AddComponent<SpriteLayerCompositor>();
            missing.SortingLayerName = "NoSuchLayer";
            LogAssert.Expect(LogType.Warning, new Regex("Sorting layer 'NoSuchLayer' does not exist"));
            go.SetActive(true);
            yield return null;
            yield return null;
            yield return null;

            LogAssert.NoUnexpectedReceived(); // exactly the one expected warning
            Assert.AreEqual(0, missing.Renderers[0].sortingLayerID, "drew on Default");
        }

        [UnityTest]
        public IEnumerator ACustomEighthSlotWorksAndAnEmptyListFallsBackToTheDefaultSet()
        {
            var go = Make("Eight");
            go.SetActive(false);
            var eight = go.AddComponent<SpriteLayerCompositor>();
            var list = SpriteLayerCompositor.DefaultSlots();
            list.Add(new DefinitionId("layer-cape"));
            SetField(eight, "slots", list);
            go.SetActive(true);
            yield return null;
            Assert.AreEqual(8, eight.Renderers.Count);
            Assert.AreEqual("Layer_layer-cape", eight.Renderers[7].name);

            var empty = Make("Empty");
            empty.SetActive(false);
            var emptyCompositor = empty.AddComponent<SpriteLayerCompositor>();
            SetField(emptyCompositor, "slots", new List<DefinitionId>());
            LogAssert.Expect(LogType.Warning, new Regex("No slots configured"));
            empty.SetActive(true);
            yield return null;
            Assert.AreEqual(SpriteLayerCompositor.DefaultSlots().Count, emptyCompositor.Renderers.Count);
            Assert.IsFalse(emptyCompositor.Disabled);
        }

        private IEnumerator ExpectDisabledFor(List<DefinitionId> badSlots)
        {
            var go = Make("Bad");
            go.SetActive(false);
            var compositor = go.AddComponent<SpriteLayerCompositor>();
            SetField(compositor, "slots", badSlots);
            LogAssert.Expect(LogType.Error, new Regex("Invalid slot configuration"));
            go.SetActive(true);
            yield return null;
            yield return null;
            yield return null;

            LogAssert.NoUnexpectedReceived(); // one error, then silence: no per-frame exceptions
            Assert.IsTrue(compositor.Disabled);
            Assert.AreEqual(0, compositor.Renderers.Count);
            Assert.IsNotNull(compositor.State, "State is still usable so callers do not throw");
        }

        [UnityTest]
        public IEnumerator ADefaultDefinitionIdEntryDisablesTheCompositorWithOneError() =>
            ExpectDisabledFor(new List<DefinitionId> { Body, default });

        [UnityTest]
        public IEnumerator ADuplicateSlotDisablesTheCompositorWithOneError() =>
            ExpectDisabledFor(new List<DefinitionId> { Body, Hair, Body });

        [UnityTest]
        public IEnumerator MoreThanTheSlotCapDisablesTheCompositorWithOneError() =>
            ExpectDisabledFor(Enumerable.Range(0, SpriteSortingContract.MaxSlotsPerObject + 1).Select(i => new DefinitionId("s" + i)).ToList());

        [Test]
        public void StateIsUsableBeforeAwakeOnAnInactiveObject()
        {
            var go = Make("Inactive");
            go.SetActive(false);
            var compositor = go.AddComponent<SpriteLayerCompositor>(); // edit-time authoring shape: Awake has not run

            Assert.IsNotNull(compositor.State);
            Assert.AreEqual(SpriteLayerCompositor.DefaultSlots().Count, compositor.State.SlotCount);
            Assert.AreEqual(0, compositor.Renderers.Count);
            Assert.AreEqual(0, go.transform.childCount, "reading State creates no children");
        }
    }
}

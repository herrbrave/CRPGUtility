using System.Collections.Generic;
using System.IO;
using System.Linq;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Editor.Character;
using CRPG.Editor.Validation;
using CRPG.Gameplay;
using CRPG.Navigation;
using CRPG.Navigation.Grid;
using CRPG.Presentation;
using CRPG.Presentation.Animation;
using CRPG.Presentation.Equipment;
using CRPG.Presentation.Sprites;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace CRPG.Tests.EditMode
{
    /// <summary>
    /// 2D campaign Task 2.5: CharacterWrapperBuilder's Grid/Sprite options -- the component matrix, the generated card, the
    /// first-child rule, idempotent updates, and refusal of conflicting updates. Writes real prefab assets into a scratch folder.
    /// </summary>
    public class SpriteWrapperBuilderTests
    {
        private const string ScratchFolder = "Assets/Temp/SpriteWrapperBuilderTests";
        private readonly List<Object> made = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in made) if (o != null) Object.DestroyImmediate(o);
            made.Clear();

            if (AssetDatabase.IsValidFolder(ScratchFolder)) AssetDatabase.DeleteAsset(ScratchFolder);
            const string parent = "Assets/Temp";
            if (AssetDatabase.IsValidFolder(parent) && AssetDatabase.FindAssets(string.Empty, new[] { parent }).Length == 0) AssetDatabase.DeleteAsset(parent);
        }

        private static CharacterSetupSpec GridSprite() => new() { Movement = MovementBackend.Grid, Presentation = PresentationKind.Sprite };

        private static string Path(string name) => $"{ScratchFolder}/{name}.prefab";

        private static string Sha(string assetPath) =>
            System.BitConverter.ToString(System.Security.Cryptography.SHA1.Create().ComputeHash(File.ReadAllBytes(assetPath)));

        private static int CountIMovables(GameObject root) => root.GetComponentsInChildren<MonoBehaviour>(true).Count(m => m is IMovable);

        private static Transform Find(GameObject root, string path) => root.transform.Find(path);

        // ---------------------------------------------------------------------------------------------
        // Grid + Sprite
        // ---------------------------------------------------------------------------------------------

        [Test]
        public void GridSpriteYieldsExactlyOneMoverAndTheTopDownComponentSet()
        {
            var spec = GridSprite();
            spec.GridSpeed = 5.5f;
            var root = CharacterWrapperBuilder.BuildOrUpdate(spec, Path("GridSprite"));

            Assert.AreEqual(1, CountIMovables(root), "exactly one IMovable");
            Assert.IsNotNull(root.GetComponent<GridCharacterMotor>());
            Assert.AreEqual(5.5f, root.GetComponent<GridCharacterMotor>().Speed);
            Assert.IsNull(root.GetComponentInChildren<NavMeshAgent>(true));
            Assert.IsNull(root.GetComponentInChildren<NavMeshCharacterMotor>(true));
            Assert.IsNull(root.GetComponentInChildren<CapsuleCollider>(true));
            Assert.IsNull(root.GetComponentInChildren<CharacterPlayableAnimator>(true));
            Assert.IsNull(root.GetComponentInChildren<EquipmentVisualBinder>(true));

            var box = root.GetComponent<BoxCollider>();
            Assert.IsNotNull(box);
            Assert.AreEqual(new Vector3(0.7f, 1f, 0.7f), box.size, "a 0.7-of-a-cell footprint");
            Assert.AreEqual(new Vector3(0f, 0.5f, 0f), box.center);

            var body = root.GetComponent<Rigidbody>();
            Assert.IsTrue(body.isKinematic);
            Assert.IsFalse(body.useGravity);
            Assert.IsNotNull(root.GetComponent<FacingController>());
            Assert.IsNotNull(root.GetComponent<CharacterAgent>());
            Assert.IsNotNull(root.GetComponent<SpriteEquipmentBinder>(), "the binders live on CharacterRoot, so they survive a Model swap");
            Assert.IsNotNull(root.GetComponent<SpriteAppearanceBinder>());
            Assert.IsNotNull(root.GetComponent<SpriteActionCues>(), "combat cues live on CharacterRoot too");
        }

        [Test]
        public void GridSpriteCarriesTheSpecsHitPointIdOnItsCues()
        {
            var spec = GridSprite();
            spec.SpriteHitPointResourceId = new DefinitionId("resource-hp");
            var root = CharacterWrapperBuilder.BuildOrUpdate(spec, Path("CuesHp"));
            Assert.AreEqual(new DefinitionId("resource-hp"), root.GetComponent<SpriteActionCues>().HitPointResourceId);

            spec.SpriteHitPointResourceId = new DefinitionId("resource-vigor");
            root = CharacterWrapperBuilder.BuildOrUpdate(spec, Path("CuesHp"));
            Assert.AreEqual(new DefinitionId("resource-vigor"), root.GetComponent<SpriteActionCues>().HitPointResourceId, "an update re-applies it");
            Assert.AreEqual(1, root.GetComponents<SpriteActionCues>().Length);
        }

        [Test]
        public void TheCardHierarchyAndGeometryMatchTheApprovedSpike()
        {
            var root = CharacterWrapperBuilder.BuildOrUpdate(GridSprite(), Path("Geometry"));

            var visualRoot = Find(root, "VisualRoot");
            Assert.IsNotNull(visualRoot.GetComponent<CharacterView>());
            Assert.AreEqual(new Vector3(0f, 0.02f, 0f), visualRoot.localPosition, "the world-Y lift lives on VisualRoot so a Model swap cannot lose it");

            var model = Find(root, "VisualRoot/Model");
            Assert.AreSame(model, visualRoot.GetChild(0), "the card is VisualRoot's first child");
            Assert.AreEqual(Vector3.zero, model.localPosition);
            Assert.AreEqual(Quaternion.identity, model.localRotation);
            Assert.IsNotNull(model.GetComponent<CameraFacingBillboard>());
            Assert.IsNotNull(model.GetComponent<SpriteDepthSorter>());
            var driver = model.GetComponent<RockingMotionDriver>();

            var pivot = Find(root, "VisualRoot/Model/RockPivot");
            Assert.AreEqual(new Vector3(0f, -0.5f, 0f), pivot.localPosition);
            Assert.AreSame(pivot, driver.RockPivot);

            var layers = Find(root, "VisualRoot/Model/RockPivot/Layers");
            Assert.AreEqual(new Vector3(0f, 0.5f, 0f), layers.localPosition);
            var compositor = layers.GetComponent<SpriteLayerCompositor>();
            Assert.IsNotNull(compositor);
            Assert.AreEqual(SpriteSortingContract.WorldLayer, compositor.SortingLayerName);
        }

        [Test]
        public void EverySlotHasAPreAuthoredLayerRendererOnTheSortingLayerWithADoubleSidedDefaultMaterial()
        {
            var root = CharacterWrapperBuilder.BuildOrUpdate(GridSprite(), Path("Layers"));
            var layers = Find(root, "VisualRoot/Model/RockPivot/Layers");
            var expected = SpriteLayerCompositor.DefaultSlots().Select(s => "Layer_" + s.Value).ToArray();

            CollectionAssert.AreEqual(expected, layers.Cast<Transform>().Select(t => t.name).ToArray());
            foreach (Transform child in layers)
            {
                var renderer = child.GetComponent<SpriteRenderer>();
                Assert.IsNotNull(renderer);
                Assert.AreEqual(SpriteSortingContract.WorldLayer, renderer.sortingLayerName);
                Assert.IsNotNull(renderer.sharedMaterial, "the default sprite material");
                Assert.That(renderer.sharedMaterial.shader.name, Does.Contain("Sprite"));
            }
        }

        [Test]
        public void ACustomSlotListAndSortingLayerAreApplied()
        {
            var spec = GridSprite();
            spec.SpriteLayerSlots = new List<DefinitionId> { new("layer-body"), new("layer-cape") };
            spec.SpriteSortingLayer = "Ground";

            var root = CharacterWrapperBuilder.BuildOrUpdate(spec, Path("Custom"));
            var layers = Find(root, "VisualRoot/Model/RockPivot/Layers");

            CollectionAssert.AreEqual(new[] { "Layer_layer-body", "Layer_layer-cape" }, layers.Cast<Transform>().Select(t => t.name).ToArray());
            Assert.AreEqual("Ground", layers.GetComponent<SpriteLayerCompositor>().SortingLayerName);
            Assert.AreEqual("Ground", layers.GetChild(0).GetComponent<SpriteRenderer>().sortingLayerName);
        }

        [Test]
        public void AConsumerChildUnderVisualRootNeverDisplacesTheModelFromFirstPlace()
        {
            var path = Path("FirstChild");
            var spec = GridSprite();
            CharacterWrapperBuilder.BuildOrUpdate(spec, path);

            var editable = PrefabUtility.LoadPrefabContents(path);
            new GameObject("ConsumerAnchor").transform.SetParent(editable.transform.Find("VisualRoot"), false);
            PrefabUtility.SaveAsPrefabAsset(editable, path);
            PrefabUtility.UnloadPrefabContents(editable);

            var updated = CharacterWrapperBuilder.BuildOrUpdate(spec, path);

            var visualRoot = Find(updated, "VisualRoot");
            Assert.AreEqual("Model", visualRoot.GetChild(0).name, "SetParent appends last; the builder must put Model first");
            Assert.IsNotNull(visualRoot.Find("ConsumerAnchor"), "and the consumer's own child survives");
        }

        // ---------------------------------------------------------------------------------------------
        // The rest of the matrix
        // ---------------------------------------------------------------------------------------------

        [Test]
        public void NavMeshSpriteKeepsTheNavMeshMoverButUsesTheTopDownBox()
        {
            var spec = new CharacterSetupSpec { Movement = MovementBackend.NavMesh, Presentation = PresentationKind.Sprite };
            var root = CharacterWrapperBuilder.BuildOrUpdate(spec, Path("NavSprite"));

            Assert.AreEqual(1, CountIMovables(root));
            Assert.IsNotNull(root.GetComponent<NavMeshAgent>());
            Assert.IsNotNull(root.GetComponent<NavMeshCharacterMotor>());
            Assert.IsNull(root.GetComponent<GridCharacterMotor>());
            Assert.IsNotNull(root.GetComponent<BoxCollider>(), "top-down, so a box even though it walks a NavMesh");
            Assert.IsNull(root.GetComponent<CapsuleCollider>());
            Assert.IsNotNull(root.GetComponent<SpriteEquipmentBinder>());
            Assert.IsNull(root.GetComponent<CharacterPlayableAnimator>());
        }

        [Test]
        public void GridHumanoidUsesTheGridMoverAndBoxWithTheHumanoidPresentationSet()
        {
            var template = new GameObject("ModelTemplate");
            template.AddComponent<Animator>();
            made.Add(template);
            Directory.CreateDirectory(ScratchFolder);
            AssetDatabase.Refresh();
            var model = PrefabUtility.SaveAsPrefabAsset(template, Path("Model"));
            var spec = new CharacterSetupSpec
            {
                Movement = MovementBackend.Grid, Presentation = PresentationKind.Humanoid, ModelPrefab = model,
            };

            var root = CharacterWrapperBuilder.BuildOrUpdate(spec, Path("GridHumanoid"));

            Assert.AreEqual(1, CountIMovables(root));
            Assert.IsNotNull(root.GetComponent<GridCharacterMotor>());
            Assert.IsNull(root.GetComponent<NavMeshAgent>());
            Assert.IsNotNull(root.GetComponent<BoxCollider>());
            Assert.IsNotNull(root.GetComponent<CharacterPlayableAnimator>());
            Assert.IsNotNull(root.GetComponent<EquipmentVisualBinder>());
            Assert.IsNull(root.GetComponent<SpriteEquipmentBinder>());
            Assert.AreEqual("Model", Find(root, "VisualRoot").GetChild(0).name);
        }

        [Test]
        public void ASpriteSpecIgnoresAModelPrefabAndBuildsTheSameCard()
        {
            var template = new GameObject("IgnoredModel");
            template.AddComponent<Animator>();
            made.Add(template);
            Directory.CreateDirectory(ScratchFolder);
            AssetDatabase.Refresh();
            var model = PrefabUtility.SaveAsPrefabAsset(template, Path("Ignored"));
            var spec = GridSprite();
            spec.ModelPrefab = model;

            var root = CharacterWrapperBuilder.BuildOrUpdate(spec, Path("SpriteWithModel"));

            Assert.IsNotNull(Find(root, "VisualRoot/Model/RockPivot/Layers"));
            Assert.IsNull(root.GetComponentInChildren<Animator>(true), "the ignored model's Animator is not in the wrapper");
        }

        [Test]
        public void SpriteCardBuilderProducesASelfContainedIdentityModel()
        {
            var card = SpriteCardBuilder.Build(GridSprite());
            made.Add(card);

            Assert.AreEqual("Model", card.name);
            Assert.AreEqual(Vector3.zero, card.transform.localPosition);
            Assert.AreEqual(Quaternion.identity, card.transform.localRotation);
            Assert.IsNotNull(card.GetComponentInChildren<SpriteLayerCompositor>(true));
            Assert.AreEqual(SpriteLayerCompositor.DefaultSlots().Count, card.GetComponentsInChildren<SpriteRenderer>(true).Length);
        }

        // ---------------------------------------------------------------------------------------------
        // Idempotent update
        // ---------------------------------------------------------------------------------------------

        [Test]
        public void UpdatingAGridSpriteWrapperKeepsTheGuidAddsNothingTwiceAndAppliesChanges()
        {
            var path = Path("Idempotent");
            var spec = GridSprite();
            var first = CharacterWrapperBuilder.BuildOrUpdate(spec, path);
            var guid = AssetDatabase.AssetPathToGUID(path);
            var componentCount = first.GetComponentsInChildren<Component>(true).Length;

            spec.GridSpeed = 9f;
            spec.SpriteSortingLayer = "Ground";
            var second = CharacterWrapperBuilder.BuildOrUpdate(spec, path);

            Assert.AreEqual(guid, AssetDatabase.AssetPathToGUID(path));
            Assert.AreEqual(componentCount, second.GetComponentsInChildren<Component>(true).Length, "no duplicated components or children");
            Assert.AreEqual(1, second.GetComponents<GridCharacterMotor>().Length);
            Assert.AreEqual(1, second.GetComponents<BoxCollider>().Length);
            Assert.AreEqual(9f, second.GetComponent<GridCharacterMotor>().Speed);
            Assert.AreEqual("Ground", Find(second, "VisualRoot/Model/RockPivot/Layers").GetComponent<SpriteLayerCompositor>().SortingLayerName);
        }

        [Test]
        public void AnExistingRootBoxColliderOnAGridWrapperIsPatchedNotDuplicated()
        {
            // The builder owns CharacterRoot's BoxCollider on top-down wrappers the way it owns the capsule on humanoid ones.
            var path = Path("BoxOwnership");
            var spec = GridSprite();
            CharacterWrapperBuilder.BuildOrUpdate(spec, path);

            var editable = PrefabUtility.LoadPrefabContents(path);
            editable.GetComponent<BoxCollider>().size = new Vector3(5f, 5f, 5f);
            PrefabUtility.SaveAsPrefabAsset(editable, path);
            PrefabUtility.UnloadPrefabContents(editable);

            var updated = CharacterWrapperBuilder.BuildOrUpdate(spec, path);

            Assert.AreEqual(1, updated.GetComponents<BoxCollider>().Length);
            Assert.AreEqual(new Vector3(0.7f, 1f, 0.7f), updated.GetComponent<BoxCollider>().size);
        }

        [Test]
        public void ConsumerOwnedContentOnASpriteWrapperSurvivesUpdates()
        {
            var path = Path("Consumer");
            var spec = GridSprite();
            CharacterWrapperBuilder.BuildOrUpdate(spec, path);

            var editable = PrefabUtility.LoadPrefabContents(path);
            new GameObject("ConsumerMarker").transform.SetParent(editable.transform, false);
            var marker = editable.AddComponent<SphereCollider>(); // NOT a BoxCollider: the builder owns that one on top-down wrappers
            marker.radius = 7.5f;
            PrefabUtility.SaveAsPrefabAsset(editable, path);
            PrefabUtility.UnloadPrefabContents(editable);

            spec.GridSpeed = 3f;
            var updated = CharacterWrapperBuilder.BuildOrUpdate(spec, path);

            Assert.IsNotNull(updated.transform.Find("ConsumerMarker"));
            Assert.AreEqual(7.5f, updated.GetComponent<SphereCollider>().radius, 0.001f);
            Assert.AreEqual(3f, updated.GetComponent<GridCharacterMotor>().Speed);
        }

        // ---------------------------------------------------------------------------------------------
        // Refusals
        // ---------------------------------------------------------------------------------------------

        private static CharacterSetupSpec Spec(MovementBackend movement, PresentationKind presentation) =>
            new() { Movement = movement, Presentation = presentation };

        private void AssertRefused(string path, CharacterSetupSpec update, string mentions)
        {
            var before = Sha(path);
            var ex = Assert.Throws<System.InvalidOperationException>(() => CharacterWrapperBuilder.BuildOrUpdate(update, path));
            StringAssert.Contains("create a new wrapper", ex.Message);
            StringAssert.Contains(mentions, ex.Message);
            Assert.AreEqual(before, Sha(path), "the asset file is byte-for-byte unchanged");
        }

        [Test]
        public void AGridSpecOnANavMeshWrapperIsRefusedAndTheAssetIsUntouched()
        {
            var path = Path("NavToGrid");
            CharacterWrapperBuilder.BuildOrUpdate(Spec(MovementBackend.NavMesh, PresentationKind.Sprite), path);
            AssertRefused(path, Spec(MovementBackend.Grid, PresentationKind.Sprite), "NavMeshAgent");
        }

        [Test]
        public void ANavMeshSpecOnAGridWrapperIsRefused()
        {
            var path = Path("GridToNav");
            CharacterWrapperBuilder.BuildOrUpdate(Spec(MovementBackend.Grid, PresentationKind.Sprite), path);
            AssertRefused(path, Spec(MovementBackend.NavMesh, PresentationKind.Sprite), "GridCharacterMotor");
        }

        [Test]
        public void ASpriteSpecOnAHumanoidWrapperIsRefused()
        {
            var path = Path("HumanoidToSprite");
            CharacterWrapperBuilder.BuildOrUpdate(Spec(MovementBackend.NavMesh, PresentationKind.Humanoid), path);
            AssertRefused(path, Spec(MovementBackend.NavMesh, PresentationKind.Sprite), "CharacterPlayableAnimator");
        }

        [Test]
        public void AHumanoidSpecOnASpriteWrapperIsRefused()
        {
            var path = Path("SpriteToHumanoid");
            CharacterWrapperBuilder.BuildOrUpdate(Spec(MovementBackend.NavMesh, PresentationKind.Sprite), path);
            AssertRefused(path, Spec(MovementBackend.NavMesh, PresentationKind.Humanoid), "SpriteEquipmentBinder");
        }

        [Test]
        public void AHumanoidSpecOnASpriteWrapperIsRefusedForItsCuesToo()
        {
            var path = Path("SpriteCuesToHumanoid");
            CharacterWrapperBuilder.BuildOrUpdate(Spec(MovementBackend.NavMesh, PresentationKind.Sprite), path);
            var editable = PrefabUtility.LoadPrefabContents(path);
            Object.DestroyImmediate(editable.GetComponent<SpriteEquipmentBinder>());
            Object.DestroyImmediate(editable.GetComponent<SpriteAppearanceBinder>());
            PrefabUtility.SaveAsPrefabAsset(editable, path);
            PrefabUtility.UnloadPrefabContents(editable);

            AssertRefused(path, Spec(MovementBackend.NavMesh, PresentationKind.Humanoid), "SpriteActionCues");
        }

        [Test]
        public void ATopDownSpecOnAWrapperThatAlreadyHasACapsuleIsRefused()
        {
            var path = Path("CapsuleToBox");
            CharacterWrapperBuilder.BuildOrUpdate(Spec(MovementBackend.NavMesh, PresentationKind.Humanoid), path);
            // Grid + Humanoid: the mover conflict fires too, so isolate the collider rule with a NavMesh + Sprite spec on a bare capsule wrapper.
            var editable = PrefabUtility.LoadPrefabContents(path);
            Object.DestroyImmediate(editable.GetComponent<CharacterPlayableAnimator>());
            Object.DestroyImmediate(editable.GetComponent<EquipmentVisualBinder>());
            PrefabUtility.SaveAsPrefabAsset(editable, path);
            PrefabUtility.UnloadPrefabContents(editable);

            AssertRefused(path, Spec(MovementBackend.NavMesh, PresentationKind.Sprite), "CapsuleCollider");
        }

        [Test]
        public void AConflictingComponentOnAChildIsAlsoDetected()
        {
            var path = Path("ChildConflict");
            CharacterWrapperBuilder.BuildOrUpdate(Spec(MovementBackend.Grid, PresentationKind.Sprite), path);
            var editable = PrefabUtility.LoadPrefabContents(path);
            editable.transform.Find("VisualRoot").gameObject.AddComponent<NavMeshAgent>();
            PrefabUtility.SaveAsPrefabAsset(editable, path);
            PrefabUtility.UnloadPrefabContents(editable);

            AssertRefused(path, Spec(MovementBackend.Grid, PresentationKind.Sprite), "NavMeshAgent");
        }

        [Test]
        public void ARefusedUpdateLeavesNoPreviewSceneOpen()
        {
            var path = Path("NoLeak");
            CharacterWrapperBuilder.BuildOrUpdate(Spec(MovementBackend.NavMesh, PresentationKind.Sprite), path);
            var scenesBefore = UnityEngine.SceneManagement.SceneManager.sceneCount;

            Assert.Throws<System.InvalidOperationException>(() => CharacterWrapperBuilder.BuildOrUpdate(Spec(MovementBackend.Grid, PresentationKind.Sprite), path));

            Assert.AreEqual(scenesBefore, UnityEngine.SceneManagement.SceneManager.sceneCount);
            Assert.IsNull(PrefabStageUtility_CurrentOrNull(), "no prefab stage was opened either");
        }

        private static object PrefabStageUtility_CurrentOrNull() => UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();

        // ---------------------------------------------------------------------------------------------
        // Validator
        // ---------------------------------------------------------------------------------------------

        [Test]
        public void ASpriteSpecWithNoModelOrAnimationProfileHasNoErrors()
        {
            var results = CharacterRigValidator.Validate(GridSprite());
            Assert.IsEmpty(results.Where(r => r.Severity == ValidationSeverity.Error), string.Join("\n", results));
        }

        [Test]
        public void TheHumanoidResultsAreUnchangedByTheNewOptions()
        {
            var results = CharacterRigValidator.Validate(new CharacterSetupSpec());

            Assert.IsTrue(results.Any(r => r.Field == nameof(CharacterSetupSpec.ModelPrefab) && r.Severity == ValidationSeverity.Error));
            Assert.IsTrue(results.Any(r => r.Field == nameof(CharacterSetupSpec.AnimationProfile) && r.Severity == ValidationSeverity.Error));
        }

        [Test]
        public void GridAndSpriteSettingErrorsAreReported()
        {
            var spec = GridSprite();
            spec.GridCellSize = 0f;
            spec.GridColliderFill = 1.5f;
            spec.GridColliderHeight = -1f;
            spec.GridSpeed = 0f;
            spec.SpriteLayerSlots = new List<DefinitionId> { new("a"), new("a"), default };

            var errors = CharacterRigValidator.Validate(spec).Where(r => r.Severity == ValidationSeverity.Error).Select(r => r.Field).ToList();

            CollectionAssert.IsSupersetOf(errors, new[]
            {
                nameof(CharacterSetupSpec.GridCellSize), nameof(CharacterSetupSpec.GridColliderFill),
                nameof(CharacterSetupSpec.GridColliderHeight), nameof(CharacterSetupSpec.GridSpeed), nameof(CharacterSetupSpec.SpriteLayerSlots),
            });

            spec.SpriteLayerSlots = Enumerable.Range(0, SpriteSortingContract.MaxSlotsPerObject + 1).Select(i => new DefinitionId("s" + i)).ToList();
            Assert.IsTrue(CharacterRigValidator.Validate(spec).Any(r => r.Field == nameof(CharacterSetupSpec.SpriteLayerSlots) && r.Message.Contains("exceeds")));
        }

        [Test]
        public void AMissingSortingLayerANegativeCardHeightAndAnIgnoredModelAreOnlyWarnings()
        {
            var template = new GameObject("M");
            made.Add(template);
            Directory.CreateDirectory(ScratchFolder);
            AssetDatabase.Refresh();
            var model = PrefabUtility.SaveAsPrefabAsset(template, Path("WarnModel"));
            var spec = GridSprite();
            spec.SpriteSortingLayer = "NoSuchLayer";
            spec.SpriteCardHeight = -1f;
            spec.ModelPrefab = model;

            var results = CharacterRigValidator.Validate(spec);

            Assert.IsEmpty(results.Where(r => r.Severity == ValidationSeverity.Error), string.Join("\n", results));
            var warned = results.Where(r => r.Severity == ValidationSeverity.Warning).Select(r => r.Field).ToList();
            CollectionAssert.IsSupersetOf(warned, new[]
            {
                nameof(CharacterSetupSpec.SpriteSortingLayer), nameof(CharacterSetupSpec.SpriteCardHeight), nameof(CharacterSetupSpec.ModelPrefab),
            });
        }
    }
}

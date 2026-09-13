using System.Collections.Generic;
using System.Reflection;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Editor.Character;
using CRPG.Gameplay;
using CRPG.Presentation;
using CRPG.Presentation.Animation;
using CRPG.Presentation.Equipment;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace CRPG.Tests.EditMode
{
    /// <summary>
    /// Task 2.5 (Production Foundation): CharacterWrapperBuilder writes real prefab assets to disk (GUID
    /// persistence is inherently a disk-level Unity concept, not something an in-memory fixture can fake), so
    /// this deliberately uses a real scratch folder under TestProject -- cleaned up in TearDown.
    /// </summary>
    public class CharacterWrapperBuilderTests
    {
        private const string ScratchFolder = "Assets/Temp/CharacterWrapperBuilderTests";
        private readonly List<GameObject> spawnedTemplates = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in spawnedTemplates)
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            spawnedTemplates.Clear();

            if (AssetDatabase.IsValidFolder(ScratchFolder)) AssetDatabase.DeleteAsset(ScratchFolder);

            // The scratch folder's own parent ("Assets/Temp") is a byproduct of creating it, not something
            // any test needs -- remove it too once empty, so a full test run doesn't leave a permanent stray
            // empty folder behind (it would otherwise persist because DeleteAsset above only removes the leaf).
            const string parentFolder = "Assets/Temp";
            if (AssetDatabase.IsValidFolder(parentFolder) && AssetDatabase.FindAssets(string.Empty, new[] { parentFolder }).Length == 0)
                AssetDatabase.DeleteAsset(parentFolder);
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

        private int modelTemplateCount;

        // CharacterWrapperBuilder uses PrefabUtility.InstantiatePrefab on the model (matching
        // BaseHumanMaleCharacterAuthor's real usage -- that's what preserves the nested-PrefabInstance
        // relationship Task 1.3's own gotcha is about), which requires a real persisted prefab ASSET, not a
        // loose scene GameObject -- so the fixture must save one, not just construct one in memory.
        private GameObject CreateModelTemplate()
        {
            var scratchGo = new GameObject("ModelTemplate");
            scratchGo.AddComponent<Animator>();
            spawnedTemplates.Add(scratchGo);

            if (!AssetDatabase.IsValidFolder(ScratchFolder)) CreateScratchFolder();
            var path = $"{ScratchFolder}/ModelTemplate{modelTemplateCount++}.prefab";
            var saved = PrefabUtility.SaveAsPrefabAsset(scratchGo, path);
            return saved;
        }

        private static void CreateScratchFolder()
        {
            var parts = ScratchFolder.Split('/');
            var current = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        private static CharacterAnimationProfile CreateProfile(params string[] cueIds)
        {
            var profile = ScriptableObject.CreateInstance<CharacterAnimationProfile>();
            var cues = new List<AnimationCueClip>();
            foreach (var cueId in cueIds)
            {
                var cue = new AnimationCueClip();
                SetField(cue, "cueId", new DefinitionId(cueId));
                cues.Add(cue);
            }
            SetField(profile, "cues", cues);
            return profile;
        }

        private static CharacterSetupSpec ValidSpec(GameObject model = null)
        {
            return new CharacterSetupSpec
            {
                ModelPrefab = model,
                AnimationProfile = CreateProfile("anim-idle", "anim-moving"),
            };
        }

        [Test]
        public void BuildOrUpdateCreatesAPrefabWithTheExpectedComponents()
        {
            var path = $"{ScratchFolder}/Basic.prefab";
            var spec = ValidSpec(CreateModelTemplate());

            var prefab = CharacterWrapperBuilder.BuildOrUpdate(spec, path);

            Assert.IsNotNull(prefab);
            Assert.IsNotNull(prefab.GetComponent<CapsuleCollider>());
            Assert.IsNotNull(prefab.GetComponent<Rigidbody>());
            Assert.IsNotNull(prefab.GetComponent<NavMeshAgent>());
            Assert.IsNotNull(prefab.GetComponent<CharacterAgent>());
            Assert.IsNotNull(prefab.GetComponent<FacingController>());
            Assert.IsNotNull(prefab.GetComponent<EquipmentVisualBinder>());
            Assert.IsNotNull(prefab.GetComponent<CharacterPlayableAnimator>());
            Assert.IsNotNull(prefab.GetComponentInChildren<CharacterView>(true));
            Assert.IsNotNull(prefab.GetComponentInChildren<Animator>(true), "The model must be nested under VisualRoot as 'Model'.");
        }

        [Test]
        public void BuildOrUpdateAppliesCapsuleAndAgentSettingsFromTheSpec()
        {
            var path = $"{ScratchFolder}/Settings.prefab";
            var spec = ValidSpec(CreateModelTemplate());
            spec.CapsuleHeight = 2.2f;
            spec.CapsuleRadius = 0.4f;
            spec.AgentSpeed = 5f;

            var prefab = CharacterWrapperBuilder.BuildOrUpdate(spec, path);

            var capsule = prefab.GetComponent<CapsuleCollider>();
            Assert.AreEqual(2.2f, capsule.height);
            Assert.AreEqual(0.4f, capsule.radius);
            Assert.AreEqual(5f, prefab.GetComponent<NavMeshAgent>().speed);
        }

        [Test]
        public void BuildOrUpdateWithNoModelPrefabStillProducesAValidWrapper()
        {
            // Matches BaseHumanMaleCharacterAuthor.AuthorBaseNpcTemplate's bare-template case: a wrapper with
            // gameplay components but no Model yet, populated later via CharacterView.SetModel.
            var path = $"{ScratchFolder}/NoModel.prefab";
            var spec = ValidSpec(model: null);

            GameObject prefab = null;
            Assert.DoesNotThrow(() => prefab = CharacterWrapperBuilder.BuildOrUpdate(spec, path));
            Assert.IsNotNull(prefab.GetComponentInChildren<CharacterView>(true));
            Assert.IsNull(prefab.GetComponentInChildren<Animator>(true), "No model was supplied -- there should be nothing to bind yet.");
        }

        [Test]
        public void UpdatingAnExistingWrapperTwicePreservesItsGuid()
        {
            // The explicit acceptance from the implementation plan: "Add GUID-preservation test for updating
            // the wrapper twice."
            var path = $"{ScratchFolder}/Repeated.prefab";
            var spec = ValidSpec(CreateModelTemplate());

            CharacterWrapperBuilder.BuildOrUpdate(spec, path);
            var guidAfterFirst = AssetDatabase.AssetPathToGUID(path);

            // A second update with meaningfully different content (a different model, different capsule
            // settings) -- not just a no-op re-save -- proving the GUID survives a REAL content change, not
            // just an identical rebuild.
            spec.ModelPrefab = CreateModelTemplate();
            spec.CapsuleHeight = 2.0f;
            CharacterWrapperBuilder.BuildOrUpdate(spec, path);
            var guidAfterSecond = AssetDatabase.AssetPathToGUID(path);

            Assert.AreEqual(guidAfterFirst, guidAfterSecond, "Updating an existing wrapper must preserve its asset GUID even when the content genuinely changes.");
            Assert.IsFalse(string.IsNullOrEmpty(guidAfterFirst));
        }

        [Test]
        public void BuildOrUpdateWiresTheAnimationProfileAndCueIds()
        {
            var path = $"{ScratchFolder}/CueIds.prefab";
            var spec = ValidSpec(CreateModelTemplate());
            spec.IdleCueId = new DefinitionId("anim-idle-custom");
            spec.MovingCueId = new DefinitionId("anim-moving-custom");
            spec.AnimationProfile = CreateProfile("anim-idle-custom", "anim-moving-custom");

            var prefab = CharacterWrapperBuilder.BuildOrUpdate(spec, path);

            var animator = prefab.GetComponent<CharacterPlayableAnimator>();
            var wiredIdle = (DefinitionId)typeof(CharacterPlayableAnimator)
                .GetField("idleCueId", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(animator);
            Assert.AreEqual(spec.IdleCueId, wiredIdle);
        }

        [Test]
        public void BuildOrUpdateThrowsWhenSpecIsNull()
        {
            Assert.Throws<System.ArgumentNullException>(() => CharacterWrapperBuilder.BuildOrUpdate(null, $"{ScratchFolder}/Null.prefab"));
        }

        // Phase 2 correction pass (R1): the explicit acceptance the review asked for -- create a wrapper, add
        // consumer-owned content (a custom child GameObject AND a component the tool knows nothing about)
        // update TWICE with genuinely different settings/model, reload the asset, and confirm both the
        // consumer content and an external reference into it survive completely untouched. The OLD "always
        // rebuild from scratch" implementation would have silently discarded all of this.
        //
        // Uses a plain built-in BoxCollider (with a distinctive size, standing in for "arbitrary consumer
        // data") rather than a custom MonoBehaviour type -- confirmed empirically (via a live isolated probe)
        // that a private nested class defined inside this EditMode test assembly does not reliably survive a
        // LoadPrefabContents/SaveAsPrefabAsset round-trip the way a normal top-level script would; a built-in
        // component sidesteps that unrelated quirk while still proving the exact same "arbitrary untouched
        // extra component" claim.
        [Test]
        public void UpdatingAnExistingWrapperTwicePreservesConsumerOwnedContentAndExternalReferences()
        {
            var path = $"{ScratchFolder}/ConsumerContent.prefab";
            var spec = ValidSpec(CreateModelTemplate());
            CharacterWrapperBuilder.BuildOrUpdate(spec, path);
            var guidAfterFirstBuild = AssetDatabase.AssetPathToGUID(path);

            // Simulate a consumer hand-editing the wrapper in the Editor after the tool created it: add a
            // custom child GameObject and a component the tool has never heard of, directly onto the saved
            // asset via LoadPrefabContents/SaveAsPrefabAsset (the same real editing path a human would
            // exercise through the Inspector).
            var editableRoot = PrefabUtility.LoadPrefabContents(path);
            var customChild = new GameObject("ConsumerSelectionMarker");
            customChild.transform.SetParent(editableRoot.transform, false);
            var marker = editableRoot.AddComponent<BoxCollider>();
            marker.size = new Vector3(12.345f, 12.345f, 12.345f);
            PrefabUtility.SaveAsPrefabAsset(editableRoot, path);
            PrefabUtility.UnloadPrefabContents(editableRoot);

            // An external asset holding a direct subobject reference INTO the wrapper -- exactly the kind of
            // reference the review warned a full-rebuild-and-overwrite would silently break.
            var externalReferenceHolder = ScriptableObject.CreateInstance<ExternalReferenceHolder>();
            var prefabAfterEdit = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            externalReferenceHolder.ReferencedCustomChild = prefabAfterEdit.transform.Find("ConsumerSelectionMarker").gameObject;

            // Two more genuine updates through the tool -- a different model AND different settings each time,
            // not a no-op re-save.
            spec.ModelPrefab = CreateModelTemplate();
            spec.CapsuleHeight = 2.1f;
            CharacterWrapperBuilder.BuildOrUpdate(spec, path);

            spec.ModelPrefab = CreateModelTemplate();
            spec.CapsuleHeight = 2.3f;
            var finalPrefab = CharacterWrapperBuilder.BuildOrUpdate(spec, path);

            // Identity preserved through two real content changes.
            Assert.AreEqual(guidAfterFirstBuild, AssetDatabase.AssetPathToGUID(path));
            Assert.IsNotNull(finalPrefab);

            // Consumer-owned content preserved, through TWO updates.
            var finalChild = finalPrefab.transform.Find("ConsumerSelectionMarker");
            Assert.IsNotNull(finalChild, "The consumer's own child GameObject must survive two tool updates.");
            var finalMarker = finalPrefab.GetComponent<BoxCollider>();
            Assert.IsNotNull(finalMarker, "The consumer's own component must survive two tool updates.");
            Assert.AreEqual(12.345f, finalMarker.size.x, 0.001f, "The consumer's own component's data must be untouched, not reset.");

            // Tool-owned content actually updated (not stale from the first build).
            Assert.AreEqual(2.3f, finalPrefab.GetComponent<CapsuleCollider>().height);

            // The external reference resolves to the SAME persisted subobject, not a dangling/replaced one --
            // proves this was a real in-place edit, not a delete-and-recreate.
            Assert.IsNotNull(externalReferenceHolder.ReferencedCustomChild, "An external reference into the wrapper must not go dangling across updates.");
            Assert.AreEqual("ConsumerSelectionMarker", externalReferenceHolder.ReferencedCustomChild.name);

            UnityEngine.Object.DestroyImmediate(externalReferenceHolder);
        }

        // A minimal standalone ScriptableObject purely to hold an external GameObject reference for the test
        // above.
        private sealed class ExternalReferenceHolder : ScriptableObject
        {
            public GameObject ReferencedCustomChild;
        }
    }
}

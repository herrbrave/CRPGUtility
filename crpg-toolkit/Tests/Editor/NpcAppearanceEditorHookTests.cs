using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using CRPG.Actors;
using CRPG.Editor.Npc;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace CRPG.Tests.EditMode
{
    /// <summary>The extension point on the NpcDefinition inspector: a project supplies its own appearance creator. The test handler is switched on per test (it is discovered globally).</summary>
    public class NpcAppearanceEditorHookTests
    {
        public sealed class TestHandler : INpcAppearanceEditor
        {
            public static bool Enabled, ThrowInHandles, ThrowInCreate, HandleOnlyWithPrefab;
            public static readonly List<NpcDefinition> Asked = new();
            public static readonly List<TestElement> Created = new();

            public bool Handles(NpcDefinition npc)
            {
                if (!Enabled) return false;
                Asked.Add(npc);
                if (ThrowInHandles) throw new InvalidOperationException("handles boom");
                return !HandleOnlyWithPrefab || npc.VisualPrefab != null;
            }

            public VisualElement Create(SerializedObject npc)
            {
                if (ThrowInCreate) throw new InvalidOperationException("create boom");
                var element = new TestElement { name = "test-appearance-extension" };
                Created.Add(element);
                return element;
            }
        }

        public sealed class TestElement : VisualElement, IDisposable
        {
            public bool Disposed;
            public void Dispose() => Disposed = true;
        }

        private readonly List<UnityEngine.Object> made = new();

        [SetUp]
        public void SetUp()
        {
            TestHandler.Enabled = true; TestHandler.ThrowInHandles = TestHandler.ThrowInCreate = TestHandler.HandleOnlyWithPrefab = false;
            TestHandler.Asked.Clear(); TestHandler.Created.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            TestHandler.Enabled = false;   // the handler is found in every inspector built by the rest of the suite: off again
            foreach (var o in made) if (o != null) UnityEngine.Object.DestroyImmediate(o);
            made.Clear();
        }

        private (UnityEditor.Editor Editor, VisualElement Root, NpcDefinition Npc) Inspect()
        {
            var npc = ScriptableObject.CreateInstance<NpcDefinition>(); made.Add(npc);
            var editor = UnityEditor.Editor.CreateEditor(npc, typeof(NpcDefinitionEditor)); made.Add(editor);
            return (editor, editor.CreateInspectorGUI(), npc);
        }

        private static void Rebuild(UnityEditor.Editor editor) => ((NpcDefinitionEditor)editor).RebuildAppearanceExtensions();

        [Test]
        public void AHandlerThatHandlesTheNpcGetsItsElementAddedAfterTheRawAppearanceFoldout()
        {
            var (_, root, npc) = Inspect();

            var element = root.Q<VisualElement>("test-appearance-extension");
            Assert.IsNotNull(element, "the creator's element is in the inspector");
            Assert.Contains(npc, TestHandler.Asked, "it was asked about THIS npc");
            var host = root.Q<VisualElement>("appearance-extensions");
            Assert.IsNotNull(host);
            var foldouts = root.Query<Foldout>().ToList();
            var appearanceFoldout = foldouts.Find(f => f.text == "Appearance");
            Assert.IsNotNull(appearanceFoldout, "the raw Appearance foldout stays: it is the data");
            Assert.Less(root.IndexOf(appearanceFoldout), root.IndexOf(host), "the extension host comes after the raw foldout");
        }

        [Test]
        public void AHandlerThatDoesNotHandleTheNpcAddsNothing()
        {
            TestHandler.Enabled = false;

            var (_, root, _) = Inspect();

            Assert.IsNull(root.Q<VisualElement>("test-appearance-extension"));
            Assert.AreEqual(0, root.Q<VisualElement>("appearance-extensions").childCount);
        }

        [Test]
        public void AHandlerThatThrowsNeverBreaksTheInspectorAndTheRestOfItStillBuilds()
        {
            TestHandler.ThrowInHandles = true;
            LogAssert.Expect(LogType.Exception, new Regex("handles boom"));
            var (_, root, _) = Inspect();
            Assert.IsNotNull(root.Q<VisualElement>("appearance-extensions"));
            Assert.IsNotEmpty(root.Query<PropertyField>().ToList(), "the ordinary fields are still drawn");

            TestHandler.ThrowInHandles = false; TestHandler.ThrowInCreate = true;
            LogAssert.Expect(LogType.Exception, new Regex("create boom"));
            var (_, second, _) = Inspect();
            Assert.IsNotEmpty(second.Query<PropertyField>().ToList());
            Assert.IsNull(second.Q<VisualElement>("test-appearance-extension"));
        }

        private sealed class HostWindow : EditorWindow { }

        [UnityTest]
        public System.Collections.IEnumerator AssigningTheVisualPrefabInAShownInspectorRebuildsTheCreatorOnItsOwn()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) Assert.Ignore("an editor window needs a graphics device: run this fixture without -nographics");
            TestHandler.HandleOnlyWithPrefab = true;
            var (editor, root, _) = Inspect();
            var window = ScriptableObject.CreateInstance<HostWindow>(); made.Add(window);
            window.Show();
            window.rootVisualElement.Add(root);   // the tracker polls through the panel, so the inspector must be in one
            yield return null;
            Assert.AreEqual(0, TestHandler.Created.Count);

            var prefab = new GameObject("prefab-stand-in"); made.Add(prefab);
            editor.serializedObject.FindProperty("visualPrefab").objectReferenceValue = prefab;
            editor.serializedObject.ApplyModifiedPropertiesWithoutUndo();

            var frames = 0;
            while (TestHandler.Created.Count == 0 && frames++ < 120) yield return null;
            window.Close();
            Assert.AreEqual(1, TestHandler.Created.Count, "the creator appeared without anyone calling Rebuild");
        }

        [Test]
        public void RebuildingAsksAgainDisposesTheOldElementAndAddsANewOne()
        {
            TestHandler.HandleOnlyWithPrefab = true;
            var (editor, root, npc) = Inspect();
            Assert.IsNull(root.Q<VisualElement>("test-appearance-extension"), "a new NPC has no visual prefab yet: this handler waits for one");
            Assert.AreEqual(0, TestHandler.Created.Count);

            var serialized = new SerializedObject(npc);
            serialized.FindProperty("visualPrefab").objectReferenceValue = new GameObject("prefab-stand-in"); made.Add(serialized.FindProperty("visualPrefab").objectReferenceValue);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Rebuild(editor);

            var first = TestHandler.Created[0];
            Assert.IsNotNull(root.Q<VisualElement>("test-appearance-extension"), "the NPC now has a visual prefab: the creator appears");

            Rebuild(editor);

            Assert.IsTrue(first.Disposed, "the previous creator element is disposed when the inspector rebuilds it");
            Assert.AreEqual(2, TestHandler.Created.Count);
            Assert.AreEqual(1, root.Query<VisualElement>("test-appearance-extension").ToList().Count, "exactly one creator, not an accumulation");
        }

        [Test]
        public void TheInspectorDisposesTheCreatorWhenItGoesAway()
        {
            var (editor, root, _) = Inspect();
            var element = TestHandler.Created[0];
            Assert.IsFalse(element.Disposed);

            typeof(NpcDefinitionEditor).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.Invoke(editor, null);

            Assert.IsTrue(element.Disposed, "a preview render utility inside the creator would otherwise leak");
            Assert.IsNotNull(root);
        }

        [Test]
        public void TheRawAppearanceDataIsStillDrawnForEveryNpcWhetherOrNotAHandlerApplies()
        {
            TestHandler.Enabled = false;
            var (_, withoutHandler, _) = Inspect();
            TestHandler.Enabled = true;
            var (_, withHandler, _) = Inspect();

            Assert.IsTrue(withoutHandler.Query<PropertyField>().ToList().Exists(f => f.bindingPath == "appearance"));
            Assert.IsTrue(withHandler.Query<PropertyField>().ToList().Exists(f => f.bindingPath == "appearance"));
        }
    }
}

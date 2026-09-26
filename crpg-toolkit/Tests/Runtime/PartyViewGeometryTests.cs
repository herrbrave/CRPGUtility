using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Party;
using CRPG.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace CRPG.Tests.PlayMode
{
    /// <summary>Task 5.1 Gate review (T-2): the hp-bar's fill width is computed from the bar's real resolved
    /// width via GeometryChangedEvent, not a fixed constant -- this is only observable with a real layout pass,
    /// so it lives here (PlayMode), not in PartyViewSkinContractTests (EditMode has no layout pass).</summary>
    public class PartyViewGeometryTests
    {
        private GameObject root;
        private ResourceDefinition hp;

        [TearDown]
        public void Cleanup()
        {
            if (root != null) Object.Destroy(root);
            if (hp != null) Object.Destroy(hp);
        }

        private PartyPresenter BuildPresenter(int current, int max)
        {
            hp = ScriptableObject.CreateInstance<ResourceDefinition>();
            typeof(DefinitionAsset).GetField("id", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(hp, "resource-hp-test");

            var registry = new CharacterRegistry();
            var id = RuntimeEntityId.NewId();
            var character = new CharacterState(id);
            character.AddResource(new ResourceState(hp.Id, max, current));
            registry.Register(character);

            var party = new PartyState(new[] { id });
            party.Select(id);
            party.SetActive(id);
            var names = new Dictionary<RuntimeEntityId, string> { [id] = "Test" };
            return new PartyPresenter(party, registry, hp, names);
        }

        private IEnumerator BuildView(float barWidth, PartyPresenter presenter, System.Action<VisualElement, VisualElement> onBuilt)
        {
            root = new GameObject("PartyViewGeometry");
            var panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            panelSettings.scaleMode = PanelScaleMode.ConstantPixelSize;
            panelSettings.scale = 1f;

            var document = root.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;

            var view = root.AddComponent<CRPG.UI.PartyView>();
            view.UsePlaceholderStyle = false;
            var container = new VisualElement();
            document.rootVisualElement.Add(container);
            view.Initialize(presenter, container);

            var card = container.Q(name: "party-roster").Children().First();
            var bar = card.Q(name: "hp-bar");
            bar.style.width = barWidth; // this is the consumer's/skin's job now (T-2) -- set here to simulate it

            for (var i = 0; i < 6; i++) yield return null; // let a real layout pass run and fire GeometryChangedEvent

            onBuilt(bar, card.Q(name: "hp-bar__fill"));
        }

        [UnityTest]
        public IEnumerator TheFillWidthTracksTheBarsActualResolvedWidthAtOneSize()
        {
            var presenter = BuildPresenter(current: 5, max: 10); // 50%
            yield return BuildView(200f, presenter, (bar, fill) =>
            {
                Assert.AreEqual(200f, bar.resolvedStyle.width, 1f, "the bar itself resolves to whatever width was set on it, unmodified by PartyView");
                Assert.AreEqual(100f, fill.resolvedStyle.width, 2f, "the fill is 50% of the bar's REAL resolved width (200), not a hard-coded constant");
            });
        }

        [UnityTest]
        public IEnumerator TheFillWidthTracksTheBarsActualResolvedWidthAtADifferentSizeProvingItIsNotHardCoded()
        {
            var presenter = BuildPresenter(current: 5, max: 10); // 50%
            yield return BuildView(80f, presenter, (bar, fill) =>
            {
                Assert.AreEqual(80f, bar.resolvedStyle.width, 1f);
                Assert.AreEqual(40f, fill.resolvedStyle.width, 2f, "the SAME 50% now computes against an 80px bar, not the same pixel value as the 200px case above -- proves the width isn't hard-coded to one number");
            });
        }

        [UnityTest]
        public IEnumerator TheFillWidthUpdatesAgainIfTheBarsWidthChangesLater()
        {
            var presenter = BuildPresenter(current: 5, max: 10); // 50%
            GameObject capturedRoot = null;
            yield return BuildView(200f, presenter, (bar, fill) =>
            {
                capturedRoot = root;
                Assert.AreEqual(100f, fill.resolvedStyle.width, 2f);
                bar.style.width = 50f; // simulate a later resize (a different skin, a window resize)
            });

            for (var i = 0; i < 6; i++) yield return null;

            var fillAfter = capturedRoot.GetComponentInChildren<UIDocument>().rootVisualElement.Q(name: "hp-bar__fill");
            Assert.AreEqual(25f, fillAfter.resolvedStyle.width, 2f, "the fill must keep tracking the bar's width, not freeze at the first value it ever saw");
        }
    }
}

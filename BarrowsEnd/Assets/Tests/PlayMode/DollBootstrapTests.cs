using System;
using System.Collections;
using BarrowsEnd.Gate;
using BarrowsEnd.Shell;
using CRPG.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace BarrowsEnd.Tests
{
    /// <summary>Task 5.2: the paper-doll screen end to end, driven through the real click stack -- opening on the
    /// active member's real equipped gear, unequip/equip through the doll's own buttons updating both the slot
    /// list and the live sprite preview, cycling between party members, true-modal open/close, and the HUD's own
    /// toggle button. Boots the same way HudBootstrapTests/ShellTests do (AuthoredParty: Aldric, member 0, is
    /// authored with item-iron-helm/item-short-sword/item-round-shield equipped -- see GateLayout.DefaultMembers).</summary>
    public class DollBootstrapTests
    {
        private const string CorePath = "Assets/Scenes/Core.unity";

        private ShellBootstrap shell;
        private HudBootstrap hud;
        private DollBootstrap doll;

        [UnitySetUp]
        public IEnumerator Boot()
        {
            ShellBootstrap.StartModeOverride = ShellStartMode.AuthoredParty;
            // Task 6.1: cycling to "a different member" needs at least 2 party members -- the real Core scene now
            // authors only the protagonist, so keep booting the demo roster on purpose.
            ShellBootstrap.MembersOverride = GateLayout.DefaultMembers();
            yield return SceneManager.LoadSceneAsync(CorePath, LoadSceneMode.Single);
            shell = UnityEngine.Object.FindFirstObjectByType<ShellBootstrap>();
            Assert.IsNotNull(shell);
            yield return WaitUntil(() => shell.IsReady, 20f, "the shell finished booting");

            hud = UnityEngine.Object.FindFirstObjectByType<HudBootstrap>();
            doll = UnityEngine.Object.FindFirstObjectByType<DollBootstrap>();
            Assert.IsNotNull(hud, "the authored Core scene has a HudBootstrap");
            Assert.IsNotNull(doll, "the authored Core scene has a DollBootstrap");
            yield return WaitUntil(() => HudMenuRoot() != null, 10f, "the HUD finished building its first frame");
        }

        [TearDown]
        public void Cleanup()
        {
            // Belt-and-braces against a leaked modal scope surviving into the next test: DollView.OnDisable now
            // force-closes on its own (the scene reload should trigger it), but a test must not depend on that.
            if (doll != null && doll.IsOpen) doll.ToggleOpen();
            ShellBootstrap.StartModeOverride = null;
            ShellBootstrap.MembersOverride = null;
        }

        private static IEnumerator WaitUntil(Func<bool> condition, float seconds, string what)
        {
            var elapsed = 0f;
            while (!condition() && elapsed < seconds) { elapsed += Mathf.Min(Time.deltaTime, 0.1f); yield return null; }
            Assert.IsTrue(condition(), $"timed out waiting for: {what}");
        }

        private VisualElement HudMenuRoot()
        {
            var root = hud.GetComponent<UIDocument>().rootVisualElement;
            return root.Q(name: "hud-system-menu") != null ? root : null;
        }

        private VisualElement DollRoot() => doll.GetComponent<UIDocument>().rootVisualElement;
        private DollView View() => doll.GetComponent<DollView>();

        [UnityTest]
        public IEnumerator TheHudButtonOpensAndClosesTheDoll()
        {
            Assert.IsFalse(doll.IsOpen);

            UiClick.Pointer(HudMenuRoot().Q(name: "doll-button"));
            yield return null;
            Assert.IsTrue(doll.IsOpen);

            UiClick.Pointer(HudMenuRoot().Q(name: "doll-button"));
            yield return null;
            Assert.IsFalse(doll.IsOpen);
        }

        // Regression guard for a real bug found live (user-reported): the row container originally reused the
        // "be-slot" USS class -- a fixed 40x40px icon square from Task 5.0 -- which clamped every anatomical
        // slot row to 40px tall regardless of its actual content (icon + two text lines + a button), squashing
        // all five rows on top of one another. Text-content assertions elsewhere in this file don't exercise
        // layout at all, so this checks real resolved geometry instead.
        [UnityTest]
        public IEnumerator TheFiveAnatomicalSlotRowsDoNotOverlapVertically()
        {
            doll.ToggleOpen();
            yield return null;
            yield return null; // let layout settle

            var root = DollRoot();
            float? previousBottom = null;
            foreach (var slotId in new[] { "slot-head", "slot-armor", "slot-mainhand", "slot-offhand", "slot-clothing-body" })
            {
                var row = root.Q(name: $"doll-slot-{slotId}");
                Assert.IsNotNull(row, $"row for {slotId} exists");
                var bound = row.worldBound;
                Assert.Greater(bound.height, 40f, $"{slotId}'s row is tall enough to fit its icon + two text lines, not clamped to a 40px icon-square box");
                if (previousBottom.HasValue) Assert.GreaterOrEqual(bound.yMin, previousBottom.Value, $"{slotId}'s row must start at or below the previous row's bottom edge, not overlap it");
                previousBottom = bound.yMax;
            }
        }

        // Regression guard for the follow-up to the row-overlap bug (user-reported): a long item name must not
        // spill outside its column. The label truncates with CSS ellipsis, so this checks the REAL mechanism
        // that recovers the full text -- hovering shows a tooltip with it -- rather than just asserting a style
        // property is set.
        [UnityTest]
        public IEnumerator HoveringATruncatedItemNameShowsItsFullTextInATooltip()
        {
            doll.ToggleOpen();
            yield return null;

            var root = DollRoot();
            var itemLabel = root.Q<Label>(name: "doll-slot-slot-mainhand-item");
            Assert.AreEqual("Short Sword", itemLabel.text, "the full name is still in .text even though the label renders it truncated");

            var tooltipPanel = root.Q(name: "be-tooltip-panel");
            Assert.IsNotNull(tooltipPanel, "the doll builds one tooltip panel per screen");
            Assert.AreEqual(DisplayStyle.None, tooltipPanel.style.display.value, "hidden before any hover");

            using (var enter = PointerEnterEvent.GetPooled()) { enter.target = itemLabel; itemLabel.SendEvent(enter); }
            yield return null;

            Assert.AreEqual(DisplayStyle.Flex, tooltipPanel.style.display.value, "hovering the (possibly truncated) name shows the tooltip");
            Assert.AreEqual("Short Sword", tooltipPanel.Q<Label>(name: "be-tooltip-label").text, "the tooltip shows the item's full, untruncated name");

            using (var leave = PointerLeaveEvent.GetPooled()) { leave.target = itemLabel; itemLabel.SendEvent(leave); }
            yield return null;

            Assert.AreEqual(DisplayStyle.None, tooltipPanel.style.display.value, "leaving hides it again");
        }

        [UnityTest]
        public IEnumerator OpeningShowsTheActiveMembersRealEquippedGear()
        {
            doll.ToggleOpen();
            yield return null;

            var root = DollRoot();
            Assert.AreEqual("Iron Helm", root.Q<Label>(name: "doll-slot-slot-head-item").text);
            Assert.AreEqual("Short Sword", root.Q<Label>(name: "doll-slot-slot-mainhand-item").text);
            Assert.AreEqual("Round Shield", root.Q<Label>(name: "doll-slot-slot-offhand-item").text);
            Assert.AreEqual("-- empty --", root.Q<Label>(name: "doll-slot-slot-armor-item").text);
            Assert.AreEqual("-- empty --", root.Q<Label>(name: "doll-slot-slot-clothing-body-item").text);
        }

        // layer-helmet is index 4 in SpriteLayerCompositor.DefaultSlots' fixed order (body, feet, clothes, hair,
        // helmet, shield, weapon).
        private const int HelmetLayerIndex = 4;

        [UnityTest]
        public IEnumerator UnequippingUpdatesBothTheSlotListAndTheLivePreview()
        {
            doll.ToggleOpen();
            yield return null;
            var helmetImageBefore = View().Preview.LayerImages[HelmetLayerIndex];
            Assert.AreEqual(DisplayStyle.Flex, helmetImageBefore.style.display.value, "the helm is visibly painted before unequipping");

            UiClick.Pointer(DollRoot().Q<Button>(name: "doll-slot-slot-head-unequip"));
            yield return null;

            var root = DollRoot();
            Assert.AreEqual("-- empty --", root.Q<Label>(name: "doll-slot-slot-head-item").text, "the slot list shows it unequipped");
            Assert.AreEqual(DisplayStyle.None, View().Preview.LayerImages[HelmetLayerIndex].style.display.value, "the live sprite preview actually repainted the helmet layer away, not just the slot label");
        }

        [UnityTest]
        public IEnumerator EquippingFromTheBackpackMovesItBackIntoItsSlot()
        {
            doll.ToggleOpen();
            yield return null;
            var unequipButton = DollRoot().Q<Button>(name: "doll-slot-slot-head-unequip");
            var helmInstanceId = (CRPG.Core.RuntimeEntityId)unequipButton.userData; // captured before the click invalidates this row's data
            UiClick.Pointer(unequipButton);
            yield return null;
            Assert.AreEqual("-- empty --", DollRoot().Q<Label>(name: "doll-slot-slot-head-item").text);

            // Queried by the exact instance id (not "the first Equip button found"): the shared backpack may hold
            // other equippable loot too, and clicking the wrong row's button would silently no-op this test.
            var equipButton = DollRoot().Q<Button>(name: $"doll-backpack-row-{helmInstanceId}-equip");
            Assert.IsNotNull(equipButton, "the unequipped helm is now in the backpack with an Equip button");

            // UiClick.Pointer (not Submit) here reliably drops the click: found empirically that a headless batch
            // PlayMode run's panel.Pick() at this exact button's own worldBound centre resolves to an ANCESTOR
            // container instead of the button itself, for an element added by a mid-session Clear()+Add()
            // rebuild specifically (every button present since the doll's first frame -- close, the anatomical
            // slots' own unequip buttons -- picks correctly). UiClick.Submit sidesteps Pick() entirely (a
            // NavigationSubmitEvent with an explicit target, which Button answers unconditionally), so it is not
            // exposed to that picking desync -- this is the project's own documented floor for exactly this class
            // of case (see UiClick.cs), not a workaround invented for this test.
            UiClick.Submit(equipButton);
            yield return null;

            Assert.AreEqual("Iron Helm", DollRoot().Q<Label>(name: "doll-slot-slot-head-item").text, "equipping from the backpack put it back in its anatomical slot");
        }

        [UnityTest]
        public IEnumerator CyclingShowsADifferentMembersRealGearWithoutThrowing()
        {
            doll.ToggleOpen();
            yield return null;
            var hpBefore = DollRoot().Q<Label>(name: "doll-hp").text;

            UiClick.Pointer(DollRoot().Q<Button>(name: "doll-next-button"));
            yield return null;

            var root = DollRoot();
            // Brenna (member 1) has only item-short-sword equipped -- no helm, no shield.
            Assert.AreEqual("Short Sword", root.Q<Label>(name: "doll-slot-slot-mainhand-item").text);
            Assert.AreEqual("-- empty --", root.Q<Label>(name: "doll-slot-slot-head-item").text);
            Assert.AreNotEqual(hpBefore, root.Q<Label>(name: "doll-hp").text, "a different member's HP shows");
        }

        [UnityTest]
        public IEnumerator OpenBeginsATrueModalScopeAndCloseEndsIt()
        {
            var capture = new UIToolkitPointerCapture();
            var screenCentre = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            Assert.IsFalse(capture.IsPointerOverUI(screenCentre), "nothing captured before the doll opens");

            doll.ToggleOpen();
            yield return null;
            Assert.IsTrue(capture.IsPointerOverUI(screenCentre), "a true modal blocks EVERY position, not just the doll's own bounds");

            doll.ToggleOpen();
            yield return null;
            Assert.IsFalse(capture.IsPointerOverUI(screenCentre), "closing ends the modal scope");
        }

        // Regression guard for a real bug found live (user-reported): at Play-session/app shutdown, sibling
        // component teardown order on one GameObject is not guaranteed -- UIDocument can release its own
        // rootVisualElement before DollView's own OnDisable runs, and Close() threw a NullReferenceException
        // there. Disabling the UIDocument component directly (without touching DollView) reproduces exactly that
        // ordering: the panel is torn down, DollView is still enabled, and its own OnDisable/Close() has not run
        // yet.
        [UnityTest]
        public IEnumerator ClosingDoesNotThrowIfTheUIDocumentAlreadyTornDownItsPanel()
        {
            doll.ToggleOpen();
            yield return null;
            Assert.IsTrue(doll.IsOpen);

            var document = doll.GetComponent<UIDocument>();
            document.enabled = false;
            yield return null;
            Assert.IsNull(document.rootVisualElement, "confirms the premise: a disabled UIDocument really has torn down its panel");

            Assert.DoesNotThrow(() => View().Close(), "Close() must tolerate rootVisualElement already being gone");
        }
    }
}

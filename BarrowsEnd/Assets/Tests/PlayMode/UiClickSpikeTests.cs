using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace BarrowsEnd.Tests
{
    /// <summary>
    /// Task 4.2 spike: which way of clicking a UI Toolkit Button works in a PlayMode run here? Three rungs, highest fidelity first:
    /// (A) a real mouse click through the Input System test framework, (B) synthetic pointer events at the button's centre, (C) a
    /// NavigationSubmitEvent with the target set. Each rung clicks its own button on a live panel and the outcome of every rung is logged, so the
    /// finding is recorded whatever happens. Rungs B and C are asserted (B is what the screen tests click with; C is the guaranteed floor); rung A is only recorded: in a batch run the app is not focused, so UI Toolkit ignores real input.
    /// </summary>
    public class UiClickSpikeTests : InputTestFixture
    {
        private GameObject host;
        private PanelSettings settings;

        public override void TearDown()
        {
            if (host != null) Object.Destroy(host);
            if (settings != null) Object.Destroy(settings);
            base.TearDown();
        }

        private (Button a, Button b, Button c, int[] clicks) BuildPanel()
        {
            settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            settings.scale = 1f;
            host = new GameObject("UiClickSpike");
            var document = host.AddComponent<UIDocument>();
            document.panelSettings = settings;

            var clicks = new int[3];
            Button Make(string name, float top, int index)
            {
                var button = new Button(() => clicks[index]++) { name = name, text = name };
                button.style.position = Position.Absolute;
                button.style.left = 40;
                button.style.top = top;
                button.style.width = 160;
                button.style.height = 40;
                document.rootVisualElement.Add(button);
                return button;
            }

            return (Make("rung-a", 40, 0), Make("rung-b", 120, 1), Make("rung-c", 200, 2), clicks);
        }

        [UnityTest]
        public IEnumerator EachClickRungIsTriedAndTheOutcomeRecorded()
        {
            var (a, b, c, clicks) = BuildPanel();
            for (var i = 0; i < 3; i++) yield return null; // the panel needs frames to lay out before worldBound is valid

            // (A) a real mouse: panel coordinates are top-left origin, screen coordinates bottom-left
            var mouse = InputSystem.AddDevice<Mouse>();
            var center = a.worldBound.center;
            Set(mouse.position, new Vector2(center.x, Screen.height - center.y));
            yield return null;
            Press(mouse.leftButton);
            yield return null;
            Release(mouse.leftButton);
            for (var i = 0; i < 3; i++) yield return null;

            // (B) synthetic pointer events at the button's centre
            UiClick.Pointer(b);
            yield return null;

            // (C) the submit floor
            UiClick.Submit(c);
            yield return null;

            Debug.Log($"[UiClickSpike] rung A (real mouse)={(clicks[0] > 0 ? "FIRED" : "did not fire")}, rung B (pointer events)={(clicks[1] > 0 ? "FIRED" : "did not fire")}, " +
                      $"rung C (submit)={(clicks[2] > 0 ? "FIRED" : "did not fire")}; panel pick at B's centre={(b.panel.Pick(b.worldBound.center) != null ? "hit" : "miss")}; " +
                      $"Application.isFocused={Application.isFocused}, Screen={Screen.width}x{Screen.height}");

            Assert.AreEqual(1, clicks[1], "rung B (synthetic pointer events at the button's centre) fires clicked once: the route every screen test uses");
            Assert.AreEqual(1, clicks[2], "the NavigationSubmit floor fires clicked exactly once");
        }
    }
}

using System.Collections;
using CRPG.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace CRPG.Tests.PlayMode
{
    public class UIToolkitPointerCaptureTests
    {
        private GameObject host;
        private PanelSettings settings;
        private UIDocument document;

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (document != null) UIToolkitPointerCapture.Unregister(document);
            if (host != null) Object.Destroy(host);
            if (settings != null) Object.Destroy(settings);
            yield return null;
        }

        [UnityTest]
        public IEnumerator BottomLeftScreenCoordinatesPickTopOnlyPanelAtMultipleScales()
        {
            settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            host = new GameObject("AsymmetricCaptureTest");
            document = host.AddComponent<UIDocument>();
            document.panelSettings = settings;
            var root = document.rootVisualElement;
            root.pickingMode = PickingMode.Ignore;
            var button = new Button { text = "Top-only control" };
            button.style.position = Position.Absolute;
            button.style.left = 20;
            button.style.top = 20;
            button.style.width = 100;
            button.style.height = 30;
            root.Add(button);
            UIToolkitPointerCapture.Register(document);
            var capture = new UIToolkitPointerCapture();

            foreach (var scale in new[] { 1f, 1.5f })
            {
                settings.scale = scale;
                yield return null;
                yield return null;
                var topLeftPixels = button.worldBound.center * scale;
                var inputPosition = new Vector2(topLeftPixels.x, Screen.height - topLeftPixels.y);
                Assert.IsTrue(capture.IsPointerOverUI(inputPosition), "Actual Input System coordinates over the top button must capture at scale " + scale);
                Assert.IsFalse(capture.IsPointerOverUI(topLeftPixels), "Vertically mirrored empty ground must not capture at scale " + scale);
            }
        }
    }
}

using UnityEngine;
using UnityEngine.UIElements;

namespace CRPG.UI
{
    /// <summary>
    /// Task 3.6: the single shared UIDocument/PanelSettings an always-on multi-panel HUD (Party/Inventory/
    /// QuestLog/Save) should sit under -- NOT one UIDocument per panel. Camp/Store/Dialogue each correctly own
    /// their own UIDocument because they're MODAL, one-at-a-time screens; several such panels open
    /// simultaneously would each be an independent root with an undefined relative sortingOrder, and (per
    /// UIToolkitPointerCapture's own established finding) a full-screen root is pickable everywhere by default.
    /// CreateRegion hands each panel a small, explicitly-anchored child VisualElement to build into instead --
    /// the shared root itself is set to PickingMode.Ignore, so only the actual anchored regions (and whatever's
    /// inside them) can ever capture a click; every other point on screen passes through to the world.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class HudRoot : MonoBehaviour
    {
        private UIDocument document;

        private void Awake()
        {
            document = GetComponent<UIDocument>();
            document.rootVisualElement.pickingMode = PickingMode.Ignore;
        }

        private void OnEnable() => UIToolkitPointerCapture.Register(GetComponent<UIDocument>());
        private void OnDisable() => UIToolkitPointerCapture.Unregister(GetComponent<UIDocument>());

        /// <summary>Creates and anchors a new child region for one HUD panel to build its content into.</summary>
        public VisualElement CreateRegion(HudCorner corner, float width, float margin = 16f)
        {
            var region = new VisualElement();
            HudPanelLayout.AnchorToCorner(region, corner, width, margin);
            document.rootVisualElement.Add(region);
            return region;
        }
    }
}

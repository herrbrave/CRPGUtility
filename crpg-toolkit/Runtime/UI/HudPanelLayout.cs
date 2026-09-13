using UnityEngine.UIElements;

namespace CRPG.UI
{
    public enum HudCorner { TopLeft, TopRight, BottomLeft, BottomRight }

    /// <summary>Task 3.6: an always-on HUD panel (Party/Inventory/QuestLog/Save) must NOT use the same
    /// unconstrained "fill available space" root style Camp/Store/Dialogue correctly use for their MODAL,
    /// one-at-a-time screens -- a UIDocument root with no explicit size defaults to filling the whole screen,
    /// and (confirmed directly in Task 3.2) a full-screen root is pickable everywhere by default, meaning
    /// several such panels open simultaneously would each independently block every world click, anywhere on
    /// screen, permanently. AnchorToCorner gives a HUD panel a small, explicitly-sized, non-overlapping region
    /// instead, so IsPointerOverUI only reads true within that panel's own actual bounds.</summary>
    public static class HudPanelLayout
    {
        public static void AnchorToCorner(VisualElement root, HudCorner corner, float width, float margin = 16f)
        {
            root.style.position = Position.Absolute;
            root.style.width = width;
            root.style.height = StyleKeyword.Auto;
            root.style.maxHeight = new Length(60, LengthUnit.Percent);

            root.style.left = StyleKeyword.Auto;
            root.style.right = StyleKeyword.Auto;
            root.style.top = StyleKeyword.Auto;
            root.style.bottom = StyleKeyword.Auto;

            switch (corner)
            {
                case HudCorner.TopLeft:
                    root.style.left = margin;
                    root.style.top = margin;
                    break;
                case HudCorner.TopRight:
                    root.style.right = margin;
                    root.style.top = margin;
                    break;
                case HudCorner.BottomLeft:
                    root.style.left = margin;
                    root.style.bottom = margin;
                    break;
                case HudCorner.BottomRight:
                    root.style.right = margin;
                    root.style.bottom = margin;
                    break;
            }
        }
    }
}

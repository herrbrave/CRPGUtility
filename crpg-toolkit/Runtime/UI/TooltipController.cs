using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace CRPG.UI
{
    /// <summary>
    /// A single floating tooltip panel for one screen, shown on pointer-enter over any element attached via
    /// <see cref="Attach"/> and hidden on pointer-leave. Styled with <c>be-tooltip</c> (barrowsend-skin.uss'
    /// "small paper panel", built out for real use here rather than left as a gallery-only stub).
    /// <para>Exists mainly for a truncated (ellipsis) label: the label keeps its full text in <c>Label.text</c>
    /// even while CSS clips its rendering, so a lazily-evaluated <c>Func&lt;string&gt;</c> always shows the
    /// CURRENT full text, not a stale snapshot taken when <see cref="Attach"/> was called.</para>
    /// </summary>
    public sealed class TooltipController
    {
        private readonly VisualElement root;
        private readonly VisualElement panel;
        private readonly Label label;

        /// <param name="root">Where the floating panel lives -- must be a positioned ancestor (e.g. the screen's
        /// own absolute-fill root) so the panel can float above every sibling without being clipped by a
        /// narrower column.</param>
        public TooltipController(VisualElement root)
        {
            this.root = root ?? throw new ArgumentNullException(nameof(root));

            panel = new VisualElement { name = "be-tooltip-panel", pickingMode = PickingMode.Ignore };
            panel.AddToClassList("be-panel");
            panel.AddToClassList("be-part--panel-paper");
            panel.AddToClassList("be-tooltip"); // added last: its own smaller padding overrides be-panel's
            panel.style.position = Position.Absolute;
            panel.style.display = DisplayStyle.None;

            label = new Label { name = "be-tooltip-label" };
            label.AddToClassList("be-label");
            label.style.whiteSpace = WhiteSpace.NoWrap;
            panel.Add(label);

            root.Add(panel);
        }

        /// <summary>Shows the tooltip just below <paramref name="target"/> on hover, reading
        /// <paramref name="textProvider"/> fresh each time (a null/empty result skips showing it).</summary>
        public void Attach(VisualElement target, Func<string> textProvider)
        {
            target.RegisterCallback<PointerEnterEvent>(_ => Show(target, textProvider()));
            target.RegisterCallback<PointerLeaveEvent>(_ => Hide());
            target.RegisterCallback<DetachFromPanelEvent>(_ => Hide());
        }

        private void Show(VisualElement target, string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            label.text = text;
            var below = new Vector2(target.worldBound.xMin, target.worldBound.yMax + 4f);
            var local = root.WorldToLocal(below);
            panel.style.left = local.x;
            panel.style.top = local.y;
            panel.style.display = DisplayStyle.Flex;
            panel.BringToFront();
        }

        private void Hide() => panel.style.display = DisplayStyle.None;
    }
}

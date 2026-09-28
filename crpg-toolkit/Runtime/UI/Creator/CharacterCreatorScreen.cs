using System;
using System.Collections.Generic;
using System.Linq;
using CRPG.Presentation.Creator;
using UnityEngine;
using UnityEngine.UIElements;

namespace CRPG.UI
{
    /// <summary>
    /// The 2D character creator screen (Task 4.2, reworked in 4.2b): a live layered-sprite preview on the left, one <c>&lt; Title: Value &gt;</c> row per
    /// <see cref="CharacterCreatorModel"/> row on the right (every row shows an "n / count" position, and a row whose options carry a swatch, such as Hair Colour,
    /// draws it beside the name), a one-line "Gear:" label under the rows naming the class's starting kit, and Randomize / Confirm / (Cancel) below. A thin view: all
    /// logic lives in the model, and the screen only renders it and forwards clicks.
    /// <para><b>Contract for a re-skin (Phase 5):</b> every element has a stable <c>name</c> and a <c>creator-*</c> USS class -- NOTE the element's <c>name</c> and
    /// its CLASS are not always the same string (e.g. the <c>creator-kit-label</c> element carries class <c>creator-kit</c>, <c>error-label</c> carries
    /// <c>creator-error</c>, and every <c>row-&lt;key&gt;-prev</c>/<c>-next</c> button carries the SHARED class <c>creator-row__button</c>, not one class per row) --
    /// a Task 5.3 review finding, since targeting the wrong one silently writes a dead USS rule. Structure and sizing are inline (the
    /// screen must fill the panel and the preview must sit on whole pixels); the placeholder LOOK (colours, borders) is inline too and is applied only while
    /// <see cref="UsePlaceholderStyle"/> is true, because an inline style beats any USS rule. Turn it off and add a <see cref="StyleSheet"/> to restyle. Names:
    /// <c>creator-screen</c>, <c>creator-preview-panel</c>, <c>creator-options-panel</c>, <c>row-&lt;key&gt;</c> with <c>row-&lt;key&gt;-value</c>/<c>-position</c>/<c>-prev</c>/<c>-next</c>
    /// (and <c>-swatch</c> on swatch rows; row keys are the layer-slot id, the filter key such as <c>hair-color</c>, or <c>class</c>), <c>creator-kit-label</c>,
    /// <c>random-button</c>, <c>confirm-button</c>, <c>cancel-button</c>, <c>error-label</c>. Task 5.3: with the flag off, the row <c>&lt;</c>/<c>&gt;</c> buttons carry
    /// <c>be-arrow</c> + <c>be-part--arrow-left</c>/<c>-right</c> instead of an inline size and text glyph (matching the paper doll's own cycle-arrow precedent).</para>
    /// <para><b>Panel settings the preview needs to stay pixel-exact:</b> <see cref="PanelScaleMode.ConstantPixelSize"/> (fractional scaling blurs integer-scaled sprites), the
    /// runtime theme, and every dynamic-atlas filter active. <c>previewScale</c> must stay a whole number of screen pixels per sprite pixel.</para>
    /// <para>Show/Hide toggle the root's display and the pointer-capture registration; never disable the GameObject or the UIDocument to hide it (a disabled UIDocument destroys
    /// its root). Disabling this component and re-enabling it rebuilds the layout from the stored model.</para>
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class CharacterCreatorScreen : MonoBehaviour
    {
        [Tooltip("Whole screen pixels per sprite pixel for the preview (a fractional size would blur it).")]
        [SerializeField, Min(1)] private int previewScale = 12;
        [Tooltip("Applies the plain built-in look. Turn off when a StyleSheet supplies the look (inline styles beat USS).")]
        [SerializeField] private bool usePlaceholderStyle = true;
        [SerializeField] private List<StyleSheet> styleSheets = new();

        /// <summary>Matches PartyView/DollView's own public property -- set it before the first <see cref="Initialize"/>
        /// (a flag flip after Build() has already run inline colors would need a rebuild to take effect, not just this
        /// property changing).</summary>
        public bool UsePlaceholderStyle { get => usePlaceholderStyle; set => usePlaceholderStyle = value; }

        private sealed class RowView
        {
            public CreatorRow Row;
            public Label Value;
            public Label Position;
            public VisualElement Swatch;
            public Button Prev, Next;
        }

        private readonly List<RowView> rowViews = new();
        private CharacterCreatorModel model;
        private Action<CreatorResult> onConfirm;
        private Action onCancel;
        private Func<int> seedSource;
        private UIDocument document;
        private LayeredSpritePreview preview;
        private Label errorLabel;
        private Label kitLabel;
        private Button randomButton, confirmButton, cancelButton;
        private bool subscribed;
        private bool finished;
        private bool visible = true;

        /// <summary>The preview widget (null until the layout is built).</summary>
        public LayeredSpritePreview Preview => preview;

        /// <summary>True after Confirm (or Cancel) fired: every button is disabled until <see cref="Show"/> re-arms the screen.</summary>
        public bool IsFinished => finished;

        /// <param name="onConfirm">Called exactly once per arming with the built result.</param>
        /// <param name="onCancel">Optional: the Cancel button exists only when this is given.</param>
        /// <param name="seedSource">Seed for Randomize (default <c>Environment.TickCount</c>); inject one for a deterministic test.</param>
        public void Initialize(CharacterCreatorModel model, Action<CreatorResult> onConfirm, Action onCancel = null, Func<int> seedSource = null)
        {
            Detach();
            this.model = model ?? throw new ArgumentNullException(nameof(model));
            this.onConfirm = onConfirm;
            this.onCancel = onCancel;
            this.seedSource = seedSource ?? (() => Environment.TickCount);
            finished = false;
            if (isActiveAndEnabled) Build();
        }

        /// <summary>Shows the screen (root display, pointer capture) and re-arms Confirm/Cancel.</summary>
        public void Show()
        {
            visible = true;
            finished = false;
            document = document != null ? document : GetComponent<UIDocument>();
            if (document.rootVisualElement != null) document.rootVisualElement.style.display = DisplayStyle.Flex;
            if (isActiveAndEnabled) UIToolkitPointerCapture.Register(document);
            if (preview != null) Refresh();
        }

        /// <summary>Hides the screen and releases the world: the root is not displayed and the pointer capture registration is dropped.</summary>
        public void Hide()
        {
            visible = false;
            document = document != null ? document : GetComponent<UIDocument>();
            if (document.rootVisualElement != null) document.rootVisualElement.style.display = DisplayStyle.None;
            UIToolkitPointerCapture.Unregister(document);
        }

        private void OnEnable()
        {
            document = GetComponent<UIDocument>();
            if (visible) UIToolkitPointerCapture.Register(document);
            if (model != null) Build();
        }

        private void OnDisable()
        {
            Detach();
            if (document != null) UIToolkitPointerCapture.Unregister(document);
        }

        private void Detach()
        {
            if (model != null && subscribed) model.Changed -= OnModelChanged;
            subscribed = false;
        }

        // ---------------------------------------------------------------------------------------------
        // Layout
        // ---------------------------------------------------------------------------------------------

        private void Build()
        {
            Detach();
            var root = document.rootVisualElement;
            root.Clear();
            rowViews.Clear();
            foreach (var sheet in styleSheets)
                if (sheet != null && !root.styleSheets.Contains(sheet)) root.styleSheets.Add(sheet);
            root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

            // Sized explicitly (absolute, all four insets 0) so it genuinely fills the panel: a flex-grow child of an auto-sized document root can collapse to
            // nothing, which would also break the modal capture. Anchored top-left with whole-pixel padding, never centred, so the preview cannot land on a half pixel.
            var screen = Element("creator-screen", "creator-screen");
            screen.AddToClassList("be-screen"); // Task 5.3: without this, the backdrop goes transparent once the
                                                 // placeholder's own inline color is gated off below (Task 5.2's
                                                 // .doll-screen needed the identical fix for its own modal dim).
            screen.style.position = Position.Absolute;
            screen.style.left = screen.style.top = screen.style.right = screen.style.bottom = 0;
            screen.style.flexDirection = FlexDirection.Row;
            screen.style.alignItems = Align.FlexStart;
            screen.style.paddingLeft = screen.style.paddingTop = 48;
            if (usePlaceholderStyle) screen.style.backgroundColor = new Color(0.09f, 0.1f, 0.12f, 1f);
            root.Add(screen);

            var previewPanel = Element("creator-preview-panel", "creator-preview-panel");
            previewPanel.AddToClassList("be-panel");
            previewPanel.AddToClassList("be-part--panel-wood");
            previewPanel.style.flexShrink = 0;
            previewPanel.style.paddingLeft = previewPanel.style.paddingRight = previewPanel.style.paddingTop = previewPanel.style.paddingBottom = 24;
            if (usePlaceholderStyle)
            {
                previewPanel.style.backgroundColor = new Color(0.76f, 0.66f, 0.5f, 1f);
                Border(previewPanel, new Color(0.45f, 0.33f, 0.2f, 1f), 4);
            }

            preview = new LayeredSpritePreview { name = "creator-preview", Scale = Math.Max(1, previewScale) };
            preview.AddToClassList("creator-preview");
            preview.Bind(model.Preview);
            previewPanel.Add(preview);
            screen.Add(previewPanel);

            var options = Element("creator-options-panel", "creator-options-panel");
            options.AddToClassList("be-panel");
            options.AddToClassList("be-part--panel-wood");
            options.style.width = 560;
            options.style.marginLeft = 32;
            options.style.flexShrink = 0;
            options.style.paddingLeft = options.style.paddingRight = options.style.paddingTop = options.style.paddingBottom = 24;
            if (usePlaceholderStyle)
            {
                options.style.backgroundColor = new Color(0.34f, 0.35f, 0.38f, 1f);
                Border(options, new Color(0.2f, 0.2f, 0.22f, 1f), 4);
            }

            foreach (var row in model.Rows) options.Add(BuildRow(row));

            kitLabel = new Label { name = "creator-kit-label" };
            kitLabel.AddToClassList("creator-kit");
            kitLabel.AddToClassList("be-label");
            kitLabel.style.whiteSpace = WhiteSpace.Normal;
            kitLabel.style.marginTop = 4;
            kitLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            if (usePlaceholderStyle) kitLabel.style.color = new Color(0.85f, 0.85f, 0.88f, 1f);
            options.Add(kitLabel);

            errorLabel = new Label { name = "error-label" };
            errorLabel.AddToClassList("creator-error");
            errorLabel.AddToClassList("be-label");
            errorLabel.AddToClassList("be-label--danger");
            errorLabel.style.display = DisplayStyle.None;
            errorLabel.style.whiteSpace = WhiteSpace.Normal;
            errorLabel.style.marginTop = 8;
            if (usePlaceholderStyle) errorLabel.style.color = new Color(1f, 0.55f, 0.5f, 1f);
            options.Add(errorLabel);

            var footer = Element("creator-footer", "creator-footer");
            footer.style.flexDirection = FlexDirection.Row;
            footer.style.marginTop = 16;
            randomButton = FooterButton(footer, "random-button", "Randomize", OnRandomize);
            confirmButton = FooterButton(footer, "confirm-button", "Confirm", OnConfirm);
            cancelButton = onCancel != null ? FooterButton(footer, "cancel-button", "Cancel", OnCancel) : null;
            options.Add(footer);
            screen.Add(options);

            model.Changed += OnModelChanged;
            subscribed = true;
            Refresh();
        }

        private VisualElement BuildRow(CreatorRow row)
        {
            var view = new RowView { Row = row };
            var container = Element("row-" + row.Key, "creator-row");
            container.style.flexDirection = FlexDirection.Row;
            container.style.alignItems = Align.Center;
            container.style.marginBottom = 8;

            view.Prev = RowButton(container, $"row-{row.Key}-prev", isLeft: true, () => row.Advance(-1));

            if (row.Options.Any(o => o.HasSwatch))
            {
                view.Swatch = new VisualElement { name = $"row-{row.Key}-swatch" };
                view.Swatch.AddToClassList("creator-row__swatch");
                view.Swatch.style.width = view.Swatch.style.height = 28;
                view.Swatch.style.flexShrink = 0;
                view.Swatch.style.marginLeft = 8;
                // NOT gated behind usePlaceholderStyle (Task 5.3 review): this border is functional, not a look
                // choice -- it keeps visually similar swatches (blond vs silver hair) distinguishable regardless
                // of skin, the same reasoning PartyView's hp-bar geometry is never gated either.
                Border(view.Swatch, new Color(0.95f, 0.95f, 0.97f, 1f), 2);
                container.Add(view.Swatch);
            }

            view.Value = new Label { name = $"row-{row.Key}-value" };
            view.Value.AddToClassList("creator-row__value");
            view.Value.AddToClassList("be-label");
            view.Value.style.flexGrow = 1;
            view.Value.style.flexShrink = 1;
            view.Value.style.unityTextAlign = TextAnchor.MiddleCenter;
            view.Value.style.marginLeft = view.Value.style.marginRight = 8;
            if (usePlaceholderStyle) view.Value.style.color = Color.white;
            container.Add(view.Value);

            view.Position = new Label { name = $"row-{row.Key}-position" };
            view.Position.AddToClassList("creator-row__position");
            view.Position.AddToClassList("be-label");
            view.Position.AddToClassList("be-label--muted"); // the "n / count" counter -- genuinely secondary text
            view.Position.style.minWidth = 64;
            view.Position.style.unityTextAlign = TextAnchor.MiddleRight;
            view.Position.style.marginRight = 8;
            if (usePlaceholderStyle) view.Position.style.color = new Color(0.8f, 0.8f, 0.82f, 1f);
            container.Add(view.Position);

            view.Next = RowButton(container, $"row-{row.Key}-next", isLeft: false, () => row.Advance(1));

            rowViews.Add(view);
            return container;
        }

        private static VisualElement Element(string name, string cssClass)
        {
            var element = new VisualElement { name = name };
            element.AddToClassList(cssClass);
            return element;
        }

        // Task 5.3 review (blocker): this used to set its inline 36x32 size and "<"/">" text UNCONDITIONALLY, so
        // simply adding be-arrow/be-part--arrow-left(-right) classes alongside it would have stretched that 22x21
        // art to a non-integer scale with the text glyph drawn on top -- an inline style always beats USS. Now
        // gated exactly like Border() elsewhere in this file: flag on keeps today's look/size unchanged (so other,
        // non-BarrowsEnd toolkit consumers on the default theme see no difference); flag off drops the inline
        // size (letting be-arrow's own 22x21 win) and the text (the background image IS the glyph), matching
        // DollView.ArrowButton's real, working precedent exactly.
        private Button RowButton(VisualElement parent, string name, bool isLeft, Action onClick)
        {
            var button = new Button(onClick) { name = name };
            button.AddToClassList("creator-row__button");
            if (usePlaceholderStyle)
            {
                button.text = isLeft ? "<" : ">";
                button.style.width = 36;
                button.style.height = 32;
            }
            else
            {
                button.text = string.Empty;
                button.AddToClassList("be-arrow");
                button.AddToClassList(isLeft ? "be-part--arrow-left" : "be-part--arrow-right");
            }

            parent.Add(button);
            return button;
        }

        private Button FooterButton(VisualElement parent, string name, string text, Action onClick)
        {
            var button = new Button(onClick) { name = name, text = text };
            button.AddToClassList("creator-footer__button");
            button.AddToClassList("be-button");
            button.AddToClassList("be-part--button-secondary"); // matches the HUD's own convention: secondary throughout, no primary/secondary split
            button.style.height = 36;
            button.style.minWidth = 110;
            button.style.marginRight = 8;
            parent.Add(button);
            return button;
        }

        private static void Border(VisualElement element, Color color, float width)
        {
            element.style.borderTopWidth = element.style.borderBottomWidth = element.style.borderLeftWidth = element.style.borderRightWidth = width;
            element.style.borderTopColor = element.style.borderBottomColor = element.style.borderLeftColor = element.style.borderRightColor = color;
        }

        // ---------------------------------------------------------------------------------------------
        // Behaviour
        // ---------------------------------------------------------------------------------------------

        private void OnModelChanged()
        {
            ClearError();
            Refresh();
        }

        private void Refresh()
        {
            foreach (var view in rowViews)
            {
                var row = view.Row;
                view.Value.text = $"{row.Title}: {(row.Count > 0 ? row.Selected.Name : "None")}";
                view.Position.text = row.Count > 0 ? $"{row.Index + 1} / {row.Count}" : "";
                if (view.Swatch != null)
                {
                    var has = row.Count > 0 && row.Selected.HasSwatch;
                    view.Swatch.style.backgroundColor = has ? (Color)row.Selected.Swatch : Color.clear;
                }

                var cycle = !finished && row.Count > 1; // a row with nothing to move to has nothing to click
                view.Prev.SetEnabled(cycle);
                view.Next.SetEnabled(cycle);
            }

            var kit = model.StartingItems;
            kitLabel.text = kit.Count > 0 ? "Gear: " + string.Join(", ", kit.Select(i => i.Name)) : "Gear: none";
            randomButton?.SetEnabled(!finished);
            confirmButton?.SetEnabled(!finished);
            cancelButton?.SetEnabled(!finished);
            preview?.Refresh();
        }

        private void ClearError()
        {
            if (errorLabel == null) return;
            errorLabel.text = "";
            errorLabel.style.display = DisplayStyle.None;
        }

        private void OnRandomize()
        {
            if (finished) return;
            model.Randomize(seedSource());
        }

        private void OnConfirm()
        {
            if (finished) return;

            CreatorResult result;
            try
            {
                result = model.Build();
            }
            catch (InvalidOperationException e)
            {
                errorLabel.text = e.Message;
                errorLabel.style.display = DisplayStyle.Flex;
                return;
            }

            finished = true;
            Refresh();
            onConfirm?.Invoke(result);
        }

        private void OnCancel()
        {
            if (finished) return;
            finished = true;
            Refresh();
            onCancel?.Invoke();
        }
    }
}

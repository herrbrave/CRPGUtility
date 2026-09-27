using System;
using System.Collections.Generic;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Inventory;
using CRPG.Presentation.Creator;
using CRPG.Presentation.Equipment;
using CRPG.Presentation.Sprites;
using UnityEngine;
using UnityEngine.UIElements;

namespace CRPG.UI
{
    /// <summary>
    /// The paper-doll screen (Task 5.2): a live layered-sprite preview with cycle arrows, the five fixed
    /// anatomical equip slots, the six ability scores, current/max HP, and the shared backpack with Equip/
    /// Unequip. True modal (<see cref="UIToolkitPointerCapture.BeginModal"/>/<c>Dispose</c>), unlike Camp/Store/
    /// Dialogue/the creator which only <c>Register</c> their own document (their own elements swallow clicks
    /// landing on them, not genuine modality) -- a Task 5.2 review finding.
    /// <para>Feeds ONE <see cref="SpriteLayerState"/> from BOTH a <see cref="SpriteAppearanceBinder"/> (body/hair/
    /// clothes) and a <see cref="SpriteEquipmentBinder"/> (gear) -- the equipment binder alone paints gear over
    /// nothing (another review finding). Both binders throw if re-<c>Initialize</c>d for a different tuple without
    /// <see cref="SpriteEquipmentBinder.Unbind"/>/<see cref="SpriteAppearanceBinder.Unbind"/> first, so every
    /// retarget (cycle, re-open after a load) unbinds both before rebinding. <see cref="LayeredSpritePreview.Refresh"/>
    /// is never called automatically by anything, so this view calls it explicitly after every retarget and every
    /// equip/unequip -- both self-initiated (its own buttons) and external (its own subscription to
    /// ItemEquipped/ItemUnequipped, matching <see cref="InventoryView"/>'s precedent). That handler also forces
    /// <see cref="SpriteEquipmentBinder.Refresh"/> first: a full reconcile from the current inventory rather than
    /// trusting the fired event's payload, so it is correct regardless of subscriber order relative to the
    /// binder's own subscription to the same event.</para>
    /// <para><b>Contract for a re-skin:</b> stable names/classes throughout, same convention as PartyView/
    /// CharacterCreatorScreen: <c>doll-screen</c>, <c>doll-panel</c>, <c>doll-preview</c>, <c>doll-prev-button</c>/
    /// <c>doll-next-button</c>, <c>doll-slot-&lt;slotId&gt;</c> (each a <c>doll-slot-row</c> -- NOT <c>be-slot</c>,
    /// which is a fixed 40x40px icon square from Task 5.0, the wrong shape for a whole row), <c>doll-stats</c>/
    /// <c>doll-stat-&lt;statId&gt;</c>, <c>doll-hp</c>, <c>doll-backpack</c>, <c>doll-status</c>, <c>doll-close-button</c>.
    /// The placeholder LOOK is inline and gated behind <see cref="usePlaceholderStyle"/> (default true); turn it off
    /// and add a StyleSheet to restyle (inline styles beat USS).</para>
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class DollView : MonoBehaviour
    {
        [SerializeField] private bool usePlaceholderStyle = true;
        [SerializeField] private List<StyleSheet> styleSheets = new();
        /// <summary>Applies the plain built-in look. Turn off (before the first <see cref="Initialize"/>, like
        /// HudBootstrap does for PartyView) when the skin's own theme supplies the look instead -- inline styles
        /// beat USS, so a flag flip after Build() has already run inline colors would need a rebuild to take
        /// effect, not just this property changing.</summary>
        public bool UsePlaceholderStyle { get => usePlaceholderStyle; set => usePlaceholderStyle = value; }
        [Tooltip("Whole screen pixels per sprite pixel for the preview (a fractional size would blur it).")]
        [SerializeField, Min(1)] private int previewScale = 8;

        private sealed class SlotRow
        {
            public DefinitionId SlotId;
            public Label Label;
            public Image Icon;
            public Label ItemName;
            public Button UnequipButton;
        }

        private UIDocument document;
        private DollPresenter presenter;
        private ICharacterRegistry characters;
        private IRulesDatabase rulesDatabase;
        private PartyInventoryState inventory;
        private IGameEventBus eventBus;
        private SpriteEquipmentBinder equipmentBinder;
        private SpriteAppearanceBinder appearanceBinder;
        private SpriteLayerState previewState;
        private LayeredSpritePreview preview;

        private readonly List<SlotRow> slotRows = new();
        private readonly List<Label> abilityLabels = new();
        private readonly List<LayerPaint> iconPaintBuffer = new();

        private VisualElement backpackContainer;
        private Label hpLabel;
        private Label statusLabel;
        private Button prevButton, nextButton;
        private bool subscribed;
        private bool isOpen;
        private TooltipController tooltip;
        private IDisposable modalScope;

        public ITextLocalizer Localizer { get; set; }
        public bool IsOpen => isOpen;

        /// <summary>The preview widget (null until the layout is built) -- exposed for the same reason
        /// CharacterCreatorScreen.Preview is: a test (or another consumer) verifying the live sprite actually
        /// changed, not just the slot text.</summary>
        public LayeredSpritePreview Preview => preview;

        /// <summary>(Re)binds the doll to a session's state -- called once at first build and again after a
        /// successful load, matching InventoryView.Initialize's idempotent unsubscribe/rebuild/resubscribe
        /// pattern. Retargets both sprite binders to the new tuple.</summary>
        public void Initialize(DollPresenter dollPresenter, ICharacterRegistry characterRegistry, IRulesDatabase rules, PartyInventoryState partyInventory, IGameEventBus gameEventBus)
        {
            Unsubscribe();

            presenter = dollPresenter ?? throw new ArgumentNullException(nameof(dollPresenter));
            characters = characterRegistry ?? throw new ArgumentNullException(nameof(characterRegistry));
            rulesDatabase = rules ?? throw new ArgumentNullException(nameof(rules));
            inventory = partyInventory ?? throw new ArgumentNullException(nameof(partyInventory));
            eventBus = gameEventBus;

            document = document != null ? document : GetComponent<UIDocument>();
            EnsureBinders();
            if (previewState == null) previewState = new SpriteLayerState(SpriteLayerCompositor.DefaultSlots());
            if (document.rootVisualElement.childCount == 0) Build();

            RetargetBinders();
            Refresh();
            Subscribe();

            if (isOpen) document.rootVisualElement.style.display = DisplayStyle.Flex;
            else document.rootVisualElement.style.display = DisplayStyle.None;
        }

        /// <summary>Opens the doll on the party's currently active character (it may have changed via the HUD
        /// roster while the doll was closed) and begins a true modal scope.</summary>
        public void Open()
        {
            if (isOpen) return;
            isOpen = true;
            if (document != null && document.rootVisualElement != null) document.rootVisualElement.style.display = DisplayStyle.Flex;
            modalScope = UIToolkitPointerCapture.BeginModal();

            presenter.SyncToActiveCharacter();
            RetargetBinders();
            Refresh();
        }

        // Guarded like CharacterCreatorScreen.Show/Hide: at Play-session/app shutdown, UIDocument can release its
        // own rootVisualElement (and even the component itself can go "destroyed but referenced") before this
        // component's own OnDisable/OnDestroy runs -- sibling-component teardown order on the same GameObject is
        // not guaranteed. Found live: Close() (called from OnDisable while the doll was left open) threw a
        // NullReferenceException at shutdown because rootVisualElement was already gone.
        public void Close()
        {
            if (!isOpen) return;
            isOpen = false;
            if (document != null && document.rootVisualElement != null) document.rootVisualElement.style.display = DisplayStyle.None;
            modalScope?.Dispose();
            modalScope = null;
        }

        // Force-closes rather than just unsubscribing: UIToolkitPointerCapture.BeginModal's scope MUST be
        // disposed by whoever began it, or modalDepth leaks and every future pointer position reads as captured
        // forever (found by a real scene-reload-mid-open in a PlayMode test run -- a leaked modal from one test
        // silently broke an unrelated later test's own "nothing captured" assertion).
        private void OnDisable()
        {
            Close();
            Unsubscribe();
        }

        private void OnDestroy()
        {
            Close();
            Unsubscribe();
        }

        private void EnsureBinders()
        {
            if (equipmentBinder == null) equipmentBinder = gameObject.AddComponent<SpriteEquipmentBinder>();
            if (appearanceBinder == null) appearanceBinder = gameObject.AddComponent<SpriteAppearanceBinder>();
        }

        /// <summary>Unbinds both sprite binders (idempotent, safe even the very first time) and rebinds them to
        /// the presenter's current character -- required before every Initialize()/cycle/re-open, since both
        /// binders throw on a different tuple otherwise.</summary>
        private void RetargetBinders()
        {
            equipmentBinder.Unbind();
            appearanceBinder.Unbind();

            var character = presenter.CurrentCharacter;
            appearanceBinder.SetTarget(previewState);
            appearanceBinder.Initialize(character, characters, rulesDatabase);

            equipmentBinder.SetTarget(previewState);
            equipmentBinder.Initialize(character, rulesDatabase, inventory, eventBus);

            preview?.Refresh();
        }

        private void Subscribe()
        {
            if (subscribed || eventBus == null) return;
            eventBus.Subscribe(GameEvent.ItemEquipped, HandleInventoryEvent);
            eventBus.Subscribe(GameEvent.ItemUnequipped, HandleInventoryEvent);
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed) return;
            eventBus?.Unsubscribe(GameEvent.ItemEquipped, HandleInventoryEvent);
            eventBus?.Unsubscribe(GameEvent.ItemUnequipped, HandleInventoryEvent);
            subscribed = false;
        }

        // RefreshEquipmentAndPreview() forces a full reconcile before repainting rather than trusting the fired
        // event's payload -- correct regardless of whether this handler runs before or after
        // SpriteEquipmentBinder's own subscription to the same event.
        private void HandleInventoryEvent(GameEventContext context) => RefreshEquipmentAndPreview();

        // ---------------------------------------------------------------------------------------------
        // Layout
        // ---------------------------------------------------------------------------------------------

        private void Build()
        {
            var root = document.rootVisualElement;
            root.Clear();
            slotRows.Clear();
            abilityLabels.Clear();
            foreach (var sheet in styleSheets)
                if (sheet != null && !root.styleSheets.Contains(sheet)) root.styleSheets.Add(sheet);

            var screen = Element("doll-screen", "doll-screen");
            screen.style.position = Position.Absolute;
            screen.style.left = screen.style.top = screen.style.right = screen.style.bottom = 0;
            screen.style.alignItems = Align.Center;
            screen.style.justifyContent = Justify.Center;
            if (usePlaceholderStyle) screen.style.backgroundColor = new Color(0f, 0f, 0f, 0.55f);
            root.Add(screen);
            tooltip = new TooltipController(screen); // a truncated (ellipsis) name label still has its full text
                                                      // in .text -- see the class doc comment

            var panel = Element("doll-panel", "doll-panel");
            panel.AddToClassList("be-panel");
            panel.AddToClassList("be-part--panel-wood");
            panel.style.flexDirection = FlexDirection.Row;
            panel.style.paddingLeft = panel.style.paddingRight = panel.style.paddingTop = panel.style.paddingBottom = 16;
            if (usePlaceholderStyle)
            {
                panel.style.backgroundColor = new Color(0.34f, 0.35f, 0.38f, 1f);
                Border(panel, new Color(0.2f, 0.2f, 0.22f, 1f), 4);
            }
            screen.Add(panel);

            // Left column: cycle arrows + preview + stat block + HP.
            var leftColumn = Element("doll-left", "doll-left");
            leftColumn.style.flexShrink = 0;
            leftColumn.style.alignItems = Align.Center;
            leftColumn.style.marginRight = 24;
            panel.Add(leftColumn);

            var cycleRow = Element("doll-cycle-row", "doll-cycle-row");
            cycleRow.style.flexDirection = FlexDirection.Row;
            cycleRow.style.alignItems = Align.Center;
            leftColumn.Add(cycleRow);

            prevButton = ArrowButton(cycleRow, "doll-prev-button", isLeft: true, OnCyclePrevious);

            preview = new LayeredSpritePreview { name = "doll-preview", Scale = Math.Max(1, previewScale) };
            preview.AddToClassList("doll-preview");
            preview.style.marginLeft = preview.style.marginRight = 8;
            preview.Bind(previewState);
            cycleRow.Add(preview);

            nextButton = ArrowButton(cycleRow, "doll-next-button", isLeft: false, OnCycleNext);

            hpLabel = new Label { name = "doll-hp" };
            hpLabel.AddToClassList("doll-hp");
            hpLabel.AddToClassList("be-label");
            hpLabel.style.marginTop = 8;
            if (usePlaceholderStyle) hpLabel.style.color = Color.white;
            leftColumn.Add(hpLabel);

            var statsBlock = Element("doll-stats", "doll-stats");
            statsBlock.style.marginTop = 8;
            leftColumn.Add(statsBlock);
            foreach (var statId in DollPresenter.AbilityStatIds)
            {
                var label = new Label { name = $"doll-stat-{statId.Value}" };
                label.AddToClassList("doll-stat");
                label.AddToClassList("be-label");
                label.AddToClassList("be-label--muted");
                if (usePlaceholderStyle) label.style.color = new Color(0.85f, 0.85f, 0.88f, 1f);
                abilityLabels.Add(label);
                statsBlock.Add(label);
            }

            // Middle column: the five anatomical slots.
            var slotsColumn = Element("doll-slots", "doll-slots");
            slotsColumn.style.flexShrink = 0;
            slotsColumn.style.width = 220;
            slotsColumn.style.marginRight = 24;
            panel.Add(slotsColumn);
            foreach (var slotId in DollPresenter.AnatomicalSlots) slotsColumn.Add(BuildSlotRow(slotId));

            // Right column: backpack + status + close.
            var rightColumn = Element("doll-right", "doll-right");
            rightColumn.style.width = 260;
            panel.Add(rightColumn);

            backpackContainer = Element("doll-backpack", "doll-backpack");
            rightColumn.Add(backpackContainer);

            statusLabel = new Label { name = "doll-status" };
            statusLabel.AddToClassList("doll-status");
            statusLabel.AddToClassList("be-label");
            statusLabel.AddToClassList("be-label--danger");
            statusLabel.style.whiteSpace = WhiteSpace.Normal;
            statusLabel.style.marginTop = 8;
            if (usePlaceholderStyle) statusLabel.style.color = new Color(1f, 0.55f, 0.5f, 1f);
            rightColumn.Add(statusLabel);

            var closeButton = new Button(Close) { name = "doll-close-button", text = "Close" };
            closeButton.AddToClassList("be-button");
            closeButton.AddToClassList("be-part--button-secondary");
            closeButton.style.marginTop = 8;
            closeButton.style.width = 100;
            closeButton.style.height = 36;
            rightColumn.Add(closeButton);
        }

        private VisualElement BuildSlotRow(DefinitionId slotId)
        {
            var row = new SlotRow { SlotId = slotId };
            // NOT "be-slot": that class is a fixed 40x40px icon square (barrowsend-skin.uss, Task 5.0's
            // equipment-icon concept), and forcing a whole row (icon + text column + button) into a 40x40 box is
            // exactly what squashed every row on top of its neighbour when this was first built and tested live.
            var container = Element($"doll-slot-{slotId.Value}", "doll-slot-row");
            container.style.flexDirection = FlexDirection.Row;
            container.style.alignItems = Align.Center;
            container.style.marginBottom = 8;
            container.style.minHeight = 40;

            // be-slot (Task 5.0) is reused here for exactly the equipment-icon-square concept it was designed
            // for -- 32px, not its own default 40px (a non-integer scale against 16px sprites, D5's pixel-
            // exactness rule; the class's OWN width/height are overridden by these inline values, same trick
            // used against a real property below in Truncate()). be-part--panel-inset gives it a real
            // recessed-socket background (existing art, no new part needed) instead of the bare box the gallery
            // itself never gave be-slot.
            row.Icon = new Image { name = $"doll-slot-{slotId.Value}-icon" };
            row.Icon.AddToClassList("doll-slot__icon");
            row.Icon.AddToClassList("be-slot");
            row.Icon.AddToClassList("be-part--panel-inset");
            row.Icon.style.width = row.Icon.style.height = 32;
            row.Icon.style.flexShrink = 0;
            row.Icon.scaleMode = ScaleMode.ScaleToFit;
            container.Add(row.Icon);

            var textColumn = Element($"doll-slot-{slotId.Value}-text", "doll-slot__text");
            textColumn.style.marginLeft = 8;
            textColumn.style.flexGrow = 1;
            container.Add(textColumn);

            row.Label = new Label { name = $"doll-slot-{slotId.Value}-label" };
            // NOT be-label--muted: found live (user-reported) -- its low-contrast --be-ink-muted, deliberately
            // dim for de-emphasis, read as barely visible on the wood panel's own brown. This label is content
            // the user actually needs to read (which slot is which), not secondary text, so it gets full-strength
            // ink; doll-slot__label's own CSS rule (bold) is what keeps it visually distinct from the item name
            // below it instead.
            row.Label.AddToClassList("doll-slot__label");
            row.Label.AddToClassList("be-label");
            if (usePlaceholderStyle) row.Label.style.color = new Color(0.8f, 0.8f, 0.82f, 1f);
            textColumn.Add(row.Label);

            row.ItemName = new Label { name = $"doll-slot-{slotId.Value}-item" };
            row.ItemName.AddToClassList("doll-slot__item");
            row.ItemName.AddToClassList("be-label");
            Truncate(row.ItemName);
            if (usePlaceholderStyle) row.ItemName.style.color = Color.white;
            textColumn.Add(row.ItemName);
            tooltip.Attach(row.ItemName, () => row.ItemName.text);

            row.UnequipButton = new Button(() => OnUnequipSlotClicked(row)) { name = $"doll-slot-{slotId.Value}-unequip", text = "X" };
            row.UnequipButton.AddToClassList("be-button");
            row.UnequipButton.AddToClassList("be-part--button-square");
            row.UnequipButton.style.width = 28;
            row.UnequipButton.style.height = 28;
            container.Add(row.UnequipButton);

            slotRows.Add(row);
            return container;
        }

        private static VisualElement Element(string name, string cssClass)
        {
            var element = new VisualElement { name = name };
            element.AddToClassList(cssClass);
            return element;
        }

        // be-arrow (Task 5.0's "the creator's < >", never actually wired to a clickable Button before this --
        // the gallery's own example is a plain, unclickable VisualElement swatch): no text, since the background
        // image (be-part--arrow-left/-right) IS the glyph.
        private static Button ArrowButton(VisualElement parent, string name, bool isLeft, Action onClick)
        {
            var button = new Button(onClick) { name = name, text = string.Empty };
            button.AddToClassList("be-arrow");
            button.AddToClassList(isLeft ? "be-part--arrow-left" : "be-part--arrow-right");
            parent.Add(button);
            return button;
        }

        private static void Border(VisualElement element, Color color, float width)
        {
            element.style.borderTopWidth = element.style.borderBottomWidth = element.style.borderLeftWidth = element.style.borderRightWidth = width;
            element.style.borderTopColor = element.style.borderBottomColor = element.style.borderLeftColor = element.style.borderRightColor = color;
        }

        // A long item/slot name (found live: it visibly overflowed its column) clips with an ellipsis instead of
        // spilling into whatever sits next to it; TooltipController.Attach reads the label's own full .text on
        // hover, so nothing here needs to remember the untruncated string separately.
        private static void Truncate(Label label)
        {
            label.style.whiteSpace = WhiteSpace.NoWrap;
            label.style.overflow = Overflow.Hidden;
            label.style.textOverflow = TextOverflow.Ellipsis;
        }

        // ---------------------------------------------------------------------------------------------
        // Refresh
        // ---------------------------------------------------------------------------------------------

        private void Refresh()
        {
            RefreshSlotsAndBackpack();
            RefreshStatsAndHp();
            prevButton.SetEnabled(presenter.MemberCount > 1);
            nextButton.SetEnabled(presenter.MemberCount > 1);
        }

        private void RefreshStatsAndHp()
        {
            var scores = presenter.GetAbilityScores();
            for (var i = 0; i < abilityLabels.Count; i++)
                abilityLabels[i].text = i < scores.Count ? $"{scores[i].Abbreviation} {scores[i].Score}" : "";

            var (current, max) = presenter.GetHitPoints();
            hpLabel.text = $"HP {current} / {max}";
        }

        private void RefreshSlotsAndBackpack()
        {
            var slots = presenter.GetAnatomicalSlots();
            for (var i = 0; i < slotRows.Count && i < slots.Count; i++)
            {
                var row = slotRows[i];
                var slot = slots[i];
                row.Label.text = TextKeyResolver.Resolve(slot.SlotName, Localizer);

                if (slot.HasItem)
                {
                    var item = slot.Item;
                    row.ItemName.text = TextKeyResolver.Resolve(item.Name, Localizer);
                    row.Icon.sprite = ResolveIcon(item.InstanceId);
                    row.Icon.RemoveFromClassList("be-slot--empty");
                    row.UnequipButton.style.display = DisplayStyle.Flex;
                    row.UnequipButton.userData = item.InstanceId;
                }
                else
                {
                    row.ItemName.text = "-- empty --";
                    row.Icon.sprite = null; // the be-slot socket (background art) still shows, just with no glyph
                    row.Icon.AddToClassList("be-slot--empty");
                    row.UnequipButton.style.display = DisplayStyle.None;
                    row.UnequipButton.userData = null;
                }
            }

            backpackContainer.Clear();
            var backpackTitle = new Label("Backpack") { name = "doll-backpack-title" };
            backpackTitle.AddToClassList("be-label");
            backpackTitle.style.marginBottom = 4;
            backpackContainer.Add(backpackTitle);
            foreach (var item in presenter.GetBackpackItems())
                backpackContainer.Add(BuildBackpackRow(item));
        }

        private VisualElement BuildBackpackRow(InventoryItemSummary item)
        {
            var row = new VisualElement { name = $"doll-backpack-row-{item.InstanceId}" };
            row.AddToClassList("doll-backpack__row");
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 4;

            // 32px, not the earlier 24px: 24/16 was ALSO a non-integer scale against a 16px sprite cell (the same
            // pixel-exactness mistake finding 6 already flagged for the anatomical slots), just never caught
            // live because a backpack item's icon is small enough not to look obviously wrong at a glance.
            var icon = new Image { name = $"doll-backpack-row-{item.InstanceId}-icon", scaleMode = ScaleMode.ScaleToFit, sprite = ResolveIcon(item.InstanceId) };
            icon.AddToClassList("be-slot");
            icon.AddToClassList("be-part--panel-inset");
            if (icon.sprite == null) icon.AddToClassList("be-slot--empty");
            icon.style.width = icon.style.height = 32;
            icon.style.flexShrink = 0;
            row.Add(icon);

            var label = new Label($"{TextKeyResolver.Resolve(item.Name, Localizer)} x{item.Quantity}") { name = $"doll-backpack-row-{item.InstanceId}-label" };
            label.AddToClassList("be-label");
            label.style.flexGrow = 1;
            label.style.marginLeft = 4;
            Truncate(label);
            if (usePlaceholderStyle) label.style.color = Color.white;
            row.Add(label);
            tooltip.Attach(label, () => label.text);

            if (item.RequiredSlot.IsValid)
            {
                var equipButton = new Button(() => OnEquipClicked(item)) { name = $"doll-backpack-row-{item.InstanceId}-equip", text = "Equip" };
                equipButton.AddToClassList("be-button");
                equipButton.AddToClassList("be-part--button-secondary");
                equipButton.style.height = 28;
                row.Add(equipButton);
            }

            return row;
        }

        private Sprite ResolveIcon(RuntimeEntityId instanceId)
        {
            if (!inventory.TryGetItem(instanceId, out var item)) return null;
            iconPaintBuffer.Clear();
            if (!SpriteEquipmentPaints.TryResolveSpritePaints(rulesDatabase, item.DefinitionId, iconPaintBuffer, out _)) return null;
            return iconPaintBuffer.Count > 0 ? iconPaintBuffer[0].Option.Sprite : null;
        }

        // ---------------------------------------------------------------------------------------------
        // Commands
        // ---------------------------------------------------------------------------------------------

        private void OnCycleNext()
        {
            presenter.CycleNext();
            RetargetBinders();
            Refresh();
        }

        private void OnCyclePrevious()
        {
            presenter.CyclePrevious();
            RetargetBinders();
            Refresh();
        }

        private void OnEquipClicked(InventoryItemSummary item) => ShowResult(presenter.Equip(item.InstanceId, item.RequiredSlot));

        private void OnUnequipSlotClicked(SlotRow row)
        {
            if (row.UnequipButton.userData is RuntimeEntityId instanceId) ShowResult(presenter.Unequip(instanceId));
        }

        // Refreshes here even though a successful equip/unequip's ItemEquipped/ItemUnequipped event (published
        // synchronously by EquipmentService, before Equip()/Unequip() even returns) already triggered the exact
        // same refresh once via HandleInventoryEvent, nested inside that publish -- matching InventoryView.
        // ShowResult's identical double-refresh-through-both-paths precedent (its own Refresh() call here, plus
        // its own event-bus subscription). This costs one redundant rebuild per action but keeps equip/unequip
        // correct even with no event bus wired at all.
        //
        // Real finding from building this, worth recording here even though it turned out unrelated to the
        // double refresh: a PlayMode test clicking a backpack row's Equip button via a synthetic
        // PointerDownEvent/PointerUpEvent pair (UiClick.Pointer) never reached this handler, for that button
        // specifically -- one created by RefreshSlotsAndBackpack's own Clear()+Add() rebuild mid-session, rather
        // than present since the doll's first frame. panel.Pick() at the button's own worldBound centre resolved
        // to an ANCESTOR container instead of the button, in a headless batch PlayMode run only; every button
        // present since the doll's first frame (Close, each anatomical slot's own Unequip) picked correctly every
        // time, and the same failure reproduced identically with only a single refresh, ruling out the double
        // refresh as the cause. The fix lives in the test, which uses the NavigationSubmit path for that one
        // click instead (UiClick.Submit, matching UiClick.cs's own documented floor for exactly this class of
        // case), sidestepping Pick() entirely.
        private void ShowResult(InventoryOperationResult result)
        {
            statusLabel.text = result.Success ? string.Empty : DescribeFailure(result.Outcome);
            RefreshEquipmentAndPreview();
        }

        private void RefreshEquipmentAndPreview()
        {
            equipmentBinder.Refresh();
            preview.Refresh();
            RefreshSlotsAndBackpack();
        }

        private static string DescribeFailure(InventoryOperationOutcome outcome) => outcome switch
        {
            InventoryOperationOutcome.ItemNotFound => "That item isn't available.",
            InventoryOperationOutcome.NotEquippable => "That item can't be equipped.",
            InventoryOperationOutcome.WrongSlot => "That item doesn't fit that slot.",
            InventoryOperationOutcome.ItemEquipped => "Unequip that item first.",
            InventoryOperationOutcome.NotSupportedYet => "Not supported yet.",
            _ => "That didn't work.",
        };
    }
}

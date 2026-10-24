using System;
using CRPG.Core;
using CRPG.Maps;
using UnityEngine;
using UnityEngine.UIElements;

namespace CRPG.UI
{
    /// <summary>
    /// The location map HUD: a party-following minimap built into a host-supplied region, and a full-screen area
    /// map overlay added to the host's existing HUD document. Creates no UIDocument of its own and reads no
    /// keyboard -- the game host decides which key opens or closes the area map and calls
    /// <see cref="OpenAreaMap"/> / <see cref="CloseAreaMap"/>.
    /// </summary>
    /// <remarks>
    /// While the area map is open it holds a <see cref="UIToolkitPointerCapture"/> modal scope, plus the optional
    /// host-supplied <see cref="SuspendWorldInput"/> lease; both are released exactly once on close, disable,
    /// re-initialize and destroy. Only the area map's own drag and wheel are handled here; the simulation is never
    /// paused (no time-scale write, no motor change). Re-<see cref="Initialize"/>-ing tears the previous build down
    /// first, so a rebound HUD never duplicates overlays or callbacks.
    /// </remarks>
    public sealed class LocationMapHud : MonoBehaviour
    {
        private MapViewStyle style = MapViewStyle.Default;
        private LocationMapPresenter presenter;
        private MapView minimapView;
        private MapView areaView;
        private VisualElement minimapRegion;
        private VisualElement documentRoot;
        private VisualElement minimapPanel;
        private VisualElement areaOverlay;
        private Label unavailableLabel;
        private bool areaOpen;
        private IDisposable modalLease;
        private IDisposable inputLease;
        private bool dragging;
        private int dragPointerId = -1;
        private Vector2 dragLast;
        private int pressButton = -1, pressPointerId = -1;
        private Vector2 pressStart;
        private bool pressMoved;
        private string pressKey;
        private Texture fogTexture;
        private Color fogTint = Color.white;

        /// <summary>Optional localizer for the location title.</summary>
        public ITextLocalizer Localizer { get; set; }

        /// <summary>Host hook: asked before the area map opens; returning false (a dialogue or shop is open)
        /// refuses the request.</summary>
        public Func<bool> CanOpenAreaMap { get; set; }

        /// <summary>Host hook: called when the area map opens to suspend world input; the returned lease is
        /// disposed when it closes. Null means the host has no world input to suspend.</summary>
        public Func<IDisposable> SuspendWorldInput { get; set; }

        public bool IsInitialized => presenter != null;
        public bool IsAreaMapOpen => areaOpen;
        public LocationMapPresenter Presenter => presenter;
        public MapView MinimapView => minimapView;
        public MapView AreaView => areaView;
        public VisualElement AreaOverlay => areaOverlay;
        public VisualElement MinimapPanel => minimapPanel;
        public bool IsDragging => dragging;

        /// <summary>A click (not a drag) on the area map: the world position, the button (0 left, 1 right) and the landmark or pin under the pointer. The host decides what it means; nothing here moves anything in the world.</summary>
        public event Action<MapClick> AreaClicked;

        /// <summary>Puts a texture (a fog-of-war mask) over the map image and under the markers, in both views, covering the image's own world rectangle. Borrowed, never destroyed here. Survives a re-initialisation.</summary>
        public void SetOverlay(Texture overlay, Color? tint = null)
        {
            fogTexture = overlay; fogTint = tint ?? Color.white;
            ApplyOverlay();
        }

        public void ClearOverlay() => SetOverlay(null);

        private void ApplyOverlay()
        {
            minimapView?.SetOverlay(fogTexture, fogTint);
            areaView?.SetOverlay(fogTexture, fogTint);
        }

        /// <summary>Builds the minimap into <paramref name="minimapRegion"/> (already anchored by the host, e.g.
        /// via HudRoot.CreateRegion) and the area-map overlay under <paramref name="hudDocumentRoot"/>. An invalid
        /// definition throws before anything is built.</summary>
        public void Initialize(LocationMapDefinition definition, IMapMarkerSource source, VisualElement minimapRegion,
            VisualElement hudDocumentRoot, MapViewStyle mapStyle = null)
        {
            if (minimapRegion == null) throw new ArgumentNullException(nameof(minimapRegion));
            if (hudDocumentRoot == null) throw new ArgumentNullException(nameof(hudDocumentRoot));

            var newPresenter = new LocationMapPresenter(definition, source); // validates before we touch any UI

            Teardown();

            style = mapStyle ?? MapViewStyle.Default;
            presenter = newPresenter;
            this.minimapRegion = minimapRegion;
            documentRoot = hudDocumentRoot;

            // The full-screen overlay stretches to this root, and a UIDocument root with no in-flow content has
            // no height of its own -- so make sure it fills the panel. Harmless for a root that already does.
            documentRoot.style.flexGrow = 1;

            BuildMinimap();
            BuildAreaOverlay();
            ApplyOverlay();

            documentRoot.RegisterCallback<GeometryChangedEvent>(OnDocumentGeometryChanged);
            ApplyMinimapSize(documentRoot.layout.size);

            presenter.Sampled += RefreshViews;
            presenter.SampleNow();
        }

        private void Update()
        {
            presenter?.Tick(Time.unscaledTime);
        }

        private void OnDisable() => CloseAreaMap();

        private void OnDestroy() => Teardown();

        // ----- area map open / close ---------------------------------------------------------------------

        /// <summary>Opens the area map, fitted to the whole location. Returns false if there is nothing to open,
        /// it is already open, or the host's <see cref="CanOpenAreaMap"/> refuses.</summary>
        public bool OpenAreaMap()
        {
            if (presenter == null || areaOpen) return false;
            if (CanOpenAreaMap != null && !CanOpenAreaMap()) return false;

            areaOpen = true;
            modalLease = UIToolkitPointerCapture.BeginModal();   // pointer ownership first...
            inputLease = SuspendWorldInput?.Invoke();            // ...then suspend the world's actions

            presenter.FitArea();
            areaOverlay.style.display = DisplayStyle.Flex;
            minimapPanel.style.display = DisplayStyle.None;   // the overlay replaces it while open
            presenter.SampleNow();
            return true;
        }

        /// <summary>Closes the area map: hides it, clears any drag, and releases both leases (each exactly once).
        /// Safe to call when already closed; never cancels anything in the world.</summary>
        public void CloseAreaMap()
        {
            if (!areaOpen) return;
            areaOpen = false;

            EndDrag();
            ClearPress();
            if (areaOverlay != null) areaOverlay.style.display = DisplayStyle.None;
            if (minimapPanel != null) minimapPanel.style.display = DisplayStyle.Flex;

            var modal = modalLease;
            var input = inputLease;
            modalLease = null;
            inputLease = null;
            modal?.Dispose();   // pointer ownership released immediately...
            input?.Dispose();   // ...world input resumes on the adapter's own deferred schedule
        }

        public void ToggleAreaMap()
        {
            if (areaOpen) CloseAreaMap();
            else OpenAreaMap();
        }

        /// <summary>Greys out and disables the minimap's own buttons and wheel while a modal owns the screen.</summary>
        public void SetMinimapInteractable(bool interactable) => minimapPanel?.SetEnabled(interactable);

        // ----- view commands -----------------------------------------------------------------------------

        public void ZoomMinimap(float factor)
        {
            if (presenter == null) return;
            presenter.Minimap.ZoomAtCenter(factor);
            minimapView.Refresh(presenter.Markers);
        }

        public void ZoomArea(float factor)
        {
            if (presenter == null) return;
            presenter.Area.ZoomAtCenter(factor);
            areaView.Refresh(presenter.Markers);
        }

        public void ZoomAreaAt(Vector2 viewportLocal, float factor)
        {
            if (presenter == null) return;
            presenter.Area.ZoomAt(viewportLocal, factor);
            areaView.Refresh(presenter.Markers);
        }

        public void PanArea(Vector2 viewportDelta)
        {
            if (presenter == null) return;
            presenter.Area.PanByViewportDelta(viewportDelta);
            areaView.Refresh(presenter.Markers);
        }

        public void FitArea()
        {
            if (presenter == null) return;
            presenter.FitArea();
            areaView.Refresh(presenter.Markers);
        }

        public void RecenterArea()
        {
            if (presenter == null) return;
            presenter.RecenterAreaOnActive();
            areaView.Refresh(presenter.Markers);
        }

        // ----- construction ------------------------------------------------------------------------------

        private string Title() => TextKeyResolver.Resolve(new TextKey(presenter.Definition.TitleKey, presenter.Definition.Title), Localizer);

        private void BuildMinimap()
        {
            minimapPanel = new VisualElement { name = "minimap-panel" };
            minimapPanel.AddToClassList("minimap-panel");
            minimapPanel.style.backgroundColor = style.PanelBackground;
            SetPadding(minimapPanel, style.MinimapPadding);

            var header = new VisualElement { name = "minimap-header" };
            header.style.flexDirection = FlexDirection.Row;
            header.style.justifyContent = Justify.SpaceBetween;
            header.style.marginBottom = 2;
            header.Add(new Label(Title())
            {
                name = "minimap-title",
                style = { color = style.TextColor, fontSize = 12, flexShrink = 1, overflow = Overflow.Hidden, whiteSpace = WhiteSpace.NoWrap },
            });
            header.Add(new Label("N")
            {
                name = "minimap-north",
                style = { color = style.TextColor, fontSize = 12, unityFontStyleAndWeight = FontStyle.Bold, marginLeft = 6 },
            });
            minimapPanel.Add(header);

            minimapView = new MapView(presenter.Minimap, presenter.Definition, style, hoverLabels: false);
            minimapView.Root.style.width = style.MinimapSize;
            minimapView.Root.style.height = style.MinimapSize;
            minimapView.Root.RegisterCallback<WheelEvent>(OnMinimapWheel);

            unavailableLabel = new Label("Position unavailable") { name = "minimap-unavailable", pickingMode = PickingMode.Ignore };
            unavailableLabel.style.position = Position.Absolute;
            unavailableLabel.style.left = 4;
            unavailableLabel.style.bottom = 4;
            unavailableLabel.style.fontSize = 11;
            unavailableLabel.style.color = style.TextColor;
            unavailableLabel.style.backgroundColor = style.HoverLabelBackground;
            unavailableLabel.style.display = DisplayStyle.None;
            minimapView.Root.Add(unavailableLabel);
            minimapPanel.Add(minimapView.Root);

            var footer = new VisualElement { name = "minimap-footer" };
            footer.style.flexDirection = FlexDirection.Row;
            footer.style.marginTop = 2;
            footer.Add(MakeButton("minimap-zoom-out", "-", () => ZoomMinimap(1f / style.ButtonZoomFactor)));
            footer.Add(MakeButton("minimap-zoom-in", "+", () => ZoomMinimap(style.ButtonZoomFactor)));
            var open = MakeButton("minimap-open-map", "Open Map", () => OpenAreaMap());
            open.style.flexGrow = 1;
            footer.Add(open);
            minimapPanel.Add(footer);

            minimapRegion.Add(minimapPanel);
        }

        private void BuildAreaOverlay()
        {
            areaOverlay = new VisualElement { name = "area-map-overlay" };
            areaOverlay.AddToClassList("area-map-overlay");
            areaOverlay.pickingMode = PickingMode.Position;
            areaOverlay.style.position = Position.Absolute;
            areaOverlay.style.left = 0;
            areaOverlay.style.top = 0;
            areaOverlay.style.right = 0;
            areaOverlay.style.bottom = 0;
            areaOverlay.style.backgroundColor = new Color(0f, 0f, 0f, 0.75f);
            areaOverlay.style.display = DisplayStyle.None;
            areaOverlay.style.flexDirection = FlexDirection.Column;
            SetPadding(areaOverlay, 32f);

            var header = new VisualElement { name = "area-map-header" };
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.marginBottom = 8;
            header.Add(new Label(Title())
            {
                name = "area-map-title",
                style = { color = style.TextColor, fontSize = 24, flexGrow = 1, unityFontStyleAndWeight = FontStyle.Bold },
            });
            header.Add(MakeButton("area-zoom-out", "-", () => ZoomArea(1f / style.ButtonZoomFactor)));
            header.Add(MakeButton("area-zoom-in", "+", () => ZoomArea(style.ButtonZoomFactor)));
            header.Add(MakeButton("area-fit", "Fit Location", FitArea));
            header.Add(MakeButton("area-recenter", "Recenter", RecenterArea));
            header.Add(MakeButton("area-close", "Close", CloseAreaMap));
            areaOverlay.Add(header);

            areaView = new MapView(presenter.Area, presenter.Definition, style, hoverLabels: true);
            areaView.Root.style.flexGrow = 1;
            areaView.Root.RegisterCallback<PointerDownEvent>(OnAreaPointerDown);
            areaView.Root.RegisterCallback<PointerMoveEvent>(OnAreaPointerMove);
            areaView.Root.RegisterCallback<PointerUpEvent>(OnAreaPointerUp);
            areaView.Root.RegisterCallback<PointerCaptureOutEvent>(OnAreaPointerCaptureOut);
            areaView.Root.RegisterCallback<WheelEvent>(OnAreaWheel);
            areaOverlay.Add(areaView.Root);

            areaOverlay.Add(BuildLegend());
            documentRoot.Add(areaOverlay);
        }

        private VisualElement BuildLegend()
        {
            var legend = new VisualElement { name = "area-map-legend" };
            legend.style.flexDirection = FlexDirection.Row;
            legend.style.marginTop = 8;

            legend.Add(LegendEntry("Active", MapMarkerSnapshot.ForParty(RuntimeEntityId.None, null, Vector2.zero, MapPartyMarkerState.Active)));
            legend.Add(LegendEntry("Selected", MapMarkerSnapshot.ForParty(RuntimeEntityId.None, null, Vector2.zero, MapPartyMarkerState.Selected)));
            legend.Add(LegendEntry("Party", MapMarkerSnapshot.ForParty(RuntimeEntityId.None, null, Vector2.zero, MapPartyMarkerState.Unselected)));
            legend.Add(LegendEntry("Landmark", MapMarkerSnapshot.ForLandmark(null, "legend", null, Vector2.zero)));
            return legend;
        }

        private VisualElement LegendEntry(string text, MapMarkerSnapshot sample)
        {
            const float Box = 20f;
            var row = new VisualElement { name = "legend-" + text.ToLowerInvariant() };
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginRight = 16;

            var holder = new VisualElement { pickingMode = PickingMode.Ignore };
            holder.style.width = Box;
            holder.style.height = Box;
            var glyph = new VisualElement { pickingMode = PickingMode.Ignore };
            var size = MapMarkerGlyphs.Apply(glyph, sample, style);
            glyph.style.left = (Box - size) * 0.5f;
            glyph.style.top = (Box - size) * 0.5f;
            holder.Add(glyph);
            row.Add(holder);
            row.Add(new Label(text) { style = { color = style.TextColor, fontSize = 12, marginLeft = 4 } });
            return row;
        }

        private Button MakeButton(string buttonName, string text, Action onClick)
        {
            // Explicit look so the controls read on any panel, with or without a theme stylesheet.
            var button = new Button(onClick) { name = buttonName, text = text };
            button.style.marginLeft = 4;
            button.style.color = style.TextColor;
            button.style.backgroundColor = style.ButtonBackground;
            button.style.paddingLeft = 8;
            button.style.paddingRight = 8;
            button.style.paddingTop = 2;
            button.style.paddingBottom = 2;
            button.style.fontSize = 12;
            button.style.borderTopWidth = 1;
            button.style.borderRightWidth = 1;
            button.style.borderBottomWidth = 1;
            button.style.borderLeftWidth = 1;
            button.style.borderTopColor = style.ButtonBorder;
            button.style.borderRightColor = style.ButtonBorder;
            button.style.borderBottomColor = style.ButtonBorder;
            button.style.borderLeftColor = style.ButtonBorder;
            return button;
        }

        private static void SetPadding(VisualElement element, float padding)
        {
            element.style.paddingLeft = padding;
            element.style.paddingRight = padding;
            element.style.paddingTop = padding;
            element.style.paddingBottom = padding;
        }

        // ----- layout ------------------------------------------------------------------------------------

        private void OnDocumentGeometryChanged(GeometryChangedEvent evt) => ApplyMinimapSize(evt.newRect.size);

        /// <summary>Square minimap at its nominal size, shrunk on a small viewport rather than overlapping the
        /// rest of the HUD.</summary>
        private void ApplyMinimapSize(Vector2 documentSize)
        {
            if (minimapView == null) return;

            var size = style.MinimapSize;
            if (documentSize.x > 0f && documentSize.y > 0f)
            {
                var allowed = Mathf.Min(documentSize.x, documentSize.y) * style.MinimapMaxViewportFraction;
                size = Mathf.Clamp(allowed, style.MinimapMinimumSize, style.MinimapSize);
            }

            minimapView.Root.style.width = size;
            minimapView.Root.style.height = size;
            minimapRegion.style.width = size + style.MinimapPadding * 2f;
        }

        private void RefreshViews()
        {
            if (presenter == null) return;
            minimapView.Refresh(presenter.Markers);
            if (areaOpen) areaView.Refresh(presenter.Markers);
            if (unavailableLabel != null)
                unavailableLabel.style.display = presenter.FocusAvailable ? DisplayStyle.None : DisplayStyle.Flex;
        }

        // ----- pointer handling --------------------------------------------------------------------------

        private void OnMinimapWheel(WheelEvent evt)
        {
            ZoomMinimap(evt.delta.y < 0f ? style.WheelZoomFactor : 1f / style.WheelZoomFactor);
            evt.StopPropagation();
        }

        private void OnAreaWheel(WheelEvent evt)
        {
            var local = areaView.Root.WorldToLocal(evt.mousePosition);
            ZoomAreaAt(local, evt.delta.y < 0f ? style.WheelZoomFactor : 1f / style.WheelZoomFactor);
            evt.StopPropagation();
        }

        private void OnAreaPointerDown(PointerDownEvent evt)
        {
            if ((evt.button != 0 && evt.button != 1) || dragging || pressButton >= 0) return;

            var local = areaView.Root.WorldToLocal(evt.position);
            pressButton = evt.button; pressPointerId = evt.pointerId; pressStart = local; pressMoved = false; pressKey = MarkerKeyUnder(evt.target as VisualElement);
            if (evt.button == 0)
            {
                dragging = true;
                dragPointerId = evt.pointerId;
                dragLast = local;
            }

            areaView.Root.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        }

        private void OnAreaPointerMove(PointerMoveEvent evt)
        {
            if (pressButton < 0 || evt.pointerId != pressPointerId) return;

            var local = areaView.Root.WorldToLocal(evt.position);
            if (!pressMoved && (local - pressStart).magnitude > style.ClickDragThreshold) pressMoved = true;
            if (dragging)
            {
                PanArea(local - dragLast);
                dragLast = local;
            }

            evt.StopPropagation();
        }

        private void OnAreaPointerUp(PointerUpEvent evt)
        {
            if (pressButton < 0 || evt.pointerId != pressPointerId || evt.button != pressButton) return;

            var button = pressButton; var key = pressKey; var moved = pressMoved; var id = pressPointerId;
            ClearPress();
            EndDrag();
            if (areaView.Root.HasPointerCapture(id)) areaView.Root.ReleasePointer(id);
            if (!moved)
            {
                var local = areaView.Root.WorldToLocal(evt.position);
                AreaClicked?.Invoke(new MapClick(areaView.State.Projection.ViewportToWorld(local), local, button, key));
            }

            evt.StopPropagation();
        }

        // Capture can be lost without a pointer-up (window focus, panel change): the press and the drag must end too.
        private void OnAreaPointerCaptureOut(PointerCaptureOutEvent evt) { ClearPress(); ClearDragState(); }

        private void ClearPress() { pressButton = -1; pressPointerId = -1; pressMoved = false; pressKey = null; }

        /// <summary>The key of the landmark or pin element the pointer went down on (the element or an ancestor below the view root), or null.</summary>
        private string MarkerKeyUnder(VisualElement target)
        {
            for (var e = target; e != null && e != areaView.Root; e = e.parent)
                if (e.userData is string key && (e.ClassListContains("map-marker--landmark") || e.ClassListContains("map-marker--pin"))) return key;
            return null;
        }

        private void EndDrag()
        {
            if (!dragging) return;
            var id = dragPointerId;
            ClearDragState();
            if (areaView != null && areaView.Root.HasPointerCapture(id)) areaView.Root.ReleasePointer(id);
        }

        private void ClearDragState()
        {
            dragging = false;
            dragPointerId = -1;
        }

        // ----- teardown ----------------------------------------------------------------------------------

        private void Teardown()
        {
            CloseAreaMap();

            if (presenter != null) presenter.Sampled -= RefreshViews;
            presenter = null;

            if (documentRoot != null) documentRoot.UnregisterCallback<GeometryChangedEvent>(OnDocumentGeometryChanged);

            minimapView?.Root.UnregisterCallback<WheelEvent>(OnMinimapWheel);
            if (areaView != null)
            {
                areaView.Root.UnregisterCallback<PointerDownEvent>(OnAreaPointerDown);
                areaView.Root.UnregisterCallback<PointerMoveEvent>(OnAreaPointerMove);
                areaView.Root.UnregisterCallback<PointerUpEvent>(OnAreaPointerUp);
                areaView.Root.UnregisterCallback<PointerCaptureOutEvent>(OnAreaPointerCaptureOut);
                areaView.Root.UnregisterCallback<WheelEvent>(OnAreaWheel);
            }

            minimapView?.Dispose();
            areaView?.Dispose();
            minimapPanel?.RemoveFromHierarchy();
            areaOverlay?.RemoveFromHierarchy();

            minimapView = null;
            areaView = null;
            minimapPanel = null;
            areaOverlay = null;
            unavailableLabel = null;
            minimapRegion = null;
            documentRoot = null;
        }
    }
}

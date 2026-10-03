using System.Collections.Generic;
using System.Linq;
using BarrowsEnd.Gate;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Presentation.Sprites;
using CRPG.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace BarrowsEnd.Shell
{
    /// <summary>
    /// Task 5.1: the real, re-skinned in-game HUD, replacing <see cref="ShellPanel"/>'s single debug panel. A
    /// top-left party roster (a real <see cref="PartyView"/>, flag off) and a top-right system menu (Save/Load/
    /// Zoom + a status readout), both built once <see cref="ShellBootstrap.IsReady"/> and both anchored through a
    /// <see cref="HudRoot"/> region on this same GameObject.
    /// <para>After a successful <see cref="ShellBootstrap.LoadNow"/>, a BRAND NEW <see cref="PartyPresenter"/> is
    /// built (not <c>PartyPresenter.Rebind</c> on the existing one) and handed to <see cref="PartyView.Rebind"/>:
    /// mutating the existing presenter's own `Party`/`Characters` fields in place, then handing that SAME instance
    /// to <c>PartyView.Rebind</c>, would make the view unsubscribe from whatever the presenter's `Party` property
    /// returns AT THAT MOMENT -- the already-swapped NEW state, not the real old one it was actually still
    /// listening to -- leaving the true dangling subscription <c>PartyView.Rebind</c> exists to prevent.</para>
    /// </summary>
    [RequireComponent(typeof(HudRoot))]
    public sealed class HudBootstrap : MonoBehaviour
    {
        private const float RosterWidth = 260f;
        // Two 100px buttons side by side per row (plus a 4px gap and be-panel's own padding, --be-space-3 = 16px
        // each side) need >= 236px -- a 150px region overflowed the second button in each row off the visible
        // area entirely (found live: its click landed nowhere, downFired/clickedFired both false even though the
        // element existed and was "enabled").
        private const float MenuWidth = 260f;

        [SerializeField] private ShellBootstrap bootstrap;
        [SerializeField] private DollBootstrap dollBootstrap;
        [SerializeField] private MoveDestinationMarker moveDestinationMarker;

        private HudRoot hudRoot;
        private PartyPresenter presenter;
        private PartyView partyView;
        private ResourceDefinition hpResource;
        private Label statusLabel;
        private Label messageLabel;
        private DragSelectionOverlay dragOverlay;
        private bool built;

        private void OnEnable()
        {
            hudRoot = GetComponent<HudRoot>();
            if (bootstrap != null) { bootstrap.MessageChanged += OnMessage; bootstrap.SessionRebound += OnSessionRebound; }
        }

        private void OnDisable()
        {
            if (bootstrap != null) { bootstrap.MessageChanged -= OnMessage; bootstrap.SessionRebound -= OnSessionRebound; }
        }

        private void OnDestroy() => dragOverlay?.Dispose();

        private void Update()
        {
            if (built || bootstrap == null || !bootstrap.IsReady) return;
            Build();
            built = true;
        }

        // The party cards only redraw on selection/membership changes, so damage and healing never reached them.
        // A cheap per-frame signature of every member's hit points redraws them the moment any number moves.
        private string partyHpSignature = "";

        private void RefreshPartyCardsWhenHitPointsChange()
        {
            if (partyView == null || presenter == null) return;
            var signature = string.Join("|", presenter.GetMembers().Select(m => $"{m.Id}:{m.CurrentHp}/{m.MaxHp}"));
            if (signature == partyHpSignature) return;

            partyHpSignature = signature;
            partyView.RefreshNow();
        }

        private void LateUpdate()
        {
            if (built) RefreshPartyCardsWhenHitPointsChange();
            if (!built || statusLabel == null) return;
            var session = bootstrap.Services?.Session;
            statusLabel.text = session != null && session.Characters.TryGetCharacter(session.Party.ActiveCharacter, out var member)
                ? ShellPanel.Summarize(member, session.RulesDatabase)
                : "";
        }

        private void Build()
        {
            var session = bootstrap.Services.Session;
            hpResource = ResolveHitPointsResource(session.RulesDatabase);

            var rosterRegion = hudRoot.CreateRegion(HudCorner.TopLeft, RosterWidth);
            presenter = BuildPresenter(session);
            var partyViewGo = new GameObject("hud-party-view");
            partyViewGo.transform.SetParent(transform, false);
            partyView = partyViewGo.AddComponent<PartyView>();
            partyView.UsePlaceholderStyle = false;
            partyView.Initialize(presenter, rosterRegion);

            var menuRegion = hudRoot.CreateRegion(HudCorner.TopRight, MenuWidth);
            BuildSystemMenu(menuRegion);

            // 2D-Campaign-ExplorationUX-Followups.md D2/D3/D7: the drag-select overlay attaches directly to
            // the HUD's own document root (never through CreateRegion, which would swallow every world click --
            // see DragSelectionOverlay's own class doc comment), wired here in runtime code since CRPG.UI
            // cannot serialize/reference bootstrap.InputSource's own type directly (B1/B2).
            dragOverlay = new DragSelectionOverlay(hudRoot.DocumentRoot, bootstrap.InputSource);

            if (moveDestinationMarker != null)
            {
                moveDestinationMarker.Initialize(bootstrap.InputSource);
                moveDestinationMarker.IsWorldTransitioning = () => bootstrap.Gate.IsBusy;
            }
        }

        /// <summary>Throws with a clear message rather than leaving the HUD silently blank if the shipped ruleset
        /// ever stops defining hit points -- the resolution mechanism itself (RulesDatabase.TryResolve against
        /// GateSessionServices.HitPoints) is already proven by GearAndRulesetContentTests.</summary>
        private static ResourceDefinition ResolveHitPointsResource(IRulesDatabase rules)
        {
            if (!rules.TryResolve<ResourceDefinition>(GateSessionServices.HitPoints, out var resource))
                throw new System.InvalidOperationException($"The ruleset has no ResourceDefinition for '{GateSessionServices.HitPoints}': the HUD's HP bars have nothing to read.");
            return resource;
        }

        private PartyPresenter BuildPresenter(CRPG.Gameplay.GameSession session)
        {
            // Task 6.1 (T6.1-D9): ShellBootstrap.DisplayNameFor covers both boot members and the 3 fixed
            // companion ids -- a companion isn't a boot-time member any more (they join through
            // ShellBootstrap.RecruitCompanion), so seeding this dictionary from bootstrap.Members alone would
            // leave a recruited companion showing as a raw GUID. A not-yet-recruited companion's entry costs
            // nothing (PartyPresenter only ever looks a name up for an id actually in session.Party.Members).
            var names = new Dictionary<RuntimeEntityId, string>();
            for (var i = 0; i < bootstrap.Members.Count; i++) names[ShellBootstrap.MemberIdFor(i)] = bootstrap.DisplayNameFor(ShellBootstrap.MemberIdFor(i));
            foreach (var key in CompanionRoster.Keys) names[CompanionRoster.IdFor(key)] = bootstrap.DisplayNameFor(CompanionRoster.IdFor(key));
            return new PartyPresenter(session.Party, session.Characters, hpResource, names);
        }

        private void BuildSystemMenu(VisualElement region)
        {
            var panel = new VisualElement { name = "hud-system-menu" };
            panel.AddToClassList("be-panel");
            panel.AddToClassList("be-part--panel-stone");
            region.Add(panel);

            var row1 = MenuRow(panel);
            row1.Add(MenuButton("save-button", "Save", () => bootstrap.SaveNow()));
            row1.Add(MenuButton("load-button", "Load", OnLoadClicked));

            var row2 = MenuRow(panel);
            row2.Add(MenuButton("zoom-out-button", "Zoom -", () => bootstrap.CameraRig.Rig.ApplyZoom(-1f)));
            row2.Add(MenuButton("zoom-in-button", "Zoom +", () => bootstrap.CameraRig.Rig.ApplyZoom(1f)));

            if (dollBootstrap != null)
            {
                var row3 = MenuRow(panel);
                var dollButton = MenuButton("doll-button", "Inventory", () => dollBootstrap.ToggleOpen());
                dollButton.style.width = 208; // full row width (two 100px buttons + the 4px inter-button margin + 4px slack)
                dollButton.style.marginRight = 0;
                row3.Add(dollButton);
            }

            statusLabel = new Label { name = "hud-status" };
            statusLabel.AddToClassList("be-label");
            statusLabel.AddToClassList("be-label--muted");
            statusLabel.style.whiteSpace = WhiteSpace.Normal;
            panel.Add(statusLabel);

            messageLabel = new Label { name = "hud-message" };
            messageLabel.AddToClassList("be-label");
            messageLabel.AddToClassList("be-label--muted");
            messageLabel.style.whiteSpace = WhiteSpace.Normal;
            panel.Add(messageLabel);
        }

        private void OnLoadClicked() => bootstrap.LoadNow();

        // Every kind of load (this menu's Load, a lost fight's checkpoint reload, Continue) swaps in a brand-new session. The party panel
        // must rebind to it whichever way it came about -- otherwise it keeps showing the old session's members and hit points.
        private void OnSessionRebound()
        {
            if (!built) return;

            // A brand-new PartyPresenter, not a mutated existing one -- see the class doc comment for why.
            var newPresenter = BuildPresenter(bootstrap.Services.Session);
            partyView.Rebind(newPresenter);
            presenter = newPresenter;

            dollBootstrap?.OnLoadCompleted();
        }

        private static VisualElement MenuRow(VisualElement parent)
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 6 } };
            parent.Add(row);
            return row;
        }

        private static Button MenuButton(string name, string text, System.Action onClick)
        {
            var button = new Button(onClick) { name = name, text = text };
            button.AddToClassList("be-button");
            button.AddToClassList("be-part--button-secondary");
            button.style.width = 100;
            button.style.height = 36;
            button.style.marginRight = 4;
            return button;
        }

        private void OnMessage(string text)
        {
            if (messageLabel != null) messageLabel.text = text;
        }
    }
}

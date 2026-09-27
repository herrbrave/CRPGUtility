using BarrowsEnd.Gate;
using CRPG.Actors;
using CRPG.Core;
using CRPG.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace BarrowsEnd.Shell
{
    /// <summary>
    /// Task 5.2: the paper-doll screen's composition root. Owns its own <see cref="UIDocument"/> (the gallery
    /// panel settings from Task 5.0 -- sortingOrder 10, already carrying <c>BarrowsEndTheme.tss</c> -- reused
    /// rather than authoring a third panel-settings asset: the creator's own panel settings carry the DEFAULT
    /// theme, not the skin, so reusing THAT would render the doll unskinned) and a <see cref="DollPresenter"/>,
    /// built once <see cref="ShellBootstrap.IsReady"/> and re-bound after a successful load.
    /// <para>Unlike <see cref="HudBootstrap"/>'s <see cref="PartyPresenter"/> (which is rebuilt fresh, never
    /// mutated in place, after a load -- see that class's doc comment), <see cref="DollPresenter"/> IS mutated in
    /// place via <see cref="DollPresenter.Rebind"/>: DollView never subscribes to PartyState.Changed directly (it
    /// only reads the presenter on demand and subscribes to the event bus for equip/unequip), so there is no
    /// dangling-subscription risk analogous to PartyView's.</para>
    /// <para>There is only one Load button (in <see cref="HudBootstrap"/>'s system menu), so this has no load
    /// hook of its own: <see cref="HudBootstrap"/> calls <see cref="OnLoadCompleted"/> from inside its own Load
    /// button's callback after rebuilding its own state.</para>
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class DollBootstrap : MonoBehaviour
    {
        [SerializeField] private ShellBootstrap bootstrap;
        [SerializeField] private DollView view;

        private DollPresenter presenter;
        private bool built;

        public bool IsOpen => built && view.IsOpen;

        private void Update()
        {
            if (built || bootstrap == null || !bootstrap.IsReady) return;
            Build();
            built = true;
        }

        /// <summary>The HUD's doll button: opens the doll (on the party's current active member) if closed, else
        /// closes it. A no-op before the session is ready.</summary>
        public void ToggleOpen()
        {
            if (!built) return;
            if (view.IsOpen) view.Close();
            else view.Open();
        }

        /// <summary>Called by HudBootstrap's own Load button callback after a successful load. Rebuilds this the
        /// first time (the session was not ready yet when Update() checked) or rebinds the existing presenter/view
        /// to the new session's state otherwise.</summary>
        public void OnLoadCompleted()
        {
            if (!built)
            {
                Build();
                built = true;
                return;
            }

            var session = bootstrap.Services.Session;
            presenter.Rebind(session.Party, session.Characters, session.RulesDatabase, session.Inventory, bootstrap.Services.Equipment);
            view.Initialize(presenter, session.Characters, session.RulesDatabase, session.Inventory, session.EventBus);
        }

        private void Build()
        {
            var session = bootstrap.Services.Session;
            var hp = ResolveHitPointsResource(session.RulesDatabase);
            var inventoryPresenter = new InventoryPresenter(session.Inventory, session.RulesDatabase, bootstrap.Services.Equipment);
            presenter = new DollPresenter(session.Party, session.Characters, session.RulesDatabase, inventoryPresenter, hp);
            view.UsePlaceholderStyle = false; // the skin pass: gallery-panel-settings.asset already carries the real theme
            view.Initialize(presenter, session.Characters, session.RulesDatabase, session.Inventory, session.EventBus);
        }

        /// <summary>Same reasoning as HudBootstrap.ResolveHitPointsResource: throws with a clear message rather
        /// than leaving the doll silently blank if the shipped ruleset ever stops defining hit points.</summary>
        private static ResourceDefinition ResolveHitPointsResource(IRulesDatabase rules)
        {
            if (!rules.TryResolve<ResourceDefinition>(GateSessionServices.HitPoints, out var resource))
                throw new System.InvalidOperationException($"The ruleset has no ResourceDefinition for '{GateSessionServices.HitPoints}': the doll's HP readout has nothing to read.");
            return resource;
        }
    }
}

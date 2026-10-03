using System.Collections.Generic;
using CRPG.Core;
using CRPG.Gameplay;
using CRPG.Navigation.Grid;
using UnityEngine;

namespace BarrowsEnd.Gate
{
    /// <summary>A barrier that opens when a Timeline key occurs (a lever pulled). Its child transforms ARE the cells it blocks
    /// and the visual bars drawn over them (the cells are painted as plain floor in the baked grid, so the bake sees the far
    /// side as reachable). State is NEVER set in <c>Start</c>: <see cref="Awake"/> closes it, and <see cref="Refresh"/> --
    /// called by the shell whenever a session is wired (boot, scene load, after a save load) -- sets it open OR closed, both
    /// ways, from the persisted key. That is what survives travel away and back, a same-scene load of an older save (the
    /// runtime grid is not rebuilt then), and a cross-scene load.</summary>
    public sealed class PortcullisGate : MonoBehaviour
    {
        [SerializeField] private string openKey = "deep-lever-pulled";

        public GameSession Session { get; private set; }
        public bool IsOpen { get; private set; }
        public string OpenKey => openKey;

        private void Awake() => Apply(false);

        public void Refresh(GameSession session)
        {
            Session = session;
            Apply(session != null && session.Timeline.HasOccurred(openKey));
        }

        private void Apply(bool open)
        {
            IsOpen = open;

            var provider = WalkabilityGridProvider.FindInScene(gameObject.scene);
            var cells = new List<Transform>();
            foreach (Transform child in transform) cells.Add(child);

            if (provider != null)
            {
                var grid = provider.Grid;
                foreach (var cell in cells) grid.SetBlocked(grid.ToGrid(cell.position), !open);
            }

            foreach (var cell in cells) cell.gameObject.SetActive(!open);
        }
    }
}

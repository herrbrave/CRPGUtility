using System.Collections.Generic;
using CRPG.Actors;
using CRPG.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace CRPG.Samples.TacticalEncounter
{
    /// <summary>A plain in-memory ICombatLog -- collects every entry (attack rolls, damage, reactions) so
    /// CombatLogPanel has something real to render. "Logs to the console" isn't enough for a live human review
    /// to actually confirm a click did something; this is the sample's own minimal visible feedback.</summary>
    public sealed class SampleCombatLog : ICombatLog
    {
        private readonly List<CombatLogEntry> entries = new();
        public IReadOnlyList<CombatLogEntry> Entries => entries;
        public event System.Action<CombatLogEntry> EntryLogged;

        public void Log(CombatLogEntry entry)
        {
            entries.Add(entry);
            EntryLogged?.Invoke(entry);
        }
    }

    /// <summary>Small procedural UIDocument panel (same rebuild-on-refresh convention as DialogueView/
    /// TurnOrderView) showing the last few combat-log lines and every tracked combatant's current HP --
    /// without this, a successful attack and a miss look identical to someone just watching, and a win/loss
    /// has no visible result at all. Also the sample's one shared "id -> friendly name" lookup
    /// (<see cref="DisplayNames"/>) -- CombatLogEntry.Summary() and TurnOrderPresenter both otherwise fall back
    /// to a RuntimeEntityId's raw 32-character string (confirmed live: unreadable, and long enough to visually
    /// wrap a narrow HUD region into a dense, overlapping-looking block of text).</summary>
    public sealed class CombatLogPanel : MonoBehaviour
    {
        private const int MaxVisibleLines = 6;

        private VisualElement hpArea;
        private VisualElement logArea;
        private Label resultLabel;
        private ICharacterRegistry characters;
        private readonly List<(RuntimeEntityId Id, string Label, DefinitionId HitPointsId)> tracked = new();
        private readonly Dictionary<RuntimeEntityId, string> displayNames = new();

        // Real combat log entries (attack rolls, damage, reactions) and sample-local status text (a rejected
        // target, a turn ending) share one chronological display -- a click that silently fails validation
        // (e.g. a target out of range) previously left zero trace anywhere, reading as "the click did nothing"
        // with no way to tell that from a genuine bug. See TacticalEncounterBootstrap's AbilityTargetingGate
        // .TargetRejected subscription.
        private readonly List<string> logLines = new();

        /// <summary>Shared with TurnOrderPresenter so both panels resolve the same ids to the same names --
        /// built here (populated by Track) since this panel already owns the "id -> label" pairing.</summary>
        public IReadOnlyDictionary<RuntimeEntityId, string> DisplayNames => displayNames;

        public void Initialize(SampleCombatLog log, ICharacterRegistry characterRegistry, VisualElement hostContainer)
        {
            characters = characterRegistry;

            hostContainer.style.flexDirection = FlexDirection.Column;
            hostContainer.style.paddingLeft = 12;
            hostContainer.style.paddingTop = 12;
            hostContainer.style.paddingRight = 12;
            hostContainer.style.paddingBottom = 12;
            hostContainer.style.backgroundColor = new Color(0f, 0f, 0f, 0.5f);

            hpArea = new VisualElement();
            hostContainer.Add(hpArea);

            resultLabel = new Label { style = { fontSize = 16, unityFontStyleAndWeight = FontStyle.Bold, color = Color.white, marginTop = 8, marginBottom = 4, display = DisplayStyle.None } };
            hostContainer.Add(resultLabel);

            logArea = new VisualElement();
            hostContainer.Add(logArea);

            log.EntryLogged += entry => AddLine(Describe(entry));
            Refresh();
        }

        /// <summary>Sample-local status text (not a real CombatLogEntry -- e.g. "Melee Strike: out of range")
        /// interleaved into the same chronological log a real attack/damage/reaction entry appears in.</summary>
        public void AddLine(string text)
        {
            logLines.Add(text);
            Refresh();
        }

        /// <summary>Refreshes immediately (not just recorded for the next event) -- Initialize() always runs
        /// before any Track() call in the composition root, so without this the HP list would stay empty until
        /// the very first combat log entry arrived.</summary>
        public void Track(RuntimeEntityId id, string label, DefinitionId hitPointsId)
        {
            tracked.Add((id, label, hitPointsId));
            displayNames[id] = label;
            Refresh();
        }

        public void ShowResult(string text)
        {
            resultLabel.text = text;
            resultLabel.style.display = DisplayStyle.Flex;
        }

        private void Refresh()
        {
            hpArea.Clear();
            foreach (var (id, label, hitPointsId) in tracked)
            {
                var text = characters.TryGetCharacter(id, out var character) && character.TryGetResource(hitPointsId, out var hp)
                    ? $"{label}: {hp.Current}/{hp.Max} HP"
                    : $"{label}: --";
                hpArea.Add(new Label(text) { style = { fontSize = 12, color = Color.white } });
            }

            logArea.Clear();
            var start = Mathf.Max(0, logLines.Count - MaxVisibleLines);
            for (var i = start; i < logLines.Count; i++)
                logArea.Add(new Label(logLines[i]) { style = { fontSize = 11, color = Color.white, whiteSpace = WhiteSpace.Normal, marginBottom = 2 } });
        }

        /// <summary>CombatLogEntry.Summary() interpolates Source/Target directly (a raw RuntimeEntityId's own
        /// ToString() -- a 32-character hex string), since the generic engine log has no concept of a
        /// "friendly name." Substituting every tracked id's own string form for its label works for any
        /// concrete entry type without needing to special-case AttackRollLogEntry/DamageLogEntry/etc. here.</summary>
        private string Describe(CombatLogEntry entry)
        {
            var text = entry.Summary();
            foreach (var (id, label) in displayNames)
            {
                var raw = id.ToString();
                if (!string.IsNullOrEmpty(raw)) text = text.Replace(raw, label);
            }

            return text;
        }
    }
}

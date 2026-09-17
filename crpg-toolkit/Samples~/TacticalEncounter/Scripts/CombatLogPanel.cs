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
    /// has no visible result at all.</summary>
    public sealed class CombatLogPanel : MonoBehaviour
    {
        private const int MaxVisibleLines = 6;

        private VisualElement hpArea;
        private VisualElement logArea;
        private Label resultLabel;
        private SampleCombatLog combatLog;
        private ICharacterRegistry characters;
        private readonly List<(RuntimeEntityId Id, string Label, DefinitionId HitPointsId)> tracked = new();

        public void Initialize(SampleCombatLog log, ICharacterRegistry characterRegistry, VisualElement hostContainer)
        {
            combatLog = log;
            characters = characterRegistry;

            hostContainer.style.flexDirection = FlexDirection.Column;

            hpArea = new VisualElement();
            hostContainer.Add(hpArea);

            resultLabel = new Label { style = { fontSize = 16, unityFontStyleAndWeight = FontStyle.Bold, marginTop = 8, marginBottom = 4, display = DisplayStyle.None } };
            hostContainer.Add(resultLabel);

            logArea = new VisualElement();
            hostContainer.Add(logArea);

            combatLog.EntryLogged += _ => Refresh();
            Refresh();
        }

        public void Track(RuntimeEntityId id, string label, DefinitionId hitPointsId) => tracked.Add((id, label, hitPointsId));

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
                hpArea.Add(new Label(text) { style = { fontSize = 12 } });
            }

            logArea.Clear();
            var start = Mathf.Max(0, combatLog.Entries.Count - MaxVisibleLines);
            for (var i = start; i < combatLog.Entries.Count; i++)
                logArea.Add(new Label(combatLog.Entries[i].Summary()) { style = { fontSize = 11, whiteSpace = WhiteSpace.Normal } });
        }
    }
}

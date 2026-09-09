using System;
using System.Collections.Generic;

namespace CRPG.Core
{
    public sealed class CombatLog : ICombatLog
    {
        private readonly List<CombatLogEntry> entries = new();

        public IReadOnlyList<CombatLogEntry> Entries => entries;
        public event Action<CombatLogEntry> EntryLogged;

        public void Log(CombatLogEntry entry)
        {
            entries.Add(entry);
            EntryLogged?.Invoke(entry);
        }
    }
}

using System;
using System.Collections.Generic;

namespace CRPG.Core
{
    public interface ICombatLog
    {
        IReadOnlyList<CombatLogEntry> Entries { get; }
        event Action<CombatLogEntry> EntryLogged;
        void Log(CombatLogEntry entry);
    }
}

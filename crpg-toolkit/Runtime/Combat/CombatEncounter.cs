using System;
using System.Collections.Generic;
using System.Linq;
using CRPG.Core;

namespace CRPG.Combat
{
    /// <summary>
    /// Turn order and round tracking for one fight. Combatants are added with their already-rolled initiative
    /// (see <see cref="IInitiativeRule"/>) and sorted highest-first when the encounter starts.
    /// </summary>
    public sealed class CombatEncounter
    {
        private readonly List<Combatant> combatants = new();
        private int currentIndex = -1;

        public IReadOnlyList<Combatant> Combatants => combatants;
        public int Round { get; private set; } = 1;
        public bool HasStarted { get; private set; }
        public Combatant CurrentTurn => currentIndex >= 0 && currentIndex < combatants.Count ? combatants[currentIndex] : null;

        public void AddCombatant(RuntimeEntityId id, int initiative, DefinitionId faction = default) =>
            combatants.Add(new Combatant(id, initiative, faction));

        public void Start()
        {
            combatants.Sort((a, b) => b.Initiative.CompareTo(a.Initiative));
            currentIndex = 0;
            Round = 1;
            HasStarted = true;
        }

        public Combatant AdvanceTurn()
        {
            if (!HasStarted) throw new InvalidOperationException("Combat has not started.");
            if (combatants.Count == 0) return null;

            currentIndex++;
            if (currentIndex >= combatants.Count)
            {
                currentIndex = 0;
                Round++;
            }

            return CurrentTurn;
        }

        /// <summary>Drops a combatant from turn order (fled, died, ...). If it was the current or an earlier
        /// combatant, the turn pointer is adjusted so the encounter keeps progressing correctly; if it *was*
        /// the current combatant, whoever now occupies that slot becomes current without an explicit
        /// AdvanceTurn call. Whether "dropped to zero HP" means removed at all is a ruleset decision (a 5e
        /// PC goes unconscious, not removed; a monster might die outright) -- callers decide when to call this.</summary>
        public void RemoveCombatant(RuntimeEntityId id)
        {
            var index = combatants.FindIndex(c => c.Id == id);
            if (index < 0) return;

            combatants.RemoveAt(index);
            if (index <= currentIndex && currentIndex > 0) currentIndex--;
        }

        /// <summary>Every still-present combatant (already excludes the dead/fled, since those are gone via
        /// RemoveCombatant) whose Faction differs from <paramref name="combatant"/>'s. Empty if the combatant
        /// isn't in this encounter.</summary>
        public IReadOnlyList<RuntimeEntityId> GetEnemiesOf(RuntimeEntityId combatant)
        {
            var self = combatants.Find(c => c.Id == combatant);
            if (self == null) return Array.Empty<RuntimeEntityId>();

            return combatants.Where(c => c.Faction != self.Faction).Select(c => c.Id).ToList();
        }

        /// <summary>Every still-present combatant sharing <paramref name="combatant"/>'s Faction, excluding
        /// itself. Empty if the combatant isn't in this encounter.</summary>
        public IReadOnlyList<RuntimeEntityId> GetAlliesOf(RuntimeEntityId combatant)
        {
            var self = combatants.Find(c => c.Id == combatant);
            if (self == null) return Array.Empty<RuntimeEntityId>();

            return combatants.Where(c => c.Faction == self.Faction && c.Id != combatant).Select(c => c.Id).ToList();
        }
    }
}

using UnityEngine;

namespace VikingRaid.Game
{
    /// <summary>
    /// Where a wipe returns the party (review findings 14): <see cref="Index"/> is the number of encounters WON (0..4). After a wipe the scene is reloaded and the party stands on the
    /// checkpoint slots of encounter <c>Index</c> (the road before the next unwon fight), with the earlier encounters' goblins gone. The very first boot is NOT a checkpoint
    /// reload: the party starts in the start clearing. Index 4 means the camp is destroyed: the victory state, never reloaded into.
    /// The only static state in the game besides the test input factory; reset on domain start-up and by every test.
    /// </summary>
    public static class Checkpoint
    {
        public const int EncounterCount = 4;

        public static int Index { get; private set; }

        /// <summary>True once a wipe has reloaded the scene: boot places the party on the checkpoint slots instead of the start clearing.</summary>
        public static bool SpawnAtCheckpoint { get; private set; }

        public static bool CampDestroyed => Index >= EncounterCount;

        /// <summary>Records that encounter <paramref name="encounterIndex"/> (0-based) was won. Never goes backwards.</summary>
        public static void EncounterWon(int encounterIndex) => Index = Mathf.Max(Index, Mathf.Clamp(encounterIndex + 1, 0, EncounterCount));

        /// <summary>The party was wiped: the next boot is a checkpoint reload.</summary>
        public static void WipeReload() => SpawnAtCheckpoint = true;

        public static void Reset() { Index = 0; SpawnAtCheckpoint = false; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnDomainStart() => Reset();
    }
}

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using VikingRaid.Game;

namespace VikingRaid.Tests.EditMode
{
    /// <summary>
    /// Plays each of the four designed encounters N times with the REAL rules engine, the real goblin brains and a scripted hero policy (see Arena), and holds the
    /// results to the gates in Task 2 design Section 12. A "fight" ends when one side is out (a downed hero counts as FULL hit points lost for that hero) or the
    /// round cap is hit (a stalemate = a failure: a fight the goblins cannot finish is a design bug, not a draw).
    /// </summary>
    public class BalanceSimulationTests
    {
        private const int Fights = 400;
        private static readonly string[] Party = { "bjorn", "ulf", "sigrid", "astrid" };

        private sealed class Encounter
        {
            public string Name; public string[] Goblins;
            public float WinMin, WinMax = 1.01f, LostMin, LostMax = 1f, DownedMin, DownedMax = 1f; // gates
        }

        private static readonly Encounter[] Encounters =
        {
            new() { Name = "E1 3 Grunts", Goblins = WorldLayout.EncounterGoblins[0],
                    WinMin = 0.99f, LostMin = 0.05f, LostMax = 0.25f, DownedMax = 0.10f },
            new() { Name = "E2 2 Warriors + Archer", Goblins = WorldLayout.EncounterGoblins[1],
                    WinMin = 0.97f, LostMin = 0.15f, LostMax = 0.40f, DownedMin = 0.10f, DownedMax = 0.40f },
            new() { Name = "E3 2 Warriors + 2 Archers + Shaman", Goblins = WorldLayout.EncounterGoblins[2],
                    WinMin = 0.90f, LostMin = 0.35f, LostMax = 0.60f, DownedMin = 0.30f, DownedMax = 0.75f },
            new() { Name = "E4 Troll + King + Shaman", Goblins = WorldLayout.EncounterGoblins[3],
                    WinMin = 0.55f, WinMax = 0.80f },
        };

        private struct Stats
        {
            public float Win, Lost, AnyDowned, Rounds, Stalemates, Farmed;
            public Dictionary<string, float> DamageShare;
        }

        private static Stats Simulate(ContentFixture content, Encounter encounter, int fights)
        {
            int wins = 0, stalemates = 0, anyDowned = 0, farmed = 0; double lost = 0, rounds = 0;
            var damage = Party.ToDictionary(p => p, _ => 0f);
            for (var seed = 1; seed <= fights; seed++)
            {
                using var arena = new Arena(content, seed * 31 + encounter.Name.Length);
                // the SAME formation the world places the encounters in (WorldLayout), so the measured balance is the balance of the built arenas
                for (var i = 0; i < Party.Length; i++) { var o = WorldLayout.PartyOffset(i); arena.Add(Party[i], new Vector3(o.x, 0, o.y)); }
                for (var i = 0; i < encounter.Goblins.Length; i++)
                    { var o = WorldLayout.GoblinOffset(i, encounter.Goblins.Length); arena.Add(encounter.Goblins[i], new Vector3(o.x, 0, o.y)); }

                var r = arena.Run();
                foreach (var line in arena.Diagnostics.Take(2)) Debug.Log("[Bal] diag " + line);
                if (r.Won) wins++;
                if (r.Stalemate) stalemates++;
                if (r.HeroesDowned > 0) anyDowned++;
                farmed += r.FarmedDownedHero;
                lost += r.HeroHpLost;
                rounds += r.Rounds;
                foreach (var kv in r.DamageTakenByHero) damage[kv.Key] += kv.Value;
            }

            var total = Mathf.Max(1f, damage.Values.Sum());
            return new Stats
            {
                Win = wins / (float)fights, Lost = (float)(lost / fights), AnyDowned = anyDowned / (float)fights, Rounds = (float)(rounds / fights),
                Stalemates = stalemates, Farmed = farmed, DamageShare = damage.ToDictionary(kv => kv.Key, kv => kv.Value / total),
            };
        }

        [Test]
        public void EveryEncounterMeetsItsDifficultyGates()
        {
            var content = ContentFixture.Load();
            var failures = new List<string>();
            var results = new List<(Encounter e, Stats s)>();

            foreach (var e in Encounters)
            {
                var s = Simulate(content, e, Fights);
                results.Add((e, s));
                var share = string.Join(" ", s.DamageShare.Select(kv => $"{kv.Key}={kv.Value:P0}"));
                Debug.Log($"[Bal] {e.Name,-36} win={s.Win:P1} hpLost={s.Lost:P1} anyDowned={s.AnyDowned:P1} rounds={s.Rounds:F1} stalemates={s.Stalemates} farmedDowned={s.Farmed} dmgShare[{share}]");

                if (s.Win < e.WinMin || s.Win > e.WinMax) failures.Add($"{e.Name}: win {s.Win:P1} outside [{e.WinMin:P0},{e.WinMax:P0}]");
                if (s.Lost < e.LostMin || s.Lost > e.LostMax) failures.Add($"{e.Name}: hp lost {s.Lost:P1} outside [{e.LostMin:P0},{e.LostMax:P0}]");
                if (s.AnyDowned < e.DownedMin || s.AnyDowned > e.DownedMax) failures.Add($"{e.Name}: P(>=1 downed) {s.AnyDowned:P1} outside [{e.DownedMin:P0},{e.DownedMax:P0}]");
                if (s.Rounds > 14f) failures.Add($"{e.Name}: mean rounds {s.Rounds:F1} > 14");
                if (s.Stalemates > 0) failures.Add($"{e.Name}: {s.Stalemates} fights hit the round cap (a failure)");
                if (s.Farmed > 0) failures.Add($"{e.Name}: goblins attacked a downed hero while another stood {s.Farmed} times");
                if (e.Name.StartsWith("E2") || e.Name.StartsWith("E3"))
                    foreach (var kv in s.DamageShare)
                        if (kv.Value > 0.60f) failures.Add($"{e.Name}: {kv.Key} takes {kv.Value:P0} of all damage (> 60%): the AI is focus-firing one hero");
            }

            for (var i = 1; i < results.Count; i++)
                if (!(results[i - 1].s.Lost < results[i].s.Lost))
                    failures.Add($"mean HP lost is not monotonic: {results[i - 1].e.Name} {results[i - 1].s.Lost:P1} !< {results[i].e.Name} {results[i].s.Lost:P1}");

            Assert.IsEmpty(failures, "\n" + string.Join("\n", failures));
        }
    }
}

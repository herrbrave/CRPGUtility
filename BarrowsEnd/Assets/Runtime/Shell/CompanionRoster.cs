using System;
using BarrowsEnd.Gate;
using CRPG.Core;

namespace BarrowsEnd.Shell
{
    /// <summary>Task 6.1: the fixed data for the three Act 1 recruits (Garrick, Elara, Silas) -- reuses
    /// <see cref="GateMemberSpec"/>'s own shape (it already has everything <c>ShellBootstrap.RecruitCompanion</c>
    /// needs: displayName/spawnId/maxHp/appearance/gear), just keyed by a stable string instead of a boot-order
    /// index, since companions join mid-session, not at boot. Ids are FIXED and distinct from
    /// <see cref="ShellBootstrap.MemberIdFor"/>'s own hex range so a save containing a recruited companion can be
    /// loaded after a process restart (see the plan's fact 6/T6.1-D3). Gear/appearance draws only from items
    /// BarrowsEnd already authors (<c>BarrowsEndGearAuthor</c>'s creator-pick armors + Kenney weapon picks) --
    /// zero new item authoring needed (the review's should-fix S1 found the "5-item budget" v1 assumed doesn't
    /// exist).</summary>
    public static class CompanionRoster
    {
        public static readonly string[] Keys = { "garrick", "elara", "silas" };

        public static RuntimeEntityId IdFor(string companionKey) => companionKey switch
        {
            "garrick" => RuntimeEntityId.FromString("barrowsend-companion-garrick"),
            "elara" => RuntimeEntityId.FromString("barrowsend-companion-elara"),
            "silas" => RuntimeEntityId.FromString("barrowsend-companion-silas"),
            _ => throw new ArgumentException($"Unknown companion key '{companionKey}'.", nameof(companionKey)),
        };

        public static GateMemberSpec SpecFor(string companionKey) => companionKey switch
        {
            // Garrick the Stalwart (Fighter): heavy armor, broadsword, wooden shield -- frontline tank.
            "garrick" => new GateMemberSpec
            {
                displayName = "Garrick", spawnId = "companion-garrick", maxHp = 24,
                appearance = new[] { "layer-body=body-kenney-0-0", "layer-hair=hair-kenney-19-0", "layer-clothes=clothes-kenney-6-0" },
                gear = new[] { "item-armor-be-2-0", "item-weapon-44-6", "item-round-shield" }, // Fighter Padded Mail, Iron Sword, Round Shield
            },
            // Elara the Swift (Rogue): light leathers, blade, burst/lockpicking utility.
            "elara" => new GateMemberSpec
            {
                displayName = "Elara", spawnId = "companion-elara", maxHp = 16,
                appearance = new[] { "layer-body=body-kenney-1-1", "layer-hair=hair-kenney-23-2", "layer-clothes=clothes-kenney-8-0" },
                gear = new[] { "item-armor-be-3-0", "item-short-sword" }, // Rogue Dark Leathers, Short Sword
            },
            // Brother Silas (Cleric, Life Domain): simple robes, mace, party sustain/healing.
            "silas" => new GateMemberSpec
            {
                displayName = "Silas", spawnId = "companion-silas", maxHp = 18,
                appearance = new[] { "layer-body=body-kenney-0-1", "layer-hair=hair-kenney-19-8", "layer-clothes=clothes-kenney-10-0" },
                gear = new[] { "item-armor-be-1-0", "item-weapon-47-4" }, // Cleric Vestments, Iron Mace
            },
            _ => throw new ArgumentException($"Unknown companion key '{companionKey}'.", nameof(companionKey)),
        };

        /// <summary>The authored Town fixture's GameObject name for this companion (before recruiting) -- shared
        /// between <c>BarrowsEndTownAuthor</c> (which creates it) and <c>ShellBootstrap</c> (which reads the
        /// recruit's world position from it, then hides it once recruited).</summary>
        public static string FixtureName(string companionKey) => "Companion_" + companionKey;
    }
}

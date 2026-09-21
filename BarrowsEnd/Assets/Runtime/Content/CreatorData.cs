using System;
using UnityEngine;

namespace BarrowsEnd
{
    /// <summary>
    /// The character creator's curated content, deserialized with <see cref="JsonUtility"/> from two JSON files under Assets/Content/Creator
    /// (Phase 4, Task 4.0). JsonUtility has no dictionaries, so every keyed collection is an array whose elements carry their key.
    /// At runtime these are read through a serialized TextAsset reference (never File.ReadAllText on an Assets/ path, which fails in a player).
    /// </summary>
    [Serializable]
    public sealed class CreatorPick
    {
        /// <summary>A sprite catalog option id (<c>hair-kenney-21-0</c>). Ids are permanent save contracts; a pick only references one.</summary>
        public string option;
        /// <summary>The curated name the creator shows for this option.</summary>
        public string name;
        /// <summary>Weapons only: the item this pick equips. Empty for skin/hair/outfit.</summary>
        public string item;
    }

    /// <summary><c>CreatorPicks.json</c>: what each creator row offers, in display order.</summary>
    [Serializable]
    public sealed class CreatorPicks
    {
        public CreatorPick[] skin = Array.Empty<CreatorPick>();
        public CreatorPick[] hair = Array.Empty<CreatorPick>();
        public CreatorPick[] outfit = Array.Empty<CreatorPick>();
        public CreatorPick[] weapons = Array.Empty<CreatorPick>();

        public static CreatorPicks Parse(string json) => JsonUtility.FromJson<CreatorPicks>(json);
    }

    [Serializable]
    public sealed class CreatorChoiceDefault
    {
        public string requirementId;
        public string[] options = Array.Empty<string>();
    }

    [Serializable]
    public sealed class CreatorClassEntry
    {
        public string classId;
        /// <summary>Item ids this class may start with (consumer data, not rules-derived: the ruleset has no weapon proficiency data).</summary>
        public string[] weapons = Array.Empty<string>();
        /// <summary>The six stat ids, highest priority first; the standard array is assigned in this order.</summary>
        public string[] scoreOrder = Array.Empty<string>();
        /// <summary>The level-1 choices the creator auto-resolves for this class.</summary>
        public CreatorChoiceDefault[] choiceDefaults = Array.Empty<CreatorChoiceDefault>();
    }

    /// <summary><c>CreatorClassTable.json</c>: the rules layer the creator does not ask the player about.</summary>
    [Serializable]
    public sealed class CreatorClassTable
    {
        public string race = "";
        public string background = "";
        public int level = 1;
        /// <summary>The six ability scores handed out in each class's <c>scoreOrder</c> (the SRD standard array). One source for the creator and its validators.</summary>
        public int[] standardArray = Array.Empty<int>();
        public CreatorClassEntry[] classes = Array.Empty<CreatorClassEntry>();

        public static CreatorClassTable Parse(string json) => JsonUtility.FromJson<CreatorClassTable>(json);
    }
}

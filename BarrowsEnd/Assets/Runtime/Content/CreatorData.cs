using System;
using UnityEngine;

namespace BarrowsEnd
{
    /// <summary>
    /// The character creator's curated content, deserialized with <see cref="JsonUtility"/> from two JSON files under Assets/Content/Creator
    /// (Phase 4, Tasks 4.0-4.2b). JsonUtility has no dictionaries, so every keyed collection is an array whose elements carry their key.
    /// At runtime these are read through a serialized TextAsset reference (never File.ReadAllText on an Assets/ path, which fails in a player).
    /// </summary>
    [Serializable]
    public sealed class CreatorPick
    {
        /// <summary>A sprite catalog option id (<c>body-kenney-1-0</c>, <c>clothes-be-0-0</c>). Ids are permanent save contracts; a pick only references one.</summary>
        public string option;
        /// <summary>The curated name the creator shows for this option.</summary>
        public string name;
        /// <summary>Armor and weapons: the item this pick equips. Empty for skin.</summary>
        public string item;
    }

    /// <summary>One hair colour: a swatch and the 16 hair options (12 head styles, then 4 beards) in the SAME style order as every other colour.</summary>
    [Serializable]
    public sealed class CreatorHairColor
    {
        public string id;
        public string name;
        /// <summary><c>#RRGGBB</c>, drawn as the swatch beside the Hair Colour row.</summary>
        public string swatch;
        public string[] options = Array.Empty<string>();
    }

    /// <summary><c>CreatorPicks.json</c>: what each creator row offers, in display order.</summary>
    [Serializable]
    public sealed class CreatorPicks
    {
        public CreatorPick[] skin = Array.Empty<CreatorPick>();
        /// <summary>The names of the 16 hair styles, shared by every colour (index = position in each colour's options).</summary>
        public string[] hairStyleNames = Array.Empty<string>();
        public CreatorHairColor[] hairColors = Array.Empty<CreatorHairColor>();
        /// <summary>The class armors (the class's outfit): custom generated sprites (<c>clothes-be-N-0</c>) and their items (<c>item-armor-be-N-0</c>).</summary>
        public CreatorPick[] armors = Array.Empty<CreatorPick>();
        /// <summary>Every weapon: each is a real item (loot), and a class's starting weapon is one of them.</summary>
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
        /// <summary>The class's starting kit as item ids: the armor (its outfit), then the weapon. Consumer data, not rules-derived.</summary>
        public string[] startingItems = Array.Empty<string>();
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

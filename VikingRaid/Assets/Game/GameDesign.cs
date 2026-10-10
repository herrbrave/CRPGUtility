using System.Collections.Generic;
using System.Linq;

namespace VikingRaid.Game
{
    /// <summary>What an ability does, at the level the authoring script and the tests care about.</summary>
    public enum AbilityKind
    {
        MeleeAttack,   // attack roll vs AC, Enemy, short range, costs the Action
        RangedAttack,  // attack roll vs AC, Enemy, long range, costs the Action
        SpellAttack,   // like RangedAttack but the attack stat is the caster's SpellcastingStatId
        HealAlly,      // heal an Ally in range; bonus action + a per-fight use pool
        SelfBuff,      // applies the Reckless condition to the user; bonus action
    }

    /// <summary>One ability of the game's own content (the shipped SRD5 Second Wind is reused, see <see cref="SecondWindId"/>).</summary>
    public sealed class AbilitySpec
    {
        public string Id, Name, Description, StatId, Cue;
        public AbilityKind Kind;
        public int Count, Sides, Modifier;
        public float Range, RecoverSeconds;

        public float AverageDamage => Count * (Sides + 1) / 2f + Modifier;
    }

    /// <summary>One combatant definition (one <c>NpcDefinition</c> and one visual prefab). The single source of truth for the authoring script AND the
    /// design tests: the tests compute AC and attack bonus through the REAL engine rules and compare with <see cref="ExpectedAc"/> / the expected
    /// attack bonuses, so a drift in either the authoring or the engine math is caught.</summary>
    public sealed class UnitSpec
    {
        public string Id, DisplayName, Prefab, Brain;
        public bool Hero;
        public int Level, Hp, ExpectedAc, Movement;
        public int Str, Dex, Con, Int, Wis, Cha;
        public string[] Abilities = new string[0];
        public bool Multiattack;            // AttackCount +1 (Troll, King)
        public string SpellcastingStatId;   // spell attacks (Shaman)
        public int SecondWindUses, VerseUses;

        public string NpcId => "npc-vr-" + Id;
        public int DexModifier => (Dex - 10) >> 1;
        public int StrModifier => (Str - 10) >> 1;
        public int ProficiencyBonus => 2 + (Level - 1) / 4;
        /// <summary>The Armor Class feature bonus needed so 10 + Dex modifier + bonus reaches <see cref="ExpectedAc"/>.</summary>
        public int ArmorBonus => ExpectedAc - 10 - DexModifier;

        public int AbilityModifier(string statId) => (statId switch
        {
            GameDesign.Str => Str, GameDesign.Dex => Dex, GameDesign.Con => Con,
            GameDesign.Int => Int, GameDesign.Wis => Wis, GameDesign.Cha => Cha, _ => 10,
        } - 10) >> 1;

        /// <summary>The attack bonus the engine must compute for <paramref name="ability"/>: governing-stat modifier + proficiency (every attack is proficient).</summary>
        public int ExpectedAttackBonus(AbilitySpec ability) =>
            AbilityModifier(ability.Kind == AbilityKind.SpellAttack ? SpellcastingStatId : ability.StatId) + ProficiencyBonus;
    }

    /// <summary>A behaviour set for a goblin type (heroes are player-driven; the balance simulation uses a scripted policy instead).</summary>
    public sealed class BrainSpec
    {
        public string Id, MeleeAbilityId, RangedAbilityId;
        public float StopDistance;    // melee: how close Move goes; ranged: range - 2 (so a shooter does not walk after every shot)
        public float RangedRange;
    }

    /// <summary>The game's content design, as data. See Docs/Plans/VikingRaid-Task2-ContentAndBalance.md (Sections 3-4 and 12).</summary>
    public static class GameDesign
    {
        public const string Str = "stat-strength", Dex = "stat-dexterity", Con = "stat-constitution", Int = "stat-intelligence", Wis = "stat-wisdom", Cha = "stat-charisma";
        public const string Hp = "resource-hp", Action = "resource-action", BonusAction = "resource-bonus-action", Reaction = "resource-reaction";
        public const string Movement = "resource-movement", VerseUses = "resource-vr-verse-uses", SecondWindUses = "resource-second-wind";
        public const string SecondWindId = "ability-second-wind";
        public const string RecklessCondition = "condition-vr-reckless";
        public const string HeroesFaction = "faction-heroes", GoblinsFaction = "faction-goblins";
        public const string MultiattackFeature = "feature-vr-multiattack";

        public static string ArmorFeatureId(int bonus) => "feature-vr-armor-" + bonus;

        private const string Chars = "Assets/Characters/Prefabs/";

        public static readonly AbilitySpec[] Abilities =
        {
            // ---- heroes
            new() { Id = "ability-vr-sword-strike", Name = "Sword Strike", Kind = AbilityKind.MeleeAttack, StatId = Str, Count = 1, Sides = 8, Modifier = 3, Range = 2f, Cue = "anim-attack-melee", RecoverSeconds = 1.2f,
                    Description = "A sword blow at a creature within reach." },
            new() { Id = "ability-vr-great-axe", Name = "Great Axe", Kind = AbilityKind.MeleeAttack, StatId = Str, Count = 1, Sides = 12, Modifier = 3, Range = 2f, Cue = "anim-attack-melee", RecoverSeconds = 1.2f,
                    Description = "A brutal two-handed axe swing." },
            new() { Id = "ability-vr-reckless-attack", Name = "Reckless Attack", Kind = AbilityKind.SelfBuff, Range = 0f, RecoverSeconds = 0.4f,
                    Description = "Bonus action. Until the start of your next turn your attack rolls have advantage, and so do attack rolls against you." },
            new() { Id = "ability-vr-longbow", Name = "Longbow Shot", Kind = AbilityKind.RangedAttack, StatId = Dex, Count = 1, Sides = 8, Modifier = 3, Range = 14f, Cue = "anim-shoot", RecoverSeconds = 1.4f,
                    Description = "Loose an arrow at a creature up to 14 m away." },
            new() { Id = "ability-vr-dagger-stab", Name = "Dagger Stab", Kind = AbilityKind.MeleeAttack, StatId = Dex, Count = 1, Sides = 4, Modifier = 3, Range = 2f, Cue = "anim-attack-melee", RecoverSeconds = 1.2f,
                    Description = "A quick knife thrust at an adjacent creature." },
            new() { Id = "ability-vr-spear-thrust", Name = "Spear Thrust", Kind = AbilityKind.MeleeAttack, StatId = Dex, Count = 1, Sides = 6, Modifier = 1, Range = 2.5f, Cue = "anim-attack-melee", RecoverSeconds = 1.2f,
                    Description = "A reaching spear thrust." },
            new() { Id = "ability-vr-rallying-verse", Name = "Rallying Verse", Kind = AbilityKind.HealAlly, Count = 1, Sides = 8, Modifier = 3, Range = 8f, Cue = "anim-shoot", RecoverSeconds = 1.0f,
                    Description = "Bonus action. Sing an ally back to their feet: heal 1d8+3 within 8 m. Three verses per fight." },
            // ---- goblins
            new() { Id = "ability-vr-cleaver", Name = "Cleaver", Kind = AbilityKind.MeleeAttack, StatId = Dex, Count = 1, Sides = 6, Modifier = 2, Range = 2f, Cue = "anim-attack-melee", RecoverSeconds = 1.2f,
                    Description = "A rusty cleaver." },
            new() { Id = "ability-vr-goblin-sword", Name = "Goblin Sword", Kind = AbilityKind.MeleeAttack, StatId = Str, Count = 1, Sides = 8, Modifier = 3, Range = 2f, Cue = "anim-attack-melee", RecoverSeconds = 1.2f,
                    Description = "A notched blade." },
            new() { Id = "ability-vr-shortbow", Name = "Shortbow", Kind = AbilityKind.RangedAttack, StatId = Dex, Count = 1, Sides = 6, Modifier = 3, Range = 12f, Cue = "anim-shoot", RecoverSeconds = 1.4f,
                    Description = "A crooked shortbow." },
            new() { Id = "ability-vr-goblin-dagger", Name = "Goblin Dagger", Kind = AbilityKind.MeleeAttack, StatId = Dex, Count = 1, Sides = 4, Modifier = 2, Range = 2f, Cue = "anim-attack-melee", RecoverSeconds = 1.2f,
                    Description = "A stolen dagger." },
            new() { Id = "ability-vr-fire-bolt", Name = "Fire Bolt", Kind = AbilityKind.SpellAttack, Count = 1, Sides = 8, Modifier = 0, Range = 12f, Cue = "anim-shoot", RecoverSeconds = 1.0f,
                    Description = "A spit of green-black flame (spell attack)." },
            new() { Id = "ability-vr-troll-rend", Name = "Rend", Kind = AbilityKind.MeleeAttack, StatId = Str, Count = 2, Sides = 6, Modifier = 3, Range = 2.5f, Cue = "anim-attack-melee", RecoverSeconds = 1.2f,
                    Description = "Claws and club. Two attacks per Action." },
            new() { Id = "ability-vr-king-greatsword", Name = "Warlord's Greatsword", Kind = AbilityKind.MeleeAttack, StatId = Str, Count = 1, Sides = 8, Modifier = 2, Range = 2.5f, Cue = "anim-attack-melee", RecoverSeconds = 1.2f,
                    Description = "The Goblin King's blade. Two attacks per Action." },
        };

        public static AbilitySpec Ability(string id) => Abilities.First(a => a.Id == id);

        public static readonly UnitSpec[] Units =
        {
            // ---- heroes: level 3 (proficiency +2); HP and AC are the SRD-flavoured design numbers
            new() { Id = "bjorn", DisplayName = "Bjorn", Prefab = Chars + "Hero_Bjorn.prefab", Hero = true, Level = 3, Hp = 31, ExpectedAc = 18, Movement = 8,
                    Str = 16, Dex = 10, Con = 15, Int = 10, Wis = 12, Cha = 10, Abilities = new[] { "ability-vr-sword-strike", SecondWindId }, SecondWindUses = 1 },
            new() { Id = "ulf", DisplayName = "Ulf", Prefab = Chars + "Hero_Ulf.prefab", Hero = true, Level = 3, Hp = 34, ExpectedAc = 14, Movement = 8,
                    Str = 17, Dex = 12, Con = 16, Int = 8, Wis = 10, Cha = 10, Abilities = new[] { "ability-vr-great-axe", "ability-vr-reckless-attack" } },
            new() { Id = "sigrid", DisplayName = "Sigrid", Prefab = Chars + "Hero_Sigrid.prefab", Hero = true, Level = 3, Hp = 26, ExpectedAc = 15, Movement = 8,
                    Str = 10, Dex = 16, Con = 14, Int = 10, Wis = 12, Cha = 10, Abilities = new[] { "ability-vr-longbow", "ability-vr-dagger-stab" } },
            new() { Id = "astrid", DisplayName = "Astrid", Prefab = Chars + "Hero_Astrid.prefab", Hero = true, Level = 3, Hp = 24, ExpectedAc = 13, Movement = 8,
                    Str = 10, Dex = 12, Con = 13, Int = 10, Wis = 12, Cha = 16, Abilities = new[] { "ability-vr-spear-thrust", "ability-vr-rallying-verse" }, VerseUses = 3 },

            // ---- goblins (one definition per visual prefab)
            new() { Id = "goblin-grunt-a", DisplayName = "Goblin Grunt", Prefab = Chars + "Goblin_Grunt_A.prefab", Brain = "vr-brain-grunt", Level = 1, Hp = 9, ExpectedAc = 13, Movement = 6,
                    Str = 8, Dex = 14, Con = 10, Int = 10, Wis = 8, Cha = 8, Abilities = new[] { "ability-vr-cleaver" } },
            new() { Id = "goblin-grunt-b", DisplayName = "Goblin Grunt", Prefab = Chars + "Goblin_Grunt_B.prefab", Brain = "vr-brain-grunt", Level = 1, Hp = 9, ExpectedAc = 13, Movement = 6,
                    Str = 8, Dex = 14, Con = 10, Int = 10, Wis = 8, Cha = 8, Abilities = new[] { "ability-vr-cleaver" } },
            new() { Id = "goblin-grunt-c", DisplayName = "Goblin Grunt", Prefab = Chars + "Goblin_Grunt_C.prefab", Brain = "vr-brain-grunt", Level = 1, Hp = 9, ExpectedAc = 13, Movement = 6,
                    Str = 8, Dex = 14, Con = 10, Int = 10, Wis = 8, Cha = 8, Abilities = new[] { "ability-vr-cleaver" } },
            new() { Id = "goblin-warrior-a", DisplayName = "Goblin Warrior", Prefab = Chars + "Goblin_Warrior_A.prefab", Brain = "vr-brain-warrior", Level = 2, Hp = 20, ExpectedAc = 17, Movement = 6,
                    Str = 18, Dex = 12, Con = 12, Int = 8, Wis = 10, Cha = 8, Abilities = new[] { "ability-vr-goblin-sword" } },
            new() { Id = "goblin-warrior-b", DisplayName = "Goblin Warrior", Prefab = Chars + "Goblin_Warrior_B.prefab", Brain = "vr-brain-warrior", Level = 2, Hp = 20, ExpectedAc = 17, Movement = 6,
                    Str = 18, Dex = 12, Con = 12, Int = 8, Wis = 10, Cha = 8, Abilities = new[] { "ability-vr-goblin-sword" } },
            new() { Id = "goblin-warrior-c", DisplayName = "Goblin Warrior", Prefab = Chars + "Goblin_Warrior_C.prefab", Brain = "vr-brain-warrior", Level = 2, Hp = 20, ExpectedAc = 17, Movement = 6,
                    Str = 18, Dex = 12, Con = 12, Int = 8, Wis = 10, Cha = 8, Abilities = new[] { "ability-vr-goblin-sword" } },
            new() { Id = "goblin-archer-m", DisplayName = "Goblin Archer", Prefab = Chars + "Goblin_Archer_M.prefab", Brain = "vr-brain-archer", Level = 1, Hp = 9, ExpectedAc = 14, Movement = 6,
                    Str = 8, Dex = 14, Con = 10, Int = 10, Wis = 10, Cha = 8, Abilities = new[] { "ability-vr-shortbow", "ability-vr-goblin-dagger" } },
            new() { Id = "goblin-archer-f", DisplayName = "Goblin Archer", Prefab = Chars + "Goblin_Archer_F.prefab", Brain = "vr-brain-archer", Level = 1, Hp = 9, ExpectedAc = 14, Movement = 6,
                    Str = 8, Dex = 14, Con = 10, Int = 10, Wis = 10, Cha = 8, Abilities = new[] { "ability-vr-shortbow", "ability-vr-goblin-dagger" } },
            new() { Id = "goblin-shaman", DisplayName = "Goblin Shaman", Prefab = Chars + "Goblin_Shaman.prefab", Brain = "vr-brain-shaman", Level = 3, Hp = 22, ExpectedAc = 13, Movement = 6,
                    Str = 8, Dex = 12, Con = 12, Int = 10, Wis = 16, Cha = 10, Abilities = new[] { "ability-vr-fire-bolt" }, SpellcastingStatId = Wis },
            new() { Id = "goblin-troll", DisplayName = "Cave Troll", Prefab = Chars + "Goblin_Troll.prefab", Brain = "vr-brain-troll", Level = 5, Hp = 50, ExpectedAc = 15, Movement = 4,
                    Str = 16, Dex = 12, Con = 18, Int = 6, Wis = 8, Cha = 6, Abilities = new[] { "ability-vr-troll-rend" }, Multiattack = true },
            new() { Id = "goblin-king", DisplayName = "Goblin King", Prefab = Chars + "Goblin_King.prefab", Brain = "vr-brain-king", Level = 5, Hp = 45, ExpectedAc = 17, Movement = 4,
                    Str = 14, Dex = 12, Con = 14, Int = 12, Wis = 12, Cha = 14, Abilities = new[] { "ability-vr-king-greatsword" }, Multiattack = true },
        };

        public static UnitSpec Unit(string id) => Units.First(u => u.Id == id);
        public static IEnumerable<UnitSpec> Heroes => Units.Where(u => u.Hero);
        public static IEnumerable<UnitSpec> Goblins => Units.Where(u => !u.Hero);

        public static readonly BrainSpec[] Brains =
        {
            new() { Id = "vr-brain-grunt", MeleeAbilityId = "ability-vr-cleaver", StopDistance = 1.5f },
            new() { Id = "vr-brain-warrior", MeleeAbilityId = "ability-vr-goblin-sword", StopDistance = 1.5f },
            new() { Id = "vr-brain-troll", MeleeAbilityId = "ability-vr-troll-rend", StopDistance = 2.0f },
            new() { Id = "vr-brain-king", MeleeAbilityId = "ability-vr-king-greatsword", StopDistance = 2.0f },
            // shooters stop 2 m inside their range (not 6 m: they would walk after every shot) and fall back to the dagger when adjacent
            new() { Id = "vr-brain-archer", MeleeAbilityId = "ability-vr-goblin-dagger", RangedAbilityId = "ability-vr-shortbow", RangedRange = 12f, StopDistance = 10f },
            // the shaman has only the bolt: with a melee fallback the shipped curve shape would prefer the staff when adjacent
            new() { Id = "vr-brain-shaman", RangedAbilityId = "ability-vr-fire-bolt", RangedRange = 12f, StopDistance = 10f },
        };

        /// <summary>The distinct armor bonuses the units need (one <c>feature-vr-armor-N</c> each).</summary>
        public static IEnumerable<int> ArmorBonuses => Units.Select(u => u.ArmorBonus).Distinct().OrderBy(b => b);
    }
}

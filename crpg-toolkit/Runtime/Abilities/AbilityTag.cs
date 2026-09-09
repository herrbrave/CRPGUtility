using System;

namespace CRPG.Abilities
{
    [Flags]
    public enum AbilityTag
    {
        None = 0,
        Attack = 1 << 0,
        Spell = 1 << 1,
        Melee = 1 << 2,
        Ranged = 1 << 3,
        Weapon = 1 << 4,
        Healing = 1 << 5,
        Movement = 1 << 6,
        Reaction = 1 << 7,
        Concentration = 1 << 8,
        Hostile = 1 << 9,
        Utility = 1 << 10,
        Ritual = 1 << 11
    }
}

using UnityEngine;

namespace CRPG.Actors
{
    /// <summary>SRD5's ability-modifier formula: floor((score - 10) / 2). Pure math, kept separate from any
    /// specific derived stat (AC, attack bonus, saves, skills all need it) so none of them duplicate it.</summary>
    public static class AbilityModifier
    {
        public static int From(int score) => Mathf.FloorToInt((score - 10) / 2f);
    }
}

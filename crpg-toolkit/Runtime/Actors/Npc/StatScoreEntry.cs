using System;
using CRPG.Core;
using UnityEngine;

namespace CRPG.Actors
{
    /// <summary>One ability score authored directly onto an NpcDefinition. StatBlock (what CharacterState.Stats
    /// and CharacterBuildPlan.AbilityScores both use) wraps a Dictionary internally and isn't Inspector-friendly,
    /// so NPCs -- authored complete up front, with no player-facing score-generation method -- use a flat list
    /// of these instead; NpcSpawnService copies them into a real StatBlock at spawn time.</summary>
    [Serializable]
    public struct StatScoreEntry
    {
        [SerializeField] private DefinitionId statId;
        [SerializeField] private int score;

        public readonly DefinitionId StatId => statId;
        public readonly int Score => score;
    }
}

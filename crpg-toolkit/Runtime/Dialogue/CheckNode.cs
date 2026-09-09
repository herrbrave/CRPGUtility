using System;
using UnityEngine;

namespace CRPG.Dialogue
{
    /// <summary>
    /// Covers both spec's "Skill Check" and "Ability Check" node kinds -- the underlying mechanic (roll 1d20 +
    /// a gathered modifier vs. a DC, branch on success/failure) is identical for both; only the QueryKey
    /// differs ("Skill:Persuasion" vs. "Ability:Strength", say). Uses the same FeatureModifierCollector-driven
    /// query-key pattern as DefaultInitiativeRule, so a ruleset opts a stat/skill into being checkable just by
    /// tagging a FeatureModifier with the matching key -- no engine changes.
    /// </summary>
    [Serializable]
    public sealed class CheckNode : DialogueNode
    {
        [SerializeField] private string queryKey;
        [SerializeField] private int difficultyClass;
        [SerializeField] private CheckPerformer performer = CheckPerformer.BestPartyMember;
        [SerializeField] private string successNodeId;
        [SerializeField] private string failureNodeId;

        public string QueryKey => queryKey;
        public int DifficultyClass => difficultyClass;
        public CheckPerformer Performer => performer;
        public string SuccessNodeId => successNodeId;
        public string FailureNodeId => failureNodeId;
    }
}

using System.Collections.Generic;
using CRPG.Core;
using UnityEngine;

namespace CRPG.Quests
{
    /// <summary>Authored quest data -- a display name/description and its objectives. A quest is complete
    /// once every one of its objectives is (no optional/alternative objectives in this first pass -- AND
    /// semantics only, matching the project's KISS motto).</summary>
    [CreateAssetMenu(menuName = "CRPG Toolkit/Quests/Quest Definition")]
    public sealed class QuestDefinition : DefinitionAsset
    {
        [SerializeField] private string displayName;
        [SerializeField, TextArea] private string description;
        [SerializeField] private List<QuestObjectiveDefinition> objectives = new();

        public string DisplayName => displayName;
        public string Description => description;
        public IReadOnlyList<QuestObjectiveDefinition> Objectives => objectives;
    }
}

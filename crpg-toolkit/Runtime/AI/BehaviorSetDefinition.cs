using System.Collections.Generic;
using CRPG.Core;
using UnityEngine;

namespace CRPG.AI
{
    /// <summary>A reusable, named bundle of Behaviors per creature archetype -- "Goblin Warrior" = [MeleeAttack,
    /// Retreat]. Authored once, assigned to every monster of that archetype's UtilityAIDecisionProvider, the
    /// same way a ClassDefinition bundles features for every Fighter. References Behaviors by DefinitionId,
    /// same convention as BehaviorDefinition's own ability reference.</summary>
    [CreateAssetMenu(menuName = "CRPG Toolkit/AI/Behavior Set Definition")]
    public sealed class BehaviorSetDefinition : DefinitionAsset
    {
        [SerializeField] private string displayName;
        [SerializeField] private List<DefinitionId> behaviorIds = new();

        public string DisplayName => displayName;
        public IReadOnlyList<DefinitionId> BehaviorIds => behaviorIds;
    }
}

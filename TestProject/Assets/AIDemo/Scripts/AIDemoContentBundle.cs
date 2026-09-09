using System.Collections.Generic;
using CRPG.Abilities;
using CRPG.Actors;
using CRPG.AI;
using CRPG.Core;
using UnityEngine;

// Dogfood-only demo plumbing (see third_party_assets / TestProject conventions) -- not part of the shipped
// package. Holds the AI-combat-test-specific content (an attack ability, a Movement resource, and two
// differently-tuned BehaviorSetDefinitions each mixing a "close distance" Move Behavior with their attack
// Behavior) on top of the Fighter sample content's HitPoints/Action resources, so Play mode can register
// everything into a runtime IRulesDatabase without Editor-only AssetDatabase queries.
public sealed class AIDemoContentBundle : ScriptableObject
{
    [SerializeField] private FighterSampleContentBundle fighterContent;
    [SerializeField] private List<DefinitionAsset> definitions = new();
    [SerializeField] private ResourceDefinition movement;
    [SerializeField] private AbilityDefinition strikeAbility;
    [SerializeField] private BehaviorDefinition heroAttackBehavior;
    [SerializeField] private BehaviorDefinition goblinAttackBehavior;
    [SerializeField] private BehaviorDefinition closeDistanceBehavior;
    [SerializeField] private BehaviorSetDefinition heroBehaviorSet;
    [SerializeField] private BehaviorSetDefinition goblinBehaviorSet;
    [SerializeField] private ConditionDefinition unconsciousCondition;
    [SerializeField] private ConditionDefinition proneCondition;
    [SerializeField] private ConditionDefinition restrainedCondition;

    public FighterSampleContentBundle FighterContent => fighterContent;
    public IReadOnlyList<DefinitionAsset> Definitions => definitions;
    public ResourceDefinition Movement => movement;
    public AbilityDefinition StrikeAbility => strikeAbility;
    public BehaviorDefinition HeroAttackBehavior => heroAttackBehavior;
    public BehaviorDefinition GoblinAttackBehavior => goblinAttackBehavior;
    public BehaviorDefinition CloseDistanceBehavior => closeDistanceBehavior;
    public BehaviorSetDefinition HeroBehaviorSet => heroBehaviorSet;
    public BehaviorSetDefinition GoblinBehaviorSet => goblinBehaviorSet;
    public ConditionDefinition UnconsciousCondition => unconsciousCondition;
    public ConditionDefinition ProneCondition => proneCondition;
    public ConditionDefinition RestrainedCondition => restrainedCondition;
}

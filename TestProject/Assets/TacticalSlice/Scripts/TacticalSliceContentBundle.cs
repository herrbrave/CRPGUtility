using System.Collections.Generic;
using CRPG.Abilities;
using CRPG.Actors;
using CRPG.Core;
using UnityEngine;

// Dogfood-only demo plumbing -- not part of the shipped package. Holds the tactical-slice-specific content
// (a melee attack, an opportunity-attack reaction plus the feature that grants it, and the Movement resource)
// on top of the Fighter sample content's stats/HitPoints/Action/Reaction, plus references to four real
// SRD5e2014 spells (FireBolt/HealingWord/Bless/Command) reused as-is for ranged/support/concentration/temporary-
// effect coverage -- Task 4.5 deliberately does not duplicate these, only the melee/reaction content genuinely
// specific to this fixture.
public sealed class TacticalSliceContentBundle : ScriptableObject
{
    [SerializeField] private FighterSampleContentBundle fighterContent;
    [SerializeField] private List<DefinitionAsset> definitions = new();
    [SerializeField] private ResourceDefinition movement;
    [SerializeField] private AbilityDefinition meleeStrike;
    [SerializeField] private AbilityDefinition opportunityAttack;
    [SerializeField] private FeatureDefinition reactiveStrikes;
    [SerializeField] private AbilityDefinition fireBolt;
    [SerializeField] private AbilityDefinition healingWord;
    [SerializeField] private AbilityDefinition bless;
    [SerializeField] private AbilityDefinition command;

    public FighterSampleContentBundle FighterContent => fighterContent;
    public IReadOnlyList<DefinitionAsset> Definitions => definitions;
    public ResourceDefinition Movement => movement;
    public AbilityDefinition MeleeStrike => meleeStrike;
    public AbilityDefinition OpportunityAttack => opportunityAttack;
    public FeatureDefinition ReactiveStrikes => reactiveStrikes;
    public AbilityDefinition FireBolt => fireBolt;
    public AbilityDefinition HealingWord => healingWord;
    public AbilityDefinition Bless => bless;
    public AbilityDefinition Command => command;
}

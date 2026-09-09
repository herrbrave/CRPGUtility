using System.Collections.Generic;
using System.Reflection;
using CRPG.Abilities;
using CRPG.Actors;
using CRPG.AI;
using CRPG.Core;
using CRPG.Inventory;
using CRPG.Quests;
using CRPG.Rules;
using UnityEngine;

namespace CRPG.Tests.EditMode
{
    /// <summary>Builds ScriptableObject definitions (and other authoring-only types) for tests via reflection,
    /// since their fields are private/serialized (authoring is meant to happen through the Inspector, not a
    /// public constructor).</summary>
    internal static class TestDefinitionFactory
    {
        public static ItemDefinition CreateItemDefinition(
            string id,
            string displayName,
            float weight = 0f,
            int maxStackSize = 1,
            DefinitionId equipmentSlotId = default,
            float baseValue = 0f,
            DefinitionId equippedFeatureId = default)
        {
            var definition = ScriptableObject.CreateInstance<ItemDefinition>();
            SetPrivateField(definition, "id", id);
            SetPrivateField(definition, "displayName", displayName);
            SetPrivateField(definition, "weight", weight);
            SetPrivateField(definition, "maxStackSize", maxStackSize);
            SetPrivateField(definition, "equipmentSlotId", equipmentSlotId);
            SetPrivateField(definition, "baseValue", baseValue);
            SetPrivateField(definition, "equippedFeatureId", equippedFeatureId);
            return definition;
        }

        public static QuestObjectiveDefinition CreateQuestObjective(
            string id, string displayName, QuestObjectiveType type, string trackingKey, int targetCount = 1)
        {
            var objective = new QuestObjectiveDefinition();
            SetPrivateField(objective, "id", id);
            SetPrivateField(objective, "displayName", displayName);
            SetPrivateField(objective, "type", type);
            SetPrivateField(objective, "trackingKey", trackingKey);
            SetPrivateField(objective, "targetCount", targetCount);
            return objective;
        }

        public static QuestDefinition CreateQuestDefinition(string id, string displayName, params QuestObjectiveDefinition[] objectives)
        {
            var definition = ScriptableObject.CreateInstance<QuestDefinition>();
            SetPrivateField(definition, "id", id);
            SetPrivateField(definition, "displayName", displayName);
            SetPrivateField(definition, "objectives", new List<QuestObjectiveDefinition>(objectives));
            return definition;
        }

        public static EquipmentSlotDefinition CreateEquipmentSlotDefinition(string id, string displayName)
        {
            var definition = ScriptableObject.CreateInstance<EquipmentSlotDefinition>();
            SetPrivateField(definition, "id", id);
            SetPrivateField(definition, "displayName", displayName);
            return definition;
        }

        public static ResourceDefinition CreateResourceDefinition(string id, string displayName, RestTrigger restTrigger = RestTrigger.None)
        {
            var definition = ScriptableObject.CreateInstance<ResourceDefinition>();
            SetPrivateField(definition, "id", id);
            SetPrivateField(definition, "displayName", displayName);
            SetPrivateField(definition, "restTrigger", restTrigger);
            return definition;
        }

        public static FeatureDefinition CreateFeatureDefinition(string id, string displayName, params FeatureModifier[] modifiers)
        {
            var definition = ScriptableObject.CreateInstance<FeatureDefinition>();
            SetPrivateField(definition, "id", id);
            SetPrivateField(definition, "displayName", displayName);
            SetPrivateField(definition, "modifiers", new List<FeatureModifier>(modifiers));
            return definition;
        }

        public static ConditionDefinition CreateConditionDefinition(string id, string displayName, params FeatureModifier[] modifiers)
        {
            var definition = ScriptableObject.CreateInstance<ConditionDefinition>();
            SetPrivateField(definition, "id", id);
            SetPrivateField(definition, "displayName", displayName);
            SetPrivateField(definition, "modifiers", new List<FeatureModifier>(modifiers));
            return definition;
        }

        public static FeatureModifier CreateFeatureModifier(string queryKey, RuleModifierOperation operation, float value, int priority = 0)
        {
            object boxed = new FeatureModifier();
            SetPrivateField(boxed, "queryKey", queryKey);
            SetPrivateField(boxed, "operation", operation);
            SetPrivateField(boxed, "value", value);
            SetPrivateField(boxed, "priority", priority);
            return (FeatureModifier)boxed;
        }

        public static void SetGrantedResources(FeatureDefinition feature, params ResourceGrant[] grants) =>
            SetPrivateField(feature, "grantedResources", new List<ResourceGrant>(grants));

        public static void SetGrantedProficiencies(FeatureDefinition feature, params DefinitionId[] proficiencyIds) =>
            SetPrivateField(feature, "grantedProficiencies", new List<DefinitionId>(proficiencyIds));

        public static void SetChoices(FeatureDefinition feature, params ChoiceRequirement[] choices) =>
            SetPrivateField(feature, "choices", new List<ChoiceRequirement>(choices));

        public static ResourceGrant CreateResourceGrant(ResourceDefinition resource, int maxAmount)
        {
            object boxed = new ResourceGrant();
            SetPrivateField(boxed, "resource", resource);
            SetPrivateField(boxed, "maxAmount", maxAmount);
            return (ResourceGrant)boxed;
        }

        public static ChoiceRequirement CreateChoiceRequirement(string id, string displayName, ChoiceType type, int count, params DefinitionId[] options)
        {
            var requirement = new ChoiceRequirement();
            SetPrivateField(requirement, "id", id);
            SetPrivateField(requirement, "displayName", displayName);
            SetPrivateField(requirement, "type", type);
            SetPrivateField(requirement, "count", count);
            SetPrivateField(requirement, "options", new List<DefinitionId>(options));
            return requirement;
        }

        public static StatDefinition CreateStatDefinition(string id, string displayName)
        {
            var definition = ScriptableObject.CreateInstance<StatDefinition>();
            SetPrivateField(definition, "id", id);
            SetPrivateField(definition, "displayName", displayName);
            return definition;
        }

        public static RaceDefinition CreateRaceDefinition(string id, string displayName, params DefinitionId[] grantedFeatureIds)
        {
            var definition = ScriptableObject.CreateInstance<RaceDefinition>();
            SetPrivateField(definition, "id", id);
            SetPrivateField(definition, "displayName", displayName);
            SetPrivateField(definition, "grantedFeatureIds", new List<DefinitionId>(grantedFeatureIds));
            return definition;
        }

        public static BackgroundDefinition CreateBackgroundDefinition(string id, string displayName, params DefinitionId[] grantedFeatureIds)
        {
            var definition = ScriptableObject.CreateInstance<BackgroundDefinition>();
            SetPrivateField(definition, "id", id);
            SetPrivateField(definition, "displayName", displayName);
            SetPrivateField(definition, "grantedFeatureIds", new List<DefinitionId>(grantedFeatureIds));
            return definition;
        }

        public static ClassLevelGrant CreateClassLevelGrant(int level, params DefinitionId[] featureIds)
        {
            var grant = new ClassLevelGrant();
            SetPrivateField(grant, "level", level);
            SetPrivateField(grant, "featureIds", new List<DefinitionId>(featureIds));
            return grant;
        }

        public static ClassDefinition CreateClassDefinition(string id, string displayName, DiceExpression hitDie, params ClassLevelGrant[] levelProgression)
        {
            var definition = ScriptableObject.CreateInstance<ClassDefinition>();
            SetPrivateField(definition, "id", id);
            SetPrivateField(definition, "displayName", displayName);
            SetPrivateField(definition, "hitDie", hitDie);
            SetPrivateField(definition, "levelProgression", new List<ClassLevelGrant>(levelProgression));
            return definition;
        }

        public static SubclassDefinition CreateSubclassDefinition(string id, string displayName, params ClassLevelGrant[] levelProgression)
        {
            var definition = ScriptableObject.CreateInstance<SubclassDefinition>();
            SetPrivateField(definition, "id", id);
            SetPrivateField(definition, "displayName", displayName);
            SetPrivateField(definition, "levelProgression", new List<ClassLevelGrant>(levelProgression));
            return definition;
        }

        public static StatScoreEntry CreateStatScoreEntry(DefinitionId statId, int score)
        {
            object boxed = new StatScoreEntry();
            SetPrivateField(boxed, "statId", statId);
            SetPrivateField(boxed, "score", score);
            return (StatScoreEntry)boxed;
        }

        public static NpcDefinition CreateNpcDefinition(string id, string displayName, int level = 1, params StatScoreEntry[] abilityScores)
        {
            var definition = ScriptableObject.CreateInstance<NpcDefinition>();
            SetPrivateField(definition, "id", id);
            SetPrivateField(definition, "displayName", displayName);
            SetPrivateField(definition, "level", level);
            SetPrivateField(definition, "abilityScores", new List<StatScoreEntry>(abilityScores));
            return definition;
        }

        public static AbilityDefinition CreateAbilityDefinition(
            string id, string displayName, TargetRelation relation, int maxTargets = 1, float range = 100f)
        {
            var definition = ScriptableObject.CreateInstance<AbilityDefinition>();
            SetPrivateField(definition, "id", id);
            SetPrivateField(definition, "displayName", displayName);

            var targeting = new TargetingDefinition();
            SetPrivateField(targeting, "mode", TargetMode.SingleUnit);
            SetPrivateField(targeting, "relation", relation);
            SetPrivateField(targeting, "minTargets", 1);
            SetPrivateField(targeting, "maxTargets", maxTargets);
            SetPrivateField(targeting, "range", range);
            SetPrivateField(definition, "targeting", targeting);

            return definition;
        }

        public static Consideration CreateConsideration(string inputKey, AnimationCurve curve, float weight = 1f)
        {
            var consideration = new Consideration();
            SetPrivateField(consideration, "inputKey", inputKey);
            SetPrivateField(consideration, "curve", curve);
            SetPrivateField(consideration, "weight", weight);
            return consideration;
        }

        public static BehaviorDefinition CreateBehaviorDefinition(
            string id, string displayName, DefinitionId abilityId, params Consideration[] considerations)
        {
            var definition = ScriptableObject.CreateInstance<BehaviorDefinition>();
            SetPrivateField(definition, "id", id);
            SetPrivateField(definition, "displayName", displayName);
            SetPrivateField(definition, "abilityId", abilityId);
            SetPrivateField(definition, "considerations", new List<Consideration>(considerations));
            return definition;
        }

        public static BehaviorDefinition CreateMoveBehaviorDefinition(
            string id, string displayName, DefinitionId movementResourceId, TargetRelation relation, float stopDistance, params Consideration[] considerations)
        {
            var definition = ScriptableObject.CreateInstance<BehaviorDefinition>();
            SetPrivateField(definition, "id", id);
            SetPrivateField(definition, "displayName", displayName);
            SetPrivateField(definition, "kind", BehaviorKind.Move);
            SetPrivateField(definition, "movementResourceId", movementResourceId);
            SetPrivateField(definition, "moveTargetRelation", relation);
            SetPrivateField(definition, "stopDistance", stopDistance);
            SetPrivateField(definition, "considerations", new List<Consideration>(considerations));
            return definition;
        }

        public static BehaviorSetDefinition CreateBehaviorSetDefinition(string id, string displayName, params DefinitionId[] behaviorIds)
        {
            var definition = ScriptableObject.CreateInstance<BehaviorSetDefinition>();
            SetPrivateField(definition, "id", id);
            SetPrivateField(definition, "displayName", displayName);
            SetPrivateField(definition, "behaviorIds", new List<DefinitionId>(behaviorIds));
            return definition;
        }

        public static void SetPrivateField(object target, string fieldName, object value)
        {
            var type = target.GetType();
            while (type != null)
            {
                var field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (field != null)
                {
                    field.SetValue(target, value);
                    return;
                }

                type = type.BaseType;
            }

            throw new System.Exception($"Field '{fieldName}' not found on {target.GetType().Name} or its base types.");
        }
    }
}

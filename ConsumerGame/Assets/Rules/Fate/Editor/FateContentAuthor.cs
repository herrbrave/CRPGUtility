using System.Collections.Generic;
using System.Reflection;
using CRPG.Abilities;
using CRPG.Actors;
using CRPG.Consumer.Fate;
using CRPG.Core;
using UnityEditor;
using UnityEngine;

namespace CRPG.Consumer.Fate.Editor
{
    /// <summary>
    /// One-time Editor authoring script for Gate D's Fate content -- same pattern as
    /// TacticalSliceContentAuthor.cs/FighterSampleContentAuthor.cs in crpg-toolkit's own TestProject: creates
    /// real, persisted .asset files via <see cref="AssetDatabase.CreateAsset"/>, never hand-edited YAML, never
    /// runtime reflection inside a shipped provider's Register() call. Re-running this menu item overwrites
    /// the same asset paths, so it's safe to use as "regenerate from scratch."
    /// </summary>
    internal static class FateContentAuthor
    {
        private const string ContentFolder = "Assets/Rules/Fate/Content";

        [MenuItem("Consumer Game/Fate/Author Content")]
        public static void AuthorContent()
        {
            if (!AssetDatabase.IsValidFolder(ContentFolder))
                AssetDatabase.CreateFolder("Assets/Rules/Fate", "Content");

            var action = CreateResource("resource-fate-action", "Action");
            var fatePoints = CreateResource("resource-fate-points", "Fate Points");
            var stress = CreateResource("resource-fate-stress", "Stress");
            var fight = CreateResource("resource-fate-skill-fight", "Fight");
            var athletics = CreateResource("resource-fate-skill-athletics", "Athletics");
            var notice = CreateResource("resource-fate-skill-notice", "Notice");
            var will = CreateResource("resource-fate-skill-will", "Will");

            var strike = CreateStrikeAbility("ability-fate-strike", "Strike", action, fight, athletics, stress, invokeBonus: 0, extraCost: null);
            var strikeInvoking = CreateStrikeAbility("ability-fate-strike-invoking", "Strike, Invoking a Stunt", action, fight, athletics, stress, invokeBonus: 2, extraCost: fatePoints);

            var provider = ScriptableObject.CreateInstance<FateRulesetProvider>();
            SetField(provider, "content", new List<DefinitionAsset> { action, fatePoints, stress, fight, athletics, notice, will, strike, strikeInvoking });
            SetField(provider, "skills", new List<FateRulesetProvider.SkillEntry>
            {
                MakeSkillEntry("Skill:Fight", fight),
                MakeSkillEntry("Skill:Athletics", athletics),
                MakeSkillEntry("Skill:Notice", notice),
                MakeSkillEntry("Skill:Will", will),
            });
            SetField(provider, "actionResource", action);
            SetField(provider, "fatePointsResource", fatePoints);
            SetField(provider, "stressResource", stress);
            SetField(provider, "strikeAbility", strike);
            SetField(provider, "strikeInvokingAbility", strikeInvoking);
            CreateAsset(provider, "provider-fate-condensed");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[FateContentAuthor] Authored 7 resources, 2 abilities, and the FateRulesetProvider under " + ContentFolder);
        }

        private static ResourceDefinition CreateResource(string id, string displayName)
        {
            var resource = ScriptableObject.CreateInstance<ResourceDefinition>();
            SetField(resource, "id", id);
            SetField(resource, "displayName", displayName);
            CreateAsset(resource, id);
            return resource;
        }

        private static AbilityDefinition CreateStrikeAbility(
            string id, string displayName, ResourceDefinition action, ResourceDefinition attackSkill,
            ResourceDefinition defenseSkill, ResourceDefinition stress, int invokeBonus, ResourceDefinition extraCost)
        {
            var ability = ScriptableObject.CreateInstance<AbilityDefinition>();
            SetField(ability, "id", id);
            SetField(ability, "displayName", displayName);
            SetField(ability, "tags", AbilityTag.Attack | AbilityTag.Melee);

            var targeting = new TargetingDefinition();
            SetField(targeting, "mode", TargetMode.SingleUnit);
            SetField(targeting, "relation", TargetRelation.Enemy);
            SetField(targeting, "minTargets", 1);
            SetField(targeting, "maxTargets", 1);
            SetField(targeting, "range", 2.5f);
            SetField(ability, "targeting", targeting);

            var costs = new List<AbilityCost> { MakeCost(action, 1) };
            if (extraCost != null) costs.Add(MakeCost(extraCost, 1));
            SetField(ability, "costs", costs);

            var effect = new FateAttackEffect();
            SetField(effect, "attackSkillResource", attackSkill);
            SetField(effect, "defenseSkillResource", defenseSkill);
            SetField(effect, "stressResource", stress);
            SetField(effect, "invokeBonus", invokeBonus);
            SetField(ability, "effects", new List<AbilityEffect> { effect });

            CreateAsset(ability, id);
            return ability;
        }

        private static AbilityCost MakeCost(ResourceDefinition resource, int amount)
        {
            object boxed = new AbilityCost();
            SetField(boxed, "resource", resource);
            SetField(boxed, "amount", amount);
            return (AbilityCost)boxed;
        }

        private static FateRulesetProvider.SkillEntry MakeSkillEntry(string queryKey, ResourceDefinition resource)
        {
            object boxed = new FateRulesetProvider.SkillEntry();
            SetField(boxed, "queryKey", queryKey);
            SetField(boxed, "resource", resource);
            return (FateRulesetProvider.SkillEntry)boxed;
        }

        private static void CreateAsset(Object asset, string fileName)
        {
            var path = $"{ContentFolder}/{fileName}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Object>(path);
            if (existing != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(asset, path);
            EditorUtility.SetDirty(asset);
        }

        private static void SetField(object target, string fieldName, object value)
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

using System.Collections.Generic;
using CRPG.Actors;
using CRPG.Abilities;
using CRPG.Core;
using CRPG.Inventory;
using CRPG.Quests;

namespace CRPG.Saving
{
    public sealed class ContentValidationResult
    {
        public bool IsValid => Errors.Count == 0;
        public List<string> Errors { get; } = new();
    }

    /// <summary>
    /// Task 3.5: "JSON parse success is not save validity" -- checked BEFORE SessionLoadCoordinator ever builds
    /// a live GameSession, so bad content is rejected while the old session is still completely untouched.
    /// Walks every DefinitionId-shaped reference in a SaveData against IRulesDatabase, and checks a handful of
    /// references that must stay internally consistent WITHIN the save itself (not against content at all).
    /// <para>Deliberately does NOT validate "Proficiencies" against a specific DefinitionAsset type -- a
    /// proficiency id can legitimately point at a StatDefinition (saving-throw proficiency) or other targets
    /// depending on content, and there is no single dedicated ProficiencyDefinition type in this codebase to
    /// resolve against; rejecting a well-formed but ambiguously-typed id would be a false positive. Only
    /// non-emptiness is checked for those.</para>
    /// </summary>
    public static class ContentValidator
    {
        public static ContentValidationResult Validate(SaveData data, IRulesDatabase rulesDatabase)
        {
            var result = new ContentValidationResult();
            var characterIds = new HashSet<string>();

            foreach (var character in data.Characters)
            {
                if (!string.IsNullOrEmpty(character.Id) && !characterIds.Add(character.Id))
                    result.Errors.Add($"Duplicate character id '{character.Id}' within this save.");

                foreach (var stat in character.Stats) RequireResolves<StatDefinition>(rulesDatabase, stat.StatId, $"Character '{character.Id}' stat", result);
                foreach (var resource in character.Resources) RequireResolves<ResourceDefinition>(rulesDatabase, resource.ResourceId, $"Character '{character.Id}' resource", result);
                foreach (var condition in character.Conditions) RequireResolves<ConditionDefinition>(rulesDatabase, condition.ConditionId, $"Character '{character.Id}' condition", result);
                foreach (var featureId in character.Features) RequireResolves<FeatureDefinition>(rulesDatabase, featureId, $"Character '{character.Id}' feature", result);
                foreach (var abilityId in character.KnownAbilities) RequireResolves<AbilityDefinition>(rulesDatabase, abilityId, $"Character '{character.Id}' known ability", result);
                foreach (var abilityId in character.PreparedAbilities) RequireResolves<AbilityDefinition>(rulesDatabase, abilityId, $"Character '{character.Id}' prepared ability", result);
                foreach (var proficiencyId in character.Proficiencies)
                {
                    if (string.IsNullOrEmpty(proficiencyId)) result.Errors.Add($"Character '{character.Id}' has an empty proficiency id.");
                }

                RequireResolvesOrNone<StatDefinition>(rulesDatabase, character.SpellcastingStatId, $"Character '{character.Id}' SpellcastingStatId", result);
                RequireResolvesOrNone<AbilityDefinition>(rulesDatabase, character.ConcentratingOnAbilityId, $"Character '{character.Id}' ConcentratingOnAbilityId", result);
            }

            foreach (var item in data.InventoryItems)
            {
                RequireResolves<ItemDefinition>(rulesDatabase, item.DefinitionId, "Inventory item", result);
                RequireResolvesOrNone<EquipmentSlotDefinition>(rulesDatabase, item.EquippedSlotId, $"Item '{item.InstanceId}' EquippedSlotId", result);
                if (!string.IsNullOrEmpty(item.EquippedCharacterId) && !characterIds.Contains(item.EquippedCharacterId))
                    result.Errors.Add($"Item '{item.InstanceId}' is equipped by character '{item.EquippedCharacterId}', which is not present in this save.");
            }

            var questIds = new HashSet<string>();
            foreach (var quest in data.Quests)
            {
                if (!string.IsNullOrEmpty(quest.QuestId) && !questIds.Add(quest.QuestId))
                    result.Errors.Add($"Duplicate quest id '{quest.QuestId}' within this save.");
                RequireResolves<QuestDefinition>(rulesDatabase, quest.QuestId, "Quest", result);
            }

            var spawnIds = new HashSet<string>();
            foreach (var spawn in data.WorldSpawns)
            {
                if (!string.IsNullOrEmpty(spawn.SpawnId) && !spawnIds.Add(spawn.SpawnId))
                    result.Errors.Add($"Duplicate world spawn id '{spawn.SpawnId}' within this save.");
                if (!string.IsNullOrEmpty(spawn.CharacterId) && !characterIds.Contains(spawn.CharacterId))
                    result.Errors.Add($"World spawn '{spawn.SpawnId}' references character '{spawn.CharacterId}', which is not present in this save.");
                RequireResolvesOrNone<NpcDefinition>(rulesDatabase, spawn.NpcDefinitionId, $"World spawn '{spawn.SpawnId}' NpcDefinitionId", result);
            }

            foreach (var store in data.Stores)
                foreach (var listing in store.Listings)
                    RequireResolves<ItemDefinition>(rulesDatabase, listing.ItemId, $"Store '{store.StoreId}' listing", result);

            foreach (var container in data.LootContainers)
                foreach (var item in container.Items)
                    RequireResolves<ItemDefinition>(rulesDatabase, item.DefinitionId, $"Loot container '{container.ContainerId}' item", result);

            return result;
        }

        private static void RequireResolves<TDefinition>(IRulesDatabase rulesDatabase, string idValue, string context, ContentValidationResult result)
            where TDefinition : class, IDefinition
        {
            var id = new DefinitionId(idValue);
            if (!id.IsValid) { result.Errors.Add($"{context} has an empty/missing id."); return; }
            if (!rulesDatabase.TryResolve<TDefinition>(id, out _))
                result.Errors.Add($"{context} references '{idValue}' ({typeof(TDefinition).Name}), which does not resolve against the current content.");
        }

        /// <summary>DefinitionId.None (empty) is a valid, intentionally-absent sentinel -- e.g. a non-caster's
        /// SpellcastingStatId, a party member's NpcDefinitionId -- and is accepted, not rejected.</summary>
        private static void RequireResolvesOrNone<TDefinition>(IRulesDatabase rulesDatabase, string idValue, string context, ContentValidationResult result)
            where TDefinition : class, IDefinition
        {
            if (string.IsNullOrEmpty(idValue)) return;
            if (!rulesDatabase.TryResolve<TDefinition>(new DefinitionId(idValue), out _))
                result.Errors.Add($"{context} references '{idValue}' ({typeof(TDefinition).Name}), which does not resolve against the current content.");
        }
    }
}

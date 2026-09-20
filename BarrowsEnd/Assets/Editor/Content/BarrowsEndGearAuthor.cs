using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using CRPG.Abilities;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Inventory;
using CRPG.Presentation.Equipment;
using CRPG.Presentation.Sprites;
using CRPG.Rules.SRD5;
using UnityEditor;
using UnityEngine;

namespace BarrowsEnd.Editor
{
    /// <summary>
    /// Task 2.7a: authors the gear, debug abilities and ruleset provider the gate scene (and later the campaign) needs. Idempotent: an
    /// existing asset is updated in place (asset GUIDs and definition ids stay). Sprites come from the generated catalogs BY OPTION ID,
    /// so re-curating which sprite a piece of gear shows means changing one option id here, never an item id.
    /// <para>Item, profile, ability and slot ids are permanent save contracts (a save records equipped slot ids; the load validator
    /// resolves them). SRD5 has no head slot, so <c>slot-head</c> is added here and registered in the provider.</para>
    /// </summary>
    public static class BarrowsEndGearAuthor
    {
        public const string NpcDir = "Assets/Content/Npcs";
        public const string SkeletonNpcId = "npc-skeleton";
        public const string SkeletonBodyOptionId = "body-be-0-0";
        public const string SkeletonFaction = "faction-undead";
        public const string GearDir = "Assets/Content/Gear";
        public const string AbilityDir = "Assets/Content/Abilities";
        public const string RulesetPath = "Assets/Content/Ruleset/BarrowsEndRuleset.asset";
        public const string Srd5ProviderPath = "Packages/com.crpgtoolkit.core/Rules/SRD5e2014/Content/SRD5RulesetProvider.asset";
        public const string HitPointsPath = "Packages/com.crpgtoolkit.core/Rules/SRD5e2014/Content/ResourceDefinition_HitPoints.asset";

        public const string HeadSlotId = "slot-head";

        public sealed class GearSpec
        {
            public string ItemId;
            public string ProfileId;
            public string DisplayName;
            public string SlotId;
            public string LayerSlot;
            public string CatalogShortName;
            public string OptionId;
            public string[] HiddenLayerSlots = Array.Empty<string>();
            public int Priority;
        }

        public static readonly GearSpec[] Gear =
        {
            new() { ItemId = "item-iron-helm", ProfileId = "visual-iron-helm", DisplayName = "Iron Helm", SlotId = HeadSlotId,
                    LayerSlot = "layer-helmet", CatalogShortName = "helmet", OptionId = "helmet-kenney-28-0", HiddenLayerSlots = new[] { "layer-hair" } },
            new() { ItemId = "item-short-sword", ProfileId = "visual-short-sword", DisplayName = "Short Sword", SlotId = "slot-mainhand",
                    LayerSlot = "layer-weapon", CatalogShortName = "weapon", OptionId = "weapon-kenney-42-0" },
            new() { ItemId = "item-round-shield", ProfileId = "visual-round-shield", DisplayName = "Round Shield", SlotId = "slot-offhand",
                    LayerSlot = "layer-shield", CatalogShortName = "shield", OptionId = "shield-kenney-33-0" },
            new() { ItemId = "item-tunic", ProfileId = "visual-tunic", DisplayName = "Tunic", SlotId = "slot-clothing-body",
                    LayerSlot = "layer-clothes", CatalogShortName = "clothes", OptionId = "clothes-kenney-6-0" },
        };

        public sealed class DebugAbilitySpec
        {
            public string AbilityId;
            public string DisplayName;
            public int Sides;
            public int Modifier;
        }

        // 1d2+2 = 3-4 (an ordinary hit); 1d2+40 = 41-42 (overkill >= any character's max HP, so the death-save service kills outright).
        public static readonly DebugAbilitySpec[] DebugAbilities =
        {
            new() { AbilityId = "ability-debug-hit", DisplayName = "Debug Hit (3-4)", Sides = 2, Modifier = 2 },
            new() { AbilityId = "ability-debug-smash", DisplayName = "Debug Smash (41-42)", Sides = 2, Modifier = 40 },
        };

        public static string ItemPath(GearSpec g) => $"{GearDir}/{g.ItemId}.asset";
        public static string ProfilePath(GearSpec g) => $"{GearDir}/{g.ProfileId}.asset";
        public static string SlotPath => $"{GearDir}/{HeadSlotId}.asset";
        public static string AbilityPath(DebugAbilitySpec a) => $"{AbilityDir}/{a.AbilityId}.asset";

        [MenuItem("Barrows End/Content/Author Gear And Ruleset")]
        public static void Author()
        {
            Directory.CreateDirectory(GearDir);
            Directory.CreateDirectory(AbilityDir);
            Directory.CreateDirectory(NpcDir);
            Directory.CreateDirectory(Path.GetDirectoryName(RulesetPath));
            AssetDatabase.Refresh();

            var content = new List<DefinitionAsset>();

            // The seven sprite catalogs.
            foreach (var spec in BarrowsEndCatalogAuthor.Specs)
            {
                var catalog = AssetDatabase.LoadAssetAtPath<SpriteLayerCatalog>(BarrowsEndCatalogAuthor.CatalogPath(spec));
                if (catalog == null) throw new InvalidOperationException($"{BarrowsEndCatalogAuthor.CatalogPath(spec)} is missing -- run 'Barrows End/Content/Author Sprite Catalogs' first.");
                content.Add(catalog);
            }

            var headSlot = LoadOrCreate<EquipmentSlotDefinition>(SlotPath);
            SetField(headSlot, "id", HeadSlotId);
            SetField(headSlot, "displayName", "Head");
            Save(headSlot);
            content.Add(headSlot);

            foreach (var gear in Gear)
            {
                var catalog = AssetDatabase.LoadAssetAtPath<SpriteLayerCatalog>(BarrowsEndCatalogAuthor.CatalogPath(BarrowsEndCatalogAuthor.Specs.First(s => s.ShortName == gear.CatalogShortName)));
                var option = catalog.Options.FirstOrDefault(o => o.optionId.Value == gear.OptionId);
                if (option == null || option.sprite == null)
                    throw new InvalidOperationException($"Option '{gear.OptionId}' is not in catalog-{gear.CatalogShortName}; pick another sprite for {gear.ItemId}.");

                var profile = LoadOrCreate<EquipmentVisualProfile>(ProfilePath(gear));
                SetField(profile, "id", gear.ProfileId);
                SetField(profile, "kind", EquipmentVisualKind.Sprite);
                SetField(profile, "spriteLayers", new List<SpriteLayerEntry> { new(new DefinitionId(gear.LayerSlot), option.sprite, Color.white) });
                SetField(profile, "spritePriority", gear.Priority);
                SetField(profile, "hiddenLayerSlots", gear.HiddenLayerSlots.Select(s => new DefinitionId(s)).ToList());
                Save(profile);

                var item = LoadOrCreate<ItemDefinition>(ItemPath(gear));
                SetField(item, "id", gear.ItemId);
                SetField(item, "displayName", gear.DisplayName);
                SetField(item, "maxStackSize", 1);
                SetField(item, "equipmentSlotId", new DefinitionId(gear.SlotId));
                SetField(item, "equipmentVisualProfileId", new DefinitionId(gear.ProfileId));
                Save(item);

                content.Add(profile);
                content.Add(item);
            }

            var hitPoints = AssetDatabase.LoadAssetAtPath<CRPG.Actors.ResourceDefinition>(HitPointsPath)
                            ?? throw new InvalidOperationException($"{HitPointsPath} is missing.");
            foreach (var spec in DebugAbilities)
            {
                var ability = LoadOrCreate<AbilityDefinition>(AbilityPath(spec));
                SetField(ability, "id", spec.AbilityId);
                SetField(ability, "displayName", spec.DisplayName);
                SetField(ability, "tags", AbilityTag.Attack | AbilityTag.Hostile);
                SetField(ability, "costs", new List<AbilityCost>()); // no turn loop refills a cost pool: a debug ability must be free

                var targeting = new TargetingDefinition();
                SetField(targeting, "relation", TargetRelation.Any);
                SetField(targeting, "range", 1000f);
                SetField(targeting, "requireLineOfSight", false);
                SetField(targeting, "requireLineOfEffect", false);
                SetField(targeting, "requireVisibleTarget", false);
                SetField(ability, "targeting", targeting);

                var damage = new DamageEffect();
                SetField(damage, "hitPointResource", hitPoints);
                SetField(damage, "amount", new DiceExpression(1, spec.Sides, spec.Modifier));
                SetField(ability, "effects", new List<AbilityEffect> { damage });
                Save(ability);
                content.Add(ability);
            }

            var skeleton = LoadOrCreate<NpcDefinition>($"{NpcDir}/{SkeletonNpcId}.asset");
            var grant = (object)new ResourceGrant();
            SetField(grant, "resource", hitPoints);
            SetField(grant, "maxAmount", 13);
            SetField(skeleton, "id", SkeletonNpcId);
            SetField(skeleton, "displayName", "Skeleton");
            SetField(skeleton, "faction", new DefinitionId(SkeletonFaction));
            SetField(skeleton, "level", 1);
            SetField(skeleton, "resources", new List<ResourceGrant> { (ResourceGrant)grant });
            SetField(skeleton, "visualPrefab", AssetDatabase.LoadAssetAtPath<GameObject>(BarrowsEndCharacterAuthor.SpriteCharacterPath)
                                               ?? throw new InvalidOperationException($"{BarrowsEndCharacterAuthor.SpriteCharacterPath} is missing -- run the character author first."));
            SetField(skeleton, "appearance", new List<AppearanceEntry> { new(new DefinitionId("layer-body"), new DefinitionId(SkeletonBodyOptionId)) });
            Save(skeleton);
            content.Add(skeleton);

            var srd5 = AssetDatabase.LoadAssetAtPath<SRD5RulesetProvider>(Srd5ProviderPath)
                       ?? throw new InvalidOperationException($"{Srd5ProviderPath} is missing.");
            var ruleset = LoadOrCreate<BarrowsEndRulesetProvider>(RulesetPath);
            SetField(ruleset, "srd5", srd5);
            SetField(ruleset, "extraContent", content);
            Save(ruleset);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[BarrowsEnd] Authored ruleset with {content.Count} extra definitions ({Gear.Length} gear items, {DebugAbilities.Length} debug abilities).");
        }

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) return existing;
            var created = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(created, path);
            return created;
        }

        private static void Save(UnityEngine.Object asset) => EditorUtility.SetDirty(asset);

        private static void SetField(object target, string name, object value)
        {
            var type = target.GetType();
            while (type != null)
            {
                var field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
                if (field != null) { field.SetValue(target, value); return; }
                type = type.BaseType;
            }

            throw new Exception($"Field '{name}' not found on {target.GetType().Name}.");
        }
    }
}

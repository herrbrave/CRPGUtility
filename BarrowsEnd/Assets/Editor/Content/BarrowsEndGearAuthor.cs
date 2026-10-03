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
using CRPG.Quests;
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

        // Task 6.1: the palisade-breach combat tutorial's hostile. No rat/beast sprite exists anywhere in this
        // project's art (confirmed by search -- BarrowsEndCatalogAuthor.cs:63's own comment says both "be" body
        // options are "the two custom skeleton bodies"), so this reuses the second, otherwise-unused skeleton
        // body (body-be-1-0) as a placeholder -- visually distinct from the skeleton's own body-be-0-0 at least,
        // flagged here for a real rat sprite once one exists (matches this project's standing "demo art is
        // placeholder" convention elsewhere).
        public const string GiantRatNpcId = "npc-giant-rat";
        public const string GiantRatBodyOptionId = "body-be-1-0";
        public const string GiantRatFaction = "faction-hostile";
        public const string RatBiteAbilityId = "ability-rat-bite";

        // Task 6.1: no party member (protagonist or companion) had ANY combat ability before this task -- found
        // live while wiring the palisade-breach tutorial: with nothing granted, the real AbilityBarView would
        // show zero buttons and the fight could never be won through the actual UI. A real (not "debug") melee
        // ability every party member is granted at creation/recruit time -- flat 1d6, matching the rat bite's
        // own "auto-hit, no attack roll" simplification (real weapon-damage-die/attack-bonus resolution needs
        // equipped-weapon-aware combat math this task doesn't build).
        public const string StrikeAbilityId = "ability-strike";

        // Task 6.1 (T6.1-D4): Act 1's own quest -- NOT a multi-act quest, so it can't complete prematurely once
        // Act 1's own objective finishes (fact 10's should-fix). One Kill objective, trackingKey matching what
        // PalisadeBreachTrigger records on the party's actual victory (not a per-rat count -- the trigger records
        // the flag exactly once, so targetCount 1 against HistoricalCount is correct).
        public const string GatheringStormQuestId = "quest-gathering-storm";
        public const string DefeatPalisadeRatsObjectiveId = "defeat-palisade-rats";

        // Task 6.1 (T6.1-D7): Lyra's Arcanist Study needs ONE new item -- no SRD5-package scroll exists (fact
        // 13, confirmed by search), unlike Fendrick's/Silvan's catalogs, which stock entirely from the already-
        // resolving SRD5e2014 package with zero new authoring.
        public const string IdentifyScrollItemId = "item-scroll-identify";
        public const string GearDir = "Assets/Content/Gear";
        public const string CurrencyItemId = "item-gold";
        public const string AbilityDir = "Assets/Content/Abilities";
        public const string RulesetPath = "Assets/Content/Ruleset/BarrowsEndRuleset.asset";
        public const string Srd5ProviderPath = "Packages/com.crpgtoolkit.core/Rules/SRD5e2014/Content/SRD5RulesetProvider.asset";
        public const string ActionPath = "Packages/com.crpgtoolkit.core/Rules/SRD5e2014/Content/ResourceDefinition_Action.asset";
        public const string HitPointsPath = "Packages/com.crpgtoolkit.core/Rules/SRD5e2014/Content/ResourceDefinition_HitPoints.asset";

        public const string HeadSlotId = "slot-head";
        public const string ArmorSlotId = "slot-armor";
        public const int ArmorPriority = 10;

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

        public const string CreatorPicksPath = "Assets/Content/Creator/CreatorPicks.json";

        private static GearSpec[] gear;

        /// <summary>The fixed campaign gear plus one main-hand weapon item per weapon pick and one armor item per armor pick in <c>CreatorPicks.json</c>
        /// (Task 4.0: every weapon the creator can show is a real item, so it can also be loot; Task 4.2b: each class's armor is an item on <c>slot-armor</c> painting
        /// the clothes layer, priority 10 so it beats a tunic worn in another slot). A pick whose item is already fixed gear (the short sword) adds nothing.
        /// Computed once per domain load; a changed picks file needs a recompile/reload to be seen.</summary>
        public static GearSpec[] Gear => gear ??= BuildGear();

        private static GearSpec[] BuildGear()
        {
            var all = new List<GearSpec>(FixedGear);
            if (!File.Exists(CreatorPicksPath)) return all.ToArray();

            var picks = CreatorPicks.Parse(File.ReadAllText(CreatorPicksPath));
            var known = new HashSet<string>(all.Select(g => g.ItemId));
            foreach (var pick in picks.weapons)
            {
                if (string.IsNullOrEmpty(pick.item) || !known.Add(pick.item)) continue;
                all.Add(new GearSpec
                {
                    ItemId = pick.item, ProfileId = "visual-" + pick.item.Substring("item-".Length), DisplayName = pick.name,
                    SlotId = "slot-mainhand", LayerSlot = "layer-weapon", CatalogShortName = "weapon", OptionId = pick.option,
                });
            }

            foreach (var pick in picks.armors)
            {
                if (string.IsNullOrEmpty(pick.item) || !known.Add(pick.item)) continue;
                all.Add(new GearSpec
                {
                    ItemId = pick.item, ProfileId = "visual-" + pick.item.Substring("item-".Length), DisplayName = pick.name,
                    SlotId = ArmorSlotId, LayerSlot = "layer-clothes", CatalogShortName = "clothes", OptionId = pick.option, Priority = ArmorPriority,
                });
            }

            return all.ToArray();
        }

        private static readonly GearSpec[] FixedGear =
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
        public static string GoldPath => $"{GearDir}/{CurrencyItemId}.asset";
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

            // Task 6.0: a stackable currency item -- nothing in BarrowsEnd sold/bought anything before the
            // Task 6.0 vendor proof, so none existed. Registered here (not a separate script) since Author()
            // overwrites the ruleset's whole extraContent list on every run -- anything registered elsewhere
            // would be silently wiped the next time this runs.
            var gold = LoadOrCreate<ItemDefinition>(GoldPath);
            SetField(gold, "id", CurrencyItemId);
            SetField(gold, "displayName", "Gold Piece");
            SetField(gold, "maxStackSize", 999);
            SetField(gold, "baseValue", 1f);
            Save(gold);
            content.Add(gold);

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

            // Task 6.1 (T6.1-D6): a real, usable melee ability -- unlike the debug abilities above (Any-relation,
            // 1000 range, purely dev tooling), this targets Enemy at true melee range with a real visibility
            // requirement. Line-of-sight/line-of-effect are deliberately left off (not "off by omission" --
            // a melee bite only ever resolves at 1.5-unit adjacency, where an obstruction check adds real risk
            // of getting BarrowsEnd's own PhysicsLineOfSightProvider obstruction mask wrong -- e.g. picking up
            // the attacker's/target's own collider as a false obstruction -- for no gameplay benefit at melee
            // range) for zero benefit this task actually needs.
            var actionResource = AssetDatabase.LoadAssetAtPath<CRPG.Actors.ResourceDefinition>(ActionPath)
                                 ?? throw new InvalidOperationException($"{ActionPath} is missing.");
            var ratBite = LoadOrCreate<AbilityDefinition>($"{AbilityDir}/{RatBiteAbilityId}.asset");
            SetField(ratBite, "id", RatBiteAbilityId);
            SetField(ratBite, "displayName", "Bite");
            SetField(ratBite, "tags", AbilityTag.Attack | AbilityTag.Hostile);
            SetField(ratBite, "costs", new List<AbilityCost> { ActionCost(actionResource) }); // Task 6.1: no per-turn resource pool exists yet for any combatant (party or hostile) -- matches the debug abilities' own "no turn loop refills a cost pool" reasoning; item 6's tactical-stack wiring is what would add real action-economy tracking, for every combatant at once, not just this one NPC's own definition.
            var ratTargeting = new TargetingDefinition();
            SetField(ratTargeting, "relation", TargetRelation.Enemy);
            SetField(ratTargeting, "range", 1.5f); // one grid cell's reach
            SetField(ratTargeting, "requireLineOfSight", false);
            SetField(ratTargeting, "requireLineOfEffect", false);
            SetField(ratTargeting, "requireVisibleTarget", true);
            SetField(ratBite, "targeting", ratTargeting);
            var ratDamage = new DamageEffect();
            SetField(ratDamage, "hitPointResource", hitPoints);
            SetField(ratDamage, "amount", new DiceExpression(1, 4, 0)); // SRD5's own Giant Rat bite, roughly (1d4-1); kept non-negative for simplicity
            SetField(ratBite, "effects", new List<AbilityEffect> { ratDamage });
            Save(ratBite);
            content.Add(ratBite);

            var strike = LoadOrCreate<AbilityDefinition>($"{AbilityDir}/{StrikeAbilityId}.asset");
            SetField(strike, "id", StrikeAbilityId);
            SetField(strike, "displayName", "Strike");
            SetField(strike, "tags", AbilityTag.Attack);
            SetField(strike, "costs", new List<AbilityCost> { ActionCost(actionResource) });
            var strikeTargeting = new TargetingDefinition();
            SetField(strikeTargeting, "relation", TargetRelation.Enemy);
            SetField(strikeTargeting, "range", 1.5f);
            SetField(strikeTargeting, "requireLineOfSight", false);
            SetField(strikeTargeting, "requireLineOfEffect", false);
            SetField(strikeTargeting, "requireVisibleTarget", true);
            SetField(strike, "targeting", strikeTargeting);
            var strikeDamage = new DamageEffect();
            SetField(strikeDamage, "hitPointResource", hitPoints);
            SetField(strikeDamage, "amount", new DiceExpression(1, 6, 0));
            SetField(strike, "effects", new List<AbilityEffect> { strikeDamage });
            Save(strike);
            content.Add(strike);

            var giantRat = LoadOrCreate<NpcDefinition>($"{NpcDir}/{GiantRatNpcId}.asset");
            var ratGrant = (object)new ResourceGrant();
            SetField(ratGrant, "resource", hitPoints);
            SetField(ratGrant, "maxAmount", 7); // SRD5's own Giant Rat: 7 HP
            SetField(giantRat, "id", GiantRatNpcId);
            SetField(giantRat, "displayName", "Giant Rat");
            SetField(giantRat, "faction", new DefinitionId(GiantRatFaction));
            SetField(giantRat, "level", 1);
            SetField(giantRat, "resources", new List<ResourceGrant> { (ResourceGrant)ratGrant });
            SetField(giantRat, "abilityIds", new List<DefinitionId> { new(RatBiteAbilityId) });
            SetField(giantRat, "visualPrefab", AssetDatabase.LoadAssetAtPath<GameObject>(BarrowsEndCharacterAuthor.SpriteCharacterPath)
                                                ?? throw new InvalidOperationException($"{BarrowsEndCharacterAuthor.SpriteCharacterPath} is missing -- run the character author first."));
            SetField(giantRat, "appearance", new List<AppearanceEntry> { new(new DefinitionId("layer-body"), new DefinitionId(GiantRatBodyOptionId)) });
            Save(giantRat);
            content.Add(giantRat);

            // Task 6.1 (T6.1-D4/fact 10): Act 1's own quest, registered here (not a separate script) for the same
            // reason the currency item is -- this method overwrites the ruleset's whole extraContent list on
            // every run, so anything registered elsewhere would be silently wiped the next time it runs. Must be
            // registered or a save containing this quest fails ContentValidator on a later load (fact 10's B8).
            var identifyScroll = LoadOrCreate<ItemDefinition>($"{GearDir}/{IdentifyScrollItemId}.asset");
            SetField(identifyScroll, "id", IdentifyScrollItemId);
            SetField(identifyScroll, "displayName", "Scroll of Identify");
            SetField(identifyScroll, "maxStackSize", 20);
            SetField(identifyScroll, "baseValue", 25f);
            Save(identifyScroll);
            content.Add(identifyScroll);

            var quest = LoadOrCreate<QuestDefinition>($"{NpcDir}/{GatheringStormQuestId}.asset");
            var objective = new QuestObjectiveDefinition();
            SetField(objective, "id", DefeatPalisadeRatsObjectiveId);
            SetField(objective, "displayName", "Clear the giant rats breaching the palisade");
            SetField(objective, "type", QuestObjectiveType.Kill);
            SetField(objective, "trackingKey", BarrowsEnd.Gate.PalisadeBreachTrigger.RatsDefeatedTimelineKey);
            SetField(objective, "targetCount", 1);
            SetField(quest, "id", GatheringStormQuestId);
            SetField(quest, "displayName", "The Gathering Storm");
            SetField(quest, "description", "Mayor Thaddeus has sent you to clear the giant rats breaching Barrow's End's palisade before the caverns beneath the town can be explored.");
            SetField(quest, "objectives", new List<QuestObjectiveDefinition> { objective });
            Save(quest);
            content.Add(quest);

            var movement = LoadOrCreate<CRPG.Actors.ResourceDefinition>("Assets/Content/Ruleset/resource-movement.asset");
            SetField(movement, "id", BarrowsEnd.Gate.TacticalCombatSession.MovementResourceId);
            SetField(movement, "displayName", "Movement");
            Save(movement);
            content.Add(movement);

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

        private static AbilityCost ActionCost(CRPG.Actors.ResourceDefinition action)
        {
            object cost = new AbilityCost();
            typeof(AbilityCost).GetField("resource", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(cost, action);
            typeof(AbilityCost).GetField("amount", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(cost, 1);
            return (AbilityCost)cost;
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

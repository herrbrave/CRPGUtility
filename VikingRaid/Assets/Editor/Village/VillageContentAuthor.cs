using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CRPG.Actors;
using CRPG.Core;
using UnityEditor;
using UnityEngine;
using VikingRaid.Game;

namespace VikingRaid.EditorTools
{
    /// <summary>
    /// Authors the village's NPC definitions (<c>Assets/Content/Village/Npcs</c>): flat, designer-authored character sheets for the named townsfolk, one per villager prefab. Re-runnable (assets
    /// are updated in place, so their GUIDs and every scene reference to them survive). They are deliberately NOT added to the Viking Raid ruleset's content list -- the location spawn
    /// points hold the definitions directly, as the toolkit's own locations do; <c>GameContentAuthor</c> rewrites that list on every run and would drop them.
    /// <para>Dialogue, quests and equipment are not set here: they are what the in-editor NPC tooling is for.</para>
    /// </summary>
    public static class VillageContentAuthor
    {
        public const string Folder = "Assets/Content/Village/Npcs";
        public const string VillagersFaction = "faction-villagers";

        public sealed class VillagerSpec
        {
            public string NpcId, DisplayName, Description;
            /// <summary>The look the NPC starts with (slot, option): written ONCE, when the NPC has none. After that the creator in the inspector owns it, and re-running the author never overwrites an edit.</summary>
            public (string Slot, string Option)[] Look = System.Array.Empty<(string, string)>();
            public int Hp = 4, Str = 10, Dex = 10, Con = 10, Int = 10, Wis = 10, Cha = 10;
        }

        public static readonly VillagerSpec[] Villagers =
        {
            new() { NpcId = "npc-village-elder", DisplayName = "Elder Hrolf", Hp = 9, Wis = 14, Cha = 13, Int = 12,
                    Description = "The village elder: grey-bearded, shrewd, and tired of raiders.", Look = new[] { ("body", "body-leader-male-01"), ("look", "look-04-a"), ("hair", "hair-viking-hair-04"), ("cloak", "cloak-viking-cape-02") } },
            new() { NpcId = "npc-village-hild", DisplayName = "Hild", Hp = 4, Dex = 12, Wis = 12,
                    Description = "A weaver who hears everything that is said at the well.", Look = new[] { ("body", "body-peasant-female-01"), ("look", "look-02-b"), ("hair", "hair-viking-hair-03"), ("gear", "gear-war-camp-bags-02") } },
            new() { NpcId = "npc-village-orm", DisplayName = "Orm", Hp = 5, Str = 12, Con = 12,
                    Description = "A fisherman with a bad knee and a good boat.", Look = new[] { ("body", "body-peasant-male-01"), ("look", "look-03-a"), ("headwear", "headwear-war-camp-hat-02"), ("gear", "gear-viking-pouch-02") } },
        };

        [MenuItem("Viking Raid/Village/Author Villager Content")]
        public static void Run()
        {
            EnsureFolder(Folder);
            var turnResources = new[] { GameDesign.Action, GameDesign.BonusAction, GameDesign.Reaction, GameDesign.Movement };
            foreach (var villager in Villagers) Author(villager, turnResources);
            AssetDatabase.SaveAssets();
            Debug.Log($"[VillageContentAuthor] authored {Villagers.Length} villager NPC definitions under {Folder}");
        }

        public static NpcDefinition Load(string npcId) => AssetDatabase.LoadAssetAtPath<NpcDefinition>($"{Folder}/{npcId}.asset");

        private static void Author(VillagerSpec v, string[] turnResources)
        {
            var path = $"{Folder}/{v.NpcId}.asset";
            var npc = AssetDatabase.LoadAssetAtPath<NpcDefinition>(path);
            if (npc == null) { npc = ScriptableObject.CreateInstance<NpcDefinition>(); AssetDatabase.CreateAsset(npc, path); }

            var basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterRosterAuthor.NpcBasePath);
            if (basePrefab == null) throw new InvalidOperationException($"{CharacterRosterAuthor.NpcBasePath} is missing: run 'Viking Raid/Village/Author Villager Characters' first.");

            SetField(npc, "id", v.NpcId);
            SetField(npc, "displayName", v.DisplayName);
            SetField(npc, "description", v.Description);
            SetField(npc, "faction", new DefinitionId(VillagersFaction));
            SetField(npc, "level", 1);
            SetField(npc, "abilityScores", new List<StatScoreEntry>
            {
                Score(GameDesign.Str, v.Str), Score(GameDesign.Dex, v.Dex), Score(GameDesign.Con, v.Con),
                Score(GameDesign.Int, v.Int), Score(GameDesign.Wis, v.Wis), Score(GameDesign.Cha, v.Cha),
            });

            // a villager owns the same resources a combatant does (Hit Points plus the four turn resources, which an encounter refills but never grants), so the first time one is
            // dragged into a fight it works without re-authoring
            var resources = new List<ResourceGrant> { Grant(GameDesign.Hp, v.Hp) };
            foreach (var id in turnResources) resources.Add(Grant(id, id == GameDesign.Movement ? 6 : 1));
            SetField(npc, "resources", resources);

            // the appearance creator owns the look: the prefab and the entries are only filled in when the NPC has none (an NPC from before the creator pointed at its own per-villager prefab)
            if (npc.VisualPrefab == null || AssetDatabase.GetAssetPath(npc.VisualPrefab).Contains("/Villager_")) SetField(npc, "visualPrefab", basePrefab);
            if (npc.Appearance.Count == 0) SetField(npc, "appearance", v.Look.Select(l => new AppearanceEntry(new DefinitionId(l.Slot), new DefinitionId(l.Option))).ToList());
            EditorUtility.SetDirty(npc);
        }

        private static StatScoreEntry Score(string statId, int score)
        {
            object boxed = new StatScoreEntry();
            SetField(boxed, "statId", new DefinitionId(statId));
            SetField(boxed, "score", score);
            return (StatScoreEntry)boxed;
        }

        private static ResourceGrant Grant(string resourceId, int max)
        {
            var definition = Find<CRPG.Actors.ResourceDefinition>(resourceId);
            if (definition == null) throw new InvalidOperationException($"resource '{resourceId}' was not found: run 'Viking Raid/Task 2/Author Content' first.");
            object boxed = new ResourceGrant();
            SetField(boxed, "resource", definition);
            SetField(boxed, "maxAmount", max);
            return (ResourceGrant)boxed;
        }

        private static T Find<T>(string id) where T : DefinitionAsset
        {
            foreach (var guid in AssetDatabase.FindAssets($"t:{typeof(T).Name}"))
            {
                var asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null && asset.Id.Value == id) return asset;
            }

            return null;
        }

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

        private static void EnsureFolder(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath)) return;
            var parts = assetPath.Split('/');
            var current = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}

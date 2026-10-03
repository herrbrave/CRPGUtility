using System.Collections.Generic;
using System.Linq;
using BarrowsEnd.Gate;
using BarrowsEnd.Shell;
using CRPG.Core;
using CRPG.Dialogue;
using CRPG.Gameplay.Interactions.Adapters;
using CRPG.Gameplay.Scenes;
using CRPG.Inventory;
using UnityEditor;
using UnityEngine;
using static BarrowsEnd.Editor.LocationAuthoringKit;

namespace BarrowsEnd.Editor
{
    /// <summary>Task 6.1 (task breakdown items 7-9): the real cast of Act 1 -- all 10 named handoff NPCs plus
    /// Garrick/Elara/Silas's own recruiting fixtures, placed just outside the building doors
    /// <c>BarrowsEndTownAuthor.cs</c> already carved (source fact 1's exact coordinates), each an
    /// <see cref="AddTalkPoint"/> fixture exactly like Task 6.0's own proof fixtures. Building occupancy (per the
    /// handoff doc + the master plan's own building-naming): Mayor's Hall -> Thaddeus; Shrine of Silas -> Silas
    /// (companion); The Iron Anvil -> Barnaby; Restful Hearth Inn &amp; Tavern -> Elida + Grumble + Garrick
    /// (companion, "found in the tavern" per the handoff); Fendrick's Apothecary -> Fendrick; Marjorie's General
    /// Store -> Marjorie; Guard Barracks -> Kael; Lyra's Arcanist Study -> Lyra; Silvan's Fletcher -> Silvan;
    /// Market Tents -> Elara (companion); Jenkins' bench -> Jenkins.</summary>
    public static class BarrowsEndAct1NpcAuthor
    {
        // Duplicated from BarrowsEndUiAuthor.GalleryPanelSettingsPath -- same cross-boundary convention
        // BarrowsEndTownAuthor.cs's own AuthorProofFixtures already established.
        private const string GalleryPanelSettingsPath = "Assets/Content/Ui/gallery-panel-settings.asset";

        public static void Author(GameObject root, LocationContent content)
        {
            var panelSettings = AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.PanelSettings>(GalleryPanelSettingsPath)
                ?? throw new System.InvalidOperationException($"{GalleryPanelSettingsPath} is missing -- author the UI content first.");

            AuthorMayorThaddeus(root, panelSettings);
            AuthorCompanion(root, panelSettings, "garrick", "Garrick", Cell(44, 17), Cell(44, 18)); // Restful Hearth, west of the door
            AuthorCompanion(root, panelSettings, "elara", "Elara", Cell(25, 31), Cell(25, 30)); // Market Tents
            AuthorCompanion(root, panelSettings, "silas", "Silas", Cell(21, 17), Cell(21, 18)); // Shrine of Silas

            AuthorElidaAndGrumble(root, panelSettings);
            AuthorBarnaby(root, content, panelSettings);
            AuthorFendrick(root, content, panelSettings);
            AuthorMarjorie(root, content, panelSettings);
            AuthorKael(root, panelSettings);
            AuthorLyra(root, content, panelSettings);
            AuthorSilvan(root, content, panelSettings);
            AuthorJenkins(root, panelSettings);
        }

        // ---------------------------------------------------------------------------------------------
        // Mayor Thaddeus -- the main quest, simplified per the master plan's D1 (no tempo_points/compound-AND).
        // ---------------------------------------------------------------------------------------------

        private static DialogueDefinition BuildMayorDialogue()
        {
            SerializableGuid Id(string seed) => SerializableGuid.FromString(seed);

            var end = new EndNode();
            SetField(end, "nodeId", Id("mayor-end"));

            var questGiven = new LineNode();
            SetField(questGiven, "nodeId", Id("mayor-quest-given"));
            SetField(questGiven, "speakerName", "Mayor Thaddeus");
            SetField(questGiven, "text", "Excellent. Head past the northern barricades. The guards have orders to let you through.");
            SetField(questGiven, "nextNodeId", Id("mayor-end"));

            var startQuest = new ActionNode();
            SetField(startQuest, "nodeId", Id("mayor-start-quest"));
            var startQuestAction = new StartQuestAction();
            SetField(startQuestAction, "questKey", BarrowsEndGearAuthor.GatheringStormQuestId);
            SetField(startQuest, "action", startQuestAction);
            SetField(startQuest, "nextNodeId", Id("mayor-quest-given"));

            var notReady = new LineNode();
            SetField(notReady, "nodeId", Id("mayor-not-ready"));
            SetField(notReady, "speakerName", "Mayor Thaddeus");
            SetField(notReady, "text", "Recruit the rest of your squad first. Garrick, Elara, and Silas are all still in town -- come back when you're ready.");
            SetField(notReady, "nextNodeId", Id("mayor-end"));

            // T6.1-D5/fact 9: a single CountAtLeast check on the shared "companion-recruited" key, not three
            // chained per-companion BranchNodes.
            var readyCheck = new BranchNode();
            SetField(readyCheck, "nodeId", Id("mayor-ready-check"));
            SetField(readyCheck, "timelineKey", "companion-recruited");
            SetField(readyCheck, "conditionType", TimelineConditionType.CountAtLeast);
            SetField(readyCheck, "threshold", 3d);
            SetField(readyCheck, "trueNodeId", Id("mayor-start-quest"));
            SetField(readyCheck, "falseNodeId", Id("mayor-not-ready"));

            var exposition = new LineNode();
            SetField(exposition, "nodeId", Id("mayor-exposition"));
            SetField(exposition, "speakerName", "Mayor Thaddeus");
            SetField(exposition, "text", "Goblins, mostly. But they are organized. They've built heavy barricades and seem to be guarding something deeper in the stone.");
            SetField(exposition, "nextNodeId", Id("mayor-choice"));

            var readyOption = new ChoiceOption();
            SetField(readyOption, "text", "We have our squad and our supplies. We are ready to descend.");
            SetField(readyOption, "nextNodeId", Id("mayor-ready-check"));

            var expositionOption = new ChoiceOption();
            SetField(expositionOption, "text", "What exactly is down there?");
            SetField(expositionOption, "nextNodeId", Id("mayor-exposition"));

            var choice = new ChoiceNode();
            SetField(choice, "nodeId", Id("mayor-choice"));
            SetField(choice, "options", new List<ChoiceOption> { readyOption, expositionOption });

            var briefing = new LineNode();
            SetField(briefing, "nodeId", Id("mayor-briefing"));
            SetField(briefing, "speakerName", "Mayor Thaddeus");
            SetField(briefing, "text", "I see the new mercenary squad has finally arrived. The caverns beneath Barrow's End are crawling with hostiles. Are you properly equipped to handle this, or are you just here to waste my time?");
            SetField(briefing, "nextNodeId", Id("mayor-choice"));

            // Task 6.3: once the necromancer is dead the briefing tree is moot -- Thaddeus congratulates instead. The start node is a
            // BranchNode on the boss's own won key (quest completion records no key of its own).
            var victoryLine = new LineNode();
            SetField(victoryLine, "nodeId", Id("mayor-victory-line"));
            SetField(victoryLine, "speakerName", "Mayor Thaddeus");
            SetField(victoryLine, "text", "The Deep Dark is quiet, and Barrow's End still stands. I did not think any squad could do it. The town owes you more than gold -- you will always have a bed at the Restful Hearth.");
            SetField(victoryLine, "nextNodeId", Id("mayor-end"));
            var victoryCheck = new BranchNode();
            SetField(victoryCheck, "nodeId", Id("mayor-victory-check"));
            SetField(victoryCheck, "timelineKey", BarrowsEndCavernsAuthor.NecromancerDefeatedKey);
            SetField(victoryCheck, "conditionType", TimelineConditionType.HasOccurred);
            SetField(victoryCheck, "trueNodeId", Id("mayor-victory-line"));
            SetField(victoryCheck, "falseNodeId", Id("mayor-briefing"));

            var dialogue = ScriptableObject.CreateInstance<DialogueDefinition>();
            SetField(dialogue, "id", "dlg-mayor-thaddeus");
            SetField(dialogue, "startNodeId", Id("mayor-victory-check"));
            SetField(dialogue, "nodes", new List<DialogueNode> { victoryCheck, victoryLine, briefing, choice, readyCheck, startQuest, questGiven, notReady, exposition, end });
            return dialogue;
        }

        /// <summary>Rewrites <c>dlg-mayor-thaddeus.asset</c> IN PLACE (same asset, same GUID): the baked Town scene references it, and
        /// <see cref="SaveDialogue"/> deletes and recreates, which would orphan the reference and silently make the Mayor unclickable.</summary>
        [MenuItem("Barrows End/Content/Update Mayor Dialogue In Place")]
        public static void AuthorMayorDialogueInPlace()
        {
            var fresh = BuildMayorDialogue();
            var path = $"{BarrowsEndGearAuthor.NpcDir}/dlg-mayor-thaddeus.asset";
            var existing = AssetDatabase.LoadAssetAtPath<DialogueDefinition>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(fresh, path);
            }
            else
            {
                fresh.name = existing.name;
                EditorUtility.CopySerialized(fresh, existing);
                EditorUtility.SetDirty(existing);
                Object.DestroyImmediate(fresh);
            }

            AssetDatabase.SaveAssets();
        }

        private static void AuthorMayorThaddeus(GameObject root, UnityEngine.UIElements.PanelSettings panelSettings)
        {
            var dialogue = BuildMayorDialogue();
            SaveDialogue(dialogue, "dlg-mayor-thaddeus");

            var (_, adapter) = AddTalkPoint(root, "MayorThaddeus", Cell(9, 17), Cell(9, 18), ArchSprite());
            SetField(adapter, "panelSettings", panelSettings);
            SetField(adapter, "dialogue", dialogue);
        }

        // ---------------------------------------------------------------------------------------------
        // Garrick / Elara / Silas -- recruiting. RecruitToPartyAction + a shared "companion-recruited" flag
        // (T6.1-D2/D5, fact 9's should-fix).
        // ---------------------------------------------------------------------------------------------

        private static void AuthorCompanion(GameObject root, UnityEngine.UIElements.PanelSettings panelSettings, string companionKey, string displayName, Vector2Int cell, Vector2Int approach)
        {
            SerializableGuid Id(string seed) => SerializableGuid.FromString(seed);
            var prefix = "companion-" + companionKey;

            var end = new EndNode();
            SetField(end, "nodeId", Id(prefix + "-end"));

            var greeting = new LineNode();
            SetField(greeting, "nodeId", Id(prefix + "-greeting"));
            SetField(greeting, "speakerName", displayName);
            SetField(greeting, "text", "Glad to fight alongside you. Lead the way.");
            SetField(greeting, "nextNodeId", Id(prefix + "-end"));

            var recordRecruited = new ActionNode();
            SetField(recordRecruited, "nodeId", Id(prefix + "-record"));
            var recordAction = new RecordEventAction();
            SetField(recordAction, "key", "companion-recruited");
            SetField(recordRecruited, "action", recordAction);
            SetField(recordRecruited, "nextNodeId", Id(prefix + "-greeting"));

            var recruit = new ActionNode();
            SetField(recruit, "nodeId", Id(prefix + "-recruit"));
            var recruitAction = new RecruitToPartyAction();
            SetField(recruitAction, "companionKey", companionKey);
            SetField(recruit, "action", recruitAction);
            SetField(recruit, "nextNodeId", Id(prefix + "-record"));

            var dialogue = ScriptableObject.CreateInstance<DialogueDefinition>();
            SetField(dialogue, "id", "dlg-" + prefix);
            SetField(dialogue, "startNodeId", Id(prefix + "-recruit"));
            SetField(dialogue, "nodes", new List<DialogueNode> { recruit, recordRecruited, greeting, end });
            SaveDialogue(dialogue, "dlg-" + prefix);

            // T6.1-D2: named exactly CompanionRoster.FixtureName(companionKey) -- ShellBootstrap looks this
            // fixture up by that same name both to read the recruit's spawn-position fallback and to hide it
            // once recruited.
            var (_, adapter) = AddTalkPoint(root, CompanionRoster.FixtureName(companionKey), cell, approach, ArchSprite());
            SetField(adapter, "panelSettings", panelSettings);
            SetField(adapter, "dialogue", dialogue);
        }

        // ---------------------------------------------------------------------------------------------
        // Elida (rest/save -- trivial reuse of Task 6.0's own proven pattern) + Grumble (side-quest teaser only).
        // ---------------------------------------------------------------------------------------------

        private static void AuthorElidaAndGrumble(GameObject root, UnityEngine.UIElements.PanelSettings panelSettings)
        {
            SerializableGuid Id(string seed) => SerializableGuid.FromString(seed);

            var elidaEnd = new EndNode();
            SetField(elidaEnd, "nodeId", Id("elida-end"));
            var elidaGreeting = new LineNode();
            SetField(elidaGreeting, "nodeId", Id("elida-greeting"));
            SetField(elidaGreeting, "speakerName", "Elida");
            SetField(elidaGreeting, "text", "There now -- rested and saved. The Restful Hearth is always open to you.");
            SetField(elidaGreeting, "nextNodeId", Id("elida-end"));
            var elidaRest = new ActionNode();
            SetField(elidaRest, "nodeId", Id("elida-rest"));
            SetField(elidaRest, "action", new RequestLongRestAction());
            SetField(elidaRest, "nextNodeId", Id("elida-save"));
            var elidaSave = new ActionNode();
            SetField(elidaSave, "nodeId", Id("elida-save"));
            SetField(elidaSave, "action", new RequestSaveAction());
            SetField(elidaSave, "nextNodeId", Id("elida-greeting"));
            var elidaDialogue = ScriptableObject.CreateInstance<DialogueDefinition>();
            SetField(elidaDialogue, "id", "dlg-elida");
            SetField(elidaDialogue, "startNodeId", Id("elida-rest"));
            SetField(elidaDialogue, "nodes", new List<DialogueNode> { elidaRest, elidaSave, elidaGreeting, elidaEnd });
            SaveDialogue(elidaDialogue, "dlg-elida");
            // Elida shares the Restful Hearth's own door area with Grumble and Garrick -- spread across X
            // (Garrick at 44, Elida at the door's own 45, Grumble at 46), all one cell south of the door row,
            // toward the square.
            var (_, elidaAdapter) = AddTalkPoint(root, "Elida", Cell(45, 17), Cell(45, 18), ArchSprite());
            SetField(elidaAdapter, "panelSettings", panelSettings);
            SetField(elidaAdapter, "dialogue", elidaDialogue);

            var grumbleDialogue = BuildGrumbleDialogue();
            SaveDialogue(grumbleDialogue, "dlg-grumble");
            var (_, grumbleAdapter) = AddTalkPoint(root, "Grumble", Cell(46, 17), Cell(46, 18), ArchSprite());
            SetField(grumbleAdapter, "panelSettings", panelSettings);
            SetField(grumbleAdapter, "dialogue", grumbleDialogue);
        }

        /// <summary>Task 6.2: Grumble's mushroom side quest. Branch order matters (the review): finished -> accepted -> not
        /// accepted, so three mushrooms picked before ever accepting still go through the accept step first.</summary>
        public static DialogueDefinition BuildGrumbleDialogue()
        {
            SerializableGuid Id(string seed) => SerializableGuid.FromString(seed);
            const string Speaker = "Grumble";

            LineNode Line(string id, string text, string next)
            {
                var line = new LineNode();
                SetField(line, "nodeId", Id(id));
                SetField(line, "speakerName", Speaker);
                SetField(line, "text", text);
                SetField(line, "nextNodeId", Id(next));
                return line;
            }

            ActionNode Act(string id, DialogueAction action, string next)
            {
                var node = new ActionNode();
                SetField(node, "nodeId", Id(id));
                SetField(node, "action", action);
                SetField(node, "nextNodeId", Id(next));
                return node;
            }

            DialogueAction Record(string key)
            {
                var action = new RecordEventAction();
                SetField(action, "key", key);
                return action;
            }

            DialogueAction Give(string assetPath, int quantity)
            {
                var action = new GiveItemAction();
                SetField(action, "item", AssetDatabase.LoadAssetAtPath<ItemDefinition>(assetPath) ?? throw new System.InvalidOperationException($"{assetPath} is missing."));
                SetField(action, "quantity", quantity);
                return action;
            }

            BranchNode Branch(string id, string key, TimelineConditionType type, double threshold, string whenTrue, string whenFalse)
            {
                var branch = new BranchNode();
                SetField(branch, "nodeId", Id(id));
                SetField(branch, "timelineKey", key);
                SetField(branch, "conditionType", type);
                SetField(branch, "threshold", threshold);
                SetField(branch, "trueNodeId", Id(whenTrue));
                SetField(branch, "falseNodeId", Id(whenFalse));
                return branch;
            }

            var startQuest = new StartQuestAction();
            SetField(startQuest, "questKey", BarrowsEndGearAuthor.GrumbleQuestId);

            var end = new EndNode();
            SetField(end, "nodeId", Id("grumble-end"));

            var nodes = new List<DialogueNode>
            {
                Branch("grumble-done-check", BarrowsEndGearAuthor.GrumbleQuestCompleteKey, TimelineConditionType.HasOccurred, 0, "grumble-done-line", "grumble-accepted-check"),
                Line("grumble-done-line", "Best mushroom stew in the valley, thanks to you. Eat up -- the Hearth is always open.", "grumble-end"),
                Branch("grumble-accepted-check", "grumble-quest-accepted", TimelineConditionType.HasOccurred, 0, "grumble-enough-check", "grumble-offer-check"),
                Branch("grumble-enough-check", HarvestInteractionAdapter.SharedKey, TimelineConditionType.CountAtLeast, 3, "grumble-turnin-line", "grumble-reminder-line"),
                Line("grumble-reminder-line", "Three giant mushrooms, that's all I ask. They grow down in the caverns -- the mine tracks and around the water.", "grumble-end"),
                Line("grumble-turnin-line", "Now THAT is a mushroom! Here, take this for your trouble -- and a little something to patch yourselves up.", "grumble-turnin-gold"),
                Act("grumble-turnin-gold", Give(BarrowsEndGearAuthor.GoldPath, 50), "grumble-turnin-potion"),
                Act("grumble-turnin-potion", Give("Packages/com.crpgtoolkit.core/Rules/SRD5e2014/Content/ItemDefinition_PotionofHealing.asset", 1), "grumble-turnin-record"),
                Act("grumble-turnin-record", Record(BarrowsEndGearAuthor.GrumbleQuestCompleteKey), "grumble-end"),
                Branch("grumble-offer-check", EncounterTrigger.PalisadeWonKey, TimelineConditionType.HasOccurred, 0, "grumble-offer-line", "grumble-early-line"),
                Line("grumble-early-line", "You look like the sort who could brave the caverns for me. Giant mushrooms down there, real delicacies -- ask me again once you've made some progress.", "grumble-end"),
                Line("grumble-offer-line", "You made it past the rats -- good. Bring me three giant mushrooms from down in the caverns and I'll make it worth your while.", "grumble-accept-record"),
                Act("grumble-accept-record", Record("grumble-quest-accepted"), "grumble-accept-quest"),
                Act("grumble-accept-quest", startQuest, "grumble-accept-line"),
                Line("grumble-accept-line", "Mind the goblins. Mushrooms don't taste right with an arrow in them.", "grumble-end"),
                end,
            };

            var dialogue = ScriptableObject.CreateInstance<DialogueDefinition>();
            SetField(dialogue, "id", "dlg-grumble");
            SetField(dialogue, "startNodeId", Id("grumble-done-check"));
            SetField(dialogue, "nodes", nodes);
            return dialogue;
        }

        /// <summary>Rewrites <c>dlg-grumble.asset</c> IN PLACE (same asset, same GUID) so the already-baked Town scene
        /// that references it keeps working -- <see cref="SaveDialogue"/> deletes and recreates, which would orphan the
        /// reference until the whole Town is re-authored.</summary>
        [MenuItem("Barrows End/Content/Update Grumble Dialogue In Place")]
        public static void AuthorGrumbleDialogueInPlace()
        {
            var fresh = BuildGrumbleDialogue();
            var path = $"{BarrowsEndGearAuthor.NpcDir}/dlg-grumble.asset";
            var existing = AssetDatabase.LoadAssetAtPath<DialogueDefinition>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(fresh, path);
            }
            else
            {
                fresh.name = existing.name;
                EditorUtility.CopySerialized(fresh, existing);
                EditorUtility.SetDirty(existing);
                Object.DestroyImmediate(fresh);
            }

            AssetDatabase.SaveAssets();
        }

        // ---------------------------------------------------------------------------------------------
        // Vendors (T6.1-D7): Barnaby/Marjorie stock BarrowsEnd's own items; Fendrick/Silvan stock the confirmed-
        // resolving SRD5e2014 items directly (fact 13, no new authoring); Lyra needs the one new scroll item.
        // ---------------------------------------------------------------------------------------------

        private static void AuthorBarnaby(GameObject root, LocationContent content, UnityEngine.UIElements.PanelSettings panelSettings)
        {
            var items = new (string id, int qty)[]
            {
                ("item-short-sword", 5), ("item-round-shield", 5), ("item-iron-helm", 5),
                ("item-dagger", 5), ("item-mace", 5),
            };
            AuthorVendor(root, content, panelSettings, "Barnaby", Cell(33, 17), Cell(33, 18),
                "The Iron Anvil doesn't run on wishes. Take a look, tell me what you need.", "store-barnaby", items);
        }

        private static void AuthorFendrick(GameObject root, LocationContent content, UnityEngine.UIElements.PanelSettings panelSettings)
        {
            var items = new (string id, int qty)[] { ("item-healing-potion", 10) };
            AuthorVendor(root, content, panelSettings, "Fendrick", Cell(57, 17), Cell(57, 18),
                "Potions, salves, the odd antitoxin. Don't drink anything you can't identify first.", "store-fendrick", items);
        }

        private static void AuthorMarjorie(GameObject root, LocationContent content, UnityEngine.UIElements.PanelSettings panelSettings)
        {
            var items = new (string id, int qty)[] { ("item-tunic", 10) };
            AuthorVendor(root, content, panelSettings, "Marjorie", Cell(9, 32), Cell(9, 31),
                "Rope, packs, the basics every adventurer forgets until they need it.", "store-marjorie", items);
        }

        private static void AuthorLyra(GameObject root, LocationContent content, UnityEngine.UIElements.PanelSettings panelSettings)
        {
            var items = new (string id, int qty)[] { (BarrowsEndGearAuthor.IdentifyScrollItemId, 10) };
            AuthorVendor(root, content, panelSettings, "Lyra", Cell(45, 32), Cell(45, 31),
                "Scrolls, tomes, the occasional dangerous secret. Handle everything on the third shelf carefully.", "store-lyra", items);
        }

        private static void AuthorSilvan(GameObject root, LocationContent content, UnityEngine.UIElements.PanelSettings panelSettings)
        {
            var items = new (string id, int qty)[] { ("item-shortbow", 5), ("item-longbow", 3) };
            AuthorVendor(root, content, panelSettings, "Silvan", Cell(57, 32), Cell(57, 31),
                "Fletcher and bowyer. String's fresh, fletching's true -- take your pick.", "store-silvan", items);
        }

        private static void AuthorVendor(GameObject root, LocationContent content, UnityEngine.UIElements.PanelSettings panelSettings,
            string npcName, Vector2Int cell, Vector2Int approach, string greetingText, string storeId, (string id, int qty)[] items)
        {
            SerializableGuid Id(string seed) => SerializableGuid.FromString(seed);
            var prefix = storeId;

            var greeting = new LineNode();
            SetField(greeting, "nodeId", Id(prefix + "-greeting"));
            SetField(greeting, "speakerName", npcName);
            SetField(greeting, "text", greetingText);
            SetField(greeting, "nextNodeId", Id(prefix + "-open"));

            var end = new EndNode();
            SetField(end, "nodeId", Id(prefix + "-end"));

            var openStore = new ActionNode();
            SetField(openStore, "nodeId", Id(prefix + "-open"));
            SetField(openStore, "action", new OpenStoreAction());
            SetField(openStore, "nextNodeId", Id(prefix + "-end"));

            var dialogue = ScriptableObject.CreateInstance<DialogueDefinition>();
            SetField(dialogue, "id", "dlg-" + prefix);
            SetField(dialogue, "startNodeId", Id(prefix + "-greeting"));
            SetField(dialogue, "nodes", new List<DialogueNode> { greeting, openStore, end });
            SaveDialogue(dialogue, "dlg-" + prefix);

            var (_, adapter) = AddTalkPoint(root, npcName, cell, approach, ArchSprite());
            SetField(adapter, "panelSettings", panelSettings);
            SetField(adapter, "dialogue", dialogue);

            var resolvedItems = items.Select(spec => (item: ResolveItemAsset(spec.id), qty: spec.qty)).ToArray();
            AddStoreAppending(content, storeId, adapter, resolvedItems);
        }

        // SRD5e2014 package path -- Fendrick's/Silvan's items (fact 13: confirmed already-resolving through the
        // real session at runtime, but the EDITOR-time asset the StoreListingSeed's own SerializedObject
        // reference needs still lives at its real package path, not BarrowsEnd's own Content/Gear folder.
        private const string Srd5ContentDir = "Packages/com.crpgtoolkit.core/Rules/SRD5e2014/Content";

        private static readonly Dictionary<string, string> Srd5ItemAssetNames = new()
        {
            ["item-healing-potion"] = "ItemDefinition_PotionofHealing",
            ["item-shortbow"] = "ItemDefinition_Shortbow",
            ["item-longbow"] = "ItemDefinition_Longbow",
            ["item-dagger"] = "ItemDefinition_Dagger",
            ["item-mace"] = "ItemDefinition_Mace",
        };

        private static ItemDefinition ResolveItemAsset(string id)
        {
            var path = Srd5ItemAssetNames.TryGetValue(id, out var assetName)
                ? $"{Srd5ContentDir}/{assetName}.asset"
                : $"{BarrowsEndGearAuthor.GearDir}/{id}.asset";
            return AssetDatabase.LoadAssetAtPath<ItemDefinition>(path)
                ?? throw new System.InvalidOperationException($"{id} is missing at {path} -- author gear first.");
        }

        // Appends one more store to LocationContent's own private `stores` list -- Task 6.0's own inline
        // registration OVERWROTE the whole array (arraySize = 1), safe for its single proof fixture but not for
        // this task's 5 real vendors sharing the same LocationContent.
        private static void AddStoreAppending(LocationContent content, string storeId, TalkInteractionAdapter adapter, (ItemDefinition item, int qty)[] listings)
        {
            var so = new SerializedObject(content);
            var storesProp = so.FindProperty("stores");
            var index = storesProp.arraySize;
            storesProp.arraySize = index + 1;
            var storeElement = storesProp.GetArrayElementAtIndex(index);
            storeElement.FindPropertyRelative("StoreId").stringValue = storeId;
            storeElement.FindPropertyRelative("Adapter").objectReferenceValue = adapter;
            var listingsProp = storeElement.FindPropertyRelative("InitialListings");
            listingsProp.arraySize = listings.Length;
            for (var i = 0; i < listings.Length; i++)
            {
                var listingElement = listingsProp.GetArrayElementAtIndex(i);
                listingElement.FindPropertyRelative("Item").objectReferenceValue = listings[i].item;
                listingElement.FindPropertyRelative("Quantity").intValue = listings[i].qty;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---------------------------------------------------------------------------------------------
        // Kael / Jenkins -- flavor-only, no store (T6.1-D7).
        // ---------------------------------------------------------------------------------------------

        private static void AuthorKael(GameObject root, UnityEngine.UIElements.PanelSettings panelSettings)
        {
            AuthorFlavorNpc(root, panelSettings, "Kael", Cell(21, 32), Cell(21, 31),
                "Captain Kael, Barrow's End Guard. Report anything strange near the palisade -- we've had rats breaching the fencing all week.");
        }

        private static void AuthorJenkins(GameObject root, UnityEngine.UIElements.PanelSettings panelSettings)
        {
            AuthorFlavorNpc(root, panelSettings, "Jenkins", Cell(31, 24), Cell(32, 24),
                "Old Man Jenkins here. Sit a while -- I know every rumor worth knowing in Barrow's End, and most that aren't.");
        }

        private static void AuthorFlavorNpc(GameObject root, UnityEngine.UIElements.PanelSettings panelSettings, string npcName, Vector2Int cell, Vector2Int approach, string text)
        {
            SerializableGuid Id(string seed) => SerializableGuid.FromString(seed);
            var prefix = "flavor-" + npcName.ToLowerInvariant();

            var line = new LineNode();
            SetField(line, "nodeId", Id(prefix + "-line"));
            SetField(line, "speakerName", npcName);
            SetField(line, "text", text);
            SetField(line, "nextNodeId", Id(prefix + "-end"));

            var end = new EndNode();
            SetField(end, "nodeId", Id(prefix + "-end"));

            var dialogue = ScriptableObject.CreateInstance<DialogueDefinition>();
            SetField(dialogue, "id", "dlg-" + prefix);
            SetField(dialogue, "startNodeId", Id(prefix + "-line"));
            SetField(dialogue, "nodes", new List<DialogueNode> { line, end });
            SaveDialogue(dialogue, "dlg-" + prefix);

            var (_, adapter) = AddTalkPoint(root, npcName, cell, approach, ArchSprite());
            SetField(adapter, "panelSettings", panelSettings);
            SetField(adapter, "dialogue", dialogue);
        }

        // ---------------------------------------------------------------------------------------------
        // Shared save helper -- same delete-then-recreate idiom BarrowsEndTownAuthor.BuildTalkerDialogue uses.
        // ---------------------------------------------------------------------------------------------

        private static void SaveDialogue(DialogueDefinition dialogue, string fileName)
        {
            var path = $"{BarrowsEndGearAuthor.NpcDir}/{fileName}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Object>(path);
            if (existing != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(dialogue, path);
        }
    }
}

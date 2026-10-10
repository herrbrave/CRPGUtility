using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CRPG.Core;
using CRPG.Dialogue;
using CRPG.Gameplay.Scenes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using VikingRaid.Village;

namespace VikingRaid.EditorTools
{
    /// <summary>
    /// Gives the three demo villagers something to say (design <c>Docs/Plans/Toolkit-NpcDialogue.md</c>, Section 5): a greeting and a couple of questions each, a dialogue asset beside the NPC
    /// (<c>dlg-&lt;spawn id&gt;</c>, the toolkit's own naming), assigned to the spawn point; and wires the village's dialogue panel settings. Idempotent: a villager whose spawn point already has a dialogue is
    /// left alone, so dialogue written by hand in the graph window is never overwritten. The one exception is a dialogue asset that exists but has no nodes (the empty stub the Place NPC panel creates):
    /// that one is filled in place, keeping its asset and id.
    /// </summary>
    public static class VillageDialogueAuthor
    {
        public const string ScenePath = "Assets/Scenes/VikingVillage.unity";
        public const string PanelSettingsPath = "Assets/Scenes/VikingRaid/HudPanelSettings.asset";
        public const string Folder = "Assets/Content/Village/Npcs";

        [MenuItem("Viking Raid/Village/3. Author NPC Dialogues")]
        public static void Run()
        {
            foreach (var line in Author()) Debug.Log("[VillageDialogueAuthor] " + line);
        }

        public static List<string> Author()
        {
            var report = new List<string>();
            VillageContentAuthor.Run();   // idempotent: authors the villager definitions (Ragna is new) without touching looks that were edited in the inspector
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var location = Object.FindFirstObjectByType<LocationContent>();
            var bootstrap = Object.FindFirstObjectByType<VillageBootstrap>();
            if (location == null || bootstrap == null) { report.Add("ERROR: the village scene has no LocationContent or VillageBootstrap: run '1. Author Village Scene' first."); return report; }

            // after OpenScene: opening a scene unloads assets nothing references (see VillageRoutineAuthor)
            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (panel == null) report.Add($"ERROR: {PanelSettingsPath} not found: the village has no panel to show conversations in.");
            else
            {
                var so = new SerializedObject(bootstrap);
                so.FindProperty("dialoguePanelSettings").objectReferenceValue = panel;
                so.ApplyModifiedProperties();
                report.Add("dialogue panel settings wired");
            }

            EnsureRagna(location, report);
            AuthorQuest(report);
            Give(location, "village-orm", report, OrmGraph, 5);
            Give(location, "village-hild", report, HildGraph, 4);
            Give(location, "village-elder", report, ElderGraph, 4);
            Give(location, "village-ragna", report, RagnaGraph, 0);
            GiveHildAShop(location, report);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            report.Add("scene saved");
            return report;
        }

        /// <summary>The version of the dialogue scripts below. Each run (re)writes a dialogue this author wrote earlier at a LOWER version; a dialogue written by hand (a different shape, no author label) is never touched.</summary>
        public const int Version = 5;

        /// <summary>What a bed at Hild's costs, in gold. One constant for the price check, the payment and the line that names it.</summary>
        public const int InnPrice = 5;
        private const string LabelPrefix = "villageDialogue-v";

        // True when the asset is empty, carries an older version label, or has no label but still has exactly the shape of the first version (so it is certainly the one this author wrote).
        private static bool MayRewrite(DialogueDefinition dialogue, int firstVersionNodeCount)
        {
            if (dialogue == null || dialogue.Nodes.Count == 0) return true;
            var labels = AssetDatabase.GetLabels(dialogue).Where(l => l.StartsWith(LabelPrefix)).ToList();
            if (labels.Count == 0) return dialogue.Nodes.Count == firstVersionNodeCount;
            return labels.All(l => int.TryParse(l.Substring(LabelPrefix.Length), out var n) && n < Version);
        }

        private static void Give(LocationContent location, string spawnId, List<string> report, System.Func<(string start, List<DialogueNode> nodes)> graph, int firstVersionNodeCount)
        {
            var index = location.SpawnPoints.ToList().FindIndex(p => p.SpawnId == spawnId);
            if (index < 0) { report.Add($"{spawnId}: no such spawn point; skipped."); return; }

            var point = location.SpawnPoints[index];
            if (point.Dialogue != null && !MayRewrite(point.Dialogue, firstVersionNodeCount)) { report.Add($"{spawnId}: already has a dialogue ('{point.Dialogue.name}'); kept."); return; }

            var path = $"{Folder}/dlg-{spawnId}.asset";
            var dialogue = point.Dialogue != null ? point.Dialogue : AssetDatabase.LoadAssetAtPath<DialogueDefinition>(path);
            if (dialogue == null)
            {
                dialogue = ScriptableObject.CreateInstance<DialogueDefinition>();
                Set(dialogue, "id", $"dlg-{spawnId}");
                AssetDatabase.CreateAsset(dialogue, path);
            }

            var (start, nodes) = graph();
            Set(dialogue, "displayName", point.Definition != null ? point.Definition.DisplayName : spawnId);
            Set(dialogue, "startNodeId", Id(start));
            Set(dialogue, "nodes", nodes);
            EditorUtility.SetDirty(dialogue);
            AssetDatabase.SetLabels(dialogue, AssetDatabase.GetLabels(dialogue).Where(l => !l.StartsWith(LabelPrefix)).Append(LabelPrefix + Version).ToArray());
            AssetDatabase.SaveAssets();

            var so = new SerializedObject(location);
            so.FindProperty("spawnPoints").GetArrayElementAtIndex(index).FindPropertyRelative("Dialogue").objectReferenceValue = dialogue;
            so.ApplyModifiedProperties();
            report.Add($"{spawnId}: dialogue '{dialogue.name}' ({nodes.Count} nodes)");
        }

        // ---------------------------------------------------------------- the scripts

        // The errand: Orm asks the party to tell Hild the salt fish has come in. The keys are authored Timeline events (the pattern Barrow's End uses): accepted -> told Hild -> reported back. The quest's objectives
        // count the last two SINCE the quest started; the dialogues branch on all three.
        public const string QuestId = "quest-village-sacks";
        public const string AcceptedKey = "orm-sacks-accepted", ToldHildKey = "hild-told-fish-arrived", DoneKey = "orm-sacks-done";

        private static (string, List<DialogueNode>) OrmGraph() => Graph("start", new DialogueNode[]
        {
            TimelineBranch("start", DoneKey, "thanks", "b-told"),
            Line("thanks", "Orm", "Good folk. The fish is stowed and Hild is happy. The dock owes you.", "end"),
            TimelineBranch("b-told", ToldHildKey, "report", "b-acc"),
            Line("report", "Orm", "She said that? Then the fish is as good as sold. Here, for your trouble.", "rec-done"),
            Action("rec-done", Record(DoneKey), "paid"),
            Line("paid", "Orm", "(Orm presses some coins into your hand.)", "end"),
            TimelineBranch("b-acc", AcceptedKey, "waiting", "greet"),
            Line("waiting", "Orm", "Well? Go on, tell Hild the salt fish has come in.", "end"),
            Line("greet", "Orm", "Mind the planks, stranger. The boats are late again and this cart will not load itself.", "ask"),
            ChoiceOf("ask", Opt("What are you hauling?", "cargo"), Opt("Do you need anything?", "need"), Opt("Farewell.", "end")),
            Line("cargo", "Orm", "Salt fish, mostly. It smells worse than it weighs, and it weighs plenty.", "end"),
            Line("need", "Orm", "Strong backs, and an errand-runner. Hild at the alehouse wants the salt fish and does not know it has come in. Tell her, and I will make it worth your while.", "offer"),
            ChoiceOf("offer", Opt("I will tell her.", "rec-acc"), Opt("Not now.", "end")),
            Action("rec-acc", Record(AcceptedKey), "start-q"),
            Action("start-q", StartQuest(QuestId), "go"),
            Line("go", "Orm", "Good. Off you go.", "end"),
            End("end"),
        });

        private static (string, List<DialogueNode>) HildGraph() => Graph("greet", new DialogueNode[]
        {
            Line("greet", "Hild", "Fine weather for it. The fjord is calm and the mead is cold.", "ask"),
            ChoiceOf("ask", Opt("Let me see your wares.", "shop"), Opt($"I need a bed ({InnPrice} gold).", "check"), Opt("Anything happening in the village?", "news"),
                Opt("Orm sent me. The salt fish has come in.", "fish", visibleIfKey: AcceptedKey), Opt("Farewell.", "end")),
            Action("fish", Record(ToldHildKey), "fish-line"),
            Line("fish-line", "Hild", "The salt fish already! Tell Orm I will take every barrel.", "more"),
            Action("shop", new OpenStoreAction(), "more"),
            ItemBranch("check", GoldItem(), InnPrice, "pay", "poor"),
            Action("pay", Spend(GoldItem(), InnPrice), "rest"),
            Action("rest", new RequestLongRestAction(), "slept"),
            Line("slept", "Hild", "There. A warm bed and a full night. You look new made.", "more"),
            Line("poor", "Hild", $"{InnPrice} gold for a bed, friend. Come back when your purse agrees.", "more"),
            Line("more", "Hild", "Anything else, then?", "ask"),
            Line("news", "Hild", "Only the elder complaining about his knees. Ask him yourself, he loves an audience.", "more"),
            End("end"),
        });

        private static CRPG.Inventory.ItemDefinition GoldItem() =>
            AssetDatabase.FindAssets("t:ItemDefinition").Select(g => AssetDatabase.LoadAssetAtPath<CRPG.Inventory.ItemDefinition>(AssetDatabase.GUIDToAssetPath(g))).FirstOrDefault(i => i != null && i.Id.Value == "item-gold-piece");

        private static BranchNode ItemBranch(string id, CRPG.Inventory.ItemDefinition item, int quantity, string whenTrue, string whenFalse)
        {
            var node = new BranchNode();
            Set(node, "nodeId", Id(id));
            Set(node, "source", BranchSource.PartyItemCount);
            Set(node, "item", item);
            Set(node, "quantity", quantity);
            Set(node, "trueNodeId", Id(whenTrue));
            Set(node, "falseNodeId", Id(whenFalse));
            return node;
        }

        private static SpendItemAction Spend(CRPG.Inventory.ItemDefinition item, int quantity)
        {
            var action = new SpendItemAction();
            Set(action, "item", item);
            Set(action, "quantity", quantity);
            return action;
        }

        private static (string, List<DialogueNode>) RagnaGraph() => Graph("greet", new DialogueNode[]
        {
            Line("greet", "Ragna", "You are the ones for the ships? I can handle an oar as well as a spear. Take me along.", "ask"),
            ChoiceOf("ask", Opt("Join us.", "join"), Opt("Not yet.", "end")),
            Action("join", Recruit("follow"), "welcome"),
            Line("welcome", "Ragna", "Lead on. I will keep up.", "end"),
            End("end"),
        });

        private static RecruitToPartyAction Recruit(string key)
        {
            var action = new RecruitToPartyAction();
            Set(action, "companionKey", key);
            return action;
        }

        // Ragna stands near the party's start, wandering a little, until the party takes her along. A person has to be able to CLICK her, and the village is hilly: a spot below a rise is hidden from the camera
        // (the click lands on the grass in front of her and sends the party walking). So the spot is chosen for visibility: the one on walkable ground, reachable from the start, with the most clear lines to a camera
        // looking down from eight directions. Placed once; if she is later found buried, re-running this moves her to a better spot.
        private static void EnsureRagna(LocationContent location, List<string> report)
        {
            var npc = VillageContentAuthor.Load("npc-village-ragna");
            var start = GameObject.Find("PartyStart");
            if (npc == null || start == null) { report.Add("WARNING: Ragna's definition or the party start is missing; she was not placed."); return; }

            var index = location.SpawnPoints.ToList().FindIndex(p => p.SpawnId == "village-ragna");
            if (index >= 0)
            {
                var anchor = location.SpawnPoints[index].Anchor;
                if (anchor != null && !EditorUtility.IsPersistent(anchor) && ClearDirections(anchor.position) < 8)
                {
                    var better = BestVisibleSpot(start.transform);
                    if (better.HasValue) { anchor.position = better.Value; report.Add($"village-ragna was hidden from the camera: moved to {better.Value}"); }
                }

                return;
            }

            var spot = BestVisibleSpot(start.transform);
            if (!spot.HasValue) { report.Add("WARNING: no open, reachable, visible ground near the party start for Ragna; she was not placed."); return; }

            CRPG.Editor.Location.LocationPlacement.AddSpawn(location, npc, spot.Value, Quaternion.LookRotation(-start.transform.forward), "village-ragna");
            index = location.SpawnPoints.ToList().FindIndex(p => p.SpawnId == "village-ragna");
            var so = new SerializedObject(location);
            var routine = so.FindProperty("spawnPoints").GetArrayElementAtIndex(index).FindPropertyRelative("Routine");
            routine.FindPropertyRelative("Kind").enumValueIndex = (int)CRPG.Gameplay.Ambient.NpcRoutineKind.Wander;
            routine.FindPropertyRelative("AreaRadius").floatValue = 3f;
            routine.FindPropertyRelative("PauseMin").floatValue = 3f;
            routine.FindPropertyRelative("PauseMax").floatValue = 8f;
            so.ApplyModifiedProperties();
            report.Add($"village-ragna placed at {spot.Value}");
        }

        // How many of eight camera directions (45 degree steps, pitch 55, 40 m out) can see a standing person at this point: nothing solid on the click layers in between.
        private static int ClearDirections(Vector3 foot)
        {
            Physics.SyncTransforms();
            var head = foot + Vector3.up * 1.2f;
            var clear = 0;
            for (var k = 0; k < 8; k++)
            {
                var direction = Quaternion.Euler(0f, k * 45f, 0f) * (Quaternion.Euler(55f, 0f, 0f) * Vector3.back);   // from the person toward a camera above and behind
                direction = new Vector3(direction.x, Mathf.Abs(direction.y), direction.z).normalized;
                var camera = head + direction * 40f;
                if (!Physics.Linecast(camera, head, out var hit, VikingRaid.Game.WorldLayers.Hittable, QueryTriggerInteraction.Ignore) || hit.distance > 38.8f) clear++;
            }

            return clear;
        }

        private static Vector3? BestVisibleSpot(Transform start)
        {
            var path = new UnityEngine.AI.NavMeshPath();
            Vector3? best = null;
            var bestScore = -1;
            var bestDistance = float.MaxValue;
            for (var radius = 4f; radius <= 10f; radius += 2f)
                for (var angle = 0; angle < 360; angle += 20)
                {
                    var wanted = start.position + Quaternion.AngleAxis(angle, Vector3.up) * Vector3.forward * radius;
                    if (!UnityEngine.AI.NavMesh.SamplePosition(wanted, out var hit, 2f, UnityEngine.AI.NavMesh.AllAreas)) continue;
                    if (!UnityEngine.AI.NavMesh.CalculatePath(start.position, hit.position, UnityEngine.AI.NavMesh.AllAreas, path) || path.status != UnityEngine.AI.NavMeshPathStatus.PathComplete) continue;

                    var score = ClearDirections(hit.position);
                    var distance = Vector3.Distance(start.position, hit.position);
                    if (score > bestScore || (score == bestScore && distance < bestDistance)) { best = hit.position; bestScore = score; bestDistance = distance; }
                }

            return best;
        }

        private static (string, List<DialogueNode>) ElderGraph() => Graph("greet", new DialogueNode[]
        {
            Line("greet", "Elder", "Ah, young ones. Sit, sit... well, stand, I suppose. It is the knees.", "ask"),
            Choice("ask", ("Have you any advice?", "advice"), ("Farewell.", "end")),
            Line("advice", "Elder", "Keep your shield high and your word higher. The rest is just rowing.", "end"),
            End("end"),
        });

        private static (string, List<DialogueNode>) Graph(string start, DialogueNode[] nodes) => (start, nodes.ToList());

        private static SerializableGuid Id(string seed) => SerializableGuid.FromString(seed);

        private static LineNode Line(string id, string speaker, string text, string next)
        {
            var node = new LineNode();
            Set(node, "nodeId", Id(id));
            Set(node, "speakerName", speaker);
            Set(node, "text", text);
            Set(node, "nextNodeId", Id(next));
            return node;
        }

        private static ChoiceNode Choice(string id, params (string text, string next)[] options)
        {
            var node = new ChoiceNode();
            Set(node, "nodeId", Id(id));
            var list = new List<ChoiceOption>();
            foreach (var (text, next) in options)
            {
                var option = new ChoiceOption();
                Set(option, "text", text);
                Set(option, "nextNodeId", Id(next));
                list.Add(option);
            }

            Set(node, "options", list);
            return node;
        }

        private static BranchNode TimelineBranch(string id, string key, string whenTrue, string whenFalse)
        {
            var node = new BranchNode();
            Set(node, "nodeId", Id(id));
            Set(node, "timelineKey", key);
            Set(node, "conditionType", TimelineConditionType.HasOccurred);
            Set(node, "trueNodeId", Id(whenTrue));
            Set(node, "falseNodeId", Id(whenFalse));
            return node;
        }

        private static RecordEventAction Record(string key)
        {
            var action = new RecordEventAction();
            Set(action, "key", key);
            return action;
        }

        private static StartQuestAction StartQuest(string questKey)
        {
            var action = new StartQuestAction();
            Set(action, "questKey", questKey);
            return action;
        }

        private static ChoiceOption Opt(string text, string next, string visibleIfKey = null)
        {
            var option = new ChoiceOption();
            Set(option, "text", text);
            Set(option, "nextNodeId", Id(next));
            if (!string.IsNullOrEmpty(visibleIfKey))
            {
                Set(option, "hasVisibilityCondition", true);
                Set(option, "timelineKey", visibleIfKey);
                Set(option, "conditionType", TimelineConditionType.HasOccurred);
            }

            return option;
        }

        private static ChoiceNode ChoiceOf(string id, params ChoiceOption[] options)
        {
            var node = new ChoiceNode();
            Set(node, "nodeId", Id(id));
            Set(node, "options", new List<ChoiceOption>(options));
            return node;
        }

        // The quest asset and its place in the ruleset: Start(questKey) and the quest log both resolve through the rules database. Created once; a quest edited by hand is left alone.
        private const string QuestFolder = "Assets/Content/Village/Quests";
        private const string RulesetPath = "Assets/Content/Ruleset/VikingRaidRuleset.asset";

        private static void AuthorQuest(List<string> report)
        {
            System.IO.Directory.CreateDirectory(QuestFolder);
            var path = $"{QuestFolder}/{QuestId}.asset";
            var quest = AssetDatabase.LoadAssetAtPath<CRPG.Quests.QuestDefinition>(path);
            if (quest == null)
            {
                quest = ScriptableObject.CreateInstance<CRPG.Quests.QuestDefinition>();
                Set(quest, "id", QuestId);
                Set(quest, "displayName", "Word for Hild");
                Set(quest, "description", "Orm wants the alehouse keeper told that the salt fish has come in.");

                CRPG.Quests.QuestObjectiveDefinition Objective(string id, string name, string key)
                {
                    var o = new CRPG.Quests.QuestObjectiveDefinition();
                    Set(o, "id", id);
                    Set(o, "displayName", name);
                    Set(o, "type", CRPG.Quests.QuestObjectiveType.TalkTo);
                    Set(o, "trackingKey", key);
                    Set(o, "targetCount", 1);
                    Set(o, "progressMode", CRPG.Quests.QuestObjectiveProgressMode.CountSinceActivation);
                    return o;
                }

                Set(quest, "objectives", new List<CRPG.Quests.QuestObjectiveDefinition>
                {
                    Objective("tell-hild", "Tell Hild the salt fish has come in", ToldHildKey),
                    Objective("report-back", "Report back to Orm", DoneKey),
                });
                var reward = new CRPG.Quests.QuestItemReward();
                Set(reward, "item", GoldItem());
                Set(reward, "quantity", 15);
                Set(quest, "rewards", new List<CRPG.Quests.QuestItemReward> { reward });
                AssetDatabase.CreateAsset(quest, path);
                AssetDatabase.SaveAssets();
                report.Add($"created {path}");
            }

            var ruleset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(RulesetPath);
            if (ruleset == null) { report.Add($"WARNING: {RulesetPath} not found: the quest is not registered, so it cannot start."); return; }

            var so = new SerializedObject(ruleset);
            var list = so.FindProperty("extraContent");
            for (var i = 0; i < list.arraySize; i++)
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == quest) return;

            list.arraySize++;
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = quest;
            so.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
            report.Add("registered the quest in the ruleset");
        }

        private static ActionNode Action(string id, DialogueAction action, string next)
        {
            var node = new ActionNode();
            Set(node, "nodeId", Id(id));
            Set(node, "action", action);
            Set(node, "nextNodeId", Id(next));
            return node;
        }

        // Hild keeps the alehouse: a small stock from the SRD items, priced as the SRD says. Only filled in once: a shop edited by hand in the inspector is left alone.
        private static void GiveHildAShop(LocationContent location, List<string> report)
        {
            var index = location.SpawnPoints.ToList().FindIndex(p => p.SpawnId == "village-hild");
            if (index < 0 || location.SpawnPoints[index].Store.HasStore) return;

            var stock = new[] { "item-healing-potion", "item-explorers-pack", "item-dagger", "item-handaxe", "item-shield", "item-leather-armor" };
            var so = new SerializedObject(location);
            var listings = so.FindProperty("spawnPoints").GetArrayElementAtIndex(index).FindPropertyRelative("Store").FindPropertyRelative("Listings");
            listings.ClearArray();
            foreach (var guid in AssetDatabase.FindAssets("t:ItemDefinition"))
            {
                var item = AssetDatabase.LoadAssetAtPath<CRPG.Inventory.ItemDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (item == null || !stock.Contains(item.Id.Value)) continue;
                listings.arraySize++;
                var entry = listings.GetArrayElementAtIndex(listings.arraySize - 1);
                entry.FindPropertyRelative("Item").objectReferenceValue = item;
                entry.FindPropertyRelative("Quantity").intValue = item.Id.Value == "item-dagger" || item.Id.Value == "item-handaxe" ? 2 : -1;
            }

            so.ApplyModifiedProperties();
            report.Add($"village-hild: shop with {listings.arraySize} listings");
        }

        private static EndNode End(string id)
        {
            var node = new EndNode();
            Set(node, "nodeId", Id(id));
            return node;
        }

        // The node classes keep their fields private (authoring goes through the graph window); a one-shot author sets them the way Barrow's End's authors do.
        private static void Set(object target, string field, object value)
        {
            for (var type = target.GetType(); type != null; type = type.BaseType)
            {
                var info = type.GetField(field, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (info == null) continue;
                info.SetValue(target, value);
                return;
            }

            throw new System.InvalidOperationException($"{field} not found on {target.GetType().Name}");
        }
    }
}

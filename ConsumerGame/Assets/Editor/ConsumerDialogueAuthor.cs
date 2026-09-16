using System.Collections.Generic;
using System.Reflection;
using CRPG.Core;
using CRPG.Dialogue;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace CRPG.Consumer.Game.Editor
{
    /// <summary>
    /// One-time authoring script for the compact game's single dialogue beat: a greeting, one Skill:Will
    /// check (exercising <c>FateCheckRule</c> through the real, unmodified <c>DialogueRunner.PerformCheck</c>
    /// path -- the same proof style Task 5.1 established for the alternate ruleset fixture), and a
    /// success/failure line each leading to End. Also authors the runtime <see cref="PanelSettings"/> both
    /// this dialogue and the HUD need -- created fresh here rather than reused across scene rebuilds, per this
    /// project's own established gotcha (create/load panel assets AFTER any NewScene call, never before).
    /// </summary>
    internal static class ConsumerDialogueAuthor
    {
        private const string ContentFolder = "Assets/Game/Content";

        [MenuItem("Consumer Game/Author Dialogue And UI Content")]
        public static void AuthorContent()
        {
            if (!AssetDatabase.IsValidFolder(ContentFolder))
            {
                if (!AssetDatabase.IsValidFolder("Assets/Game")) AssetDatabase.CreateFolder("Assets", "Game");
                AssetDatabase.CreateFolder("Assets/Game", "Content");
            }

            AuthorDialogue();
            AuthorPanelSettings();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[ConsumerDialogueAuthor] Authored dialogue-fate-greeting and 2 PanelSettings assets under " + ContentFolder);
        }

        private static void AuthorDialogue()
        {
            var greeting = new LineNode();
            SetField(greeting, "nodeId", Id("greeting"));
            SetField(greeting, "speakerName", "Wanderer");
            SetField(greeting, "text", "You look steady on your feet. Care to prove it?");
            SetField(greeting, "nextNodeId", Id("check"));

            var check = new CheckNode();
            SetField(check, "nodeId", Id("check"));
            SetField(check, "queryKey", "Skill:Will");
            SetField(check, "difficultyClass", 1);
            SetField(check, "performer", CheckPerformer.ActiveCharacter);
            SetField(check, "successNodeId", Id("success"));
            SetField(check, "failureNodeId", Id("failure"));

            var success = new LineNode();
            SetField(success, "nodeId", Id("success"));
            SetField(success, "speakerName", "Wanderer");
            SetField(success, "text", "Steady indeed. Safe travels.");
            SetField(success, "nextNodeId", Id("end"));

            var failure = new LineNode();
            SetField(failure, "nodeId", Id("failure"));
            SetField(failure, "speakerName", "Wanderer");
            SetField(failure, "text", "Hah -- you flinched. Still, safe travels.");
            SetField(failure, "nextNodeId", Id("end"));

            var end = new EndNode();
            SetField(end, "nodeId", Id("end"));

            var dialogue = ScriptableObject.CreateInstance<DialogueDefinition>();
            SetField(dialogue, "id", "dialogue-fate-greeting");
            SetField(dialogue, "startNodeId", Id("greeting"));
            SetField(dialogue, "nodes", new List<DialogueNode> { greeting, check, success, failure, end });

            CreateAsset(dialogue, "dialogue-fate-greeting");
        }

        private static void AuthorPanelSettings()
        {
            CreatePanelSettings("consumer-hud-panel-settings");
            CreatePanelSettings("consumer-dialogue-panel-settings");
        }

        private static void CreatePanelSettings(string fileName)
        {
            var path = $"{ContentFolder}/{fileName}.asset";
            if (AssetDatabase.LoadAssetAtPath<PanelSettings>(path) != null) return; // keep an existing one -- no theme/font to lose by regenerating, but no need to churn GUIDs either

            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            AssetDatabase.CreateAsset(settings, path);
            EditorUtility.SetDirty(settings);
        }

        private static SerializableGuid Id(string seed) => SerializableGuid.FromString(seed);

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
                if (field != null) { field.SetValue(target, value); return; }
                type = type.BaseType;
            }

            throw new System.Exception($"Field '{fieldName}' not found on {target.GetType().Name} or its base types.");
        }
    }
}

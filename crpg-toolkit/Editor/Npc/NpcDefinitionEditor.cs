using System;
using System.Collections.Generic;
using System.Linq;
using CRPG.Actors;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace CRPG.Editor.Npc
{
    /// <summary>A form-based custom Inspector for NpcDefinition, not a graph canvas -- NPC data is a record
    /// (stats, dropdowns, lists), not a flow, so it doesn't need GraphView's node/edge machinery the way
    /// DialogueDefinition does. Every real field is a plain bound PropertyField; the one piece of actual logic
    /// is the "Populate from Race/Class/Background" convenience button, which pre-fills FeatureIds/
    /// SpellcastingStatId/Level *once* from existing character-creation content and then gets out of the way --
    /// NpcDefinition itself keeps no live reference to whatever Race/Class/Background was used.</summary>
    [CustomEditor(typeof(NpcDefinition))]
    public sealed class NpcDefinitionEditor : UnityEditor.Editor
    {
        private ObjectField raceField;
        private ObjectField classField;
        private ObjectField backgroundField;
        private IntegerField templateLevelField;
        private VisualElement extensionHost;
        private readonly List<IDisposable> extensionDisposables = new();

        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();

            root.Add(Section("Identity", "displayName", "description", "faction", "visualPrefab"));
            root.Add(Section("Dialogue", "dialogueId"));
            root.Add(Section("Stats", "level", "abilityScores"));
            root.Add(Section("Proficiencies", "proficiencies"));
            root.Add(Section("Resources", "resources"));
            root.Add(Section("Features & Abilities", "featureIds", "abilityIds", "spellcastingStatId"));
            root.Add(Section("Equipment", "startingItemIds"));
            root.Add(Section("Appearance", "appearance"));

            // a project's appearance creator (see INpcAppearanceEditor) goes right after the raw data; it follows the NPC's visual prefab, so it is rebuilt when that changes
            extensionHost = new VisualElement { name = "appearance-extensions" };
            root.Add(extensionHost);
            RebuildAppearanceExtensions();
            root.TrackPropertyValue(serializedObject.FindProperty("visualPrefab"), _ => RebuildAppearanceExtensions());

            root.Add(BuildTemplateSection());

            return root;
        }

        /// <summary>Asks every <see cref="INpcAppearanceEditor"/> in the project whether it handles this NPC and (re)builds their elements. A faulty creator is logged and skipped, never fatal.</summary>
        public void RebuildAppearanceExtensions()
        {
            ReleaseAppearanceExtensions();
            extensionHost?.Clear();
            if (extensionHost == null || target == null) return;

            var npc = (NpcDefinition)target;
            foreach (var type in TypeCache.GetTypesDerivedFrom<INpcAppearanceEditor>())
            {
                if (type.IsAbstract || type.IsInterface || type.GetConstructor(Type.EmptyTypes) == null) continue;
                try
                {
                    var handler = (INpcAppearanceEditor)Activator.CreateInstance(type);
                    if (!handler.Handles(npc)) continue;
                    var element = handler.Create(serializedObject);
                    if (element == null) continue;
                    extensionHost.Add(element);
                    if (element is IDisposable disposable) extensionDisposables.Add(disposable);
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);   // one broken creator must not take the whole inspector down with it
                }
            }
        }

        private void ReleaseAppearanceExtensions()
        {
            foreach (var disposable in extensionDisposables)
            {
                try { disposable.Dispose(); }
                catch (Exception ex) { Debug.LogException(ex); }
            }

            extensionDisposables.Clear();
        }

        private void OnDisable() => ReleaseAppearanceExtensions();

        private VisualElement Section(string title, params string[] propertyNames)
        {
            var foldout = new Foldout { text = title, value = true };
            foreach (var propertyName in propertyNames)
            {
                var property = serializedObject.FindProperty(propertyName);
                var field = new PropertyField(property);
                field.Bind(serializedObject);
                foldout.Add(field);
            }

            return foldout;
        }

        /// <summary>Editor-only convenience: pick a Race/Class/Background + level, pre-fill this NPC's
        /// FeatureIds/SpellcastingStatId/Level from them once. Deliberately does NOT resolve any
        /// ChoiceRequirement (a race's subrace pick, a class's skill pick, ...) -- there's no interactive
        /// choice UI here to drive that, so those stay for the designer to add by hand afterward rather than
        /// silently guessing a default that might be wrong.</summary>
        private VisualElement BuildTemplateSection()
        {
            var foldout = new Foldout { text = "Populate From Race / Class / Background", value = false };
            foldout.Add(new Label(
                "One-time convenience: copies granted features (and the class's spellcasting stat) onto this " +
                "NPC. Does not resolve any choice a race/class/background would normally ask a player to make " +
                "(subrace, skill picks, ...) -- add those manually afterward if wanted. Doesn't keep a live " +
                "reference to what you pick here.")
            { style = { whiteSpace = WhiteSpace.Normal, marginBottom = 4 } });

            raceField = new ObjectField("Race") { objectType = typeof(RaceDefinition) };
            classField = new ObjectField("Class") { objectType = typeof(ClassDefinition) };
            backgroundField = new ObjectField("Background") { objectType = typeof(BackgroundDefinition) };
            templateLevelField = new IntegerField("Level") { value = 1 };

            foldout.Add(raceField);
            foldout.Add(classField);
            foldout.Add(backgroundField);
            foldout.Add(templateLevelField);
            foldout.Add(new Button(ApplyTemplate) { text = "Populate" });

            return foldout;
        }

        private void ApplyTemplate()
        {
            var npc = (NpcDefinition)target;
            var featureIds = npc.FeatureIds.ToList();
            var spellcastingStatId = npc.SpellcastingStatId;

            if (raceField.value is RaceDefinition race)
                featureIds.AddRange(race.GrantedFeatureIds.Where(id => !featureIds.Contains(id)));

            if (backgroundField.value is BackgroundDefinition background)
                featureIds.AddRange(background.GrantedFeatureIds.Where(id => !featureIds.Contains(id)));

            if (classField.value is ClassDefinition classDefinition)
            {
                featureIds.AddRange(classDefinition.FeaturesGrantedThroughLevel(templateLevelField.value).Where(id => !featureIds.Contains(id)));
                if (classDefinition.SpellcastingStatId.IsValid) spellcastingStatId = classDefinition.SpellcastingStatId;
            }

            serializedObject.Update();
            serializedObject.FindProperty("featureIds").arraySize = 0;
            for (var i = 0; i < featureIds.Count; i++)
            {
                serializedObject.FindProperty("featureIds").InsertArrayElementAtIndex(i);
                serializedObject.FindProperty("featureIds").GetArrayElementAtIndex(i).FindPropertyRelative("value").stringValue = featureIds[i].Value;
            }
            serializedObject.FindProperty("spellcastingStatId").FindPropertyRelative("value").stringValue = spellcastingStatId.Value;
            serializedObject.FindProperty("level").intValue = templateLevelField.value;
            serializedObject.ApplyModifiedProperties();

            EditorUtility.SetDirty(npc);
        }
    }
}

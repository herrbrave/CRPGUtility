using System;
using System.Collections.Generic;
using System.Linq;
using CRPG.Abilities;
using CRPG.Actors;
using CRPG.Core;
using CRPG.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace CRPG.Samples.CharacterCreator
{
    /// <summary>
    /// The sample's whole interactive flow: Race -&gt; Class -&gt; Background -&gt; Ability Scores -&gt; every
    /// generically-rendered <see cref="ChoiceRequirement"/> the real selections surfaced -&gt; a summary with a
    /// Create Character button. Deliberately linear/one-way (a Start Over button discards the plan and begins
    /// again) rather than allowing in-place back/edit: <see cref="CharacterBuildPlan.RefreshPendingChoices"/>
    /// only ever ADDS to PendingChoices/ResolvedChoices, so letting an earlier answer change without rebuilding
    /// the whole plan could leave stale, no-longer-relevant choices behind. Owns the CharacterBuildPlan for its
    /// own lifetime; the Bootstrap only receives the finished plan once, via <see cref="onCreateCharacter"/>,
    /// and is the sole owner of everything after that (build/register/spawn) -- this view never touches a
    /// GameSession/CharacterState/CharacterAgent directly.
    /// <para>
    /// Race deliberately excludes Elf: <c>RaceDefinition_Elf</c> unconditionally grants
    /// <c>feature-high-elf-traits</c>, whose one choice (a Wizard cantrip) has an empty <c>Options</c> list and
    /// relies on <c>FilterTag: "wizard-cantrip"</c> -- the engine has no generic FilterTag-resolution support
    /// today (AbilityDefinition carries a typed AbilityTag, not a free-string tag), so this sample cannot
    /// resolve that one choice without inventing filtering semantics that belong in the engine, not a sample.
    /// Every other reachable race/class/background choice in the real SRD5 content has a fully explicit,
    /// satisfiable Options list and is handled generically below.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class CharacterCreatorView : MonoBehaviour
    {
        // RaceDefinition_Elf's own authored id -- see the class remarks for why it's excluded.
        private static readonly DefinitionId ExcludedElfRaceId = new("race-elf");

        private static readonly (string Id, string Label)[] AbilityScoreStats =
        {
            ("stat-strength", "Strength"),
            ("stat-dexterity", "Dexterity"),
            ("stat-constitution", "Constitution"),
            ("stat-intelligence", "Intelligence"),
            ("stat-wisdom", "Wisdom"),
            ("stat-charisma", "Charisma"),
        };

        private static readonly int[] StandardArray = { 15, 14, 13, 12, 10, 8 };

        private enum Screen { Race, Class, Background, AbilityScores, PendingChoice, Summary }

        private UIDocument document;
        private VisualElement root;
        private VisualElement creationPanel;
        private VisualElement contentArea;
        private VisualElement summaryPanel;

        private IRulesDatabase rulesDatabase;
        private CharacterBuildService buildService;
        private Action<CharacterBuildPlan> onCreateCharacter;

        private CharacterBuildPlan plan;
        private Screen screen;
        private int abilityScoreIndex;
        private List<int> remainingAbilityScores;

        public void Initialize(IRulesDatabase database, CharacterBuildService service, Action<CharacterBuildPlan> createCharacterCallback)
        {
            rulesDatabase = database;
            buildService = service;
            onCreateCharacter = createCharacterCallback;
            document = GetComponent<UIDocument>();

            BuildLayout();
            StartOver();
        }

        private void OnEnable() => UIToolkitPointerCapture.Register(GetComponent<UIDocument>());
        private void OnDisable() => UIToolkitPointerCapture.Unregister(GetComponent<UIDocument>());

        private void BuildLayout()
        {
            root = document.rootVisualElement;
            root.Clear();
            root.style.flexGrow = 1;

            creationPanel = new VisualElement { style = { width = 420, marginLeft = 24, marginTop = 24, paddingLeft = 16, paddingTop = 16, paddingRight = 16, paddingBottom = 16 } };
            creationPanel.style.borderTopWidth = creationPanel.style.borderBottomWidth = creationPanel.style.borderLeftWidth = creationPanel.style.borderRightWidth = 1;
            creationPanel.style.borderTopColor = creationPanel.style.borderBottomColor = creationPanel.style.borderLeftColor = creationPanel.style.borderRightColor = Color.gray;
            root.Add(creationPanel);

            var startOverButton = new Button(StartOver) { text = "Start Over" };
            startOverButton.style.marginBottom = 8;
            creationPanel.Add(startOverButton);

            contentArea = new VisualElement();
            creationPanel.Add(contentArea);

            summaryPanel = new VisualElement
            {
                style =
                {
                    width = 320, marginLeft = 24, marginTop = 24, paddingLeft = 12, paddingTop = 12, paddingRight = 12, paddingBottom = 12,
                    display = DisplayStyle.None,
                }
            };
            summaryPanel.style.borderTopWidth = summaryPanel.style.borderBottomWidth = summaryPanel.style.borderLeftWidth = summaryPanel.style.borderRightWidth = 1;
            summaryPanel.style.borderTopColor = summaryPanel.style.borderBottomColor = summaryPanel.style.borderLeftColor = summaryPanel.style.borderRightColor = Color.gray;
            root.Add(summaryPanel);
        }

        private void StartOver()
        {
            plan = new CharacterBuildPlan();
            abilityScoreIndex = 0;
            remainingAbilityScores = new List<int>(StandardArray);
            screen = Screen.Race;
            summaryPanel.style.display = DisplayStyle.None;
            creationPanel.style.display = DisplayStyle.Flex;
            Refresh();
        }

        private void Refresh()
        {
            var newContent = new VisualElement();

            switch (screen)
            {
                case Screen.Race:
                    var races = rulesDatabase.All<RaceDefinition>().Where(r => r.Id != ExcludedElfRaceId)
                        .Select(r => (Value: r, Label: r.DisplayName)).ToList();
                    newContent.Add(Header("Choose your Race"));
                    newContent.Add(BuildPicker(races, 1, selection =>
                    {
                        plan.Race = selection[0];
                        buildService.RefreshPendingChoices(plan);
                        screen = Screen.Class;
                        Refresh();
                    }));
                    break;

                case Screen.Class:
                    var classes = rulesDatabase.All<ClassDefinition>().Select(c => (Value: c, Label: c.DisplayName)).ToList();
                    newContent.Add(Header("Choose your Class"));
                    newContent.Add(BuildPicker(classes, 1, selection =>
                    {
                        plan.Class = selection[0];
                        buildService.RefreshPendingChoices(plan);
                        screen = Screen.Background;
                        Refresh();
                    }));
                    break;

                case Screen.Background:
                    var backgrounds = rulesDatabase.All<BackgroundDefinition>().Select(b => (Value: b, Label: b.DisplayName)).ToList();
                    newContent.Add(Header("Choose your Background"));
                    newContent.Add(BuildPicker(backgrounds, 1, selection =>
                    {
                        plan.Background = selection[0];
                        buildService.RefreshPendingChoices(plan);
                        screen = Screen.AbilityScores;
                        Refresh();
                    }));
                    break;

                case Screen.AbilityScores:
                    var (statId, statLabel) = AbilityScoreStats[abilityScoreIndex];
                    var scoreOptions = remainingAbilityScores.Select(v => (Value: v, Label: v.ToString())).ToList();
                    newContent.Add(Header($"Assign Ability Score: {statLabel} ({abilityScoreIndex + 1} of {AbilityScoreStats.Length})"));
                    newContent.Add(BuildPicker(scoreOptions, 1, selection =>
                    {
                        var score = selection[0];
                        plan.AbilityScores.SetScore(new DefinitionId(statId), score);
                        remainingAbilityScores.Remove(score);
                        abilityScoreIndex++;
                        buildService.RefreshPendingChoices(plan);
                        screen = abilityScoreIndex >= AbilityScoreStats.Length ? Screen.PendingChoice : Screen.AbilityScores;
                        Refresh();
                    }));
                    break;

                case Screen.PendingChoice:
                    buildService.RefreshPendingChoices(plan);
                    if (plan.PendingChoices.Count == 0)
                    {
                        screen = Screen.Summary;
                        Refresh();
                        return;
                    }

                    var requirement = plan.PendingChoices[0];
                    var choiceOptions = requirement.Options
                        .Select(id => (Value: id, Label: DisplayNameFor(id, requirement.Type)))
                        .ToList();
                    newContent.Add(Header(requirement.DisplayName));
                    newContent.Add(BuildPicker(choiceOptions, requirement.Count, selection =>
                    {
                        plan.ResolveChoice(requirement.Id, selection);
                        buildService.RefreshPendingChoices(plan);
                        screen = Screen.PendingChoice;
                        Refresh();
                    }));
                    break;

                case Screen.Summary:
                    newContent.Add(Header("Review your Character"));
                    newContent.Add(new Label($"{plan.Race.DisplayName} {plan.Class.DisplayName} ({plan.Background.DisplayName})") { style = { marginBottom = 4 } });
                    foreach (var (id, label) in AbilityScoreStats)
                        newContent.Add(new Label($"{label}: {plan.AbilityScores.GetScore(new DefinitionId(id))}"));

                    var createButton = new Button { text = "Create Character" };
                    createButton.style.marginTop = 8;
                    createButton.clicked += () =>
                    {
                        createButton.SetEnabled(false); // one-shot -- a second click must not build/spawn twice.
                        onCreateCharacter(plan);
                    };
                    newContent.Add(createButton);
                    break;
            }

            var index = contentArea.parent.IndexOf(contentArea);
            contentArea.parent.Insert(index, newContent);
            contentArea.RemoveFromHierarchy();
            contentArea = newContent;
        }

        /// <summary>Called by the Bootstrap once the character is really built, registered, and spawned. Hides
        /// the creation flow entirely and shows a small read-only summary sourced from the real, final
        /// CharacterState -- proving Build() actually applied everything, not just what the plan asked for.
        /// Race/Class/Background names still come from <paramref name="plan"/> since CharacterState itself
        /// carries no such identity field.</summary>
        public void ShowCreatedCharacter(CharacterState character, CharacterBuildPlan builtPlan)
        {
            creationPanel.style.display = DisplayStyle.None;
            summaryPanel.Clear();
            summaryPanel.style.display = DisplayStyle.Flex;

            summaryPanel.Add(Header($"{builtPlan.Race.DisplayName} {builtPlan.Class.DisplayName} ({builtPlan.Background.DisplayName})"));
            foreach (var (id, label) in AbilityScoreStats)
                summaryPanel.Add(new Label($"{label}: {character.Stats.GetScore(new DefinitionId(id))}"));

            foreach (var featureId in character.Features)
                summaryPanel.Add(new Label($"Feature: {DisplayNameFor(featureId, ChoiceType.SelectFeature)}"));
            foreach (var proficiencyId in character.Proficiencies)
                summaryPanel.Add(new Label($"Proficiency: {DisplayNameFor(proficiencyId, ChoiceType.SelectSkill)}"));
            foreach (var abilityId in character.KnownAbilities)
                summaryPanel.Add(new Label($"Knows: {DisplayNameFor(abilityId, ChoiceType.SelectSpell)}"));

            summaryPanel.Add(new Label("Click the ground to move.") { style = { marginTop = 8, color = Color.yellow } });
        }

        private static Label Header(string text) => new(text) { style = { fontSize = 16, unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 8, whiteSpace = WhiteSpace.Normal } };

        private string DisplayNameFor(DefinitionId id, ChoiceType type)
        {
            switch (type)
            {
                case ChoiceType.SelectFeature:
                    if (rulesDatabase.TryResolve<FeatureDefinition>(id, out var feature)) return feature.DisplayName;
                    break;
                case ChoiceType.SelectSkill:
                case ChoiceType.SelectProficiency:
                    if (rulesDatabase.TryResolve<SkillDefinition>(id, out var skill)) return skill.DisplayName;
                    if (rulesDatabase.TryResolve<StatDefinition>(id, out var stat)) return stat.DisplayName;
                    break;
                case ChoiceType.SelectSpell:
                    if (rulesDatabase.TryResolve<AbilityDefinition>(id, out var ability)) return ability.DisplayName;
                    break;
            }

            return id.Value;
        }

        /// <summary>The one generic picker every screen above reuses: toggle buttons restricted to the real
        /// <paramref name="options"/> (an out-of-pool selection is structurally impossible -- a button only
        /// ever exists for a real option) into a de-duplicating selection set (a duplicate is structurally
        /// impossible too -- single-select clears-and-replaces, multi-select is add-if-absent/remove-if-present).
        /// Confirm only enables once the selection count exactly equals <paramref name="requiredCount"/>.</summary>
        private static VisualElement BuildPicker<T>(IReadOnlyList<(T Value, string Label)> options, int requiredCount, Action<IReadOnlyList<T>> onConfirm)
        {
            var container = new VisualElement();
            var selected = new List<T>();
            var buttons = new List<(T Value, string Label, Button Button)>();
            var statusLabel = new Label();
            Button confirmButton = null;

            void Redraw()
            {
                statusLabel.text = $"Selected {selected.Count} of {requiredCount}";
                confirmButton.SetEnabled(selected.Count == requiredCount);
                foreach (var (value, label, button) in buttons)
                    button.text = selected.Contains(value) ? $"[Selected] {label}" : label;
            }

            void Toggle(T value)
            {
                if (requiredCount == 1)
                {
                    selected.Clear();
                    selected.Add(value);
                }
                else if (!selected.Remove(value))
                {
                    selected.Add(value);
                }

                Redraw();
            }

            foreach (var option in options)
            {
                var value = option.Value;
                var button = new Button(() => Toggle(value)) { text = option.Label };
                button.style.marginBottom = 2;
                buttons.Add((value, option.Label, button));
                container.Add(button);
            }

            container.Add(statusLabel);
            confirmButton = new Button(() => onConfirm(new List<T>(selected))) { text = "Confirm" };
            confirmButton.style.marginTop = 8;
            container.Add(confirmButton);

            Redraw();
            return container;
        }
    }
}

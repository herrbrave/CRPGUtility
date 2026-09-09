using System.Linq;
using System.Text;
using CRPG.Actors;
using CRPG.Core;
using UnityEngine;

// Dogfood-only demo script (see third_party_assets / TestProject conventions) -- not part of the shipped
// package. Proves the character-creation pipeline end to end: registers the Fighter sample content into a
// runtime IRulesDatabase, drives CharacterBuildPlan/CharacterBuildService (auto-resolving every remaining
// choice generically -- no special-cased "if this is the fighting style choice" logic), and shows the result
// both in the Console and as an on-screen summary next to the visual character model.
public sealed class FighterDemoBootstrap : MonoBehaviour
{
    [SerializeField] private FighterSampleContentBundle content;
    [SerializeField] private int level = 3;

    private string summary = "Building character...";

    private void Start()
    {
        if (content == null)
        {
            summary = "No FighterSampleContentBundle assigned.";
            Debug.LogError($"[FighterDemoBootstrap] {summary}");
            return;
        }

        var rulesDatabase = new RulesDatabase();
        rulesDatabase.RegisterRange(content.Definitions);

        var plan = new CharacterBuildPlan
        {
            Race = content.Race,
            Class = content.CharacterClass,
            Subclass = content.Subclass,
            Background = content.Background,
            Level = level,
        };

        plan.AbilityScores.SetScore(content.Strength.Id, 16);
        plan.AbilityScores.SetScore(content.Dexterity.Id, 12);
        plan.AbilityScores.SetScore(content.Constitution.Id, 14);
        plan.AbilityScores.SetScore(content.Intelligence.Id, 10);
        plan.AbilityScores.SetScore(content.Wisdom.Id, 12);
        plan.AbilityScores.SetScore(content.Charisma.Id, 8);

        var buildService = new CharacterBuildService(rulesDatabase);
        buildService.RefreshPendingChoices(plan);

        // Auto-resolve every remaining choice by taking its first N options -- deliberately generic (no
        // knowledge of "this is the fighting style choice") to prove the pipeline needs no special-casing.
        while (plan.PendingChoices.Count > 0)
        {
            var choice = plan.PendingChoices[0];
            plan.ResolveChoice(choice.Id, choice.Options.Take(choice.Count).ToList());
            buildService.RefreshPendingChoices(plan);
        }

        var character = buildService.Build(plan, RuntimeEntityId.NewId());

        // HP-at-creation (hit die max + CON modifier, then +avg/roll + CON mod per level) needs the SRD5
        // ability-modifier math this sample deliberately doesn't build -- set directly for the demo.
        character.AddResource(new ResourceState(content.HitPoints.Id, max: 28));
        character.AddResource(new ResourceState(content.Action.Id, max: 1));
        character.AddResource(new ResourceState(content.BonusAction.Id, max: 1));
        character.AddResource(new ResourceState(content.ReactionResource.Id, max: 1));

        var registry = new CharacterRegistry();
        registry.Register(character);

        summary = BuildReadableSummary(character, rulesDatabase, plan);
        Debug.Log($"[FighterDemoBootstrap]\n{summary}");
    }

    private static string BuildReadableSummary(CharacterState character, IRulesDatabase rulesDatabase, CharacterBuildPlan plan)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"{plan.Race.DisplayName} {plan.Class.DisplayName} ({plan.Subclass.DisplayName}), Level {plan.Level}");
        builder.AppendLine($"Background: {plan.Background.DisplayName}");

        var scores = string.Join("  ", rulesDatabase.All<StatDefinition>()
            .OrderBy(s => s.DisplayName)
            .Select(s => $"{s.Abbreviation} {character.Stats.GetScore(s.Id)}"));
        builder.AppendLine(scores);

        var features = string.Join(", ", character.Features
            .Select(id => rulesDatabase.TryResolve<FeatureDefinition>(id, out var f) ? f.DisplayName : id.ToString()));
        builder.AppendLine($"Features: {features}");

        var proficiencies = string.Join(", ", character.Proficiencies.Select(id => id.ToString()));
        builder.AppendLine($"Proficiencies: {proficiencies}");

        return builder.ToString();
    }

    private void OnGUI()
    {
        GUI.Label(new Rect(10, 10, 500, 300), summary);
    }
}

using System.Collections.Generic;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Dialogue;
using CRPG.Inventory;
using CRPG.Quests;
using UnityEngine;

// Dogfood-only demo plumbing (see third_party_assets / TestProject conventions) -- not part of the shipped
// package. Holds every DefinitionAsset OakhavenContentAuthor created, plus the specific ones the demo
// bootstrap needs by name, mirroring FighterSampleContentBundle's rationale (Play mode registers them into a
// runtime IRulesDatabase without needing Editor-only AssetDatabase queries).
public sealed class OakhavenContentBundle : ScriptableObject
{
    [SerializeField] private FighterSampleContentBundle fighterContent;
    [SerializeField] private List<DefinitionAsset> definitions = new();
    [SerializeField] private FeatureDefinition soldierTrainingFeature;
    [SerializeField] private ItemDefinition ringOfProtection;
    [SerializeField] private DialogueDefinition dialogueEldor;
    [SerializeField] private DialogueDefinition dialogueGriff;
    [SerializeField] private DialogueDefinition dialogueWellCavern;
    [SerializeField] private DialogueDefinition dialogueMiningCrevasse;
    [SerializeField] private DialogueDefinition dialogueBroodChamber;
    [SerializeField] private QuestDefinition questLostHeir;
    [SerializeField] private QuestDefinition questGoblinMenace;

    public FighterSampleContentBundle FighterContent => fighterContent;
    public IReadOnlyList<DefinitionAsset> Definitions => definitions;
    public FeatureDefinition SoldierTrainingFeature => soldierTrainingFeature;
    public ItemDefinition RingOfProtection => ringOfProtection;
    public DialogueDefinition DialogueEldor => dialogueEldor;
    public DialogueDefinition DialogueGriff => dialogueGriff;
    public DialogueDefinition DialogueWellCavern => dialogueWellCavern;
    public DialogueDefinition DialogueMiningCrevasse => dialogueMiningCrevasse;
    public DialogueDefinition DialogueBroodChamber => dialogueBroodChamber;
    public QuestDefinition QuestLostHeir => questLostHeir;
    public QuestDefinition QuestGoblinMenace => questGoblinMenace;
}

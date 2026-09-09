using System.Collections.Generic;
using CRPG.Core;
using UnityEngine;

namespace CRPG.Actors
{
    /// <summary>
    /// A complete, ready-to-spawn NPC -- the non-interactive counterpart to CharacterBuildPlan/
    /// CharacterBuildService. Where a player build resolves ChoiceRequirements interactively (pick a skill,
    /// pick a spell, ...), every field here is already a flat, designer-authored value: an NPC has to spawn
    /// instantly and deterministically, with no choice UI to drive. This deliberately covers both a full SRD5
    /// monster stat block (flat stats, no Race/Class at all) and a "built like a PC" named NPC (an Editor
    /// convenience can pre-fill these fields from a RaceDefinition/ClassDefinition/BackgroundDefinition once,
    /// but nothing here references them live) -- one type, not two, since both are ultimately just "a character
    /// sheet authored by a designer instead of a player."
    /// <para>Every cross-system reference (Dialogue, items) is a bare DefinitionId, never a direct object
    /// reference -- CRPG.Actors has no reason to depend on CRPG.Dialogue or CRPG.Inventory, matching how
    /// RaceDefinition/FeatureDefinition already reference proficiencies/abilities by id only. Equipping
    /// StartingItemIds into real inventory slots, and wiring DialogueId into an actual conversation trigger, are
    /// both left to whoever spawns this NPC -- the same "authoring stops at data, integration is a separate
    /// step" boundary CharacterBuildService.Build already draws around SelectEquipment for players.
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "CRPG Toolkit/Actors/Npc Definition")]
    public sealed class NpcDefinition : DefinitionAsset
    {
        [SerializeField] private string displayName;
        [SerializeField, TextArea] private string description;
        [SerializeField] private DefinitionId faction;
        [SerializeField] private DefinitionId dialogueId;
        [SerializeField] private int level = 1;
        [SerializeField] private List<StatScoreEntry> abilityScores = new();
        [SerializeField] private List<DefinitionId> proficiencies = new();
        [SerializeField] private List<ResourceGrant> resources = new();
        [SerializeField] private List<DefinitionId> featureIds = new();
        [SerializeField] private List<DefinitionId> abilityIds = new();
        [SerializeField] private DefinitionId spellcastingStatId;
        [SerializeField] private List<DefinitionId> startingItemIds = new();
        [SerializeField] private GameObject visualPrefab;

        public string DisplayName => displayName;
        public string Description => description;
        public DefinitionId Faction => faction;

        /// <summary>DefinitionId.None if this NPC has no conversation -- not every NPC talks.</summary>
        public DefinitionId DialogueId => dialogueId;
        public int Level => level;
        public IReadOnlyList<StatScoreEntry> AbilityScores => abilityScores;
        public IReadOnlyList<DefinitionId> Proficiencies => proficiencies;
        public IReadOnlyList<ResourceGrant> Resources => resources;

        /// <summary>FeatureDefinitions granted directly (racial/class traits, monster traits, whatever the
        /// designer wants this NPC to have) -- skips CharacterBuildService's Race/Class/Background/level-
        /// progression collection entirely, since there's no build plan here to collect it from.</summary>
        public IReadOnlyList<DefinitionId> FeatureIds => featureIds;

        /// <summary>AbilityDefinitions (spells or mundane abilities) this NPC knows outright -- the NPC
        /// equivalent of CharacterState.KnownAbilities, populated directly instead of via SelectSpell choices.</summary>
        public IReadOnlyList<DefinitionId> AbilityIds => abilityIds;

        /// <summary>DefinitionId.None for a non-caster. Only meaningful if AbilityIds includes any spells --
        /// governs spell attack rolls/save DCs exactly like CharacterState.SpellcastingStatId does for a PC.</summary>
        public DefinitionId SpellcastingStatId => spellcastingStatId;

        public IReadOnlyList<DefinitionId> StartingItemIds => startingItemIds;
        public GameObject VisualPrefab => visualPrefab;
    }
}

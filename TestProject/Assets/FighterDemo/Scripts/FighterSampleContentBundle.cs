using System.Collections.Generic;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Inventory;
using UnityEngine;

// Dogfood-only demo plumbing (see third_party_assets / TestProject conventions) -- not part of the shipped
// package. Holds every DefinitionAsset the Fighter sample content author created, plus the specific ones the
// demo bootstrap needs by name, so Play mode can register them into a runtime IRulesDatabase without needing
// Editor-only AssetDatabase queries.
public sealed class FighterSampleContentBundle : ScriptableObject
{
    [SerializeField] private List<DefinitionAsset> definitions = new();
    [SerializeField] private RaceDefinition race;
    [SerializeField] private ClassDefinition characterClass;
    [SerializeField] private SubclassDefinition subclass;
    [SerializeField] private BackgroundDefinition background;
    [SerializeField] private StatDefinition strength;
    [SerializeField] private StatDefinition dexterity;
    [SerializeField] private StatDefinition constitution;
    [SerializeField] private StatDefinition intelligence;
    [SerializeField] private StatDefinition wisdom;
    [SerializeField] private StatDefinition charisma;
    [SerializeField] private ResourceDefinition hitPoints;
    [SerializeField] private ResourceDefinition action;
    [SerializeField] private ResourceDefinition bonusAction;
    [SerializeField] private ResourceDefinition reactionResource;
    [SerializeField] private EquipmentSlotDefinition mainHandSlot;
    [SerializeField] private EquipmentSlotDefinition armorSlot;
    [SerializeField] private ItemDefinition longsword;
    [SerializeField] private ItemDefinition chainShirt;
    [SerializeField] private ItemDefinition goldPiece;
    [SerializeField] private ItemDefinition healingPotion;

    public IReadOnlyList<DefinitionAsset> Definitions => definitions;
    public RaceDefinition Race => race;
    public ClassDefinition CharacterClass => characterClass;
    public SubclassDefinition Subclass => subclass;
    public BackgroundDefinition Background => background;
    public StatDefinition Strength => strength;
    public StatDefinition Dexterity => dexterity;
    public StatDefinition Constitution => constitution;
    public StatDefinition Intelligence => intelligence;
    public StatDefinition Wisdom => wisdom;
    public StatDefinition Charisma => charisma;
    public ResourceDefinition HitPoints => hitPoints;
    public ResourceDefinition Action => action;
    public ResourceDefinition BonusAction => bonusAction;
    public ResourceDefinition ReactionResource => reactionResource;
    public EquipmentSlotDefinition MainHandSlot => mainHandSlot;
    public EquipmentSlotDefinition ArmorSlot => armorSlot;
    public ItemDefinition Longsword => longsword;
    public ItemDefinition ChainShirt => chainShirt;
    public ItemDefinition GoldPiece => goldPiece;
    public ItemDefinition HealingPotion => healingPotion;
}

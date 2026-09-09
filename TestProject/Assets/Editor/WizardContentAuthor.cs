using System.Collections.Generic;
using System.Reflection;
using CRPG.Abilities;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Rules;
using UnityEditor;
using UnityEngine;

// Dogfood-only tooling (see third_party_assets / TestProject conventions) -- not part of the shipped package.
//
// One-time content author for a minimal SRD5 "Wizard, levels 1-3" slice, mirroring FighterSampleContentAuthor.cs
// exactly: real .asset files saved under the package's Rules/SRD5e2014/Content/, authored through
// DefinitionAsset fields via reflection instead of the Inspector. Spells are just AbilityDefinitions (no
// separate spell system) -- Fire Bolt and Second Wind are both an AbilityDefinition with different
// costs/targeting/effects.
//
// Simplifications / judgment calls (same "author what's expressible, note the gap" spirit as Fighter's own
// header comment):
// - Spell ranges are authored in meters (feet / 3.28, rounded to a clean value) since 1 world unit = 1 meter
//   here, not 1 foot.
// - No damage-type modeling exists yet (fire/cold/force/acid all resolve as generic HP damage via DamageEffect)
//   -- Fire Bolt/Ray of Frost/Burning Hands/Scorching Ray/Web all lose their real damage type.
// - Ray of Frost's real "reduces target's speed by 10 ft" on-hit rider is dropped -- no Speed stat exists yet.
// - Magic Missile (3 darts, 1d4+1 force each, guaranteed hit, can be split across targets) is modeled as a
//   single guaranteed DamageEffect (1d4+1) applied once per chosen target, targetable at up to 3 creatures --
//   this matches the "split across targets" case but *undercounts* the common "dump all 3 darts into one
//   target" case (real total 3d4+3 to one creature), since DamageEffect has no per-effect repeat-count/stacking
//   mechanism to model multiple darts landing on the same target within one cast.
// - Scorching Ray (3 separate ranged spell-attack rolls, each 2d6 fire, can all be aimed at one target or
//   split) is modeled the same way as Magic Missile above: AttackRollEffect + DamageEffect against up to 3
//   targets, one roll each -- it cannot stack multiple rays onto a single target either.
// - Burning Hands' "half damage on a successful Dex save" is approximated by wrapping the *same* DamageEffect
//   instance in both SavingThrowEffect.onFailure and onSuccess (per the architecture's own suggested shortcut)
//   rather than a true half -- there's no "halve this effect's output" primitive, so a successful save currently
//   still takes full damage. Noted here rather than silently modeled wrong.
// - Burning Hands' 15-foot cone and Web's 20-foot cube are declared via TargetingDefinition's Cone/Area mode +
//   areaSize only -- there's no real AoE target-resolution solver, so "who's actually in the cone/cube" is left
//   to whatever UI/AI eventually picks targets, capped at a reasonable maxTargets.
// - Mage Armor's "13 + Dex modifier" AC formula is flattened to a fixed Override(ArmorClass, 13) -- there's no
//   way to author "base + another stat's modifier" as a single FeatureModifier, so the Dex contribution is lost.
//   Shield's "+5 AC until the start of your next turn" likewise has no duration/auto-removal mechanism, so both
//   spells' GrantFeatureEffect grants their AC feature *permanently* once cast -- same explicitly-accepted
//   simplification the architecture notes for Mage Armor. A real implementation needs the buff removed on
//   unequip/dispel/turn-end; nothing here does that yet.
// - Web's "restrained while in the webs, Strength check to escape" and Invisibility's "ends on attack/cast"
//   are both modeled as ApplyConditionEffect with duration = -1 (indefinite) -- there's no mechanism yet to
//   auto-remove a condition on escape, on the spell ending, or on the triggering action (attacking/casting),
//   so both conditions are authored as data only (condition-restrained, condition-invisible, no modifiers) and
//   just sit on the target until something else removes them. Nothing yet grants Restrained's real
//   speed-0/attack-advantage-disadvantage rules or Invisible's attack advantage/disadvantage rules either --
//   authored as data ahead of the consuming system, same as Fighter's Improved Critical/CriticalRange.
// - Detect Magic and Comprehend Languages are authored as zero-Effect Utility spells (their real value is
//   informational/narrative, matching Mage Hand/Minor Illusion/Prestidigitation) -- Detect Magic keeps the
//   Concentration tag since it really requires concentration; neither models the SRD's "ritual" casting mode
//   since no ritual-casting mechanism exists yet.
// - Concentration is tagged (AbilityTag.Concentration) on every real-SRD concentration spell here (Detect
//   Magic, Web, Invisibility) even though nothing enforces "only one concentration spell at a time" yet --
//   same "author ahead of the consuming system" pattern as Fighter's Improved Critical.
// - No Arcane Tradition (subclass) is authored -- out of scope for this pass (levels 1-3 core class only, and
//   the real subclass choice happens at level 2 with feature-heavy subclasses this slice doesn't need).
public static class WizardContentAuthor
{
    private const string ContentFolder = "Packages/com.crpgtoolkit.core/Rules/SRD5e2014/Content";

    [MenuItem("CRPG Toolkit/Content/Author Wizard Content (SRD5)")]
    public static void Author()
    {
        EnsureFolder(ContentFolder);
        var all = new List<DefinitionAsset>();

        // --- Reuse existing shared content authored by the Fighter pass -----------------------------------
        var intelligence = LoadExisting<StatDefinition>("Intelligence");
        var wisdom = LoadExisting<StatDefinition>("Wisdom");
        var dexterity = LoadExisting<StatDefinition>("Dexterity");
        var history = LoadExisting<SkillDefinition>("History");
        var insight = LoadExisting<SkillDefinition>("Insight");
        var hp = LoadExisting<ResourceDefinition>("Hit Points");
        var action = LoadExisting<ResourceDefinition>("Action");
        var reaction = LoadExisting<ResourceDefinition>("Reaction");

        // --- New shared skills the real SRD Wizard skill list needs ---------------------------------------
        var arcana = CreateSkill("skill-arcana", "Arcana", intelligence, all);
        var investigation = CreateSkill("skill-investigation", "Investigation", intelligence, all);
        var medicine = CreateSkill("skill-medicine", "Medicine", wisdom, all);
        var religion = CreateSkill("skill-religion", "Religion", intelligence, all);

        // --- New shared spell-slot resources ----------------------------------------------------------------
        var spellSlot1 = CreateResource("resource-spell-slot-1", "Spell Slot Level 1", all, RestTrigger.LongRest);
        var spellSlot2 = CreateResource("resource-spell-slot-2", "Spell Slot Level 2", all, RestTrigger.LongRest);

        // --- Conditions used by Web/Invisibility (authored as data; nothing consumes their real rules yet) ---
        var restrained = CreateAsset<ConditionDefinition>("condition-restrained", "Restrained", all);
        SetField(restrained, "displayName", "Restrained");
        SetField(restrained, "description",
            "A restrained creature's speed becomes 0, it has disadvantage on Dexterity saving throws, and " +
            "attack rolls against it have advantage while its own attack rolls have disadvantage. (No modifiers " +
            "authored yet -- nothing in the engine consumes speed/advantage from conditions.)");

        var invisible = CreateAsset<ConditionDefinition>("condition-invisible", "Invisible", all);
        SetField(invisible, "displayName", "Invisible");
        SetField(invisible, "description",
            "An invisible creature is impossible to see without magic or a special sense. Attack rolls against " +
            "it have disadvantage, and its own attack rolls have advantage. (No modifiers authored yet -- " +
            "nothing in the engine consumes advantage/disadvantage from conditions.)");

        // --- Cantrips (SelectSpell pool at level 1) ----------------------------------------------------------
        var fireBolt = CreateAbility("spell-fire-bolt", "Fire Bolt", AbilityTag.Spell | AbilityTag.Attack | AbilityTag.Ranged, all);
        SetField(fireBolt, "description",
            "You hurl a mote of fire at a creature or object within range. Make a ranged spell attack against " +
            "the target. On a hit, the target takes 1d10 fire damage. This spell's damage increases by 1d10 " +
            "when you reach 5th, 11th, and 17th level (not modeled -- no character-level-scaling hook on " +
            "AbilityDefinition yet).");
        SetField(fireBolt, "targeting", Targeting(TargetMode.SingleUnit, TargetRelation.Enemy, 1, 1, 36.5f));
        SetField(fireBolt, "effects", new List<AbilityEffect> { AttackRoll(Damage(hp, new DiceExpression(1, 10))) });

        var mageHand = CreateAbility("spell-mage-hand", "Mage Hand", AbilityTag.Spell | AbilityTag.Utility, all);
        SetField(mageHand, "description",
            "A spectral, floating hand appears at a point you choose within range and lasts for 1 minute. You " +
            "can use your action to move it up to 30 feet and manipulate objects, open unlocked doors/containers, " +
            "stow/retrieve items, or pour a container's contents. It can't attack, activate magic items, or " +
            "carry more than 10 pounds. Pure utility -- no mechanical effect authored.");
        SetField(mageHand, "targeting", Targeting(TargetMode.Point, TargetRelation.Any, 1, 1, 9f));

        var minorIllusion = CreateAbility("spell-minor-illusion", "Minor Illusion", AbilityTag.Spell | AbilityTag.Utility, all);
        SetField(minorIllusion, "description",
            "You create a sound or an image of an object within range that lasts for 1 minute. A creature can " +
            "use its action to inspect the image and, on an Intelligence (Investigation) check against your " +
            "spell save DC, discern it is an illusion. Pure utility -- no mechanical effect authored.");
        SetField(minorIllusion, "targeting", Targeting(TargetMode.Point, TargetRelation.Any, 1, 1, 9f));

        var prestidigitation = CreateAbility("spell-prestidigitation", "Prestidigitation", AbilityTag.Spell | AbilityTag.Utility, all);
        SetField(prestidigitation, "description",
            "A minor magical trick: create a harmless sensory effect, light/snuff a small flame, clean or soil " +
            "an object, chill/warm/flavor nonliving material, or make a mark/small image, lasting up to 1 hour. " +
            "Pure utility -- no mechanical effect authored.");
        SetField(prestidigitation, "targeting", Targeting(TargetMode.Point, TargetRelation.Any, 1, 1, 3f));

        var rayOfFrost = CreateAbility("spell-ray-of-frost", "Ray of Frost", AbilityTag.Spell | AbilityTag.Attack | AbilityTag.Ranged, all);
        SetField(rayOfFrost, "description",
            "A frigid beam of blue-white light streaks toward a creature within range. Make a ranged spell " +
            "attack against the target. On a hit, it takes 1d8 cold damage, and its speed is reduced by 10 " +
            "feet until the start of your next turn (speed reduction not modeled -- no Speed stat exists yet). " +
            "Damage increases by 1d8 at 5th, 11th, and 17th level (not modeled).");
        SetField(rayOfFrost, "targeting", Targeting(TargetMode.SingleUnit, TargetRelation.Enemy, 1, 1, 18f));
        SetField(rayOfFrost, "effects", new List<AbilityEffect> { AttackRoll(Damage(hp, new DiceExpression(1, 8))) });

        var cantrips = new[] { fireBolt.Id, mageHand.Id, minorIllusion.Id, prestidigitation.Id, rayOfFrost.Id };

        // --- 1st-level spells ---------------------------------------------------------------------------------
        var magicMissile = CreateAbility("spell-magic-missile", "Magic Missile", AbilityTag.Spell, all);
        SetField(magicMissile, "description",
            "You create three glowing darts of magical force. Each dart hits a creature of your choice that " +
            "you can see within range, dealing 1d4 + 1 force damage. The darts all strike simultaneously and " +
            "the attack automatically hits. Modeled here as up to 3 targets, each taking one guaranteed 1d4+1 " +
            "hit -- stacking multiple darts onto a single target isn't modeled (no per-effect repeat/stack " +
            "mechanism yet), so a focused single-target cast undercounts the real 3d4+3 total.");
        SetField(magicMissile, "costs", new List<AbilityCost> { Cost(action, 1), Cost(spellSlot1, 1) });
        SetField(magicMissile, "targeting", Targeting(TargetMode.MultiUnit, TargetRelation.Enemy, 1, 3, 36.5f));
        SetField(magicMissile, "effects", new List<AbilityEffect> { Damage(hp, new DiceExpression(1, 4, 1)) });

        var burningHands = CreateAbility("spell-burning-hands", "Burning Hands", AbilityTag.Spell | AbilityTag.Hostile, all);
        SetField(burningHands, "description",
            "A thin sheet of flame shoots from your outstretched fingertips. Each creature in a 15-foot cone " +
            "must make a Dexterity saving throw, taking 3d6 fire damage on a failed save, or half as much on a " +
            "successful one. Damage type not modeled (generic HP damage only). Half-damage-on-success is " +
            "approximated by reusing the same DamageEffect for both outcomes (see file header) -- a successful " +
            "save currently still takes full damage.");
        var burningHandsDamage = Damage(hp, new DiceExpression(3, 6));
        SetField(burningHands, "costs", new List<AbilityCost> { Cost(action, 1), Cost(spellSlot1, 1) });
        SetField(burningHands, "targeting", Targeting(TargetMode.Cone, TargetRelation.Any, 1, 6, 0f, 4.5f));
        SetField(burningHands, "effects", new List<AbilityEffect> { SavingThrow(dexterity.Id, burningHandsDamage, burningHandsDamage) });

        var mageArmorFeature = CreateFeature("feature-mage-armor-ac", "Mage Armor", all);
        SetField(mageArmorFeature, "modifiers", new List<FeatureModifier> { Modifier("ArmorClass", RuleModifierOperation.Override, 13f) });
        var mageArmor = CreateAbility("spell-mage-armor", "Mage Armor", AbilityTag.Spell | AbilityTag.Utility, all);
        SetField(mageArmor, "description",
            "You touch a willing creature who isn't wearing armor, and a protective magical force surrounds it " +
            "until the spell ends (8 hours). The target's base AC becomes 13 + its Dexterity modifier. " +
            "Flattened to a fixed Override(ArmorClass, 13) -- there's no way to author \"base + another stat's " +
            "modifier\" as one FeatureModifier, so the Dex contribution is lost. Granted permanently (no " +
            "duration/unequip-removal mechanism yet) -- see file header.");
        SetField(mageArmor, "costs", new List<AbilityCost> { Cost(action, 1), Cost(spellSlot1, 1) });
        SetField(mageArmor, "targeting", Targeting(TargetMode.SingleUnit, TargetRelation.Ally, 1, 1, 1.5f));
        var mageArmorEffect = new GrantFeatureEffect();
        SetField(mageArmorEffect, "feature", mageArmorFeature);
        SetField(mageArmorEffect, "applyToSelf", false);
        SetField(mageArmor, "effects", new List<AbilityEffect> { mageArmorEffect });

        var shieldFeature = CreateFeature("feature-shield-ac", "Shield", all);
        SetField(shieldFeature, "modifiers", new List<FeatureModifier> { Modifier("ArmorClass", RuleModifierOperation.Add, 5f) });
        var shield = CreateAbility("spell-shield", "Shield", AbilityTag.Spell | AbilityTag.Reaction, all);
        SetField(shield, "description",
            "An invisible barrier of magical force appears and protects you. Until the start of your next " +
            "turn, you have a +5 bonus to AC, including against the triggering attack, and you take no damage " +
            "from magic missile. Cast as a reaction. Granted permanently (no duration/\"until start of your " +
            "next turn\" removal mechanism yet) -- see file header.");
        SetField(shield, "costs", new List<AbilityCost> { Cost(reaction, 1), Cost(spellSlot1, 1) });
        SetField(shield, "targeting", SelfTargeting());
        var shieldEffect = new GrantFeatureEffect();
        SetField(shieldEffect, "feature", shieldFeature);
        SetField(shieldEffect, "applyToSelf", true);
        SetField(shield, "effects", new List<AbilityEffect> { shieldEffect });

        var detectMagic = CreateAbility("spell-detect-magic", "Detect Magic", AbilityTag.Spell | AbilityTag.Utility | AbilityTag.Concentration, all);
        SetField(detectMagic, "description",
            "For the duration (up to 10 minutes, concentration), you sense the presence of magic within 30 " +
            "feet of you, and can use your action to see a faint aura around a visible magical creature or " +
            "object and learn its school of magic. Can be cast as a ritual (not modeled -- no ritual-casting " +
            "mechanism yet). Pure utility -- no mechanical effect authored.");
        SetField(detectMagic, "costs", new List<AbilityCost> { Cost(action, 1), Cost(spellSlot1, 1) });
        SetField(detectMagic, "targeting", SelfTargeting());

        var comprehendLanguages = CreateAbility("spell-comprehend-languages", "Comprehend Languages", AbilityTag.Spell | AbilityTag.Utility, all);
        SetField(comprehendLanguages, "description",
            "For 1 hour, you understand the literal meaning of any spoken language you hear, and any written " +
            "language you touch. Can be cast as a ritual (not modeled). Pure utility -- no mechanical effect " +
            "authored.");
        SetField(comprehendLanguages, "costs", new List<AbilityCost> { Cost(action, 1), Cost(spellSlot1, 1) });
        SetField(comprehendLanguages, "targeting", SelfTargeting());

        // --- 2nd-level spells -----------------------------------------------------------------------------
        var scorchingRay = CreateAbility("spell-scorching-ray", "Scorching Ray", AbilityTag.Spell | AbilityTag.Attack | AbilityTag.Ranged, all);
        SetField(scorchingRay, "description",
            "You create three rays of fire and hurl them at targets within range, one at a time or several. " +
            "Make a ranged spell attack for each ray; on a hit, the target takes 2d6 fire damage. Modeled as " +
            "up to 3 targets, each taking one attack-roll-gated 2d6 hit -- like Magic Missile, stacking multiple " +
            "rays onto one target isn't modeled.");
        SetField(scorchingRay, "costs", new List<AbilityCost> { Cost(action, 1), Cost(spellSlot2, 1) });
        SetField(scorchingRay, "targeting", Targeting(TargetMode.MultiUnit, TargetRelation.Enemy, 1, 3, 36.5f));
        SetField(scorchingRay, "effects", new List<AbilityEffect> { AttackRoll(Damage(hp, new DiceExpression(2, 6))) });

        var web = CreateAbility("spell-web", "Web", AbilityTag.Spell | AbilityTag.Hostile | AbilityTag.Concentration, all);
        SetField(web, "description",
            "Thick, sticky webbing fills a 20-foot cube within range for up to 1 hour (concentration). Each " +
            "creature that starts its turn in the webs, or enters them, must make a Dexterity saving throw or " +
            "become restrained as long as it remains in the webs (a Strength check against your spell save DC " +
            "frees it early -- escape not modeled). Difficult terrain/light obscurement/flammability not " +
            "modeled. Restrained condition applied with an indefinite duration -- no auto-removal on escape or " +
            "on the spell ending yet, see file header.");
        SetField(web, "costs", new List<AbilityCost> { Cost(action, 1), Cost(spellSlot2, 1) });
        SetField(web, "targeting", Targeting(TargetMode.Area, TargetRelation.Enemy, 1, 6, 18f, 6f));
        var webCondition = new ApplyConditionEffect();
        SetField(webCondition, "condition", restrained);
        SetField(webCondition, "duration", -1);
        SetField(web, "effects", new List<AbilityEffect> { SavingThrow(dexterity.Id, webCondition, null) });

        var invisibility = CreateAbility("spell-invisibility", "Invisibility", AbilityTag.Spell | AbilityTag.Utility | AbilityTag.Concentration, all);
        SetField(invisibility, "description",
            "A creature you touch becomes invisible until the spell ends (up to 1 hour, concentration). " +
            "Anything it wears or carries is invisible with it. The spell ends early if the target attacks or " +
            "casts a spell (not modeled -- no auto-removal-on-action mechanism yet, applied with an indefinite " +
            "duration). Higher-level-slot extra targets not modeled.");
        SetField(invisibility, "costs", new List<AbilityCost> { Cost(action, 1), Cost(spellSlot2, 1) });
        SetField(invisibility, "targeting", Targeting(TargetMode.SingleUnit, TargetRelation.Ally, 1, 1, 1.5f));
        var invisibilityCondition = new ApplyConditionEffect();
        SetField(invisibilityCondition, "condition", invisible);
        SetField(invisibilityCondition, "duration", -1);
        SetField(invisibility, "effects", new List<AbilityEffect> { invisibilityCondition });

        // --- Features ------------------------------------------------------------------------------------
        var skillChoiceFeature = CreateFeature("feature-wizard-skill-choice", "Wizard Skill Proficiencies", all);
        SetField(skillChoiceFeature, "choices", new List<ChoiceRequirement>
        {
            Choice("choose-wizard-skills", "Choose 2 skills", ChoiceType.SelectSkill, 2,
                arcana.Id, history.Id, investigation.Id, insight.Id, medicine.Id, religion.Id),
        });

        var cantripChoiceFeature = CreateFeature("feature-wizard-cantrips", "Cantrips Known", all);
        SetField(cantripChoiceFeature, "choices", new List<ChoiceRequirement>
        {
            Choice("choose-wizard-cantrips", "Choose 3 cantrips", ChoiceType.SelectSpell, 3, cantrips),
        });

        var slots1 = CreateFeature("feature-wizard-slots-1", "Spellcasting (Level 1 Slots)", all);
        SetField(slots1, "grantedResources", new List<ResourceGrant> { ResourceGrantOf(spellSlot1, 2) });

        var slots2 = CreateFeature("feature-wizard-slots-2", "Spellcasting (Level 2 Slots)", all);
        SetField(slots2, "grantedResources", new List<ResourceGrant> { ResourceGrantOf(spellSlot1, 3) });

        var slots3 = CreateFeature("feature-wizard-slots-3", "Spellcasting (Level 3 Slots)", all);
        SetField(slots3, "grantedResources", new List<ResourceGrant>
        {
            ResourceGrantOf(spellSlot1, 4),
            ResourceGrantOf(spellSlot2, 2),
        });

        // --- Class -----------------------------------------------------------------------------------------
        var wizard = CreateAsset<ClassDefinition>("class-wizard", "Wizard", all);
        SetField(wizard, "displayName", "Wizard");
        SetField(wizard, "hitDie", new DiceExpression(1, 6));
        SetField(wizard, "primaryStatIds", new List<DefinitionId> { intelligence.Id });
        SetField(wizard, "savingThrowProficiencyIds", new List<DefinitionId> { intelligence.Id, wisdom.Id });
        SetField(wizard, "spellcastingStatId", intelligence.Id);
        SetField(wizard, "spellcastingType", SpellcastingType.Prepared);
        SetField(wizard, "preparedSpellsLevelDivisor", 1);
        SetField(wizard, "spellListAbilityIds", new List<DefinitionId>
        {
            magicMissile.Id, burningHands.Id, mageArmor.Id, shield.Id, detectMagic.Id, comprehendLanguages.Id,
            scorchingRay.Id, web.Id, invisibility.Id,
        });
        SetField(wizard, "levelProgression", new List<ClassLevelGrant>
        {
            LevelGrant(1, skillChoiceFeature.Id, cantripChoiceFeature.Id, slots1.Id),
            LevelGrant(2, slots2.Id),
            LevelGrant(3, slots3.Id),
        });

        // No AssetDatabase.Refresh()/SaveAssets() here: either can trigger a reimport that invalidates the
        // in-memory references held above, silently leaving later reflection writes to already-created assets
        // dropped. Everything is saved once, at the very end.

        foreach (var definition in all) EditorUtility.SetDirty(definition);
        AssetDatabase.SaveAssets();

        Debug.Log($"[WizardContentAuthor] Authored {all.Count} SRD5 Wizard definitions under {ContentFolder}.");
    }

    private static T LoadExisting<T>(string displayName) where T : DefinitionAsset
    {
        var safeName = displayName.Replace(" ", "").Replace(":", "");
        var path = $"{ContentFolder}/{typeof(T).Name}_{safeName}.asset";
        var asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset == null)
            Debug.LogError($"[WizardContentAuthor] Could not load existing asset at {path} -- has Fighter content been authored yet?");
        return asset;
    }

    private static SkillDefinition CreateSkill(string id, string displayName, StatDefinition governingStat, List<DefinitionAsset> all)
    {
        var skill = CreateAsset<SkillDefinition>(id, displayName, all);
        SetField(skill, "displayName", displayName);
        SetField(skill, "governingStat", governingStat);
        return skill;
    }

    private static ResourceDefinition CreateResource(string id, string displayName, List<DefinitionAsset> all, RestTrigger restTrigger = RestTrigger.None)
    {
        var resource = CreateAsset<ResourceDefinition>(id, displayName, all);
        SetField(resource, "displayName", displayName);
        SetField(resource, "restTrigger", restTrigger);
        return resource;
    }

    private static FeatureDefinition CreateFeature(string id, string displayName, List<DefinitionAsset> all)
    {
        var feature = CreateAsset<FeatureDefinition>(id, displayName, all);
        SetField(feature, "displayName", displayName);
        return feature;
    }

    private static AbilityDefinition CreateAbility(string id, string displayName, AbilityTag tags, List<DefinitionAsset> all)
    {
        var ability = CreateAsset<AbilityDefinition>(id, displayName, all);
        SetField(ability, "displayName", displayName);
        SetField(ability, "tags", tags);
        return ability;
    }

    private static DamageEffect Damage(ResourceDefinition hitPointResource, DiceExpression amount)
    {
        var effect = new DamageEffect();
        SetField(effect, "hitPointResource", hitPointResource);
        SetField(effect, "amount", amount);
        return effect;
    }

    private static AttackRollEffect AttackRoll(AbilityEffect onHit)
    {
        // governingStatId intentionally left at DefinitionId.None (the default) for every spell attack roll --
        // that resolves from the caster's own spellcasting ability at cast time, so one spell asset works
        // correctly no matter which class casts it.
        var effect = new AttackRollEffect();
        SetField(effect, "onHit", onHit);
        return effect;
    }

    /// <summary>Every spell save DC here uses useCasterSpellSaveDC = true (computed from the caster's own
    /// spellcasting ability at cast time via CharacterState.SpellcastingStatId) rather than a fixed
    /// difficultyClass, since the same spell asset can be cast by different classes.</summary>
    private static SavingThrowEffect SavingThrow(DefinitionId savingStatId, AbilityEffect onFailure, AbilityEffect onSuccess)
    {
        var effect = new SavingThrowEffect();
        SetField(effect, "savingStatId", savingStatId);
        SetField(effect, "useCasterSpellSaveDC", true);
        SetField(effect, "onFailure", onFailure);
        SetField(effect, "onSuccess", onSuccess);
        return effect;
    }

    private static T CreateAsset<T>(string id, string assetName, List<DefinitionAsset> all) where T : DefinitionAsset
    {
        var instance = ScriptableObject.CreateInstance<T>();
        SetField(instance, "id", id);
        var safeName = assetName.Replace(" ", "").Replace(":", "");
        var path = $"{ContentFolder}/{typeof(T).Name}_{safeName}.asset";
        if (AssetDatabase.LoadAssetAtPath<T>(path) != null) AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(instance, path);
        all.Add(instance);
        return instance;
    }

    private static FeatureModifier Modifier(string queryKey, RuleModifierOperation operation, float value)
    {
        object boxed = new FeatureModifier();
        SetField(boxed, "queryKey", queryKey);
        SetField(boxed, "operation", operation);
        SetField(boxed, "value", value);
        SetField(boxed, "priority", 0);
        return (FeatureModifier)boxed;
    }

    private static ChoiceRequirement Choice(string id, string displayName, ChoiceType type, int count, params DefinitionId[] options)
    {
        var requirement = new ChoiceRequirement();
        SetField(requirement, "id", id);
        SetField(requirement, "displayName", displayName);
        SetField(requirement, "type", type);
        SetField(requirement, "count", count);
        SetField(requirement, "options", new List<DefinitionId>(options));
        return requirement;
    }

    private static ResourceGrant ResourceGrantOf(ResourceDefinition resource, int maxAmount)
    {
        object boxed = new ResourceGrant();
        SetField(boxed, "resource", resource);
        SetField(boxed, "maxAmount", maxAmount);
        return (ResourceGrant)boxed;
    }

    private static AbilityCost Cost(ResourceDefinition resource, int amount)
    {
        object boxed = new AbilityCost();
        SetField(boxed, "resource", resource);
        SetField(boxed, "amount", amount);
        return (AbilityCost)boxed;
    }

    private static ClassLevelGrant LevelGrant(int level, params DefinitionId[] featureIds)
    {
        var grant = new ClassLevelGrant();
        SetField(grant, "level", level);
        SetField(grant, "featureIds", new List<DefinitionId>(featureIds));
        return grant;
    }

    private static TargetingDefinition Targeting(TargetMode mode, TargetRelation relation, int minTargets, int maxTargets, float range, float areaSize = 0f)
    {
        var targeting = new TargetingDefinition();
        SetField(targeting, "mode", mode);
        SetField(targeting, "relation", relation);
        SetField(targeting, "minTargets", minTargets);
        SetField(targeting, "maxTargets", maxTargets);
        SetField(targeting, "range", range);
        SetField(targeting, "areaSize", areaSize);
        return targeting;
    }

    private static TargetingDefinition SelfTargeting()
    {
        var targeting = new TargetingDefinition();
        SetField(targeting, "mode", TargetMode.Self);
        SetField(targeting, "relation", TargetRelation.Self);
        SetField(targeting, "minTargets", 1);
        SetField(targeting, "maxTargets", 1);
        return targeting;
    }

    private static void SetField(object target, string fieldName, object value)
    {
        var type = target.GetType();
        while (type != null)
        {
            var field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (field != null)
            {
                field.SetValue(target, value);
                return;
            }

            type = type.BaseType;
        }

        throw new System.Exception($"Field '{fieldName}' not found on {target.GetType().Name} or its base types.");
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

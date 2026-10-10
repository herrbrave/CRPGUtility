using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using CRPG.Abilities;
using CRPG.AI;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Rules;
using CRPG.Rules.SRD5;
using UnityEditor;
using UnityEngine;
using VikingRaid.Game;

namespace VikingRaid.ContentEditor
{
    /// <summary>
    /// Task 2: authors every definition the game's four fights need, from the single design table in <see cref="GameDesign"/>: resources (Movement,
    /// verse uses), the Reckless condition, armour/multiattack features, abilities, one NpcDefinition per combatant prefab, the goblin behaviour sets,
    /// and the <see cref="VikingRaidRulesetProvider"/> that registers it all on top of SRD5. Re-runnable: existing assets are updated in place (GUIDs and
    /// ids stay).
    /// <para>Rules this encodes (each from the Opus review of the design, all test-enforced): every NPC owns Action, Bonus Action, Reaction, Movement and
    /// Hit Points (turn resources are only REFILLED by the encounter, never granted); every one of the six ability scores is authored (a missing stat is 0 =
    /// -5); every AI-usable ability costs Action or Bonus Action; every ability an NPC may use is in its own abilityIds; goblin behaviours carry a pure
    /// 0-HP veto on the TARGET (a wounded living target must never push Attack below Move: see the curve notes below).</para>
    /// </summary>
    public static class GameContentAuthor
    {
        public const string Root = "Assets/Content";
        private const string Srd5 = "Packages/com.crpgtoolkit.core/Rules/SRD5e2014/Content/";
        public const string RulesetPath = Root + "/Ruleset/VikingRaidRuleset.asset";
        public const string HpKey = "TargetResourcePercent:" + GameDesign.Hp;

        [MenuItem("Viking Raid/Task 2/Author Content")]
        public static void Run()
        {
            foreach (var folder in new[] { "Ruleset", "Resources", "Conditions", "Features", "Abilities", "Npcs", "Behaviors" })
                Directory.CreateDirectory($"{Root}/{folder}");
            AssetDatabase.Refresh();

            var content = new List<DefinitionAsset>();
            var hp = Load<CRPG.Actors.ResourceDefinition>(Srd5 + "ResourceDefinition_HitPoints.asset");
            var action = Load<CRPG.Actors.ResourceDefinition>(Srd5 + "ResourceDefinition_Action.asset");
            var bonus = Load<CRPG.Actors.ResourceDefinition>(Srd5 + "ResourceDefinition_BonusAction.asset");
            var reaction = Load<CRPG.Actors.ResourceDefinition>(Srd5 + "ResourceDefinition_Reaction.asset");
            var secondWindUses = Load<CRPG.Actors.ResourceDefinition>(Srd5 + "ResourceDefinition_SecondWindUses.asset");
            var secondWind = Load<AbilityDefinition>(Srd5 + "AbilityDefinition_SecondWind.asset");

            var movement = AuthorResource(GameDesign.Movement, "Movement");
            var verseUses = AuthorResource(GameDesign.VerseUses, "Verses");
            content.Add(movement);
            content.Add(verseUses);

            var reckless = AuthorReckless();
            content.Add(reckless);

            foreach (var bonusAc in GameDesign.ArmorBonuses)
                content.Add(AuthorFeature(GameDesign.ArmorFeatureId(bonusAc), $"Armor (+{bonusAc} AC)", ("ArmorClass", RuleModifierOperation.Add, bonusAc)));
            content.Add(AuthorFeature(GameDesign.MultiattackFeature, "Multiattack", ("AttackCount", RuleModifierOperation.Add, 1)));

            var abilities = new Dictionary<string, AbilityDefinition> { [GameDesign.SecondWindId] = secondWind };
            foreach (var spec in GameDesign.Abilities)
            {
                var ability = AuthorAbility(spec, hp, action, bonus, verseUses, reckless);
                abilities[spec.Id] = ability;
                content.Add(ability);
            }

            foreach (var unit in GameDesign.Units)
                content.Add(AuthorNpc(unit, hp, action, bonus, reaction, movement, verseUses, secondWindUses));

            foreach (var brain in GameDesign.Brains)
                content.AddRange(AuthorBrain(brain, movement));

            var srd5 = Load<SRD5RulesetProvider>(Srd5 + "SRD5RulesetProvider.asset");
            var ruleset = LoadOrCreate<VikingRaidRulesetProvider>(RulesetPath);
            SetField(ruleset, "srd5", srd5);

            // The village (quests, ...) registers its own content in this list; rewriting the list must not drop it.
            foreach (var kept in ruleset.ExtraContent)
                if (kept != null && AssetDatabase.GetAssetPath(kept).StartsWith("Assets/Content/Village/") && !content.Contains(kept)) content.Add(kept);

            SetField(ruleset, "extraContent", content);
            EditorUtility.SetDirty(ruleset);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[GameContentAuthor] authored {content.Count} definitions + the ruleset at {RulesetPath}");
        }

        // ------------------------------------------------------------------ resources / conditions / features
        private static CRPG.Actors.ResourceDefinition AuthorResource(string id, string displayName)
        {
            var resource = LoadOrCreate<CRPG.Actors.ResourceDefinition>($"{Root}/Resources/{id}.asset");
            SetField(resource, "id", id);
            SetField(resource, "displayName", displayName);
            EditorUtility.SetDirty(resource);
            return resource;
        }

        private static FeatureModifier Modifier(string queryKey, RuleModifierOperation operation, float value)
        {
            object boxed = new FeatureModifier();
            SetField(boxed, "queryKey", queryKey);
            SetField(boxed, "operation", operation);
            SetField(boxed, "value", value);
            return (FeatureModifier)boxed;
        }

        /// <summary>Our own Reckless: the SHIPPED condition-reckless gives attackers DISadvantage (op 7 under AttacksAgainst) and never expires.</summary>
        private static ConditionDefinition AuthorReckless()
        {
            var condition = LoadOrCreate<ConditionDefinition>($"{Root}/Conditions/{GameDesign.RecklessCondition}.asset");
            SetField(condition, "id", GameDesign.RecklessCondition);
            SetField(condition, "displayName", "Reckless");
            SetField(condition, "description", "Your attack rolls have advantage, and so do attack rolls against you, until the start of your next turn.");
            SetField(condition, "modifiers", new List<FeatureModifier>
            {
                Modifier("AttackBonus", RuleModifierOperation.Advantage, 0),
                Modifier("AttacksAgainst", RuleModifierOperation.Advantage, 0),
            });
            EditorUtility.SetDirty(condition);
            return condition;
        }

        private static FeatureDefinition AuthorFeature(string id, string displayName, params (string Key, RuleModifierOperation Op, float Value)[] modifiers)
        {
            var feature = LoadOrCreate<FeatureDefinition>($"{Root}/Features/{id}.asset");
            SetField(feature, "id", id);
            SetField(feature, "displayName", displayName);
            SetField(feature, "modifiers", modifiers.Select(m => Modifier(m.Key, m.Op, m.Value)).ToList());
            EditorUtility.SetDirty(feature);
            return feature;
        }

        // ------------------------------------------------------------------ abilities
        private static AbilityCost Cost(CRPG.Actors.ResourceDefinition resource, int amount = 1)
        {
            object boxed = new AbilityCost();
            SetField(boxed, "resource", resource);
            SetField(boxed, "amount", amount);
            return (AbilityCost)boxed;
        }

        private static AbilityDefinition AuthorAbility(AbilitySpec spec, CRPG.Actors.ResourceDefinition hp, CRPG.Actors.ResourceDefinition action,
            CRPG.Actors.ResourceDefinition bonus, CRPG.Actors.ResourceDefinition verseUses, ConditionDefinition reckless)
        {
            var ability = LoadOrCreate<AbilityDefinition>($"{Root}/Abilities/{spec.Id}.asset");
            SetField(ability, "id", spec.Id);
            SetField(ability, "displayName", spec.Name);
            SetField(ability, "description", spec.Description);
            SetField(ability, "recoverSeconds", spec.RecoverSeconds);
            SetField(ability, "prepareSeconds", 0f);
            SetField(ability, "animationCueId", string.IsNullOrEmpty(spec.Cue) ? default : new DefinitionId(spec.Cue));

            var targeting = new TargetingDefinition();
            var dice = spec.Count > 0 ? new DiceExpression(spec.Count, spec.Sides, spec.Modifier) : default; // a self-buff rolls nothing
            var damage = new DamageEffect();
            SetField(damage, "hitPointResource", hp);
            SetField(damage, "amount", dice);

            switch (spec.Kind)
            {
                case AbilityKind.MeleeAttack:
                case AbilityKind.RangedAttack:
                case AbilityKind.SpellAttack:
                {
                    var melee = spec.Kind == AbilityKind.MeleeAttack;
                    SetField(ability, "tags", melee ? AbilityTag.Attack | AbilityTag.Melee | AbilityTag.Weapon | AbilityTag.Hostile
                        : spec.Kind == AbilityKind.SpellAttack ? AbilityTag.Attack | AbilityTag.Spell | AbilityTag.Ranged | AbilityTag.Hostile
                        : AbilityTag.Attack | AbilityTag.Ranged | AbilityTag.Weapon | AbilityTag.Hostile);
                    SetField(ability, "costs", new List<AbilityCost> { Cost(action) });
                    SetField(targeting, "mode", TargetMode.SingleUnit);
                    SetField(targeting, "relation", TargetRelation.Enemy);
                    SetField(targeting, "range", spec.Range);
                    // line of sight only matters for shooters, and only once a game wires an ILineOfSightProvider; melee never needs it
                    SetField(targeting, "requireLineOfSight", !melee);
                    SetField(targeting, "requireLineOfEffect", false);
                    SetField(targeting, "requireVisibleTarget", true);
                    var attack = new AttackRollEffect();
                    // None = the caster's SpellcastingStatId (a spell attack); a weapon names its stat
                    SetField(attack, "governingStatId", spec.Kind == AbilityKind.SpellAttack ? default : new DefinitionId(spec.StatId));
                    SetField(attack, "onHit", damage);
                    SetField(ability, "effects", new List<AbilityEffect> { attack });
                    break;
                }
                case AbilityKind.HealAlly:
                {
                    SetField(ability, "tags", AbilityTag.Healing);
                    SetField(ability, "costs", new List<AbilityCost> { Cost(bonus), Cost(verseUses) });
                    SetField(targeting, "mode", TargetMode.SingleUnit);
                    SetField(targeting, "relation", TargetRelation.Ally);
                    SetField(targeting, "range", spec.Range);
                    SetField(targeting, "requireLineOfSight", false);
                    SetField(targeting, "requireLineOfEffect", false);
                    var heal = new HealEffect();
                    SetField(heal, "hitPointResource", hp);
                    SetField(heal, "amount", dice);
                    SetField(ability, "effects", new List<AbilityEffect> { heal });
                    break;
                }
                case AbilityKind.SelfBuff:
                {
                    SetField(ability, "tags", AbilityTag.Utility);
                    SetField(ability, "costs", new List<AbilityCost> { Cost(bonus) });
                    // Mode AND relation Self: the combat UI only auto-casts Self-mode abilities
                    SetField(targeting, "mode", TargetMode.Self);
                    SetField(targeting, "relation", TargetRelation.Self);
                    SetField(targeting, "range", 0f);
                    SetField(targeting, "requireLineOfSight", false);
                    SetField(targeting, "requireLineOfEffect", false);
                    var apply = new ApplyConditionEffect();
                    SetField(apply, "condition", reckless);
                    SetField(apply, "duration", 1);
                    SetField(apply, "boundary", DurationBoundary.StartOfTargetTurn);
                    SetField(ability, "effects", new List<AbilityEffect> { apply });
                    break;
                }
            }

            SetField(ability, "targeting", targeting);
            EditorUtility.SetDirty(ability);
            return ability;
        }

        // ------------------------------------------------------------------ combatants
        private static ResourceGrant Grant(CRPG.Actors.ResourceDefinition resource, int max)
        {
            object boxed = new ResourceGrant();
            SetField(boxed, "resource", resource);
            SetField(boxed, "maxAmount", max);
            return (ResourceGrant)boxed;
        }

        private static StatScoreEntry Score(string statId, int score)
        {
            object boxed = new StatScoreEntry();
            SetField(boxed, "statId", new DefinitionId(statId));
            SetField(boxed, "score", score);
            return (StatScoreEntry)boxed;
        }

        private static NpcDefinition AuthorNpc(UnitSpec unit, CRPG.Actors.ResourceDefinition hp, CRPG.Actors.ResourceDefinition action,
            CRPG.Actors.ResourceDefinition bonus, CRPG.Actors.ResourceDefinition reaction, CRPG.Actors.ResourceDefinition movement,
            CRPG.Actors.ResourceDefinition verseUses, CRPG.Actors.ResourceDefinition secondWindUses)
        {
            var npc = LoadOrCreate<NpcDefinition>($"{Root}/Npcs/{unit.NpcId}.asset");
            SetField(npc, "id", unit.NpcId);
            SetField(npc, "displayName", unit.DisplayName);
            SetField(npc, "faction", new DefinitionId(unit.Hero ? GameDesign.HeroesFaction : GameDesign.GoblinsFaction));
            SetField(npc, "level", unit.Level);
            // ALL six scores: a missing stat reads 0 = a -5 modifier (AC 5, -5 to hit)
            SetField(npc, "abilityScores", new List<StatScoreEntry>
            {
                Score(GameDesign.Str, unit.Str), Score(GameDesign.Dex, unit.Dex), Score(GameDesign.Con, unit.Con),
                Score(GameDesign.Int, unit.Int), Score(GameDesign.Wis, unit.Wis), Score(GameDesign.Cha, unit.Cha),
            });

            // turn resources are only REFILLED by the encounter, never granted: every NPC must own all four, plus Hit Points
            var resources = new List<ResourceGrant>
            {
                Grant(hp, unit.Hp), Grant(action, 1), Grant(bonus, 1), Grant(reaction, 1), Grant(movement, unit.Movement),
            };
            if (unit.SecondWindUses > 0) resources.Add(Grant(secondWindUses, unit.SecondWindUses));
            if (unit.VerseUses > 0) resources.Add(Grant(verseUses, unit.VerseUses));
            SetField(npc, "resources", resources);

            var features = new List<DefinitionId> { new(GameDesign.ArmorFeatureId(unit.ArmorBonus)) };
            if (unit.Multiattack) features.Add(new DefinitionId(GameDesign.MultiattackFeature));
            SetField(npc, "featureIds", features);
            SetField(npc, "abilityIds", unit.Abilities.Select(a => new DefinitionId(a)).ToList());
            SetField(npc, "spellcastingStatId", string.IsNullOrEmpty(unit.SpellcastingStatId) ? default : new DefinitionId(unit.SpellcastingStatId));

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(unit.Prefab);
            if (prefab == null) throw new InvalidOperationException($"{unit.Prefab} is missing -- run 'Viking Raid/Task 1/Author Characters' first.");
            SetField(npc, "visualPrefab", prefab);
            EditorUtility.SetDirty(npc);
            return npc;
        }

        // ------------------------------------------------------------------ behaviour sets
        /// <summary>
        /// Curve rules (see Section 12 of the design doc). UtilityMath.CombineScores applies a compensation factor, so with TWO considerations Move's ceiling
        /// rises 0.6 -> 0.72: an Attack whose per-consideration factor drops below ~0.73 for a LIVING target would lose to Move and the goblin would shuffle
        /// forever. So the HP consideration is a PURE 0-HP veto -- (0,0),(0.01,1),(1,1) -- on BOTH Move and the attacks: a downed target scores 0 and is never
        /// chased or attacked while a living one exists. The input key must be exactly TargetResourcePercent:resource-hp (a typo reads as 0 = every attack vetoed).
        /// </summary>
        private static IEnumerable<DefinitionAsset> AuthorBrain(BrainSpec brain, CRPG.Actors.ResourceDefinition movement)
        {
            var ids = new List<DefinitionId>();
            var assets = new List<DefinitionAsset>();

            var close = AuthorBehavior($"{brain.Id}-close", "Close Distance", BehaviorKind.Move);
            SetField(close, "movementResourceId", new DefinitionId(GameDesign.Movement));
            SetField(close, "moveTargetRelation", TargetRelation.Enemy);
            SetField(close, "stopDistance", brain.StopDistance);
            SetField(close, "considerations", new List<Consideration>
            {
                Distance(brain.RangedAbilityId != null
                    ? new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(20f, 0.1f))
                    : new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(2f, 0.5f), new Keyframe(20f, 0.1f))),
                HpVeto(),
            });
            assets.Add(close);

            if (brain.MeleeAbilityId != null)
            {
                var melee = AuthorBehavior($"{brain.Id}-melee", "Melee", BehaviorKind.UseAbility);
                SetField(melee, "abilityId", new DefinitionId(brain.MeleeAbilityId));
                // a shooter's melee fallback outranks its bow when adjacent (1 -> 0.95 vs 0.9 -> 0.86); a pure melee unit's attack floor 0.85 > Move's 0.6 ceiling
                SetField(melee, "considerations", new List<Consideration>
                {
                    Distance(brain.RangedAbilityId != null
                        ? new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(2f, 0.95f))
                        : new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(2f, 0.85f))),
                    HpVeto(),
                });
                assets.Add(melee);
            }

            if (brain.RangedAbilityId != null)
            {
                var ranged = AuthorBehavior($"{brain.Id}-ranged", "Ranged", BehaviorKind.UseAbility);
                SetField(ranged, "abilityId", new DefinitionId(brain.RangedAbilityId));
                SetField(ranged, "considerations", new List<Consideration>
                {
                    Distance(new AnimationCurve(new Keyframe(0f, 0.9f), new Keyframe(brain.RangedRange, 0.86f))),
                    HpVeto(),
                });
                assets.Add(ranged);
            }

            ids.AddRange(assets.Select(a => a.Id));
            var set = LoadOrCreate<BehaviorSetDefinition>($"{Root}/Behaviors/{brain.Id}.asset");
            SetField(set, "id", brain.Id);
            SetField(set, "displayName", brain.Id);
            SetField(set, "behaviorIds", ids);
            EditorUtility.SetDirty(set);
            assets.Add(set);
            return assets;
        }

        private static BehaviorDefinition AuthorBehavior(string id, string displayName, BehaviorKind kind)
        {
            var behavior = LoadOrCreate<BehaviorDefinition>($"{Root}/Behaviors/{id}.asset");
            SetField(behavior, "id", id);
            SetField(behavior, "displayName", displayName);
            SetField(behavior, "kind", kind);
            EditorUtility.SetDirty(behavior);
            return behavior;
        }

        private static Consideration Distance(AnimationCurve curve)
        {
            var c = new Consideration();
            SetField(c, "inputKey", "Distance");
            SetField(c, "curve", curve);
            SetField(c, "weight", 1f);
            return c;
        }

        private static Consideration HpVeto()
        {
            var c = new Consideration();
            SetField(c, "inputKey", HpKey);
            SetField(c, "curve", new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.01f, 1f), new Keyframe(1f, 1f)));
            SetField(c, "weight", 1f);
            return c;
        }

        // ------------------------------------------------------------------ plumbing
        private static T Load<T>(string path) where T : UnityEngine.Object =>
            AssetDatabase.LoadAssetAtPath<T>(path) ?? throw new InvalidOperationException($"{path} is missing.");

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) return existing;
            var created = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(created, path);
            return created;
        }

        private static void SetField(object target, string name, object value)
        {
            var type = target.GetType();
            while (type != null)
            {
                var field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
                if (field != null) { field.SetValue(target, value); return; }
                type = type.BaseType;
            }

            throw new Exception($"Field '{name}' not found on {target.GetType().Name}.");
        }
    }
}

using System.Collections.Generic;
using CRPG.Abilities;
using CRPG.AI;
using CRPG.Actors;
using CRPG.Core;
using UnityEditor;
using UnityEngine;

namespace CRPG.Samples.TacticalEncounter.Editor
{
    /// <summary>
    /// Authors the TacticalEncounter sample's own combat content: a real melee-weapon-attack ability (SRD5e2014
    /// ships no such ability today -- AttackRollEffect is only ever used by 4 ranged spells -- so this mirrors
    /// TacticalSliceContentAuthor.AuthorMeleeAbility's recipe, adapted to reference the package's own real
    /// shipped Strength/HitPoints/Action/Reaction content directly instead of a TestProject-only bundle),
    /// Opportunity Attack + the Reactive Strikes feature granting it, a sample-owned Movement resource (no
    /// ResourceDefinition_Movement ships in SRD5e2014 either), and the "Foe Warrior" Behavior content driving
    /// the sample's AI. Re-runnable: edits an already-existing asset in place (SetField + SetDirty), never
    /// deletes/recreates.
    /// <para>
    /// The two Behavior curves are the exact values that make a legal attack structurally dominant over
    /// movement (found the hard way -- a real defect in the naive "empty Considerations" approach the
    /// TacticalSlice dogfood precedent uses, see Tests/Fixtures/TacticalEncounterVerification/
    /// TacticalAiScoringTests.cs, which proves these exact curves against the real UtilityAIDecisionProvider
    /// before this script was ever run): Melee Strike Nearest's Distance curve floors at 0.85; Close Distance's
    /// ceilings at 0.6 -- so whenever an attack is legal it always outscores movement, never a tie.
    /// </para>
    /// </summary>
    internal static class TacticalEncounterContentAuthor
    {
        private const string ContentFolder = "Packages/com.crpgtoolkit.core/Samples/TacticalEncounter/Content";
        private const string Srd5ContentFolder = "Packages/com.crpgtoolkit.core/Rules/SRD5e2014/Content";
        private const float MeleeRange = 2f;
        private const float MoveStopDistance = 1.5f;

        [MenuItem("CRPG Toolkit/Samples/Tactical Encounter/1. Author Content")]
        public static void Author()
        {
            EnsureFolder(ContentFolder);

            var strength = LoadSrd5<StatDefinition>("StatDefinition_Strength");
            var hitPoints = LoadSrd5<ResourceDefinition>("ResourceDefinition_HitPoints");
            var action = LoadSrd5<ResourceDefinition>("ResourceDefinition_Action");
            var reaction = LoadSrd5<ResourceDefinition>("ResourceDefinition_Reaction");
            if (strength == null || hitPoints == null || action == null || reaction == null)
            {
                Debug.LogError("[TacticalEncounterContentAuthor] Could not load one or more real SRD5 assets -- aborting.");
                return;
            }

            var movement = CreateOrLoad<ResourceDefinition>($"{ContentFolder}/ResourceDefinition_Movement.asset");
            SetField(movement, "id", "resource-tacticalencounter-movement");
            SetField(movement, "displayName", "Movement");

            var meleeStrike = CreateOrLoad<AbilityDefinition>($"{ContentFolder}/AbilityDefinition_MeleeStrike.asset");
            AuthorMeleeAbility(
                meleeStrike, "ability-tacticalencounter-meleestrike", "Melee Strike",
                "A basic weapon attack against a creature within melee reach.",
                AbilityTag.Attack | AbilityTag.Melee, action, 1, strength, hitPoints);

            var opportunityAttack = CreateOrLoad<AbilityDefinition>($"{ContentFolder}/AbilityDefinition_OpportunityAttack.asset");
            AuthorMeleeAbility(
                opportunityAttack, "ability-tacticalencounter-opportunityattack", "Opportunity Attack",
                "A reflexive weapon attack against a creature that leaves your reach.",
                AbilityTag.Attack | AbilityTag.Melee | AbilityTag.Reaction, reaction, 1, strength, hitPoints);

            var reactiveStrikes = CreateOrLoad<FeatureDefinition>($"{ContentFolder}/FeatureDefinition_ReactiveStrikes.asset");
            SetField(reactiveStrikes, "id", "feature-tacticalencounter-reactivestrikes");
            SetField(reactiveStrikes, "displayName", "Reactive Strikes");
            SetField(reactiveStrikes, "description", "Grants Opportunity Attack -- a reflexive weapon attack when a creature leaves your reach.");
            SetField(reactiveStrikes, "grantedAbilityIds", new List<DefinitionId> { opportunityAttack.Id });

            var closeDistance = CreateOrLoad<BehaviorDefinition>($"{ContentFolder}/BehaviorDefinition_CloseDistance.asset");
            SetField(closeDistance, "id", "behavior-tacticalencounter-closedistance");
            SetField(closeDistance, "displayName", "Close Distance");
            SetField(closeDistance, "kind", BehaviorKind.Move);
            SetField(closeDistance, "movementResourceId", movement.Id);
            SetField(closeDistance, "moveTargetRelation", TargetRelation.Enemy);
            SetField(closeDistance, "stopDistance", MoveStopDistance);
            SetField(closeDistance, "considerations", new List<Consideration>
            {
                CreateDistanceConsideration(new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(MeleeRange, 0.5f), new Keyframe(20f, 0.1f))),
            });

            var meleeStrikeNearest = CreateOrLoad<BehaviorDefinition>($"{ContentFolder}/BehaviorDefinition_MeleeStrikeNearest.asset");
            SetField(meleeStrikeNearest, "id", "behavior-tacticalencounter-meleestrikenearest");
            SetField(meleeStrikeNearest, "displayName", "Melee Strike Nearest");
            SetField(meleeStrikeNearest, "kind", BehaviorKind.UseAbility);
            SetField(meleeStrikeNearest, "abilityId", meleeStrike.Id);
            SetField(meleeStrikeNearest, "considerations", new List<Consideration>
            {
                CreateDistanceConsideration(new AnimationCurve(new Keyframe(0f, 1.0f), new Keyframe(MeleeRange, 0.85f))),
            });

            var foeWarrior = CreateOrLoad<BehaviorSetDefinition>($"{ContentFolder}/BehaviorSetDefinition_FoeWarrior.asset");
            SetField(foeWarrior, "id", "behaviorset-tacticalencounter-foewarrior");
            SetField(foeWarrior, "displayName", "Foe Warrior");
            SetField(foeWarrior, "behaviorIds", new List<DefinitionId> { closeDistance.Id, meleeStrikeNearest.Id });

            foreach (var asset in new Object[] { movement, meleeStrike, opportunityAttack, reactiveStrikes, closeDistance, meleeStrikeNearest, foeWarrior })
                EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();

            Debug.Log($"[TacticalEncounterContentAuthor] Authored TacticalEncounter content under {ContentFolder}.");
        }

        private static void AuthorMeleeAbility(
            AbilityDefinition asset, string id, string displayName, string description, AbilityTag tags,
            ResourceDefinition costResource, int costAmount, StatDefinition governingStat, ResourceDefinition hitPointResource)
        {
            SetField(asset, "id", id);
            SetField(asset, "displayName", displayName);
            SetField(asset, "description", description);
            SetField(asset, "tags", tags);

            // AbilityCost is a struct -- SetField must mutate one boxed instance and unbox it back, never a
            // bare local, same established fix TacticalSliceContentAuthor/TacticalTestFactory already use.
            object boxedCost = new AbilityCost();
            SetField(boxedCost, "resource", costResource);
            SetField(boxedCost, "amount", costAmount);
            SetField(asset, "costs", new List<AbilityCost> { (AbilityCost)boxedCost });

            var targeting = new TargetingDefinition();
            SetField(targeting, "mode", TargetMode.SingleUnit);
            SetField(targeting, "relation", TargetRelation.Enemy);
            SetField(targeting, "minTargets", 1);
            SetField(targeting, "maxTargets", 1);
            SetField(targeting, "range", MeleeRange);
            // Line-of-sight/line-of-effect deliberately not required: at a 2-unit melee range, two adjacent
            // combatants have line of sight to each other in every realistic case this small sample's own
            // single wall obstacle produces -- kept simple rather than wiring a physics-based
            // ILineOfSightProvider for a distinction that barely matters at melee range.
            SetField(targeting, "requireLineOfSight", false);
            SetField(targeting, "requireLineOfEffect", false);
            SetField(targeting, "requireVisibleTarget", true);
            SetField(asset, "targeting", targeting);

            var damage = new DamageEffect();
            SetField(damage, "hitPointResource", hitPointResource);
            SetField(damage, "amount", new DiceExpression(1, 8, 3));

            var attack = new AttackRollEffect();
            SetField(attack, "governingStatId", governingStat.Id);
            SetField(attack, "onHit", damage);
            SetField(asset, "effects", new List<AbilityEffect> { attack });

            SetField(asset, "prepareSeconds", 0f);
            SetField(asset, "recoverSeconds", 0f);
        }

        private static Consideration CreateDistanceConsideration(AnimationCurve curve)
        {
            var consideration = new Consideration();
            SetField(consideration, "inputKey", "Distance");
            SetField(consideration, "curve", curve);
            SetField(consideration, "weight", 1f);
            return consideration;
        }

        private static T CreateOrLoad<T>(string path) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) return existing;

            var created = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(created, path);
            return created;
        }

        private static T LoadSrd5<T>(string assetName) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>($"{Srd5ContentFolder}/{assetName}.asset");
            if (asset == null) Debug.LogError($"[TacticalEncounterContentAuthor] Could not load {assetName} from {Srd5ContentFolder}.");
            return asset;
        }

        private static void SetField(object target, string fieldName, object value)
        {
            var type = target.GetType();
            while (type != null)
            {
                var field = type.GetField(fieldName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly);
                if (field != null) { field.SetValue(target, value); return; }
                type = type.BaseType;
            }

            Debug.LogError($"[TacticalEncounterContentAuthor] Field '{fieldName}' not found on {target.GetType().Name}.");
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
            var folderName = System.IO.Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(string.IsNullOrEmpty(parent) ? "Assets" : parent, folderName);
        }
    }
}

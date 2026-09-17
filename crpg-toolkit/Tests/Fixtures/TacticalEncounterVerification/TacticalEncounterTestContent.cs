using System.Collections.Generic;
using System.Reflection;
using CRPG.Abilities;
using CRPG.AI;
using CRPG.Actors;
using CRPG.Combat;
using CRPG.Core;
using UnityEngine;

namespace CRPG.Tests.Fixtures.TacticalEncounterVerification
{
    /// <summary>Same shape as the CRPG.Tests.PlayMode assembly's own internal TestPositionProvider
    /// (Tests/Runtime/TacticalActionTests.cs) -- can't reference that one directly (different assembly, and
    /// internal), so this is a deliberate, small duplicate: fixed positions for pure-logic tests, live
    /// Transform tracking for tests that spawn real GameObjects.</summary>
    internal sealed class TestPositionProvider : IPositionProvider
    {
        private readonly Dictionary<RuntimeEntityId, Vector3> fixedPositions = new();
        private readonly Dictionary<RuntimeEntityId, Transform> liveTransforms = new();

        public void Set(RuntimeEntityId id, Vector3 position) => fixedPositions[id] = position;
        public void Track(RuntimeEntityId id, Transform transform) => liveTransforms[id] = transform;

        public Vector3 GetPosition(RuntimeEntityId entity)
        {
            if (liveTransforms.TryGetValue(entity, out var transform)) return transform.position;
            return fixedPositions.TryGetValue(entity, out var position) ? position : Vector3.zero;
        }
    }

    internal sealed class NoOpMovementController : IMovementController
    {
        public float TryMoveTowards(RuntimeEntityId entity, Vector3 destination, float maxDistance) => 0f;
    }

    /// <summary>A plain in-memory ICombatLog so tests can assert real mechanics actually happened (an attack
    /// roll, a reaction) rather than only inferring it from a side effect like a HP change.</summary>
    internal sealed class RecordingCombatLog : ICombatLog
    {
        private readonly List<CombatLogEntry> entries = new();
        public IReadOnlyList<CombatLogEntry> Entries => entries;
        public event System.Action<CombatLogEntry> EntryLogged;
        public void Log(CombatLogEntry entry) { entries.Add(entry); EntryLogged?.Invoke(entry); }
    }

    /// <summary>
    /// Builds the TacticalEncounter sample's own content in memory, matching exactly what
    /// TacticalEncounterContentAuthor.cs (Editor-only, Samples~-only, unreachable from this assembly) writes to
    /// real .asset files -- same reflection-based ScriptableObject construction technique
    /// TestDefinitionFactory/TacticalSliceContentAuthor already use elsewhere in this project. Deliberately
    /// duplicated here rather than shared: this fixture never references a sample's own assembly (the sample
    /// assembly doesn't exist until imported -- same constraint BasicCrpgVerificationTests/
    /// CharacterCreatorVerificationTests already established), and the real author script's Editor-only
    /// AssetDatabase calls aren't meaningful here anyway -- this fixture never loads a shipped .asset, it
    /// builds fresh, isolated instances every test (see TacticalEncounterVerificationTests' own isolation
    /// rationale).
    /// <para>
    /// The two Behavior curves are the exact values Design decisions settled on after finding a real defect in
    /// the naive empty-Considerations approach (see the plan): Attack's floor (0.85) must always beat
    /// Movement's ceiling (0.6) so a legal attack is never starved by a tied, score-0 Move decision.
    /// </para>
    /// </summary>
    internal static class TacticalEncounterTestContent
    {
        public const float MeleeRange = 2f;
        public const float MoveStopDistance = 1.5f;

        public static StatDefinition CreateStat(string id, string displayName)
        {
            var stat = ScriptableObject.CreateInstance<StatDefinition>();
            SetField(stat, "id", id);
            SetField(stat, "displayName", displayName);
            return stat;
        }

        public static ResourceDefinition CreateResource(string id, string displayName)
        {
            var resource = ScriptableObject.CreateInstance<ResourceDefinition>();
            SetField(resource, "id", id);
            SetField(resource, "displayName", displayName);
            return resource;
        }

        /// <summary>Mirrors TacticalSliceContentAuthor.AuthorMeleeAbility's recipe exactly (AttackRollEffect
        /// wrapping a DamageEffect, Strength-governed, costs the Action resource) -- adapted to take its
        /// resource/stat dependencies as explicit parameters instead of a TestProject-only content bundle.</summary>
        public static AbilityDefinition CreateMeleeAbility(
            string id, string displayName, AbilityTag tags, ResourceDefinition costResource, int costAmount,
            DefinitionId governingStatId, ResourceDefinition hitPointResource, DiceExpression damage,
            float prepareSeconds, float recoverSeconds)
        {
            var asset = ScriptableObject.CreateInstance<AbilityDefinition>();
            SetField(asset, "id", id);
            SetField(asset, "displayName", displayName);
            SetField(asset, "tags", tags);

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
            SetField(targeting, "requireLineOfSight", false);
            SetField(targeting, "requireLineOfEffect", false);
            SetField(targeting, "requireVisibleTarget", true);
            SetField(asset, "targeting", targeting);

            var damageEffect = new DamageEffect();
            SetField(damageEffect, "hitPointResource", hitPointResource);
            SetField(damageEffect, "amount", damage);

            var attack = new AttackRollEffect();
            SetField(attack, "governingStatId", governingStatId);
            SetField(attack, "onHit", damageEffect);
            SetField(asset, "effects", new List<AbilityEffect> { attack });

            SetField(asset, "prepareSeconds", prepareSeconds);
            SetField(asset, "recoverSeconds", recoverSeconds);
            return asset;
        }

        public static FeatureDefinition CreateFeatureGrantingAbility(string id, string displayName, DefinitionId grantedAbilityId)
        {
            var feature = ScriptableObject.CreateInstance<FeatureDefinition>();
            SetField(feature, "id", id);
            SetField(feature, "displayName", displayName);
            SetField(feature, "grantedAbilityIds", new List<DefinitionId> { grantedAbilityId });
            return feature;
        }

        public static BehaviorDefinition CreateCloseDistanceBehavior(string id, string displayName, DefinitionId movementResourceId)
        {
            var curve = new AnimationCurve(
                new Keyframe(0f, 0.6f),
                new Keyframe(MeleeRange, 0.5f),
                new Keyframe(20f, 0.1f));

            var behavior = ScriptableObject.CreateInstance<BehaviorDefinition>();
            SetField(behavior, "id", id);
            SetField(behavior, "displayName", displayName);
            SetField(behavior, "kind", BehaviorKind.Move);
            SetField(behavior, "movementResourceId", movementResourceId);
            SetField(behavior, "moveTargetRelation", TargetRelation.Enemy);
            SetField(behavior, "stopDistance", MoveStopDistance);
            SetField(behavior, "considerations", new List<Consideration> { CreateDistanceConsideration(curve) });
            return behavior;
        }

        public static BehaviorDefinition CreateMeleeStrikeNearestBehavior(string id, string displayName, DefinitionId abilityId)
        {
            var curve = new AnimationCurve(
                new Keyframe(0f, 1.0f),
                new Keyframe(MeleeRange, 0.85f));

            var behavior = ScriptableObject.CreateInstance<BehaviorDefinition>();
            SetField(behavior, "id", id);
            SetField(behavior, "displayName", displayName);
            SetField(behavior, "kind", BehaviorKind.UseAbility);
            SetField(behavior, "abilityId", abilityId);
            SetField(behavior, "considerations", new List<Consideration> { CreateDistanceConsideration(curve) });
            return behavior;
        }

        public static BehaviorSetDefinition CreateBehaviorSet(string id, string displayName, params DefinitionId[] behaviorIds)
        {
            var set = ScriptableObject.CreateInstance<BehaviorSetDefinition>();
            SetField(set, "id", id);
            SetField(set, "displayName", displayName);
            SetField(set, "behaviorIds", new List<DefinitionId>(behaviorIds));
            return set;
        }

        private static Consideration CreateDistanceConsideration(AnimationCurve curve)
        {
            var consideration = new Consideration();
            SetField(consideration, "inputKey", "Distance");
            SetField(consideration, "curve", curve);
            SetField(consideration, "weight", 1f);
            return consideration;
        }

        public sealed class SampleContent
        {
            public RulesDatabase RulesDatabase { get; set; }
            public AbilityDefinition MeleeStrike { get; set; }
            public AbilityDefinition OpportunityAttack { get; set; }
            public FeatureDefinition ReactiveStrikes { get; set; }
            public BehaviorSetDefinition FoeWarrior { get; set; }
            public DefinitionId StrengthId { get; set; }
            public ResourceDefinition HitPoints { get; set; }
            public ResourceDefinition Action { get; set; }
            public ResourceDefinition Reaction { get; set; }
            public ResourceDefinition Movement { get; set; }
            public DefinitionId HitPointsId => HitPoints.Id;
            public DefinitionId ActionId => Action.Id;
            public DefinitionId ReactionId => Reaction.Id;
            public DefinitionId MovementId => Movement.Id;
        }

        /// <summary>The sample's full real content set -- Melee Strike, Opportunity Attack, Reactive Strikes,
        /// and the "Foe Warrior" Behavior/BehaviorSet -- everything TacticalEncounterContentAuthor.cs authors
        /// as real .asset files, built here in memory for isolated tests. A fresh instance every call (a fresh
        /// RulesDatabase too) -- tests must never share one of these across test methods.</summary>
        public static SampleContent BuildFullSampleContent()
        {
            var rulesDatabase = new RulesDatabase();

            var strength = CreateStat("stat-strength", "Strength");
            var hitPoints = CreateResource("resource-hitpoints", "Hit Points");
            var action = CreateResource("resource-action", "Action");
            var reaction = CreateResource("resource-reaction", "Reaction");
            var movement = CreateResource("resource-movement", "Movement");
            rulesDatabase.Register(strength);
            rulesDatabase.Register(hitPoints);
            rulesDatabase.Register(action);
            rulesDatabase.Register(reaction);
            rulesDatabase.Register(movement);

            var meleeStrike = CreateMeleeAbility(
                "ability-meleestrike", "Melee Strike", AbilityTag.Attack | AbilityTag.Melee,
                action, 1, strength.Id, hitPoints, new DiceExpression(1, 8, 3), prepareSeconds: 0f, recoverSeconds: 0f);
            rulesDatabase.Register(meleeStrike);

            var opportunityAttack = CreateMeleeAbility(
                "ability-opportunityattack", "Opportunity Attack", AbilityTag.Attack | AbilityTag.Melee | AbilityTag.Reaction,
                reaction, 1, strength.Id, hitPoints, new DiceExpression(1, 8, 3), prepareSeconds: 0f, recoverSeconds: 0f);
            rulesDatabase.Register(opportunityAttack);

            var reactiveStrikes = CreateFeatureGrantingAbility("feature-reactivestrikes", "Reactive Strikes", opportunityAttack.Id);
            rulesDatabase.Register(reactiveStrikes);

            var closeDistance = CreateCloseDistanceBehavior("behavior-closedistance", "Close Distance", movement.Id);
            var meleeStrikeNearest = CreateMeleeStrikeNearestBehavior("behavior-meleestrikenearest", "Melee Strike Nearest", meleeStrike.Id);
            rulesDatabase.Register(closeDistance);
            rulesDatabase.Register(meleeStrikeNearest);

            var foeWarrior = CreateBehaviorSet("behaviorset-foewarrior", "Foe Warrior", closeDistance.Id, meleeStrikeNearest.Id);
            rulesDatabase.Register(foeWarrior);

            return new SampleContent
            {
                RulesDatabase = rulesDatabase,
                MeleeStrike = meleeStrike,
                OpportunityAttack = opportunityAttack,
                ReactiveStrikes = reactiveStrikes,
                FoeWarrior = foeWarrior,
                StrengthId = strength.Id,
                HitPoints = hitPoints,
                Action = action,
                Reaction = reaction,
                Movement = movement,
            };
        }

        public static void SetField(object target, string fieldName, object value)
        {
            var type = target.GetType();
            while (type != null)
            {
                var field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (field != null) { field.SetValue(target, value); return; }
                type = type.BaseType;
            }

            Debug.LogError($"[TacticalEncounterTestContent] Field '{fieldName}' not found anywhere in {target.GetType()}'s hierarchy.");
        }
    }
}

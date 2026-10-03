using System.Collections.Generic;
using System.Reflection;
using CRPG.AI;
using CRPG.Abilities;
using CRPG.Core;
using UnityEngine;

namespace BarrowsEnd.Gate
{
    /// <summary>Builds and registers hostile behavior sets at runtime (no authored asset needed). Every behavior
    /// carries a Distance consideration: an empty consideration list scores 0, and the decision provider only
    /// replaces a decision on a strictly higher score, so without curves a Move behavior listed first always wins
    /// and a hostile in melee range never attacks. Attack's lowest score (0.85) must stay above Move's highest
    /// (0.6) -- the same curves the toolkit's own tactical verification content settled on.</summary>
    public static class HostileBrainKit
    {
        public const float MeleeRange = 2f;

        public static BehaviorSetDefinition BuildMeleeSet(RulesDatabase rulesDatabase, string setId, string displayName, string abilityId, float stopDistance)
        {
            var close = Create(rulesDatabase, $"{setId}-close", "Close Distance", BehaviorKind.Move);
            SetField(close, "movementResourceId", new DefinitionId(TacticalCombatSession.MovementResourceId));
            SetField(close, "moveTargetRelation", TargetRelation.Enemy);
            SetField(close, "stopDistance", stopDistance);
            SetField(close, "considerations", new List<Consideration>
            {
                DistanceConsideration(new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(MeleeRange, 0.5f), new Keyframe(20f, 0.1f))),
            });

            var attack = Create(rulesDatabase, $"{setId}-attack", $"{displayName} Attack", BehaviorKind.UseAbility);
            SetField(attack, "abilityId", new DefinitionId(abilityId));
            SetField(attack, "considerations", new List<Consideration>
            {
                DistanceConsideration(new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(MeleeRange, 0.85f))),
            });

            var set = ScriptableObject.CreateInstance<BehaviorSetDefinition>();
            SetField(set, "id", setId);
            SetField(set, "displayName", displayName);
            SetField(set, "behaviorIds", new List<DefinitionId> { close.Id, attack.Id });
            rulesDatabase.Register(set);
            return set;
        }

        private static BehaviorDefinition Create(RulesDatabase rulesDatabase, string id, string displayName, BehaviorKind kind)
        {
            var behavior = ScriptableObject.CreateInstance<BehaviorDefinition>();
            SetField(behavior, "id", id);
            SetField(behavior, "displayName", displayName);
            SetField(behavior, "kind", kind);
            rulesDatabase.Register(behavior);
            return behavior;
        }

        private static Consideration DistanceConsideration(AnimationCurve curve)
        {
            var consideration = new Consideration();
            SetField(consideration, "inputKey", "Distance");
            SetField(consideration, "curve", curve);
            SetField(consideration, "weight", 1f);
            return consideration;
        }

        private static void SetField(object target, string fieldName, object value)
        {
            var type = target.GetType();
            while (type != null)
            {
                var field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (field != null) { field.SetValue(target, value); return; }
                type = type.BaseType;
            }

            throw new System.Exception($"Field '{fieldName}' not found on {target.GetType().Name}.");
        }
    }
}

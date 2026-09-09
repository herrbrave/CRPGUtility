using System.Collections.Generic;
using CRPG.Abilities;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Rules;
using UnityEngine;

namespace CRPG.AI
{
    /// <summary>
    /// The Brain: evaluates every Behavior in a BehaviorSetDefinition against every valid candidate target,
    /// scores each (Behavior, Target) Decision via UtilityMath, and returns the highest-scoring one. The
    /// BehaviorSetDefinition already *is* the authored content -- this class is just the code that evaluates
    /// it, the same way CampPresenter is code wrapping data rather than being data itself.
    /// </summary>
    public sealed class UtilityAIDecisionProvider : ITurnDecisionProvider
    {
        private readonly BehaviorSetDefinition behaviorSet;
        private readonly IRulesDatabase rulesDatabase;
        private readonly ICharacterRegistry characters;
        private readonly IAbilityResolutionService abilityResolution;
        private readonly IPositionProvider positions;
        private readonly IRandomSource randomSource;
        private readonly ConsiderationInputRegistry inputRegistry;
        private readonly ICombatLog combatLog;

        public UtilityAIDecisionProvider(
            BehaviorSetDefinition behaviorSet,
            IRulesDatabase rulesDatabase,
            ICharacterRegistry characters,
            IAbilityResolutionService abilityResolution,
            IPositionProvider positions,
            IRandomSource randomSource,
            ConsiderationInputRegistry inputRegistry = null,
            ICombatLog combatLog = null)
        {
            this.behaviorSet = behaviorSet;
            this.rulesDatabase = rulesDatabase;
            this.characters = characters;
            this.abilityResolution = abilityResolution;
            this.positions = positions;
            this.randomSource = randomSource;
            this.inputRegistry = inputRegistry ?? new ConsiderationInputRegistry();
            this.combatLog = combatLog;
        }

        public TurnDecision GetDecision(TurnContext context)
        {
            TurnDecision best = null;
            var bestScore = float.NegativeInfinity;
            var bestBehaviorName = string.Empty;
            var trace = new List<UtilityDecisionTraceEntry>();

            foreach (var behaviorId in behaviorSet.BehaviorIds)
            {
                if (!rulesDatabase.TryResolve<BehaviorDefinition>(behaviorId, out var behavior)) continue;

                if (behavior.Kind == BehaviorKind.Move)
                {
                    ScoreMoveBehavior(behavior, context, ref best, ref bestScore, ref bestBehaviorName, trace);
                    continue;
                }

                if (!rulesDatabase.TryResolve<AbilityDefinition>(behavior.AbilityId, out var ability)) continue;
                if (ability.Targeting.MaxTargets != 1) continue; // v1 scope: single-target abilities only
                if (!abilityResolution.CanUse(context.Self, ability)) continue;

                var selfPosition = positions.GetPosition(context.Self);
                foreach (var target in SelectCandidates(ability.Targeting.Relation, context))
                {
                    if (Vector3.Distance(selfPosition, positions.GetPosition(target)) > ability.Targeting.Range) continue;

                    var score = ScoreCandidate(behavior, context, target);
                    trace.Add(new UtilityDecisionTraceEntry(behavior.DisplayName, target, score));

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestBehaviorName = behavior.DisplayName;
                        best = new TurnDecision(ability, new[] { target }, score);
                    }
                }
            }

            combatLog?.Log(new UtilityDecisionLogEntry(
                context.Self,
                best?.Targets[0] ?? RuntimeEntityId.None,
                bestBehaviorName,
                best != null ? bestScore : 0f,
                trace));

            return best;
        }

        /// <summary>A Move behavior has no ability/CanUse/range check to gate on -- the only prerequisite is
        /// having any Movement resource left at all (checked once, not per-candidate, since it doesn't depend
        /// on the target). Scored with the exact same Considerations model as a UseAbility behavior, which is
        /// what lets e.g. a Distance input favor a *far* target here and a *close* one on an attack Behavior in
        /// the same BehaviorSetDefinition -- movement and attacking compete on one utility score, not two.</summary>
        private void ScoreMoveBehavior(
            BehaviorDefinition behavior, TurnContext context, ref TurnDecision best, ref float bestScore, ref string bestBehaviorName,
            List<UtilityDecisionTraceEntry> trace)
        {
            if (!characters.TryGetCharacter(context.Self, out var self)) return;
            if (!self.TryGetResource(behavior.MovementResourceId, out var movement) || movement.Current <= 0) return;

            foreach (var target in SelectCandidates(behavior.MoveTargetRelation, context))
            {
                var score = ScoreCandidate(behavior, context, target);
                trace.Add(new UtilityDecisionTraceEntry(behavior.DisplayName, target, score));

                if (score > bestScore)
                {
                    bestScore = score;
                    bestBehaviorName = behavior.DisplayName;
                    best = new TurnDecision(behavior.MovementResourceId, behavior.StopDistance, new[] { target }, score);
                }
            }
        }

        private float ScoreCandidate(BehaviorDefinition behavior, TurnContext context, RuntimeEntityId target)
        {
            var considerationContext = new ConsiderationContext(context.Self, target, characters, positions, randomSource, context.InfluenceMap);
            var scores = new List<float>(behavior.Considerations.Count);
            foreach (var consideration in behavior.Considerations)
                scores.Add(consideration.Evaluate(inputRegistry.GetInput(consideration.InputKey, considerationContext)));

            return UtilityMath.CombineScores(scores);
        }

        /// <summary>Neutral has no candidate roster in TurnContext (only enemies/allies) -- there's no third
        /// "neutral" list for a Brain to draw from yet, so it resolves to no candidates rather than guessing.</summary>
        private static IReadOnlyList<RuntimeEntityId> SelectCandidates(TargetRelation relation, TurnContext context) => relation switch
        {
            TargetRelation.Enemy => context.PotentialEnemies,
            TargetRelation.Ally => context.PotentialAllies,
            TargetRelation.Self => new[] { context.Self },
            TargetRelation.Any => Combine(context.PotentialEnemies, context.PotentialAllies, context.Self),
            _ => System.Array.Empty<RuntimeEntityId>(),
        };

        private static IReadOnlyList<RuntimeEntityId> Combine(IReadOnlyList<RuntimeEntityId> a, IReadOnlyList<RuntimeEntityId> b, RuntimeEntityId self)
        {
            var combined = new List<RuntimeEntityId>(a.Count + b.Count + 1) { self };
            combined.AddRange(a);
            combined.AddRange(b);
            return combined;
        }
    }
}

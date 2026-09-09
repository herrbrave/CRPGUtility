using System;
using UnityEngine;

namespace CRPG.AI
{
    /// <summary>
    /// One IAUS axis: a named Input, mapped through a response curve into [0,1], optionally softened by a
    /// Weight. The curve's X axis is calibrated to the input's own natural range (e.g. world-unit distances),
    /// not pre-normalized -- Evaluate clamps the curve's *output*, not the raw input. Nested authored data
    /// (like ChoiceRequirement/QuestObjectiveDefinition), never a standalone asset.
    /// </summary>
    [Serializable]
    public sealed class Consideration
    {
        [SerializeField] private string inputKey;
        [SerializeField] private AnimationCurve curve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        [SerializeField] private float weight = 1f;

        public string InputKey => inputKey;
        public AnimationCurve Curve => curve;
        public float Weight => weight;

        /// <summary>Weight of 1 = full effect; weight of 0 makes this Consideration score as if it were a
        /// perfect 1 regardless of the curve -- present but not punishing.</summary>
        public float Evaluate(float rawInput)
        {
            var score = Mathf.Clamp01(curve.Evaluate(rawInput));
            return 1f - weight * (1f - score);
        }
    }
}

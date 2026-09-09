using System.Linq;
using CRPG.Core;

namespace CRPG.Actors
{
    /// <summary>Applies SRD5's exhaustion table. Levels are tracked as a single ConditionInstance's StackCount
    /// (1-6), not six separate ConditionDefinitions -- DefaultMovementSpeedRule reads this same stack count
    /// directly to halve (level 2+) or zero (level 5+) speed, since exhaustion's effects are hardcoded by
    /// numeric level in the real rules rather than being a generic composable modifier. Level 6 is instant
    /// death, reported back to the caller rather than enforced here (this is a static helper, not a
    /// self-subscribing service like DeathSaveService -- nothing yet generates exhaustion automatically, e.g. a
    /// forced march or environmental hazard system, so whatever eventually calls IncreaseExhaustion is
    /// responsible for reacting to a true return the same way DeathSaveService reacts to its own death checks).
    /// <para>Deliberately NOT built: disadvantage on ability checks (level 1+) and on attacks/saves (level 3+)
    /// -- this engine has no generic "ability check" rule hook outside skills/dialogue yet -- and halved hit
    /// point maximum (level 4+), since safely mutating and later un-mutating a derived max HP needs more care
    /// than this pass budgeted for. Speed reduction and instant death are the two effects solid enough to fully
    /// automate tonight.</para></summary>
    public static class ExhaustionService
    {
        public static int GetLevel(CharacterState character, DefinitionId exhaustionConditionId)
        {
            var existing = character.Conditions.FirstOrDefault(c => c.ConditionId == exhaustionConditionId);
            return existing?.StackCount ?? 0;
        }

        /// <returns>True if this increase reached level 6 (instant death).</returns>
        public static bool IncreaseExhaustion(CharacterState character, DefinitionId exhaustionConditionId, int amount = 1)
        {
            var existing = character.Conditions.FirstOrDefault(c => c.ConditionId == exhaustionConditionId);
            if (existing == null)
            {
                existing = new ConditionInstance(exhaustionConditionId, RuntimeEntityId.None, character.Id, stackCount: System.Math.Min(6, amount));
                character.ApplyCondition(existing);
            }
            else
            {
                existing.StackCount = System.Math.Min(6, existing.StackCount + amount);
            }

            return existing.StackCount >= 6;
        }

        public static void DecreaseExhaustion(CharacterState character, DefinitionId exhaustionConditionId, int amount = 1)
        {
            var existing = character.Conditions.FirstOrDefault(c => c.ConditionId == exhaustionConditionId);
            if (existing == null) return;

            if (existing.StackCount <= amount) character.RemoveCondition(existing);
            else existing.StackCount -= amount;
        }
    }
}

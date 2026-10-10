using System.Collections.Generic;
using CRPG.Quests;
using UnityEditor;

namespace CRPG.Editor.Validation
{
    /// <summary>
    /// Author-time checks for a <see cref="QuestDefinition"/>: the mistakes that otherwise surface as a crash or a quest that can never finish. A reward with no item makes the quest's completion THROW
    /// (<c>QuestService.ClaimReward</c> dereferences it), an objective with no tracking key can never advance, and two objectives with one id share one progress record.
    /// </summary>
    public static class QuestValidator
    {
        public static List<ValidationResult> Validate(QuestDefinition quest)
        {
            var results = new List<ValidationResult>();
            var path = AssetDatabase.GetAssetPath(quest);

            if (quest.Objectives.Count == 0)
                results.Add(new ValidationResult(ValidationSeverity.Error, path, "Objectives", "The quest has no objectives, so it can never be completed.", "Add at least one objective."));

            var ids = new HashSet<string>();
            for (var i = 0; i < quest.Objectives.Count; i++)
            {
                var objective = quest.Objectives[i];
                if (string.IsNullOrEmpty(objective.Id)) results.Add(new ValidationResult(ValidationSeverity.Error, path, $"Objectives[{i}].Id", $"Objective {i} has no id.", "Give it a stable id."));
                else if (!ids.Add(objective.Id)) results.Add(new ValidationResult(ValidationSeverity.Error, path, $"Objectives[{i}].Id", $"Objective id '{objective.Id}' is used twice: they would share one progress record.", "Give each objective its own id."));

                if (string.IsNullOrEmpty(objective.TrackingKey))
                    results.Add(new ValidationResult(ValidationSeverity.Error, path, $"Objectives[{i}].TrackingKey", $"Objective '{objective.Id}' has no tracking key, so nothing can ever advance it.", "Set the Timeline key it watches."));
                if (objective.TargetCount < 1 && objective.ProgressMode != QuestObjectiveProgressMode.CurrentStatePredicate)
                    results.Add(new ValidationResult(ValidationSeverity.Warning, path, $"Objectives[{i}].TargetCount", $"Objective '{objective.Id}' needs {objective.TargetCount} occurrences, so it is complete at once.", "Set a target of 1 or more."));
                if (objective.ProgressMode == QuestObjectiveProgressMode.HistoricalCount)
                    results.Add(new ValidationResult(ValidationSeverity.Warning, path, $"Objectives[{i}].ProgressMode", $"Objective '{objective.Id}' counts HISTORICAL events (including ones from before the quest started), so it may be complete the moment the quest begins.", "Use CountSinceActivation unless that is intended."));
            }

            for (var i = 0; i < quest.Rewards.Count; i++)
            {
                var reward = quest.Rewards[i];
                if (reward == null || reward.Item == null)
                    results.Add(new ValidationResult(ValidationSeverity.Error, path, $"Rewards[{i}].Item", $"Reward {i} has no item: completing the quest would throw.", "Assign the item or remove the reward."));
                else if (reward.Quantity < 1)
                    results.Add(new ValidationResult(ValidationSeverity.Warning, path, $"Rewards[{i}].Quantity", $"Reward {i} grants nothing (quantity {reward.Quantity}).", "Set a quantity of 1 or more."));
            }

            return results;
        }
    }
}

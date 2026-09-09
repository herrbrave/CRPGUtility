using System.Collections.Generic;
using CRPG.Core;
using CRPG.Quests;

namespace CRPG.UI
{
    /// <summary>
    /// Pure data layer for a quest journal screen -- no UI Toolkit dependency, mirroring CampPresenter/
    /// StorePresenter's split. No dedicated QuestLogView exists yet (unlike Camp/Store); this is authored ahead
    /// of that consumer the same way Champion's Improved Critical was authored ahead of an attack-resolution
    /// system, so a future journal UI has a ready-made data layer to build on.
    /// </summary>
    public sealed class QuestLogPresenter
    {
        private readonly QuestLog log;
        private readonly IRulesDatabase rulesDatabase;

        public QuestLogPresenter(QuestLog log, IRulesDatabase rulesDatabase)
        {
            this.log = log;
            this.rulesDatabase = rulesDatabase;
        }

        public IReadOnlyList<QuestSummary> GetQuests()
        {
            var summaries = new List<QuestSummary>();

            foreach (var quest in log.Quests)
            {
                if (!rulesDatabase.TryResolve<QuestDefinition>(quest.QuestId, out var definition)) continue;

                var objectives = new List<QuestObjectiveSummary>();
                foreach (var objectiveDefinition in definition.Objectives)
                {
                    if (!quest.TryGetObjective(objectiveDefinition.Id, out var objectiveState)) continue;
                    objectives.Add(new QuestObjectiveSummary(
                        objectiveDefinition.DisplayName, objectiveState.CurrentCount, objectiveDefinition.TargetCount, objectiveState.IsComplete));
                }

                summaries.Add(new QuestSummary(definition.DisplayName, definition.Description, quest.Status, objectives));
            }

            return summaries;
        }
    }
}

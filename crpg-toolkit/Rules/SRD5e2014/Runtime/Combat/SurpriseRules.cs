using System;
using System.Collections.Generic;
using CRPG.Actors;
using CRPG.Core;

namespace CRPG.Rules.SRD5
{
    /// <summary>
    /// Surprise under the SRD: when a fight starts, a creature that has not noticed any of the other side is surprised. It cannot move or act on its first turn and cannot react until that turn ends, which is the surprised condition (it prevents actions) ending at the end of the creature's own first turn.
    /// Who noticed whom is the game's to say (a sneaking party against passive Perception, light and cones); this class only turns that answer into conditions. It ignores anyone already out of the fight.
    /// </summary>
    public static class SurpriseRules
    {
        public static readonly DefinitionId SurprisedId = new("condition-surprised");

        /// <summary>The observers none of the threats were noticed by. <paramref name="noticed"/> answers "did this observer notice that threat".</summary>
        public static IReadOnlyList<RuntimeEntityId> Surprised(IReadOnlyList<RuntimeEntityId> observers, IReadOnlyList<RuntimeEntityId> threats, Func<RuntimeEntityId, RuntimeEntityId, bool> noticed)
        {
            var result = new List<RuntimeEntityId>();
            foreach (var observer in observers)
            {
                var any = false;
                foreach (var threat in threats)
                {
                    if (noticed(observer, threat)) { any = true; break; }
                }

                if (!any && threats.Count > 0) result.Add(observer);
            }

            return result;
        }

        /// <summary>Applies the surprised condition to each, to end when that creature's first turn ends. Publishes the usual condition events so the log and the UI see it.</summary>
        public static void Apply(ICharacterRegistry characters, IGameEventBus events, ICombatLog log, IEnumerable<RuntimeEntityId> surprised)
        {
            foreach (var id in surprised)
            {
                if (!characters.TryGetCharacter(id, out var character) || character.HasCondition(SurprisedId)) continue;
                character.ApplyCondition(new ConditionInstance(SurprisedId, id, id, 1, boundary: DurationBoundary.EndOfTargetTurn));
                log?.Log(new ConditionLogEntry(id, id, SurprisedId, applied: true));
                events?.Publish(new GameEventContext(GameEvent.ConditionApplied, id, id));
            }
        }
    }
}

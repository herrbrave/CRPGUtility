using System;
using CRPG.Core;
using UnityEngine;

namespace CRPG.Dialogue
{
    /// <summary>The "Set Flag" node from spec, generalized to Timeline's richer value model. NpcId is recorded
    /// as the event's Source for EntityRef-kind values and for traceability, not because the NPC "caused" it.</summary>
    [Serializable]
    public sealed class RecordEventAction : DialogueAction
    {
        [SerializeField] private string key;
        [SerializeField] private TimelineValueKind valueKind = TimelineValueKind.Flag;
        [SerializeField] private double numberValue;
        [SerializeField] private string textValue;
        [SerializeField] private double amount = 1;

        public override void Execute(DialogueContext context)
        {
            var value = valueKind switch
            {
                TimelineValueKind.Number => TimelineValue.OfNumber(numberValue),
                TimelineValueKind.Text => TimelineValue.OfText(textValue),
                TimelineValueKind.EntityRef => TimelineValue.OfEntity(context.NpcId),
                _ => TimelineValue.Flag,
            };

            context.Timeline.Record(key, value, amount, context.NpcId);
        }
    }
}

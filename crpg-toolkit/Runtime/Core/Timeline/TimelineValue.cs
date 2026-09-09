using System;

namespace CRPG.Core
{
    /// <summary>The payload of one TimelineEvent. Mirrors the typed-key idea spec's WorldState sketched
    /// (Bool/Int/Float/String/Entity), but as a value carried by an event rather than a mutable dictionary
    /// entry -- Timeline is the single source of truth for "current state," derived by querying the log.</summary>
    public readonly struct TimelineValue : IEquatable<TimelineValue>
    {
        private TimelineValue(TimelineValueKind kind, double number, string text, RuntimeEntityId entity)
        {
            Kind = kind;
            Number = number;
            Text = text;
            Entity = entity;
        }

        public TimelineValueKind Kind { get; }
        public double Number { get; }
        public string Text { get; }
        public RuntimeEntityId Entity { get; }

        public static readonly TimelineValue Flag = new(TimelineValueKind.Flag, 0, null, default);
        public static TimelineValue OfNumber(double value) => new(TimelineValueKind.Number, value, null, default);
        public static TimelineValue OfText(string value) => new(TimelineValueKind.Text, 0, value, default);
        public static TimelineValue OfEntity(RuntimeEntityId value) => new(TimelineValueKind.EntityRef, 0, null, value);

        public bool Equals(TimelineValue other) =>
            Kind == other.Kind && Number.Equals(other.Number) && Text == other.Text && Entity == other.Entity;

        public override bool Equals(object obj) => obj is TimelineValue other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Kind, Number, Text, Entity);
    }
}

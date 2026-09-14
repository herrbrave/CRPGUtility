using System;
using CRPG.Core;

namespace CRPG.Actors
{
    /// <summary>Identifies which specific ability cast (or other effect source) owns a <see cref="ConditionInstance"/>,
    /// so cleanup can remove exactly one cast's conditions without touching an unrelated source's instance of the
    /// same condition type on the same target -- see <see cref="CharacterState.RemoveConditionsWithOwner"/>,
    /// <see cref="ConcentrationEffectCleanup"/>, and CRPG.Abilities.DispelEffect.
    /// GUID-backed and persisted (CRPG.Saving.ConditionSaveEntry.OwnerId /
    /// CRPG.Saving.CharacterSaveData.ConcentrationEffectOwnerId) -- deliberately NOT a
    /// process-lifetime counter like <c>ActionToken</c>/<c>MoveRequestId</c>, which only ever answer "is this
    /// specific in-flight thing still current" and stop mattering the instant a save happens. Ownership names a
    /// relationship (this condition belongs to that cast) that must still be true after a reload, exactly like
    /// <see cref="RuntimeEntityId"/> already must be -- same shape, same reasoning.</summary>
    public readonly struct EffectOwnerId : IEquatable<EffectOwnerId>
    {
        private readonly string value;

        private EffectOwnerId(string value) => this.value = value;

        public static readonly EffectOwnerId None = new(null);

        public static EffectOwnerId NewOwner() => new(Guid.NewGuid().ToString("N"));

        /// <summary>Reconstructs an id previously produced by <see cref="ToString"/> -- for save/load, where an
        /// ownership relationship must survive the round trip rather than being freshly generated.</summary>
        public static EffectOwnerId FromString(string value) => new(string.IsNullOrEmpty(value) ? null : value);

        public bool IsValid => !string.IsNullOrEmpty(value);

        public bool Equals(EffectOwnerId other) => string.Equals(value, other.value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is EffectOwnerId other && Equals(other);
        public override int GetHashCode() => value?.GetHashCode() ?? 0;
        public override string ToString() => value ?? string.Empty;

        public static bool operator ==(EffectOwnerId a, EffectOwnerId b) => a.Equals(b);
        public static bool operator !=(EffectOwnerId a, EffectOwnerId b) => !a.Equals(b);
    }

    /// <summary>When a <see cref="ConditionInstance.RemainingDuration"/> decrements -- authored per-application on
    /// CRPG.Abilities.ApplyConditionEffect, not per <see cref="ConditionDefinition"/>, since
    /// different abilities can apply the same condition type with different durations/boundaries.
    /// <see cref="None"/> (the default) means it never ticks -- preserves today's behavior for any content that
    /// doesn't explicitly opt in. See CRPG.Combat.ConditionDurationService for how these fire.</summary>
    public enum DurationBoundary
    {
        None,
        StartOfTargetTurn,
        EndOfTargetTurn,
        StartOfSourceTurn,
        EndOfSourceTurn,
    }

    /// <summary>A condition currently affecting a character. <see cref="RemainingDuration"/> of null means
    /// indefinite -- it lasts until something explicitly removes it (a saving throw, an end-of-combat cleanup, ...).</summary>
    public sealed class ConditionInstance
    {
        public ConditionInstance(
            DefinitionId conditionId, RuntimeEntityId source, RuntimeEntityId target, int? remainingDuration = null,
            int stackCount = 1, EffectOwnerId ownerId = default, DurationBoundary boundary = DurationBoundary.None)
        {
            ConditionId = conditionId;
            Source = source;
            Target = target;
            RemainingDuration = remainingDuration;
            StackCount = stackCount;
            OwnerId = ownerId;
            Boundary = boundary;
        }

        public DefinitionId ConditionId { get; }
        public RuntimeEntityId Source { get; }
        public RuntimeEntityId Target { get; }
        public int? RemainingDuration { get; set; }
        public int StackCount { get; set; }

        /// <summary>Which specific cast/effect application created this instance. <see cref="EffectOwnerId.None"/>
        /// for anything applied outside CRPG.Abilities.ApplyConditionEffect (there is no such
        /// production path today, but save-restored data from before this field existed would also come back this
        /// way) -- ownership-scoped removal (<see cref="CharacterState.RemoveConditionsWithOwner"/>) never matches
        /// an invalid owner, so these are treated as ordinary, individually-cure-only conditions.</summary>
        public EffectOwnerId OwnerId { get; }
        public DurationBoundary Boundary { get; }
    }
}

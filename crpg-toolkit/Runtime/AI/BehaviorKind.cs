namespace CRPG.AI
{
    /// <summary>What kind of action a Behavior scores/performs. Kept as a discriminator on BehaviorDefinition
    /// itself (rather than two separate definition types) so a BehaviorSetDefinition can freely mix "use this
    /// ability" and "close the distance" options that all compete on the exact same utility score -- that's
    /// the whole point of adding movement as a Behavior instead of a special-cased pre-step.</summary>
    public enum BehaviorKind
    {
        /// <summary>Score candidate targets for an AbilityDefinition and use it on the winner. The original
        /// (and default, for backward compatibility with already-authored content) behavior kind.</summary>
        UseAbility = 0,

        /// <summary>Score candidate targets to move toward, then close distance on the winner via
        /// MovementResolutionService. Has no AbilityId/Effects -- see MovementResourceId/MoveTargetRelation/
        /// StopDistance on BehaviorDefinition for what it needs instead.</summary>
        Move = 1,
    }
}

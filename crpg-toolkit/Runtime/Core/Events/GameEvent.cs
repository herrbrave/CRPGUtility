namespace CRPG.Core
{
    /// <summary>Generic lifecycle events features/abilities can subscribe to (spec "Trigger framework"), so
    /// "when X happens, do Y" is data a FeatureDefinition declares rather than a bespoke script per feature.</summary>
    public enum GameEvent
    {
        CombatStarted,
        CombatEnded,

        TurnStarted,
        TurnEnded,

        AbilityStarted,
        AbilityResolved,

        /// <summary>Task 4.2: an ability's resources were already spent and AbilityStarted already published
        /// when one of its effects threw an unexpected exception mid-Apply -- deliberately distinct from an
        /// ordinary rejection (which never gets this far and never publishes anything). No rollback: matches a
        /// real tabletop cast not un-happening because of a later mishap.</summary>
        AbilityFaulted,

        BeforeAttackRoll,
        AfterAttackRoll,

        OnHit,
        OnMiss,
        OnCriticalHit,

        BeforeDamage,
        AfterDamage,

        BeforeSavingThrow,
        AfterSavingThrow,

        BeforeAbilityCheck,
        AfterAbilityCheck,

        ConditionApplied,
        ConditionRemoved,

        CreatureDroppedToZeroHP,
        CreatureStabilized,
        CreatureDied,

        LeftThreatenedArea,

        ConcentrationStarted,
        ConcentrationBroken,

        ShortRest,
        LongRest,

        QuestStarted,
        QuestObjectiveCompleted,
        QuestCompleted,

        ItemEquipped,
        ItemUnequipped,

        AnimationCueChanged
    }
}

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

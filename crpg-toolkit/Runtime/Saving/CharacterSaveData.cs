using System;
using System.Collections.Generic;

namespace CRPG.Saving
{
    [Serializable]
    public sealed class StatScoreSaveEntry
    {
        public string StatId;
        public int Score;
    }

    [Serializable]
    public sealed class ResourceSaveEntry
    {
        public string ResourceId;
        public int Max;
        public int Current;
    }

    [Serializable]
    public sealed class ConditionSaveEntry
    {
        public string ConditionId;
        public string Source;
        public string Target;
        public bool HasRemainingDuration;
        public int RemainingDuration;
        public int StackCount;

        // Task 4.4: OwnerId is GUID-backed (CRPG.Actors.EffectOwnerId), exactly like Source/Target's
        // RuntimeEntityId already is -- round-trips safely, unlike a process-lifetime counter would. Empty
        // string (EffectOwnerId.None) for a save written before this field existed.
        public string OwnerId;
        public string Boundary;
    }

    /// <summary>Plain, JsonUtility-friendly mirror of one CharacterState. Dictionaries/HashSets are flattened
    /// to lists -- JsonUtility can't serialize either directly. Public fields throughout (no property
    /// indirection): these DTOs are never hand-edited in an Inspector, only produced/consumed by SaveService,
    /// so the [SerializeField]-plus-property convention the rest of the package uses for authored data would
    /// just be boilerplate here.</summary>
    [Serializable]
    public sealed class CharacterSaveData
    {
        public string Id;
        public List<StatScoreSaveEntry> Stats = new();
        public List<ResourceSaveEntry> Resources = new();
        public List<ConditionSaveEntry> Conditions = new();
        public List<string> Proficiencies = new();
        public List<string> Features = new();

        // Task 3.5: closes the gap Docs/Design/State-Persistence-Matrix.md named and Task 0.2's
        // intentional-red SaveServiceTests cases document -- CurrentAnimationCueId stays deliberately absent
        // (transient presentation state, not gameplay-authoritative).
        public int Level;
        public bool HasDeathSaves;
        public int DeathSaveSuccesses;
        public int DeathSaveFailures;
        public string SpellcastingStatId;
        public string ConcentratingOnAbilityId;

        // Task 4.4: round-tripped alongside ConcentratingOnAbilityId so a later ConcentrationEffectCleanup.Break
        // can still sweep the right targets after a reload -- see EffectOwnerId's own remarks on why this is
        // safe to persist (GUID-backed, not a process-lifetime counter).
        public string ConcentrationEffectOwnerId;
        public List<string> ConcentrationAffectedTargets = new();

        public List<string> KnownAbilities = new();
        public List<string> PreparedAbilities = new();

        // 2D campaign Task 2.4 (format v3): layer slot -> option id. Absent from v2 saves, where the initializer
        // correctly leaves it empty.
        public List<AppearanceSaveEntry> Appearance = new();
    }

    [Serializable]
    public sealed class AppearanceSaveEntry
    {
        public string Slot;
        public string Option;
    }
}

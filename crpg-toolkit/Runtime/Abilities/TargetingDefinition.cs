using System;
using UnityEngine;

namespace CRPG.Abilities
{
    /// <summary>
    /// What an ability can be aimed at. An ability can't execute until the chosen target count is within
    /// [MinTargets, MaxTargets] (spec "Forced target selection"); a UI layer enforces this incrementally while
    /// the player is picking, and <see cref="IAbilityResolutionService.IsValidTargetSelection"/> is the same
    /// check the engine uses right before executing.
    /// </summary>
    /// <remarks>A tag-based TargetFilter ("only constructs", "only creatures below half HP") isn't modeled
    /// yet -- it needs a creature-tagging system that doesn't exist until SRD content (Phase 7) is authored.</remarks>
    [Serializable]
    public sealed class TargetingDefinition
    {
        [SerializeField] private TargetMode mode = TargetMode.SingleUnit;
        [SerializeField] private TargetRelation relation = TargetRelation.Enemy;
        [SerializeField] private int minTargets = 1;
        [SerializeField] private int maxTargets = 1;
        [SerializeField] private float range = 5f;
        [SerializeField] private float areaSize;
        [SerializeField] private bool requireLineOfSight = true;
        [SerializeField] private bool requireLineOfEffect = true;
        [SerializeField] private bool requireVisibleTarget = true;

        public TargetMode Mode => mode;
        public TargetRelation Relation => relation;
        public int MinTargets => minTargets;
        public int MaxTargets => maxTargets;
        public float Range => range;
        public float AreaSize => areaSize;
        public bool RequireLineOfSight => requireLineOfSight;
        public bool RequireLineOfEffect => requireLineOfEffect;
        public bool RequireVisibleTarget => requireVisibleTarget;
    }
}

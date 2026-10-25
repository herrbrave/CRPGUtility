using System;
using UnityEngine;

namespace CRPG.Abilities
{
    /// <summary>Where a shaped area is anchored: at the point the player aimed at (Shatter, Web, Fog Cloud) or on the caster (an aura; a cone, a line or a cube that starts at the caster and points where he aimed: Burning Hands, Thunderwave).</summary>
    public enum AreaAnchor { AtAim, AtCaster }

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
        [SerializeField] private AreaAnchor areaAnchor = AreaAnchor.AtAim;
        [SerializeField] private float areaHeight = 6f;
        [SerializeField] private int areaTargetCap;
        [SerializeField] private bool spreadProjectiles;

        public TargetMode Mode => mode;
        public TargetRelation Relation => relation;
        public int MinTargets => minTargets;
        public int MaxTargets => maxTargets;
        public float Range => range;
        public float AreaSize => areaSize;
        public bool RequireLineOfSight => requireLineOfSight;
        public bool RequireLineOfEffect => requireLineOfEffect;
        public bool RequireVisibleTarget => requireVisibleTarget;

        /// <summary>Where a shaped area is anchored; AtAim by default. Cone and Line always start at the caster.</summary>
        public AreaAnchor Anchor => areaAnchor;
        /// <summary>Cylinders: how tall, in metres, above the aim point.</summary>
        public float AreaHeight => areaHeight;
        /// <summary>The only cap on how many creatures an area can hit (nearest to its origin first); 0 = no cap. MinTargets and MaxTargets do NOT apply to an aimed ability: the shape decides who is hit.</summary>
        public int AreaTargetCap => areaTargetCap;

        /// <summary>The ability fires <see cref="MaxTargets"/> projectiles (Magic Missile's three darts) and the caster may put them on fewer creatures: they are dealt out to the chosen creatures in turn, so one target takes all of them, two take two and one, and each dart is its own roll.
        /// Off: one strike per chosen creature. The list the caller passes stays free of duplicates either way.</summary>
        public bool SpreadProjectiles => spreadProjectiles;

        /// <summary>A shaped area: the creatures inside decide the targets.</summary>
        public bool IsShaped => mode == TargetMode.Area || mode == TargetMode.Line || mode == TargetMode.Cone || mode == TargetMode.Sphere || mode == TargetMode.Cylinder;

        /// <summary>An aura: a sphere or cylinder on the caster. Needs no aim and is cast at once, like Self.</summary>
        public bool CasterCentred => (mode == TargetMode.Sphere || mode == TargetMode.Cylinder) && areaAnchor == AreaAnchor.AtCaster;

        /// <summary>The ability is pointed at the ground (a point or a direction) instead of at creatures.</summary>
        public bool RequiresAim => mode == TargetMode.Point || mode == TargetMode.Direction || (IsShaped && !CasterCentred);

        /// <summary>Range limits where the aim point may be: a Point, or an area anchored at the aim. A cone, a line or a cube on the caster only takes a direction.</summary>
        public bool UsesRange => mode == TargetMode.Point || (IsShaped && !CasterCentred && areaAnchor == AreaAnchor.AtAim && mode != TargetMode.Cone && mode != TargetMode.Line);
    }
}

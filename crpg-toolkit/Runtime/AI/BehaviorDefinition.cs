using System.Collections.Generic;
using CRPG.Abilities;
using CRPG.Core;
using UnityEngine;

namespace CRPG.AI
{
    /// <summary>
    /// An IAUS Behavior, scored against a candidate target by its Considerations. Two kinds share this one
    /// asset type (rather than a type per kind) specifically so a BehaviorSetDefinition can mix them and let
    /// them compete on the same utility score:
    /// <list type="bullet">
    /// <item><see cref="BehaviorKind.UseAbility"/> (the default/original kind): "use this ability." References
    /// the ability by DefinitionId, not a direct object reference -- same convention as every other
    /// cross-definition reference in the toolkit (e.g. FeatureDefinition.GrantedAbilityIds), so content can be
    /// renamed/reorganized/modded without breaking a Behavior. Reuses the resolved AbilityDefinition's own
    /// Targeting to enumerate valid targets (relation/range) rather than a second targeting concept -- an
    /// ability already fully describes what it can hit. Scoped to single-target abilities for now
    /// (Targeting.MaxTargets == 1); true multi-target/AoE scoring needs cluster-placement heuristics not built
    /// here.</item>
    /// <item><see cref="BehaviorKind.Move"/>: "close the distance." Has no ability to reuse Targeting from, so
    /// it carries its own MovementResourceId/MoveTargetRelation/StopDistance instead.</item>
    /// </list>
    /// </summary>
    [CreateAssetMenu(menuName = "CRPG Toolkit/AI/Behavior Definition")]
    public sealed class BehaviorDefinition : DefinitionAsset
    {
        [SerializeField] private string displayName;
        [SerializeField] private BehaviorKind kind = BehaviorKind.UseAbility;
        [SerializeField] private DefinitionId abilityId;
        [SerializeField] private List<Consideration> considerations = new();

        [SerializeField] private DefinitionId movementResourceId;
        [SerializeField] private TargetRelation moveTargetRelation = TargetRelation.Enemy;
        [SerializeField] private float stopDistance = 1f;

        public string DisplayName => displayName;
        public BehaviorKind Kind => kind;
        public DefinitionId AbilityId => abilityId;
        public IReadOnlyList<Consideration> Considerations => considerations;

        /// <summary>Only meaningful when <see cref="Kind"/> is <see cref="BehaviorKind.Move"/>.</summary>
        public DefinitionId MovementResourceId => movementResourceId;
        /// <summary>Only meaningful when <see cref="Kind"/> is <see cref="BehaviorKind.Move"/>.</summary>
        public TargetRelation MoveTargetRelation => moveTargetRelation;
        /// <summary>Only meaningful when <see cref="Kind"/> is <see cref="BehaviorKind.Move"/>: how close to get
        /// to the winning target, e.g. a melee engagement range so the next turn's attack is in range.</summary>
        public float StopDistance => stopDistance;
    }
}

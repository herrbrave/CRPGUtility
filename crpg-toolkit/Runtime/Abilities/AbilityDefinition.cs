using System.Collections.Generic;
using CRPG.Core;
using UnityEngine;

namespace CRPG.Abilities
{
    /// <summary>
    /// Spells and mundane actions use the same ability system (spec "Ability framework") -- a Fire Bolt and a
    /// Second Wind are both just an AbilityDefinition with different costs/targeting/effects, not two
    /// different systems.
    /// </summary>
    [CreateAssetMenu(menuName = "CRPG Toolkit/Abilities/Ability Definition")]
    public sealed class AbilityDefinition : DefinitionAsset
    {
        [SerializeField] private string displayName;
        [SerializeField, TextArea] private string description;
        [SerializeField] private AbilityTag tags;
        [SerializeField] private List<AbilityCost> costs = new();
        [SerializeField] private TargetingDefinition targeting = new();
        [SerializeReference] private List<AbilityEffect> effects = new();

        /// <summary>0 for a non-spell ability or a cantrip; a spell's real SRD level (1-9) otherwise. Only
        /// meaningful for upcasting -- see AbilityResolutionService.Execute's upcastWithSlot parameter and
        /// ResourceDefinition.SpellSlotLevel.</summary>
        [SerializeField] private int spellLevel;

        /// <summary>Task 4.3: real gameplay-timed action phases (Docs/Design/Production-Foundation.md sec.4.4,
        /// "Prepare -> Commit -> Recover -> Complete"). Both default to 0 -- purely additive, every existing
        /// asset deserializes as instant/unchanged (0-duration phases collapse to effectively-instant, matching
        /// today's AbilityResolutionService.Execute()'s own single-call behavior exactly). See
        /// CRPG.Gameplay.Actions.AbilityActionExecution, the only consumer.</summary>
        [SerializeField] private float prepareSeconds;
        [SerializeField] private float recoverSeconds;

        /// <summary>Task 4.5: a bare presentation tag (not resolved through <see cref="IRulesDatabase"/>), same
        /// treatment as <see cref="CRPG.Actors.CharacterAnimationProfile"/>'s own cue ids -- what
        /// <see cref="CRPG.Gameplay.Actions.IActionPresentationHook.RequestActionCue"/> should ask the
        /// character's animator to play once this ability commits. Purely additive, defaults to
        /// <see cref="DefinitionId.None"/> so every existing asset deserializes unchanged; a real hook
        /// implementation must no-op on None rather than guess a cue from tags.</summary>
        [SerializeField] private DefinitionId animationCueId;

        public string DisplayName => displayName;
        public string Description => description;
        public AbilityTag Tags => tags;
        public IReadOnlyList<AbilityCost> Costs => costs;
        public TargetingDefinition Targeting => targeting;
        public IReadOnlyList<AbilityEffect> Effects => effects;
        public int SpellLevel => spellLevel;
        public float PrepareSeconds => prepareSeconds;
        public float RecoverSeconds => recoverSeconds;
        public DefinitionId AnimationCueId => animationCueId;
    }
}

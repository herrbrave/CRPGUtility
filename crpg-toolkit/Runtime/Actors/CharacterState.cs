using System.Collections.Generic;
using CRPG.Core;

namespace CRPG.Actors
{
    /// <summary>
    /// Everything mutable about one character at runtime. Never references a ScriptableObject definition
    /// directly -- only <see cref="DefinitionId"/>s, resolved through an <see cref="IRulesDatabase"/> when
    /// something actually needs the authored data. This is the "state" half of the definitions/state split.
    /// </summary>
    /// <remarks>
    /// Equipment intentionally isn't modeled here -- it belongs to the Inventory assembly (item instances,
    /// slots, the shared party inventory) and will reference a CharacterState by <see cref="Id"/> rather than
    /// the other way around.
    /// </remarks>
    public sealed class CharacterState
    {
        private readonly Dictionary<DefinitionId, ResourceState> resources = new();
        private readonly List<ConditionInstance> conditions = new();
        private readonly HashSet<DefinitionId> proficiencies = new();
        private readonly List<DefinitionId> features = new();
        private readonly HashSet<DefinitionId> knownAbilities = new();
        private readonly HashSet<DefinitionId> preparedAbilities = new();

        public CharacterState(RuntimeEntityId id)
        {
            Id = id;
        }

        public RuntimeEntityId Id { get; }
        public int Level { get; set; } = 1;
        public StatBlock Stats { get; } = new();

        /// <summary>Null unless this character is currently unconscious and dying. See DeathSaveService.</summary>
        public DeathSaveState DeathSaves { get; set; }

        /// <summary>Which ability this character casts spells with (set from ClassDefinition.SpellcastingStatId
        /// at build time). DefinitionId.None for non-casters. Copied onto the character rather than kept as a
        /// live ClassDefinition reference so a spell ability shared across classes (e.g. a Cleric and a Paladin
        /// both able to cast the same spell) resolves the *caster's* governing stat, not one baked into the
        /// spell asset -- see AttackRollEffect/SavingThrowEffect.</summary>
        public DefinitionId SpellcastingStatId { get; set; }

        /// <summary>DefinitionId.None means not currently concentrating. Set by AbilityResolutionService when
        /// an AbilityTag.Concentration ability resolves (overwriting any previous value -- casting a new
        /// concentration spell always ends the last one); cleared by ConcentrationService on a failed
        /// Constitution save after taking damage. Note: ending concentration this way does not retroactively
        /// remove whatever the broken spell's effects already did (e.g. a condition it applied stays applied) --
        /// only new content built around this flag can decide to clean itself up when it sees concentration end.</summary>
        public DefinitionId ConcentratingOnAbilityId { get; set; }

        /// <summary>The last animation cue requested for this character via AnimationOrderService -- e.g. an
        /// attack swing or a combat-ready stance. DefinitionId.None means nothing's been explicitly requested.
        /// Deliberately does NOT cover continuous locomotion (idle/walk blending) -- that's a purely visual
        /// concern driven by movement speed, never routed through CharacterState. Purely a data record of the
        /// last request; whatever visual layer is watching AnimationCueChanged decides what to actually play.</summary>
        public DefinitionId CurrentAnimationCueId { get; set; }

        public IReadOnlyDictionary<DefinitionId, ResourceState> Resources => resources;
        public IReadOnlyList<ConditionInstance> Conditions => conditions;
        public IReadOnlyCollection<DefinitionId> Proficiencies => proficiencies;

        public void AddResource(ResourceState resource) => resources[resource.ResourceId] = resource;

        public bool TryGetResource(DefinitionId resourceId, out ResourceState resource) => resources.TryGetValue(resourceId, out resource);

        public void ApplyCondition(ConditionInstance condition) => conditions.Add(condition);

        public void RemoveCondition(ConditionInstance condition) => conditions.Remove(condition);

        public bool HasCondition(DefinitionId conditionId)
        {
            foreach (var condition in conditions)
            {
                if (condition.ConditionId == conditionId) return true;
            }

            return false;
        }

        public void GrantProficiency(DefinitionId proficiencyId) => proficiencies.Add(proficiencyId);

        public bool HasProficiency(DefinitionId proficiencyId) => proficiencies.Contains(proficiencyId);

        /// <summary>Currently active FeatureDefinition ids (from race/class/feats/items/conditions/abilities).
        /// See <see cref="FeatureModifierCollector"/> for turning these into rule-query modifiers.</summary>
        public IReadOnlyList<DefinitionId> Features => features;

        public void GrantFeature(DefinitionId featureId) => features.Add(featureId);

        public void RemoveFeature(DefinitionId featureId) => features.Remove(featureId);

        public bool HasFeature(DefinitionId featureId) => features.Contains(featureId);

        /// <summary>Spells (and cantrips) this character has permanently learned -- via SelectSpell choices at
        /// build/level-up, for both Known-casters (their whole spell list works this way) and Prepared-casters
        /// (just their cantrips; leveled spells go through PreparedAbilities instead).</summary>
        public IReadOnlyCollection<DefinitionId> KnownAbilities => knownAbilities;

        public void LearnAbility(DefinitionId abilityId) => knownAbilities.Add(abilityId);

        public bool KnowsAbility(DefinitionId abilityId) => knownAbilities.Contains(abilityId);

        /// <summary>A Prepared-caster's current day's selection from their class's full spell list. Replaced
        /// wholesale by PrepareSpells (see SpellPreparationService), not added to incrementally -- preparing
        /// spells is a single daily choice, not a permanent unlock.</summary>
        public IReadOnlyCollection<DefinitionId> PreparedAbilities => preparedAbilities;

        public void PrepareSpells(IEnumerable<DefinitionId> abilityIds)
        {
            preparedAbilities.Clear();
            foreach (var abilityId in abilityIds) preparedAbilities.Add(abilityId);
        }

        public bool HasPrepared(DefinitionId abilityId) => preparedAbilities.Contains(abilityId);
    }
}

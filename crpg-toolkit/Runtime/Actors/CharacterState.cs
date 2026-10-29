using System;
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
        private readonly Dictionary<DefinitionId, DefinitionId> appearance = new();
        private readonly List<ConditionInstance> conditions = new();
        private readonly HashSet<DefinitionId> proficiencies = new();
        private readonly List<DefinitionId> features = new();
        private readonly HashSet<DefinitionId> knownAbilities = new();
        private readonly HashSet<DefinitionId> preparedAbilities = new();
        private readonly List<RuntimeEntityId> concentrationAffectedTargets = new();

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

        /// <summary>DefinitionId.None means not currently concentrating. Set via <see cref="SetConcentrationEffect"/>
        /// when an AbilityTag.Concentration ability resolves (or faults after partially applying effects --
        /// see AbilityResolutionService.Execute); cleared via <see cref="ClearConcentration"/>, called by
        /// <see cref="ConcentrationEffectCleanup.Break"/> from both AbilityResolutionService's replacement path
        /// and ConcentrationService's failed-save/CreatureDied paths. Unlike the pre-Task-4.4 version of this
        /// field, ending concentration now DOES retroactively remove whatever the broken spell's effects already
        /// did -- see <see cref="ConcentrationEffectOwnerId"/>/<see cref="ConcentrationAffectedTargets"/> and
        /// ConcentrationEffectCleanup.Break -- as long as the character was concentrating with a valid owner
        /// (always true for anything set this session; see that field's own remarks for the one exception).</summary>
        public DefinitionId ConcentratingOnAbilityId { get; private set; }

        /// <summary>Which specific cast <see cref="ConcentratingOnAbilityId"/> refers to -- lets
        /// ConcentrationEffectCleanup.Break remove exactly that cast's conditions from
        /// <see cref="ConcentrationAffectedTargets"/> without touching an unrelated source's condition on the
        /// same target. <see cref="EffectOwnerId.None"/> only for a character restored from a save written before
        /// this field existed (the flag round-trips, the owner/targets do not for that one legacy case) -- Break
        /// still clears the flag correctly then, it just has nothing to sweep.</summary>
        public EffectOwnerId ConcentrationEffectOwnerId { get; private set; }

        /// <summary>The *actual* recipients of the current concentration cast's owned conditions (from
        /// AbilityExecutionContext.OwnedTargets, not the ability's originally-requested target list -- a
        /// self-effect, an expanded-area target, or a redirected target must still be swept correctly).</summary>
        public IReadOnlyList<RuntimeEntityId> ConcentrationAffectedTargets => concentrationAffectedTargets;

        /// <summary>Starts (or, via the caller's own prior ConcentrationEffectCleanup.Break call, replaces)
        /// concentration atomically -- the ability id, owner id, and affected-targets snapshot always change
        /// together, so they can never drift out of sync the way three independently-settable properties could.</summary>
        public void SetConcentrationEffect(DefinitionId abilityId, EffectOwnerId ownerId, IEnumerable<RuntimeEntityId> affectedTargets)
        {
            ConcentratingOnAbilityId = abilityId;
            ConcentrationEffectOwnerId = ownerId;
            concentrationAffectedTargets.Clear();
            concentrationAffectedTargets.AddRange(affectedTargets);
        }

        /// <summary>Clears the concentration flag/owner/targets together. Does NOT remove any conditions itself --
        /// that's ConcentrationEffectCleanup.Break's job (it calls this as one step of its own sweep); calling
        /// this directly is for restore/reset paths that don't want a sweep (e.g. SaveService.RestoreCharacters
        /// starting from a blank CharacterState).</summary>
        public void ClearConcentration()
        {
            ConcentratingOnAbilityId = DefinitionId.None;
            ConcentrationEffectOwnerId = EffectOwnerId.None;
            concentrationAffectedTargets.Clear();
        }

        /// <summary>The last animation cue requested for this character via AnimationOrderService -- e.g. an
        /// attack swing or a combat-ready stance. DefinitionId.None means nothing's been explicitly requested.
        /// Deliberately does NOT cover continuous locomotion (idle/walk blending) -- that's a purely visual
        /// concern driven by movement speed, never routed through CharacterState. Purely a data record of the
        /// last request; whatever visual layer is watching AnimationCueChanged decides what to actually play.</summary>
        public DefinitionId CurrentAnimationCueId { get; set; }

        /// <summary>2D campaign: how this character LOOKS -- layer slot id -> option id (e.g. layer-hair -> hair-kenney-19-0),
        /// resolved to sprites by a SpriteLayerCatalog at the view. Unlike <see cref="CurrentAnimationCueId"/> it is durable
        /// (saved). Set it before the character's view is bound, or refresh the view's appearance binder afterwards.</summary>
        public IReadOnlyDictionary<DefinitionId, DefinitionId> Appearance => appearance;

        /// <summary>Sets one appearance entry. An invalid slot or option is ignored (no throw, like the other mutators);
        /// use <see cref="ClearAppearance"/> to remove an entry.</summary>
        public void SetAppearance(DefinitionId slot, DefinitionId option)
        {
            if (!slot.IsValid || !option.IsValid) return;
            appearance[slot] = option;
        }

        public void ClearAppearance(DefinitionId slot) => appearance.Remove(slot);

        public void ClearAllAppearance() => appearance.Clear();

        public IReadOnlyDictionary<DefinitionId, ResourceState> Resources => resources;
        public IReadOnlyList<ConditionInstance> Conditions => conditions;
        public IReadOnlyCollection<DefinitionId> Proficiencies => proficiencies;

        public void AddResource(ResourceState resource) => resources[resource.ResourceId] = resource;

        public bool TryGetResource(DefinitionId resourceId, out ResourceState resource) => resources.TryGetValue(resourceId, out resource);

        public void ApplyCondition(ConditionInstance condition) => conditions.Add(condition);

        public void RemoveCondition(ConditionInstance condition) => conditions.Remove(condition);

        /// <summary>Removes and returns every currently-applied condition matching <paramref name="predicate"/>
        /// (a real snapshot list, not a live view -- callers loop over the RETURN VALUE to log/publish events
        /// without touching this character's live condition list again, which is what keeps a ConditionRemoved
        /// subscriber that itself mutates conditions from corrupting an in-progress sweep). "Remove every
        /// stacked instance of this condition TYPE regardless of source" (a cure) and "remove only this specific
        /// cast's instance(s)" (<see cref="RemoveConditionsWithOwner"/>) are deliberately different operations --
        /// see CRPG.Abilities.Effects.RemoveConditionEffect vs. ConcentrationEffectCleanup/DispelEffect.</summary>
        public IReadOnlyList<ConditionInstance> RemoveConditions(Predicate<ConditionInstance> predicate)
        {
            var removed = conditions.FindAll(predicate);
            foreach (var instance in removed) conditions.Remove(instance);
            return removed;
        }

        /// <summary>Removes only the condition instance(s) a specific cast/effect application owns -- never
        /// matches an ownerless condition, even when called with <see cref="EffectOwnerId.None"/> (guarded
        /// internally so a caller can't accidentally broad-purge by passing an invalid owner).</summary>
        public IReadOnlyList<ConditionInstance> RemoveConditionsWithOwner(EffectOwnerId ownerId) =>
            ownerId.IsValid ? RemoveConditions(c => c.OwnerId == ownerId) : Array.Empty<ConditionInstance>();

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

        /// <summary>Once a loadout is assigned, an empty loadout must not fall back to legacy unrestricted
        /// NPC casting. The latch survives forgetting the last spell and is persisted by SaveService.</summary>
        public bool HasRestrictedAbilityAccess { get; private set; }

        public void LearnAbility(DefinitionId abilityId)
        {
            HasRestrictedAbilityAccess = true;
            knownAbilities.Add(abilityId);
        }

        /// <summary>Removes a permanent learned entry. Preparation and feature grants have independent
        /// ownership and are unchanged; callers migrating old prepared-as-known data can move it explicitly.</summary>
        public bool ForgetAbility(DefinitionId abilityId) => knownAbilities.Remove(abilityId);

        public bool KnowsAbility(DefinitionId abilityId) => knownAbilities.Contains(abilityId);

        /// <summary>A Prepared-caster's current day's selection from their class's full spell list. Replaced
        /// wholesale by PrepareSpells (see SpellPreparationService), not added to incrementally -- preparing
        /// spells is a single daily choice, not a permanent unlock.</summary>
        public IReadOnlyCollection<DefinitionId> PreparedAbilities => preparedAbilities;

        public void PrepareSpells(IEnumerable<DefinitionId> abilityIds)
        {
            HasRestrictedAbilityAccess = true;
            preparedAbilities.Clear();
            foreach (var abilityId in abilityIds) preparedAbilities.Add(abilityId);
        }

        public bool HasPrepared(DefinitionId abilityId) => preparedAbilities.Contains(abilityId);
    }
}

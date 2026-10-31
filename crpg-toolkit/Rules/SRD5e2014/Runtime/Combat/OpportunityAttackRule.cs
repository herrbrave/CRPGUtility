using System;
using System.Collections.Generic;
using CRPG.Abilities;
using CRPG.Actors;
using CRPG.Combat;
using CRPG.Core;
using UnityEngine;

namespace CRPG.Rules.SRD5
{
    /// <summary>
    /// A creature leaving reach can be struck once with an available normal melee attack, spending its
    /// opponent's Reaction rather than Action. Movement/crossing detection belongs to the combat engine;
    /// this policy selects and resolves the actual attack through the ordinary ability pipeline.
    /// </summary>
    public sealed class OpportunityAttackRule : IOpportunityAttackRule, IDisposable
    {
        private static readonly DefinitionId Disengaging = new("condition-disengaging");
        private static readonly DefinitionId Blinded = new("condition-blinded");
        private static readonly DefinitionId Invisible = new("condition-invisible");
        private static readonly DefinitionId HitPoints = new("resource-hp");
        private static readonly DefinitionId ActionResource = new("resource-action");

        private readonly ICharacterRegistry characters;
        private readonly IRulesDatabase rulesDatabase;
        private readonly IAbilityResolutionService abilities;
        private readonly IFactionProvider factions;
        private readonly IPositionProvider positions;
        private readonly ILineOfSightProvider lineOfSight;
        private readonly IPerceptionQuery perception;
        private readonly ResourceDefinition reactionResource;
        private readonly Dictionary<AbilityDefinition, AbilityDefinition> reactionAttacks = new();
        private bool disposed;

        public OpportunityAttackRule(
            ICharacterRegistry characters,
            IRulesDatabase rulesDatabase,
            IAbilityResolutionService abilities,
            IFactionProvider factions,
            IPositionProvider positions,
            ILineOfSightProvider lineOfSight,
            ResourceDefinition reactionResource,
            IPerceptionQuery perception = null)
        {
            this.characters = characters ?? throw new ArgumentNullException(nameof(characters));
            this.rulesDatabase = rulesDatabase ?? throw new ArgumentNullException(nameof(rulesDatabase));
            this.abilities = abilities ?? throw new ArgumentNullException(nameof(abilities));
            this.factions = factions ?? throw new ArgumentNullException(nameof(factions));
            this.positions = positions ?? throw new ArgumentNullException(nameof(positions));
            this.lineOfSight = lineOfSight ?? throw new ArgumentNullException(nameof(lineOfSight));
            this.perception = perception;
            this.reactionResource = reactionResource != null ? reactionResource : throw new ArgumentNullException(nameof(reactionResource));
        }

        public bool CanProvoke(RuntimeEntityId mover) =>
            !disposed && characters.TryGetCharacter(mover, out var character) && !character.HasCondition(Disengaging);

        /// <summary>Planning eligibility only: neither current distance nor current sight is authoritative
        /// for a route which may enter reach and leave it later. Resolve checks both at the crossing.</summary>
        public bool CanReact(RuntimeEntityId reactor, RuntimeEntityId mover)
        {
            if (!CanProvoke(mover) || reactor == mover
                || !characters.TryGetCharacter(reactor, out var character)
                || !factions.TryGetFaction(reactor, out var reactorFaction)
                || !factions.TryGetFaction(mover, out var moverFaction)
                || reactorFaction == moverFaction)
                return false;

            if (character.DeathSaves != null
                || (character.TryGetResource(HitPoints, out var hp) && hp.Current <= 0)
                || !character.TryGetResource(reactionResource.Id, out var reaction) || reaction.Current < 1)
                return false;

            foreach (var condition in character.Conditions)
            {
                if (rulesDatabase.TryResolve<ConditionDefinition>(condition.ConditionId, out var definition)
                    && definition.PreventsActions)
                    return false;
            }

            foreach (var attack in AvailableAttacks(character))
            {
                if (abilities.CanAttempt(reactor, ReactionFor(attack)).IsValid) return true;
            }

            return false;
        }

        public float ReachOf(RuntimeEntityId reactor)
        {
            if (disposed || !characters.TryGetCharacter(reactor, out var character)) return 0f;
            var reach = 0f;
            foreach (var attack in AvailableAttacks(character)) reach = Mathf.Max(reach, attack.Targeting.Range);
            return reach;
        }

        public bool Resolve(RuntimeEntityId reactor, RuntimeEntityId mover)
        {
            if (!CanReact(reactor, mover)) return false;
            characters.TryGetCharacter(reactor, out var character);
            characters.TryGetCharacter(mover, out var target);
            var origin = positions.GetPosition(reactor);
            var destination = positions.GetPosition(mover);
            if (perception != null)
            {
                if (!perception.CanSee(reactor, mover)) return false;
            }
            else if (character.HasCondition(Blinded) || target.HasCondition(Invisible) || !lineOfSight.HasLineOfSight(origin, destination)) return false;

            var distance = Vector3.Distance(origin, destination);
            var targets = new[] { mover };
            foreach (var attack in AvailableAttacks(character))
            {
                // Check each attack's own reach, not just the reactor's longest available reach. The
                // explicit check also protects callers whose ability service has no spatial providers.
                if (distance > attack.Targeting.Range) continue;
                var reaction = ReactionFor(attack);
                if (!abilities.Validate(reactor, reaction, targets).IsValid) continue;
                // Never try a second attack after committing: even a fault can already have spent Reaction.
                return abilities.Execute(reactor, reaction, targets).Succeeded;
            }

            return false;
        }

        private IEnumerable<AbilityDefinition> AvailableAttacks(CharacterState character)
        {
            var ids = new HashSet<DefinitionId>();
            foreach (var id in character.KnownAbilities) ids.Add(id);
            foreach (var id in character.PreparedAbilities) ids.Add(id);
            foreach (var featureId in character.Features)
            {
                if (!rulesDatabase.TryResolve<FeatureDefinition>(featureId, out var feature)) continue;
                foreach (var id in feature.GrantedAbilityIds) ids.Add(id);
            }

            // Do not enumerate the database: legacy unrestricted casting is not ownership of every
            // weapon attack in the game. A creature without an actual attack cannot make this reaction.
            foreach (var id in ids)
            {
                if (rulesDatabase.TryResolve<AbilityDefinition>(id, out var attack) && IsNormalMeleeAttack(attack))
                    yield return attack;
            }
        }

        private static bool IsNormalMeleeAttack(AbilityDefinition attack)
        {
            const AbilityTag required = AbilityTag.Attack | AbilityTag.Melee;
            const AbilityTag excluded = AbilityTag.Spell | AbilityTag.Ranged | AbilityTag.Reaction;
            if ((attack.Tags & required) != required || (attack.Tags & excluded) != 0 || attack.SpellLevel != 0)
                return false;

            // A normal Attack costs exactly one Action, not a class resource, bonus action or charge.
            // One top-level attack roll also excludes multi-effect actions; its on-hit graph is unrestricted.
            return attack.Costs.Count == 1 && attack.Costs[0].Resource != null
                && attack.Costs[0].Resource.Id == ActionResource && attack.Costs[0].Amount == 1
                && attack.Effects.Count == 1 && attack.Effects[0] is AttackRollEffect roll && roll.OnHit != null
                && attack.Targeting != null && attack.Targeting.Mode == TargetMode.SingleUnit
                && attack.Targeting.Range > 0f;
        }

        private AbilityDefinition ReactionFor(AbilityDefinition original)
        {
            if (!reactionAttacks.TryGetValue(original, out var reaction))
            {
                reaction = AbilityDefinition.CreateReaction(original, reactionResource);
                reactionAttacks.Add(original, reaction);
            }

            return reaction;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (var reaction in reactionAttacks.Values)
            {
                if (reaction == null) continue;
                if (Application.isPlaying) UnityEngine.Object.Destroy(reaction);
                else UnityEngine.Object.DestroyImmediate(reaction);
            }

            reactionAttacks.Clear();
        }
    }
}

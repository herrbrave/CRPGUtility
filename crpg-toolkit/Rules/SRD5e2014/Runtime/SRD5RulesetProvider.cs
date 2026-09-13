using System.Collections.Generic;
using CRPG.Actors;
using CRPG.Combat;
using CRPG.Core;
using UnityEngine;

namespace CRPG.Rules.SRD5
{
    /// <summary>
    /// Task 4.1's reference <see cref="IRulesetDefinition"/> -- registers every content asset this ruleset
    /// ships (see <c>SRD5RulesetProviderAuthor</c>, the Editor menu item that populates <see cref="content"/>
    /// from Rules/SRD5e2014/Content/) plus this ruleset's own default policy implementations. Mirrors the
    /// existing ad hoc *ContentBundle ScriptableObjects (FighterSampleContentBundle, OakhavenContentBundle,
    /// AIDemoContentBundle) in shape -- this formalizes that repeated convention behind a shared contract
    /// rather than replacing it outright; those bundles are untouched and still valid for their own demos.
    /// <para>
    /// Deliberately does NOT register DeathSaveService/ConcentrationService -- both are plain services, not
    /// policy instances behind a swappable interface (there's no IDeathSaveService/IConcentrationService to key
    /// a PolicyRegistry slot on), and both currently have zero production callers. Whichever task actually
    /// starts using them constructs them directly, exactly as AICombatDemoBootstrap already does for
    /// DeathSaveService today.
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "CRPG Toolkit/Rules/SRD5 Ruleset Provider")]
    public sealed class SRD5RulesetProvider : ScriptableObject, IRulesetDefinition
    {
        private static readonly DefinitionId DexterityStatId = new("stat-dexterity");
        private static readonly DefinitionId ExhaustionConditionId = new("condition-exhaustion");

        [SerializeField] private List<DefinitionAsset> content = new();

        public IReadOnlyList<DefinitionAsset> Content => content;

        public string RulesetId => "srd5e2014";

        public void Register(IRulesetRegistrationBuilder builder)
        {
            builder.RegisterDefinitions(content);

            builder.RegisterPolicy<IArmorClassRule>(new DefaultArmorClassRule(builder.Characters, builder.RulesDatabase, DexterityStatId));
            builder.RegisterPolicy<IAttackBonusRule>(new DefaultAttackBonusRule(builder.Characters, builder.RulesDatabase));
            builder.RegisterPolicy<IDamageResistanceRule>(new DefaultDamageResistanceRule(builder.Characters, builder.RulesDatabase));
            builder.RegisterPolicy<IInitiativeRule>(new DefaultInitiativeRule(builder.Characters, builder.RulesDatabase, builder.DiceService));
            builder.RegisterPolicy<IMovementSpeedRule>(new DefaultMovementSpeedRule(builder.Characters, builder.RulesDatabase, ExhaustionConditionId));
            builder.RegisterPolicy<ISavingThrowRule>(new DefaultSavingThrowRule(builder.Characters, builder.RulesDatabase));
            builder.RegisterPolicy<ISkillCheckRule>(new DefaultSkillCheckRule(builder.Characters, builder.RulesDatabase));
        }
    }
}

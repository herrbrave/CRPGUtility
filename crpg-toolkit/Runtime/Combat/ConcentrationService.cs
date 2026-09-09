using CRPG.Actors;
using CRPG.Core;

namespace CRPG.Combat
{
    /// <summary>SRD5's "taking damage while concentrating" rule: a Constitution saving throw, DC = the greater
    /// of 10 or half the damage taken (rounded down), or concentration ends. Layered on DamageEffect's generic
    /// BeforeDamage event exactly like DeathSaveService is layered on the same event -- DamageEffect itself
    /// knows nothing about concentration. Self-subscribes on construction since its whole job is reacting to
    /// that one event.</summary>
    public sealed class ConcentrationService
    {
        private readonly ICharacterRegistry characters;
        private readonly IDiceService diceService;
        private readonly ISavingThrowRule savingThrowRule;
        private readonly IGameEventBus eventBus;
        private readonly ICombatLog combatLog;
        private readonly DefinitionId constitutionStatId;

        public ConcentrationService(
            ICharacterRegistry characters,
            IDiceService diceService,
            ISavingThrowRule savingThrowRule,
            IGameEventBus eventBus,
            DefinitionId constitutionStatId,
            ICombatLog combatLog = null)
        {
            this.characters = characters;
            this.diceService = diceService;
            this.savingThrowRule = savingThrowRule;
            this.eventBus = eventBus;
            this.constitutionStatId = constitutionStatId;
            this.combatLog = combatLog;

            eventBus.Subscribe(GameEvent.BeforeDamage, OnBeforeDamage);
        }

        private void OnBeforeDamage(GameEventContext context)
        {
            if (!characters.TryGetCharacter(context.Target, out var character)) return;
            if (!character.ConcentratingOnAbilityId.IsValid) return;

            var amount = context.Data.TryGetValue("Amount", out var value) && value is int intValue ? intValue : 0;
            if (amount <= 0) return;

            var dc = System.Math.Max(10, amount / 2);

            if (savingThrowRule.AutoFails(context.Target, constitutionStatId))
            {
                BreakConcentration(context.Target, character);
                return;
            }

            var bonus = savingThrowRule.ComputeSavingThrowBonus(context.Target, constitutionStatId);
            var advantage = savingThrowRule.ComputeAdvantage(context.Target, constitutionStatId);
            var roll = diceService.Roll(new RollRequest(
                new DiceExpression(1, 20), RollPurpose.SavingThrow, advantage, source: context.Target, target: context.Target,
                modifiers: new[] { new RollModifier("Concentration", bonus) }));

            var succeeded = roll.Total >= dc;
            combatLog?.Log(new SavingThrowLogEntry(context.Target, context.Target, roll, dc, constitutionStatId, succeeded));

            if (!succeeded) BreakConcentration(context.Target, character);
        }

        private void BreakConcentration(RuntimeEntityId characterId, CharacterState character)
        {
            character.ConcentratingOnAbilityId = DefinitionId.None;
            eventBus.Publish(new GameEventContext(GameEvent.ConcentrationBroken, characterId, characterId));
        }
    }
}

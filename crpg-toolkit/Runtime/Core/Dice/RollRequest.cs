using System;
using System.Collections.Generic;

namespace CRPG.Core
{
    public sealed class RollRequest
    {
        public RollRequest(
            DiceExpression dice,
            RollPurpose purpose = RollPurpose.Generic,
            AdvantageState advantage = AdvantageState.Normal,
            RuntimeEntityId source = default,
            RuntimeEntityId target = default,
            IReadOnlyList<RollModifier> modifiers = null)
        {
            Dice = dice;
            Purpose = purpose;
            Advantage = advantage;
            Source = source;
            Target = target;
            Modifiers = modifiers ?? Array.Empty<RollModifier>();
        }

        public DiceExpression Dice { get; }
        public AdvantageState Advantage { get; }
        public IReadOnlyList<RollModifier> Modifiers { get; }
        public RuntimeEntityId Source { get; }
        public RuntimeEntityId Target { get; }
        public RollPurpose Purpose { get; }
    }
}

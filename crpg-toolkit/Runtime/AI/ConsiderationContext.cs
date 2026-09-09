using CRPG.Actors;
using CRPG.Core;

namespace CRPG.AI
{
    /// <summary>Everything a Consideration input might need to compute its raw value for one (Self, Target)
    /// pairing being scored.</summary>
    public sealed class ConsiderationContext
    {
        public ConsiderationContext(
            RuntimeEntityId self,
            RuntimeEntityId target,
            ICharacterRegistry characters,
            IPositionProvider positions,
            IRandomSource randomSource,
            InfluenceMap influenceMap = null)
        {
            Self = self;
            Target = target;
            Characters = characters;
            Positions = positions;
            RandomSource = randomSource;
            InfluenceMap = influenceMap;
        }

        public RuntimeEntityId Self { get; }
        public RuntimeEntityId Target { get; }
        public ICharacterRegistry Characters { get; }
        public IPositionProvider Positions { get; }
        public IRandomSource RandomSource { get; }

        /// <summary>Null if this encounter has no spatial hazards to reason about.</summary>
        public InfluenceMap InfluenceMap { get; }
    }
}

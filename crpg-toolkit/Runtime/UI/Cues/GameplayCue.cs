using CRPG.Core;

namespace CRPG.UI
{
    /// <summary>A typed, data-only signal that something audio/VFX-worthy happened -- store buy/sell, an
    /// equip/unequip/drop, a scene-travel completion. No audio/VFX implementation ships in this package (design
    /// doc: "do not embed licensed media in runtime"); this is only the seam a game implementer's own
    /// <see cref="IGameplayCueSink"/> consumes.</summary>
    public readonly struct GameplayCue
    {
        public GameplayCue(string cueId, RuntimeEntityId? entityId = null, DefinitionId? actionId = null)
        {
            CueId = cueId;
            EntityId = entityId;
            ActionId = actionId;
        }

        public string CueId { get; }
        public RuntimeEntityId? EntityId { get; }
        public DefinitionId? ActionId { get; }
    }
}

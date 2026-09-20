using CRPG.Core;

namespace CRPG.Presentation.Sprites
{
    /// <summary>Resolves an appearance choice -- (layer slot, option id) -- to the sprite and tint to draw. The runtime
    /// implementation is <see cref="SpriteCatalogSource"/>, built over the SpriteLayerCatalog assets in a rules database; a test or a
    /// creator preview may supply its own.</summary>
    public interface ISpriteOptionSource
    {
        bool TryGetOption(DefinitionId slot, DefinitionId option, out SpriteOption result);
    }
}

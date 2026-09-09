using System.Collections.Generic;

namespace CRPG.Core
{
    /// <summary>
    /// Resolves stable <see cref="DefinitionId"/>s to their <see cref="IDefinition"/> asset. Save files, mods
    /// and DLC only ever store a <see cref="DefinitionId"/>; this is the one place that turns it back into content.
    /// </summary>
    public interface IRulesDatabase
    {
        bool TryResolve<TDefinition>(DefinitionId id, out TDefinition definition) where TDefinition : class, IDefinition;

        TDefinition Resolve<TDefinition>(DefinitionId id) where TDefinition : class, IDefinition;

        IEnumerable<TDefinition> All<TDefinition>() where TDefinition : class, IDefinition;
    }
}

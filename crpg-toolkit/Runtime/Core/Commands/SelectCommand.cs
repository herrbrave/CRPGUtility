using System.Collections.Generic;

namespace CRPG.Core
{
    public readonly struct SelectCommand
    {
        public SelectCommand(IReadOnlyList<RuntimeEntityId> characters, SelectionMode mode)
        {
            Characters = characters;
            Mode = mode;
        }

        public IReadOnlyList<RuntimeEntityId> Characters { get; }
        public SelectionMode Mode { get; }
    }
}

using System;

namespace CRPG.Core
{
    /// <summary>
    /// What Exploration (and later Combat/Abilities) listens to for player intent. The Input System package
    /// is just one possible producer -- an Integrations/InputSystem adapter raycasts mouse clicks into
    /// world-space commands and raises these events; gameplay code never touches Input System types directly.
    /// </summary>
    public interface IPlayerCommandSource
    {
        event Action<SelectCommand> Select;
        event Action<MoveCommand> Move;
    }
}

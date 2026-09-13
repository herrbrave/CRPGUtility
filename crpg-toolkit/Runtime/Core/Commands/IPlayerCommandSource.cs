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

        /// <summary>Raised when a click/drag resolves onto something interactable rather than a selectable
        /// party member or bare ground. No consumer exists yet (Task 3.3's shared interaction lifecycle) --
        /// this is the reserved wiring point, same shape as <see cref="Select"/>/<see cref="Move"/>.</summary>
        event Action<InteractCommand> Interact;

        /// <summary>Raised on an explicit cancel (Escape, ...). Carries no payload -- what "cancel" means is
        /// entirely up to whatever's currently listening (an in-flight targeting session, an open modal, ...).</summary>
        event Action Cancel;
    }
}

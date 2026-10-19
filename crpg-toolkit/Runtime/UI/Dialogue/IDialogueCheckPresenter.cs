using System;
using CRPG.Dialogue;

namespace CRPG.UI
{
    /// <summary>
    /// A game's own way to show a skill check: the <see cref="DialogueView"/> hands it the prepared check instead of showing its built-in Roll button. The presenter shows whatever it likes (a dice popup with optional bonuses), calls <paramref name="roll"/> ONCE with the
    /// player's choices (the conversation moves on to the success or failure node at that moment), shows the result, and calls <paramref name="done"/> when the player has seen it; the view then continues. Without one the built-in Roll button is used.
    /// </summary>
    public interface IDialogueCheckPresenter
    {
        void Present(CheckPrompt prompt, Func<CheckRollOptions, DialogueCheckOutcome> roll, Action done);
    }
}

namespace CRPG.UI
{
    /// <summary>One currently-visible ChoiceOption, paired with its index in the ChoiceNode's full Options
    /// list -- the index DialogueRunner.ChooseOption expects, which is not the same as its position among only
    /// the visible choices once any option is Timeline-gated.</summary>
    public sealed class DialogueChoiceSummary
    {
        public DialogueChoiceSummary(int optionIndex, string text)
        {
            OptionIndex = optionIndex;
            Text = text;
        }

        public int OptionIndex { get; }
        public string Text { get; }
    }
}

namespace CRPG.UI
{
    /// <summary>Presentation-friendly snapshot of one quest's item reward -- QuestItemReward itself was never
    /// surfaced by QuestLogPresenter before Task 3.6.</summary>
    public sealed class QuestRewardSummary
    {
        public QuestRewardSummary(TextKey itemName, int quantity)
        {
            ItemName = itemName;
            Quantity = quantity;
        }

        public TextKey ItemName { get; }
        public int Quantity { get; }
    }
}

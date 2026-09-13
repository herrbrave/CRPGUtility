namespace CRPG.UI
{
    public enum InventoryOperationOutcome
    {
        Success,
        ItemNotFound,
        NotEquippable,
        WrongSlot,
        ItemEquipped,
        NotSupportedYet,
    }

    public sealed class InventoryOperationResult
    {
        private InventoryOperationResult(InventoryOperationOutcome outcome)
        {
            Outcome = outcome;
        }

        public InventoryOperationOutcome Outcome { get; }
        public bool Success => Outcome == InventoryOperationOutcome.Success;

        public static InventoryOperationResult Ok() => new(InventoryOperationOutcome.Success);
        public static InventoryOperationResult Fail(InventoryOperationOutcome outcome) => new(outcome);
    }
}

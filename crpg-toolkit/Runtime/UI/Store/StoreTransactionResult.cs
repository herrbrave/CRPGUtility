namespace CRPG.UI
{
    public enum StoreTransactionOutcome { Success, NotListed, OutOfStock, InsufficientFunds, ItemNotOwned, ItemEquipped }

    public sealed class StoreTransactionResult
    {
        private StoreTransactionResult(StoreTransactionOutcome outcome)
        {
            Outcome = outcome;
        }

        public StoreTransactionOutcome Outcome { get; }
        public bool Success => Outcome == StoreTransactionOutcome.Success;

        public static StoreTransactionResult Ok() => new(StoreTransactionOutcome.Success);
        public static StoreTransactionResult Fail(StoreTransactionOutcome outcome) => new(outcome);
    }
}

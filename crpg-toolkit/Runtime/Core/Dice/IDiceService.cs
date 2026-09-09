namespace CRPG.Core
{
    public interface IDiceService
    {
        RollResult Roll(RollRequest request);
    }
}

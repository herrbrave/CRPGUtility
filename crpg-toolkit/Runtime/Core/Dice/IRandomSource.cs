namespace CRPG.Core
{
    /// <summary>Never use UnityEngine.Random from gameplay/rules code — go through this so rolls are testable,
    /// replayable and save/load-safe.</summary>
    public interface IRandomSource
    {
        int Next(int minInclusive, int maxExclusive);
    }

    public static class RandomSourceExtensions
    {
        public static int RollDie(this IRandomSource random, int sides) => random.Next(1, sides + 1);
    }
}

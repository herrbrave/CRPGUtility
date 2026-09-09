using System;

namespace CRPG.Saving
{
    [Serializable]
    public sealed class SaveHeader
    {
        public int FormatVersion;
        public string SavedAtUtc;
    }
}

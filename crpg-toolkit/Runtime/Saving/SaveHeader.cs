using System;

namespace CRPG.Saving
{
    [Serializable]
    public sealed class SaveHeader
    {
        public int FormatVersion;
        public string SavedAtUtc;

        /// <summary>SceneManager.GetActiveScene().path at capture time -- a full path, not a short name, since
        /// short scene names aren't guaranteed unique (Task 3.5). A load whose recorded path differs from the
        /// currently active scene is rejected outright (SceneMismatch) -- cross-scene load/travel is Task 3.6's
        /// job, not this one's.</summary>
        public string ScenePath;

        /// <summary>Caller-supplied opaque identifier for the active ruleset. There is no real ruleset-identity/
        /// versioning system yet (that's Task 4.1's job) -- this is an honest placeholder passthrough a loader
        /// can compare against its own expected value, not a full versioning system.</summary>
        public string RulesetId;

        /// <summary>Caller-supplied opaque identifier for authored-content compatibility, same rationale as
        /// RulesetId -- there is no content-versioning system to compute this from yet.</summary>
        public string ContentCompatibilityId;
    }
}

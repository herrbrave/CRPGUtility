using CRPG.Core;

namespace CRPG.Rules.SRD5
{
    public static class CommonActions
    {
        public static readonly DefinitionId GrantId = new("feature-common-actions");
        public static readonly DefinitionId MovementId = new("resource-movement");
        public static readonly DefinitionId DodgingId = new("condition-dodging");
        public static readonly DefinitionId DisengagingId = new("condition-disengaging");
        public static readonly DefinitionId HelpedId = new("condition-helped");
        public static readonly DefinitionId DistractedId = new("condition-distracted");

        public static bool IsTurnCondition(DefinitionId id) => id == DodgingId || id == DisengagingId || id == HelpedId || id == DistractedId;
    }
}

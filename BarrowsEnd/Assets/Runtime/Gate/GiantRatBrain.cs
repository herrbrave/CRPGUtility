using System.Collections.Generic;
using System.Reflection;
using CRPG.AI;
using CRPG.Core;

namespace BarrowsEnd.Gate
{
    /// <summary>Task 6.1: the palisade-breach tutorial's hostile AI -- a single "Attack Nearest" behavior (no
    /// "Close Distance" move behavior: rats spawn already at melee range, per TacticalCombatSession's own
    /// documented scope cut). Built and registered at runtime via reflection, the exact pattern
    /// <c>TestProject/Assets/TacticalSlice/Scripts/TacticalSliceBootstrap.cs</c>'s own <c>BuildGoblinBehaviorSet</c>
    /// already proves works (real `ScriptableObject.CreateInstance` + private-field reflection, then
    /// <c>RulesDatabase.Register</c> so <see cref="UtilityAIDecisionProvider"/> can resolve <c>behaviorIds</c> by
    /// DefinitionId at decision time) -- not authored as a design-time asset, since this content has no need to
    /// be hand-tunable in the Inspector and every other piece of Task 6.1's combat/dialogue content is
    /// authored from code anyway.</summary>
    public static class GiantRatBrain
    {
        public const string BehaviorSetId = "behaviorset-giant-rat";
        private const string AttackBehaviorId = "behavior-giant-rat-attack";

        // Duplicated from BarrowsEndGearAuthor.RatBiteAbilityId (Editor-only, not reachable from Runtime) --
        // same "duplicate the id/path string across the Editor/Runtime boundary" convention BarrowsEndTownAuthor's
        // own GalleryPanelSettingsPath comment already established for this project.
        private const string RatBiteAbilityId = "ability-rat-bite";

        /// <summary>Idempotent: returns the already-registered set on a second call in the same session (a
        /// second rat encounter, or a repeat trigger) rather than re-registering and throwing. Takes
        /// <see cref="IRulesDatabase"/> like every other consumer in this project, downcasting internally to the
        /// concrete <see cref="RulesDatabase"/> its own <c>Register</c> method needs -- the runtime type is
        /// always <c>RulesDatabase</c> (confirmed: <c>RulesetSessionFactory.BuildAndValidate</c> is the only
        /// place a session's RulesDatabase is ever constructed).</summary>
        public static BehaviorSetDefinition BuildAndRegister(IRulesDatabase rulesDatabase)
        {
            if (rulesDatabase.TryResolve<BehaviorSetDefinition>(new DefinitionId(BehaviorSetId), out var existing))
                return existing;

            var concrete = (RulesDatabase)rulesDatabase;

            var attack = UnityEngine.ScriptableObject.CreateInstance<BehaviorDefinition>();
            SetField(attack, "id", AttackBehaviorId);
            SetField(attack, "displayName", "Bite Nearest");
            SetField(attack, "abilityId", new DefinitionId(RatBiteAbilityId));
            SetField(attack, "considerations", new List<Consideration>());
            concrete.Register(attack);

            var set = UnityEngine.ScriptableObject.CreateInstance<BehaviorSetDefinition>();
            SetField(set, "id", BehaviorSetId);
            SetField(set, "displayName", "Giant Rat");
            SetField(set, "behaviorIds", new List<DefinitionId> { attack.Id });
            concrete.Register(set);

            return set;
        }

        private static void SetField(object target, string fieldName, object value)
        {
            var type = target.GetType();
            while (type != null)
            {
                var field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (field != null) { field.SetValue(target, value); return; }
                type = type.BaseType;
            }

            throw new System.Exception($"Field '{fieldName}' not found on {target.GetType().Name}.");
        }
    }
}

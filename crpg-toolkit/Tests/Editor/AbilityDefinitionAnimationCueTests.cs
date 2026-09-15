using CRPG.Abilities;
using CRPG.Core;
using NUnit.Framework;
using UnityEngine;

namespace CRPG.Tests.EditMode
{
    /// <summary>Task 4.5: <see cref="AbilityDefinition.AnimationCueId"/> is purely additive -- every existing
    /// asset must deserialize with it defaulting to <see cref="DefinitionId.None"/>, and an authored value must
    /// round-trip like any other bare tag field.</summary>
    public class AbilityDefinitionAnimationCueTests
    {
        [Test]
        public void DefaultsToNoneForAnExistingUnmodifiedAsset()
        {
            var ability = ScriptableObject.CreateInstance<AbilityDefinition>();
            Assert.AreEqual(DefinitionId.None, ability.AnimationCueId);
        }

        [Test]
        public void RoundTripsAnAuthoredCueId()
        {
            var ability = ScriptableObject.CreateInstance<AbilityDefinition>();
            var cueId = new DefinitionId("attack-melee");

            TestDefinitionFactory.SetPrivateField(ability, "animationCueId", cueId);

            Assert.AreEqual(cueId, ability.AnimationCueId);
        }
    }
}

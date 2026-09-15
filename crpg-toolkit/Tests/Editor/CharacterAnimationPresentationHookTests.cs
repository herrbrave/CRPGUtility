using CRPG.Abilities;
using CRPG.Core;
using CRPG.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace CRPG.Tests.EditMode
{
    /// <summary>Task 4.5: covers only the target-independent "no cue authored / no live agent" no-op paths in
    /// EditMode (no live scene needed, since both return before ever touching
    /// <see cref="CRPG.Gameplay.CharacterAgent.AllLive"/>). The real "finds the bound animator and plays the
    /// cue" happy path needs a genuinely live <see cref="CharacterAgent"/>/<see cref="CRPG.Presentation.Animation.CharacterPlayableAnimator"/>
    /// pair (CharacterAgent's OnEnable-populated live registry is documented elsewhere in this project as
    /// unreliable outside PlayMode) -- that path is proven end-to-end by the PlayMode
    /// TacticalEncounterControllerTests instead, deliberately, rather than duplicating a bespoke fixture here.</summary>
    public class CharacterAnimationPresentationHookTests
    {
        [Test]
        public void NeverThrowsAndDoesNothingWhenTheAbilityHasNoAuthoredCue()
        {
            var ability = ScriptableObject.CreateInstance<AbilityDefinition>();
            var hook = new CharacterAnimationPresentationHook();

            Assert.DoesNotThrow(() => hook.RequestActionCue(RuntimeEntityId.NewId(), ability));
        }

        [Test]
        public void NeverThrowsWhenNoLiveAgentExistsForTheSource()
        {
            var ability = ScriptableObject.CreateInstance<AbilityDefinition>();
            TestDefinitionFactory.SetPrivateField(ability, "animationCueId", new DefinitionId("attack-melee"));
            var hook = new CharacterAnimationPresentationHook();

            // No CharacterAgent was ever instantiated/enabled this test -- CharacterAgent.AllLive is empty.
            Assert.DoesNotThrow(() => hook.RequestActionCue(RuntimeEntityId.NewId(), ability));
        }
    }
}

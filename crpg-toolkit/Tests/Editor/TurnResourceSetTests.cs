using CRPG.Actors;
using CRPG.Combat;
using CRPG.Core;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    public class TurnResourceSetTests
    {
        [Test]
        public void RestoreFullRestoresEveryConfiguredResourceToMax()
        {
            var action = TestDefinitionFactory.CreateResourceDefinition("resource-action", "Action");
            var bonusAction = TestDefinitionFactory.CreateResourceDefinition("resource-bonus-action", "Bonus Action");

            var character = new CharacterState(RuntimeEntityId.NewId());
            character.AddResource(new ResourceState(action.Id, max: 1, current: 0));
            character.AddResource(new ResourceState(bonusAction.Id, max: 1, current: 0));

            var turnResources = new TurnResourceSet(new[] { action, bonusAction });
            turnResources.RestoreFull(character);

            character.TryGetResource(action.Id, out var actionState);
            character.TryGetResource(bonusAction.Id, out var bonusState);
            Assert.AreEqual(1, actionState.Current);
            Assert.AreEqual(1, bonusState.Current);
        }
    }
}

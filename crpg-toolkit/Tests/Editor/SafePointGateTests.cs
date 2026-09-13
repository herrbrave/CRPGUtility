using System;
using CRPG.Actors;
using CRPG.Combat;
using CRPG.Core;
using CRPG.Gameplay;
using CRPG.Gameplay.Scenes;
using CRPG.Rules;
using CRPG.Saving;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    /// <summary>Task 3.5: SafePointGate.CheckSafeToSave() against each real dependency independently. All
    /// three dependencies are optional -- a scene wiring none of them up is still safe to save.
    /// InteractionInProgress specifically is instead covered by InteractionLifecycleTests.cs (PlayMode) --
    /// CharacterAgent.AllLive/WorldInteractable.AllLive (which a real InteractionController resolves performer/
    /// target through) are populated by OnEnable, which Unity does not reliably dispatch synchronously within
    /// a plain EditMode test for freshly AddComponent'd objects (confirmed empirically); PlayMode's real player
    /// loop does not have this issue, and that file already has the exact fixtures needed.</summary>
    public class SafePointGateTests
    {
        [Test]
        public void NoneWhenNoDependenciesAreWired()
        {
            var gate = new SafePointGate();
            Assert.AreEqual(SafePointBlockReason.None, gate.CheckSafeToSave());
        }

        private static CombatEncounterService CreateCombatEncounterService(CharacterRegistry characters)
        {
            var rulesDatabase = new RulesDatabase();
            var diceService = new DiceService(new SeededRandomSource(1));
            return new CombatEncounterService(characters, new DefaultInitiativeRule(characters, rulesDatabase, diceService), new TurnResourceSet(Array.Empty<ResourceDefinition>()));
        }

        [Test]
        public void CombatActiveBlocksSaving()
        {
            var characters = new CharacterRegistry();
            var performerId = RuntimeEntityId.NewId();
            characters.Register(new CharacterState(performerId));
            var combat = CreateCombatEncounterService(characters);
            combat.StartCombat(new[] { performerId });

            var gate = new SafePointGate(combat: combat);

            Assert.AreEqual(SafePointBlockReason.CombatActive, gate.CheckSafeToSave());
        }

        [Test]
        public void NoBlockOnceCombatEnds()
        {
            var characters = new CharacterRegistry();
            var performerId = RuntimeEntityId.NewId();
            characters.Register(new CharacterState(performerId));
            var combat = CreateCombatEncounterService(characters);
            combat.StartCombat(new[] { performerId });
            combat.EndCombat();

            var gate = new SafePointGate(combat: combat);

            Assert.AreEqual(SafePointBlockReason.None, gate.CheckSafeToSave());
        }

        [Test]
        public void LoadInProgressBlocksSaving()
        {
            var loadCoordinator = new SessionLoadCoordinator();
            TestDefinitionFactory.SetPrivateField(loadCoordinator, "isLoading", true);

            var gate = new SafePointGate(loadCoordinator: loadCoordinator);

            Assert.AreEqual(SafePointBlockReason.LoadInProgress, gate.CheckSafeToSave());
        }

        [Test]
        public void SceneTransitionInProgressBlocksSavingAndTakesPriorityOverLoadInProgress()
        {
            var transitionGate = new WorldTransitionGate();
            transitionGate.BeginBusy("scene-travel");

            var loadCoordinator = new SessionLoadCoordinator();
            TestDefinitionFactory.SetPrivateField(loadCoordinator, "isLoading", true);

            var gate = new SafePointGate(loadCoordinator: loadCoordinator, transitionGate: transitionGate);

            Assert.AreEqual(SafePointBlockReason.SceneTransitionInProgress, gate.CheckSafeToSave());
        }

        [Test]
        public void NoBlockWhenTheSharedTransitionGateIsWiredButNotBusy()
        {
            var gate = new SafePointGate(transitionGate: new WorldTransitionGate());
            Assert.AreEqual(SafePointBlockReason.None, gate.CheckSafeToSave());
        }
    }
}

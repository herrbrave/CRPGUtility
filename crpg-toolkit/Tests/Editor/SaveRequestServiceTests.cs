using System;
using CRPG.Actors;
using CRPG.Combat;
using CRPG.Core;
using CRPG.Gameplay;
using CRPG.Rules;
using CRPG.Saving;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    /// <summary>Task 3.5: the real save entrypoint -- proves a blocked safe-point aborts before any write is
    /// even attempted (not just that SafePointGate itself reports the right reason in isolation).</summary>
    public class SaveRequestServiceTests
    {
        private sealed class SpyFileIO : ISaveFileIO
        {
            public bool AnyWriteAttempted;
            public bool FileExists(string path) => false;
            public string ReadAllText(string path) => throw new InvalidOperationException("Not expected to be called.");
            public void WriteAllText(string path, string contents) => AnyWriteAttempted = true;
            public void Replace(string sourceFileName, string destinationFileName, string destinationBackupFileName) => AnyWriteAttempted = true;
            public void Move(string sourceFileName, string destinationFileName) => AnyWriteAttempted = true;
        }

        private static CombatEncounterService CreateActiveCombat(out RuntimeEntityId combatantId)
        {
            var characters = new CharacterRegistry();
            combatantId = RuntimeEntityId.NewId();
            characters.Register(new CharacterState(combatantId));
            var rulesDatabase = new RulesDatabase();
            var combat = new CombatEncounterService(characters, new DefaultInitiativeRule(characters, rulesDatabase, new DiceService(new SeededRandomSource(1))), new TurnResourceSet(Array.Empty<ResourceDefinition>()));
            combat.StartCombat(new[] { combatantId });
            return combat;
        }

        [Test]
        public void ABlockedSafePointAbortsBeforeAnyWriteAttempt()
        {
            var session = new GameSession(new RulesDatabase(), new DiceService(new SeededRandomSource(1)));
            var worldSpawnService = new WorldSpawnService(session);
            var combat = CreateActiveCombat(out _);
            var safePointGate = new SafePointGate(combat: combat);
            var io = new SpyFileIO();
            var saveFileStore = new SaveFileStore(io);
            var requestService = new SaveRequestService(session, worldSpawnService, safePointGate, saveFileStore);

            var result = requestService.RequestSave("irrelevant.json");

            Assert.AreEqual(SaveRequestOutcome.Blocked, result.Outcome);
            Assert.AreEqual(SafePointBlockReason.CombatActive, result.BlockReason);
            Assert.IsFalse(io.AnyWriteAttempted, "A blocked save must never reach SaveFileStore.Save.");
        }

        [Test]
        public void ASafeRequestWritesSuccessfully()
        {
            var session = new GameSession(new RulesDatabase(), new DiceService(new SeededRandomSource(1)));
            var worldSpawnService = new WorldSpawnService(session);
            var safePointGate = new SafePointGate();
            var io = new SpyFileIO();
            var saveFileStore = new SaveFileStore(io);
            var requestService = new SaveRequestService(session, worldSpawnService, safePointGate, saveFileStore);

            var result = requestService.RequestSave("irrelevant.json");

            Assert.AreEqual(SaveRequestOutcome.Success, result.Outcome);
            Assert.IsTrue(io.AnyWriteAttempted);
        }
    }
}

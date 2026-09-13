using System.Collections.Generic;
using System.Linq;
using CRPG.Core;
using CRPG.Inventory;
using CRPG.Saving;
using UnityEngine.SceneManagement;

namespace CRPG.Gameplay
{
    /// <summary>
    /// Task 3.5: the actual save entrypoint -- checks SafePointGate BEFORE touching anything (a blocked
    /// request never reaches SaveFileStore), then gathers session state + the live WorldSpawnService's
    /// CaptureSnapshot() + whatever store/loot dictionaries the composition root registered, and writes.
    /// Task 3.6: implements ISaveRequestPort so a CRPG.UI SavePresenter can depend on the port (in CRPG.Saving)
    /// instead of on this type directly, avoiding a CRPG.UI -> CRPG.Gameplay cycle.
    /// </summary>
    public sealed class SaveRequestService : ISaveRequestPort
    {
        private readonly GameSession session;
        private readonly WorldSpawnService worldSpawnService;
        private readonly SafePointGate safePointGate;
        private readonly SaveFileStore saveFileStore;
        private readonly string rulesetId;
        private readonly string contentCompatibilityId;

        public SaveRequestService(
            GameSession session,
            WorldSpawnService worldSpawnService,
            SafePointGate safePointGate,
            SaveFileStore saveFileStore = null,
            string rulesetId = null,
            string contentCompatibilityId = null)
        {
            this.session = session;
            this.worldSpawnService = worldSpawnService;
            this.safePointGate = safePointGate;
            this.saveFileStore = saveFileStore ?? new SaveFileStore();
            this.rulesetId = rulesetId;
            this.contentCompatibilityId = contentCompatibilityId;
        }

        public SaveRequestResult RequestSave(string path)
        {
            var block = safePointGate.CheckSafeToSave();
            if (block != SafePointBlockReason.None) return SaveRequestResult.Blocked(block);

            var worldSpawns = worldSpawnService.CaptureSnapshot().ToList();
            var additionalCharacterIds = worldSpawns
                .Where(s => !s.Defeated && !string.IsNullOrEmpty(s.CharacterId))
                .Select(s => RuntimeEntityId.FromString(s.CharacterId));

            // Task 3.6: sourced from the session itself (every location's state it has ever brought into
            // existence, whether or not that location is currently loaded) rather than whatever a single
            // currently-active scene's own bootstrap happened to construct -- a save must capture every
            // visited location's store/loot state, not just the active one.
            var data = SaveService.Capture(
                session.Party, session.Characters, session.Inventory, session.Timeline, session.Quests,
                additionalCharacterIds, worldSpawns, session.Stores, session.LootContainers,
                SceneManager.GetActiveScene().path, rulesetId, contentCompatibilityId);

            var json = SaveService.ToJson(data);
            var writeResult = saveFileStore.Save(path, json);
            return writeResult.Success ? SaveRequestResult.Ok() : SaveRequestResult.WriteFailed(writeResult.Message);
        }
    }
}

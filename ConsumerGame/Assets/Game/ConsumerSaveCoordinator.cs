using System.Collections.Generic;
using CRPG.Actors;
using CRPG.Combat;
using CRPG.Consumer.Fate;
using CRPG.Core;
using CRPG.Gameplay;
using CRPG.Saving;
using UnityEngine;

namespace CRPG.Consumer.Game
{
    /// <summary>
    /// Owns the save/load-specific wiring only -- SafePointGate/SaveRequestService, and the ruleset-aware
    /// SessionLoadCoordinator path (Task 5.2's Gap 1 fix), including re-supplying the same authored
    /// FateRulesetProvider asset on reload. Deliberately not the whole bootstrap.
    /// </summary>
    public sealed class ConsumerSaveCoordinator
    {
        private static readonly System.Type[] RequiredPolicies = { typeof(ISkillCheckRule) };

        private readonly GameSessionHost host;
        private readonly FateRulesetProvider provider;
        private readonly SaveFileStore saveFileStore = new();

        public SafePointGate SafePointGate { get; }

        public ConsumerSaveCoordinator(GameSessionHost host, FateRulesetProvider provider, CombatEncounterService combat)
        {
            this.host = host;
            this.provider = provider;
            SafePointGate = new SafePointGate(combat: combat);
        }

        public SaveRequestResult Save(WorldSpawnService worldSpawnService, string path)
        {
            var saveRequestService = new SaveRequestService(host.Session, worldSpawnService, SafePointGate, saveFileStore, rulesetId: provider.RulesetId);
            return saveRequestService.RequestSave(path);
        }

        /// <summary>Party visual prefab is fixed for this compact game -- both restored party members use the
        /// same primitive PartyCapsule prefab regardless of which one they were (no per-character art choice
        /// exists to persist).</summary>
        public LoadResult Load(string path, WorldSpawnService oldWorldSpawnService, GameObject partyPrefab, IReadOnlyList<RuntimeEntityId> partyMemberIds)
        {
            var prefabs = new Dictionary<RuntimeEntityId, GameObject>();
            foreach (var id in partyMemberIds) prefabs[id] = partyPrefab;

            var coordinator = new SessionLoadCoordinator(saveFileStore);
            return coordinator.Load(path, provider, new DiceService(new SeededRandomSource(System.Environment.TickCount)), RequiredPolicies, host, oldWorldSpawnService, prefabs);
        }
    }
}

using System;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Gameplay.Scenes;
using CRPG.Saving;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CRPG.Gameplay
{
    /// <summary>
    /// Task 3.6: ILoadRequestPort implementation wrapping SessionLoadCoordinator.LoadCoroutine -- the seam a
    /// CRPG.UI SavePresenter depends on (via the port interface, living in CRPG.Saving) instead of on
    /// SessionLoadCoordinator/LoadResult directly, since CRPG.UI must never reference CRPG.Gameplay (which
    /// already references CRPG.UI). Constructed and held by the composition root, which supplies everything
    /// this needs to actually run a load: the coordinator, a Core-scene SceneTravelRunner to drive the
    /// coroutine, the fixed rules/dice/host/coreScene/party-visual-prefab context, and a callback for the FULL
    /// LoadResult (GameSession/WorldSpawnService included) -- the composition root uses that full result to run
    /// SessionConsumerRegistry.RebindAll and swap its own WorldSpawnService reference; the thinner
    /// LoadOutcomeReport this class hands back through RequestLoad is all a presenter ever needs to see.
    /// </summary>
    public sealed class SessionLoadRequestAdapter : ILoadRequestPort
    {
        private readonly SessionLoadCoordinator coordinator;
        private readonly SceneTravelRunner runner;
        private readonly IRulesDatabase rulesDatabase;
        private readonly IDiceService diceService;
        private readonly GameSessionHost host;
        private readonly Func<WorldSpawnService> currentWorldSpawnService;
        private readonly Func<Scene> coreScene;
        private readonly Func<System.Collections.Generic.IReadOnlyDictionary<RuntimeEntityId, GameObject>> partyMemberVisualPrefabs;
        private readonly Action<LoadResult> onFullResult;

        public SessionLoadRequestAdapter(
            SessionLoadCoordinator coordinator,
            SceneTravelRunner runner,
            IRulesDatabase rulesDatabase,
            IDiceService diceService,
            GameSessionHost host,
            Func<WorldSpawnService> currentWorldSpawnService,
            Func<Scene> coreScene,
            Func<System.Collections.Generic.IReadOnlyDictionary<RuntimeEntityId, GameObject>> partyMemberVisualPrefabs,
            Action<LoadResult> onFullResult)
        {
            this.coordinator = coordinator;
            this.runner = runner;
            this.rulesDatabase = rulesDatabase;
            this.diceService = diceService;
            this.host = host;
            this.currentWorldSpawnService = currentWorldSpawnService;
            this.coreScene = coreScene;
            this.partyMemberVisualPrefabs = partyMemberVisualPrefabs;
            this.onFullResult = onFullResult;
        }

        public bool IsLoading => coordinator.IsLoading;

        public void RequestLoad(string path, Action<LoadOutcomeReport> onFinished)
        {
            runner.Run(coordinator.LoadCoroutine(
                path, rulesDatabase, diceService, host, currentWorldSpawnService(), coreScene(), partyMemberVisualPrefabs(),
                result =>
                {
                    onFullResult?.Invoke(result);
                    onFinished(new LoadOutcomeReport(result.Outcome, result.Message));
                }));
        }
    }
}

using System;
using System.Collections;
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
    /// <para>
    /// Task 5.2: <see cref="ForRuleset"/> builds the ruleset-aware counterpart, re-supplying a fresh
    /// <see cref="IRulesetDefinition"/> on every load (a provider is authored content -- the composition root
    /// re-resolves/re-registers it rather than this class holding a live one across an arbitrary number of
    /// future loads).
    /// </para>
    /// </summary>
    public sealed class SessionLoadRequestAdapter : ILoadRequestPort
    {
        private readonly SessionLoadCoordinator coordinator;
        private readonly SceneTravelRunner runner;
        private readonly GameSessionHost host;
        private readonly Func<WorldSpawnService> currentWorldSpawnService;
        private readonly Func<Scene> coreScene;
        private readonly Func<System.Collections.Generic.IReadOnlyDictionary<RuntimeEntityId, GameObject>> partyMemberVisualPrefabs;
        private readonly Action<LoadResult> onFullResult;
        private readonly Func<string, Scene, System.Collections.Generic.IReadOnlyDictionary<RuntimeEntityId, GameObject>, Action<LoadResult>, IEnumerator> startLoad;

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
            : this(coordinator, runner, host, currentWorldSpawnService, coreScene, partyMemberVisualPrefabs, onFullResult,
                (path, scene, prefabs, onFinished) => coordinator.LoadCoroutine(path, rulesDatabase, diceService, host, currentWorldSpawnService(), scene, prefabs, onFinished))
        {
        }

        private SessionLoadRequestAdapter(
            SessionLoadCoordinator coordinator,
            SceneTravelRunner runner,
            GameSessionHost host,
            Func<WorldSpawnService> currentWorldSpawnService,
            Func<Scene> coreScene,
            Func<System.Collections.Generic.IReadOnlyDictionary<RuntimeEntityId, GameObject>> partyMemberVisualPrefabs,
            Action<LoadResult> onFullResult,
            Func<string, Scene, System.Collections.Generic.IReadOnlyDictionary<RuntimeEntityId, GameObject>, Action<LoadResult>, IEnumerator> startLoad)
        {
            this.coordinator = coordinator;
            this.runner = runner;
            this.host = host;
            this.currentWorldSpawnService = currentWorldSpawnService;
            this.coreScene = coreScene;
            this.partyMemberVisualPrefabs = partyMemberVisualPrefabs;
            this.onFullResult = onFullResult;
            this.startLoad = startLoad;
        }

        /// <summary>Task 5.2: the ruleset-aware counterpart to the main constructor -- <paramref
        /// name="providerFactory"/> is called fresh on every load (not resolved once and cached), so the
        /// composition root controls exactly how its ruleset content/provider asset is (re-)obtained.</summary>
        public static SessionLoadRequestAdapter ForRuleset(
            SessionLoadCoordinator coordinator,
            SceneTravelRunner runner,
            Func<IRulesetDefinition> providerFactory,
            IDiceService diceService,
            System.Collections.Generic.IReadOnlyList<Type> requiredPolicyTypes,
            GameSessionHost host,
            Func<WorldSpawnService> currentWorldSpawnService,
            Func<Scene> coreScene,
            Func<System.Collections.Generic.IReadOnlyDictionary<RuntimeEntityId, GameObject>> partyMemberVisualPrefabs,
            Action<LoadResult> onFullResult) =>
            new(coordinator, runner, host, currentWorldSpawnService, coreScene, partyMemberVisualPrefabs, onFullResult,
                (path, scene, prefabs, onFinished) => coordinator.LoadCoroutine(
                    path, providerFactory(), diceService, requiredPolicyTypes, host, currentWorldSpawnService(), scene, prefabs, onFinished));

        public bool IsLoading => coordinator.IsLoading;

        public void RequestLoad(string path, Action<LoadOutcomeReport> onFinished)
        {
            runner.Run(startLoad(path, coreScene(), partyMemberVisualPrefabs(), result =>
            {
                onFullResult?.Invoke(result);
                onFinished(new LoadOutcomeReport(result.Outcome, result.Message));
            }));
        }
    }
}

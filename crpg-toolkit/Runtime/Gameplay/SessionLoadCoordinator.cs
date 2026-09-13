using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Gameplay.Scenes;
using CRPG.Saving;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace CRPG.Gameplay
{
    /// <summary>Task 3.6: LoadOutcome itself moved to CRPG.Saving (pure enum, no Gameplay-type references) so a
    /// UI-facing LoadOutcomeReport/ILoadRequestPort can reference it without CRPG.UI needing to reference
    /// CRPG.Gameplay. LoadResult stays here -- it carries live GameSession/WorldSpawnService references that
    /// only the composition root, never a UI presenter, has any legitimate use for.</summary>
    public sealed class LoadResult
    {
        private LoadResult(LoadOutcome outcome, string message, GameSession session, WorldSpawnService worldSpawnService)
        {
            Outcome = outcome;
            Message = message;
            Session = session;
            WorldSpawnService = worldSpawnService;
        }

        public LoadOutcome Outcome { get; }
        public string Message { get; }

        /// <summary>Non-null only on Success. The caller MUST replace any WorldSpawnService reference it was
        /// holding for the previous session with this one (Task 3.5) -- the previous instance is discarded
        /// wholesale along with the session it belonged to.</summary>
        public GameSession Session { get; }
        public WorldSpawnService WorldSpawnService { get; }

        public static LoadResult Ok(GameSession session, WorldSpawnService worldSpawnService) => new(LoadOutcome.Success, null, session, worldSpawnService);
        public static LoadResult Fail(LoadOutcome outcome, string message) => new(outcome, message, null, null);
    }

    /// <summary>
    /// Task 3.5: the load transaction. Builds and validates the ENTIRE new world before touching the old one --
    /// everything through the build phase operates on brand-new, inactive objects; the old session/scene stays
    /// fully live and usable until the commit point.
    /// <para>
    /// Task 3.6: <see cref="Load"/> (unchanged signature/behavior, kept for every existing single-scene caller)
    /// only ever handles a same-scene load and still rejects a scene mismatch outright. <see cref="LoadCoroutine"/>
    /// is the new, general entry point that handles BOTH same-scene and cross-scene loads uniformly -- on a
    /// scene mismatch it loads the target location additively (mirroring SceneTravelCoordinator's own shape),
    /// then runs the SAME build phase (extracted into <see cref="BuildPhase"/>) before ever touching the old
    /// world. Both methods share a single WorldTransitionGate with SceneTravelCoordinator so a load and a
    /// travel can never race each other.
    /// </para>
    /// <para>
    /// Does NOT attempt to transparently rebind scene-authored consumers that captured a reference to the OLD
    /// session's objects at composition time (InteractionController, SafePointGate, TalkInteractionAdapter's
    /// Store/Inventory, a LootInteractionAdapter's container, ...). On Success this returns the new
    /// GameSession/WorldSpawnService; re-wiring any such consumer to them is the composition root's
    /// responsibility -- see ISessionConsumer/SessionConsumerRegistry, the single call site that does this.
    /// </para>
    /// </summary>
    public sealed class SessionLoadCoordinator
    {
        private readonly SaveFileStore saveFileStore;
        private readonly WorldTransitionGate gate;

        public SessionLoadCoordinator(SaveFileStore saveFileStore = null, WorldTransitionGate gate = null)
        {
            this.saveFileStore = saveFileStore ?? new SaveFileStore();
            this.gate = gate ?? new WorldTransitionGate();
        }

        private bool isLoading;
        public bool IsLoading => isLoading;

        /// <summary>Same-scene-only, synchronous entry point -- unchanged since Task 3.5. Kept for every
        /// existing caller/test that has no Core/location scene split at all; still hard-rejects a scene
        /// mismatch (SceneMismatch) rather than attempting anything cross-scene. Use LoadCoroutine for a
        /// composition root that DOES split Core from location scenes and wants cross-scene loads to actually
        /// work.</summary>
        /// <param name="partyMemberVisualPrefabs">Visual prefab to rebuild each restored party member's view
        /// from, keyed by character id. There is no generic PC-visual-persistence system in this toolkit
        /// (player-character visual choice isn't a persisted concept) -- only the composition root that
        /// originally chose each party member's prefab knows this mapping; building a generic replacement is
        /// out of scope for a save task.</param>
        public LoadResult Load(
            string path,
            IRulesDatabase rulesDatabase,
            IDiceService diceService,
            GameSessionHost host,
            WorldSpawnService oldWorldSpawnService,
            IReadOnlyDictionary<RuntimeEntityId, GameObject> partyMemberVisualPrefabs = null)
        {
            if (IsLoading) return LoadResult.Fail(LoadOutcome.LoadAlreadyInProgress, "A load is already in progress.");
            if (gate.IsBusy) return LoadResult.Fail(LoadOutcome.WorldTransitionBusy, $"World transition already in progress ({gate.BusyReason}).");

            isLoading = true;
            gate.BeginBusy("session-load");
            try
            {
                var fileResult = saveFileStore.Load(path);
                if (fileResult.Outcome != SaveLoadOutcome.Success)
                    return LoadResult.Fail(MapFileOutcome(fileResult.Outcome), fileResult.Message);

                var data = fileResult.Data;

                var currentScenePath = SceneManager.GetActiveScene().path;
                if (!string.IsNullOrEmpty(data.Header.ScenePath) && data.Header.ScenePath != currentScenePath)
                {
                    return LoadResult.Fail(LoadOutcome.SceneMismatch,
                        $"Save is for scene '{data.Header.ScenePath}'; the active scene is '{currentScenePath}'. Use LoadCoroutine for a cross-scene load.");
                }

                var build = BuildPhase(data, rulesDatabase, diceService, partyMemberVisualPrefabs, npcScene: null, partyScene: null);
                if (!build.Success) return LoadResult.Fail(build.Outcome, build.Message);

                Commit(host, oldWorldSpawnService, build.Session, build.WorldSpawnService, build.Staged);
                return LoadResult.Ok(build.Session, build.WorldSpawnService);
            }
            finally
            {
                isLoading = false;
                gate.EndBusy();
            }
        }

        /// <summary>Task 3.6: general-purpose entry point -- handles same-scene AND cross-scene loads
        /// uniformly. coreScene is where every restored PARTY MEMBER's view is placed, always (never wherever
        /// the load happens to be reading a scene path from) -- a party member is never a location's own
        /// content. Every restored NPC is placed into whichever scene actually ends up holding this save's
        /// location content (the currently active one for a same-scene load, or the freshly, additively loaded
        /// one for a cross-scene load).</summary>
        public IEnumerator LoadCoroutine(
            string path,
            IRulesDatabase rulesDatabase,
            IDiceService diceService,
            GameSessionHost host,
            WorldSpawnService oldWorldSpawnService,
            Scene coreScene,
            IReadOnlyDictionary<RuntimeEntityId, GameObject> partyMemberVisualPrefabs,
            Action<LoadResult> onFinished)
        {
            if (IsLoading)
            {
                onFinished(LoadResult.Fail(LoadOutcome.LoadAlreadyInProgress, "A load is already in progress."));
                yield break;
            }

            if (gate.IsBusy)
            {
                onFinished(LoadResult.Fail(LoadOutcome.WorldTransitionBusy, $"World transition already in progress ({gate.BusyReason})."));
                yield break;
            }

            isLoading = true;
            gate.BeginBusy("session-load");

            var fileResult = saveFileStore.Load(path);
            if (fileResult.Outcome != SaveLoadOutcome.Success)
            {
                EndLoading();
                onFinished(LoadResult.Fail(MapFileOutcome(fileResult.Outcome), fileResult.Message));
                yield break;
            }

            var data = fileResult.Data;
            var currentScenePath = SceneManager.GetActiveScene().path;
            var crossScene = !string.IsNullOrEmpty(data.Header.ScenePath) && data.Header.ScenePath != currentScenePath;

            Scene? npcScene = null;
            Scene oldLocationScene = default;

            if (crossScene)
            {
                AsyncOperation loadOp = null;
                try
                {
                    loadOp = SceneManager.LoadSceneAsync(data.Header.ScenePath, LoadSceneMode.Additive);
                }
                catch (Exception ex)
                {
                    EndLoading();
                    onFinished(LoadResult.Fail(LoadOutcome.SceneLoadFailed, ex.Message));
                    yield break;
                }

                if (loadOp == null)
                {
                    EndLoading();
                    onFinished(LoadResult.Fail(LoadOutcome.SceneLoadFailed, $"Scene '{data.Header.ScenePath}' could not be loaded (missing from Build Settings?)."));
                    yield break;
                }

                yield return loadOp;

                var loadedScene = SceneManager.GetSceneByPath(data.Header.ScenePath);
                if (!loadedScene.IsValid())
                {
                    EndLoading();
                    onFinished(LoadResult.Fail(LoadOutcome.SceneLoadFailed, $"Scene '{data.Header.ScenePath}' loaded but could not be resolved."));
                    yield break;
                }

                npcScene = loadedScene;
                oldLocationScene = SceneManager.GetSceneByPath(currentScenePath);
            }
            else
            {
                npcScene = SceneManager.GetSceneByPath(currentScenePath);
            }

            var build = BuildPhase(data, rulesDatabase, diceService, partyMemberVisualPrefabs, npcScene, partyScene: coreScene);
            if (!build.Success)
            {
                if (crossScene && npcScene.HasValue) SceneManager.UnloadSceneAsync(npcScene.Value);
                EndLoading();
                onFinished(LoadResult.Fail(build.Outcome, build.Message));
                yield break;
            }

            Commit(host, oldWorldSpawnService, build.Session, build.WorldSpawnService, build.Staged);

            if (crossScene)
            {
                SceneManager.SetActiveScene(npcScene.Value);
                SceneManager.UnloadSceneAsync(oldLocationScene);
            }

            EndLoading();
            onFinished(LoadResult.Ok(build.Session, build.WorldSpawnService));
        }

        private void EndLoading()
        {
            isLoading = false;
            gate.EndBusy();
        }

        private sealed class BuildResult
        {
            public bool Success;
            public LoadOutcome Outcome;
            public string Message;
            public GameSession Session;
            public WorldSpawnService WorldSpawnService;
            public List<GameObject> Staged;
        }

        /// <summary>The shared build phase both Load and LoadCoroutine funnel through: restore SaveData into a
        /// brand-new GameSession, stage every world spawn's view fully INACTIVE, validate content -- entirely
        /// off to the side, never touching the old session/scene. npcScene/partyScene (both optional) explicitly
        /// place each staged instance via SceneManager.MoveGameObjectToScene right after it's created, rather
        /// than trusting Unity's default "goes into the currently active scene" (which, during this phase, is
        /// still the OLD scene) -- null means "don't move it" (Load's own single-scene callers, matching their
        /// pre-Task-3.6 behavior exactly).</summary>
        private static BuildResult BuildPhase(
            SaveData data,
            IRulesDatabase rulesDatabase,
            IDiceService diceService,
            IReadOnlyDictionary<RuntimeEntityId, GameObject> partyMemberVisualPrefabs,
            Scene? npcScene,
            Scene? partyScene)
        {
            var validation = ContentValidator.Validate(data, rulesDatabase);
            if (!validation.IsValid)
                return new BuildResult { Success = false, Outcome = LoadOutcome.ContentValidationFailed, Message = string.Join("; ", validation.Errors) };

            var newSession = new GameSession(rulesDatabase, diceService);
            SaveService.Restore(data, newSession.Party, newSession.Characters, newSession.Inventory, (Timeline)newSession.Timeline, newSession.Quests);

            var newWorldSpawnService = new WorldSpawnService(newSession);
            var staged = new List<GameObject>();
            try
            {
                foreach (var entry in data.WorldSpawns)
                {
                    if (entry.Defeated)
                    {
                        newWorldSpawnService.ImportDefeatedTombstone(entry.SpawnId, new DefinitionId(entry.NpcDefinitionId));
                        continue;
                    }

                    var characterId = RuntimeEntityId.FromString(entry.CharacterId);
                    var position = new Vector3(entry.PosX, entry.PosY, entry.PosZ);
                    var rotation = Quaternion.Euler(0f, entry.FacingYaw, 0f);
                    var npcDefinitionId = new DefinitionId(entry.NpcDefinitionId);

                    GameObject prefab;
                    var faction = default(DefinitionId);
                    if (npcDefinitionId.IsValid)
                    {
                        rulesDatabase.TryResolve<NpcDefinition>(npcDefinitionId, out var npcDefinition);
                        prefab = npcDefinition?.VisualPrefab;
                        faction = npcDefinition?.Faction ?? default;
                    }
                    else if (partyMemberVisualPrefabs != null && partyMemberVisualPrefabs.TryGetValue(characterId, out var partyPrefab))
                    {
                        prefab = partyPrefab;
                    }
                    else
                    {
                        throw new InvalidOperationException($"No visual prefab supplied for restored party member '{characterId}'.");
                    }

                    var instance = newWorldSpawnService.BindRestoredView(entry.SpawnId, characterId, npcDefinitionId, prefab, position, rotation, faction, startInactive: true);

                    var destinationScene = npcDefinitionId.IsValid ? npcScene : (partyScene ?? npcScene);
                    if (destinationScene.HasValue) SceneManager.MoveGameObjectToScene(instance, destinationScene.Value);

                    staged.Add(instance);
                }
            }
            catch (Exception ex)
            {
                foreach (var instance in staged) UnityEngine.Object.Destroy(instance);
                newSession.Dispose();
                return new BuildResult { Success = false, Outcome = LoadOutcome.ViewBindingFailed, Message = ex.Message };
            }

            return new BuildResult { Success = true, Session = newSession, WorldSpawnService = newWorldSpawnService, Staged = staged };
        }

        /// <summary>Build phase fully succeeded -- only now does the old world get touched. Destroys EVERY
        /// agent the old WorldSpawnService owned (party included) regardless of which scene each lived in -- a
        /// load reconstructs the whole world fresh from disk, unlike a travel (SceneTravelCoordinator), which
        /// deliberately keeps the party/session alive and only ever releases one location's own NPCs.</summary>
        private static void Commit(GameSessionHost host, WorldSpawnService oldWorldSpawnService, GameSession newSession, WorldSpawnService newWorldSpawnService, List<GameObject> staged)
        {
            var previousSession = host.Session;
            if (oldWorldSpawnService != null)
            {
                var oldAgents = oldWorldSpawnService.OwnedAgents.ToList();
                foreach (var agent in oldAgents)
                {
                    agent.gameObject.SetActive(false);
                    agent.Unbind();
                }
                foreach (var agent in oldAgents) UnityEngine.Object.Destroy(agent.gameObject);
            }
            previousSession?.Dispose();
            host.AdoptSession(newSession);

            // Task 3.6: matches AdventureSliceBootstrap.PlaceInitialParty's own established fix for the same
            // underlying NavMeshAgent quirk -- an agent's INTERNAL nav-mesh position doesn't reliably resync
            // from transform.position/SetActive alone (each instance here already went through one Instantiate
            // -> synchronous OnEnable -> SetActive(false) cycle before this second, real activation). Without
            // an explicit Warp, a restored agent can settle on a stale internal position and visibly path back
            // to where its transform actually is, instead of simply appearing there.
            foreach (var instance in staged)
            {
                instance.SetActive(true);
                var navAgent = instance.GetComponentInChildren<NavMeshAgent>(true);
                navAgent?.Warp(instance.transform.position);
            }
        }

        private static LoadOutcome MapFileOutcome(SaveLoadOutcome outcome) => outcome switch
        {
            SaveLoadOutcome.FileNotFound => LoadOutcome.FileNotFound,
            SaveLoadOutcome.IoError => LoadOutcome.IoError,
            SaveLoadOutcome.Corrupt => LoadOutcome.Corrupt,
            SaveLoadOutcome.UnsupportedNewerVersion => LoadOutcome.UnsupportedNewerVersion,
            SaveLoadOutcome.NoMigrationPath => LoadOutcome.NoMigrationPath,
            _ => LoadOutcome.IoError,
        };
    }
}

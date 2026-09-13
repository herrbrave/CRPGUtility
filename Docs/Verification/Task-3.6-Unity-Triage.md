# Task 3.6 Unity verification triage

## Confirmed findings

- Live target: `C:\Users\Peter\wkspaces\CRPGUnity\TestProject`, Unity 6000.6.0f1, Pipeline port 7800.
- Pipeline `status` reported ready while a read-only `eval_file` failed with `Main thread operation timed out after 5000ms`. After the user foregrounded Unity and paused the other agent, the identical probe succeeded. This demonstrates that transport readiness alone is insufficient; it does not establish the precise cause of the initial stall.
- Live probe: not playing, not paused, not compiling/updating, runInBackground already true. Do not keep toggling runInBackground as a speculative fix.
- Both live EditorBuildSettings and runtime scene enumeration include SceneTravelTestA, SceneTravelTestB, AdventureSlice_Core, AdventureSlice_Overworld, AdventureSlice_Interior. No registration repair is currently indicated.
- Earlier project Editor.log entries contain genuine SceneTravelTestA registration errors. Recent entries instead reference `Assets/Scenes/Tests/DoesNotExist.unity`. That is intentional: SceneTravelTests.NonexistentScenePathFailsCleanly expects this error via LogAssert.Expect. Never add the nonexistent scene or suppress all error logs to fix it.
- Actual current Editor log is `TestProject/Logs/Editor.log`; the AppData Unity Editor.log contains only startup output here.
- AdventureSlice_Overworld successfully loaded and bootstrap logged Ready in the prior playthrough.
- A real startup issue remains: AdventureSliceBootstrap calls SpawnParty before loading startingLocationScenePath. Active prefab instantiation enables NavMeshAgents before any location NavMesh exists, producing `Failed to create agent because there is no valid NavMesh`. Stage agents inactive from instantiation or defer their activation until navigation is ready; retain explicit Core ownership. Deactivating after active Instantiate is too late to prevent OnEnable.

## Verification discipline for the implementing agent

1. Only one agent drives Unity. Do not run overlapping tests, evals, play/stop commands, or recompile requests.
2. Use a bounded read-only main-thread probe before testing. Probe is in `TestProject/AgentScripts/VerificationStateProbe.cs` (outside Assets, no compile/import).
3. If status is ready but that probe times out, stop extending waits. Check editor foreground/modal state with the user. Do not kill/restart Unity without permission or start a second editor against its open project.
4. Start PlayMode tests with `run_tests --mode playmode --filter SceneTravelTests --async_tests true`. The initial zero-count/running response is admission, NOT a pass.
5. Poll the returned status path / test-status, allowing domain reload and checking log progress. Use a bounded deadline; on no progress report a stalled run, not success. Do not start another run while one is pending.
6. Read expected negative-test logs alongside their named test result. Require a completed nonzero result count and zero failures.
7. Verify cross-scene tests separately, then the full suites. Editor tests are not standalone Gate B proof.
8. For standalone build, explicitly select Core first, then Overworld and Interior. The shared list currently starts with a test scene; blindly building the whole list will boot the wrong scene.

## Scope

No gameplay, test, scene, or build-setting files were changed by this triage. The diagnostic probe and this handoff document were added. After foreground recovery, a fresh focused SceneTravelTests PlayMode run completed: 6 total, 6 passed, 0 failed/skipped/inconclusive. This includes real A-to-B-and-back travel and NonexistentScenePathFailsCleanly. Results were read from TestProject/Temp/pipeline_test_status.json. Full suites, cross-scene load, and standalone acceptance were not rerun by this triage.

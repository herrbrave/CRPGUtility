# Production Foundation — Gate B Results

## Status: PASS — user acceptance approved; Phase 3 closed

The user completed [Gate-B-Manual-Test-Script.md](Gate-B-Manual-Test-Script.md), reported PASS for every check, and explicitly approved playability/integration burden. Task 3.6 and Gate B are accepted; Phase 3 is closed. Phase 4 may proceed with Task 4.1 planning; no Phase 4 implementation was started by this acceptance update. Manual results below are user-reported, distinct from agent-run automated evidence.

Sonnet's original report is preserved in [Gate-B-Results-Sonnet-Handoff.md](Gate-B-Results-Sonnet-Handoff.md).

## Corrections made during follow-up

- **UI pointer coordinates:** `UIToolkitPointerCapture` now converts bottom-left Input System screen coordinates to top-left pixels before `RuntimePanelUtils.ScreenToPanel` applies panel scaling. The prior diagnostic fed panel coordinates directly into the router, masking this mismatch. A new real-panel PlayMode test failed before the fix and passes afterward, covering asymmetric placement at scales 1 and 1.5. Evidence: `Evidence/PointerCapture-before.xml`, `Evidence/GateB-PlayMode.json`.
- **Loot destination wiring:** a real standalone-preparation Editor click emptied the chest but hit `NullReferenceException` because `LootInteractionAdapter.Inventory` was null. `LocationInitializer` now binds both container and destination inventory. `LootService` rejects a null destination at construction, before removing any item. The existing location persistence test now executes through the adapter without pre-wiring its inventory.
- **Post-load consumers:** the fixture recreates its UI ports/subscriptions after adopting a session, including `SaveRequestService` and its safe-point gate; it disposes the old interaction controller/host instead of leaving additional subscribed hosts. Inventory and quest panels expose refresh for interaction-end reconciliation. These changes compile and are in the rebuilt player; the user subsequently confirmed repeated live save/load acceptance.
- **Cold-boot roster identity:** the fixed four-character fixture now uses stable IDs for its prefab mapping instead of per-process random IDs. This is a fixture roster convention, not a general player-character appearance persistence system. Existing random-ID scratch saves are preserved but have not been migrated; use fresh saves for acceptance.
- **Facing test fixture:** the lower-priority fallback test was injecting Motion without an IMovable; production correctly clears that motor-owned tier, so the assertion depended on timing. It now uses the persistent lower-priority Interaction tier. No gameplay facing tolerance was loosened.

## Verified evidence

| Check | Result |
|---|---|
| EditMode suite | 568/568 passed in follow-up; `Evidence/GateB-EditMode.json`. This run preceded the final exposure of the two public UI Refresh methods. |
| Final PlayMode suite | 157/157 passed, zero failures/skips/inconclusive; `Evidence/GateB-PlayMode.json`. Includes the new pointer regression and corrected facing fallback test. |
| Compile | Final `recompile_status`: completed, failed=false, errors=[] before final PlayMode run. |
| Standalone build | `build_c577b560b5e6`, Succeeded, completed 2026-09-13T22:26:02Z; `Evidence/GateB-Build.json`. |
| Standalone boot | Native process launched successfully at 1600×900; four heroes, HUD, and `[AdventureSlice] Ready.`; `Evidence/Standalone-player.log`. |
| Loot | Native click, active hero walked to chest, Inventory showed Gold Piece x25. |
| Branching dialogue | Native click, walk to merchant, greeting → Continue → two visible choices. Browse branch exercised by the agent; the user subsequently confirmed the farewell branch as PASS. |
| Store | Browse opened store; native Buy changed gold 25→10 and added one Healing Potion. Leave closed store. |
| Quest and HUD | After leaving store, Inventory showed 10 gold/1 potion and quest appeared under Completed with greeting objective 1/1. `Evidence/Standalone-quest-completed.png`. Reward display is not a claim that reward turn-in was exercised. |

Build diagnostics are not hidden: the build report recorded 24 warnings and one Pipeline `/api/exec` main-thread timeout during building, despite final build result Succeeded. Warnings include obsolete API usages, a missing script on the unrelated Resources/BaseNPC prefab, and Pipeline disabled in player builds. The executable subsequently booted and completed the live interactions above; no claim of a warning-free build is made.

One intermediate PlayMode run hit the facing-fixture failure; a later attempt was aborted during Editor/test-runner recovery. Those are not counted as passes. The final completed run above is the acceptance evidence for the suite.

## Final manual acceptance — user reported

| Check | User result |
|---|---|
| Selection and group movement | PASS |
| Bounded camera | PASS |
| UI clicks do not move the party (including resized window) | PASS |
| Chest gives 25 gold exactly once | PASS |
| Farewell dialogue branch | PASS |
| Browse/store purchase and HUD refresh | PASS |
| Quest completion | PASS |
| Same-scene save/load, then save/load again | PASS |
| Travel both directions and chest stays empty | PASS |
| Interior save/exit/relaunch/load, then travel back | PASS |
| Overworld save/exit/relaunch/load | PASS |
| Playable/integration-burden review approved | PASS |

This closes the tabled click-through acceptance check, both standalone relaunch cycles, and the user review gate. No Gate B acceptance blockers remain. The build diagnostics and old scratch-save compatibility limitation above remain documented; acceptance does not imply they were removed.

## Scope calls retained

Inventory Use remains deliberately unavailable pending Phase 4 ability execution. Drop permanently discards inventory, not a new world loot object. Localization scope remains the agreed new-presenter seam rather than a broad retrofit. No main merge or commit was performed. No Oakhaven bootstrap edits were made.

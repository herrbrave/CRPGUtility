# Gate B — manual acceptance script

Use the rebuilt standalone player:
`TestProject/Builds/StandaloneWindows64/TestProject.exe`

Build verified: `build_c577b560b5e6`, Succeeded, 2026-09-13T22:26:02Z. See `Evidence/GateB-Build.json`.

## Preparation

1. Close any running TestProject player normally, then launch the executable above. Do not run a second Unity Editor.
2. Expect four heroes and the Party, Inventory, Quest Log, and Save/Load panels. The starting location is the green Overworld.
3. Use **Slot 2** for this run. Slot 1 was left untouched. Slot 4 contains agent-verification data and is not a clean baseline.
4. Create a new save for these tests. Older fixture saves used random party IDs and cannot reconstruct their visual-prefab mapping after a fresh boot. They have not been deleted or migrated. The rebuilt fixture uses a stable four-character roster.

Controls: left-click world targets; drag a box to select heroes; Shift-click toggles selection; WASD pans the camera; mouse wheel zooms. Camera panning/zooming can reveal targets hidden behind a HUD panel. Only the active hero walks to an interaction; ordinary movement orders apply to the selected group.

## 1. Selection, movement, camera, and UI click-through

- Click a hero in the world. Expect selection to match that hero in the Party panel.
- Drag a box around multiple heroes. Expect matching multiple selection. Shift-click a hero to toggle it.
- Click empty ground. Expect the selected heroes to move; unselected heroes stay put.
- Pan with WASD toward the edge of the level. Expect a bounded camera, not unlimited travel. Zoom in/out and confirm the view remains usable.
- Wait for movement to finish. Click **Save** in Slot 2. Expect its timestamp to update and **no new world movement**.
- Click panel backgrounds, edges, and controls, particularly the Save panel. Expect no world command. Click empty ground as a negative control: movement must still work.
- Repeat the UI suppression check after resizing the window. Note the specific panel and approximate click position if anything leaks through.

## 2. Loot, branching dialogue, store, and quest

Starting landmarks: merchant is the capsule west/left of the party at world (-10, 0, 0); chest is the cube east/right at (10, 0, -5); Interior travel is the cylinder northeast at (15, 0, 15). Zoom/pan as needed.

- Click the chest. Expect the active hero to walk to it and **Gold Piece x25** to appear in the Inventory panel.
- Click the chest again. Expect **no additional gold**.
- Click the merchant. Expect walk-to-interact followed by the greeting and **Continue**.
- Click Continue. Expect two choices: **I'd like to browse your wares.** and **Just saying hello. Farewell!**
- Choose the farewell branch, finish any remaining Continue prompt, and expect dialogue to close. The greeting quest should move to Completed with its objective at 1/1.
- Talk again, Continue, and choose the browse branch. Expect the store to open.
- Buy one Healing Potion for 15 gold. Expect store inventory to show **10 gold and 1 potion**. Leave the store; the persistent Inventory panel should now show those quantities too.
- Expect the quest to remain completed after repeat conversations. A displayed reward is not evidence that it was claimed; this script does not claim a reward-turn-in feature.

Already verified by agent using native mouse input in this exact standalone build: chest grants 25 gold; browse branch opens store; buying changes currency to 10 and adds one potion; leaving refreshes the HUD and shows the completed greeting quest. Farewell branch still needs your check.

## 3. Same-scene load and saving again after load

- While idle in Overworld, **Save Slot 2**. Remember the party position, gold/potion count, and completed quest.
- Move the party away. **Load Slot 2**.
- Expect instant restoration to the saved position, not walking back. Expect the saved inventory/quest state.
- Move to a different location again and **Save Slot 2 again**.
- Move away, then **Load Slot 2 again**. Expect the *second* position. This specifically checks that Save uses the replacement session rather than the disposed original.
- Expect exactly four heroes, one set of HUD panels, and working movement/interaction after both loads.

## 4. Travel in both directions and location persistence

- Click the Overworld travel cylinder. Expect the whole party to reach the brown Interior location, with all four heroes retained.
- Use the Interior return cylinder near the arrival point. Expect return to Overworld with working input/camera.
- Revisit the chest. It must remain empty; gold must not be granted again.
- Verify inventory and completed quest survived travel. Talk to the merchant again to check its session wiring still works.
- Enter Interior once more for the next test.

## 5. Required cross-scene save / exit / relaunch / load

- While **inside Interior**, idle, **Save Slot 2**. Remember gold/potion count and position.
- Close the player normally. Confirm the window has gone. Launch the same executable again.
- Expect a fresh Overworld boot. This proves the next load must change location.
- **Load Slot 2**. Expect **Interior**, the saved party position, four heroes, saved inventory and completed quest; no error status.
- Travel from Interior back to Overworld. Expect working input and the chest still empty. Reopen the merchant dialogue/store.
- Save again, move away, and load again. Expect the new post-load save to work.

## 6. Same-scene process relaunch

- Save Slot 2 while in Overworld, close the player, and relaunch.
- Load Slot 2. Expect saved Overworld position, inventory, quest, and empty chest.

## Report back

Copy this and mark PASS / FAIL / NOT RUN:

    Selection and group movement:
    Bounded camera:
    UI clicks do not move the party (including resized window):
    Chest gives 25 gold exactly once:
    Farewell dialogue branch:
    Browse/store purchase and HUD refresh:
    Quest completion:
    Same-scene save/load, then save/load again:
    Travel both directions and chest stays empty:
    Interior save/exit/relaunch/load, then travel back:
    Overworld save/exit/relaunch/load:
    Playable/integration-burden review approved:

For a failure, provide the step, what happened, and any visible error text. A screenshot is optional; no video is needed. Player log: `%USERPROFILE%\AppData\LocalLow\DefaultCompany\TestProject\Player.log` (the agent-launched instance instead writes `TestProject/AgentScripts/GateB-Player-1.log`).

Phase 3 remains at the user review gate until the remaining acceptance checks pass and you approve it. Do not treat a successful build or unit-test run as a substitute for these checks.

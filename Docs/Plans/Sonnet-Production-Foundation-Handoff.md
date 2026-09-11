# Sonnet 5 handoff: CRPG production foundation

You are implementing in `C:\Users\Peter\wkspaces\CRPGUnity` (WSL `/mnt/c/Users/Peter/wkspaces/CRPGUnity`). This is NOT Darklight Breach; do not import its gameplay conventions or paths.

Read first:
1. `Docs/Design/Production-Foundation.md`
2. `Docs/Plans/Production-Foundation-Implementation.md`
3. Relevant current source and local Claude/user guidance.

The user wants a toolkit where a game team supplies UI, art, audio, story and small custom extensions. The package supplies ordinary character/party/camera/input/interaction/inventory/dialogue/timeline/quest/save/rules integration. Your first priority is a robust character wrapper/lifecycle, not adding another demo-specific animation patch.

These documents are proposals, not proof of implemented APIs. Proposed file names may be refined to fit inspected code; preserve ownership and acceptance criteria. Confirm design defaults with the user before dependent implementation: Playables reference backend, motor-driven in-place locomotion, turn-based tactical scope, safe-point saves. Do not reopen harmless naming choices as architecture debates.

Start ONLY with Task 0.1. Establish source-control status and other agent work first. Identify exact real-asset fixtures and clarify whether the user's rig dissatisfaction includes skinning/import quality, locomotion/turning, animation blending, equipment, or all of these. The design covers the integration layer but does not claim to have visually diagnosed the current mesh.

Then propose one bounded implementation task at a time. Do not implement the whole plan in one run. Preserve existing passing behavior unless a task explicitly changes it. Preserve asset GUIDs and test managed-reference migration when moving classes/assemblies. Do not delete/recreate prefabs as an update.

No global service locator, framework rewrite, new animation package, full SRD content expansion, or third-party asset edits to force skeleton compatibility. No commits, resets, cleans, merges, publication or installs without the user's authorization. Do not launch another coding agent/model.

Historical evidence: earlier in the planning conversation, live Unity Pipeline returned 367/367 EditMode tests passing in Unity 6000.6.0f1. Rerun to establish your own current baseline. Those tests do not prove art quality, world save completeness or ruleset independence.

The implementation plan contains a verified Pipeline command for testing the already-open editor. Do not close that editor to run `unity test`. Check the inner result, not just CLI success. Do not claim visual/build validation without performing it.

Report task ID, changed paths, data migration effects, actual failing/passing test evidence, observed PlayMode/build behavior, and blockers. Stop at each user review gate. Gate A (CharacterLab) should be reviewed for actual character feel before expanding the adventure/tactical work.

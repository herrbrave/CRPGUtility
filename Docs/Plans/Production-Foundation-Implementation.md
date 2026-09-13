# CRPG Production Foundation Implementation Plan

> For Hermes: use subagent-driven-development if subsequently asked to implement. Current handoff owner is Sonnet 5; this document does not authorize launching another agent, modifying runtime, committing, or merging.

**Goal:** Make the existing toolkit sufficient for two real-asset test games without bespoke framework glue.

**Architecture:** Preserve subsystem services and state/definition separation. Add upper-layer Gameplay composition and Presentation integration; extract tabletop policy from generic runtime. Validate each boundary with a playable vertical slice before broadening it.

**Tech Stack:** Unity 6000.6.0f1, C#, UPM/local package, NavMesh, Playables, UI Toolkit sample views, Input System adapter, NUnit EditMode/PlayMode, Unity Pipeline.

Governing design: `Docs/Design/Production-Foundation.md`. Paths below are relative to repo root. NEW paths are proposed, not existing APIs. Existing paths were inspected or enumerated during planning. Plans specify contracts and tests, not invented drop-in implementations of unbuilt APIs.

## Execution discipline

- Work one task at a time, not the whole document in one prompt. Each task is a reviewable slice; split its steps into individual test/code/editor actions.
- First inspect current source and workspace changes. Another Claude session may be working. Do not overwrite concurrent edits, reset, clean, regenerate all assets, or commit without authorization.
- For each task: write the specified regression/contract test; run it and record the expected failure; implement the minimum; run targeted then full EditMode tests; perform the specified PlayMode/editor check; report changed files and actual output. For pure authoring tasks, validate the saved artifact and reopen/inspect it rather than inventing a failing unit test.
- Preserve public compatibility with short-lived forwarding adapters where practical. Preserve Unity `.meta` files/GUIDs. Moving scripts between assemblies requires a serialized-type migration audit, especially managed references in ability/dialogue assets. `[FormerlySerializedAs]` handles field names, not assembly-qualified managed-reference moves; use `MovedFrom`/explicit editor migration as appropriate and prove reload.
- Include asset migration and save-version fixtures in the same slice as schema changes. No deferred 'wire it later' completion claims.
- End each task with a review checkpoint. Commit-sized does not mean permission to commit.

## Verification commands and caveats

From WSL, connected editor discovery:

```bash
powershell.exe -NoProfile -Command "unity.exe --no-banner --no-pager status --format json"
powershell.exe -NoProfile -Command "unity.exe --no-banner --no-pager pipeline list --format json"
```

Known working live EditMode command in this workspace:

```bash
powershell.exe -NoProfile -Command "unity.exe --no-banner --no-pager --format json command --project-path 'C:\Users\Peter\wkspaces\CRPGUnity\TestProject' --timeout 360 run_tests -- editor"
```

Expected condition: inner `data.result.success=true`, zero failures, nonzero intended test count. Do not assume the old total of 367. CLI envelope success alone is insufficient. Capture full JSON and aggregate Results programmatically rather than flooding the handoff with every passing test. Discover filter syntax using command schema/help instead of guessing; positional `-- editor` was verified. `unity test` starts a separate Editor and refuses this project while it is open—use Pipeline rather than closing the user's editor.

Use `run_tests -- playmode` only after confirming the editor is not occupied by user/Claude testing and scene changes are safe. PlayMode tests and visual tests are distinct evidence. A standalone build/relaunch is required at test-game gates. Discover build options before invoking them.

Git status timed out from WSL during reconnaissance. Establish bounded status/diff access before writes; use Windows Git if available or report the blocker. Do not interpret a timeout as a clean tree.

## Phase 0 — Reproducible baseline and decisions

### Task 0.1: Record baseline and inventory

Create `Docs/Verification/Production-Baseline.md` and `Docs/Design/State-Persistence-Matrix.md`; modify `crpg-toolkit/README.md` only after verifying its replacement status.

1. Read current guidance, inspect changes, identify active Claude scope.
2. Run EditMode suite and inventory PlayMode tests (an asmdef is not test coverage).
3. Enumerate CharacterState, party/inventory, quests, timeline, stores and scene mutable state. Classify each field as persisted/derived/transient; link capture/restore code or mark missing.
4. Record real third-party character/animation assets and redistribution restrictions; choose laboratory fixtures with the user.
5. Confirm proposed defaults: Playables, in-place motor movement, turn-based scope, safe-point saves. Record deviations before coding dependent tasks.

Acceptance: evidence links, explicit missing-state list, selected fixtures and agreed design defaults. No runtime rewrite.

### Task 0.2: Lock regressions before migration

Modify `crpg-toolkit/Tests/Editor/SaveServiceTests.cs`, `AbilityResolutionServiceTests.cs`; create `Tests/Editor/ProductionContractTests.cs` under the package only for contracts that do not fit existing fixtures.

Add tests exposing missing character save fields and targeted alternate-cost/duplicate-cost behavior. Keep new tests on the bounded work branch/task until their fixes land; do not claim baseline regression failures are newly introduced defects. Record the old behavior that the next slices intentionally change.

## Phase 1 — Shared session and character lifetime

### Task 1.1: Close current subscription lifetimes

Modify `Runtime/Exploration/PartyExplorationController.cs`, `Runtime/Actors/CharacterPlayableAnimator.cs`, `Runtime/Inventory/CharacterVisualEquipment.cs`; tests in `Tests/Editor/PartyExplorationControllerTests.cs` and NEW `Tests/Runtime/CharacterBindingTests.cs`.

Write tests for disposal, repeat initialization, unbind/rebind and disable/enable. Implement disposal/unbinding and symmetric subscriptions without changing rig architecture yet. Test a destroyed/disposed controller cannot receive another move. Review before moving types.

### Task 1.2: Ship the composition boundary

NEW `crpg-toolkit/Runtime/Gameplay/CRPG.Gameplay.asmdef`, `GameSession.cs`, `GameSessionHost.cs`, `WorldSpawnService.cs`, `CharacterAgent.cs`; NEW `Tests/Editor/GameSessionTests.cs`. Modify relevant test asmdefs explicitly.

Define typed dependencies and creation/disposal order from the design. Wrap existing NPC state creation rather than putting GameObjects inside NpcSpawnService. Implement one spawn binding state, faction, inventory, motor and view. Roll back partial registration if required binding fails. Test two sessions do not share state and double dispose is safe.

### Task 1.3: Establish the art-independent wrapper

NEW `Runtime/Presentation/CRPG.Presentation.asmdef`, `CharacterView.cs`, `CharacterRigDefinition.cs`; modify `Gameplay/CharacterAgent.cs`. Add `Tests/Runtime/CharacterWrapperTests.cs`.

Separate gameplay root and model child. Bind an explicit Animator/motion provider rather than same-object searches. Migrate `TestProject/Assets/Resources/NPCs/BaseHumanMaleCharacter.prefab` and `BaseNPC.prefab` in place after inspecting references; retain `.meta` and preserve `GilroyMasters.asset` linkage. Change `TestProject/Assets/Editor/BaseHumanMaleCharacterAuthor.cs` to update rather than delete assets.

Acceptance: replace only Model with the second art source; movement/selection identity remains valid; no duplicated motor/Animator/physics writers. Save, reopen and verify references.

## Phase 2 — Character laboratory (Gate A)

### Task 2.1: Define movement outcomes and facing ownership

Modify `Runtime/Navigation/NavMeshCharacterMotor.cs`, existing movement interfaces and their callers; NEW `Runtime/Core/Spatial/MotionSnapshot.cs`, `Runtime/Gameplay/FacingController.cs`; NEW `Tests/Runtime/CharacterMotionTests.cs`.

Test off-mesh, partial path, cancellation, replaced orders and stop/arrival distinctions. Add request IDs and structured outcomes; introduce facing priorities and disable competing agent rotation. Preserve exploration services through an adapter if the motor API changes. Verify against a real baked path with an obstacle, not only a mocked straight line.

### Task 2.2: Separate animation state from graph mechanics

NEW `Runtime/Presentation/Animation/CharacterPresentationState.cs`, `ICharacterAnimationDriver.cs`, `PlayableCharacterAnimationDriver.cs`; modify/migrate `Actors/CharacterPlayableAnimator.cs` and `CharacterAnimationProfile.cs` with serialized compatibility; NEW `Tests/Editor/CharacterPresentationStateTests.cs`.

Implement continuously updated baseline locomotion and stance, exclusive full-body actions, terminal-state priority and request-token completion. Write tests for attack while moving, movement changing during an attack, cancelled token callback, repeated stance and death override. Keep old cue entrypoint as a compatibility adapter until all demo callers migrate; do not keep two graph owners.

### Task 2.3: Make playback interruption production-safe

Modify new driver/profile; NEW `Tests/Runtime/PlayableCharacterAnimationTests.cs`; modify `TestProject/Assets/Editor/CharacterAnimationAuthor.cs` and `Assets/Resources/Animations/CharacterAnimationProfile_HumanMale.asset`.

Implement speed blending, hysteresis, normalized interruption weights and bounded graph cleanup. Test attack→hit→death during crossfade, pause/resume and disable/destroy. Add optional masked upper-body action only after full-body tests pass. Run actual clips and inspect feet, turning, blending and recovery. Confirm loop settings and avatar validity after asset reload.

### Task 2.4: Replace bone-name guesswork

NEW `Runtime/Presentation/Equipment/EquipmentVisualProfile.cs`, `RigBindingMap.cs`, `EquipmentVisualBinder.cs`; modify `Runtime/Inventory/CharacterVisualEquipment.cs` into a compatibility facade or migrate it; NEW `Tests/Runtime/EquipmentVisualBindingTests.cs`.

Test missing/duplicate bone paths and incompatible family rejection before touching renderers. Implement rigid sockets first, then compatible skinned binding, initial inventory reconciliation and body-region reference counts. Test two garments hiding the same region, removal order, load-before-view-bind, and visual failure retaining the known-good appearance.

Migrate `TestProject/Assets/Resources/Items/ItemDefinition_PeasantBoots.asset`, `ItemDefinition_PeasantSleeves.asset`, `ItemDefinition_PeasantTrousers.asset`, `ItemDefinition_PeasantTunic.asset` and their existing visual assets via editor tooling. Inspect `PeasantOutfitEquipmentAuthor.cs` first; do not change third-party skeletons to force compatibility.

### Task 2.5: Package the setup workflow and laboratory

NEW `Editor/Character/CharacterSetupWindow.cs`, `Editor/Validation/CharacterRigValidator.cs`; NEW private `TestProject/Assets/Scenes/CharacterLab.unity` and fixture scripts under `Assets/CharacterLab/`.

Provide preview/validate/update with Undo and explicit source-import changes. Add GUID-preservation test for updating the wrapper twice. Verify Gate A from the design; record failed compatibility cases as well as successful fixtures. USER REVIEW GATE: approve rig and animation feel before broad UI/combat work.

## Phase 3 — Playable exploration and adventure

### Task 3.1: Package input and pointer capture

NEW `Integrations/InputSystem/CRPG.InputSystem.asmdef`, `InputSystemCommandSource.cs`, default `.inputactions`; NEW `Runtime/Gameplay/PlayerCommandRouter.cs`; NEW `Runtime/UI/Input/UIToolkitPointerCapture.cs`; tests `Tests/Runtime/PointerCommandTests.cs`.

Define input-context priority, click/drag capture, selection and cancellation. Extend existing `Core/Commands/` only where necessary. Test UI button/drag never sends ground movement, shift selection, drag box, destroyed selected actor and modal exit. Replace demo `MouseClickCommandSource` usage without deleting it before migrated scenes work.

### Task 3.2: Finish camera and party navigation integration

Modify `Runtime/Camera/CameraRigState.cs`, `IsometricCameraRig.cs`, `CRPGCameraSettings.cs`, `Runtime/Party/` formation policies and `Runtime/Exploration/PartyMovementService.cs`; tests `Tests/Runtime/PartyNavigationTests.cs`, existing camera tests.

Ship bound/smoothed follow and camera commands, selection/group behavior and follower blockage outcomes. Test narrow passage, unreachable destination, split groups, follow after teleport and camera movement over UI. Avoid replacing navigation with a crowd engine.

### Task 3.3: Shared interaction requests

NEW `Runtime/Gameplay/Interactions/InteractionController.cs`, `InteractionRequest.cs`, `WorldInteractable.cs`; adapt `Runtime/Interaction/` existing contracts after inspection; NEW `Tests/Runtime/InteractionLifecycleTests.cs`.

Build approach→revalidate→execute→release for talk/loot/use/travel. Test cancellation, disappearing target, blocked approach and new move order. Migrate `DialogueTriggerZone.cs`/`TeleportTriggerZone.cs` callers into reference adapters, retaining authored scene behavior intentionally.

### Task 3.4: Dialogue safety and quest semantics

Modify `Runtime/Dialogue/DialogueRunner.cs`, `DialogueContext.cs`, `Runtime/Quests/QuestService.cs`, `QuestObjectiveDefinition.cs`, `QuestState.cs`; NEW `Runtime/Gameplay/ConversationController.cs`, `Editor/Validation/DialogueValidator.cs`; extend existing dialogue/quest integration tests.

First add bounded automatic traversal and missing/duplicate-node diagnostics. Then add explicit objective progress mode and activation cursor with legacy historical-count default. Include quest/objective IDs and persisted reward claim. Keep check-policy extraction in Phase 4, not a second competing rules abstraction. Verify a branched conversation advances its quest once, cancellation returns controls and reload cannot duplicate reward.

### Task 3.5: Full safe-point saves and scene continuity

Modify `Runtime/Saving/SaveService.cs`, `SaveData.cs`, `CharacterSaveData.cs`, `SaveMigrationRunner.cs`; NEW `WorldSaveData.cs`, `SaveFileStore.cs` and `Runtime/Gameplay/SessionLoadCoordinator.cs`; extend `Tests/Editor/SaveServiceTests.cs`, NEW `Tests/Runtime/WorldSaveLoadTests.cs`.

Implement the persistence matrix, schema migration fixture and safe-point rejection first. Then world spawn IDs, store/container state, character position/facing and fresh-session load swap. Test corrupt/newer/missing-content saves leave old session intact; test disk write failure and backup recovery. Reconcile visuals after state restore, not by replaying gameplay events.

### Task 3.6: Functional UI contract and adventure fixture

**Status: Complete — Gate B accepted by the user.** All manual acceptance checks, including both standalone relaunch cycles and playability/integration burden, passed. Phase 3 is closed. Evidence: `Docs/Verification/Gate-B-Results.md`. Next task: 4.1; implementation has not been started by this status update.

Extend `Runtime/UI/` presenters; NEW `Runtime/UI/Party/PartyPresenter.cs`, `Inventory/InventoryPresenter.cs`, `Saving/SavePresenter.cs`, `Quests/QuestLogView.cs` (sample view may instead live under Samples~). Add localization-ready reason/text keys and typed audio/visual output hooks without requiring third-party packages.

Use default views to author a small adventure fixture under `TestProject/Assets/AdventureSlice/`; scene under `Assets/Scenes/AdventureSlice.unity`. Complete Gate B including standalone save/exit/relaunch/load. Record consumer-specific code and move only genuinely reusable orchestration into Gameplay. USER REVIEW GATE: playable adventure and integration burden.

## Phase 4 — Ruleset seam and real tactical loop

### Task 4.1: Extract policy without losing serialized content

**Status: Code complete, full suite green (586 EditMode + 157 PlayMode).** No USER REVIEW GATE is implied by this task's own text (pure code/architecture, no playable-content claim); not yet reviewed by the user. Plan and scope calls: `C:\Users\Peter\.claude\plans\purring-orbiting-hippo.md`. Summary: `IRulesetDefinition`/`IRulesetRegistrationBuilder` (moved to `CRPG.Actors`, not `Rules/Core`, to reach `ICharacterRegistry` without a cycle) plus `PolicyRegistry`/`RulesetValidationException` (`Rules/Core`) give `GameSessionHost` a new, purely-additive `StartSession(IRulesetDefinition, ...)` overload; every existing composition root's `StartSession(IRulesDatabase, IDiceService)` call site is untouched. `AbilityModifier`/`ProficiencyBonus`/`DefaultSkillCheckRule`/`SpellPreparationService`/3 SRD5 ability-score generation methods/6 `Combat.Default*Rule`s/`DeathSaveService`/`ConcentrationService` moved into `Rules/SRD5e2014/Runtime/` (namespace-only, same class names -- confirmed none are `[SerializeReference]`-embedded or Unity-Object-derived, so no `[MovedFrom]` needed). `DialogueRunner`/`DialogueCheckResolver`'s inline `new DefaultSkillCheckRule(...)` replaced with a `DialogueContext.SkillCheckRule`/`ISkillCheckRule.CheckDie` DI seam (fails loudly on null, no fallback construction) -- `TalkInteractionAdapter`/`DialogueTriggerZone` gained a matching settable property, wired in `AdventureSliceBootstrap`/`InteractionLabHarness`/`OakhavenDemoBootstrap`. New `SRD5RulesetProvider` (ScriptableObject) + Editor menu authoring script register all 168 top-level SRD5 content assets (Content/Oakhaven/ deliberately excluded -- fixture-specific, and duplicates a top-level id). New tests: `RulesetAssemblyDependencyTests` (no generic assembly references `CRPG.Rules.SRD5`; `Assembly-CSharp`/`-Editor` whitelisted as TestProject's own composition roots), `SRD5ContentReloadTests` (every real content asset still loads/registers post-move), `PolicyRegistryTests`, plus `GameSessionHostTests`/`DialogueRunnerTests`/`DialogueCheckResolverTests` additions. Deliberately NOT done (scope calls, see plan file): `AICombatDemoBootstrap` not retrofitted to the new provider seam (left for Task 4.5, which already owns that file); ability ritual/upcast paths audited but not extracted (no separate policy class exists to move, zero production callers); `RestService` not moved (already data-driven; missing hit-dice recovery is a content gap, not hardcoded logic).

NEW `Rules/Core/RulesetDefinition.cs`, provider/registration contracts at acyclic dependency layers; move concrete SRD implementations into `Rules/SRD5e2014/Runtime/` with new names only when compatibility requires them.

Audit/migrate `Actors/AbilityModifier.cs`, `ProficiencyBonus.cs`, `DefaultSkillCheckRule.cs`, `SpellPreparationService.cs`, generation methods, `Combat/Default*Rule.cs`, death/concentration/rest policy, `DialogueRunner.PerformCheck`, and ability ritual/upcast paths. Search all callers before moving. Generic shell/infrastructure may stay; SRD formulas and decisions may not remain hardcoded behind misleading generic class names.

Add dependency test and serialized-asset reload fixtures. Test removing the SRD provider leaves generic runtime compilable; required unregistered policies block session start with useful diagnostics.

### Task 4.2: Unified validation and transactional costs

NEW `Runtime/Abilities/AbilityValidationResult.cs`, `AbilityCostPlan.cs`; modify `AbilityResolutionService.cs`, AI eligibility code and UI preview callers; extend `AbilityResolutionServiceTests.cs`.

Test duplicate/invalid targets, LOS/range/relations, unavailable grants, grouped duplicate costs and alternate costs retaining action charges. Validate again at commit; spend exactly once. Reject missing required effect services before charging. No mutation on ordinary rejection; unexpected implementation errors produce a faulted action and diagnostics, not false success.

### Task 4.3: Time-based action and movement orchestration

NEW `Runtime/Gameplay/Actions/ActionExecution.cs`, `ActionScheduler.cs`; modify `Combat/MovementResolutionService.cs`, `ReactionService.cs` and encounter integration; NEW `Tests/Runtime/TacticalActionTests.cs`.

Implement phase/token/cancellation semantics from design and connect presentation without letting clip completion own gameplay. Resolve real navigation path segments and accumulated movement costs. Stop at reaction points, bounded resolve, revalidate and continue. Test obstacle path versus straight-line range, death during reaction, target disappearing before commit, double callback, pause and animation missing. Restrict unsupported reaction/effect capabilities through validation.

### Task 4.4: Effect ownership and duration cleanup

Modify condition instances/effect pipeline and SRD concentration policy; NEW effect-instance ownership data at the existing actor/ability boundary; extend concentration/damage/condition tests and save matrix.

Test two sources applying same condition, concentration replacement, expiry at configured turn boundary, source death and dispel. Remove only the owned instance. Update save schema/fixtures if durable outside-combat effects gain new fields. Do not advertise blanket SRD compliance.

### Task 4.5: Tactical fixture and coverage ledger

NEW `Docs/Rules/SRD5-Supported-Subset.md`; modify `TestProject/Assets/AIDemo/Scripts/AICombatDemoBootstrap.cs` toward thin content setup or create `Assets/TacticalSlice/` fixture; extend presenters under `Runtime/UI/Combat/` and `Abilities/`.

Complete Gate C with real actor movement/animation and player/AI using the same authority. List supported rules/content by capability; remove or label unsupported assets from the showcased catalog. Standalone performance capture includes hardware/settings and observed frame/GC behavior; agree budgets with user before pass/fail claims.

## Phase 5 — Prove reuse, then package

### Task 5.1: Alternate ruleset contract fixture

NEW `crpg-toolkit/Tests/Fixtures/AlternateRuleset/` with explicit asmdef; tests `Tests/Editor/RulesetContractTests.cs`. Implement a small non-d20 check/action economy and run the same gameplay contract tests. No runtime special cases keyed on ruleset name. Any new required seam is a reviewed correction, not a hidden exception.

### Task 5.2: Clean consumer test game

Create a new consuming Unity project only after user approves its location and art fixture. Import toolkit without TestProject scripts. Supply distinct UI/art and alternate ruleset, and build a compact exploration/encounter/story/save loop. Complete Gate D. Record setup steps and every custom script's purpose in `Docs/Verification/Consumer-Game.md`.

### Task 5.3: Distribution preparation (not required to start private test games)

Populate `Samples~/BasicCRPG`, `CharacterCreator`, `TacticalEncounter` with redistributable assets; update `package.json`, README and docs. Validate sample path/dependency correctness in a clean install. Resolve `Licenses/TODO.md` with user/legal input and include notices inside packaged artifacts; never invent ownership/license text. Produce a supported-version/API/migration policy and validator entrypoint. No publish/upload without authorization.

## Sonnet task report template

Task ID / agreed scope:
Changed files and asset GUID/migration effects:
Tests added and expected failing result observed:
Actual test command + inner summary:
PlayMode/build/visual checks actually performed:
Unverified items and blockers:
Remaining consumer glue or unsupported capabilities:
Next proposed task (do not automatically run):

A phase is done only when its named gate is verified. A test passing because unsupported behavior is silently skipped is not capability support.

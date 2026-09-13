# Phase 2 review before Phase 3

## Recommendation

Hold the Phase 3 implementation gate for a bounded Phase 2 correction pass. The separation into Gameplay, Presentation, a presentation state machine, and a graph driver is useful, but current integration defects prevent treating Gate A as complete. No runtime, editor, asset, or test fixes were applied during this review.

Scope: the production foundation plan through Task 2.5, current uncommitted implementation, relevant earlier lifecycle/navigation code, saved laboratory, and Phase 3 prerequisites. This is not a review of all existing combat/dialogue/save implementation.

## Verification performed

- Live Unity Editor discovered at the CRPGUnity TestProject, version 6000.6.0f1, ready.
- Ran the complete live EditMode suite using the plan's Pipeline command. Enumerated all 442 results: 436 passed, 6 failed, 0 skipped/inconclusive.
- All failures are the documented save round-trip contracts: Level, KnownAbilities, PreparedAbilities, SpellcastingStatId, DeathSaves, ConcentratingOnAbilityId. They are not presented here as new Phase 2 regressions.
- Important: the inner command returned `success=true` despite those six failed tests. The enumerated test statuses and Summary are the authoritative test outcome, not that boolean alone.
- Ran a temporary Editor probe against the actual animation component/driver and authored HumanMale animation profile. It reproduced action completion/cancellation failing to restore idle, an active action surviving graph rebind without a completion route, and callback reentrancy creating an active action in terminal state. Objects and graphs were torn down in the probe.
- Opened the saved CharacterLab in a separate Unity preview scene, inspected it, and closed that preview scene without saving or replacing the user's active scene. The saved anchor has one character child, but the harness's `current` is null; no equipment test item is assigned.
- Did not enter PlayMode, run the PlayMode suite, perform human visual sign-off, or build a player. The earlier 78/78 PlayMode figure remains the previous report's evidence, not a fresh result from this review.
- `git diff --check` reported trailing whitespace on three changed generated `.slnx` entries; not a runtime blocker and not edited.

Paths below are repository-relative; line references describe the reviewed source.

## Findings

### R1 — High: Update Wrapper destroys consumer-authored prefab content and has no undo

References: `crpg-toolkit/Editor/Character/CharacterWrapperBuilder.cs:35-59`; `crpg-toolkit/Editor/Character/CharacterSetupWindow.cs:20-23,133-135`.

Every update builds an unrelated root and hierarchy, then saves over the existing prefab. Existing custom components, children, interaction/selection anchors, overrides, and settings outside the spec are omitted and therefore lost. Preserving the asset GUID does not preserve all component/subobject references or the authored contents. The confirmation dialog is not Undo; the source explicitly substitutes confirmation for the plan's undo requirement. This becomes especially dangerous when Phase 3 adds selection and interaction components to these wrappers.

Correction: load/edit the existing wrapper contents and change only tool-owned fields/model content; preserve consumer-owned objects and provide a real reversible edit/restore mechanism. Do not advertise preservation of every existing reference based on a GUID-only test.

Regression: create a wrapper, add a custom child/component and an external reference to a preserved subobject, update twice with changed settings/model, reload, verify identity/content/reference preservation, and exercise the supported undo/restore operation.

### R2 — High: the saved laboratory cannot resume its staged character

References: `TestProject/Assets/CharacterLab/CharacterLabHarness.cs:25-33,40-64,82-84`; `crpg-toolkit/Editor/Character/CharacterSetupWindow.cs:173-178`.

Preview saves the instantiated character into the scene, but `current`, `agent`, animator, motor and equipment references are nonserialized private fields. Start only binds when `current` is already populated; it never discovers the saved character under the serialized anchor. After scene reload/domain reload, Update returns immediately because `current` is null. The live saved-scene probe confirmed `anchor children=1` and `harness.current=null`.

The equipment exercise also lacks composition: only the item definition is registered, not its visual profile/dependencies (`CharacterLabHarness.cs:71-79`); the saved scene has no equipment item assigned. The harness never disposes its session, and replacement can allocate another session without disposing the old one. Its Turn request is never cleared, so it can continue overriding motion facing after the turn exercise.

Correction: reconstruct the runtime binding from serialized fixture/anchor data on startup, register the fixture's required definitions and character state, assign real equipment content, and make teardown/replacement/facing release explicit.

Regression: save, unload/reload, enter Play, operate movement/action/equipment controls, replace the fixture, and unload/reload again. Tests should exercise the laboratory itself, not only instantiate the female prefab independently.

### R3 — High: completed or cancelled actions do not restore an unchanged baseline

References: `crpg-toolkit/Runtime/Presentation/Animation/CharacterPlayableAnimator.cs:171-182,204-217,222-232`.

`hasPlayedBaseline` and `lastPlayedBaselineCueId` still describe the baseline played before the action. After completion/cancellation, `RefreshBaseline(false)` sees the same desired cue and returns without replacing the action playable. An idle character can remain on its attack clip/final pose until movement or stance changes the desired baseline. Recovery after an authored terminal clip has the same cache-invalidation issue.

Live reproduction using the actual profile:

- Initial clip: `Armature|Idle_Loop`.
- Action clip: `Armature|Sword_Attack`.
- After completion: action state inactive, graph still `Armature|Sword_Attack`.
- After cancellation: action state inactive, graph still `Armature|Sword_Attack`.

Correction: synchronize driver channel ownership with state transitions and invalidate/reapply baseline when leaving action/terminal presentation, rather than treating a previously played baseline cue as evidence that it still owns the graph.

Regression: assert the actual active clip after idle→action→complete, idle→action→cancel, and idle→terminal→recover, without changing movement/stance.

### R4 — High: graph rebind strands active actions and loses terminal presentation

References: `crpg-toolkit/Runtime/Presentation/Animation/CharacterPlayableAnimator.cs:114-126,171-174,204-212`; `crpg-toolkit/Runtime/Presentation/Animation/PlayableCharacterAnimationDriver.cs:79-105`.

Binding a new Animator destroys the old graph and clears its pending completion callback. The presentation state retains the action token, but forced baseline refresh refuses to play while that token is active. Nothing recreates the action on the new graph or cancels/completes it. The live probe confirmed that after rebind and a long driver tick, `IsActionActive` remains true. Terminal rebind also creates an empty graph: the state retains only a terminal boolean, not the terminal cue needed to reconstruct the pose. Requests made before a driver is bound have a related no-completion path.

Correction: define a deliberate action/terminal rebind policy: preserve cue/phase and reconstruct playback, or cancel exactly once and reconcile the new graph. Do not silently retain an unfinishable request.

Regression: swap models during an action and while terminal; request an action before presentation bind; verify exactly-once outcome and correct new-model pose.

### R5 — High: the normal equipment replacement path defeats known-good visual retention

References: `crpg-toolkit/Runtime/Inventory/EquipmentService.cs:41-52`; `crpg-toolkit/Runtime/Presentation/Equipment/EquipmentVisualBinder.cs:159-162,195-202`.

The binder retains the old appearance only if it receives a replacement ItemEquipped event while the previous visual still exists. The real EquipmentService first publishes ItemUnequipped for the displaced item, and the binder immediately removes the old visual. If the subsequent replacement visual fails validation, there is no old appearance left to retain. Direct event tests do not establish the promised behavior through the gameplay service.

Correction: reconcile final slot state across a replacement transaction, preserving the previous visual until the replacement has successfully bound. Keep gameplay equipment authority in EquipmentService.

Regression: use EquipmentService to equip valid gear, then replace it in the same slot with gear whose socket/bone mapping is invalid. Assert gameplay state follows the chosen equipment policy while the previous known-good appearance remains.

### R6 — High: skinned binding can still silently accept half-bound or ambiguous skeletons

References: `crpg-toolkit/Runtime/Presentation/Equipment/EquipmentVisualBinder.cs:306-336,356-366,386-404`; `crpg-toolkit/Runtime/Presentation/Equipment/RigBindingMap.cs:53-80`.

Validation checks the entries supplied by the map, not whether those entries cover every renderer bone/root. An empty or incomplete map can pass structural validation; unmapped renderer bones are simply left pointing to the garment skeleton, reproducing the old half-bound failure mode. Duplicate paths in an actual hierarchy overwrite dictionary entries; duplicate socket names use the first match. These are different from duplicate entries in the mapping asset, which existing tests do reject.

`RigBindingMap.RigFamilyId` is not checked by the binder, and missing character/profile family declarations bypass the family check. Matching paths alone do not prove compatible skin bind poses. The real fixtures currently have no declared CharacterRigDefinition, so they do not establish family enforcement on the showcased content.

Correction: validate the complete renderer remap, unambiguous source/target hierarchies, required root mapping, and consistent declared compatibility before accepting any candidate. Author and validate the supported fixture family; reject unsupported compatibility rather than silently certifying it.

Regression: incomplete/empty maps, a renderer bone omitted from the map, duplicate sibling paths/socket names, and mismatched map/profile/character families; all must reject without replacing known-good visuals.

### R7 — Medium: interruption deliberately violates the normalized-weight contract

References: `crpg-toolkit/Runtime/Presentation/Animation/PlayableCharacterAnimationDriver.cs:15-23,189-223,229-240`.

The implementation discards the less-weighted input, starts the new clip at zero, and retains only the surviving input's old weight. Total weight drops below one until the fade finishes. The comments explicitly acknowledge this rather than implementing the plan's normalized interruption requirement. At an equal-weight interruption, losing one contribution also discards part of the current pose; eventual normalization does not establish pose continuity.

Correction: preserve a normalized outgoing blend/pose contribution during interruption with bounded cleanup. Validate both actual graph weights and rendered continuity; do not replace the contract with a weaker test that only checks eventual recovery.

### R8 — Medium: mandatory-animation validation accepts cue IDs with no playable clips

References: `crpg-toolkit/Editor/Validation/CharacterRigValidator.cs:113-133`; `crpg-toolkit/Runtime/Actors/CharacterAnimationProfile.cs:64-74`.

TryGetCue only proves a matching ID exists. The validator does not check a non-null/nonzero-length clip, looping suitability, duplicate cue IDs, or consistency of the referenced stance cues. A profile with idle/moving entries whose Clip fields are null can satisfy the mandatory-cue checks and build a nonanimating wrapper. The window additionally allows Build Anyway after errors, and Preview does not validate at all.

Correction: validate actual capability data and prevent normal ready-to-use/preview claims for invalid mandatory capabilities. If incomplete drafts are intentionally supported, distinguish them explicitly from validated wrappers.

Regression: null/zero-length required clips, duplicate IDs, missing stance targets, and unsuitable loop settings with actionable diagnostics.

### R9 — Medium: synchronous outcome callbacks can violate terminal/action invariants

References: `crpg-toolkit/Runtime/Presentation/Animation/CharacterPresentationState.cs:110-119,140-156`.

EnterTerminal cancels the current action before setting `isTerminal`. A cancellation callback can synchronously request a new action during that gap; EnterTerminal then leaves both terminal and action-active true. The live probe reproduced exactly that state. RequestAction likewise invokes an old cancellation callback before assigning its new token, allowing reentrant requests to be overwritten without their own outcome.

Correction: make state transitions atomic with respect to external callbacks, using explicit callback ordering/reentrancy policy. Notify only after the new authoritative state is established.

Regression: callbacks that request another action, enter terminal, or reset state during replacement/cancellation. Every accepted token must receive exactly one result and terminal state must not retain a newly admitted action.

## Additional integration concerns

- Model replacement schedules `Destroy(oldModel)` and immediately rebuilds equipment lookup over the entire CharacterRoot (`CharacterView.cs:74-89`, `EquipmentVisualBinder.cs:112-118`). Until deferred destruction runs, old and new `Model` hierarchies coexist; name/path ambiguity and preexisting equipped visual hierarchies can contaminate the map. Restrict lookup to the explicitly bound new model and detach old content before rebuilding. Include equipped-model swaps in R4/R6 regressions.
- Animation still reads a concrete NavMeshAgent rather than the delivered motion snapshot (`CharacterPlayableAnimator.cs:114,161-165`; `CharacterView.cs:32,42`). This leaves the intended motion/presentation seam unfinished. The driver adjusts one clip's playback speed; there is no authored walk/run clip blend yet. Record these as explicit scope deviations, not completed capability.
- The new preview window avoids a compile-time TestProject type reference, but still requires a hardcoded TestProject scene path and menu command (`CharacterSetupWindow.cs:150-169`). A clean package consumer cannot use Preview without recreating that private fixture. Separate private Gate A staging from the reusable setup-tool preview contract.
- CharacterAgent.Unbind only clears its own references; GameSession.Dispose only sets a flag. WorldSpawnService does not track/despawn its returned instances. Before Phase 3 scene/input integration depends on unified ownership, define the owner that tears down movement registration, subscriptions, views and session together; do not infer full teardown from the method names.

## Gate A and Phase 3 decision

The existing Gate-A-Results document correctly says the gate is not fully satisfied. Its statement that an unauthored death cue freezes the last baseline pose is not implemented by RequestTerminal: it marks state terminal but does not stop/freeze the existing playable when the cue is absent. Verify the actual fallback rather than relying on the harness's log text.

Still required for Gate A:

1. Correct the laboratory startup and the high-severity integration findings above; add integration regressions through real service/component paths.
2. Exercise real rigid equipment, an authored death/recovery visual, scene unload/reload, and user inspection of locomotion/facing/interruption.
3. Confirm the two-model fixture satisfies the intended art-source diversity. Both are male/female models from the same asset pack; the current second-fixture test checks Humanoid and bound graph, not visible retargeted pose quality.
4. Record user approval of rig/animation feel before broad UI/combat work.

The plan asks for one compatible modular outfit family, not explicitly the outfit on both bodies. Do not automatically expand the gate to a second complete outfit; agree that extra coverage if it is needed to prove compatibility. Generic-creature support remains an explicitly separate fixture and need not block a Humanoid-only gate.

Suggested correction batches: (1) safe wrapper updates and reproducible laboratory; (2) animation state/graph synchronization and interruption; (3) equipment transaction and compatibility validation; then fresh EditMode/PlayMode plus saved-scene and human Gate A checks. Resume Task 3.1 after this review gate rather than absorbing these defects into input/camera work. The six existing save contracts belong in the planned persistence work and are not a reason to pull all of Task 3.5 into this correction pass.

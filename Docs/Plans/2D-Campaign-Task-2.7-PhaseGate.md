# Task 2.7 — Phase 2 gate scene (USER REVIEW GATE B)

Status: **DRAFT v4** (Opus reviews 1 and 2 incorporated; user chose the three-way split 2.7a / 2.7b / 2.7c, each built, tested and reported on its own, with only 2.7c handing off to USER GATE B; awaiting user approval). Parent: `2D-Campaign-Phase2-SpritePresentation.md` (Task 2.7).

## Goal
One integrated, playable scene in `BarrowsEnd/` that lets the user *see and feel* everything Phase 2 built, on real Kenney tiles, so Phase 2 can
close on their sign-off, plus an automated backstop so the scene cannot silently rot. No new engine features; what the toolkit lacks is
scene-local glue (in `BarrowsEnd/`) or a recorded finding. **The review found this is three pieces of work, not one, so it is split: 2.7a content,
2.7b bootstrap + scene, 2.7c automated backstop + gate handoff.**

## Gate checklist (what the user judges)
1. Rocking feel (walk, coast, turn, diagonals, level when idle). 2. Sorting vs characters **and props** (|Z| <= 256). 3. Pixel crispness at integer zoom (3x/4x/5x keys).
4. Click-target feel with the 0.7 box. 5. Live equipment swaps (hair hides under helmet). 6. Combat cues: attack, hit flash, down, recover, die + corpse removal.
7. Save/load round trip (appearance, gear, position, downed state).

## 2.7a — Content (BarrowsEnd), idempotent Editor author like `BarrowsEndCatalogAuthor`
Verified gap: BarrowsEnd has no `ItemDefinition`s, no Sprite `EquipmentVisualProfile`s, and SRD5 has **no head slot** (only Armor, ClothingArms/Body/Feet/Legs, MainHand, OffHand).
- A `slot-head` `EquipmentSlotDefinition`; >= 4 items (helmet, sword, shield, tunic) with matching Sprite-kind `EquipmentVisualProfile`s (`layerSlot`, `hiddenLayerSlots` = hair for the helmet), sprites from the copied Kenney sheets.
- A `BarrowsEndRulesetProvider` (`IRulesetDefinition`): SRD5 content + the 7 sprite catalogs + the new slot/items/profiles. `SpriteAppearanceBinder` reads catalogs from the session's `RulesDatabase` (`SpriteCatalogSource`), so without this nothing appears. No manifest change (SRD5 ships in the package); asmdef references are added: `CRPG.Rules.SRD5`, `CRPG.Saving`, `CRPG.Abilities`, `CRPG.UI`, `CRPG.Camera` (as needed).
- **Damage:** `DamageEffect`'s fields are private serialized, so author `AbilityDefinition` assets ("Debug Hit 3", "Debug Hit 30") with real `DamageEffect`s in the authoring script (not runtime reflection).
- EditMode content tests (extending `SpriteCatalogContentTests` style): items reference existing profiles/slots, profile sprites are non-null, helmet hides hair.

## 2.7b — Gate bootstrap + scene
Scene `BarrowsEnd/Assets/Scenes/Gate/PhaseTwoGate.unity` authored by an Editor script (never hand-edited), added to `EditorBuildSettings`. Runtime glue lives in its **own runtime asmdef** (`BarrowsEnd.Gate`), not Assembly-CSharp, so tests can reach it.
- **Map:** ~24x16 individual `SpriteRenderer` tiles (Ground layer) + a **real 3D ground collider plane** (the click router raycasts physics; sprites have no collider) + `Physics.SyncTransforms()` after authoring (auto-sync is off in this project). Props on the World layer (trees, wall with door, crates, chest) per the 2.2 contract. Blocked cells come from one authored list used for both prop placement and `WalkabilityGridProvider`/`GridWalkabilityBlocker` config.
- **Party:** 4 characters (varied appearance/gear) + 1 skeleton (hostile target) through `WorldSpawnService.BindRestoredView`, each with a **unique spawn id, `npcDefinitionId` none**, `resource-hp`; the `SpriteActionPresentationHook` is plain C# and is constructed/wired by the bootstrap. The panel builds its own `EquipmentService` (WorldSpawnService's is private).
- **Explicit engine wiring (nothing assumed):**
  - `CreatureDied` -> `WorldSpawnService.MarkDefeated` (bootstrap subscribes; the only production caller is `TacticalEncounterController`, out of scope). `SpriteActionCues` then defers the destroy.
  - Damage buttons run the authored abilities through `AbilityResolutionService`; real `DeathSaveService` registered. It only advances via `BeforeDamage` or explicit `RollDeathSave`, so the panel has a **Roll death save** button; "Damage 30" kills only via overkill >= max HP.
  - **Heal & stand** button = restore HP, clear `DeathSaves`, remove `condition-unconscious` **and publish `ConditionRemoved` itself** (`CharacterState.RemoveCondition` publishes nothing; this is the recorded engine gap, worked around in glue, not fixed).
- **Click path (verified viable unmodified):** `PlayerCommandRouter` + `PartyExplorationController` + `GridCharacterMotor` with `IPointerCaptureProvider` (`UIToolkitPointerCapture`), `RegisterMotor` per character. Keep **single selection**: `HandleMove` applies formation offsets to every selected member and would scatter the party.
- **Door:** `UseDoorInteractionAdapter` + `GridWalkabilityBlocker` on the **same GameObject**, `WorldInteractable` with an **explicit `interactionAnchor`** on a walkable cell outside the blocked set (else it logs `Debug.LogError`), an `InteractionController` ticked every frame; the panel also has an "Open door" button calling `BeginExecute` as a fallback.
- **Camera:** scene-local `GateCamera` (ortho, pitch 90, yaw 0, follows selection, integer zoom via `orthographicSize`). Throwaway; the authored camera is Phase 3.
- **Debug panel** (UI Toolkit, scene-local): equip/unequip per slot, attack, damage 3, damage 30, roll death save, heal & stand, open door, quick-save, quick-load, zoom, reset.
- **Save/load contract (real `SaveService`/`SessionLoadCoordinator`, no toolkit change needed, but four preconditions):**
  (a) the skeleton is in `additionalCharacterIds` (capture only takes party + those; otherwise `BindRestoredView` throws and the whole load fails);
  (b) all 5 characters have unique spawn ids (positions come only from `WorldSpawnSnapshot`) and `partyMemberVisualPrefabs` is supplied at load;
  (c) the gate scene is the **active** scene (load rejects a scene mismatch);
  (d) `Commit` builds a **new `GameSession`** and destroys every old agent, so the router, exploration controller, death-save service, interaction controller, equipment service and spawn service are rebuilt through `SessionConsumerRegistry`/`ISessionConsumer`.

## 2.7c — Automated backstop + handoff
- EditMode: scene content test asserting the **authored/serialized** blocked-cell list (a live grid is not populated in Edit mode: `GridWalkabilityBlocker.Push` runs in `OnEnable`), all props within |Z| <= 256, sprites from the copied art folder, sorting layers exist, door and anchor wiring.
- PlayMode (`-assemblyNames BarrowsEnd.PlayModeTests`, whose asmdef gains references to `BarrowsEnd.Gate` and the CRPG assemblies; RenderTexture only, never `ScreenCapture`): (a) 5 characters bind, cues initialized; (b) one character walks around the obstacle and through the opened door by the real command path; (c) damage to 0 -> Down; roll saves/heavy damage -> Died -> corpse destroyed after the fade; (d) equip/unequip changes layer state; (e) save -> mutate -> load restores appearance, gear, position and downed state; (f) sorting: south of a wall renders above, north below; (g) no `LogError`/exception in a 10 s run. Save a RenderTexture capture for inspection.
- Full toolkit EditMode + PlayMode suites unchanged (no toolkit runtime change expected; any change is a recorded finding).
- Handoff: menu item `Barrows End/Gate/Open Phase Two Gate`, the checklist, captures. Phase 2 closes on the user's explicit sign-off; nothing committed until they ask.

## Non-goals
Tilemaps and walkability bake, real camera rig (Phase 3), 2D creator (4), production UI (5), monster art beyond the skeleton, audio, dialogue, a full `TacticalEncounterController` combat (Phase 6).

## Effort / risk (ranked)
1. 2.7a content authoring (largest unbudgeted item). 2. Save/load through `SessionLoadCoordinator` with session-consumer rewiring (the real code risk). 3. Assembly/build-settings plumbing. 4. Glue for "real" engine events (list above). Bootstrap + panel + content is realistically 2-3x the first estimate; reviewed as three sub-tasks, each run through the tests-first / mutation-check / results workflow.

## Review 2 corrections (verified against source; supersede anything above that conflicts)
**Ordering:** strictly sequential 2.7a -> 2.7b -> 2.7c. `ContentValidator` (`ContentValidator.cs:57`) resolves each equipped slot id as an `EquipmentSlotDefinition`, so if `slot-head` is not in the provider's content list **every save with a helmet fails to load** (a hard 2.7a dependency, not a soft one). The `BarrowsEnd.Gate` asmdef from 2.7b must exist before 2.7c's PlayMode tests.

**2.7a additions:**
- Ability authoring: `AbilityDefinition.effects` is `[SerializeReference]`, so the Editor author uses `SerializedProperty.managedReferenceValue`; `DamageEffect.hitPointResource` is an object reference to the package asset `Rules/SRD5e2014/Content/ResourceDefinition_HitPoints.asset`. Debug abilities must have **empty costs** (`AbilityCostPlan`/`CheckReadiness` reject a missing pool and nothing refills Action here).
- The provider delegates `srd5Provider.Register(builder)` then `builder.RegisterDefinitions(extra)` (consumed by `GameSessionHost.StartSession` -> `RulesetSessionFactory`). Editor asmdef needs `CRPG.Actors`, `CRPG.Inventory`, `CRPG.Abilities`, `CRPG.Rules.SRD5` (references are not transitive).
- No SRD5 rule rejects an unknown slot (`EquipmentService.Equip` only checks the item's own slot), so the head slot is safe to add.

**2.7b additions / corrections:**
- Debug ability targeting: `TargetingDefinition` defaults to Enemy relation, range 5, line of sight; construct `AbilityResolutionService` with **null faction provider and null positions** (else party casts are rejected `WrongRelation`). `CanAct` rejects `DeathSaves != null` and `condition-unconscious`, so **Heal & stand** clears both before any further ability.
- Input/selection stack (previously unnamed): `InputSystemCommandSource` (feeds `PlayerCommandRouter.HandlePointerDown/Move/Up` with a camera ray), `SceneSelectableSource`, `PartyMovementService` with `GridPathfindingService` as its `INavMeshService`, an `IFormationSolver`, a `UIDocument` + `PanelSettings` registered with `UIToolkitPointerCapture.Register`, a camera tagged MainCamera on the hittable layer, EventSystem as needed. Runtime asmdef references: `CRPG.Exploration`, `CRPG.Interaction`, `CRPG.InputSystem`, `CRPG.Saving`, `CRPG.Rules.SRD5`, `CRPG.Abilities`, `CRPG.Actors`, `CRPG.Inventory`, `CRPG.Gameplay`, `CRPG.Navigation`.
- **Session rewiring is written from scratch:** no `ISessionConsumer` implementation exists anywhere; 2.7b authors one per rebuildable service (router, exploration controller/movement service, death-save service, interaction controller, equipment service, spawn service, cues hook).
- Save contract fixes: a missing skeleton in `additionalCharacterIds` fails at `ContentValidator.cs:73-75` (`ContentValidationFailed`), not inside `BindRestoredView`. Use the **ruleset-aware `Load` overload** (`SessionLoadCoordinator.cs:111`, rebuilds content/policies and checks `Header.RulesetId`), not the bare one (`:95`), and pass `scenePath` to `Capture` or the scene check never fires.
- Verified true: appearance, equipped slots, HP, `DeathSaves` and conditions all round-trip, and a restored downed character re-triggers Down through `IsDownAtBind` (prefab already carries `resource-hp`).
- Watch item: binders initialized on an inactive staged instance that is activated at commit is an untested combination.

**2.7c addition:** the save/load PlayMode test is the riskiest test; write it first.

**Honest re-estimate:** 2.7a 1.5-2 sessions; 2.7b 3-4 sessions (~12 collaborators plus the consumer layer); 2.7c 1.5-2 sessions. Total 6-8 sessions, well over the earlier 2-3x. Each sub-task ends with suites green and Results appended; commits only when asked.

## Decision (made by the user)
Split into 2.7a content, 2.7b bootstrap + scene, 2.7c automated backstop + Gate B handoff. Each sub-task ends with its own suites green and Results appended here; commits only when asked.

## Task 2.7b Results (built 2026-09-20)

**Built (BarrowsEnd):**
- `Assets/Runtime/Gate/` (new `BarrowsEnd.Gate` runtime asmdef): `GateLayout` (single source of truth for tiles, props, blocked cells, door, default party), `GateBootstrap` (composition root: ruleset-aware `GameSession`, party spawned through `WorldSpawnService.BindRestoredView` with appearance and gear set BEFORE the view binds, skeleton via `SpawnNpc`, quick-save/quick-load through the real `SaveService`/`SessionLoadCoordinator` ruleset-aware `Load`), `GateSessionServices` (the single `ISessionConsumer`: death-save service, equipment service, ability resolution, `PartyExplorationController`, interaction controller, `CreatureDied` -> `MarkDefeated`, plus every panel action), `GateCommandSource` (wraps `InputSystemCommandSource`; filters Select to party members and to ONE character), `GateCamera` (ortho, pitch 90, yaw 0, integer scale by `orthographicSize`, uses the render target height when one is set), `GatePanel` (procedural UI Toolkit debug panel registered with `UIToolkitPointerCapture`).
- `Assets/Editor/Gate/PhaseTwoGateSceneAuthor.cs` (+ `BarrowsEnd.Gate.Editor` asmdef; menus `Barrows End/Gate/Build Phase Two Gate` and `Open Phase Two Gate`), the authored scene `Assets/Scenes/Gate/PhaseTwoGate.unity` (24x16 grass/path/stone tiles, a wall row with a door, 4+ trees, 3 barrels, a chest, a real 3D ground collider, the door as `UseDoorInteractionAdapter` + `GridWalkabilityBlocker` on one object with an explicit anchor), `gate-panel-settings.asset`, and both scenes in `EditorBuildSettings`.
- 2.7a extension: `npc-skeleton` (`NpcDefinition`, HP 13, faction `faction-undead`, custom skeleton body) authored by `BarrowsEndGearAuthor` and registered in the ruleset (the skeleton must be an NPC so it restores from its `NpcDefinition` on load).
**Tests:** `GateSceneTests` (8 PlayMode, on the authored scene through the REAL router fed a camera ray exactly as `OnClick` does): party of four + skeleton all bound with cues; worn gear shows and the helm hides hair; clicking a member selects only them and clicking the skeleton does nothing; click-to-move on the grid; the wall holds until the door is clicked open, then the member walks through; real damage -> Down, heal & stand recovers, smash kills and the corpse is removed after the fade; equip buttons toggle gear live; quick-save then load restores appearance, gear, position and downed state into a brand-new session, rebuilds every consumer (`RebindCount` 2, no duplicated agents) and click + movement still work. `GateCaptureTests` (1) renders the real camera into a RenderTexture at x3/x4/x5 and saves PNGs (looked at by eye: the scene renders correctly). BarrowsEnd EditMode +1 (skeleton NPC).
**Suites (fresh copy of the real project):** BarrowsEnd EditMode 25/25; PlayMode 10/10 (8 gate + 1 capture + the existing look spike). The toolkit is unchanged, so its suites were not re-run.
**Mutation checks (all caught):** no Select filter (1 fails); no motor registration after rebind (3); `CreatureDied` not hooked to `MarkDefeated` (1); skeleton omitted from the save (1); heal not publishing `ConditionRemoved` (1); gear not equipped at spawn (2); no consumer rebind after load (1); load coordinator without the grid backend (1).

**Findings / deviations from the plan:**
- One `ISessionConsumer` (`GateSessionServices`) rebuilds all session-scoped services together, instead of one per service: they are created and discarded as a unit. `InputSystemCommandSource` (and its router) is session independent and is not rebuilt.
- No `EventSystem` in the scene: Unity 6 UI Toolkit panels take pointer input straight from the Input System package. Whether the panel buttons respond to real mouse clicks is only verifiable by hand (Gate B).
- The skeleton is an NPC (`SpawnNpc`), not a party-less character: on load it is rebuilt from its `NpcDefinition` (prefab, faction and appearance come from it).
- Debug actions use the selected character as its own source (abilities are `Any`-relation, free, no range) so the panel needs no second combatant.
- Door state is not part of the save (quick-load leaves the door as-is); recorded, out of scope for the gate.
- Environment: the user's interactive Editor held `BarrowsEnd` open, so all verification ran in throwaway copies (`Assets`, `Packages`, `ProjectSettings`); the authored scene/assets and `EditorBuildSettings.asset` were copied back. Rendering captures need a GPU device (`-nographics` segfaults the run). `-testResults` paths resolve against the project.
- Placeholder art for the wall, door, trees and barrels was picked from a labelled contact sheet of the copied Kenney sheet (`roguelikeSheet_transparent`); tile choices live in `GateLayout` and are one-line changes. The door tile has a green edge from the source art.
- Not committed.

## Task 2.7c Results (built 2026-09-20) and Phase 2 close

**Built:** `GateSceneContentTests` (7 EditMode, on the authored scene's SERIALIZED data): the provider's blocked cells equal `GateLayout` exactly and the door cell is left to its blocker; the door is one object with adapter, blocker, interactable, collider and a serialized anchor on a walkable cell; every tile and prop sprite comes from the copied Kenney sheet and sits inside |Z| <= 256, every World-layer prop y-sorts through the prop lane, tile and prop counts match the layout; Ground/World sorting layers exist and the camera is an orthographic yaw-0 pitch-90 MainCamera; the ground collider covers the whole grid; every member has a unique spawn id and a distinct walkable start cell, every appearance option exists in the catalogs and every gear item in the ruleset; both scenes are in the build settings. `GateSceneTests` gained three PlayMode tests: a member south of the wall draws above it and north of it below it; two members sort by Z; ten simulated seconds of everything (click-move, equip, attack cue, damage, door, death save, heal, zoom, save, load, smash) logs no error or exception.
**Suites:** BarrowsEnd EditMode 32/32 (25 + 7) and PlayMode 13/13 (10 + 3), run in place; toolkit EditMode 912/912 and PlayMode 395 passed + 3 skipped (unchanged since 2.6).
**Mutation checks:** prop cells dropped from the blocked list (caught), door anchor moved into the blocked door cell (caught). A third mutation (a member moved onto a blocked cell in `GateLayout`) was not a valid check: the scene stores its own serialized copy of the member cells, so it only changes on a scene rebuild. The sorting tests guard the scene's use of the toolkit's sorting contract, which has its own mutation-checked tests (2.2); they were not separately mutated.

**Phase 2 close-out.** Gate B (2026-09-20): the user ran the gate scene and reported it looks great. Everything the Phase 2 plan set out to build is built, committed and green:
- 2.0 look spike and Gate A; 2.1 rocking motion; 2.2 layer state, compositor and sorting contract; 2.3 sprite equipment; 2.4 appearance storage, catalogs and save v3; 2.5 wrapper builder, card and CharacterView fixes; 2.6 combat cues with deferred defeat; 2.7a campaign ruleset, head slot, gear, debug abilities and skeleton NPC; 2.7b gate scene, bootstrap and session rewiring; 2.7c static and behavioural backstop.
- Phase 3 inputs recorded along the way: the camera authoring must drive `orthographicSize` (the toolkit camera rig's zoom is a distance) and lock yaw to 0; tilemap/world authoring must keep |Z| <= 256 and bake the walkability grid; `HealEffect` and `CharacterState.RemoveCondition` publish nothing (heal-from-0 needs glue, as `GateSessionServices.HealAndStand` shows); door state is not saved; the gate's panel and camera are throwaway glue.
- Not verified by any automated test (left to play): real mouse clicks on the UI Toolkit panel buttons (the scene has no EventSystem by design), and pixel crispness on a real display.

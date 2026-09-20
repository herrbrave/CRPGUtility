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

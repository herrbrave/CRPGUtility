# CRPG 2D Campaign — Implementation Plan (DRAFT v2, post-Opus review)

> Status: pre-implementation planning only. Per the user's explicit instruction, no code work starts until
> this plan is approved. This document does not authorize implementation, committing, or scene/asset
> authoring yet.
>
> **v2 note:** v1 claimed "two seams, `IMovable`/`INavMeshService`, zero changes elsewhere" and got an Opus
> review that didn't survive contact with the actual source — five direct NavMesh bypasses (two of which hard-
> fail without a `NavMeshAgent`), a motor-contract mismatch, no ground-collider story, a facing seam that
> doesn't exist, and a camera section that was half right/half wrong. The core architectural bet still holds
> (confirmed independently by the review); the scope estimate did not. This version folds every finding in
> directly rather than appending a changelog — treat it as the real plan, not v1 plus patches.

**Goal:** Build a short (30-60 minute) playable 2D campaign on top of the existing CRPG Toolkit, reusing
`crpg-toolkit`'s rules/combat/dialogue/quest/save layers completely unchanged, using Kenney's 16x16 "Roguelike"
asset family (`Roguelike pack`, `Roguelike characters`, `Roguelike cave & dungeon pack`, `Roguelike indoor
pack`) for art, and `UI pack -> Adventure pack` + `Game icons` for a re-skinned HUD.

**Central architecture decision (confirmed sound, scope corrected):** "2D" means **sprite-based visual
presentation + grid-based movement planning**, not a literal switch to Unity's 2D physics/rendering pipeline.
The scene stays on the X/Z ground plane with 3D colliders and `UnityEngine.Camera`/`Physics.Raycast`, near-
vertical orthographic instead of isometric; characters/props render as camera-facing sprites instead of
meshes. The reused-unchanged surface area is real but smaller than v1 claimed: UI Toolkit HUD (`TurnOrderView`/
`AbilityBarView` — independently verified to contain zero `Camera`/`Vector3`/`Transform` references), the
combat/ability/reaction rules machinery, and `PlayerCommandRouter`/`AbilityTargetingGate`'s raycast approach —
**but only given a real 3D collider ground/wall plane still exists under the sprites** (see Task 3.0). The
movement layer needs a real new motor contract, not a drop-in swap (see Task 1.1-1.3). A handful of other
systems talk to `NavMeshAgent`/`NavMesh` directly, bypassing the `IMovable`/`INavMeshService` seam entirely,
and must be either seamed or explicitly scoped out (see Task 0.2).

## What already exists (reuse, don't rebuild) — confirmed by reading the actual current source

- **`Runtime/Navigation/IMovable`** (`SetDestination`/`Stop`/`Snapshot`) and **`INavMeshService`**
  (`TrySamplePosition`/`TryCalculatePathLength`/`TryCalculatePath`) are real seams a movement backend can plug
  into for the tactical action layer specifically. `CharacterAgent.Bind` resolves `Motor` via
  `GetComponent<IMovable>() ?? GetComponentInChildren<IMovable>()` — a new grid motor component just needs to
  implement `IMovable` and sit on the prefab; no `CharacterAgent` change needed. **But these are not the only
  places NavMesh-specific code lives** — see "What does not exist yet" below; do not treat this seam as
  covering the whole movement surface of the engine.
- **`Runtime/Gameplay/Actions/MoveActionExecution.cs`/`ActionScheduler.cs`** and
  **`Runtime/Combat/MovementResolutionService.cs`** (budget/resource spend, `FindThreatBoundaryCrossings`,
  `ResolveReactionForCrossing`) are pure `Vector3` geometry with no NavMesh-specific assumption in the geometry
  itself. This is real and confirmed. **However** (corrected from v1): `MoveActionExecution` calls
  `IMovable.SetDestination` with **arbitrary `Vector3` points**, not just cell centers — threat-crossing stops
  (`Vector3.Lerp(segmentStart, segmentEnd, t)`, `MoveActionExecution.cs:174`), budget-clip points
  (`Vector3.MoveTowards(...)`, `:226`), and the entity-target constructor's straight-line point (`:90`) are all
  continuous, not grid-aligned. A grid motor's `SetDestination` contract must accept any `Vector3`, not just
  cell centers — see Task 1.2's corrected spec.
- **`Runtime/Presentation/CharacterView.cs`**: `BindModel` calls `Animator = model.GetComponentInChildren<Animator>(true)`
  then `playableAnimator?.BindPresentation(...)` / `visualEquipment?.RefreshBoneMap(...)` — both are null-safe
  no-ops when the Model has no Humanoid `Animator`/no `EquipmentVisualBinder`, confirmed. A sprite Model (a
  GameObject with just a `SpriteRenderer` + a new lightweight sprite-facing driver) binds through the same
  `SetModel`/`BindModel` path with no `CharacterView` changes required for the sprite-only case. **Caveat**
  (new, from review): when a Model WITH an Animator is swapped to one WITHOUT one, `BindPresentation` is never
  called to replace it, and `UnbindPresentation` is never called either — `CharacterPlayableAnimator` stays
  bound to the just-destroyed old Animator. Irrelevant if this campaign never swaps a 3D model onto a sprite
  character mid-game (it won't), but worth a one-line doc-comment fix while touching this file, not a blocker.
- **`Runtime/Combat` turn/ability/reaction machinery, `Runtime/AI` (Utility AI), `Runtime/Abilities`,
  `CRPG.Rules.SRD5`** — entirely presentation/dimension-agnostic already (proven twice: `TacticalEncounter` and
  the Fate `ConsumerGame`). Reuse `TacticalEncounter`'s composition pattern (`AbilityTargetingGate`,
  `TacticalPlayerMoveAdapter`, `TacticalCombatUIBridge`, `TurnOrderView`/`AbilityBarView` via `HudRoot`)
  near-verbatim. **Correction**: `TacticalEncounterSceneAuthor.cs` does **not** use `IsometricCameraRig` — it
  authors a bare `Camera` with a hand-set transform. The rig and the sample are two independently-unproven
  camera stories; see Task 3.0.
- **`Runtime/UI/Combat/TurnOrderView.cs`, `Runtime/UI/Abilities/AbilityBarView.cs`, `HudRoot`** — independently
  grepped for `Camera`/`Vector3`/`WorldToScreen`/`Transform`: zero hits. Re-skinning with Kenney's Adventure-
  pack UI chrome and Game-icon sprites is a real USS/texture swap only, no C# changes expected.
- **`PlayerCommandRouter`'s drag-select and ray resolution**: `camera.WorldToScreenPoint`/the `screenPoint.z < 0`
  behind-camera check (`:158-159`) and `InputSystemCommandSource`'s `camera.ScreenPointToRay` both behave
  correctly for an orthographic camera — confirmed, this part of the plan holds.
- **`CameraRigState`'s pitch math at Pitch=90**: independently computed — `Euler(90,0,0) * Vector3.back` places
  the camera directly overhead with a clean forward vector, no gimbal lock. This part of v1's camera claim was
  right. What v1 got wrong around it is in Task 3.0 below.

## What does not exist yet / needs building — corrected scope

### Direct NavMesh bypasses outside the IMovable/INavMeshService seam (new, from review — must be resolved before Phase 6)

Four real call sites, across three files, talk to `NavMeshAgent`/`UnityEngine.AI.NavMesh` directly, not through
the seam (corrected from an earlier count of five — `TravelInteractionAdapter` was mistakenly included; it only
delegates to `SceneTravelCoordinator`, which is already counted, and touches no NavMesh API itself):

1. `Runtime/Gameplay/Scenes/SceneTravelCoordinator.cs:121` — `NavMesh.SamplePosition(...)` validates every
   destination formation slot directly; a failure here rejects the whole scene travel.
2. `SceneTravelCoordinator.cs:157-158` — `agent.GetComponent<NavMeshAgent>()` then `.Warp(...)`; **`navAgent ==
   null` is treated as failure and rolls the whole travel back** — a grid-motor character cannot scene-travel
   at all today.
3. `Runtime/Gameplay/SessionLoadCoordinator.cs:474` — `GetComponentInChildren<NavMeshAgent>(true)?.Warp(...)`
   on every restored agent, added specifically to fix a real "stale position after load" bug. Null-safe, so it
   won't crash, but a grid motor gets no equivalent fix and may show the same stale-position symptom on load.
4. `Runtime/Gameplay/Interactions/Adapters/TeleportInteractionAdapter.cs` — `GetComponent<NavMeshAgent>()`
   then `.Warp(...)`.
5. `Runtime/Gameplay/Interactions/Adapters/UseDoorInteractionAdapter.cs:14` — doors work by toggling a real
   carving `NavMeshObstacle`. **A grid pathfinder has no way to see a closed door under this implementation.**

(Numbered 1-5 above for traceability to the original finding, but item 4 and item 2 land in the same file and
the real fix count is four call sites in three files — `SceneTravelCoordinator`, `SessionLoadCoordinator`,
`TeleportInteractionAdapter`, `UseDoorInteractionAdapter`.) The full implementation-ready design for all four,
plus a `bool`-returning `IMovable.Warp` and the `IWalkabilityBlocker` seam, is in
`Docs/Plans/2D-Campaign-Phase1-GridMovement.md`, which also found and closed a structural gap this list didn't
originally have room for: which `WalkabilityGrid` is active for a given location scene, and how the
grid pathfinding service and every grid-backed door/motor re-bind to it across additive scene travel.

### Ground/wall collider plane (new, from review)

`PlayerCommandRouter`/`AbilityTargetingGate` raycast with `Physics.Raycast` (3D) against `hittableMask`. Today
the ground hit target is a real 3D mesh (`TacticalEncounterSceneAuthor.cs`'s `CreatePrimitive(PrimitiveType.Plane)`).
Once meshes are replaced by sprites, **nothing is left for the ground-tier raycast to hit** unless an explicit
invisible 3D collider plane/wall-box layer is authored underneath the tile art — `Physics.Raycast` cannot see a
`TilemapCollider2D` at all (different physics world). This also affects `PhysicsLineOfSightProvider`
(`Physics.Linecast`) — without real 3D wall colliders, line-of-sight checks never find an obstruction and no
ability is ever blocked by a dungeon wall.

### Facing and animation approach (LOCKED — user decision 2026-09-18)

**Characters always face the camera (pure billboard) and "walking" is a simple procedural rock/tilt, South
Park-style — no directional sprites, no walk-cycle frames, no per-direction sprite set at all.** This closes
Open Decisions 2 (animation depth) and most of the facing risk the review raised: since the visible sprite
never changes based on facing direction, nothing about the visual needs to read `FacingController`/yaw at all.

- `FacingController` stays on CharacterRoot completely unchanged — still drives root rotation as pure data,
  still serves `ActionScheduler.StartAbility`'s `CommittedAction` facing claim and `InteractionController`'s
  `TurningToFace` gate exactly as it does today for 3D characters. Zero regression risk, because nothing visual
  depends on its output anymore.
- The visible sprite is a child that always renders camera-facing regardless of root rotation (a plain
  billboard: match the child's rotation to the camera's each frame, or simpler still, counter-rotate against
  the root — trivial either way given a nearly-top-down orthographic camera with locked yaw).
- The new presentation piece is small: a procedural rock/tilt driven by `IMovable.Snapshot.Velocity` (moving vs.
  idle) — oscillate a small rotation (or a squash/offset) on a timer while moving, settle to neutral at rest.
  No sprite-swap logic, no direction state machine.

### Camera (corrected scope, from review)

Pitch=90 math is clean (confirmed), but three real issues remain:

1. **`Rig.Zoom` moves the camera transform (`cameraTransform.localPosition = Vector3.back * Zoom`), which has
   no visual effect on an orthographic camera** — apparent size comes from `Camera.orthographicSize`, which the
   rig never touches. Scroll-to-zoom would be silently dead unless explicitly wired (e.g. the input adapter's
   zoom delta also adjusts `orthographicSize`, or `CameraRigState` gains that as a new, small, explicit
   responsibility).
2. **Yaw must be authored to zero and locked** (`rotationSpeed`/`snapRotationDegrees` = 0 in the settings
   asset) — at Pitch 90, Q/E rotation spins the whole map under upright sprites, which reads as broken, not as
   a feature.
3. **No sample proves this rig at all.** `TacticalEncounter` hand-authors a bare `Camera` transform, not
   `IsometricCameraRig`. The 3-transform `CameraRoot -> YawPivot -> PitchPivot -> Camera` hierarchy needs to be
   authored and proven fresh, not assumed working from precedent.

Also new, not previously addressed: **sprite sorting/depth** — a camera-facing billboard at true top-down pitch
lies visually flat and coplanar with ground tiles, producing z-fighting unless given an explicit vertical
offset and/or a `SpriteRenderer` sorting-order scheme.

### Grid connectivity, diagonal cost, and cell occupancy (new Open Decisions, from review)

- **4- vs 8-connected pathing.** Every other distance check in the engine (`MovementResolutionService`'s threat
  range, the segment/circle threat-crossing math, ability range) is Euclidean. 4-connected grid pathing makes
  path *cost* Manhattan while range checks stay Euclidean — a move that's cheap on the grid can still be "far"
  for range purposes, and looks like a staircase. Needs a real decision (4-connected simple-but-diagonal-blind,
  or 8-connected with SRD5's 5-10-5 alternating diagonal cost) before Task 1.1.
- **Cell occupancy / local avoidance.** `NavMeshAgent` provides free avoidance; a grid motor has none.
  `PartyMovementService`'s formation-slot spacing (1.5 units) vs. a 1-unit grid means two formation slots can
  land on the same cell without an explicit occupancy check, and combat has no "can two combatants share a
  cell" rule yet. Needs a decision before Phase 1 fixtures can be written meaningfully.

### Prefab authoring path (new, from review)

`Editor/Character/CharacterWrapperBuilder.cs` unconditionally adds `NavMeshAgent` and `FacingController` to any
character root it builds. A 2D character prefab needs its own authoring path (or an explicit opt-out flag) —
otherwise a grid motor added alongside an auto-added `NavMeshAgent` creates two `IMovable`-shaped components on
one prefab, making `CharacterAgent`'s `GetComponent<IMovable>() ?? GetComponentInChildren<IMovable>()`
resolution order-dependent and silently wrong.

### Walkability data source, world authoring, 2D character-creator customization depth — unchanged from v1

- Walkability data source: **decided** (was Open Decision 3 in v1) — a live `Tilemap`/`TilemapCollider2D`
  option is off the table now that the ground/wall plane must be real 3D colliders anyway (see above). Go with
  an Editor-time bake into a plain `WalkabilityGrid` asset, derived from wherever the 3D wall/floor colliders
  end up authored.
- World authoring: build directly in Unity's native `Tilemap` editor from sliced Kenney sheets (not via Tiled
  `.tmx` import) — unchanged from v1, still recommended to avoid an external-tool dependency. The walkability
  bake step reads the *3D collider* layer authored alongside the Tilemap, not the Tilemap itself.
- Animation depth: **decided** (see "Facing and animation approach" above) — camera-facing billboard + a
  procedural rock/tilt, no directional sprites or walk-cycle frames needed. Character customization depth
  (single static sprite per archetype, vs. any palette/layer variation) is still genuinely unknown until the
  user's asset inventory confirms it — correctly left open, and does **not** block Phase 1. Only blocks Phase 4
  scope, and Phase 2 only slightly (whether one sprite or a small variant set needs swapping in).
- **Character-creator "preview" claim, corrected**: v1 said "only the preview swaps a 3D instantiate for a 2D
  sprite preview." There is no live preview today — `CharacterCreatorBootstrap.cs`'s `Instantiate(heroPrefab,
  ...)` happens once, after character creation completes, not as a live-updating preview while picking. A
  live preview is a genuinely new feature for Phase 4, not a swap. Separately, `CharacterBuildPlan` carries no
  appearance/visual field at all — mapping a built Race/Class to a chosen archetype sprite is new consumer-side
  data this plan must author, not something the existing build-plan type already resolves.

## Phase 0 — Scope lock (must complete before any Phase 1 code)

### Task 0.1: Asset inventory close-out — RESOLVED

- Character customization: **layered/modular, confirmed feasible.** Kenney's `Character pack (425 assets)` is
  genuinely modular (`PNG/[Skin]/Face|Hair|Pants|Shirts|Shoes/...`, plus a `Completes` folder for pre-assembled
  references) — a real source for Task 2.5's layer compositor. It's a different, front-facing art style from
  the 16x16 top-down "Roguelike" world tiles, which is fine and not a mismatch: since characters are always
  camera-facing billboards (per the locked South-Park-style animation decision), the character art was never
  going to need to match the world's top-down perspective anyway.
- Animation depth: locked (billboard + procedural rock, see Phase 2). No walk-cycle dependency.
- Tile grid size: 16px Kenney native tiles, 1 cell = 1 Unity unit (see Open Decisions).
- Map count: confirmed from the layout handoff — 1 town (Barrow's End, ~11 locations) + 3 dungeon levels
  (Glimmering Caverns Upper/Middle/Deep), connected via a cave entrance and (per the dialogue sample) an
  optional mining-lift shortcut. This is real, load-bearing input for Task 0.2 below.

### Task 0.2: NavMesh-bypass scope — RESOLVED (seam it, not scope around)

The confirmed map count settles this: multi-scene travel between town and 3 dungeon levels is real, required
content, and the Deep Cavern boss arena has a portcullis (a door-class obstacle) — so scoping around
`SceneTravelCoordinator`/`UseDoorInteractionAdapter`'s NavMesh bypasses is not viable. Decision: seam both.
- `SceneTravelCoordinator`: replace the direct `NavMesh.SamplePosition`/`NavMeshAgent.Warp` calls with
  `INavMeshService.TrySamplePosition` (already grid-service-compatible) and an `IMovable`-level
  teleport/reposition capability (new — `IMovable` currently has no instant-reposition method, only
  `SetDestination`/`Stop`; add one, since `MoveActionExecution`'s contract already assumes `IMovable` is the
  single source of truth for a mover's position semantics).
- `UseDoorInteractionAdapter`: currently couples directly to `NavMeshObstacle`. Needs a small walkability-
  blocker abstraction (e.g. an `IWalkabilityBlocker`/similar that both a `NavMeshObstacle`-backed adapter and a
  grid-cell-blocking adapter can implement) so the same interaction-adapter pattern works for both movement
  backends without the adapter itself branching on which one is active.
- `SessionLoadCoordinator`'s `NavMeshAgent.Warp` on restore: covered by the same new `IMovable` reposition
  capability above — one fix serves both call sites.
- `TeleportInteractionAdapter`/`TravelInteractionAdapter`: same fix, third call site, no separate design needed.

### Task 0.3: Grid connectivity, cell occupancy, collider authoring — RESOLVED

- **8-connected, true Euclidean step cost** (1.0 cardinal, ≈1.41 diagonal — not SRD5's tabletop 5-10-5
  alternating rule), matching the rest of the engine's Euclidean range/distance math throughout
  (`MovementResolutionService`'s threat range, ability range, the segment/circle crossing math) rather than
  introducing a Manhattan/Euclidean mismatch. Bonus from the review's own W4 finding: non-integer diagonal
  costs are naturally less likely to land exactly on `Mathf.CeilToInt`'s rounding boundary than an all-integer
  4-connected grid would, reducing (not eliminating) that risk on its own.
- **Cell occupancy: one character per cell**, a move into an occupied cell is rejected the same way an
  unreachable NavMesh destination is (simplest rule, matches tactical-grid-game convention, avoids the
  formation-slot-stacking risk the review flagged).
- **Ground/wall colliders**: confirmed approach from the review — an invisible 3D `BoxCollider` ground plane
  plus box colliders on wall tiles, authored alongside each Tilemap. `WalkabilityGrid` is baked from this
  collider layer at Editor time (Task 3.2), not derived from the Tilemap directly.

## Phase 1 — Grid movement & pathfinding (package Runtime additions)

### Task 1.1: Prove the motor contract first (moved to front per review — this is the actual risk-retirement step)

Before building a real A*/BFS pathfinder, write a minimal fake/stub grid `IMovable` and drive it directly
through `MoveActionExecution` in a test fixture, proving the REAL contract `MoveActionExecution` needs:
arbitrary (non-cell-aligned) `SetDestination` points (threat-crossing stops, budget-clip points), a
correctly-handled zero-length `SetDestination` (must resolve `Pending` -> `Arrived`, never `Invalid`), request-id
supersession semantics identical to `NavMeshCharacterMotor`'s, and the pause guarantee (`Time.timeScale = 0`
must be a true no-op — driven by scaled `Time.deltaTime`/`FixedUpdate`, never unscaled time or a coroutine that
ignores timescale). Also prove the integer-budget rounding risk directly: a straight N-cell move must spend
exactly N movement, not N+1 from float drift landing exactly on `Mathf.CeilToInt`'s boundary every single step
(a real risk specific to grid movement that irregular NavMesh distances never hit). If this fixture finds
`MoveActionExecution`/`ActionScheduler`/`MovementResolutionService` assumes something the contract review above
missed, that's a real correction to this plan, not something to work around in the grid layer.

### Task 1.2: Grid pathfinding service

NEW `crpg-toolkit/Runtime/Navigation/Grid/GridCoordinate.cs`, `WalkabilityGrid.cs`, `GridPathfindingService.cs`
(implements `INavMeshService` per Task 0.3's connectivity decision — corner list = cell-center path, but
`TryCalculatePath`'s `to` argument must snap to the nearest walkable cell rather than requiring an exact match,
matching NavMesh's own implicit snapping behavior). Document the `areaMask` parameter's meaning for a grid
service explicitly (ignored, or mapped to authored terrain-cost tiers) rather than leaving it undecided.
NEW `Tests/Editor/GridPathfindingServiceTests.cs`: straight path, path around an obstacle, unreachable
destination (partial path = failure, matching `TryCalculatePath`'s existing contract), snapping behavior,
`TrySamplePosition`/`TryCalculatePathLength` correctness.

### Task 1.3: Grid character motor

NEW `Runtime/Navigation/Grid/GridCharacterMotor.cs` (`MonoBehaviour, IMovable`) built to the contract Task 1.1
proved: accepts arbitrary `Vector3` destinations (paths to the containing cell via the pathfinder, then
traverses the remaining sub-cell distance linearly), same `MotionSnapshot` status vocabulary and request-id
semantics as `NavMeshCharacterMotor`, arrival epsilon smaller than the shortest leg it can ever be issued
(threat-crossing stops can land arbitrarily close to a cell boundary), driven by scaled time only. NEW
`Tests/Runtime/GridCharacterMotorTests.cs`: everything Task 1.1's fixture proved, now against the real
implementation, plus a real obstacle forcing a multi-cell route and a pause/resume check.

### Task 1.4: Prove the full seam end to end

Extend or add a fixture exercising `MoveActionExecution`/`ActionScheduler`/`MovementResolutionService`
(including `FindThreatBoundaryCrossings`/opportunity attacks) against the real grid motor+pathfinder, with
**zero changes** to those three files. Include the cell-occupancy policy decided in Task 0.3 if it turned out
to need enforcement here rather than at a higher layer.

## Phase 2 — Sprite presentation layer (package Runtime additions)

### Task 2.1: Billboard component

NEW small `Runtime/Presentation/Sprite/CameraFacingBillboard.cs` (Mono) — the sprite child always renders
facing the camera regardless of CharacterRoot's rotation. `FacingController` itself needs no code changes at
all (confirm with a quick fixture that `InteractionController`'s `TurningToFace` gate and `ActionScheduler`'s
`CommittedAction` facing still resolve correctly underneath a billboarded sprite — cheap to prove, not expected
to find anything, but worth a real check rather than an assumption).

### Task 2.2: Rocking-motion presentation state + driver

NEW `Runtime/Presentation/Sprite/RockingMotionState.cs` (pure C#, EditMode-testable, mirrors
`CharacterPresentationState`'s "state decides what, driver decides how" split — much smaller than a direction
state machine: just moving/idle plus an oscillation phase). NEW `Runtime/Presentation/Sprite/RockingMotionDriver.cs`
(Mono driver — reads `IMovable.Snapshot.Velocity` for moving/idle, applies a small oscillating tilt/offset to
the billboarded sprite while moving, settles to neutral at rest). NEW `Tests/Editor/RockingMotionStateTests.cs`.

### Task 2.3: Wire into CharacterView's Model-swap contract

Confirm via a real fixture that a sprite Model binds correctly through `CharacterView.SetModel`/`BindModel`
with no `CharacterView` code changes for the sprite-only case (the stale-animator-binding gap noted above only
matters for a 3D-to-sprite swap, which this campaign doesn't do — confirm that assumption holds before treating
it as moot).

### Task 2.4: 2D character prefab authoring path

New Editor authoring path (or an explicit opt-out) so a 2D character prefab does not get `CharacterWrapperBuilder`'s
automatic `NavMeshAgent`, avoiding the two-`IMovable`-components-on-one-prefab hazard.

### Task 2.5 — RESTORED, required (see Character Creator handoff below): layered 2D equipment compositor

**No longer cut.** Both handoff docs independently confirm true layered sprite customization is real scope, not
a nice-to-have: the campaign handoff's Party Composition section ("Highly customizable using modular sprite
assets — base bodies + layered armor/weapons/hair") and the GUI handoff's Character Creator wireframe (cycling
Head/Body/Weapon/Class selectors driving a live "assembled character sprite" preview). `EquipmentVisualBinder`/
`EquipmentVisualProfile` are skinned-mesh/bone-binding-specific and not reusable as-is, but the 2D equivalent is
genuinely simpler: NEW `Runtime/Presentation/Sprite/SpriteLayerCompositor.cs` — stacks ordered `SpriteRenderer`
layers (Body/Head/Weapon/etc., each an independent child with its own sorting order) and swaps each layer's
sprite independently per slot, no bone/skeleton concept needed at all. Reused by both the character creator's
live preview (Task 4.1) and in-world/in-combat rendering of whatever was assembled — the same compositor, not
two separate systems. NEW `Tests/Editor/SpriteLayerCompositorTests.cs`.

## Phase 3 — Top-down camera & world authoring (content + the collider-plane task)

### Task 3.0: Ground/wall collider plane (new, prerequisite for everything raycast-based)

Author an invisible 3D collider ground plane plus box colliders on wall tiles, placed to match the Tilemap's
visual layout, on the layer `PlayerCommandRouter`/`AbilityTargetingGate` raycast against. This is what makes
`PhysicsLineOfSightProvider`'s wall-blocking behavior real instead of silently always-true, and what makes the
ground-tier click-to-move raycast hit anything at all once meshes are gone.

### Task 3.1: Top-down camera authoring

Build and prove the real `CameraRoot -> YawPivot -> PitchPivot -> Camera` hierarchy with `IsometricCameraRig`
(not previously proven by any sample) at near-90 pitch. Author a `CRPGCameraSettings` asset with yaw locked to
zero (`rotationSpeed`/`snapRotationDegrees` = 0) and decide explicitly how zoom will affect `Camera.orthographicSize`
(the rig's own `Zoom` field has no visual effect on an orthographic camera as-is — needs either a small,
explicit addition or an input-adapter-level wiring decision). Author an explicit sprite-sorting/depth scheme to
avoid z-fighting between billboards and ground tiles at this pitch.

### Task 3.2: World authoring pipeline

Slice the relevant Kenney sheets into Unity `Tile` assets; build campaign maps directly in Unity's native
`Tilemap` editor. Bake `WalkabilityGrid` data from the Task 3.0 collider layer via an Editor tool.

## Phase 4 — 2D character creator (scope locked by the GUI handoff's wireframe B)

### Task 4.1: Live layered-sprite-preview character creator

Split-screen layout per the handoff: a preview panel (left) showing the live-assembled character sprite via
Task 2.5's `SpriteLayerCompositor`, and an options panel (right) with cycling `< Category: Value >` rows for
Head/Body/Weapon (visual layers, feed the compositor directly) and Class (a `CharacterBuildPlan` choice, feeds
`CharacterBuildService` same as the 3D sample). Confirm/Cancel footer buttons. Reuses `CharacterBuildPlan`/
`CharacterBuildService` for the Race/Class/Background rules-layer choices; the layered-appearance choices are
new consumer-side data (a small archetype/layer-option catalog) since `CharacterBuildPlan` itself carries no
appearance field. Genuinely new feature (there is no live preview in the existing 3D sample to swap from — see
prior finding), but the live-update mechanism is now simple: every category change just re-feeds the same
compositor used in-world.

## Phase 5 — Combat UI re-skin (scope expanded per the GUI handoff — three screens, not one)

The handoff specifies three UI Toolkit screens, all USS/texture work reusing existing Presenter/View pairs —
independently confirmed none of the relevant views (`TurnOrderView`/`AbilityBarView`) have 3D-world assumptions
baked in. Asset manifest: `uipack_rpg_sheet.png` + `KenPixel.ttf` (verify the exact source file during Task 0.1
— this filename doesn't exactly match the `UI pack -> Adventure pack` folder name found in the original asset
survey; confirm which Kenney pack it actually lives in before authoring against it).

### Task 5.1: Combat/exploration HUD re-skin

Party status (top-left, stacked HP + a secondary resource pip-row per member), action bar (bottom-center,
`AbilityBarView`), system menu (top-right). Reuse `TurnOrderView`/`AbilityBarView`/`HudRoot`/`AbilityTargetingGate`/
`TacticalPlayerMoveAdapter`/`TacticalCombatUIBridge` near-verbatim, re-skinned with the Kenney RPG UI sheet and
`Game icons`/expansions for ability/item icons. Confirm which existing party-status view (if any) already
renders an always-visible multi-member HP list outside of `TurnOrderView`'s combat-only turn-pip display — the
handoff's HUD shows all 4 party members' HP simultaneously during ordinary exploration, not just in combat.

### Task 5.2: Paper doll / inventory screen re-skin

Anatomical equipment-slot layout (Head/Body/L-Weapon/R-Weapon/Feet) + a stat block (STR/DEX/AC/HP) + a backpack
grid with a scrollbar. Verify this maps onto the existing `Runtime/UI/Inventory/InventoryPresenter.cs`'s data
shape (equip slots via `EquipmentService`, backpack contents) with only a new View needed — expected, not yet
confirmed; this is a new visual layout (anatomical slot arrangement) the existing sample doesn't have, so
treat "no presenter changes needed" as a verify-first claim like the rest of this plan's reuse claims, not an
assumption.

### Task 5.3: Character creator screen re-skin

Covered by Task 4.1's layout above — listed here only for the asset/USS pass once Task 4.1's structure exists.

## Phase 6 — Campaign content & story

Requires Task 0.2's NavMesh-bypass decision to be resolved or scoped around before any multi-map travel, door,
or save/load-reposition content is built. Once the story is ready: dialogue trees, quests, encounter placement,
map layout, save points — built as consumer-local bootstraps/content-authoring scripts in the new sibling
project, the same way Oakhaven/ConsumerGame were. Scoped into its own tasks once the story and map count are
known.

## Phase 7 — Gate: 30-60 minute playable pass

Live human review checkpoint: full playthrough start to finish, confirm movement/combat/dialogue/quests/saves
all work in the 2D presentation (including whatever Task 0.2 scoped in or out), confirm the re-skinned HUD
reads correctly, confirm total playtime lands in the target range.

## Remaining open decisions (need the user before/around Phase 0)

1. **Project location — DECIDED.** New sibling Unity project `BarrowsEnd/` (matches the `ConsumerGame`
   precedent), named for the campaign itself ("Barrow's End" hub town). Handoff materials received so far live
   in `BarrowsEnd/Docs/Handoff/` (`Barrows-End-Handoff.md`, `Barrows-End-Town-and-Caverns-Layout.png`; a GUI
   handoff doc still pending). The new grid-movement/sprite-presentation code belongs in `crpg-toolkit/Runtime/`
   (generic, shippable); campaign content stays consumer-local to `BarrowsEnd/`.
2. **Grid scale.** Recommend 1 grid cell = 1 Unity unit = Kenney's native 16px tile — keeps
   `MovementResolutionService`'s existing SRD5 movement-resource math meaningful with sane numbers (e.g.
   `TacticalEncounter`'s existing 8-unit movement budget / 2-unit melee threat range become an 8-cell budget /
   2-cell threat radius, a reasonable tactical scale).
3. Task 0.1's asset-inventory findings (customization depth, map count) — animation depth is now locked, see
   "Facing and animation approach."
4. Task 0.2's NavMesh-bypass scope decision.
5. Task 0.3's connectivity/occupancy/collider-authoring decisions.

## Sonnet task report template

(same as `Docs/Plans/Production-Foundation-Implementation.md`)

Task ID / agreed scope:
Changed files and asset GUID/migration effects:
Tests added and expected failing result observed:
Actual test command + inner summary:
PlayMode/build/visual checks actually performed:
Unverified items and blockers:
Remaining consumer glue or unsupported capabilities:
Next proposed task (do not automatically run):

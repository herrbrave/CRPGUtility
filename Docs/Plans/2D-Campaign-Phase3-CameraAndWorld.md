# Phase 3 Detailed Design: Top-down camera and world authoring

> Status: **DRAFT v2** (Opus review 1 folded in; awaiting user approval). Companion to `2D-Campaign-Implementation.md` (master plan, Phase 3) and
> `2D-Campaign-Phase2-SpritePresentation.md` (Phase 2, closed 2026-09-20). Supersedes the master plan's three-line Phase 3 sketch
> (3.0 collider plane / 3.1 camera / 3.2 world pipeline), folding in what Phases 1-2 and the gate scene taught us.

## Goal
Everything between "a character walks on a grid" (done) and "a designer builds the town and three caverns" (Phase 6):
1. a **real top-down camera** (orthographic, pitch 90, yaw locked, integer pixel zoom, pixel-snapped follow, clamped to the map);
2. a **tilemap authoring pipeline**: paint a location in Unity's native Tilemap editor with semantic tiles; one click bakes the walkability
   grid, the 3D collider layers the click router and line-of-sight need, the depth-sorted props, and the camera bounds, and reports mistakes;
3. a **location shell**: a Core scene plus location scenes with travel points and spawn anchors, proven with two small demo maps, including
   travel and save/load across scenes.
It builds no gameplay content (that is Phase 6) and no UI (Phase 5).

## What exists (verified in source, 2026-09-20)
- **Gate scene (2.7)**: individual flat `SpriteRenderer` tiles and props, a ground `BoxCollider`, a `WalkabilityGridProvider` with a serialized
  blocked-cell list, `GridWalkabilityBlocker` doors, the throwaway `GateCamera`. All authored from one layout class by an Editor script. This is the
  precedent to generalize, and its ground-collider fix (`PlayerCommandRouter` raycasts PHYSICS) is already proven.
- **`IsometricCameraRig`/`CameraRigState`/`CRPGCameraSettings`** (`Runtime/Camera`): the rig sets `cameraTransform.localPosition = back * Zoom`, so **zoom is a
  distance and does nothing to an orthographic camera** (found in 2.0). `Rotate` multiplies by `RotationSpeed` and `SnapRotate` by `SnapRotationDegrees`, so
  setting both to 0 already locks yaw with no code change. Pitch min/max/default can all be 90 (proven by the 2.0 look spike). Follow is exponential
  smoothing (`FollowSmoothingSpeed`), which produces sub-pixel camera positions.
- **`PartyCameraFollowBehaviour`** drives the rig from the party selection. **`LocationContent`** already carries `CameraBoundsXZ`, NPC spawn points, stores and
  loot containers, but only `SceneTravelCoordinator` reads the bounds; **nothing clamps the rig continuously**.
- **Scene travel and load** (Phase 1): `SceneTravelCoordinator` and `SessionLoadCoordinator.LoadCoroutine` are grid-aware (they activate the target scene's
  `WalkabilityGridProvider` grid), `LocationInitializer` brings a location up, `SceneTravelDestination` anchors mark arrival points, and `ISessionConsumer` /
  `SessionConsumerRegistry` is the rebind contract (written for real in 2.7b as `GateSessionServices`).
- **`PhysicsLineOfSightProvider(LayerMask)`** uses `Physics.Linecast`: with no 3D wall colliders no ability is ever blocked by a wall.
- **`PlayerCommandRouter.ResolveClick`** raycasts `hittableMask`; ANY hit that is not a `CharacterAgent` or `WorldInteractable` becomes a ground move at `hit.point`. A
  tall wall box in that mask would turn a click on a wall into a move onto the wall's top face. `InputSystemCommandSource.hittableMask` is serialized (default everything).
- **Assets**: `com.unity.modules.tilemap` is in the manifest (the Tilemap module; no `2d.tilemap.extras`). The copied sheets (`roguelikeSheet`, `roguelikeDungeon`,
  `roguelikeChar`) are sliced to named sprites (`<sheet>_<col>_<row>`, PPU 16, point filter, FullRect). The layout mock-up shows the town as roofless "dollhouse"
  buildings (interiors visible, no roofs) and the caverns as three stacked levels joined by stairs.

## Design decisions
**D1. Camera changes go in the toolkit; small, additive, default-off.** `CRPGCameraSettings` gains `orthographic` (default false), `pixelsPerUnit` (16), `zoomScales` (int
steps, e.g. 2..6), `defaultScale`, `pixelSnapFocus` (bool) and `clampToBounds`. `IsometricCameraRig` applies them: when orthographic it sets `Camera.orthographicSize =
viewportHeight / (2 * ppu * scale)` (render-target height when one is set, the lesson from `GateCamera`) and keeps the camera at a fixed distance; when not, behaviour is
byte-for-byte unchanged (existing rig tests must stay green). Zoom input steps through `zoomScales` instead of a continuous distance. Yaw lock = the existing
`rotationSpeed = snapRotationDegrees = 0`, asserted by a settings validator. A new `BarrowsEnd_Camera` asset (pitch 90/90/90, yaw locked, ortho, scales 2-6, default 3)
replaces the 2.0 spike asset.

**D2. Pixel-snapped follow, not pixel-snapped smoothing.** Smoothed following stays (it feels good) but the focus point handed to the camera transform is rounded to
`1 / (ppu * scale)` world units so sprites never shimmer. The unsnapped focus stays the source of truth (snapping is presentation only, so it cannot drift).

**D3. Bounds clamp.** When `LocationContent.CameraBoundsXZ` exists the rig clamps the visible rectangle (not the focus point) inside it, and centres on an axis where the map is
smaller than the view. The bounds are baked from the tilemap extents by default (D6) and can be overridden by hand.

**D4. Semantic tiles.** A `SemanticTile : Tile` (toolkit, `CRPG.Navigation.Grid.Tilemaps`, no ruleset knowledge) carries one `TileSemantic`:
`Ground` (walkable), `Blocked` (unwalkable, no collider height; water, chasm), `Wall` (unwalkable, 3D collider that blocks line of sight, not in the router mask),
`Prop` (unwalkable, sorted sprite object, optional low collider), `Decor` (walkable, sorted sprite object, e.g. a lantern the party walks past), `Door` (a
`GridWalkabilityBlocker` cell, spawned by the baker). There is **no default**: a tile asset is generated with an explicit semantic from a curated table
(BarrowsEnd content), and the baker fails on any tile in a map that has none.

**D5. Two-tier rendering.** Ground/floor tiles live on a real `Tilemap` (Grid `cellSwizzle = XZY`, `TilemapRenderer` on the Ground sorting layer, chunk mode) for cheap
painting and batching. Anything that must depth-sort with characters (`Prop`, `Wall` faces, `Decor`, doors) is **baked to individual `SpriteRenderer`s with a
`SpriteDepthSorter`** (exactly what the gate does), because a tilemap renderer has a single sorting order and cannot interleave with a character's Z-derived order. Painting
still happens on tilemaps (a `Props` tilemap); the baker turns it into sorted objects and hides/disables the painted layer's renderer. Task 3.0 exists to prove or overturn
this before any tool is built.

**D6. One baker, idempotent, never hand-edited output.** A `LocationBaker` (Editor) reads the location's tilemaps and regenerates a `Baked` root: the
`WalkabilityGridProvider` (origin/size from the tilemap bounds, blocked cells from semantics), a ground collider plane, merged wall boxes on a new `WorldObstacle` physics layer,
prop objects, door blockers, and `LocationContent.CameraBoundsXZ`. Re-baking replaces the `Baked` root only and keeps hand-placed objects and asset GUIDs; a determinism test
bakes twice and compares.

**D7. Physics layering.** `Ground` (ground plane, hit by the router), `WorldObstacle` (wall boxes: hit by line of sight, **excluded from the router's `hittableMask`**, so a click
on a wall is ignored or lands on the ground plane behind it as designed rather than on the wall's top). The authored camera/input prefab sets `hittableMask` accordingly and the
line-of-sight provider is constructed with `WorldObstacle`. (Confirmed as a behaviour to test, not assumed: a click on a wall cell must not produce a move onto a wall.)

**D8. Location shell.** `Core.unity` owns the session bootstrap (generalized from `GateBootstrap`: ruleset, party, input, camera rig, consumer registry, save/load) and location
scenes carry only content: tilemaps, `Baked`, `LocationContent`, `SceneTravelDestination` anchors and travel interactables. The shell is proven with two small demo locations. The
gate scene stays as the Phase 2 regression scene.

**D9. Building style and levels.** Buildings are roofless open-plan areas inside the town map (as in the mock-up): no interior scenes, no roof occlusion. Each cavern level is its own
location scene; stairs are visual only (walkable ground). Water and waterfalls are `Blocked`; mine rails are `Decor`/`Ground` art.

**D10. Who paints.** This phase builds the tools and two small demo slices (authored by script for tests, paintable by hand); the real town and caverns are painted in Phase 6 with the user.

## Task breakdown (each: plan doc section reviewed by Opus, tests first, hand mutation checks, both suites, Results appended, commit only when asked)

### Task 3.0 — Tilemap-on-XZ spike (retires the biggest unknown; small)
In `BarrowsEnd`: a Grid with `cellSwizzle = XZY`, a Ground `Tilemap` painted from sliced tiles and a `Props` tilemap, the pitch-90 ortho rig, characters walking behind and in front of
baked props. Answer with evidence (RenderTexture captures, a PlayMode test): (a) does an XZ tilemap render flat and upright under the pitch-90 camera, and is painting in the Scene view
workable; (b) chunk vs individual mode and sorting behaviour against characters; (c) tile flip/rotation flags; (d) pixel crispness at integer scales with snapped focus. Outcome: D5
confirmed or replaced (fallback: individual-mode tilemap with a custom transparency-sort axis, or all sorted content as objects). Deliverable: a short findings section, not production code.

### Task 3.1 — Top-down camera authoring (toolkit + BarrowsEnd)
D1-D3: settings fields, rig ortho path, pixel-snapped focus, bounds clamp, integer zoom stepping, a settings validator (yaw locked, pitch 90, scales sane), the `BarrowsEnd_Camera`
asset, wiring `PartyCameraFollowBehaviour` and `InputSystemCommandSource` zoom to steps. Tests: EditMode `CameraRigState`/settings maths (ortho size, snap, clamp incl. map smaller than view,
yaw stays 0 under rotate/snap input, existing perspective behaviour unchanged); PlayMode real rig + follow + bounds; the existing rig tests untouched and green.

### Task 3.2 — Semantic tiles and tile import (toolkit type + BarrowsEnd content)
`TileSemantic`, `SemanticTile`, a generator from a sliced sheet plus a curated semantics table (BarrowsEnd `TileSemanticsTable`, keyed by sprite name, rules by column/row range with
per-tile overrides, authored WITH the user for the Kenney town/cave/dungeon tiles), Tile Palettes for town and cave, and a validator (every generated tile has a semantic; no tile
without one is paintable). Tests: generator idempotence (GUIDs stable across re-run), table coverage, a tile with no semantic fails.

### Task 3.3 — Location baker (Editor tool)
D6-D7: `LocationBaker` and its report: grid from tilemap bounds and semantics, ground plane, merged wall boxes on `WorldObstacle`, baked prop/decor objects with depth sorters, door
blockers, camera bounds, plus checks (spawn anchors and travel anchors on walkable cells, doors without a walkable approach cell, walkable regions unreachable from the arrival anchor,
|Z| <= 256, tiles without semantics). Tests on tiny synthetic tilemaps (blocked cells match, wall run merging, click on a wall ignored, LOS blocked by a wall and clear elsewhere,
idempotent re-bake, hand-placed objects survive), mutation-checked.

### Task 3.4 — Location shell and cross-scene proof
D8: `Core.unity` + `LocationShell` (generalized session bootstrap and consumer registry from `GateBootstrap`/`GateSessionServices`), a location scene template, two demo locations (town slice,
cave slice) baked by the tool, travel interactables with `SceneTravelDestination` anchors, camera bounds per location. Tests: travel town -> cave -> town with the grid, party positions, camera
focus and bounds correct on arrival; save in the cave, load from the town (cross-scene `LoadCoroutine`), everything rebound; no `LogError`.

### Task 3.5 — Phase gate (USER REVIEW GATE C)
Play the two demo locations: camera feel (pixel stability while walking and following, zoom steps, bounds), sorting behind trees/buildings/walls, walls blocking movement and line of
sight, door, travel and save/load across scenes, and the painting workflow itself (paint a few tiles in the Tilemap editor, bake, walk on them). Phase 3 closes on sign-off.

## Review 1 corrections (Opus, verified against source; these supersede anything above that conflicts)
**Blockers fixed**
- **B1 painting package.** `com.unity.modules.tilemap` is the runtime module only. The Tile Palette window, brushes and `Create > 2D > Tiles` live in `com.unity.2d.tilemap`, which is NOT in BarrowsEnd's manifest (add `com.unity.2d.tilemap`, plus `com.unity.2d.tilemap.extras` only if RuleTiles are wanted) and must be added BEFORE Task 3.0, or the spike could "disprove" tilemaps for the wrong reason. Task 3.0 starts with this.
- **B2 toolkit dependency.** A `SemanticTile : Tile` in a toolkit runtime asmdef needs `"com.unity.modules.tilemap": "1.0.0"` in `crpg-toolkit/package.json` (asmdefs need no module entry: engine modules are implicit while `noEngineReferences` is false).
- **B3 layers.** `BarrowsEnd/ProjectSettings/TagManager.asset` has layers 8-31 empty: add `Ground` and `WorldObstacle` there. The shippable baker takes **layer indices as parameters**; a package cannot own project layer names.
**Wrong claims corrected**
- **D3 is not additive.** `CameraRigState.SetBounds` / `ApplyBoundsAndHeight` (`CameraRigState.cs:39,106-119`) already clamp the FOCUS point on every `SnapTo`/`FollowTarget`/`Pan`, wired by `SceneTravelCoordinator` and `AdventureSliceBootstrap`, with tests in `CameraRigStateTests.cs` (~150, 174). D3 becomes "replace the focus clamp with a VIEWPORT clamp when orthographic, leave the perspective path alone"; the two bounds tests change or gain ortho variants (perspective behaviour keeps its tests). Task 3.1 is 2 sessions, not 1.5.
- **D7's movement rationale is wrong.** `WalkabilityGrid.ToGrid` ignores Y and `ToWorld` pins Y to the origin, so a click on a wall's top face resolves to the same cell as the ground below. Keep the `WorldObstacle` layer split, but only for line of sight (and to keep wall boxes out of the router's mask for cleanliness); drop the "moves onto the wall top" story.
- **No spike camera asset exists in BarrowsEnd.** `BarrowsEnd_Camera` is new, not a replacement.
- **3.4's premise.** `TestProject/Assets/AdventureSlice` already implements Core + Overworld + Interior scenes, `SceneTravelCoordinator`, cross-scene load and consumer rebind (`AdventureSliceBootstrap.cs`, 374 lines). Task 3.4 PORTS that (and uses `LoadCoroutine(..., coreScene, ...)`), instead of generalizing the single-scene `GateBootstrap` (whose synchronous `Load` hard-rejects a scene mismatch). Its consumer rebind still follows `GateSessionServices`.
**Design corrections**
- **D5 needs both `Grid.cellSwizzle = XZY` AND `Tilemap.orientation = XZ`** (swizzle only lays cells out on XZ; sprites still render in local XY). Task 3.0 tests both, and also the cheaper alternative the review found: `Tile.gameObject` with `TileFlags.InstantiateGameObjectRuntimeOnly`, which makes the Tilemap instantiate a prop prefab per painted tile and could delete most of the baker's prop pass.
- **Performance:** `SpriteDepthSorter.LateUpdate` per instance would cost ~2400 calls a frame at 60 x 40 for props that never move. The baker sets each static prop's `sortingOrder` ONCE and attaches no sorter (only moving objects need one).
- **Scene bloat:** thousands of baked GameObjects mean ~100k lines of scene YAML and merge hostility. The baker bakes the `Baked` root into a **prefab asset or at load time**, decided in 3.0/3.3, not into the scene file.
- **LOS geometry:** characters stand at Y = origin; a wall box spanning y 0..2 puts both linecast endpoints on its bottom face (a PhysX grazing case). Wall boxes straddle the plane (centre y = 0, or an explicit eye height) and the ground plane is kept out of the LOS mask. `TacticalSliceBootstrap` passes `hittableMask` as the LOS mask: the precedent is the opposite of D7 and is not copied.
- **Also required:** `Physics.SyncTransforms()` after baking (auto-sync is off in BarrowsEnd); `BarrowsEnd.PlayModeTests.asmdef` gains `CRPG.Camera` and `CRPG.Exploration`; the zoom scale is exposed for Phase 5's HUD; the baker bakes the grid FROM SEMANTICS, a **deliberate deviation** from the master plan's "bake from colliders" (semantics are the source of truth; colliders are derived), recorded here.
**Re-estimates:** 3.0 1-1.5; 3.1 2; 3.2 1.5-2; 3.3 3-4; 3.4 1.5-2 (port, not generalize); 3.5 1. **Total 10-12.5 sessions.**

## Effort (honest)
3.0: 0.5-1 session. 3.1: 1.5. 3.2: 1.5-2 (plus a curation pass with the user). 3.3: 2-3. 3.4: 2-3 (the shell generalization is real work). 3.5: 1. **About 9-12 sessions.**

## Risks and open questions
1. **Tilemap on the XZ plane** is unproven in this project (orientation, Scene-view painting, renderer sort interaction). Task 3.0 is ordered first for that reason; D5 may change.
2. **Tile semantics need curation**: the Kenney sheets carry no metadata; wrong guesses (a tile that looks like a wall but is floor) are silent until played. Mitigation: an overlay view in the
   validator and a curation pass with the user; the no-default rule.
3. Camera pixel snapping vs. smoothing can still shimmer at sub-pixel character positions (characters move continuously; only the camera is snapped). Judge at Gate C; a snap on character
   presentation is a possible follow-up, not in scope.
4. The `Baked` root approach means the painted `Props` tilemap and the baked objects both exist in the scene; keeping them from double-rendering needs a clear convention (baker disables the
   painted renderer and says so).
5. Line of sight uses 3D boxes with a chosen height; tall-vs-short cover (barricades) is a Phase 6 tuning question.
6. Scope creep guard: no NPC placement, quests or UI here.

### Decisions needed from the user
1. Camera + semantic tile + baker code in the **toolkit** (generic, shippable, default-off) with semantics and content in BarrowsEnd: recommended, per the master plan's "generic code in the toolkit".
2. Confirm **roofless dollhouse buildings** and **one scene per cavern level with visual-only stairs** (D9).
3. Confirm **I build the tools and two demo slices; you paint the real maps in Phase 6** (D10), and that a **tile-semantics curation pass with you** belongs inside Task 3.2.
4. Zoom range: integer scales 2-6, default 3 at 1080p (about 40 x 22 tiles visible). OK?

## Task 3.0 Results (spike, run 2026-09-20)

**Built:** `BarrowsEnd/Assets/Tests/PlayMode/TilemapSpikeTests.cs` (5 tests, everything built at runtime, frames captured to `%TEMP%/sp/spike_*.png`), and the `com.unity.2d.tilemap` 1.0.0 dependency added to `BarrowsEnd/Packages/manifest.json` (+ lock). All 5 tests pass. Verified in a throwaway copy of the project because the user's Editor held BarrowsEnd open.

**Findings (all measured, not assumed):**
1. **Both settings are required.** `Grid.cellSwizzle = XZY` maps cell (x, y) to world (x, 0, z) but leaves sprites standing in local XY; `Tilemap.orientation = XZ` lays them flat. With swizzle alone the tiles are edge-on from above (background colour at the cell centre). With both, ground renders flat and **upright** (a tree's crown is toward +Z/screen-up, its trunk toward -Z, checked by sampling pixels) and cell (3,2) centres at world (3.5, 0, 2.5).
2. **A whole Tilemap has one sorting order, so it cannot interleave with characters** (tilemap order 0; characters at order -448 and -704 for Z 3.5 and 5.5 land on the same side of it). This confirms D5's premise: anything that must sort against characters cannot be a tilemap tile.
3. **The cheaper prop route works.** A tile with `Tile.gameObject` set makes the Tilemap instantiate the prefab **at the cell centre, already rotated flat** (euler 90, 0, 0) with the sprite, and a `SpriteDepthSorter` on it gives correct in-front/behind ordering against characters. Gotchas: the instance **copies the template's active state** (an inactive scene template gives inactive instances; a prefab asset is active so is fine), and it is **runtime-only by default** (`InstantiateGameObjectRuntimeOnly`), so nothing shows in Edit mode unless the tile also keeps its own sprite.
4. **Rotation flags work.** `Tilemap.SetTransformMatrix` with a 90 degree rotation about Z rotates the painted tile within the XZ plane (the trunk moved from -Z to +X).
5. **Painting tooling exists.** `com.unity.2d.tilemap` is a built-in package (resolves offline) and its palette/brush editor code handles every `cellSwizzle` (paintable scene-view grid per swizzle). Interactive painting on the XZY grid could not be exercised headless: **to be confirmed by hand early in 3.2** (a five-minute check by the user).

**Design change (D5 revised, D6 shrinks):** painted props no longer need a bake pass. The `Props` tilemap holds prop tiles whose `SemanticTile.gameObject` is a small prop prefab (`SpriteRenderer` on the World layer + a static sorter that computes its `sortingOrder` once from its Z and disables itself, added in 3.3). The Tilemap instantiates them at runtime at the correct place, flat, so **there is no `Baked/Props` root, no thousands of scene objects in the YAML, and no per-frame sorter cost**. The painted tile keeps its `sprite` for Edit-mode preview; a small `PropTilemap` component turns the painted layer's renderer off at runtime so nothing draws twice. The baker (3.3) now only bakes: the walkability grid, the ground plane and wall boxes, door blockers, camera bounds and the validation report. Cost to watch: instantiating ~2400 prop objects at scene load (measure in 3.3/3.4). 3.3's estimate drops to about 2.5-3 sessions; the phase total is 9.5-12.

**Not covered by the spike (carry into later tasks):** chunk vs individual mode for the ground layer beyond the fact that chunk mode is fine; perf of large maps; wall/collider work (3.3); interactive palette painting (above).

**Painting confirmed by hand (2026-09-20):** the user created a Grid (cell swizzle XZY) and a Tile Palette from the sliced sheet in BarrowsEnd and painted successfully. Their scratch folder (`Assets/Runtime/Grid`: a Grid prefab, a palette prefab and 1704 auto-generated `Tile` assets, one per sprite, all with no semantics) was deleted; Task 3.2's generator replaces it with semantic tiles.

## Task 3.1 Results (built 2026-09-20)

**Toolkit (default-off, additive):** `CRPGCameraSettings` gained `orthographic`, `pixelsPerUnit`, `zoomScales`, `defaultScale`, `pixelSnapFocus`. `CameraRigState` gained `Scale`, `OrthographicSize`, `HalfExtents`, `SetViewport`, `StepScale` and `PresentationFocus`; in orthographic mode `ApplyZoom` steps one integer scale per call (the camera distance never changes) and the bounds clamp keeps the VISIBLE rectangle inside the map (centring on an axis where the map is smaller than the view; the focus point is re-clamped whenever the viewport or the scale changes). `IsometricCameraRig` feeds the viewport (render-target height when set, else the camera's pixel height), sets `Camera.orthographicSize` and hands the camera the pixel-snapped focus; the unsnapped `FocusPoint` stays the source of truth so snapping cannot drift. `CameraSettingsValidator` (Editor, wired into the package validator window) errors on empty/unordered/non-positive scales and warns on a default scale off the list, an unlocked yaw and a pitch off 90. **The perspective path is byte-for-byte unchanged**: all existing camera tests pass untouched (the plan's warning that the bounds tests would change turned out not to apply, because the viewport clamp only exists in orthographic mode).
**BarrowsEnd:** `BarrowsEndCameraAuthor` (menu `Barrows End/Content/Author Camera`, idempotent) authors `Assets/Content/Camera/BarrowsEnd_Camera.asset` (orthographic, scales 2-6, default 3, pixel snap on, yaw locked, pitch 90, fixed distance 20, follow on, edge scroll off) and `BarrowsEnd_CameraRig.prefab` (CameraRoot > YawPivot > PitchPivot > Main Camera with URP data and one AudioListener; `IsometricCameraRig` + `PartyCameraFollowBehaviour` wired to the asset).
**Tests:** toolkit EditMode `CameraRigOrthographicTests` (17: size maths, default-scale fallback, no size before a viewport, one-notch zoom steps and unchanged distance, scale clamps, perspective unchanged, yaw locked, viewport bounds clamp, small-map centring, zoom-out and viewport-change re-clamp, snap on the pixel grid and per scale, follow convergence unaffected by snapping, validator rules); toolkit PlayMode `OrthographicCameraRigTests` (5: real rig and Camera). BarrowsEnd EditMode `CameraContentTests` (5: settings values, validator clean, prefab hierarchy and references, camera flags, idempotent authoring); BarrowsEnd PlayMode `CameraPrefabTests` (4: the authored prefab driven by the real `PartyCameraFollowBehaviour` with a walking party member: starts at the party, follows pixel-snapped and inside the map, steps 3 -> 6 -> 2 and stops at the ends, yaw and pitch cannot be turned).
**Suites:** toolkit EditMode 929/929 (912 + 17); toolkit PlayMode 400 passed + 3 skipped (395 + 5); BarrowsEnd EditMode 37/37 (32 + 5); BarrowsEnd PlayMode 22/22 (13 + 5 spike + 4 camera).
**Mutation checks (10, all caught):** plain focus clamp ignoring the viewport (4 fail); no centring for a small map; no snapping (2); zoom still changing the distance; orthographic size missing its factor of 2 (2); `SetViewport` not re-clamping; `StepScale` not re-clamping; the rig ignoring the snapped focus; never setting the orthographic size; never handing over the viewport (2).

**Findings:**
- The 3.0 spike tests were order-dependent: another test's scene left loaded (the look spike's floor) occupied the same coordinates and rendered into the spike's captures once the full BarrowsEnd suite ran together. The spike now builds far away on X (Z stays small for sorting).
- `OrthographicSize` was first not gated on the orthographic setting (caught by the "non-orthographic rig is unchanged" test).
- Zoom wiring is unchanged: `InputSystemCommandSource.OnZoom` calls `Rig.ApplyZoom(delta)`, which now steps one scale per event. Whether a fast wheel spin steps too fast is a feel question for Gate C (a throttle is a one-line follow-up).
- The camera prefab does not contain the input source; the location shell (3.4) wires `InputSystemCommandSource.cameraRig` to it. The gate scene keeps its throwaway `GateCamera` as the Phase 2 regression scene.
- Camera bounds are still set by whoever calls `SetBounds` (travel, or the baker in 3.3 via `LocationContent.CameraBoundsXZ`); nothing new reads them here.
- Not verifiable headless: how the snapped follow looks on a real display, and real mouse-wheel feel.

## Task 3.2 Results (built 2026-09-20; curation pass awaiting the user)

**Toolkit (ruleset-agnostic):** `CRPG.Navigation.Grid.Tilemaps`: `TileSemantic` (`Unclassified = 0` is the invalid zero value, then Ground, Blocked, Wall, Prop, Decor, Door) with `BlocksMovement` / `BlocksSight` / `IsSortedObject`; `SemanticTile : Tile` with `Configure(semantic, sprite, prefab)` (sets the runtime-only instantiate flag for sorted semantics, clears the prefab for flat ones), `GetTileData` (hides the tilemap's own copy of a sorted-object tile's sprite while playing, so nothing draws twice) and `StartUp` (gives the instantiated object the tile's sprite). `StaticSpriteSorter` (`CRPG.Presentation.Sprites`): sorts a never-moving sprite once in `Start` (prop lane, from its own Z) and disables itself, so a map of thousands of props costs nothing per frame; `Resort()` for a deliberate move; warns once when outside |Z| <= 256. `SemanticTileValidator` (Editor, wired into the package validator window): errors on unclassified, missing sprite, sorted tile without a prefab or without a SpriteRenderer; warns on no sorter, a flat tile carrying a prefab, and a sorted tile instantiated while painting. `package.json` now declares `com.unity.modules.tilemap`.
**BarrowsEnd:** the classification is a **data file**, `Assets/Content/Tiles/TileSemantics.json` (per sheet, rectangles of sheet cells with a semantic and a note; later rules override earlier; a cell no rule covers gets no tile). `TileSemanticsTable` (Editor) reads it. `BarrowsEndTileAuthor` (menu `Barrows End/Content/Author Tiles And Palettes`, idempotent) generates one `SemanticTile` per classified sprite (**1650 tiles**: `Assets/Content/Tiles/Town/town_<col>_<row>.asset`, `.../Cave/cave_<col>_<row>.asset`, names permanent), prunes tiles the table no longer classifies, creates the one shared `SortedTileObject.prefab` (SpriteRenderer on the World layer + `StaticSpriteSorter`) and two Tile Palettes (`Palettes/BarrowsEnd_Town.prefab`, `BarrowsEnd_Cave.prefab`) laid out like the sheet (column right, row downward). `BarrowsEnd/Docs/TileCuration/make_overlays.py` renders colour-coded review overlays (`town_semantics.png`, `cave_semantics.png`) from the same JSON. `TileSemantics.json` currently classifies the clearest regions: ground and floors, water (Blocked), trees/furniture/stalls/fences (Prop), building walls and windows (Wall), doors and gates (Door), torches/banners/bones/mushrooms (Decor).
**Tests:** toolkit EditMode `SemanticTileTests` (17); toolkit PlayMode `SemanticTilePlayTests` (5, a real Tilemap on the XZ plane: a sorted-object tile becomes one flat object at the cell with the tile's sprite and `Tilemap.GetSprite` is null while playing, a flat tile stays on the tilemap and instantiates nothing, instances sort correctly against characters in front of and behind them then stop updating, only sorted semantics instantiate, an out-of-range Z warns once). BarrowsEnd EditMode `TileContentTests` (24: table validity, last-rule-wins and unclassified-means-no-tile cases, every classified sprite has a tile with that semantic and every unclassified one has none, all six semantics present, folders mirror the table, validator clean on all 1650 tiles, shared prefab/flags, movement and sight flags, idempotent GUIDs, pruning, palettes hold every tile laid out like the sheet).
**Suites:** toolkit EditMode 946/946 (929 + 17); toolkit PlayMode 405 passed + 3 skipped (400 + 5); BarrowsEnd EditMode 61/61 (37 + 24); BarrowsEnd PlayMode 22/22.
**Mutation checks (13, all caught):** tile data not hiding the sprite; `StartUp` not setting the sprite; sorter not switching itself off; sorter on the wrong lane; sorted tile instantiated while painting; doors blocking from the start; props blocking sight; validator allowing unclassified; validator ignoring a missing prefab; first-rule-wins instead of last; no pruning; unclassified defaulting to Ground; palette rows flipped.

**Deviations from the plan:**
- The planned `PropTilemap` component is not needed: `SemanticTile.GetTileData` hides the duplicate sprite itself.
- Every sorted-object tile shares ONE prefab and `StartUp` sets the sprite, instead of one prefab per sprite.
- Flat decals and rails are `Ground` (paint them on a second ground tilemap); `Decor` is only for standing walkable objects. No extra "overlay" semantic was needed.
- Tile classification lives in a data file, not code, so it can be reviewed and edited without a build.

**Curation status:** this is a FIRST PASS made from the contact sheets, covering the clearest regions. Not classified on purpose (no tile yet, so not paintable): building roofs/gables, the flat colour bars and arrow icons, pond flower beds, orange rugs/pads, timber-frame pieces in the cave and various small items. Review the overlays in `BarrowsEnd/Docs/TileCuration/`, change `TileSemantics.json`, re-run `make_overlays.py` to look again and the tile author (or the tests) to regenerate.
**Findings:** the 1650 tile assets are 11 MB and ~3300 files in the repo (each tile is a small asset + meta). Not verifiable headless: how palette browsing and painting XZ tiles feels beyond the earlier hand check.

## Task 3.3 detailed design (2026-09-20; supersedes the earlier 3.3 paragraph where they differ)
**Baker (`LocationBaker`, toolkit Editor, layers passed in as options):** input is a location root containing one `Grid` (square cells, no rotation) and any number of Tilemaps of `SemanticTile`s. It computes the union of painted cells, then:
- **Walkability:** a cell is walkable iff it has a `Ground` tile (or is a `Door` cell) and no `Blocked`/`Wall`/`Prop` tile on any tilemap; a cell with no ground (void) is unwalkable. Door cells are left walkable in the provider (their own blocker owns them). Output: a `WalkabilityGridProvider` (origin = the painted bounds' min corner at the grid's Y, cell size, width/depth, blocked cells relative to the origin).
- **Colliders:** one ground `BoxCollider` over the bounds on the ground layer; `Wall` cells merged into a minimal set of rectangles, each a `BoxCollider` on the obstacle layer that **straddles the ground plane** (centre y = ground Y) so a line of sight at character height crosses it. Props and water get no collider. (Line of sight uses the obstacle layer; the router's mask excludes it.)
- **Camera bounds:** written to the location's `LocationContent` (added if missing, hand-authored spawn points left alone).
- **Fail closed:** any error leaves the scene untouched and returns a report; output only exists for data that validated. Re-baking replaces only the `Baked` child, deterministically, and calls `Physics.SyncTransforms`.
- **Checks:** tile with no semantic or a plain `Tile` (errors); world Z outside +-256 (error); a `Door` cell with no walkable neighbour (error); `SceneTravelDestination` anchors on an unwalkable cell (error); `Decor` on void (warning); walkable regions unreachable from any travel anchor (warning, 8-connected with no corner cutting, the pathfinder's own rule; doors count as passable).
**Doors need no bake pass.** The Door tile's prefab (`DoorTileObject`) carries the door logic itself: a child visual, `BoxCollider`, `GridWalkabilityBlocker`, `WorldInteractable` with an anchor child, and `UseDoorInteractionAdapter` (closed visual = the child). A new toolkit `TileDoor` component configures it in Start from the scene's `WalkabilityGridProvider`: it finds its cell, configures and closes the blocker, and moves the anchor to the nearest walkable approach cell (south first). While open the visual is hidden; a separate open sprite is left for content. The validator warns when a Door tile's prefab has no `TileDoor`.

## Task 3.3 Results (built 2026-09-20)

**Toolkit:** `CRPG.Editor.Location`: `LocationBaker.Bake(root, LocationBakeOptions)` returning a `BakeReport`, plus the pure helpers `WallMerger` (exact, deterministic rectangle cover of wall cells) and `WalkableRegions` (connected regions under the pathfinder's own rule: 8 neighbours, no corner cutting). The baker computes walkability from the painted semantics (walkable = a Ground or Door tile and no Blocked/Wall/Prop tile; a cell with no ground is void and unwalkable), writes a `WalkabilityGridProvider`, a ground `BoxCollider` on the ground layer, merged wall `BoxCollider`s on the obstacle layer that **straddle the ground plane**, and the camera bounds into the location's `LocationContent` (added if missing; hand-authored spawn points untouched). It **fails closed**: any error leaves the scene exactly as it was. Errors: a tile with no semantic or that is not a `SemanticTile`, a Grid that is not XZY / has rotation or scale / non-square cells, no painted tiles, bad or equal layers, a location outside |Z| <= 256, a Door with no walkable neighbour, a travel destination off walkable ground. Warnings: Decor on void, a Door sharing a cell with a blocker, non-XZ tilemap orientation, walkable regions unreachable from any travel destination (doors count as open). Re-baking replaces only the `Baked` child, deterministically, with `Physics.SyncTransforms`. **`TileDoor`** (`CRPG.Gameplay`): a Door tile's prefab carries the whole door (collider, `GridWalkabilityBlocker`, `WorldInteractable` with an anchor child, `UseDoorInteractionAdapter`, visual child); `TileDoor` finds the scene's grid and its own cell at Start, closes the blocker and moves the anchor to the nearest walkable neighbour (south, north, west, east, then diagonals). `SemanticTileValidator` now warns when a Door tile's prefab has no `TileDoor`.
**BarrowsEnd:** physics layers `Ground` (8) and `WorldObstacle` (9) declared in `TagManager.asset`, with `BarrowsEndLayers` (constants and masks: the router mask excludes the obstacle layer, line of sight uses only it); `BarrowsEndLocationBake` (menu `Barrows End/Location/Bake Selected Location`, logs every issue and never half-bakes); `BarrowsEndTileAuthor` also builds `DoorTileObject.prefab`, which every Door tile now uses (the tiles were regenerated).
**Tests:** toolkit EditMode `LocationBakerTests` (21: grid, origin and blocked cells; a root away from the origin; painting that does not start at cell 0; several tilemaps; wall merging and box layers/straddle; real `Physics.Linecast` blocked by a wall and clear over floor; a router-style ray that ignores obstacles hits the ground plane even over a wall; ground collider coverage; camera bounds written without disturbing spawn points; every error class refuses and writes nothing; warnings do not stop a bake; reachability through doors; idempotent re-bake keeping hand-placed objects; a refused re-bake leaves the last good bake; 200 random shapes cover exactly once; corner-cutting rule) and 1 validator test; toolkit PlayMode `TileDoorTests` (4, a real Tilemap: blocks its cell, anchor south, opens/closes and hides its visual, north fallback, no-neighbour warning and no-grid error) and a perf guard in `SemanticTilePlayTests`. BarrowsEnd EditMode `LocationBakeContentTests` (2: layers; a room baked from REAL generated tiles with the real layers and a real line-of-sight check) and a door-prefab test in `TileContentTests`.
**Suites:** toolkit EditMode 968/968 (946 + 22); toolkit PlayMode 410 passed + 3 skipped (405 + 5); BarrowsEnd EditMode 64/64 (61 + 3); BarrowsEnd PlayMode 22/22.
**Mutation checks (15, all caught):** walkability ignoring ground; door blocked in the provider; wall boxes above the ground plane; layers swapped; no fail-closed; camera bounds not written; corner cutting allowed; merger not growing vertically; old Baked kept; travel-destination check removed; origin at cell 0; door never closed; approach order wrong; interactable not configured (this one first SURVIVED, exposing that the prefab already carried the anchor, so a range assertion was added).

**Findings:**
- **Perf:** 2400 prop objects instantiate in about 86 ms and every static sorter switches itself off, so the ~2400-prop worry from the plan is closed.
- An open door simply hides its visual (the floor shows through); a separate open-door sprite is content work for later.
- The 2.6 test `HitFlashesRedThenReturnsToNormal` was a timing flake under load (two slow frames, each capped at 0.1 s, could end its 0.2 s flash before the assertion); its flash is now 1 s.
- The baker is not wired to any scene yet: the two demo locations and the shell that loads them are 3.4. Paint-then-bake is proven on synthetic maps and on real tiles in an EditMode room.
- Not committed.

## Task 3.4 detailed design (2026-09-20)
Ported from `TestProject/Assets/AdventureSlice/AdventureSliceBootstrap.cs` (Core scene + additively loaded locations), minus dialogue/quest/inventory UI (Phase 5/6). New runtime assembly `BarrowsEnd.Shell`:
- **`ShellBootstrap`** (Core scene): starts the ruleset-aware session; builds `WorldTransitionGate`, `SceneTravelRunner`, `SceneTravelCoordinator` and `SessionLoadCoordinator` all sharing one `GridPathfindingService`; loads the starting location additively, activates it and its grid; spawns the 4 party members into the Core scene with FIXED ids (so a save survives a process restart); places them in formation at the starting `SceneTravelDestination`; wires the input/selection/movement/interaction/death/equipment services by reusing `GateSessionServices` and `GateCommandSource` (the gate's session-rebind class, now the real one; renamed when the gate is retired); prepares `LocationContent` (NPC spawns, camera bounds); wires every `TravelInteractionAdapter` (Runner and Gate cannot be authored into a location scene) on boot and on every scene load. `SaveNow` / `LoadNow` use the real `SaveService` with the ACTIVE location's scene path and `SessionLoadCoordinator.LoadCoroutine(..., coreScene, ...)` (the cross-scene path); after a load every session-bound object is rebuilt (travel coordinator, services, camera follow, bounds, travel adapters).
- **Two demo locations** (`Assets/Scenes/Locations/Town_Slice.unity`, `Cave_Slice.unity`), authored by `BarrowsEndLocationAuthor` from text layouts using the REAL generated tiles, baked with `BarrowsEndLocationBake`, each with arrival `SceneTravelDestination` anchors and a travel point (collider + `WorldInteractable` + `TravelInteractionAdapter`) leading to the other.
- **`Core.unity`** (`BarrowsEndCoreSceneAuthor`): the authored camera rig prefab, `InputSystemCommandSource` (raycast camera = the rig's, `cameraRig` set so zoom works, hittable mask = `BarrowsEndLayers.RouterMask`), `ShellBootstrap`, a small `ShellPanel` (save, load, zoom, status). All scenes go in the build settings.
- **Tests:** EditMode content tests on both location scenes; PlayMode `ShellTests` driving the REAL click stack: boot, walk, click the travel point (approach, travel, arrival in formation, grid/camera bounds swapped), travel back, save in the cave then load from the town (cross-scene), everything rebound and still clickable; no LogError.

## Task 3.4 Results (built 2026-09-20)

**BarrowsEnd (no toolkit change was needed):** new runtime assembly `BarrowsEnd.Shell`: `ShellBootstrap` (Core scene composition root: ruleset-aware session, `WorldTransitionGate`, `SceneTravelRunner`, `SceneTravelCoordinator` and `SessionLoadCoordinator` sharing one `GridPathfindingService`; loads the starting location additively and activates its grid; spawns the 4 party members into the Core scene with FIXED ids `ba00...01..04`; forms them up at the starting `SceneTravelDestination`; reuses `GateSessionServices` and `GateCommandSource` for input/selection/movement/interaction/death/equipment; wires every `TravelInteractionAdapter` at boot and on each scene load; `SaveNow` with the active location's scene path and `LoadNow` through the cross-scene `LoadCoroutine`, after which every session-bound object is rebuilt) and `ShellPanel` (save, load, zoom, current scene). `BarrowsEndLocationAuthor` (menu `Barrows End/Content/Author Demo Locations`) builds `Assets/Scenes/Locations/Town_Slice.unity` (30x20: road, paved square, pond, a wall-ringed house with a door, palisade, trees, barrels, a cave-entrance travel point) and `Cave_Slice.unity` (24x16: walls, dirt track, pool, rocks, mushrooms, bones, an exit travel point) from code with the REAL generated tiles, and BAKES each (it throws if the bake is refused). `BarrowsEndCoreSceneAuthor` (menu `Barrows End/Content/Author Core Scene`) builds `Core.unity` (authored camera rig prefab, `InputSystemCommandSource` with the camera and rig wired and the router mask that excludes wall boxes, `ShellBootstrap`, `ShellPanel`) and puts Core first in the build settings with both locations, the gate and the look spike.
**Tests:** BarrowsEnd EditMode `LocationSceneContentTests` (6: bake output and bounds and layers, re-bake with no warnings, four walkable formation slots at every arrival anchor, travel points wired to real destinations in the other scene, the single door tile, Core wiring and build settings) and PlayMode `ShellTests` (5, through the REAL click stack: boot into the town with fixed ids, the party in the Core scene, the town's grid and bounds active and the camera clamped to the TOWN's edge; click-to-walk with the camera following; the house door blocking until clicked open then walking in; clicking the travel point takes the party to the cave and back with the grid, camera bounds and formation swapped and the other scene unloaded; save in the cave, travel back, load: a cross-scene load returns to the cave, a brand-new session, consumers rebuilt once, positions restored, movement and travel still work) and `ShellCaptureTests` (1, renders the town and cave through the shell camera: both looked at, correct).
**Suites:** BarrowsEnd EditMode 70/70 (64 + 6); BarrowsEnd PlayMode 28/28 (22 + 5 + 1 capture). The toolkit is unchanged, so its suites were not re-run.
**Mutation checks (12, all caught):** travel adapters not wired; travel coordinator not rebuilt after a load; services not rebound after a load; no party placement; party left in the location scene; bounds not set (in either place; the boot-time duplicate was removed); save without a scene path; random party ids; grid not activated at boot; plus the earlier hittable-mask case.

**Findings:**
- The hittable mask was authored as 0 (the router would have hit nothing): `LayerMask.m_Bits` is unsigned, so a signed `~ObstacleMask` clamped to 0. Caught by the new content test; the author now sets `uintValue` and the test pins the exact value.
- The first camera-bounds assertion was too weak (the town and cave bounds were close enough that a stale clamp passed); it now pushes the camera far past the corner and checks it stops at the ACTIVE map's edge.
- At scale 3 on 1080p the whole 30x20 town fits on screen, so the camera centres on it and never scrolls; scrolling is only exercised by larger maps.
- `GateSessionServices` and `GateCommandSource` (named for the throwaway gate) are the shell's real services layer; rename when the gate is retired.
- Not covered: NPCs, dialogue, quests, stores and any HUD (Phase 5/6); the demo maps use placeholder tile choices (the house walls are a flat grey ring, the cave arch is small); real mouse feel and the panel buttons need a hand check at Gate C.
- Not committed.

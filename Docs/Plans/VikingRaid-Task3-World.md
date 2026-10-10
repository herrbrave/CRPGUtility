# Viking Raid -- Task 3: the world (design v2: Opus-reviewed; Section 9 SUPERSEDES conflicting text above)

Status: DESIGN v2 -- reviewed by Opus (20 findings: 3 blockers, 10 major, 7 minor; the two skybox/layer blockers and the URP-settings finding were re-verified against the files).
Process: design -> Opus hostile review -> user -> implement tests-first. Nothing is committed unless the user asks.

## 1. Goal and non-goals

One Unity scene, `Assets/Scenes/VikingRaid.unity`, produced by ONE idempotent Editor authoring script from ONE layout table, that gives Task 4 everything it needs and
gives the user something good to look at:

* a pine-forest road at dusk, ~280 m long, leading from a start clearing through two ambush clearings to a goblin war camp (palisade gate, then the camp heart);
* a baked NavMesh that is walkable only on the road, the four fight arenas and the camp interior (forest off the road is solid);
* per-encounter data anchors (party stage points, goblin spawn points, trigger volumes, camera hints, checkpoints) as plain transforms under a `Anchors` root;
* lighting that makes the Viking steel and the goblin armour look like metal, not black cutouts (Task 1 left this open);
* ambience FX (fire, embers, fog, motes) at the right places.

Non-goals (Task 4): spawning heroes/goblins, the exploration controller, camera rig, HUD, the encounter state machine, the victory screen. Task 3 DOES decide where those things stand.

## 2. Facts that constrain the design

* Simulated encounter geometry (Task 2 balance, `BalanceSimulationTests`): party abreast on z = -3,-1,+1,+3 at x = 0; goblins ~20 m in front (x = 20..22, z spread 2.5 m). Goblin move 6 m (Troll/King 4 m), hero move 8 m, shortbow 12 m, longbow 14 m, bolt 12 m.
  The measured win rates are only valid if the in-game fights START from (nearly) that geometry and are fought in open ground. So each arena must be an open rectangle at least **44 m long x 22 m wide** with no solid obstacle inside, and the game must put the party on its stage points before combat starts.
* Agents: heroes `NavMeshAgent` radius 0.35 (speed 4.0), goblins 0.35 (3.0), Troll 0.55, King default; the baked surface uses ONE agent type (humanoid radius 0.5, height 1.8, step 0.4, slope 45). Min walkable gap anywhere in play space must be >= 1.6 m.
* Toolkit input picks click targets with a `Physics.Raycast` against `hittableMask` (PlayerCommandRouter / InputSystemCommandSource): ground and characters must be hittable, canopies must NOT block clicks.
* `PhysicsLineOfSightProvider(obstructionMask)` is a `Physics.Linecast` with a mask the game chooses: ranged `requireLineOfSight=true`. Trunks, rocks, walls, buildings must block; ground must not (it would block shots at a 1.4 m eye height on any bump); canopies must not exist as colliders.
* Synty shaders render fine under URP 17.6; their weapon metals went near-black with no reflection source. The PolygonGeneric/Alpine packs ship skybox materials (`Skybox_Mat_01`, `Skybox_Mat_03`, War Camp `Skybox_01`).
* Batch Unity cannot run with the user's Editor open; RenderTexture captures need a graphics device (no `-nographics`), a discarded warm-up frame, and `asyncShaderCompilation=false`.

## 3. Layout (single source of truth: `Assets/Game/WorldLayout.cs`)

A static table in the game assembly (like `GameDesign`), read by the authoring script AND by the tests, so numbers cannot drift. World axes: the road runs mostly +X with gentle curves in Z; Y is flat (ground at y = 0; no terrain, no height changes -- tactical combat on slopes is out of scope and flat makes NavMesh/LoS/cover reasoning trivial).

Road centreline = a polyline with 12 control points smoothed by Catmull-Rom, sampled every 2 m. Stations along it (distance in metres from the start):

| Station | s (m) | What |
|---|---|---|
| Start clearing | 0 | disc r=14 at the origin; party spawn; checkpoint 0 |
| Woods A | 14-60 | road 7 m wide, dense pines both sides, rocks, stumps |
| E1 arena (3 Grunts) | 60-104 | 44x22 clearing, road enters from the west edge; checkpoint 1 at the west edge |
| Woods B | 104-150 | road narrows to 5 m for a "squeeze", fallen log, rock cluster |
| E2 arena (2 Warriors + Archer) | 150-194 | 44x22 clearing with 3-4 boulders at the EDGES (no cover in the interior); checkpoint 2 |
| Woods C | 194-224 | road widens, bones and spiked stakes (War Camp pieces) start to appear, fires visible ahead |
| E3 arena (camp gate) | 224-268 | 44x22 forecourt; the PALISADE + GATE close the east end; goblins stand in front of the gate; checkpoint 3 |
| Camp heart (E4) | 268-320 | gate opening (4 m wide, stays open) into a walled yard 44x30: HQ at the far end, tents and fire pits along the walls, Troll/King/Shaman by the central fire; checkpoint 4 |

Total ~320 m (about 80 s at the heroes' jog; the time is spent in the four fights). Arena interiors are tree-free and obstacle-free; boulders/crates sit within 2 m of the arena rectangle's border only.

Each encounter has these anchors (generated from the table): `Party_Stage` (4 points abreast, 2 m apart, centred on the arena's west third), `Goblin_Spawn_i` (positions replicating the simulation: first goblin 20 m east of the party line, 2 m column offset, 2.5 m lateral pitch, facing west), `Trigger` (a box volume across the road ~26 m west of the goblin line, isTrigger, on a `Trigger` layer), `Checkpoint` (a point on the road before the trigger), `CameraHint` (a point/yaw for the tactical camera). A final `Victory` trigger sits at the HQ.

## 4. Construction (one authoring script, `WorldAuthor`, menu `Viking Raid/Task 3/Author World`)

Idempotent: deletes the scene's generated roots and rebuilds from the table with a fixed seed (Random(20261005) per feature) so reruns are bit-identical.

* **Layers/tags** (set in `TagManager` by the script, like project setup): `Ground`(8), `Obstacle`(9), `Trigger`(10), `Character`(11). Mask constants live in `WorldLayout` (`HittableMask = Ground|Character`, `SightBlockerMask = Obstacle`, `NavMeshMask = Ground|Obstacle`).
* **Ground.** Walkable ground is ONLY the corridor: a procedural road-ribbon mesh + the four arena rectangles + start disc + camp yard, welded as flat meshes (`MeshCollider`, layer Ground). Outside it a huge flat visual plane (no collider) at y = -0.02 carries the forest-floor colour so there is no void. Materials: URP/Lit solid colours created by the script (dusk-tinted: grass, packed dirt road, trampled camp mud) plus Synty ground decals/dirt pieces scattered for variety; flat colours suit the low-poly style and avoid hunting atlas swatches blind.
* **Forest.** Poisson-disc scatter of `PolygonNatureBiomes` pines (`SM_Env_Pine_01-05`, `NoLeaves`) and Viking Realm pines (`SM_Env_Tree_Pine_01-06`, groups) in a band 60 m either side of the road, excluded from the corridor + a 3 m verge and the whole arena/camp footprints; density higher near the road edge to make walls of trees, thinning outward; Alpine `Background_Trees`/`MountainRange` + `Rock_Cliff` ring the horizon beyond 90 m. Each trunk gets a **CapsuleCollider (radius 0.4, height 4, layer Obstacle) trunk-only**; canopies, bushes, ferns, grass tufts, pebbles are collider-free. Rocks (`SM_Env_Rock_*`) get a simplified Box/Sphere collider on Obstacle. ~450 pines, all `StaticEditorFlags` Batching + Occludee, SRP batcher (no GPU instancing authoring), no LODs (low-poly).
* **Camp.** War Camp pieces: `Log_Wall`/`Wall_Stake` segments forming the palisade across the forecourt end and around the yard, `SM_Bld_Gate_01` at the opening, `Tower_01/02` flanking the gate, `HeadQuarters_01` (+ `Leader` tent), 5-6 `Tent_*`, `Fire_Pit`/`Camp_Fire_01`, `Brazier`, torches, banners/flags, `Defence_Spikes`, bones/skulls, crates/barrels/racks, `Gibbet_Cage`. Building colliders (box approximations, layer Obstacle; the prefabs' mesh colliders are stripped and replaced because a MeshCollider-per-tent bloats the bake and blocks LoS through open tent flaps unpredictably) so the yard is solid except its footprint.
* **Lighting.** Skybox `Skybox_Mat_03` (Alpine dusk) or whichever of the three looks best in the captures; one directional light (warm, low, 8 deg above the horizon, soft shadows, shadow distance 70 m); ambient trilight (cool sky/blue-green equator/dark ground); exponential-squared fog (cool blue, density ~0.012) deepening into the woods, thinned at the camp which is lit by warm point lights (<= 6 per area; URP additional lights limit 8/object) with `FX_Fire_*`, `FX_Embers_01`, `FX_Smoke_*`, `FX_Fog_Small_01`; woods get `FX_Dust_Blowing_Soft_01` / `Alpine FX_Leaves_01` motes. **Reflection source:** `RenderSettings.defaultReflectionMode = Skybox` plus a baked `ReflectionProbe` (box 320x40x120 over the road, resolution 128, baked in batch via `Lightmapping.BakeReflectionProbe`, saved as an asset) so metals pick up cool sky + warm ground. This is verified by a close-up capture of a hero blade/pauldron, not assumed.
* **NavMesh.** `NavMeshSurface` on a `World` root: `collectObjects = All`, `useGeometry = PhysicsColliders`, `layerMask = Ground|Obstacle`, humanoid agent type as above, tile size default, `minRegionArea 2`. Trunk capsules cut holes; no `NavMeshObstacle` anywhere. Baked data saved next to the scene.
* **Scene wiring Task 4 will rely on:** `Anchors` root as in Section 3; `World/Lighting/FX` roots; the scene in Build Settings; a `WorldAnchors` MonoBehaviour on `Anchors` exposing typed arrays so Task 4 does not `Find` by string.

## 5. Verification (tests first; batch EditMode + PlayMode)

EditMode (pure data, run in seconds): `WorldLayoutTests` -- stations are ordered and non-overlapping; every arena >= 44x22 and inside the corridor; trigger boxes do not overlap another arena; goblin spawns reproduce the simulation geometry (party-line to first goblin = 20 m, lateral pitch 2.5 m, 4 party points 2 m apart); checkpoint s < trigger s < goblin s for every encounter.
PlayMode (real scene loaded, real NavMesh): `WorldSceneTests` --
1. the scene loads, has exactly one `NavMeshSurface`, anchors resolve, layers exist;
2. every anchor (party stage x16, goblin spawn x~13, checkpoints, victory) is within 0.3 m of the NavMesh (`SamplePosition`);
3. `NavMesh.CalculatePath` start -> each checkpoint -> each arena -> camp heart is `PathComplete`, and the path length is within 1.15x the polyline length (no huge detours);
4. each arena rectangle is open: a 1 m grid of probes is >= 97% on-NavMesh and no obstacle collider centre lies inside it (the sim's open-field assumption);
5. min corridor gap: sample the road ribbon every 2 m, `NavMesh.FindClosestEdge` distance from the centreline >= 0.8 (gap >= 1.6 m);
6. clicks are not eaten by canopies: no collider on the Ground/Obstacle layers sits above y = 4.5 (canopies collider-free) and a downward ray from 30 m above any road point hits layer Ground first;
7. line of sight works as designed: straight east-west lines across each arena at eye height are unobstructed, and a line through a trunk/wall is blocked (`PhysicsLineOfSightProvider(SightBlockerMask)`);
8. no goblin spawn point is within 1.2 m of a collider; party stage points likewise;
9. the party cannot leave the corridor: from the start, `NavMesh.SamplePosition(point 25 m off the road)` fails.
Mutation checks: drop trunk colliders -> test 3/5/7 must fail; widen a trigger into the next arena -> layout test; move a spawn into a wall -> test 8; put an Obstacle collider in an arena -> test 4.

Visual review (RenderTexture captures, no `-nographics`): `world_overview.png` (top-down orthographic of the whole road with anchors drawn), `view_start.png`, `view_e1.png`, `view_e2.png`, `view_gate.png`, `view_camp.png` (each from the encounter's `CameraHint` with the goblins and party TEMPORARILY instantiated at their anchors -- not saved in the scene), `closeup_metal.png` (a hero in the camp light), `view_forest_walk.png` (eye-level from the road). Cheap self-checks first: no magenta, mean luminance of each view in a sane band (not black, not blown out), the metal close-up's brightest-region contrast vs the Task 1 baseline.
GATE (user): review the captures.

## 6. Risks and how this design answers them

| Risk | Mitigation |
|---|---|
| NavMesh bake time / quality with ~450 trunk capsules | trunks only; corridor-only ground keeps the surface small; measured in the first bake, fall back to larger tiles |
| Sim fidelity lost to in-game start geometry | arenas sized to the sim, party stage anchors, Task 4 walks the party to them before combat |
| Dark metals | skybox + baked reflection probe, verified by capture |
| Authoring script scale (~150 prefab refs, hand-typed names) | names are verified to exist by a test; one table; missing = failing test, never a null at runtime |
| Visual quality unprovable by unit tests | the capture gate, with cheap numeric pre-checks |
| Scene is large for git | scene + generated NavMesh/probe assets are text/binary but modest; Synty is gitignored and prefabs are referenced by GUID (a clone without Synty shows missing prefabs, same as Task 1 prefabs) |

## 7. Out of scope / decisions deferred

Height variation, water, destructibles, day/night, sound, a minimap, LODs, occlusion baking, wind (Synty wind shader parameters left at defaults), the gate opening/closing animation (the gate prefab is static and open).

## 8. Questions for the reviewer

* Is "flat world, corridor-only NavMesh" the cheapest correct choice, or will flat read as a stage set? Is there a cheap, safe way to add visual height (cliffs/hills as non-walkable decor) that I have missed?
* Is the arena size (44x22) enough for the measured geometry plus a free-moving party, and is 44 m x 4 arenas + 8 woods stretches too much or too little scene for the "10-15 min" goal?
* Are the proposed layers/masks sufficient for the toolkit's input + LoS + NavMesh? Does anything in `PlayerCommandRouter`/`AbilityTargetingGate`/`TacticalEncounterController` (read them) assume ground on a particular layer, a camera component, or a `NavMeshSurface` location I will violate?
* Baked ReflectionProbe in batch mode: is `Lightmapping.BakeReflectionProbe` reliable without a window, or should the cubemap be rendered from the skybox in code (`Camera.RenderToCubemap`) and assigned to `RenderSettings.customReflectionTexture`?
* Anything in the test list that cannot fail (vacuous), or missing coverage that will bite in Task 4?


## 9. Opus review record and v2 decisions (2026-10-05) -- SUPERSEDES Sections 3-8 where they conflict

Review verdict: the design was sound in shape but wrong in eleven places that would have failed silently. Every item below is adopted unless marked otherwise.

### 9.1 Blockers (all three were real)
1. **Characters are on layer Default (verified: all 4 transforms of Hero_Bjorn are `m_Layer: 0`).** The toolkit raycasts only `hittableMask` for click-to-move AND for the ability targeting gate, so with `Ground|Character` no goblin could ever be clicked. Fix: `CharacterRosterAuthor` puts every roster prefab (recursively) on the new `Character` layer, `RosterPrefabTests` asserts it, and `HittableMask = Ground|Character|Scenery`. Never `~0` (Synty colliders stay on Default).
2. **None of the "skybox" materials is a skybox (verified: `Skybox_Mat_03` uses `PolygonGeneric/Shaders/SkyDome.shadergraph`, an Unlit MESH shader; War Camp `Skybox_01` is a skydome mesh material).** `RenderSettings.skybox` = built-in `Skybox/Procedural` tuned to dusk (it drives the horizon AND the reflection probe). The Synty dome is dropped. Fog colour = the sky's horizon colour (an unmatched exp2 fog turns a distant dome into a flat wall).
3. **The E4 yard broke the open-arena rule.** A fire pit inside the rectangle is solid, and 52 m of road cannot hold a 44 m arena plus the HQ. New camp yard: **58 m deep x 34 m wide**; the E4 arena is the western 44 x 22 of it (open), the HQ/boss fire/tents sit in the eastern 12 m and along the 6 m side strips OUTSIDE the arena rectangle. Footprints are measured from renderer bounds and asserted.

### 9.2 Major decisions
4. **Gate:** `SM_Bld_Gate_01` has door children at x=+-3.81 in the CLOSED pose (opening ~7.6 m, each door a MeshCollider). The script rotates the doors open, strips all colliders, adds one box per post and a lintel box above 2.0 m (agent height), so the passage stays clear; a PlayMode path test through the gate proves it.
5. **Trunks never touch the walkable surface** (trees are excluded from corridor + verge), so they do not cut the NavMesh; their only job is line of sight around bends and visual density. Keep trunk capsules only within 10 m of the road; beyond that trees are collider-free. The old mutation claim is withdrawn (dropping trunks fails only the LoS test).
6. **Roofs / rock tops become NavMesh islands** (Obstacle colliders are collected as geometry). Every Obstacle root gets a `NavMeshModifier` (override area = Not Walkable, apply to children); NavMesh collection is additionally restricted to bounds around the corridor (`collectObjects = Volume`). A test asserts every triangulation vertex is inside the corridor polygon (+0.1 m) at y < 0.3.
7. **Synty prefabs carry their own colliders and ambiguous names.** The script strips EVERY collider from each instantiated Synty prefab (`GetComponentsInChildren<Collider>(true)`), builds a handful of project-local prefab variants once (stripped, trunk capsule added, layer/static flags set) under `Assets/World/Prefabs/` and instantiates those (avoids hundreds of per-instance overrides), uses `Pine_Group_*` only in the collider-free outer band, and the layout table stores FULL asset paths (duplicate names exist: `FX_Fire_01` x4, `SM_Prop_Fire_Pit_01` x2, `Brazier_01` x2, `FX_Leaves_01` x2). `Pine_NoLeaves_01` is one prefab, not a variant per pine. A test verifies every path resolves.
8. **Open-arena test redone:** each arena ground is its rectangle +1 m so the eroded mesh still covers the rectangle; require 100% of probes on the inset rectangle with horizontal sample distance < 0.05, plus `NavMesh.Raycast` along every row and column (the straight moves the sim assumes), plus `Physics.OverlapBox(rect, Obstacle)` empty. Boulders sit OUTSIDE the rectangle.
9. **Trigger/checkpoint geometry:** each trigger is a box in the road 8-10 m WEST of its arena west edge, at least as wide as the walkable width + 2 m (cannot be walked around); each checkpoint is >= 8 m west of its trigger with 4 explicit spawn anchors (`NavMeshCharacterMotor.Warp` refuses points > 1 m off the mesh). Tests: trigger spans every NavMesh point at its station; spawn slots on the mesh and >= 3 m outside every trigger; the path checkpoint k -> stage k crosses only trigger k. Task 4 must ignore triggers during combat (E4 trigger sits at the gate, reachable mid-E3). The party walks to the stage anchors on trigger so the sim geometry holds.
10. **The URP asset does not match the lighting plan (verified: additional lights per object 4, 1 shadow cascade, soft shadows off, Forward).** Switch the renderer to **Forward+**, soft shadows on, 3 cascades, shadow distance 70 m, in the project-setup script / world script (it owns the URP asset already).
11. **Baked probe would capture only sky:** flag ground, trees and camp `ReflectionProbeStatic`; `asyncShaderCompilation=false` + a warm-up frame before baking; probe centred at ~2 m height inside the road (not at 20 m in the canopy). `Lightmapping.BakeReflectionProbe` is the chosen method (the reviewer confirms it works in batch with a graphics device).
12. **Road geometry:** straight segments joined with matched tangents, each arena a straight section along its own local axes (yaw = road tangent); spawns, LoS lines and triggers use arena-local axes. Station `s` values are computed from arc length, never typed. The layout table is code, not typed numbers.
13. **Test plan corrections.** Path test: `PathComplete` and last corner within 0.1 m of target (drop the 1.15x ratio, which cannot fail). Anchor test: horizontal distance <= 0.05 and `FindClosestEdge` >= 0.55 (Troll). Click test: from each `CameraHint`, raycast `HittableMask` over an arena grid and require the first hit to be Ground at y~0 (replaces the wrong "no collider above 4.5 m"). Spawn-clear test uses `Physics.CheckCapsule(..., Obstacle)`. Off-road test has an explicit radius. 14 goblin spawns (3+3+5+3). New tests: triangulation vertices inside the corridor; every collider is authored by the script and on Ground/Obstacle/Trigger/Character/Scenery; **one `WorldLayout` function for the encounter offsets that `BalanceSimulationTests` also calls** (sim and world cannot drift); camera framing: stage and spawn points inside the Task 4 viewport at its zoom/pitch with no tree `Renderer.bounds` on the camera-to-chest ray.

### 9.3 Minor decisions
14. NavMeshData is built in memory only: the script calls `AssetDatabase.CreateAsset` into `Assets/Scenes/VikingRaid/`, and saves procedural meshes as assets. Reruns are not bit-identical (fileIDs change): tests compare layout equivalence, not bytes.
15. Sun elevation 18 deg (8 deg gave N.L ~0.14 and 100 m shadows), azimuth along the road.
16. Outside the corridor a `Scenery` layer collider (in `HittableMask`, NOT in `NavMeshMask`) so off-road clicks hit something and `PartyMovementService` snaps within 5 m.
17. Fog is global: no "deeper in the woods" without a runtime driver; use particle fog (`FX_Fog_Small/Big`) for local depth and a uniform exp2 for the rest.
18. Tree count 1,200-2,000 (the near-road band alone needs ~900 at 3 m spacing), mostly collider-free, keep the LODGroups, rely on Forward+/SRP batcher.
19. Captures are taken in PlayMode (edit-mode characters stay in bind pose; particles do not simulate) or after sampling the idle clip and `ParticleSystem.Simulate`.
20. Agent/bake settings stay at the PROJECT defaults (`NavMeshAreas`: height 2, climb 0.75), not the invented 1.8/0.4. "Victory trigger at the HQ" is withdrawn: victory = last boss dead (Task 4); keep a `Victory_Camera` anchor only.

### 9.4 Additions from the reviewer answers
* **Visual height without touching the walkable surface:** a procedural heightfield OUTSIDE the corridor (y = 0 at the verge rising to 3-8 m by 40 m out, Perlin noise) so the road reads as a sunken track; `Scenery` collider; trees placed at its height; `Rock_Cliff` banks 10-30 m out; Alpine MountainRange beyond.
* Arena 44x22 is enough; scene length is right (~13-15 min estimated: 2.5/2.2/4.4/4.7 min combat + walking); do not add woods.
* Layers: Ground 8, Obstacle 9, Trigger 10, Character 11, Scenery 12 (hittable, not navigable).

### 9.5 Revised build order
0. Roster fix (Character layer) + `RosterPrefabTests`; URP asset (Forward+, shadows) in the project-setup script.
1. `WorldLayout` (pure C#: road, arenas, yard, anchors, offsets function, asset-path table) + EditMode `WorldLayoutTests`; make `BalanceSimulationTests` use the shared offsets function (the sim still passes unchanged).
2. `WorldAuthor`: ground, road, collider-stripped project prefab variants, forest, camp, lighting, probe, NavMesh asset, anchors; menu + batch entry.
3. PlayMode `WorldSceneTests` (the corrected list) + mutation checks.
4. PlayMode capture pass (overview, 4 encounter views, forest walk, metal close-up) + cheap numeric checks; append results; GATE (user): review the captures.

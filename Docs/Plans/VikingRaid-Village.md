# Viking Raid: the village scene (toolkit-driven, NPC-ready)

Status: v1 DONE 2026-10-06. A sandbox for the in-editor scene-configuration tooling: a real, walkable village on the toolkit's own stack, with NPC spawn points that are data in the scene.

## What it is

`Assets/Scenes/VikingVillage.unity` is a copy of the Synty Viking Realm `Demo` scene (the original is never touched), set up by **`Viking Raid/Village/1. Author Village Scene`**
(`Assets/Editor/Village/VillageSceneAuthor.cs`). Press Play: the four heroes stand on the waterfront by the docks, three named villagers (Elder Hrolf, Hild, Orm) stand near them,
click-to-move works, the toolkit camera follows the party (WASD pan, Q/E rotate, wheel zoom, Space recenter).

| Piece | Where | Toolkit type |
|---|---|---|
| Composition root (exploration only, no combat) | `CRPG/Bootstrap` | `VillageBootstrap` (project-local, `Assets/Village/`) |
| NPC spawn points, stores, loot, camera bounds | `CRPG/Location` | `LocationContent` + `NpcSpawnPoint` |
| NPC anchors (move these) | `CRPG/NPCs/Anchor_*` | plain transforms |
| Where the party spawns (move this) | `CRPG/PartyStart` | plain transform |
| Camera | `CRPG/CameraRig` (the demo's own camera moved under it) | `IsometricCameraRig` + `PartyCameraFollowBehaviour`, settings `Assets/Village/VillageCamera.asset` |
| Input | `CRPG/Input` | `InputSystemCommandSource`, hittable mask = Ground + Character + Scenery |
| Click filtering | runtime | `PartyCommandFilter` (a click on a villager is not a party selection) |
| Walking | runtime | `PartyExplorationController` with `facingSource` (the order's facing is the travel direction) |
| NavMesh | `CRPG/NavMesh`, data in `Assets/Scenes/VikingVillage/NavMesh.asset` | `NavMeshSurface`, physics colliders on Ground + Obstacle |

Villagers: `Assets/Characters/Villagers/*.prefab` (`Viking Raid/Village/Author Villager Characters`: unarmed Viking Realm townsfolk on the hero animation set, in their own folder because
`RosterPrefabTests` scans the combat roster's folder) and `Assets/Content/Village/Npcs/*.asset` (`Viking Raid/Village/Author Villager Content`: flat character sheets, Hit Points plus the four
turn resources so they can be dragged into a fight later). They are deliberately not in the ruleset's content list (`GameContentAuthor` rewrites that list on every run).

## How the demo scene was made walkable (the rules the author applies)

The demo is a modular kit with almost no physics: about 1,000 ground tiles, 24 buildings of 65-300 pieces each, 460 props, 300 trees (these already carry colliders), 860 rocks.

- **Ground layer** (walkable, clickable): ground and road tiles, docks and piers. **Obstacle layer** (carves the NavMesh, tops forced Not Walkable): buildings, props, fences, boats, rocks (not pebbles), stone walls and pillars.
  Foliage, grass, water, clouds and the background are left alone. A piece with no collider gets a mesh collider (LOD0 only); 519 were added.
- **The ground group reaches 2.6 km** (huge background planes), so the author measures the village by its own renderers (about 362 x 234 m with a 20 m margin) for camera bounds and every search.
- **PartyStart** is the walkable point nearest the buildings' centre that has room around it and is connected to the main walkable ground (within 90% of the best reach). **NPC anchors** are the nearest open, reachable points
  about 9 m from the party at -50, 0, +50 degrees. Both are created once: after that the author never moves them.
- Re-running the menu item is safe (it re-bakes the NavMesh and fills in anything missing). **`Rebuild Scene From The Demo`** throws the copy away and starts over (asks first).

## Verified

`VillageSceneTests` (6, PlayMode, real router and real hittable mask): boots with 4 heroes and every spawn point's NPC on the NavMesh and no logged errors; a click walks the party to the point and the order's facing
points along the travel direction; clicking a villager does not throw and leaves the party selected; the camera starts on and follows the party; 350 of 400 sample points across the village are reachable from the start.
`VillageCaptureTests` writes `VikingRaid_Logs/spikes/village_{start,top,overview,around_start,navmesh}.png` (fog is switched off for the wide shots: the demo's fog hides the whole village from 150 m up).
Totals now: 87 EditMode, 62 PlayMode, all green.

## Findings worth keeping

- The demo's village sits on a hill: a low camera ends up behind terrain and a click then resolves to the hill in front of the camera. The village camera defaults to zoom 32 / pitch 55 (the toolkit's defaults were 12 / 55);
  the click-helper in the tests confirms a point is truly visible before using it.
- The demo scene's two groups (`Village`, `Environment`) are made of thousands of prefab instances: layers and colliders on them are prefab overrides, so the author calls `RecordPrefabInstancePropertyModifications`.
- In the Editor `GetComponent<T>()` on a missing component returns a "fake null" that passes a C# null check and throws on use: `TryGetComponent` (hit again in the capture test).

## Not done (the point of the next step)

No dialogue, quests, stores or loot yet; NPCs just stand (no schedules or wandering); no HUD; the villagers are not in the ruleset; lighting is the demo's baked lighting.
The scene-configuration tooling is the next piece of work: candidates are an NPC placement tool (palette of NpcDefinitions, place on the NavMesh with a preview model, writes the `NpcSpawnPoint`),
a validator for a location (unique spawn ids, anchors on the NavMesh and reachable from the party start, definitions with a prefab), a custom inspector for `LocationContent`, and, because the village is a modular kit,
a piece-painting tool for the ground and props.

# Phase 1 Detailed Design: Grid Movement, Pathfinding, and the NavMesh-Bypass Seams

> Status: pre-implementation. Companion to `Docs/Plans/2D-Campaign-Implementation.md` (the master plan) — this
> document fills in Phase 1's tasks with real, implementation-ready specifics.
>
> **v2 note:** v1 got a second Opus review that found the motor design's central claim — "every `SetDestination`
> call this motor receives is already straight-line-safe, so it never needs to path internally" — is false for
> two major production callers (`PartyExplorationController`'s ordinary click-to-move and
> `InteractionController`'s walk-to-interact), both of which call `IMovable.SetDestination` directly with an
> arbitrary, unplanned destination, never through `MoveActionExecution` at all. It also found the design never
> addressed who owns a `WalkabilityGrid` per scene or how anything re-binds to it across additive scene travel
> — a real structural gap, not a wording issue. This version rebuilds the motor/pathfinder relationship and adds
> the missing per-scene ownership design; it does not patch v1's assumptions.

## Scope

Phase 0 (master plan) decided: 8-connected grid with true Euclidean step cost, one-character-per-cell
occupancy, seam the NavMesh-bypass call sites rather than scope around them. **Correction from the review**:
there are four real bypass call sites across three files, not five — `TravelInteractionAdapter` never touches
NavMesh directly (it only delegates to `SceneTravelCoordinator`, which is already one of the four). Both this
document and the master plan should read "four call sites" going forward.

## New types

### `CRPG.Navigation.Grid.GridCoordinate` (new, `Runtime/Navigation/Grid/GridCoordinate.cs`)

Corrected location from v1 (was mis-specified as `CRPG.Core`) — nothing in `CRPG.Core` needs a grid concept;
this belongs in `CRPG.Navigation` alongside the rest of the movement-backend code.

```csharp
public readonly struct GridCoordinate : IEquatable<GridCoordinate>
{
    public int X { get; }
    public int Z { get; } // maps to world Z (ground plane), not world Y (elevation) -- unchanged convention.
    // constructor, Equals/GetHashCode/==/!=, the 8 neighbor offsets as a static readonly array
}
```

### `CRPG.Navigation.Grid.WalkabilityGrid` (new, plain C# — real content authoring via an Editor-baked asset
wrapper is Task 3.2's job)

**Now also owns per-cell occupancy** (moved here from a separate registry — see the Occupancy section below for
why): a `WalkabilityGrid` instance is inherently scene-scoped (each location scene gets its own), so occupancy
data living on the same object is automatically scene-scoped too, with no separate lifetime/ownership story
needed.

```csharp
public sealed class WalkabilityGrid
{
    public WalkabilityGrid(Vector3 origin, float cellSize, int width, int depth, /* initial blocked cells */);

    public float CellSize { get; }
    public GridCoordinate ToGrid(Vector3 worldPosition);
    public Vector3 ToWorld(GridCoordinate cell);              // cell-center convention
    public bool IsInBounds(GridCoordinate cell);
    public bool IsWalkable(GridCoordinate cell);               // false if out of bounds OR wall/door-blocked
    public void SetBlocked(GridCoordinate cell, bool blocked); // the door/obstacle seam's mutation point

    // Occupancy (new in v2, was wrongly proposed as a global static in v1 -- see rationale below):
    public bool TryOccupy(GridCoordinate cell, RuntimeEntityId occupant); // false if already occupied by someone else
    public void Release(GridCoordinate cell, RuntimeEntityId occupant);   // no-op if occupant doesn't hold it
    public bool IsOccupied(GridCoordinate cell, out RuntimeEntityId occupant);
}
```

### `CRPG.Navigation.Grid.GridPathfindingService : INavMeshService` (new)

**Corrected ownership model (v2)**: does NOT hold an immutable `WalkabilityGrid` at construction. It's
constructed **once**, at the Core-scene composition root (same lifetime as `ActionScheduler`/
`PartyMovementService`, which already persist unchanged across scene travel — confirmed by reading
`SceneTravelCoordinator`: only the *location* scene is unloaded/reloaded, the Core scene and everything built
on it never is). It holds a **mutable current-grid reference**, re-pointed exactly once per location load:

```csharp
public sealed class GridPathfindingService : INavMeshService
{
    public void SetActiveGrid(WalkabilityGrid grid); // called once per location scene load -- see "Per-scene
                                                        // grid ownership" below for exactly where
    // INavMeshService methods below operate against whatever grid is currently active; if none is set yet
    // (e.g. queried before the first location loads), fail closed (return false) rather than NRE.
}
```

- **`TryCalculatePath(from, to, out corners, areaMask)`**: snaps `to` to the nearest walkable cell if its own
  cell isn't walkable (expanding-ring search, shared with `TrySamplePosition` below). Runs 8-connected A* with
  corner-cutting prevention (a diagonal step `(x,z)->(x+1,z+1)` is only legal if both `(x+1,z)` and `(x,z+1)`
  are walkable — verified by the reviewer as geometrically sufficient to guarantee any straight-line segment
  between two points inside the resulting corner chain never clips a wall corner). **Corrected invariant
  (v2, from the review's W2)**: `corners[0]` is always the caller's exact `from` position (Y-projected onto the
  grid's ground height), never a snapped cell center — matching `NavMeshService`'s own real behavior
  (`path.corners[0] == from`) and preserving the straight-line-safety guarantee for the first leg's threat-
  crossing math, which operates on `corners[0]` as the segment start. `corners[1..]` are cell centers along the
  route. **Same-cell case (v2, from W3)**: if `from` and `to` snap to the same cell, still return a real
  2-corner path `[from, thatCell'sCenter]` (matching NavMesh's own behavior for a very short move) rather than
  degenerating to a 1-corner no-op — unless `from` is already within the motor's arrival epsilon of that
  center, in which case return a 1-corner path (`[from]`) and let the caller treat it as an immediate arrival.
  Returns every cell along the route, no corner simplification. `areaMask` accepted, ignored (documented,
  deliberate). Unreachable = failure, matching the existing contract.
- **`TrySamplePosition`**: unchanged from v1 — walkable-cell-of-point, else expanding-ring search to
  `maxDistance`.
- **`TryCalculatePathLength`**: **corrected contract (v2, from the review's B3)**. v1 claimed this "needs to
  agree with `MoveActionExecution`'s own distance-based budget math" — false; `MoveActionExecution` never calls
  this method at all (only `TryCalculatePath`). The real, binding constraint, found by checking actual callers:
  `PartyMovementService.ResolveOneMember` calls `TryCalculatePathLength` to **gate** whether a formation slot is
  reachable at all (discarding the length value itself — `out _`), then separately calls `TrySamplePosition`
  for the actual destination. **The two methods must therefore agree on snapping and reachability** — if
  `TryCalculatePathLength` says a destination is reachable, `TryCalculatePath`/`TrySamplePosition` must not
  then disagree, or formation resolution silently diverges from what movement can actually do. Implementation:
  run the same pathfind as `TryCalculatePath`, sum real per-step distances (`cellSize` cardinal, `cellSize *
  sqrt(2)` diagonal).

Unit tests (`Tests/Editor/GridPathfindingServiceTests.cs`): straight path, path around an obstacle, a
deliberate diagonal-corner-cutting case (two walls meeting at a corner forces a cardinal detour), unreachable
destination, snapping onto a blocked start/end cell, `corners[0] == from` for both a multi-cell and a same-cell
case, `TryCalculatePathLength`/`TryCalculatePath` reachability agreement, `SetActiveGrid` swapping mid-session
(simulating scene travel) correctly changes what's reachable with no stale state from the previous grid.

### `CRPG.Navigation.Grid.GridCharacterMotor : MonoBehaviour, IMovable` (new — redesigned in v2)

**Corrected design (v2, replaces v1's "never paths, just interpolates" claim, which B1 disproved)**: this motor
**always plans its own path** on every `SetDestination` call, by calling the ambient `GridPathfindingService`
itself (injected at spawn/bind time — see "Per-scene grid ownership" below), exactly like
`NavMeshCharacterMotor` delegates to `NavMeshAgent`'s own internal pathing. There is no longer a distinction
between "a pre-planned `MoveActionExecution` leg" and "an arbitrary click-to-move/interaction-approach
destination" at the motor's level — every call gets a real A* path from the motor's current position to the
requested destination, which is cheap when the destination is one cell away (the common case for
`MoveActionExecution`'s own leg-by-leg calls) and correct when it's a long cross-map click
(`PartyExplorationController`/`InteractionController`'s calls, which the review found v1 completely missed).

- Internally: on `SetDestination`, call `GridPathfindingService.TryCalculatePath(transform.position, destination,
  out corners)`; if it fails, return `MoveResult.Invalid()`. Otherwise follow the corners sequentially (straight-
  line interpolation cell-to-cell — safe per the corner-cutting-prevention proof), with the final corner being
  the exact requested destination (not necessarily a cell center) per `TryCalculatePath`'s corrected contract.
- `MotionSnapshot` semantics, specified precisely (v1 left this vague — review's W6): `Velocity` = current
  direction-of-travel * configured move speed while actively interpolating toward a corner, `Vector3.zero`
  otherwise (this is a real, load-bearing field — `FacingController.UpdateMotionFacing` reads it directly, and
  Phase 2's rocking-motion driver hangs off the same field; a motor that never populates it silently kills
  motion facing and the rocking animation for every 2D character). `DesiredVelocity` mirrors it (no separate
  steering/avoidance concept for a grid mover). `IsOnNavMesh` means "currently on a walkable cell" (true unless
  something has gone wrong). `RemainingDistance` = sum of remaining corner-to-corner distances (best-effort;
  confirmed via grep that this field has zero production consumers today, so this doesn't need to be exact,
  just not garbage).
- Same status vocabulary/request-id-supersession semantics as `NavMeshCharacterMotor`
  (Pending/Moving/Arrived/Blocked/Invalid/Cancelled).
- **Zero-length `SetDestination` resolves `Pending` -> `Arrived` on the next `Snapshot` read, never `Invalid`**
  (unchanged from v1 — still correct, still required by `MoveActionExecution`'s budget-exhausted leg-clip case;
  corrected from v1's wrong claim that the already-in-range case also does this — it doesn't, that path returns
  before ever calling `SetDestination` at all, per the review's N2).
- **Arrival epsilon: very small (not content-tunable) AND the motor snaps `transform.position` to the EXACT
  target point on determining arrival**, not just "close enough, stop." This is v2's fix for the review's W5
  (the `Mathf.CeilToInt` integer-rounding-boundary risk): if every leg's final settled position is snapped
  exactly onto its intended stop point with zero float drift, `MoveActionExecution.SettleOutstandingDistance`'s
  cumulative-distance accumulation never has a reason to land suspiciously close to an integer boundary the way
  a "stop within 0.01, whatever that residual is" motor would. **This resolves W5 entirely inside the motor,
  with zero changes to `MoveActionExecution`** — worth stating explicitly since it's what keeps Task 1.4's
  "zero changes to the existing action/combat files" promise honest.
- **Occupancy claim/release** (v2 — see the dedicated Occupancy section below for the full design; summarized
  here): on beginning travel into a new cell, calls `WalkabilityGrid.TryOccupy`; if it fails (someone already
  there), report `Blocked` for this leg exactly like an unwalkable NavMesh result, and release nothing (the
  motor never left its current, already-occupied-by-itself cell). On completing a cell-to-cell step, releases
  the previous cell and the new cell's occupation is already held from the claim above.
- Driven by scaled `Time.deltaTime` only (the pause guarantee — unchanged from v1, still correct).
- Position is never cached separately from `transform.position` (unchanged from v1 — this is what makes
  `SessionLoadCoordinator`'s existing NavMesh-specific resync-on-restore fix a genuine no-op for this motor).

Tests (`Tests/Runtime/GridCharacterMotorTests.cs`): everything `NavMeshCharacterMotor`'s own coverage proves,
**plus**: a long, multi-cell, unplanned `SetDestination` call (simulating `PartyExplorationController`'s real
usage, not just a `MoveActionExecution`-style short leg) actually paths around an obstacle correctly; zero-
length destination resolves to `Arrived`; a straight N-cell move spends exactly N movement through the real
`MovementResolutionService`/`MoveActionExecution` pipeline; a paused tick is a true no-op; occupancy rejection
reports `Blocked` and correctly releases nothing; two motors racing for the same cell — exactly one succeeds,
the other reports `Blocked` and can retry after the winner moves on.

> **Task 1.0 implementation notes (2026-09-19, three deviations from this doc, all verified live):**
> 1. **Raw `NavMeshAgent.Warp` is unsafe, so `NavMeshCharacterMotor.Warp` does not call it blindly** (traced
>    with live Unity experiments, 2026-09-19). Raw Warp always writes `transform.position` first, so a failure
>    strands the agent off-mesh at the requested point with its path destroyed (Unity logs a *warning*, "Failed
>    to create agent because it is not close enough to the NavMesh"). It also accepts points up to ~20x
>    `agent.radius` off the mesh, then silently snaps the character onto it on the *next* frame. That produced
>    two real latent bugs: (A) `SceneTravelCoordinator` sampled each slot, discarded the sampled point, and
>    warped to the raw slot, so nearby slots could silently collapse onto one edge point while travel reported
>    success; (B) a small-radius agent could fail to warp after passing the 5-unit validation, and rollback
>    skipped the failed member, leaving it stranded (`TeleportInteractionAdapter` had no validation at all).
>    **Fix shipped with Task 1.0:** `NavMeshCharacterMotor.Warp` samples first (`warpSampleMaxDistance`, default
>    1), refuses without touching the agent or its in-flight request, and warps to the *sampled* point;
>    `SceneTravelCoordinator` warps to sampled points and rolls back every member a warp was attempted for.
>    So `IMovable.Warp`'s contract stands as written below ("returns false, mover untouched") and
>    `GridCharacterMotor.Warp` (Task 1.3) must meet it too. The "needs one more frame after scene load" comment
>    in `SceneTravelCoordinator` was not reproducible and has been softened (the settle frame is kept).
> 2. **`NavMeshObstacleWalkabilityBlocker` is a plain class, not a MonoBehaviour**, and `UseDoorInteractionAdapter`
>    keeps its serialized `obstacle` field (existing scenes and `InteractionLabSceneAuthor` reference it by
>    name). The adapter prefers any `IWalkabilityBlocker` component on the door, else wraps `obstacle`. The grid
>    blocker (Task 1.3) will be the component form.
> 3. **Test fixtures needed a real motor.** `SceneTravelTests` built party members with a bare `NavMeshAgent`;
>    with `Warp` on `IMovable` they now need `NavMeshCharacterMotor` (all real prefab authors already add it).
>    Any future fixture that scene-travels or teleports a character must do the same.

### `IMovable.Warp(Vector3 position)` — **corrected signature (v2, from the review's B2)**

```csharp
/// Instant relocation, bypassing pathing entirely. Returns false (and does NOT relocate) if the position is
/// invalid for this backend (off-mesh / out of grid bounds / unwalkable) -- callers branch on this exactly
/// like NavMeshAgent.Warp's own bool return, which two existing call sites (SceneTravelCoordinator's
/// reposition-or-rollback loop, TeleportInteractionAdapter's success/fail report) already depend on today and
/// must keep depending on. On success, cancels any in-flight move request (Snapshot.Status becomes Cancelled,
/// same as Stop()) and repositions immediately.
bool Warp(Vector3 position);
```

v1 specified `void Warp`, which the review caught as silently breaking two real failure-branching call sites
(`SceneTravelCoordinator.cs:157-162`'s repositionFailed/rollback logic, `TeleportInteractionAdapter.cs:40`'s
success report) — both currently branch on `NavMeshAgent.Warp`'s real bool return, and an existing test
(`SceneTravelTests.cs`'s `PartyRepositionFailed` case) only happens to pass under v1's `void` design by
accident (its broken fixture has no `NavMeshAgent` at all, so `Motor == null` already fails it for an unrelated
reason — the design never noticed it was relying on that).

- `NavMeshCharacterMotor.Warp`: `return agent.Warp(position)` verbatim (the real fix
  `SessionLoadCoordinator`'s own comment already documents needing), then the same cancellation bookkeeping
  `Stop()` already does, only on success.
- `GridCharacterMotor.Warp`: returns false for an out-of-bounds/unwalkable position (mirrors `NavMeshAgent.Warp`'s
  own real failure behavior for an off-mesh point), otherwise sets `transform.position` directly and does the
  same cancellation bookkeeping. This also resolves v1's own open question #2 (whether to clamp or trust the
  caller) by making the answer symmetric with the NavMesh backend: reject, don't clamp.

**Before implementing**: every real `IMovable` implementer, found exhaustively by the review (v1's own list was
wrong — named test files that don't implement the interface at all):
1. `Runtime/Navigation/NavMeshCharacterMotor.cs`
2. `Tests/Runtime/TacticalEncounterControllerTests.cs` — `RecordingMovable`
3. `Tests/Runtime/InteractionLifecycleTests.cs` — `FakeMovable`
4. `Tests/Editor/PartyExplorationControllerTests.cs` — `FakeMovable`

All four need the new member; this is a real (if mechanical) multi-file change, not a single-file one.

### `IWalkabilityBlocker` (unchanged in shape from v1 — the door/obstacle seam)

```csharp
public interface IWalkabilityBlocker
{
    bool IsBlocking { get; }
    void SetBlocking(bool blocking);
}
```

- `NavMeshObstacleWalkabilityBlocker : MonoBehaviour, IWalkabilityBlocker` — thin wrapper around the existing
  `NavMeshObstacle.enabled` toggle, zero behavior change for the 3D path.
- `GridWalkabilityBlocker : MonoBehaviour, IWalkabilityBlocker` — marks/unmarks a configured set of
  `GridCoordinate`s as blocked/unblocked on the **currently active** `WalkabilityGrid` (resolved the same way
  `GridCharacterMotor` resolves it — see "Per-scene grid ownership" below).
- **New in v2 (review's W9)**: `GridWalkabilityBlocker` must explicitly apply its configured initial state
  (open/closed) to the active grid at bind time — unlike `NavMeshObstacle`, whose serialized `enabled` field
  already *is* the initial state with no code needed, a `WalkabilityGrid`'s blocked-cell set starts empty and
  needs an explicit push. **This turns out to need no matching unblock-on-unload cleanup**: because each
  location scene gets a **wholly new** `WalkabilityGrid` instance from its own baked data every time it loads
  (via `SetActiveGrid`, never a shared/mutated-in-place grid across scenes), there is no cross-scene leftover-
  blocked-cell risk to clean up in the first place — a nice simplification that falls out of the per-scene
  ownership design below.
- `UseDoorInteractionAdapter` changes its `[SerializeField] private NavMeshObstacle obstacle;` field to resolve
  an `IWalkabilityBlocker` at bind time (exact Inspector-authoring shape — a `MonoBehaviour` field cast via `as`,
  or two optional typed fields — decide during implementation; not load-bearing enough to block this design).
  Its `obstacle.enabled = !IsOpen` line becomes `blocker?.SetBlocking(!IsOpen)`. **Also (review's W7)**:
  `Tests/Runtime/UseDoorInteractionAdapterTests.cs` sets the current field by name via reflection
  (`GetField("obstacle", ...)`) — this test needs updating alongside the field rename, not just production
  code, and the fixture needs a `NavMeshObstacleWalkabilityBlocker` component added to keep exercising the
  NavMesh path.

## Per-scene grid ownership and rebinding across scene travel (new section, v2 — closes the review's B4)

This was entirely unaddressed in v1. The real constraint, confirmed by reading `SceneTravelCoordinator` in
full: the Core scene (session, `ActionScheduler`, `PartyMovementService`, and now `GridPathfindingService`) is
never unloaded or rebuilt across a trip between locations — only the *location* scene (geometry/NavMesh/NPCs)
loads/unloads additively. So a single, persistent `GridPathfindingService` instance must be re-pointed at each
newly-loaded location's own `WalkabilityGrid` data, not reconstructed per scene.

- NEW `WalkabilityGridProvider` (`MonoBehaviour`, scene-authored, one per location scene, sits alongside that
  scene's Tilemap) — holds/exposes that scene's baked `WalkabilityGrid` (Task 3.2's baking output; out of
  Phase 1's own scope to author real content for, but Phase 1 must define the type this hand-off uses).
- `SceneTravelCoordinator.TravelCoroutine` already has exactly the right hook for this:
  `LocationInitializer.Prepare(content, session, worldSpawnService, targetScene)` is the established "do
  scene-specific setup for the newly-loaded location" call (confirmed real, existing code). Extend this step
  (or add a small sibling call right next to it) to locate the new scene's `WalkabilityGridProvider` — the same
  `FindObjectsByType<MonoBehaviour>(...).OfType<T>().FirstOrDefault(c => ((MonoBehaviour)c).gameObject.scene ==
  scene)` pattern `SceneTravelCoordinator.FindLocationContent` already uses for `ILocationContent` — and call
  `gridPathfindingService.SetActiveGrid(provider.Grid)`.
- `GridCharacterMotor`/`GridWalkabilityBlocker` (per-character/per-door scene components) don't hold their own
  reference to `GridPathfindingService` from construction — they receive it the same way other cross-cutting
  services already reach spawned characters: `WorldSpawnService`'s existing spawn/bind wiring passes it in at
  bind time (mirroring how it already threads other composition-root-owned services into spawned
  `CharacterAgent`s), not a static singleton lookup.
- This also directly resolves the review's B5 (occupancy must be scene-scoped, not a global static): because
  occupancy now lives ON `WalkabilityGrid` itself (see above), and `WalkabilityGrid` is already swapped wholesale
  per scene via `SetActiveGrid`, occupancy is automatically scene-scoped for free — two additively-loaded
  location scenes (which do briefly coexist mid-travel, confirmed by reading the coroutine) never share one
  occupancy table, because they're never the *active* grid at the same time from `GridPathfindingService`'s
  point of view. (Characters in the outgoing scene are torn down via `worldSpawnService.ReleaseSpawnsInScene`
  before `SetActiveGrid` would ever be called for the new scene in the same trip, so there's no window where
  stale occupancy claims leak across.)

`SceneTravelCoordinator`'s own constructor change (needed for step 2's `INavMeshService`/`GridPathfindingService`
access): **add an optional trailing parameter, not a required one** (review's N6) —
`INavMeshService navMeshService = null`, defaulting to `new NavMeshService()` — so all 8 existing call sites
(`SceneTravelTests.cs` x6, `AdventureSliceBootstrap.cs` x2) keep compiling unchanged. Same optional-trailing-
parameter treatment anywhere else an existing constructor would otherwise need to become a required breaking
change for this seam work.

## Occupancy — full design (was one bullet in v1; review's B5/B6 found it needs to be its own piece)

- **Storage**: on `WalkabilityGrid` itself (see above), not a separate static registry — resolves B5.
- **Claimed/released by**: `GridCharacterMotor` as it moves cell-to-cell (claim destination cell before
  entering it — rejecting the leg with `Blocked` if already held by someone else — release the cell it's
  leaving once the step completes); `WorldSpawnService`'s existing spawn/teardown hooks claim on spawn and
  release on despawn/death (mirroring how `MarkDefeated`/teardown already has a natural place to do this);
  `Warp` also claims/releases appropriately (release the old cell, claim the new one, matching movement).
- **What counts as "occupying"**: still an open call for implementation time (not blocking this design) —
  lean toward "every live `CharacterAgent` with a `GridCharacterMotor`, dead or alive" for simplicity (matching
  `CharacterAgent.AllLive`'s own existing "everything enabled" scope) unless a real playtest finds a reason a
  downed character shouldn't block a cell.
- **Outcome vocabulary when occupancy blocks a leg (review's W1, a real decision v1 dodged)**: reuse the
  existing `Blocked` status/`ActionOutcome.Interrupted` path unchanged — an ally occupying a cell is reported
  exactly like an unwalkable NavMesh result would be. **Accepted, explicit consequence, not silently
  discovered later**: `InteractionController.TickApproaching` maps a `Blocked` motion result to
  `InteractionOutcome.Blocked`, so a party member standing directly on an interaction point's approach path can
  transiently block that interaction. Given this campaign's actual map layout (roomy town squares and multi-
  cell-wide dungeon corridors per the handoff layout, not single-file chokepoints at every interactable), this
  is judged an acceptable, bounded simplification for a 30-60 minute campaign rather than something worth
  building a wait-and-retry mechanism for — flagged explicitly here as a deliberate scope decision, matching
  this project's established convention, not a silently-accepted gap.
- **Formation-slot collision (review's B6, a real, immediately player-visible bug if unaddressed)**:
  `PartyMovementService.ResolveOneMember` resolves each of up to 4 party members' destinations independently,
  with formation spacing (1.5 units) smaller than one grid cell (1 unit) — multiple members' resolved
  destinations can snap to the identical cell, after which occupancy would `Block` 3 of 4 members on an
  ordinary ground click. **Real, explicit fix, not deferred**: `PartyMovementService.ResolveOneMember`'s
  existing per-member loop gains a `HashSet<GridCoordinate>` of cells already claimed earlier in the *same*
  resolution pass, threaded through so each subsequent member's placement search explicitly excludes them
  (falling back to the next-nearest walkable cell instead of colliding). This is a genuine, narrowly-scoped
  change to `PartyMovementService` — Task 1.4 no longer claims "zero changes anywhere outside the three named
  files," and this document says so explicitly rather than letting that surface as a surprise mid-implementation.

## Call-site changes (four bypass call sites across three files)

1. **`TeleportInteractionAdapter.BeginExecute`**: `var navAgent = agent.GetComponent<NavMeshAgent>(); var
   succeeded = navAgent != null && navAgent.Warp(destination.position);` becomes `var succeeded = agent.Motor !=
   null && agent.Motor.Warp(destination.position);` — using `CharacterAgent.Motor` (already `IMovable`-typed).
2. **`SceneTravelCoordinator`**: gains the optional trailing `INavMeshService` parameter (see above).
   `NavMesh.SamplePosition(...)` becomes `navMeshService.TrySamplePosition(...)`. Both the forward-reposition
   loop and the rollback loop's `agent.GetComponent<NavMeshAgent>()` / `.Warp(...)` become `agent.Motor?.Warp(...)`
   **with the existing `null`/`false`-checked failure branching preserved exactly** (per the corrected `bool
   Warp` signature) — `agent != null` guarded first (unchanged), then `agent.Motor == null || !agent.Motor.Warp(positions[i])`
   triggers `repositionFailed = true` exactly as the current `navAgent == null || !navAgent.Warp(...)` does.
   Also gains the `LocationInitializer.Prepare`-adjacent `SetActiveGrid` hookup from the per-scene ownership
   section above.
3. **`SessionLoadCoordinator`**: `instance.GetComponentInChildren<NavMeshAgent>(true)?.Warp(...)` becomes
   resolving `IMovable` via `GetComponent<IMovable>() ?? GetComponentInChildren<IMovable>(true)` — **note the
   explicit `true` (include inactive) on the fallback call, matching the original's own `(true)` argument**
   (review's W8 — `CharacterAgent.Bind`'s own resolution pattern uses the no-arg overload, which would silently
   change behavior here if copied verbatim). Then `.Warp(instance.transform.position)`, discarding the bool
   result (this call site doesn't currently branch on Warp's success, and per the design above a grid motor's
   Warp is a behavior-preserving no-op here anyway since it has no internal position cache to resync).
4. **`UseDoorInteractionAdapter`**: covered above.

(`TravelInteractionAdapter` needs no changes — confirmed it never touches NavMesh directly.)

## Task breakdown (fills in the master plan's Phase 1)

### Task 1.0: Seam the bypasses (do first, 3D-only, must stay behavior-preserving)

Implement `IMovable.Warp` (bool-returning) on `NavMeshCharacterMotor` only, `IWalkabilityBlocker` +
`NavMeshObstacleWalkabilityBlocker`, and all four call-site changes above, using only the existing NavMesh
backend (no grid code exists yet). Update all four real `IMovable` test-fake implementers listed above, and
`UseDoorInteractionAdapterTests`'s reflection-based field access. Full existing EditMode+PlayMode suite must
stay 100% green — this isolates "did seaming break the existing NavMesh game" from "does new grid code work"
as two separately-verifiable steps.

### Task 1.1: Prove the motor contract first — DONE (2026-09-19)

**Result: the contract holds, with zero changes to `MoveActionExecution`/`ActionScheduler`/`MovementResolutionService`.**
`Tests/Runtime/GridMotorContractTests.cs` is an abstract fixture (12 tests) that any grid backend must pass,
driven through the real `MoveActionExecution` and through raw `IMovable` calls the way `PartyExplorationController`/
`InteractionController` make them. Its first subclass, `FakeGridMotorContractTests`, uses a deliberately minimal
test-only grid (small Dijkstra pathfinder, `FakeGridNavService`, `FakeGridMotor`); **Tasks 1.2/1.3 add a second
subclass over the real `GridPathfindingService`/`GridCharacterMotor` and must pass the identical tests.**
Proven: N straight cells spend exactly N; 3 diagonal cells spend Ceil(4.2426)=5 cumulatively; an exhausted-budget
zero-length leg is a success, not an interrupt; a budget clip landing mid-cell is reached exactly; an unplanned
long `SetDestination` paths around a wall and never enters a blocked cell; arbitrary non-cell-aligned
destinations arrive exactly; zero-length destinations arrive (never `Invalid`); an unreachable destination is
`Invalid` and leaves the in-flight request untouched; a new request supersedes the old id; `Stop` cancels and
retains the id; `Warp` refuses unwalkable cells without moving anything and cancels on success; `timeScale = 0`
is a true no-op and resumes cleanly. **A negative control** (a motor that overshoots each corner by 0.001) makes
a 5-cell move spend 6, confirming the exact-arrival-snap requirement is load-bearing rather than theoretical.
Full PlayMode suite after: 233 passed, 0 failed, 3 skipped (sample-import fixtures).

Original spec for this task, kept for reference:

Minimal fake grid `IMovable` driven directly through **both** `MoveActionExecution` **and** a stand-in for
`PartyExplorationController`/`InteractionController`-style unplanned long-distance `SetDestination` calls (the
review's B1 finding — v1 only proved the former). Prove: arbitrary-destination `SetDestination` that requires
real multi-cell pathing (not just a pre-planned short leg), zero-length-destination handling, request-id
supersession, the pause guarantee, and exact-arrival-snap (no float drift) before investing in the real
pathfinder/motor.

### Task 1.2: `GridPathfindingService` + `WalkabilityGrid` (including occupancy) — DONE (2026-09-19)

Shipped in `Runtime/Navigation/Grid/` (`GridCoordinate`, `WalkabilityGrid`, `GridPathfindingService`), 28 EditMode
tests (`Tests/Editor/GridPathfindingServiceTests.cs`), and a third contract subclass
(`RealGridPathfinderContractTests`) that runs the identical 12 Task 1.1 contract tests with the real pathfinder
behind the test motor — all passing. A mutation check (disabling the corner-cutting rule) was caught by 5 tests.
Full suite after: EditMode 707/707, PlayMode 245 passed / 3 skipped (sample-import fixtures).
Implementation choices beyond the spec above, worth knowing when Task 1.3 builds on this:
- **Endpoint snapping is bounded**: `GridPathfindingService(endpointSnapDistance = 2f)` — a path's start or end more
  than that many world units (to a cell centre) from any walkable cell fails rather than snapping across the map.
- **A start standing inside a now-blocked cell** (a door closed on the mover) yields `[from, nearestWalkableCentre,
  ...]`, so the first hop leaves the wall instead of cutting across its neighbours. Only that first hop is exempt
  from the "adjacent cells" corner invariant.
- **`TrySamplePosition` measures to cell centres on the ground plane (Y ignored)** and returns the centre; a point
  already on a walkable cell always samples to its centre regardless of `maxDistance`.
- **Occupancy** (`TryOccupy`/`Release`/`IsOccupied`) is implemented on `WalkabilityGrid` as designed and is ignored
  by pathfinding; releases are holder-checked. Nothing calls it yet — `GridCharacterMotor` (Task 1.3) is its first
  consumer. `SetBlocked` is the door seam's mutation point.
- Search scratch is stamped and reused per grid, so repeated queries allocate only the result.

Original spec for this task, kept for reference:

As designed above, with `SetActiveGrid` re-pointing and the corner-cutting/same-cell/`corners[0]==from` tests.

### Task 1.3: `GridCharacterMotor` + `WalkabilityGridProvider` + per-scene rebinding — DONE (2026-09-19)

Shipped in `Runtime/Navigation/Grid/`: `GridCharacterMotor`, `WalkabilityGridProvider`, `GridWalkabilityBlocker`;
plus wiring in `WorldSpawnService` (`GridNavigation` property) and `SceneTravelCoordinator` (optional trailing
`gridPathfinding` parameter). `GridCharacterMotorContractTests` runs the **entire Task 1.1 contract (12 tests)
against the real motor + real pathfinder**, plus 8 motor-specific tests (occupancy, grid swap, door seam,
snapshot velocity, spawn wiring), 3 provider tests, and a coordinator failure-path test. Full suite after:
EditMode 707/707, PlayMode 270 passed / 3 skipped (sample-import fixtures).
Implementation choices beyond the spec above:
- **`Warp` is strict, no snapping**: refuses (motor, claims and request untouched) unless the target is a walkable,
  unoccupied cell of the active grid. A grid has nothing "near but off" to snap to; callers (scene travel) already
  pass sampled cell centres. A teleport marker authored inside a wall now fails cleanly instead of stranding.
- **Occupant identity**: the motor lives in `CRPG.Navigation` and can't see `CharacterAgent`, so it takes a
  settable `OccupantId` that `WorldSpawnService` fills with the entity id at bind time (a unique id is generated if
  nothing sets one). `Pathfinding` is injected the same way — no static lookups.
- **Occupancy model**: a mover holds the cell it stands in, plus the next cell from the moment it starts toward it
  (it claims a corner's cell *before* moving; a failed claim = stop where it is, report `Blocked`); the old cell is
  released on arrival at the corner (conservative: it holds both while crossing). Claims are released on
  disable/destroy and re-made lazily when a grid is swapped or the mover re-enables.
- **`SceneTravelCoordinator`**: with a `gridPathfinding`, it activates the target scene's grid (via
  `WalkabilityGridProvider.FindInScene`) *before* validating placements, uses the pathfinder as the placement
  validator by default, restores the previous grid on any later failure (before the rollback warps run), and fails
  with `PlacementValidationFailed` if the scene has no provider or invalid grid data.
- **`GridWalkabilityBlocker`** (the grid door/portcullis) landed here rather than later; verified end to end through
  the real `UseDoorInteractionAdapter`. It pushes its initial state on first enable and never resets on re-enable.
- **Deferred to Task 1.5**: a full scene-travel round trip with real providers in the fixture scenes (only the
  failure path is exercised so far), and activating the *initial* location's grid (composition-root job via
  `FindInScene`). `SessionLoadCoordinator`'s cross-scene load does not activate a grid yet.
- **Still pending in Task 1.4**: `PartyMovementService`'s formation-slot collision fix.

As designed above, including `WorldSpawnService`'s bind-time wiring and `SceneTravelCoordinator`'s
`SetActiveGrid` hookup.

### Task 1.4: `PartyMovementService`'s formation-slot fix — DONE (2026-09-19)

**Result:** several formation slots landing in one cell now resolve to distinct free neighbouring cells instead of
collapsing. EditMode 718/718, PlayMode 271 passed / 3 skipped (existing NavMesh party-navigation tests unchanged).
**Deviation from the design above (deliberate):** the design threaded a `HashSet<GridCoordinate>` through
`ResolveOneMember`, which would have made `CRPG.Exploration` know what a grid cell is. Instead:
- NEW optional capability `ISlotAvoidingNavMeshService : INavMeshService` (`Runtime/Navigation/`) with
  `TrySamplePositionAvoiding(point, out sampled, maxDistance, IReadOnlyCollection<Vector3> claimed)`. A backend
  where characters can't share a spot implements it and decides what "same spot" means; `GridPathfindingService`
  does (same cell). A NavMesh service doesn't, so its behaviour is byte-for-byte unchanged.
- `PartyMovementService` keeps a list of claimed positions: every character standing still **outside** the order
  (unselected party members, NPCs — read from the same single `GetSelectables()` call) plus each destination handed
  out earlier in the order. When the backend supports it and anything is claimed, a slot that would snap onto a
  claimed spot is displaced to the nearest free one; that is reported as `Fallback` (not `Exact`). If nothing nearby
  is free, that attempt fails and the existing retry (compress toward the group's destination) tries the next.
- Tests: 6 for the sampler (`GridPathfindingServiceTests`), 5 for the resolver (`PartyMovementServiceGridTests`,
  including a control proving all four slots collapse onto one cell without the capability), and one PlayMode
  test running the real `PartyExplorationController` over four real grid motors.
**Decision record: reservations were evaluated and deferred (2026-09-19).** The alternative — a per-cell
*reservation* (a mover reserves its destination cell for the duration of a move; other paths may route through it
but no one else may choose it as a destination) — was put head to head with this design by an independent Opus
review, which chose slot avoidance. Reasons, so the option isn't silently re-litigated: (1) reservations only fix the
reported bug if they exist at the moment destinations are *chosen*, so the resolver would have to reserve as it
goes — that is this design's claimed list promoted to global state, not a replacement for it; (2) a reservation
leak is invisible and permanent (nothing stands on the cell, yet it can never be targeted), whereas an occupancy
leak self-heals (`GridCharacterMotor.EnsureHeld`, holder-checked release); B would need nine release paths (arrive,
`Stop`, supersede, `Warp`, disable/destroy, death mid-reaction, both `SetActiveGrid` swaps, load-restore);
(3) `MoveActionExecution` issues one `SetDestination` per leg/threat-crossing stop, so a motor-level reservation
would churn every waypoint, and reserving only the true endpoint would make `CRPG.Gameplay` know about grid
reservations; (4) it does nothing for the mid-route `Blocked` limitation below. The two are complementary: if
playtesting shows cross-order conflicts (an order a frame apart, or a click on a cell another mover is already
walking to) hurting, reservations can be layered on by having `PartyMovementService` publish its claimed list to
the grid, with no rework of this design.
**Hardening from that review (all applied):** members are resolved in `order.Members` order (claim order decides
who keeps an exact slot, so it must be deterministic — not `Dictionary` order); a member found `Unreachable` claims
its own current position since it is staying put; in-order members' current cells are deliberately *not*
pre-seeded (a party stepping forward would otherwise displace everyone off cells their teammates are vacating);
the `as ISlotAvoidingNavMeshService` cast is hoisted to a field; the grid sampler reuses a scratch set. Two new
resolver tests cover the first two rules; the PlayMode test now also asserts the four handed-out destinations are
four distinct cells (via a recording motor wrapper). Final counts: EditMode 720/720, PlayMode 271 passed / 3 skipped.
**Known, accepted limitation (same class as the occupancy-blocked-interaction note below):** pathfinding ignores
occupancy, so a member walking to its cell can be `Blocked` mid-route by a teammate who arrived at a cell on its
path first. Destinations are guaranteed distinct; routes are not guaranteed conflict-free. The PlayMode test
therefore asserts acceptance, not simultaneous arrival. If a playtest shows this hurting, the fix belongs in the
mover (wait-and-retry) or the pathfinder (soft occupancy costs), not in formation resolution.

The `HashSet<GridCoordinate>`-threaded claimed-cells fix designed above. Explicitly its own task now, not an
afterthought discovered during end-to-end testing.

### Task 1.5: Prove the full seam end to end — DONE (2026-09-19)

**Result: proven, and it found three real defects that the earlier per-component tests could not see.**
EditMode 728/728, PlayMode 284 passed / 3 skipped (sample-import fixtures). `MoveActionExecution`, `ActionScheduler`
and `MovementResolutionService` are still **completely unchanged** across all of Phase 1.
Proven end to end on the real grid motor + real pathfinder (`GridCharacterMotorContractTests`): a mid-cell threat
crossing stops the mover at the exact off-centre boundary point, resolves an opportunity attack, and the mover carries
on to the destination spending exactly the right movement; an opportunity attack that kills the mover ends the move
`Interrupted`; `ActionScheduler` runs a grid move and refuses a second while busy; a melee closing move stops exactly
`stopDistance` from the target; a real `PartyExplorationController` click sends three real motors across a wall
through a 2-cell gap and all three arrive in their own cells; and (`GridSceneTravelTests`, over two real scenes
`GridTravelTestA/B` — open vs walled) a scene-travel round trip re-points the pathfinder each way with reachability
and occupancy following it and nothing stale surviving, a failed reposition restores the old grid and the warped
member's claim, and three members whose formation slots collapse into one cell are still placed in distinct cells.
**The three defects it found (all fixed, each with a negative control or a regression test):**
1. **Single-file movement was impossible.** A mover holds its current + next cell and released the old one only on
   arriving at the next cell's centre, so a follower exactly one cell behind a leader was `Blocked` at once — every
   party leaving in a column would have stalled. Fixed two ways: a mover now releases the cell behind it the moment it
   crosses the boundary, and a failed claim first **waits** (`ClaimWaitSeconds`, default 0.5 s scaled time) because
   the occupant is usually just leaving. Proven in a one-cell-wide corridor (where nothing but waiting can work) with a
   `ClaimWaitSeconds = 0` negative control.
2. **A party click across a room stranded teammates.** A line formation puts teammates' destination cells on the same
   row as the farthest member's straight route; whoever arrived first parked in the way and the mover behind ended
   `Blocked` — Task 1.4's "accepted limitation" turned out to hit the primary use case. Fixed by re-planning: after the
   wait, the mover re-routes around every cell others currently occupy (`GridPathfindingService.TryCalculatePathAvoiding`,
   bounded to 3 re-plans, same request id, never silently changing the destination) and is only `Blocked` when no other
   route exists or the destination cell itself is the occupied one.
3. **Stop-short moves were off by up to half a cell.** `MoveActionExecution` walks the *path service's* corners, and the
   grid service ended every path at the goal cell's centre, so a melee closing move (`stopDistance` 1.5) stopped 1.0 from
   the target — closer than asked, and an outward error would have left an attacker out of reach while reporting
   arrival (the review's W4). `TryCalculatePath` now ends at the caller's **exact** `to` when its own cell is walkable
   (a destination snapped out of a wall still ends at the snapped centre); the corner-safety invariant still holds.
   The "accepted ~0.7-unit fuzz" recorded above is therefore only true for a destination that was inside a wall.
Also: `SceneTravelCoordinator` placements now use `ISlotAvoidingNavMeshService` (the same distinct-cells rule as
formation resolution). New dogfood fixtures: `TestProject/Assets/Scenes/Tests/GridTravelTestA/B.unity`, authored by
`SceneTravelTestFixtureAuthor.AuthorGridFixtures` (a separate menu item so the committed NavMesh fixtures are never
regenerated) and registered in `EditorBuildSettings`.
**Known limits carried out of Phase 1 (deliberate, documented):** (a) a wait of up to `ClaimWaitSeconds` is spent
before any `Blocked`, including behind a genuinely stationary character; (b) `MoveActionExecution` plans with the
occupancy-blind path service, so a tactical move through a parked teammate/enemy ends `Interrupted` rather than being
re-planned (turn-based, rare; if it matters the fix is in tactical path planning, not the motor); (c) two movers meeting
head-on in a corridor wide enough to pass may choose the same detour side and re-block — bounded by the re-plan cap,
they end `Blocked` rather than hang; (d) the composition root activates the *initial* location's grid itself via
`WalkabilityGridProvider.FindInScene` (travel and cross-scene load do it for every later one); (e) the grid itself is
authored by hand/`Configure` — baking one from a scene's colliders is Phase 3's Task 3.2.

**Final independent review of the whole phase (Opus, 2026-09-19): no blockers, safe to close.** It verified in code
that the wait/re-plan/claim logic cannot livelock (worst case ~4 x `ClaimWaitSeconds` then a hard `Blocked`), that a
paused game (`timeScale 0`) cannot time a waiting mover out, that there is no occupancy leak in `ReleaseCellsBehind`,
that corner-safety survives the exact-endpoint change (including diagonal final hops and boundary points), that no
caller relied on the last corner being a cell centre, and that the coordinator's swap/restore ordering is right.
Its findings and what was done:
- **Fixed — `Stop()`/`SetDestination()` mid diagonal hop could leave a mover holding no cell.** With the exact endpoint,
  a diagonal final hop can transit an unclaimed corner cell; `ReleaseAllExcept(cell I'm in)` then dropped every claim
  (and if someone else held that cell, two characters overlapped permanently). New `SettleHeldCells` claims the cell
  the mover is in, or keeps what it already holds if it cannot. Two tests; both fail against the old behaviour.
- **Fixed — `SessionLoadCoordinator` never handed the rebuilt `WorldSpawnService` the pathfinder** (every restored motor
  had `Pathfinding == null`, so movement was dead after any load) **and never activated the loaded location's grid.**
  It now takes an optional `gridPathfinding`, sets `GridNavigation` on the rebuilt spawn service, and on a *cross-scene*
  load activates the loaded scene's grid before restoring anything, restoring the previous grid on any failure. Two
  tests over the real grid scenes (`GridCrossSceneLoadTests`): success, and failure with no provider in the scene.
- **Not applicable — "a motor re-plan corrupts `MoveActionExecution`'s threat-crossing stops."** `MoveActionExecution`
  issues one short leg per planned stop, so the motor's whole path is a single hop into the next cell; when that cell is
  occupied the destination itself is the avoided cell, the re-plan refuses, and the leg ends `Blocked` -> `Interrupted`
  with no detour. Evidence: `ABlockedLegEndsMoveActionExecutionInterrupted...` uses a 3-wide arena where a detour
  *would* exist and still ends `Interrupted`. Re-planning only ever detours for long unplanned `SetDestination` calls
  (exploration, interaction), which have no threat-crossing stops. Recorded here so it isn't re-raised.
- **Deferred to the tactical-AI work (recorded, not lost):** `TacticalAiTurnDriver` issues one `StartMove` per turn and
  the path service ignores occupancy, so an AI closing to melee can target a cell an ally holds and lose its turn after a
  ~0.5 s stall. Fix when that work happens: pre-resolve the destination through `TrySamplePositionAvoiding` in tactical
  planning, and set `ClaimWaitSeconds = 0` on combat prefabs (it is a serialized field for exactly this).
- **Notes carried forward:** (1) *Phase 2:* during a claim wait the snapshot is `Moving` with `Velocity == 0` —
  billboard/rocking-motion code must key off `Velocity`, not `Status`, or it will animate a walk in place.
  (2) Travel's NPC-spawn-vs-party-placement ordering could in principle let a freshly spawned NPC motor claim a
  destination cell first (inferred, not observed); cheap guard if it ever shows: re-check placements immediately before
  warping. (3) Two members assigned each other's current cells (a very short click) end `Blocked` — an unresolvable swap.
  (4) `GridWalkabilityBlocker` can close a door on a standing character; the pathfinder's start-snapping recovers it, and
  Phase 3's collider baking should assert it. (5) A grid `Snapshot` with no request and no grid returns `None` where
  NavMesh returns `Invalid`; harmless for current callers. (6) `LoadCorners`' exact-endpoint fixup is now redundant with
  the pathfinder's own — dead but harmless.

**Phase 1 is closed.** Final counts: EditMode 728/728, PlayMode 288 passed / 3 skipped (sample-import fixtures).
`MoveActionExecution`, `ActionScheduler` and `MovementResolutionService` were never modified.

### Task 1.5 — original spec, kept for reference

`MoveActionExecution`/`ActionScheduler`/`MovementResolutionService` — genuinely zero changes to these three
files (the design above resolves both risks that could have forced changes — W4's stop-distance/cell-snapping
fuzz is accepted as a small bounded positional error rather than chased into `MoveActionExecution`, and W5's
rounding risk is resolved entirely at the motor level) — against the real grid motor+pathfinder, plus a real
multi-character occupancy scenario, plus a real `PartyExplorationController` multi-member formation move across
an obstacle, plus a real scene-travel round trip proving `SetActiveGrid` correctly switches which grid is
reachable with no stale state.

## Accepted, explicit simplifications (not gaps discovered later)

- **Stop-short-by-`stopDistance` vs. cell snapping (review's W4)**: `MoveActionExecution` computes an exact
  stop-short point before handing it to the pathfinder, which then snaps it to the nearest walkable cell if
  needed — bounded worst-case error under one cell's diagonal (~0.7 units at 1-unit cells) between the
  requested and actual stop position. Not chased further; melee threat range (2 units) and movement budgets
  (8 units) in this campaign's authored content comfortably absorb it.
- **Occupancy-blocked interactions (W1)**: accepted as a transient, bounded UX rough edge given this campaign's
  roomy map layout, not engineered around with a wait-and-retry mechanism.
- **`PartyMovementService` is no longer "zero changes"**: the formation-slot fix (Task 1.4) is real, explicit,
  and scoped — stated here rather than discovered mid-implementation.

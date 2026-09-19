# Task 2.1 Detailed Plan: Rocking motion + camera-facing billboard

> Status: BUILT (2026-09-19); plan v2 approved by the user, results at the end. (Was: DRAFT v2, post-Opus review.) Child of
> `Docs/Plans/2D-Campaign-Phase2-SpritePresentation.md` (Task 2.1, decisions D1-D3, findings 2, 3, 8, 10). Gate A passed:
> the hand-rolled spike in `BarrowsEnd/Assets/Spike/SpikeCharacter.cs` looked and moved right; **this task turns that
> spike into tested toolkit code without changing the feel.** The "Review record" at the end lists every Opus finding and
> its disposition.

## Goal

Ship, in `crpg-toolkit`, the three pieces that make a sprite card behave like the Gate-A spike, each testable without a scene:

1. `RockingMotionState` - pure logic: speed in, tilt out.
2. `RockingMotionDriver` - MonoBehaviour: reads the character's motor `Snapshot`, drives a `RockPivot` transform.
3. `CameraFacingBillboard` - MonoBehaviour: keeps the card parallel to the camera plane.

Non-goals (later tasks): layers/sorting (2.2), equipment (2.3), prefab authoring (2.5), combat cues (2.6).

## Deviations from the Phase 2 doc (flagged, per the standing rule)

- **Hop is cut.** D2 had an optional `hopHeight` (default 0). The user approved the rocking with no hop at Gate A. No hop output; the driver never writes position (verified: the spike's only position write is the constant feet offset, which is prefab-authored).
- **Namespace/folder is `Sprites`, not `Sprite`.** `CRPG.Presentation.Sprite` would shadow `UnityEngine.Sprite` in every `CRPG.Presentation.*` file (and D5 adds `Sprite` fields in 2.3). Folder `Runtime/Presentation/Sprites/`, namespace `CRPG.Presentation.Sprites`. Phase 2 doc paths get the same rename.
- **Coast carries the phase (revised after review).** During a zero-speed gap the state keeps its amplitude target at 1 *and keeps advancing the phase at the last real speed* - the gap is a sampling artifact of the motor, so we bridge it with the last good sample. (v1 froze the phase, which pinned a genuine stop at whatever lean it happened to be at for the whole coast.) Consequence, accepted and to be watched at Gate B: after a real stop the sprite rocks on for at most `coastSeconds` (<= ~0.4 of a cycle at speed 2) and then eases to level.

## Measured motion facts this design rests on (verified in source by the review)

- On a leg's arrival frame `GridCharacterMotor.Snapshot` already reports zero velocity + `Arrived` (`GridCharacterMotor.cs:194`) even though the mover moved that frame. `MoveActionExecution.Tick` does not plan the next leg while `awaitingArrival`; `OnLegArrived` only clears the flag, so the next `SetDestination` happens on the *following* Tick, and on that frame the motor has already zeroed velocity and early-returned (`:217-218`).
- **=> A one-cell-leg chain produces 2 zero-velocity frames per boundary, 3 when the driver's `Update` runs before the motor's** (nothing orders them). That is 33-50 ms at 60 fps.
- The 0.20 s coast covers that at 60 fps but a *time-only* coast breaks below ~15-20 fps, so the coast has a frame floor too (below).
- A claim wait (`Moving`, zero velocity) can last ~2 s across up to 3 re-plans; a 0.20 s coast correctly lets it settle.

## Design

### 1. `RockingMotionSettings` and `RockingMotionState` (`Runtime/Presentation/Sprites/`, pure C#)

`[Serializable] class RockingMotionSettings` - a **class with field initializers** (a serialized struct would deserialize all-zero and make the table below fiction):

| Field | Default | Origin |
|---|---|---|
| `peakDegrees` | 6 | Gate-A spike (approved) |
| `strideLength` | 1.0 | spike: one full cycle per world unit travelled |
| `startSpeed` | 0.10 | `CharacterPlayableAnimator` serialized default (hysteresis upper) |
| `stopSpeed` | 0.05 | same (hysteresis lower) |
| `coastSeconds` | 0.20 | new; must stay < `GridCharacterMotor.ClaimWaitSeconds` (0.5) |
| `coastMinFrames` | 4 | new; covers the measured 2-3 frame leg gap even at low fps |
| `amplitudeRiseRate` | 8 /s | spike |
| `amplitudeFallRate` | 6 /s | spike |

`RockingMotionState(RockingMotionSettings)` copies and **sanitizes every field** into a private normalized snapshot (Unity never calls `OnValidate` on a nested serializable; the driver's own `OnValidate` re-validates and logs): `peakDegrees = max(0)`, `strideLength > 0 else 1`, `startSpeed > 0 else 0.10`, `stopSpeed = clamp(0, startSpeed)`, `coastSeconds = max(0)`, `coastMinFrames = max(0)`, rates `> 0 else default`. A default-constructed/zero settings object therefore still behaves (an idle character can never be "permanently moving").

Public surface: `Tick(float speed, float deltaTime)`, `Reset()`, read-only `Phase` (0..<1), `Amplitude` (0..1), `TiltDegrees`, `IsMoving` (**the hysteresis/coast decision, i.e. "moving or bridging a gap"**; not the same as amplitude > 0 - documented on the property), `IsCoasting`.

Tick, in order:
1. **Input sanitation first:** `deltaTime` that is `NaN` or `<= 0` -> return; then clamp `deltaTime` to 0.1 s (NaN is tested *before* the clamp because `Mathf.Min(NaN, 0.1)` returns 0.1). `speed` that is not finite or is negative -> 0.
2. **Moving decision (hysteresis + coast).** Not moving: becomes moving when `speed >= startSpeed` (this is an explicit *not-moving -> moving edge*: `phase` resets to 0 **then** the phase advance for this tick applies; `lastSpeed`, coast timer and gap-frame count are cleared). Moving: while `speed >= stopSpeed` it stays moving, records `lastSpeed = speed`, and clears the coast timer and gap-frame count. When `speed < stopSpeed` it coasts: timer += dt, gapFrames += 1, and it stops moving only when **both** `timer >= coastSeconds` **and** `gapFrames >= coastMinFrames`. A stop clears the coast timer/gap count/`lastSpeed` (so a later start is not affected by stale coast state), and `Reset()` clears everything.
3. **Phase** while moving: `effectiveSpeed = speed >= stopSpeed ? speed : lastSpeed` (carry across gaps); `phase += effectiveSpeed * dt / strideLength; phase -= floor(phase)` (not `Mathf.Repeat`, which can return exactly 1).
4. **Amplitude** `MoveTowards(target)`; target 1 while moving (including coasting), else 0; rise/fall rates.
5. `TiltDegrees = sin(2*pi*phase) * peakDegrees * amplitude`.

Identical to the spike's behaviour except: the hysteresis band, the coast bridging, the phase-reset-on-start edge (both start at phase 0 from rest, so tick-for-tick parity at constant speed holds), and the sanitation.

**Velocity, never Status** (Phase 1 hand-off): the state takes a float speed and cannot see `MoveStatus`, so a claim wait (`Moving`, zero velocity) cannot start a rock by construction; the driver test proves the wiring.

### 2. `RockingMotionDriver` (`Runtime/Gameplay/Presentation/`, namespace `CRPG.Gameplay`)

Lives in `CRPG.Gameplay` (D1): it already references `CRPG.Navigation` and `CRPG.Presentation`; same home as `FacingController` and `CharacterAnimationPresentationHook`. **Zero public-API change** to `IMovable`/Core.

- Fields: `[SerializeField] Transform rockPivot; [SerializeField] RockingMotionSettings settings = new();`
- **Public read-only accessors** (mirroring `GridCharacterMotor.Speed`/`ClaimWaitSeconds`): `Motion` (the live `RockingMotionState`), `RockPivot`, `Settings`. Tests assert on `Motion`, not on reflection - `rockPivot.localRotation` alone cannot show "still rocking" (tilt crosses zero twice per cycle).
- `Awake`: create the state; resolve `movable = GetComponentInParent<IMovable>(true)`.
- `Update`: while `movable` is unresolved, re-resolve **every frame** (three parents is cheaper than a visible 1 s latency) and emit the "no IMovable" warning once, then no more. Read `Snapshot` **once into a local** (`GridCharacterMotor.Snapshot` recomputes `RemainingDistance` per get); `speed` = horizontal magnitude of `Velocity` (Y zeroed); `Motion.Tick(speed, Time.deltaTime)`; write `rockPivot.localRotation = Euler(0,0,TiltDegrees)` **only if it differs** from the current value (an idle level character dirties no transform).
- Only rotation is written. The feet offset is prefab-authored; CharacterRoot and its collider are never touched (click targeting can't mis-hit a tilted sprite).
- Scaled `Time.deltaTime` only: `timeScale = 0` freezes the rock.
- `OnDisable`: `Motion.Reset()`; reset the pivot to identity **null-safely** (`if (rockPivot != null)`; the pivot may already be destroyed during teardown, and an exception from `OnDisable` would fail the enclosing test).
- Missing `rockPivot`: one warning, then idle (never throws in `Update`).
- `OnValidate`: re-validate settings (`new RockingMotionState(settings)`-style normalization, log once on correction).

### 3. `CameraFacingBillboard` (`Runtime/Presentation/Sprites/`)

- `[DefaultExecutionOrder(100)]`, `LateUpdate`: `transform.rotation = camera.transform.rotation`. Runs after `IsometricCameraRig.LateUpdate` (order 0) and after `PartyCameraFollowBehaviour` (-100): follow -> rig -> billboard.
- Camera: optional `[SerializeField] Camera cameraOverride`; else `Camera.main`. **A destroyed camera is not reference-null**, so all checks use Unity's overloaded `==` (`if (cam == null)`, never `cam is null` / `?.`). While no camera exists it re-resolves every frame (cheap) but logs the warning **once** (then again only after a camera has been seen and lost).
- Position and scale untouched. FacingController's root rotation is overwritten in world space each LateUpdate - the intended decoupling (Finding 3).
- Prefab geometry this depends on, recorded so Task 2.5 reproduces the approved spike instead of re-deriving it (`LookSpikeBuilder.cs:80-86`): `Card.localPosition (0, 0.02, 0)` (the billboard component sits here), `RockPivot.localPosition (0, -0.5, 0)`, `Layers.localPosition (0, 0.5, 0)`. The card must be `VisualRoot`'s first child (Finding 4). A future X/Z offset on the card would let `FacingController`'s root Y-rotation swing it - harmless today, noted.

## Test plan (tests written first; hand mutation checks recorded in the results)

### EditMode `RockingMotionStateTests` (`Tests/Editor/`)
- Idle stays exactly zero (`TiltDegrees`, `Amplitude`, `Phase`).
- Moving ramps amplitude at `amplitudeRiseRate` (assert the value after N ticks).
- **Start edge:** the first moving tick from rest gives `Phase == speed*dt/strideLength` exactly (reset *then* advance) and amplitude `== riseRate*dt`.
- **Phase advances with distance, not time:** same total distance at speed 1 vs 2 (half the time) gives the same phase; `strideLength` scales it.
- **Real stop is monotonic-then-rest (Opus A1):** after a real stop the sprite rocks on for at most `coastSeconds` and then `Amplitude` is monotonically non-increasing to exactly 0 within `coastSeconds + 1/fallRate + tolerance`; and *no lean is pinned*: over the whole post-stop window the phase keeps advancing (never a frozen non-zero tilt with a stationary phase for longer than one tick).
- **Hysteresis:** speed between `stopSpeed` and `startSpeed` neither starts a stopped rock nor stops a moving one.
- **Coast bridging (B2 from the phase review):** while moving, zero speed for **1 frame, 3 frames** does not stop it; amplitude stays 1; phase keeps advancing at the last speed across the gap (continuity assertion: the tilt sequence across the gap equals the no-gap sequence); a **0.5 s** zero gap stops it.
- **Frame floor:** at a slow tick (dt 0.1 s) a 2-tick zero gap exceeds `coastSeconds` in time but not `coastMinFrames`, so it does not stop; a 4-tick gap does.
- A non-zero speed inside a gap resets the timer (0.15 s gap, blip, 0.15 s gap does not stop - no accumulation across blips).
- **Coast lifecycle:** move -> coast expires -> stop -> move again -> a single zero-speed frame does not stop it (stale coast state cleared); `Reset()` mid-coast clears it.
- Inputs: `deltaTime` 0 / negative / `NaN` (tested before clamp) are no-ops; `speed` `NaN`, `+Infinity`, negative are treated as 0 and never produce a `NaN` tilt; a 5 s hitch tick advances at most 0.1 s worth.
- **Sanitation:** default-constructed settings behave; zeroed settings (start 0, peak 0, coast 0, stride 0, rates 0, stop > start, negatives) produce finite output over a 1000-tick sweep and an idle character never rocks.
- **Spike-parity oracle (replaces v1's unsatisfiable "reach 6 degrees" guard):** the test ports the five lines of `SpikeCharacter.Update` (rise/fall MoveTowards, phase += v*dt/stride, tilt formula) as an oracle and asserts **tick-for-tick equality** from rest at a constant speed above `startSpeed`; plus invariant `|TiltDegrees| <= peakDegrees + 1e-4` over a long sweep; plus max `|tilt|` over three strides at dt = 1/240 is within 1 % of `peakDegrees`.
- Mutation checks (by hand, recorded): remove the coast -> the 3-frame test fails; phase-from-time instead of distance -> the speed-1-vs-2 test fails; freeze phase during coast -> the continuity test fails; drop the timer clear on stop -> the lifecycle test fails.

### PlayMode `RockingMotionDriverTests` (`Tests/Runtime/`)
`FakeMovable : MonoBehaviour, IMovable` with a settable `Snapshot` for deterministic cases; the real `GridMotorContractTests` fixture helpers (`Arena` / `RegisterMover` / `StartMove` - which builds the **real `MoveActionExecution`** - / `WaitForActionToEnd`) for the integration cases (reused, not rebuilt).
- Velocity above threshold tilts the pivot; velocity 0 leaves it exactly identity (and the transform is not rewritten while identical).
- **`Status == Moving` with zero velocity never tilts** (claim-wait shape) - the Phase 1 hand-off rule at the wiring level.
- Vertical-only velocity -> no tilt.
- `Time.timeScale = 0` -> no change while frozen; resuming continues.
- Disable -> pivot identity, `Motion` reset; re-enable -> starts level. **Destroying the pivot then disabling the driver throws nothing** (null-safe `OnDisable`).
- No `IMovable` / no `rockPivot` -> exactly one warning each (`LogAssert.Expect`, bare scene so `NoUnexpectedReceived` is not tripped by strays), no exceptions across frames; a movable added later is picked up the next frame.
- CharacterRoot position/rotation and `Collider.bounds` unchanged by rocking.
- **Integration, real `GridCharacterMotor`, multi-cell move:** `Motion.IsMoving` while moving, settles to identity after arrival (bounded simulated time).
- **Integration, one-cell legs through the real `MoveActionExecution` (Opus B2):** a multi-cell plan forced into one-cell legs; sample `Motion.IsMoving` and `Motion.Amplitude` every frame; assert `IsMoving` is true across **every** leg boundary (including the measured 2-3-frame gaps) and that amplitude never dips; then settles after the last leg. The test also records the observed maximum consecutive zero-velocity frame count, so the plan's measured "2-3 frames" becomes an asserted fact.
- (NavMesh parity test cut: the driver only reads `IMovable.Snapshot`, which `FakeMovable` covers; a runtime bake would add process-global NavMesh teardown risk for no coverage the fake doesn't give.)

### PlayMode `CameraFacingBillboardTests` (`Tests/Runtime/`)
Cameras are tagged `MainCamera` and destroyed in `TearDown` (a leaked tagged camera would poison other tests).
- Card world rotation equals camera rotation at pitch 90 and 85; follows a camera yaw change.
- **Real `IsometricCameraRig` at pitch 90:** build a `CRPGCameraSettings` and set `minimumPitch`, `maximumPitch` **and** `defaultPitch` to 90 by reflection (`CameraRigState.Pitch` is set once from `Clamp(defaultPitch, min, max)`, so raising only the max leaves the rig at 55), and **assert `rig.Rig.Pitch == 90f` before** asserting the card matches the rig's camera the frame after the rig writes it (proves both the pitch and the execution order).
- Rotating the parent changes nothing about the card's world rotation.
- `cameraOverride` beats `Camera.main`; destroying the resolved camera **throws nothing across the following frames** and re-resolves to a new `Camera.main`; no camera at all -> no throw, exactly one warning.
- Position and local scale untouched.
- `DefaultExecutionOrder(100)` reflection guard so a refactor can't reorder it before the rig.

## Deliverables and acceptance

Files: `Runtime/Presentation/Sprites/{RockingMotionSettings.cs,RockingMotionState.cs,CameraFacingBillboard.cs}`, `Runtime/Gameplay/Presentation/RockingMotionDriver.cs`, `.meta` files, and the three test files. No asmdef changes (verified: Presentation and Gameplay references, and both test asmdefs, already suffice).

Acceptance: new tests green; **full EditMode and PlayMode suites still green** (baseline from Phase 1 close: EditMode 728; PlayMode 288 + 3 skipped - to be re-measured at the start of the task, not assumed); mutation checks recorded; the feel is not re-litigated. The visual parity check in `BarrowsEnd` (swap a spike walker for the real components) is folded into Task 2.5's prefab work so 2.1 stays code-only.

## Risks / open questions

1. **Update order.** Nothing orders the driver against the motor; the coast frame floor (4) and the measured 2-3-frame gap absorb it, and tests wait an extra frame rather than adding `DefaultExecutionOrder` games.
2. **Trailing rock after a real stop** (<= `coastSeconds`) is the visible cost of bridging leg gaps. It is the one behaviour the user should watch at Gate B; `coastSeconds`/`coastMinFrames` are inspector-tunable.
3. **Two movables on one wrapper** (mis-authored): `GetComponentInParent` picks the first; the 2.5 builder refusal prevents the state, this task does not guard it.
4. **`Camera.main` in multi-camera scenes** (the Phase 4 creator-preview camera): the override field exists for that.
5. **Baseline counts unverified this session** (EditMode 728 / PlayMode 288+3 are from the Phase 1 close): first step of implementation is re-running both suites.

## Review record (Opus, 2026-09-19)

- **A1 coast froze phase, pinning a genuine stop mid-lean** - fixed (coast carries phase at last speed; real-stop test asserts no pinned lean and monotonic decay; trailing rock recorded as accepted cost).
- **A2 spike-parity guard unsatisfiable** - replaced with a tick-for-tick oracle test + invariants.
- **A3 no `OnValidate` on a nested serializable; zeroed struct = permanently moving** - `RockingMotionSettings` is a class with initializers; the state sanitizes every field; driver `OnValidate` re-validates.
- **A4 pitch-90 rig test would run at 55** - set min/max/default by reflection and assert `Rig.Pitch == 90`.
- **B1 phase-reset ordering ambiguous** - specified as a not-moving->moving edge, reset then advance; exact-value test.
- **B2 coast-timer lifecycle** - cleared on stop/start/`Reset()`; lifecycle test added.
- **B3 `+Infinity`/NaN-before-clamp** - `!IsFinite` -> 0; NaN dt tested before the clamp.
- **B4 leg gap never measured; time-based coast vs frame-based gap** - measurement recorded (2-3 frames), `coastMinFrames = 4` added, integration test asserts the observed gap.
- **B5 nothing assertable on the driver** - public `Motion`/`RockPivot`/`Settings` accessors.
- **B6 `OnDisable` not null-safe** - guarded, test added.
- **B7 billboard destroyed-camera semantics / warning spam** - `== null` throughout, one warning, destroyed-camera test.
- **B8 NavMesh parity test disproportionate** - cut, reasoning recorded.
- **C1** prefab geometry recorded; **C2** `IsRocking` replaced by defined `IsMoving`/`IsCoasting`; **C3** `floor` not `Repeat`; **C4** float-fragile boundary test replaced by 0.5x/2x cases; **C5** re-resolve every frame, warn once; **C6** `LogAssert` approach adopted; **C7** snapshot cached in a local; **C8** transform write skipped when unchanged; **C9** billboard-test cameras tagged and destroyed; **C10** baselines to be re-measured first (risk 5).
- Also adopted from the review: reuse `GridMotorContractTests` helpers and drive one-cell legs through the real `MoveActionExecution` (the preferred option, cheap after all).
- Verified correct by the review and unchanged: `IMovable`/`MotionSnapshot` API, `ClaimWaitSeconds` 0.5, `NavMeshCharacterMotor` reports real velocity, hysteresis defaults 0.1/0.05, execution-order chain, asmdef references, billboard geometry, hop cut, `Sprites` rename, `GetComponentInParent<IMovable>(true)`, scaled-time PlayMode pattern.

## Results (built 2026-09-19)

**Files:** `Runtime/Presentation/Sprites/{RockingMotionSettings,RockingMotionState,CameraFacingBillboard}.cs`, `Runtime/Gameplay/Presentation/RockingMotionDriver.cs`, tests `Tests/Editor/RockingMotionStateTests.cs` (21), `Tests/Runtime/RockingMotionDriverTests.cs` (11), `Tests/Runtime/CameraFacingBillboardTests.cs` (10). No asmdef changes.

**Suites:** EditMode 749/749 (728 baseline + 21); PlayMode 309 passed + 3 skipped sample-import fixtures, 0 failed (288 baseline + 21). Baselines re-confirmed by subtraction.

**Measured:** through the real `GridCharacterMotor` + real `MoveActionExecution`, the longest zero-velocity run between legs is **3 frames** (the review's 2-3), and the integration test asserts the gap really occurs (`> 0`) and that the rock never breaks across it. Every path corner is its own leg (an 11-corner path for a 9-cell serpentine), not just reactor crossings. Note headless frames are ~1 ms, so the time half of the coast is not exercised there; the frame floor and the EditMode tests cover it.

**Mutation checks (all caught):** coast removed -> 7 tests fail; phase frozen during coast -> 3 fail (gap bridging x2, real-stop); phase from time instead of distance -> 2 fail (distance, spike-oracle); stale coast state not cleared -> 1 fails (after tightening the lifecycle test to one restart tick then one zero frame - the first version of that test could not fail and was fixed).

**Deviations found while building (all small):**
- Restarting while the amplitude has not finished easing out **continues the phase** instead of resetting to 0 (a reset would pop the tilt). From complete rest it still starts level, as planned.
- The driver compares the tilt **float**, not quaternions: `Quaternion ==` treats sub-0.1 degree differences as equal, which left the pivot stuck at a tiny residual tilt instead of exactly level (caught by the "settles to exactly level" test).
- `[UnityTest]` cannot take `[TestCase]`; the two pitch cases are two tests. `Camera` needed a using-alias inside the test namespace because `CRPG.Camera` is a namespace.
- A test-authoring bug (an `else` binding to the wrong `if`) hid the gap measurement at first; the "gap really occurs" assertion is what exposed it.

**Manual check still owed:** a visual parity check of the real components against the spike in `BarrowsEnd` - deferred to Task 2.5 (prefab work) as planned. Nothing committed.

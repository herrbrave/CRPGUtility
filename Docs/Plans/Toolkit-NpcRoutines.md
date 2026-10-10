# Toolkit: NPC ambient routines (wander, work loop)

Status: DESIGN v2 (2026-10-09), Opus-reviewed: Section 8 supersedes anything above it that it contradicts. Awaiting the user's go. Decisions already made by the user: lives in the toolkit with Viking Raid as the first user; configured per spawn point in the scene; first slice = Wander in an area + Work loop (carry a prop between spots). Out of scope: time-of-day schedules (no clock exists), waypoint patrol, reacting to the player, NPC-to-NPC behaviour.

## 1. What exists (verified in code)

- `NpcSpawnPoint { SpawnId, Definition, Anchor }` in `ILocationContent.cs`; `LocationContent` holds the list. `WorldSpawnService.SpawnNpc` builds a `CharacterAgent` (Motor = `IMovable`, `View`, `Facing`). A restored/revisited spawn is re-bound at its last-known transform (`TryGetLastKnownTransform`).
- Movement: `IMovable.SetDestination` returns a `MoveResult` (RequestId); `Snapshot.Status` is Pending/Moving/Arrived/Blocked/Invalid/Cancelled. A partial path reports `Blocked`, never `Arrived`.
- Animation: `CharacterPlayableAnimator.RequestAction(cueId, onOutcome)` plays a one-shot cue with a Completed/Cancelled callback; looping cues via `SetStance`; baseline idle/moving follows the motor automatically. A cue missing from the profile returns `AnimationRequestToken.None` (no-op).
- No ambient/idle behaviour system exists. `BehaviorDefinition` / `UtilityAIDecisionProvider` are combat turn AI and are not reused.
- The universal animation library already imported (UAL1_Standard, CC0) has `PickUp_Table`, `Push_Loop`, `Fixing_Kneeling`, `Idle_Talking_Loop`, `Dance_Loop`, `Walk_Formal_Loop`: enough for the work loop with no new art. There is no carry-walk clip: the prop is attached to the hands while the normal walk plays.

## 2. Data (authored on the spawn point)

`NpcSpawnPoint` gains one optional field `Routine` (`NpcRoutineSpec`, serializable, default = Stand: today's behaviour, so every existing scene and save is unchanged). Plain data, no behaviour:

```
NpcRoutineSpec
  Kind          Stand | Wander | WorkLoop
  // Wander
  AreaCentre    Transform   (null = the spawn Anchor)
  AreaRadius    float       (m, default 6)
  PauseMin/Max  float       (idle seconds between legs, default 2..6)
  IdleCues      List<cue id>  (optional flavour played during a pause, e.g. look-around; empty = just idle)
  // WorkLoop
  Stops         List<WorkStop>
  Repeat        bool (always true for now; loop forever)
  Seed          int         (0 = derived from SpawnId)

WorkStop
  Point         Transform   (where to stand; its forward = facing while working)
  Action        None | PickUp | Drop | Work
  Seconds       float       (how long the action/pause takes if the cue has no natural length)
  CueId         string      (animation cue; empty = just stand)
  Prop          GameObject  (PickUp only: prefab shown in the hands)
  PropBone      HumanBodyBones (default RightHand) + local offset/rotation
```

Wander picks a random NavMesh point in the circle (`NavMesh.SamplePosition`, retry a few times, must be reachable: `NavMeshPath` complete), walks, pauses, repeats. WorkLoop cycles the stops in order. A prop is a cosmetic instance created at PickUp and destroyed at Drop (the crates on the dock are scenery; moving the real scene objects is a later slice).

## 3. Runtime

`NpcRoutineRunner : MonoBehaviour` on the spawned agent, a small state machine ticked in `Update` (no coroutines, so suspension and save are simple): `Idle -> Moving -> Acting -> Idle`. It talks only to `CharacterAgent.Motor` (`IMovable`) and `CharacterPlayableAnimator`. Created by `NpcRoutines.Attach(agent, spec, ...)`, called from the project bootstrap right after `SpawnNpc` (Viking Raid's `VillageBootstrap`, one line). Not wired into `LocationInitializer` in this slice, so Barrow's End is untouched until it opts in.

Rules:
- **Arrival** is `Snapshot.Status == Arrived`. `Blocked`, `Invalid`, `Cancelled`, or no progress for N seconds counts as failed: pick another target (wander) or retry the stop up to 3 times then skip it (work), logging once at Debug level. It never throws and never stalls the village.
- **Suspension**: `Suspend(object owner)` / `Resume(owner)` (ref-counted set). Suspended = stop the motor, cancel the current action, drop to idle; Resume continues from the next step. Who suspends: dialogue (the talk interaction), a tactical encounter, the project (menus/pause). The toolkit provides the hook; Viking Raid has no dialogue wiring yet, so slice 1 ships suspend for combat (the runner subscribes to the encounter start/end events) and dialogue becomes a one-liner when the dialogue runtime lands.
- **Facing**: arrive at a stop and turn to `Point.forward` through `FacingController` (never set rotation directly).
- **Props**: attached to the animator's humanoid bone; destroyed on Drop, on Suspend-with-cancel, on agent destroy/release. Never saved.
- **Determinism/tests**: `System.Random` seeded from the spawn id; a `Func<float>` clock and an `IMovable` fake make the state machine unit-testable without Unity physics.
- **Save/restore**: only position is saved today (`WorldSpawnService` last-known transform). The routine restarts from its first step at the restored position; the prop is lost. Acceptable for ambient life; stated, not hidden.
- **Defeat/death/disabled agent**: the runner disables itself on `CharacterAgent.OnDisable` / defeat, and does nothing unless the agent is bound and its motor is on the NavMesh.
- **Cost**: the village has ~3 villagers; per-frame work is a status check. No allocation in the steady state (reuse a `NavMeshPath`). Spec says nothing about hundreds of NPCs; if that appears, tick on a stagger.

## 4. Authoring (editor)

- The `NpcSpawnPointDrawer` row gains a Routine foldout: Kind dropdown, then only the fields of that kind. Wander: a Pick button that arms the Scene view, a radius field.
- Gizmos (`LocationGizmos`): wander circle on the ground (projected onto the first physics hit), numbered stops linked by arrows with the action as a label, same colour as the owner NPC's marker; only for the selected spawn point plus a faint version for all, to keep the scene readable.
- `Place NPC` tool: after placing, an optional "Add work stops" sub-mode (click to append a stop, Enter to finish) is a **nice-to-have for slice 2**; slice 1 creates stops as child transforms through an `Add Stop` button next to the list and the user moves them with the normal gizmo.
- `LocationValidator` additions: errors: WorkLoop with <2 stops, a PickUp with no Prop, a Drop with no earlier PickUp in the loop, a null stop point. Warnings: a stop or wander centre off the NavMesh / not reachable from the anchor, wander radius <1 m, a CueId missing from the NPC visual's animation profile (it would silently do nothing), a wander area overlapping a different NPC's by more than half.

## 5. Viking Raid slice

- Villagers' animation profile gets cues: `work-pickup` (PickUp_Table, one-shot), `work-fix` (Fixing_Kneeling, one-shot looping via repeated Seconds), `idle-talk` (Idle_Talking_Loop). The animations ride the existing UAL retarget used for the heroes (check the retarget works on the Viking rig and the Sidekick humans; wanted: no sliding feet, correct yaw).
- Dock worker: WorkLoop between two crate piles with a crate prop (Synty prop prefab, already in the project, referenced by the scene only). Two or three villagers wander around their houses.
- Routine configuration lives in `VikingVillage.unity`; `VillageBootstrap` calls `NpcRoutines.Attach` for each spawn point with a routine.

## 6. Testing

Unit (EditMode, fakes): wander picks within radius and avoids unreachable points; arrival, blocked and timeout transitions; work loop order incl. skipping a stuck stop after 3 retries; suspend/resume mid-move and mid-action (prop destroyed); prop lifetime; determinism with a fixed seed; Stand does nothing. Validator rules. A PlayMode test on the real village: spawn, run 60 simulated seconds, assert each routine NPC moved, stayed inside its area / hit its stops, the prop appeared and vanished, and nobody ended off the NavMesh. A contact-sheet capture of the dock worker carrying a crate to check the hand attachment against the walk AND pickup frames (lesson: judge grips on the animation). Mutation checks on each guard.

## 7. Open questions for review

1. Is a plain `Kind` enum + fields on the spawn point right, or should routines be `[SerializeReference]` polymorphic specs so a game can add its own kind without touching the toolkit? (Leaning enum now, `INpcRoutine` runner interface later.)
2. Should the runner live in `CRPG.Gameplay` (it needs `CharacterAgent`) — and is `CharacterPlayableAnimator` (Presentation) reachable from there without a new dependency?
3. Suspension on dialogue: where should the hook be so the dialogue runtime piece can use it without another design pass?
4. Anything in the save/restore story (re-bind after a location revisit) that would make the runner double-attach or fight `BindRestoredView`?

## 8. Opus review resolutions (2026-10-09; these override Sections 2-6 where they differ)

The review verified the Section 1 facts and found the mechanism assumptions below wrong or missing.

**Animation (fixes Section 3/5).**
- `SetStance` takes a STANCE id resolved through `StanceLocomotionSet`; a plain cue id silently falls back to idle. A held work pose (kneeling, talking) is therefore authored as a `StanceLocomotionSet` (stance `work-fix` -> idle `Fixing_Kneeling`, moving = the normal walk) and held with `SetStance` for the stop's Seconds. `RequestAction` is only for true one-shots (`PickUp_Table`); a Loop=true cue through `RequestAction` plays once and restarts with a visible hitch.
- The runner remembers the stance it set and clears it only if the animator's stance is still its own (never clobbers a combat stance).
- `RequestAction` can return a valid token and never call back (driver unbound until `CharacterView.Start`; a model swap cancels the action). Acting therefore ALWAYS ends on `max(Seconds, clip length) + margin`; a None token degrades to a timed wait; Cancelled counts as the step done.
- Cues are `DefinitionId`, not string. The villagers share `HeroAnimationProfile.asset`: add the cues (and stance set) to it additively rather than inventing a villager profile. Check `ModelYawCorrection` on `VikingNpcBase` does not fight the turn to a stop's facing.

**Facing.** `FacingController` keeps non-Motion requests until cleared. The runner requests on arrival and `ClearFacing`s on leaving Acting, on Suspend and on disable. Tier: a new dedicated `Ambient` tier (the enum is not serialized, so inserting is safe) so it never shares a tier with `InteractionController` / `AbilityActionExecution`.

**Motion ownership.** Arrival = `Status == Arrived && RequestId == my request id`. A foreign RequestId means someone else owns motion: suspend. `Stop()` only if the in-flight request is the runner's own. RVO-stuck agents never report Blocked, so the no-progress timeout is required. Stops are pre-validated with `TryCalculatePath` like wander points; a stop that is unreachable at attach is skipped with one log line. `SetDestination` returns Invalid while the agent is inactive or off the mesh: guard on that instead of burning retries.

**Lifecycle (fixes Section 3 "created by Attach after SpawnNpc").** Restored and revisited views are ALWAYS fresh GameObjects with no runner. So the API is an idempotent `NpcRoutines.AttachAll(ILocationContent, WorldSpawnService)`: walk the spawn points, resolve each live agent by spawn id (`TryGetCharacterId` + `OwnedAgents`), skip any that already has a runner. Called after `LocationInitializer.Prepare`, after a load, and by `VillageBootstrap`. It must tolerate an inactive GameObject (no reliance on `Awake`). The runner lives on the view, so `ReleaseSpawnsInScene` destroys it; props, stance and facing are cleaned in its `OnDisable`/`OnDestroy`. Every tick is gated on `agent.IsBound && agent.isActiveAndEnabled` (a defeated agent keeps its MonoBehaviours alive for the death-presentation delay). Restart from step 0 at the last-known position; the prop is lost; stated limitation.

**Suspension (answers Q3, replaces the combat-only plan).** A per-session registry keyed by entity id: `AmbientRoutines.Suspend(RuntimeEntityId, object owner)` / `Resume`, ref-counted per owner. `InteractionController` suspends when a request targeting a `WorldInteractable` is ACCEPTED (so the NPC stops during the party's approach; a wandering NPC would otherwise make the approach fail with `ApproachInsufficient`) and resumes on its existing `RequestEnded`. Dialogue, loot and stores need nothing extra. Combat suspension is deferred: the village is explicitly no-combat; the same registry serves it when a game mixes the two (one director subscribing once, not a bus subscription per runner).

**Testability (replaces Section 6's claim).** The state machine is a plain C# class with ports: `IMovable`, `INavMeshService` (sample + path, already faked in the location tests), an animation port, a facing port and a clock; the MonoBehaviour is a thin adapter. Seed from the spawn id with FNV-1a, not `string.GetHashCode`. Drop the "no allocation" claim (`TryCalculatePath` returns an array; fine per leg).

**Authoring / data.**
- `Routine` is reset by `LocationPlacement.AddSpawn` (`InsertArrayElementAtIndex` copies the previous element, which would hand a new NPC the last NPC's stops) and by `VillageSceneAuthor`; `Kind` Stand = 0 and the field has an initializer so code-built spawn points are never null. Validator warning for a stop Transform shared by two spawn points; stop/centre Transforms that are prefab assets or in another scene are errors (mirrors the anchor rules); PickUp while already holding a prop is an error.
- Enum + fields confirmed (Q1). Old scenes deserialize the new field as Stand, so Barrow's End and the TestProject scenes are unchanged on disk. A later `Custom` kind can carry a `[SerializeReference]`.
- Grip data (`PropBone`, offset, rotation) moves off the stop onto a small `CarriedProp` asset (prefab + bone + local pose), because a grip depends on the prop and the rig, not the stop. Bone via `agent.View.Animator.GetBoneTransform` after an `avatar.isHuman` check; a model swap re-attaches or drops the prop. No carry clip exists in UAL, so slice 1 uses a one-hand prop (sack, bucket, tool) and the grip is judged on walk AND pickup frames.
- NavMesh motors only: a validator warning when the visual prefab's motor is not `NavMeshCharacterMotor` (Barrow's End uses grid motors).
- Runner lives in `CRPG.Gameplay`; no new asmdef reference needed (Q2).

**Avoidance.** Villagers, heroes and goblins all use avoidance priority 50, so equal priorities shove each other, and a worker standing at a stop acts as an immovable obstacle. Routine NPCs get a less important priority (70) on attach, restored on detach. PlayMode assertion: the party can walk past the dock worker. Authoring note: keep stops off chokepoints. Watch the existing party-walk village tests for flakes.

**Editor.**
- The drawer revalidates the WHOLE location every 400 ms per row: the new reachability, profile and overlap rules run only in the menu validator (and on demand), not in the drawer. The drawer's `StartsWith("spawnPoints[1")` row filter wrongly matches row 10 (existing bug): fix to match the exact index plus `]`.
- Marker gizmos are green/red, not per-NPC, so the routine overlay uses its own colour per spawn point. "Selected" on `DrawGizmo` means the LocationContent object, so the routine overlay is drawn for the spawn point whose anchor or stop is selected, faint for the rest.
- The validator reads the animation profile's private `profile` field through `SerializedObject` (no public getter).

**Revised slice order.** (1) Pure state machine + ports + tests. (2) Adapter, `Ambient` facing tier, stance/cue authoring on `HeroAnimationProfile`, `CarriedProp`, registry + `InteractionController` hook. (3) `AttachAll` + `VillageBootstrap` + `AvoidancePriority`. (4) Spec + drawer + gizmos + validator + AddSpawn reset. (5) Village content (dock worker, wanderers), PlayMode run, contact sheet of the carry. Combat suspension and the Place-NPC "add stops" sub-mode are slice 2.

## 9. As built (2026-10-09)

Built in the Section 8 order and verified: toolkit EditMode 1220/1221 (the one skip is the existing graphics-only fixture), the new PlayMode suites green, Viking Raid EditMode 203/203 and `VillageRoutineTests` 4/4 on the real village.

**What exists.** Runtime (`crpg-toolkit/Runtime/Gameplay/Ambient/`): `NpcRoutineSpec` (+ `WorkStop`, `RoutinePlan`), `CarriedPropDefinition`, `AmbientRoutineMachine` (plain C#, ports `IRoutineAnimation` / `IRoutineFacing` / `IRoutineProp` plus `IMovable` and `INavMeshService`), `NpcRoutineRunner` (the thin Unity adapter), `RoutineAdapters`, `NpcRoutines` (`Attach`, idempotent `AttachAll`, FNV-1a `Seed`), `AmbientSuspension` (also `GameSession.Ambient`). Existing code touched, all additive: `NpcSpawnPoint.Routine`, `FacingPriority.Ambient` (inserted above Motion; the enum is not serialized), `CharacterPlayableAnimator.TryGetCue` / `HasStanceLocomotion`, `InteractionController` (suspends the target NPC from acceptance, resumes when the request ends, including a rejected approach). Editor (`Editor/Location/`): the validator's routine rules (`deepRoutineChecks` off in the inspector's per-row dot), the spawn row's routine section with an Add stop button, `LocationPlacement.AddStop` / `ResetRoutine` (a new spawn point never inherits the previous NPC's routine), routine gizmos (per-NPC colour, strong when the anchor, a stop or the centre is selected). Viking Raid: `VillageBootstrap` calls `NpcRoutines.AttachAll`; `Viking Raid/Village/2. Author NPC Routines` (`VillageRoutineAuthor`) gives Orm the dock loop (pick a sack up at the pile, put it down at the cart), Hild and the elder wander.

**Deviations from the design, on purpose.** `IdleCues` dropped (nothing used them). A work loop of ONE stop is allowed (the design said fewer than two stops was an error; a worker at a single anvil is valid); zero stops is the error. The "wander areas overlap" warning was dropped (low value). The review's claim that the drawer's row filter `StartsWith("spawnPoints[1]")` also matches row 10 was wrong: the closing bracket is part of the prefix, so the filter was already exact; I added a helper, found it equivalent, and removed it again. The dock worker's ANCHOR is moved onto the dock when his routine is first authored: the demo put him beside the party start, which is a long walk round the hill from the quay by NavMesh.

**Findings worth keeping.**
- `NavMeshAgent.remainingDistance` is Infinity until the agent is on the last stretch of a multi-corner path, so a stall check that needs a finite remaining distance reports every long walk as stalled (Orm kept being cancelled and re-ordered). Progress is now "remaining distance fell OR the NPC moved 0.25 m".
- Opening a scene (`OpenScene` Single) unloads assets nothing references, turning an earlier `LoadAssetAtPath` into a fake null: the sack reference silently saved as `{fileID: 0}`. Ensure/load assets AFTER opening the scene.
- Mutation lesson (again): apply mutants one test-family at a time. R4 (wrong facing tier) masked R5 and M11 (facing never cleared) in the same run, and I1 (never suspend) made I3's "rejected approach does not stay frozen" pass vacuously; each was killed once run alone.
- PlayMode with graphics runs at about real time, so a 120 s simulated budget costs about 120 s real: raise `Time.timeScale` (the village routine tests use 2-4).
- The sack is carried on the UpperChest bone (a shoulder carry), with the grip derived from the rig's real bone axes (UpperChest local X = root down, Y = root back, Z = root right) and the sack mesh bounds (base at y 0, 0.74 m tall), then judged on the walk, pick-up and drop frames (`VikingRaid_Logs/spikes/routine_*.png`). The clavicle bone was tried first and rotates with the arm, which swung the sack from across the chest to the side.

**Mutation checks.** 21 runtime/adapter/interaction mutants and 10 editor mutants, every one killed except `AttachAll`'s defeated-NPC guard (equivalent in the test: a defeated NPC's view is already out of `OwnedAgents`; the guard still matters during the death-presentation delay).

**Not done / limitations (honest list).**
- No test that the PARTY can walk past a working villager (the authored `RoutineAvoidancePriority` 70 and the PlayMode priority test cover the mechanism, not the pathing outcome).
- Combat suspension is not wired (the village has no combat); the registry supports it. Dialogue needs nothing extra once the dialogue runtime exists: the interaction hook already suspends.
- The carrying hand just swings; the other hand does not steady the sack. Fine at game distance, visible in a close-up.
- A saved/restored or revisited NPC restarts its loop at the first stop and loses the prop; `AttachAll` must be called after `LocationInitializer.Prepare` and after a load (Viking Raid's village has neither yet; Barrow's End does not opt in).
- NavMesh motors only (validator warning for a grid body). A work stop's animation is only checked in the deep validator.
- "Add stops by clicking in the Scene view" in the Place NPC tool is slice 2; the interactive Scene-view drawing of the new gizmos and the drawer's look are unverified by a person.

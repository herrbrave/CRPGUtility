# Phase 2 Detailed Design: Sprite Presentation (billboards, rocking motion, layered characters)

> Status: DRAFT v2, post-Opus review, pending user review. Companion to `Docs/Plans/2D-Campaign-Implementation.md`
> (master plan) and successor to `Docs/Plans/2D-Campaign-Phase1-GridMovement.md` (closed). Workflow: draft -> Opus
> review -> revise -> user review -> implement one task at a time with a checkpoint after each.
>
> **v2 note:** v1 got a hostile, source-verified Opus review. It confirmed the asset bet and the billboard/sort math
> and found: one real defect (the death cue was wired to an event most defeated creatures never raise), two design
> interactions asserted rather than designed (rocking vs. one-cell combat legs; hop vs. depth sorting), a sorting
> contract deferred that the phase gate depends on, an equipment-slot mapping left unspecified, a builder update path
> that recreates the two-`IMovable` hazard, a cheaper alternative to the `IMotionSource` change, and an appearance-storage
> split that would be re-invented within a phase. This version folds every finding in; the "Review record" at the end
> lists each and its disposition. Treat this as the real plan, not v1 plus patches.

## Goal

Make grid-moving characters *look right* in the 2D campaign: an upright-looking, always-camera-facing sprite character
built from stackable appearance/equipment layers, that rocks side to side (South Park style) while moving, shows what it
is wearing, persists how it was created, and gives basic combat feedback — on top of the existing `CharacterAgent` /
`CharacterView` / event-bus architecture, with no new package dependency.

## Findings that shape this design (verified in source or on the actual assets)

1. **The modular pixel kit is `Roguelike characters` and it is a same-canvas layer kit.** (This corrects the master
   plan's earlier pointer at the vector `Character pack`.) The sheet `roguelikeChar_transparent.png` is 917x203 =
   54x12 cells of 16x16, 1px padding (same grid as the world tiles), 448 non-empty. Category blocks are separated by
   fully empty columns: cols 0-1 rows 0-3 = 8 bare **bodies = only 4 skin tones** (3 human + 1 green) x 2 mouths;
   cols 0-1 rows 5-11 = 14 **pre-made characters** (not layers); cols 3-4 = belts/shoes; cols 6-17 = **120 torso
   garments** (arms left transparent, so any skin tone works — clothes and armor are one block); cols 19-26 = 80 hair/
   beards; cols 28-31 = **36 helmets/hats**; cols 33-40 = **60 shields, all drawn on the right (x 6-15)**; cols 42-53 =
   **110 weapons, all drawn on the left (x 0-7)**. One pose, one facing (front), no left/right-hand variants — so
   "stack by z-order, no offsets" holds. **Consequences:** (a) there is **no head or face layer** (faces are baked into
   the bodies), so the GUI handoff's "Head / Body / Weapon / Class" creator rows must be re-mapped to what exists —
   *Skin tone, Hair, Outfit (garment + belt/shoes), Weapon* (helmet/shield are equipment, not creator choices) — settled at
   Gate A; (b) shield is locked right and weapon locked left, so off-hand weapons and two-handers have no art;
   (c) **the kit has no Act 2/3 enemies** (one green orc/goblin body; no skeletons, no necromancer) — see D9.
2. **Billboard geometry.** The camera is (near-)top-down orthographic looking down -Y. A front-facing character on a
   top-down map is a 2D card **parallel to the camera plane**: `card.rotation = camera.rotation` (a full billboard, any
   pitch; unmirrored). At pitch 90 the card lies flat in XZ with card-up = world **+Z** (verified:
   `Quaternion.Euler(90,0,0) * Vector3.up == +Z`). Consequences: (a) draw order cannot come from camera distance — it is an
   explicit y-sort by world Z; (b) **centre pivot** puts a 16x16 character in exactly its own cell (a bottom pivot would
   push it into the cell above); (c) with that rotation the card's +Z faces *away* from the camera, so it renders only with a
   **Cull Off** material — `Sprites-Default` is, `Universal Render Pipeline/Unlit` is not.
3. **`FacingController` stays untouched.** It only writes CharacterRoot's rotation; the card is world-rotated to the
   camera each frame. `InteractionController`'s `TurningToFace` gate and `ActionScheduler`'s `CommittedAction` facing keep
   working exactly as today.
4. **`CharacterView` binds a Model-with-no-Animator as a no-op** (null-safe) but has a stale-binding gap: swapping a
   Model that had an Animator for one that doesn't never calls `CharacterPlayableAnimator.UnbindPresentation()`
   (`CharacterView.cs:86-89`). It also auto-binds `transform.GetChild(0)` — the card must be VisualRoot's **first child**.
5. **`EquipmentVisualBinder` is 3D-shaped** (bones, skinned meshes, sockets), but its *contract* is worth mirroring:
   identical-tuple `Initialize` is a no-op, a different tuple while bound throws, subscribe to `ItemEquipped`/
   `ItemUnequipped`, reconcile from `PartyInventoryState.ItemsEquippedBy` on bind. Its `RecomputeVisibility` only hides
   *other equipment visuals* — it cannot hide a base-body region — and its `BodyRegionId`/`HiddenBodyRegions` fields therefore
   mean something narrower than "hide any layer".
6. **`CharacterWrapperBuilder` unconditionally adds `NavMeshAgent`, `NavMeshCharacterMotor`, a `CapsuleCollider`,
   `FacingController`, `EquipmentVisualBinder`, `CharacterPlayableAnimator`, and by its own rule "never removes
   anything"** — so updating an existing NavMesh wrapper to a grid one would leave two `IMovable`s.
7. **Death is not one event.** `DamageEffect` publishes only `CreatureDroppedToZeroHP`; `DeathSaveService` then applies
   Unconscious + death saves to *any* character; `CreatureDied` fires only on overkill >= max HP or three failed saves;
   `CreatureStabilized` fires on stabilization; there is no heal/revive event. (The TacticalEncounter sample bridges 0 HP to
   `CreatureDied` itself — a per-game choice.) A cue keyed only to `CreatureDied` leaves most defeated creatures standing.
8. **Motion is bursty in tactical play.** `MoveActionExecution` issues one `SetDestination` per planned stop (one cell
   per leg), and `GridCharacterMotor.Snapshot` reports `Velocity == 0` between legs (`Arrived`) and during a claim wait
   (`Moving`, zero velocity). Any "is it moving" logic must be designed for that, not assume continuous velocity.
9. **`IActionPresentationHook.RequestActionCue(source, ability)` carries no target**; a real animator implementation
   exists (`CharacterAnimationPresentationHook`, in `CRPG.Gameplay`).
10. **Sorting layer / camera facts.** `IsometricCameraRig` writes the camera in `LateUpdate` at default order 0, so a
    billboard at `[DefaultExecutionOrder(100)]` runs after it. `CRPGCameraSettings.maximumPitch` defaults to **70** and
    `CameraRigState` clamps at construction — pitch 90 needs a new settings asset. `SpriteRenderer.sortingOrder` is
    int16: 128 order-units per world unit gives +-256 units of Z (fine for a ~60x60-cell map; clamp and warn).

## Design decisions

**D1 — Where the rocking driver lives (revised; no public-API change).** `RockingMotionState` (pure logic) lives in
`CRPG.Presentation`. `RockingMotionDriver` (the MonoBehaviour that reads a motor's `Snapshot`) lives in `CRPG.Gameplay`
(`Runtime/Gameplay/Presentation/`), which already references both `CRPG.Navigation` and `CRPG.Presentation` — the same
placement as `FacingController` and `CharacterAnimationPresentationHook`. It finds its source with
`GetComponentInParent<IMovable>()`. *Rejected:* `IMotionSource` in Core with `IMovable : IMotionSource` (source-compatible
— every implementer was checked — but a public type change for no benefit) and a `Presentation -> Navigation` reference.
*Recorded inconsistency:* the existing animator path (`CharacterPlayableAnimator`, `CharacterView`) still reads a
`NavMeshAgent` directly; that is pre-existing and untouched — the sprite path is the first consumer of `MotionSnapshot`
on the presentation side, not a claim that the whole layer already is.

**D2 — Rocking motion (revised for bursty motion).** Pure `RockingMotionState` + thin driver applying its output to a
`RockPivot` transform.
- *Moving* is decided from `Snapshot.Velocity.magnitude` with hysteresis (start 0.10 / stop 0.05 world units/s — the
  defaults are `CharacterPlayableAnimator`'s serialized fields; `CharacterPresentationState.SetMovingWithHysteresis` is the
  reference implementation of the two-threshold band). **Velocity, never Status.**
- **Coast timer:** once moving, the state stays "moving" through up to `coastSeconds` (default 0.20) of *zero* velocity, so
  the exact-zero gaps between one-cell combat legs do not restart the rock every cell; `coastSeconds` must stay below
  `GridCharacterMotor.ClaimWaitSeconds` (0.5) so a genuine claim wait or arrival still settles it. (Hysteresis alone cannot
  bridge an exact-zero dropout.) Tested explicitly with 1-frame, 3-frame and 0.5 s gaps.
- *Phase advances with distance travelled* (`phase += speed * dt / strideLength`), *amplitude eases* toward 1/0 at authored
  rates. Output: tilt `sin(2*pi*phase) * peakDegrees * amplitude` about the card-plane normal, plus an optional hop
  `|sin(pi*phase)| * hopHeight * amplitude` — **`hopHeight` defaults to 0**; whether a hop reads at all on a flat top-down
  card is a Gate A question. Defaults: 6 degrees, stride 1 unit. Feel is a user-review item, not something to perfect on paper.
- `RockPivot` sits at the card's **feet** (local y = -0.5 for a cell-sized card) with the layers offset +0.5, so the tilt
  rotates about the feet like a cardboard cut-out. Scaled `Time.deltaTime` only (`timeScale = 0` freezes it).
- **Collider/raycast:** click targeting raycasts the character's 3D collider on CharacterRoot; the card and its rock/hop
  transforms are pure visuals and never move the collider, so a tilted sprite cannot mis-hit.

**D3 — Billboard.** `CameraFacingBillboard` on the card root: `transform.rotation = camera.rotation` in `LateUpdate`,
`[DefaultExecutionOrder(100)]`. Camera = `Camera.main` with an explicit override field. Card material must be **Cull Off**
(`Sprites-Default`). Sits under VisualRoot's Model as VisualRoot's first child.

**D4 — Layer state, compositor, and the sorting contract (revised).**
- **Pure `SpriteLayerState`** (EditMode-testable, no Unity objects beyond `Sprite` references) owns everything decidable
  without a renderer: slot list and draw order; per-slot **appearance** sprite; per-slot **equipment** entries keyed by
  *inventory slot* with a priority (D5); per-slot tint; **keyed hide sets** (`Hide(slot, key)` / `Unhide`) so two hiders
  release a slot only when the last is gone; **appearance resolution** (`ApplyAppearance(map, catalogs)`); and an
  output `EffectiveLayers` (draw-ordered list of slot + sprite + tint). The MonoBehaviour is a thin renderer over that
  list, so a UI Toolkit or RenderTexture backend (the character creator's preview panel, the paper-doll screen) can drop in
  later without touching the logic — the GUI handoff renders the "assembled character sprite" inside a UI panel, where
  `SpriteRenderer`s cannot.
- **Default slots and draw order** (from the actual art): `layer-body`, `layer-feet`, `layer-clothes`, `layer-hair`,
  `layer-helmet`, `layer-shield`, `layer-weapon`. Serialized, so a consumer can add layers.
- **Sorting contract (decided now, used by tiles in Phase 3).** Two sorting layers: `Ground` (tile floors — fixed low
  order) and `World` (everything y-sorted: characters, props, tall walls). For a `World` object,
  `sortingOrder = characterBase + slotIndex` with `characterBase = -round(anchorZ * 8) * 16`, clamped to int16 with a
  warning. **`anchorZ` is CharacterRoot's world Z, never the card's** — the card moves in world Z when it hops/rocks (card-up
  is +Z at pitch 90), which would jitter its own sort order. `SpriteDepthSorter` finds its anchor as
  `GetComponentInParent<CharacterView>()?.transform.parent ?? transform.root` (an explicit `anchor` field overrides).
  Cards also sit a small fixed world-Y offset above the ground plane for physical-depth cleanliness; sorting layers, not
  depth, decide draw order.
- Sprites import Multiple, **PPU 16, centre pivot, point filtering, no compression**.

**D5 — Equipment on sprites (revised).**
- Additive `EquipmentVisualKind.Sprite` (appended, value 3; existing serialized values unaffected) and, on
  `EquipmentVisualProfile`, `SpriteLayerEntry { DefinitionId layerSlot; Sprite sprite; Color tint; int priority }` **plus a
  distinct `hiddenLayerSlots` list**. The existing `BodyRegionId`/`HiddenBodyRegions` stay 3D-only (their meaning is
  narrower — Finding 5 — and one field must not carry two semantics).
- **Inventory slot -> layer slot mapping is authored content, not code:** each item's profile names the layer(s) it
  paints (a sword -> `layer-weapon`, a shield -> `layer-shield`, a tunic -> `layer-clothes`, a helmet -> `layer-helmet`
  + `hiddenLayerSlots: [layer-hair]`). One item -> several layers is allowed (an armor piece = clothes + belt).
- **Equipment source is keyed by inventory slot** (`SlotId` from the event payload) with the entry's priority; the displayed
  sprite for a layer is the highest-priority equipment entry, else the appearance sprite, else empty. Unequipping removes
  exactly that inventory slot's entries, restoring the next entry or the appearance sprite underneath — defined behaviour
  even if two slots paint one layer (last-writer-wins is rejected).
- `SpriteEquipmentBinder` mirrors `EquipmentVisualBinder`'s public contract (Finding 5) and `WorldSpawnService.BindView`
  wires it the same way. **It deliberately has no pending-removal machinery**: `EquipmentService` publishes `ItemUnequipped`
  (old) *before* `ItemEquipped` (new) synchronously inside one `Equip()`, and `SpriteLayerState` only recomputes the
  effective layers when read (renderers update in `LateUpdate`), so the intermediate "old removed, new not yet added" state
  is never rendered. The 3D binder needs its reclaim/discard dance to protect a known-good mesh from a *failed* replacement;
  a sprite entry cannot half-fail (a missing sprite is just an empty layer, logged).

**D6 — Appearance storage lands in Phase 2 (revised; was deferred to Phase 4).** `WorldSpawnService.BindView` takes only a
`visualPrefab`, so without storage Phase 2 would need one prefab per appearance, and the tavern companions and the
created protagonist surviving save/load (Phases 4-6) would re-invent the mechanism. Precedent already exists:
`CharacterState.CurrentAnimationCueId` is presentation data on `CharacterState`. Minimal additive shape:
`CharacterState.Appearance` (`slot -> option` map, `DefinitionId` pairs) and
`CharacterSaveData.Appearance` (`List<AppearanceSaveEntry { Slot, Option }>` with a field initializer, so an old save
deserializes empty). `NpcDefinition` gains an authored appearance list copied into `CharacterState` at spawn.
`WorldSpawnService` applies it at bind (`SpriteLayerState.ApplyAppearance`). **Task 2.4a must first confirm against
`COMPATIBILITY.md` and the `SaveData.CurrentFormatVersion` policy that an additive field needs no `ISaveMigration`**
(the review inferred this from `JsonUtility` behaviour; not yet checked against the repo's stated policy). Live
mid-game appearance edits (raising an event) are out of scope; the creator preview drives `SpriteLayerState` directly.
`SpriteLayerCatalog` (`DefinitionAsset`, one per layer slot, e.g. `catalog-hair`) lists `{ optionId, displayName,
sprite }`; an unknown option id leaves that layer as-is and is reported, never thrown.

**D7 — Prefab authoring (revised).** `CharacterSetupSpec` gains `MovementBackend { NavMesh (default), Grid }` and
`PresentationKind { Humanoid (default), Sprite }`.
- Grid: no `NavMeshAgent`/`NavMeshCharacterMotor`; adds `GridCharacterMotor`; the collider is a **`BoxCollider`** sized to
  one cell (the default capsule, r = 0.3, projects a 0.6-unit disc into a 1.0-unit cell — wrong top-down); the kinematic
  `Rigidbody` stays for trigger events.
- Sprite: no `CharacterPlayableAnimator`/`EquipmentVisualBinder`; adds `SpriteEquipmentBinder`. `FacingController` and
  `CharacterAgent` stay in both.
- **Update of an existing wrapper whose backend/presentation kind differs from the spec is refused** with a clear error
  ("create a new wrapper"), because the builder's own rule is to never remove components and silently keeping a
  `NavMeshAgent` beside a `GridCharacterMotor` recreates the two-`IMovable` hazard this option exists to prevent.
- "Defaults unchanged" is stated as **the existing builder tests pass unchanged and the default component set is
  identical** (prefab YAML is not byte-stable, so byte-identity is not a testable claim). New tests: grid+sprite yields
  exactly one `IMovable`, no NavMesh components, a box collider, the card as VisualRoot's first child, idempotent update,
  and the refusal cases.

**D8 — Combat cues (revised: three states, correct events).** `SpriteActionCues` plays *attack* (hop + scale punch,
target-agnostic), *hit* (white tint flash), and a **three-state defeat**: **Down** (tip over) on
`CreatureDroppedToZeroHP`; **Die** (terminal, fade) on `CreatureDied`; **Recover** (stand back up) on
`CreatureStabilized` and on the Unconscious condition being removed. Driven by the event bus (payload targets verified
in Task 2.6) and a `SpriteActionPresentationHook : IActionPresentationHook` in `CRPG.Gameplay` mirroring
`CharacterAnimationPresentationHook`. *Open in the task:* whether a heal from 0 HP publishes anything usable — if not, the
recover path is stabilize/condition-removal only and that is documented. A directional lunge needs
`IActionPresentationHook` to carry a target (a signature change) — deferred to Phase 5, recorded. Cues live in Phase 2
(presentation), not Phase 5 (UI), because they are card transforms/tints with no UI dependency.

**D9 — Project, art, and the missing monsters.** Task 2.0 creates the `BarrowsEnd/` Unity project (sibling to
`ConsumerGame/`; manifest references `file:../../crpg-toolkit` with `testables`, URP, Input System — the ConsumerGame
precedent, verified). **Explicit URP step:** a fresh project with URP in the manifest still runs Built-in (pink
materials — documented in `Docs/Verification/Task-5.3-BasicCRPG.md`), so the task must create a URP asset + Universal
Renderer and assign `GraphicsSettings.defaultRenderPipeline` and `QualitySettings.renderPipeline`; sprites use the
Cull-Off `Sprites-Default` material (verified to render under the Universal Renderer, or the 2D Renderer is chosen here,
*before* anything depends on it). Kenney art is CC0 and may be committed. **Slicing** uses
`SpriteDataProviderFactories`/`ISpriteEditorDataProvider` with computed rects (`(col*17, row*17, 16, 16)`, skipping
empty cells) — there is no "grid slice" API; budget an Editor slicer that also auto-derives the category blocks from the
empty-column separators. **Monsters:** non-layered enemies (goblins, skeletons, the necromancer, rats) are single-sprite
characters using the same prefab with only `layer-body` populated; Task 2.0 surveys `Roguelike pack` and `Roguelike cave &
dungeon pack` for suitable sprites. **Gaps are fillable: the user has Aseprite**, so missing art (skeleton, necromancer,
off-hand weapons, a head/face layer, extra hair/outfits) can be authored by editing/recombining Kenney sprites.
**Art-provenance rule (user-set): never edit the originals in the Kenney folder — copy the source sheet into the project
(`BarrowsEnd/Assets/Art/Source/`) first and edit only the copy.** Edited/new art keeps the same 16x16 same-canvas layer
convention (so it drops into the slicer/catalog unchanged); each edited sheet gets a one-line note of what changed. Anything still
unresolved is recorded as a Phase 6 art risk rather than discovered there.

## New and changed types

New in `Runtime/Presentation/Sprite/`: `RockingMotionState`, `CameraFacingBillboard`, `SpriteLayerState`,
`SpriteLayerCompositor`, `SpriteDepthSorter`, `SpriteLayerCatalog`, `SpriteActionCues`, `SpriteEquipmentBinder`.
New in `Runtime/Gameplay/Presentation/`: `RockingMotionDriver`, `SpriteActionPresentationHook`.
Changed: `EquipmentVisualProfile` (+`Sprite` kind, +layer entries, +`hiddenLayerSlots`); `CharacterView` (unbind fix);
`CharacterWrapperBuilder`/`CharacterSetupSpec` (backend/presentation options, refusal); `WorldSpawnService.BindView` (wire the
sprite binder, apply appearance); `CharacterState` (+`Appearance`); `CharacterSaveData`/`SaveService` (+appearance);
`NpcDefinition`/`NpcSpawnService` (+authored appearance). New project: `BarrowsEnd/`. No change to `IMovable`.

## Task breakdown

### Task 2.0a — Project, URP, import
Create `BarrowsEnd/` (manifest, ProjectSettings, URP asset + Universal Renderer with the explicit activation step,
new `CRPGCameraSettings` asset with `MaximumPitch >= 90`, yaw locked). Editor slicer (computed-rect slicing + block auto-
derivation) and import of `roguelikeChar_transparent.png` plus one dungeon/indoor tile sheet at PPU 16/point/centre.
Survey monster sprites (D9). Acceptance: sprites render un-pink under the active pipeline at pitch 90 through the **real
`IsometricCameraRig`** (so the rig is proven at 90, not assumed); slicer output verified against the decoded block
layout in Finding 1.

### Task 2.0b — Look-and-feel spike (USER REVIEW GATE A)
Throwaway scene: hand-stacked characters (a bare body, an outfit, a helmet, a shield/weapon, a monster) on a few tiles, a
hand-rolled rock tilt, at the intended zoom (record the pixel-perfect rule `orthographicSize = screenHeight / (2 * 16 *
integerScale)` for Phase 3). Generates a contact sheet **for naming** (block boundaries are machine-derived). **Gate A
(user, visual):** does a stack read as one character; do 16px characters sit right against the tiles; is the rock
amplitude/cadence right and is a hop wanted at all; is flat-top-down the look wanted (vs tilted camera with upright
cut-outs — a different design, cheapest to learn now); and **which creator rows/categories** the art actually supports
(Finding 1a). Also fixes the sorting contract (D4) on real sprites.

### Task 2.1 — Rocking motion and billboard
`RockingMotionState` + EditMode tests: idle stays zero; moving ramps amplitude; phase advances with distance, not time;
easing to rest; hysteresis; **coast**: a 1-frame, 3-frame and 0.5 s zero-velocity gap (0.5 s must settle, 1-3 frames must
not); a `Moving`-status snapshot with zero velocity never starts a rock; zero delta is a no-op. `RockingMotionDriver` +
`CameraFacingBillboard` (PlayMode): a real `GridCharacterMotor` moving -> pivot tilts, stationary -> 0; a real
`MoveActionExecution` one-cell-leg move does not restart the rock at each cell; billboard matches `Camera.rotation` at 90
and 85 and follows a rotating camera; a parent rotation change does not change the card's world rotation; hop does not
change the sorter's input.

### Task 2.2 — Layer state, compositor, depth sorting
`SpriteLayerState` + EditMode tests (set/clear, equipment-over-appearance precedence and restoration, two equipment slots
painting one layer with priority, keyed hide/unhide with two hiders in either order, tint, `EffectiveLayers` order,
appearance resolution incl. unknown option). `SpriteLayerCompositor` + `SpriteDepthSorter` (PlayMode: renderer per slot in
draw order, hidden slot disables its renderer, sorting order follows CharacterRoot Z — a lower-Z character draws in front,
a hop/rock leaves the order unchanged, clamp + warning at the int16 limit).

### Task 2.3 — Equipment on sprites
`EquipmentVisualKind.Sprite`, layer entries, `hiddenLayerSlots`; `SpriteEquipmentBinder` (same lifecycle contract as
`EquipmentVisualBinder`; defers subscribe while disabled; reconciles from inventory on bind — load-before-bind and
bind-before-load); `WorldSpawnService` wiring. Tests mirror `EquipmentVisualBindingTests`' matrix for sprites: equip,
unequip restores appearance, **same-slot replacement through the real `EquipmentService` event order renders no
intermediate frame** (assert on effective layers between the two events and after), item with no profile, two items hiding
one layer removed in both orders, two inventory slots painting one layer.

### Task 2.4a — Appearance storage and spawn
Confirm the additive-field policy (D6), then `CharacterState.Appearance`, `CharacterSaveData`/`SaveService` round trip
(including an old-format save fixture with the field absent), `NpcDefinition` authored appearance, `WorldSpawnService`
applying it at bind. Tests: save -> load -> identical appearance; old save loads with empty appearance; NPC spawn carries
its authored appearance; restore path (`BindRestoredView`) applies it too.

### Task 2.4b — Catalogs and content (with the user)
`SpriteLayerCatalog` + validator (every option has a sprite, no duplicate ids). Author the campaign's catalogs from the
Gate A category decisions via an Editor script in `BarrowsEnd/` (reflection-set fields, the sample-content-author
convention): skin tones, hair, outfits/belts/shoes, helmets, shields, weapons, pre-made NPCs, monsters. A multi-sitting
content task by nature.

### Task 2.5 — View and prefab integration
`CharacterView` unbind fix (test: swap a Model with an Animator for one without; the animator ends unbound).
`CharacterWrapperBuilder`/`CharacterSetupSpec` (D7) with the tests listed there. Author the campaign's base character
prefab (card + `RockPivot` + compositor + sorter + billboard + cues, box collider, grid motor) through it, plus the monster
prefab variant.

### Task 2.6 — Combat cues
`SpriteActionCues`, `SpriteActionPresentationHook`, event wiring (payload targets verified first). Tests: each cue
starts/finishes and restores the neutral pose/tint; **a goblin at 0 HP goes Down (not Die) and Die is terminal and survives
a later hit; Recover stands it back up; a Die is never undone by a Recover**; a hook call with no cue authored is a no-op; a
hit on an already-Down character; scaled time only. Verify (and document) whether a heal publishes anything.

### Task 2.7 — Phase gate (USER REVIEW GATE B)
An integrated scene in `BarrowsEnd/`: a party of layered characters (different appearances + equipment) walking a grid map
with a door and an obstacle, changing equipment live, taking hits, going down and dying, a saved-and-loaded appearance.
Full EditMode + PlayMode suites green. **User reviews visually:** rocking feel, sorting (behind/in front of each other **and
of props**), pixel crispness, equipment swaps, cue readability. Phase 2 closes only on their sign-off.

## Task 2.0 results (built 2026-09-19; awaiting USER REVIEW GATE A)

**Built:** `BarrowsEnd/` Unity project (6000.6.0f1; manifest = ConsumerGame precedent + `com.unity.2d.sprite`, `testables`), URP asset + Universal
Renderer activated via `Barrows End/Setup/Activate URP` (GraphicsSettings + every quality level), `Assets/Art/Source/` holding **copies** of
`roguelikeChar`, `roguelikeDungeon`, `roguelikeSheet` (originals untouched), an Editor slicer (`BarrowsEndSetup.SliceSheet`: computed 16x16 rects on the 17px
pitch, empty cells skipped, PPU 16 / point / uncompressed / centre pivot / **FullRect** mesh), a throwaway look spike (`Assets/Spike/`, `Assets/Editor/LookSpikeBuilder.cs`,
`Assets/Scenes/Spike/LookSpike.unity`), a `BarrowsEnd_Camera` settings asset (pitch locked to 90), sorting layers `Ground` and `World`, and one PlayMode capture test
(`Assets/Tests/PlayMode/LookSpikeCaptureTests.cs`).

**Verified:**
- Slicing: 448 (Char) / 522 (Dungeon) / 1704 (Sheet) sprites. Column-block auto-derivation on the Char sheet gives exactly the Finding-1 layout: cols 0-1 (22 = 8 bodies + 14 premades),
  3-4 (20), 6-17 (120), 19-26 (**80** - hair *and* beards; Finding 1's "~40" was wrong), 28-31 (36), 33-40 (60), 42-53 (110).
- **Real `IsometricCameraRig` reaches pitch 90** with the new settings asset (asserted in a PlayMode test), renders un-pink under the activated URP asset, sprites y-sort correctly against props with the D4 formula
  (root Z, 128 order-units/unit), and the hand-rolled rock tilt reads. Pixel-perfect ortho size for 1080p at 4x = 8.4375.
- Hazard found: `IsometricCameraRig` treats `Zoom` as camera *distance*, so with an orthographic camera zoom does nothing. **Phase 3 camera authoring must drive `orthographicSize`** (integer scales) instead.
- Test-harness facts: `-runTests` in a project with `testables` also runs the toolkit's own tests - use `-assemblyNames BarrowsEnd.PlayModeTests`; `WaitForEndOfFrame`/`ScreenCapture` hang in batch mode - render the camera to a `RenderTexture` instead;
  a headless **edit-mode** `Camera.Render()` mixing two sprite textures draws every sprite with the first texture (batching artifact, not real) - only trust Play-mode captures.

**Monster survey (D9):** `Roguelike pack` (props, furniture, town buildings, tents, graves, chests) and `Roguelike cave & dungeon pack` (props, doors, bones) contain **no creatures**. The only monster art in the whole kit is the green orc body (Char sheet col 1 row 3 / col 0 row 3).
Skeleton, necromancer, rats/bats etc. must be drawn in Aseprite (skeleton = recolored body; necromancer = body + purple garment + hood/helmet layers is nearly free). Recorded as Phase 6 art work, sized in Task 2.4b.

**Gate A questions for the user** (see the spike scene and the two captures): (1) does a layered stack read as one character; (2) is the rock amplitude/cadence right and is a hop wanted (`hopHeight` is 0); (3) flat top-down cards vs a tilted camera with upright cut-outs;
(4) creator rows: Skin tone / Hair / Outfit / Weapon (no head/face layer exists); (5) which monster art to author.

**Gate A outcome (user, 2026-09-19): PASSED.** Stacked layers read as one character; rocking feel and amplitude approved as-is (6 degrees, no hop, so `hopHeight` stays 0); flat top-down cards approved (no tilted-camera redesign);
clothes and hair approved; no custom faces (creator rows = Skin tone / Hair / Outfit / Weapon). Skeletons first for monster art, built as a custom **body layer** decorated with the stock garment/helmet/shield/weapon layers.

**Custom art (first pass, built after Gate A):** `Assets/Art/Source/barrowsend_bodies_transparent.png` (2 skeleton bodies on the same 16x16 / 17px-pitch layout, so the existing slicer and layer kit take it unchanged), generated by
`Assets/Art/Source/make_skeleton_bodies.py` (edit the ASCII or open the PNG in Aseprite to refine). Verified in the Play-mode capture: bare skeleton reads clearly; robe + helmet + weapon layers stack correctly on it.
Still to author (Task 2.4b, with the user): necromancer (skeleton or human body + dark robe + hood), rats/bats (single-sprite, new art), and any per-monster accents.

## Risks and open questions

1. **Billboard look** — flat top-down cards vs. a tilted camera with upright cut-outs; Gate A exists to catch a wrong call
   cheaply.
2. **Creator category mapping** — the art has no head/face layer and limited hand variants; the GUI handoff's rows change
   (Finding 1a). Needs the user's call at Gate A.
3. **Monster/necromancer art** may not exist in the layer kit (D9); Phase 6 risk recorded early.
4. **Additive-save-field policy** to be confirmed against `COMPATIBILITY.md` (D6, Task 2.4a).
5. **Heal-from-0 has no event** (Finding 7) — recover-on-heal may be impossible without a new event; Task 2.6 decides.
6. **Effort:** ten sittings, not eight — 2.0a/2.0b and 2.4b are each multi-sitting.

## Accepted simplifications

- Rocking is whole-sprite tilt (+ optional hop); no per-part animation, no directional frames, no left/right flip
  (`FlipToMoveDirection` optional polish, off).
- Attack cue is target-agnostic until `IActionPresentationHook` carries a target.
- No `CharacterSetupWindow` UI for the new spec options.
- No live mid-game appearance-change event; the creator preview drives the pure state directly (creator UI is Phase 4).
- Off-hand weapons / two-handers have no art in the kit.

## Review record (Opus, 2026-09-19) — findings and dispositions

- **B1 death cue on the wrong event** — fixed (D8: Down/Die/Recover on the right events; tests for a goblin at 0 HP).
- **B2 hysteresis cannot bridge exact-zero leg gaps** — fixed (D2 coast timer; 1-frame/3-frame/0.5 s tests).
- **B3 hop fights the depth sorter** — fixed (sorter reads CharacterRoot; hop default 0; test).
- **B4 tile-vs-character sorting deferred but Gate B needs it** — fixed (D4 contract decided in Phase 2, shared with Phase 3).
- **S1 asset facts / no head layer / one-sided weapon+shield** — adopted into Finding 1 and Gate A.
- **S2 no Act 2/3 enemy art** — recorded (D9, risk 3); survey added to Task 2.0a.
- **S3 compositor not separable for UI backends** — fixed (pure `SpriteLayerState` owns logic and resolution).
- **S4 appearance storage belongs in Phase 2** — adopted (D6, Task 2.4a); additive-field policy check added.
- **S5 builder update path recreates two-`IMovable`** — fixed (refusal; box collider; restated default-compat claim).
- **S6 cheaper alternative to `IMotionSource`** — adopted (D1: driver in `CRPG.Gameplay`; no public-API change).
- **S7 slot mapping unspecified / two sources too few** — fixed (authored mapping; equipment keyed by inventory slot).
- **S8 region fields silently change meaning** — fixed (distinct `hiddenLayerSlots`).
- **N1 wrong hysteresis attribution** — corrected (D2). **N2 Cull Off** — added (Finding 2c, D3, D9). **N3 pitch-90 settings
  asset / real rig at Gate A** — added (Task 2.0a). **N5 no grid-slice API** — added (D9). **N7 contact sheet is for naming**
  — adopted. **N8 card must be first child** — added (Finding 4, D7). **N9 explicit URP activation** — added (D9).
  **N10 effort** — restated (risk 6). N4/N6 (int16 range, pixel-perfect formula) confirmed correct.

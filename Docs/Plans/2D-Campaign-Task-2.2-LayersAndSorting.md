# Task 2.2 Detailed Plan: Layer state, compositor, depth sorting

> Status: BUILT (2026-09-19); plan v2 approved by the user, results at the end. (Was: DRAFT v2, post-Opus review.) Child of
> `Docs/Plans/2D-Campaign-Phase2-SpritePresentation.md` (Task 2.2, decisions D4/D5, findings 1, 2, 4, 10; Opus B3/B4/S3/S7/S8).
> Task 2.1 is committed (`ccfc6d7`). Gate A settled the look: stacked same-canvas 16x16 layers, flat top-down cards, no hop.
> The "Review record" at the end lists every Opus finding and its disposition.

## Goal

Turn "a character is a stack of sprite layers, sorted against everything else in the world" into tested toolkit code:

1. `SpriteSortingContract` - the shared world-Z -> draw-order formula and sorting-layer names (tiles in Phase 3 reuse it).
2. `SpriteLayerState` - **pure** logic: which sprite shows in each slot given appearance, equipment, and hides. No renderers.
3. `SpriteLayerCompositor` - MonoBehaviour: one `SpriteRenderer` per slot, a thin renderer over `SpriteLayerState`.
4. `SpriteDepthSorter` - MonoBehaviour: y-sorts a character (or prop) by its **anchor's** world Z.

Non-goals: equipment events/binder (2.3), catalogs, appearance resolution and save storage (2.4), prefab authoring (2.5).

## Deviations from the Phase 2 doc (flagged)

- **No public keyed `Hide/Unhide` API.** D4 listed one; its only Phase 2 consumer is equipment (a helmet hiding hair), so a hide lives on the equipment set and goes away with it. A hides-only set (empty paints) is legal and is how a pure hider is expressed.
- **`ApplyAppearance` and `ISpriteOptionSource` move to Task 2.4** (reverses the v1 draft, on Opus's advice): they have no implementer or consumer until the catalogs and appearance storage exist, and the right shape (retaining option ids, replace-vs-merge for the restore path) depends on 2.4's save design. 2.2 keeps only the `SpriteOption` value (sprite + tint) and the direct `SetAppearance` calls.
- **Equipment ties break by ordinal key, not "most recent".** D5 said ties go to the most recent equip. A sequence number makes an identical re-set non-idempotent and makes the winner depend on dictionary enumeration order when 2.3 reconciles from inventory on bind (so load-then-bind and bind-then-load could differ). Instead: highest priority wins; ties go to the smaller ordinal `DefinitionId.Value` of the inventory-slot key. Deterministic, order-independent, no sequence state.
- **Sorter anchor fallback is the sorter's own transform, not `transform.root`.** Order: explicit `anchor` -> parent of the `CharacterView` transform (searched with `includeInactive: true`) -> own transform.
- **Prop lane, slot cap 15 (was 16).** See the contract below: each row reserves order 0 for props, so a character has at most 15 slots. The cap is exported as `SpriteSortingContract.MaxSlotsPerObject`; the state reads it rather than hard-coding a number.

## Design

### 1. `SpriteSortingContract` (`Runtime/Presentation/Sprites/`, static, pure)

- `const string GroundLayer = "Ground"; const string WorldLayer = "World";`
- `const int StepsPerUnit = 8; const int OrdersPerRow = 16; const float MaxWorldZ = 256f; const int MaxSlotsPerObject = 15; const int PropBias = 0;`
- Each 1/8-unit row `k` owns the order band `[-16k, -16k + 15]`. **Order `-16k` (offset 0) is the row's prop lane; a character's slots occupy offsets `1..15`** (`OrderFor(base, slot) = base + 1 + slot`). Row `k+1` (higher Z, drawn behind) tops out at `-16k - 1`, strictly below row `k`'s prop lane, so nothing in a row behind can draw through a prop or character in the row in front, at every slot count up to the cap. (The v1 `PropBias = -8` only held for up to 8 slots and would have buried a helmet/shield/weapon of the character behind a pillar into the pillar.)
- `static int BaseOrder(float worldZ, out bool clamped)`: **clamp the float first** (`z = Clamp(worldZ, -MaxWorldZ, MaxWorldZ)`, `clamped` is set from that comparison; NaN/Infinity -> Z 0 with `clamped = true`), then `-round(z * 8) * 16`. Clamping in float space avoids int overflow for huge finite Zs. Result range `[-32768, 32752]`.
- `static int OrderFor(int baseOrder, int offset)`: `baseOrder + offset` clamped to int16, used by **both** characters (offset `1 + slot`) and props (offset `PropBias`, or any author bias), so no write path can exceed int16.
- Higher Z draws **behind**: at pitch 90 with camera yaw 0 screen-up is world +Z. **Precondition (documented in the class summary and a Phase 3 risk): the formula assumes camera yaw 0.** `CRPGCameraSettings` clamps only pitch, so nothing enforces it today; Phase 3 must either lock yaw (e.g. `minimumYaw/maximumYaw` on the settings) or replace the formula. The sorter warns once if it sees a camera yaw more than 1 degree off 0 (cheap guard, tested).
- Range: +-256 world units of Z (a ~60x60 map is far inside). Rows within 1/8 unit of Z share a base; on a 1-unit grid same-row characters are >= 1 cell apart in X and never overlap, so the tie is invisible.
- **Tall multi-cell props:** a prop taller than one cell is sorted per occupied cell. Either author it as one sprite per cell (what the approved spike does with its two-cell pillar) or give its sorter an explicit `anchor` at its ground-footprint row; the sorter's default own-transform anchor is the sprite's *centre*, not its foot. Tiles on `Ground` are never sorted; tall walls/props on `World` use `BaseOrder(z)` + `PropBias`.

### 2. `SpriteLayerState` (pure C#)

Shared value types: `readonly struct SpriteOption { Sprite Sprite; Color Tint; }`, `readonly struct LayerPaint { DefinitionId Slot; SpriteOption Option; }`, `readonly struct EffectiveLayer { DefinitionId Slot; int SlotIndex; Sprite Sprite; Color Tint; bool Visible; }`.

`SpriteLayerState(IReadOnlyList<DefinitionId> slotOrder)` - ordered draw slots, back to front. The constructor **throws** on null/empty, an invalid `DefinitionId`, a duplicate, or more than `SpriteSortingContract.MaxSlotsPerObject`. Default slot set (owned by the compositor): `layer-body, layer-feet, layer-clothes, layer-hair, layer-helmet, layer-shield, layer-weapon`.

Inputs:
- `SetAppearance(DefinitionId slot, SpriteOption option)` / `ClearAppearance(slot)` / `ClearAllAppearance()`. A null sprite is "empty" and reported; an unknown slot is ignored and reported.
- `SetEquipment(DefinitionId key, int priority, IReadOnlyList<LayerPaint> paints, IReadOnlyList<DefinitionId> hiddenSlots)`: `key` is the **inventory slot** (`EquipmentService` publishes `context.Data["SlotId"]` as a `DefinitionId` on both events - verified). Replaces the key's paints and hides **atomically**; `paints`/`hiddenSlots` may be null or empty (a hides-only or paints-only set is legal). A slot painted twice inside one `paints` list: last wins + reported. A paint with a null sprite is ignored + reported. An unknown slot is ignored + reported.
- `ClearEquipment(DefinitionId key)`: removes the key's set; **a silent no-op for an unknown key** (no report, no exception, no `Version` bump) - 2.3 calls it for every unequip including items with no sprite profile.
- `Action<string> Report { get; set; }`, always invoked as `Report?.Invoke(...)` (a state with no handler never throws).

Resolution, per slot in draw order:
1. **Hidden** if any equipment set lists the slot in `hiddenSlots`; a hidden slot shows nothing (no appearance, no paint), including a paint the hiding set itself made on that slot (a helmet set that hid its own helmet layer would paint nothing - almost certainly an authoring mistake; 2.3's profile validator flags it). Two hiders release the slot only when the last is gone, in either order.
2. Else the paint from the equipment set with the **highest priority** that paints the slot; ties -> smaller ordinal key string; that paint's own tint applies.
3. Else the slot's appearance with its appearance tint.
4. Else empty.

Outputs:
- `int Version` - increments on every mutation that can change the output. An identical re-set (same sprite reference and exactly equal `r/g/b/a` components - not `Color ==`, which is an epsilon compare) is a no-op; so is a `SetEquipment` identical to the stored one.
- `IReadOnlyList<EffectiveLayer> Layers` - **backed by a fixed-size array** (the slot count is immutable), refreshed in place on read after a `Version` change: one entry per slot in draw order, `Visible = !hidden && Sprite != null`. The same instance is returned every time (zero per-frame allocation, asserted). Arrays have no version counter, so a mutation during enumeration yields mixed-generation data rather than an `InvalidOperationException` (documented; nobody enumerates while mutating in single-threaded Unity code).
- `void CopyVisibleTo(List<EffectiveLayer> destination)` for renderer-less backends (creator preview, paper doll).

### 3. `SpriteLayerCompositor` (MonoBehaviour)

- `[SerializeField] List<DefinitionId> slots` (default: the 7 above via a field initializer, which applies on `AddComponent` and prefab creation), `[SerializeField] Transform layersRoot` (defaults to its own transform), `[SerializeField] string sortingLayerName = "World"`.
- **`State` is a lazy property** (`state ??= new SpriteLayerState(ResolvedSlots)`), `Awake` only forces it - the 2.1 convention (`RockingMotionDriver.Motion`). This keeps the editor-time authoring path in Task 2.5 (`CharacterWrapperBuilder` runs in edit mode where `Awake` does not) from hitting a null.
- **Inspector-state handling (no throw out of `Awake`):** empty/null `slots` -> the default set + one warning. Invalid entry / duplicate / over the cap -> one error, the compositor sets a `Disabled` flag and `LateUpdate` returns immediately (an `Awake` exception would otherwise repeat as per-frame errors).
- Renderers are **found by name, else created** (`Layer_<slot>` under `layersRoot`, identity local position/rotation, unit scale), so a prefab that authors them (2.5) is not doubled and stays previewable. Default material: under URP a `SpriteRenderer` gets `Sprite-Unlit-Default` (double-sided, `Cull Off`); in a player build the native `Sprites/Default` fallback is also double-sided. The flat cards need a Cull-Off material, so a test asserts the created renderers' `sharedMaterial` is non-null and uses one of those two shaders.
- `LateUpdate`: if `State.Version != AppliedVersion`, apply per slot `sprite`, `color`, `enabled = Visible`. **Renderers update only here**, so an unequip-then-equip inside one frame is never presented half-done (`EquipmentService.Equip` publishes both events synchronously).
- `int SortingBase { get; set; }`: the setter re-applies `OrderFor(base, 1 + slotIndex)` to every renderer immediately (skipping unchanged writes) and the value is re-applied after any rebuild, so compositor and sorter are **order-independent**.
- `IReadOnlyList<SpriteRenderer> Renderers` (a cached wrapper - no per-call allocation), `int AppliedVersion`.
- **Sorting-layer guard:** `SortingLayer.NameToID` returns 0 for an unknown name and `IsValid(0)` is true (Default has id 0), so neither detects a missing layer, and `renderer.sortingLayerName` reads back "Default". The guard **enumerates `SortingLayer.layers`** and matches by name; a miss warns once and leaves the renderer on Default. The toolkit cannot create project sorting layers; consumers add `Ground` and `World` (BarrowsEnd already has them; noted for COMPATIBILITY.md).
- `State.Report` -> `Debug.LogWarning` with the object path.

### 4. `SpriteDepthSorter` (MonoBehaviour, `[DefaultExecutionOrder(50)]`)

- Fields: `[SerializeField] Transform anchor` (optional), `[SerializeField] int orderOffset` (default 0; props leave it at `PropBias` 0).
- Anchor resolution at first use (re-resolved if the anchor is destroyed): explicit -> `GetComponentInParent<CharacterView>(true)` then its `transform.parent` (CharacterRoot; the view's own transform if it has no parent) -> own transform. **It never reads the card's position** (Opus B3: the card carries the world-Y offset and the rock tilt, both of which move it in world Z at pitch 90).
- `LateUpdate`: `b = BaseOrder(anchor.position.z)`; if a `SpriteLayerCompositor` is on this object or a child, set `SortingBase = b`; else write a lone `SpriteRenderer`'s `sortingOrder = OrderFor(b, orderOffset)` (props). Writes skipped when unchanged; warn once on clamp; a non-finite anchor position skips the frame; a once-only warning if `Camera.main`'s yaw is not within 1 degree of 0.

## Test plan (tests first; mutation checks recorded)

Test assets: a small helper creates `Texture2D` + `Sprite.Create`; every test destroys both (`DestroyImmediate` of the sprite *and* its texture) in `TearDown`. `Ground` and `World` are **added permanently to `TestProject/ProjectSettings/TagManager.asset`** (the toolkit's own test project; a few lines of YAML), so tests can assert the real names round-trip and the missing-layer guard against a genuinely absent name.

### EditMode `SpriteSortingContractTests`
- Anchors: `BaseOrder(0) == 0`, `BaseOrder(1) == -128`, `BaseOrder(-1) == 128`; rounding away from midpoints (`0.06 -> 0`, `0.07 -> -16`; no `0.0625` case, `Math.Round` is banker's).
- **No interleaving at the slot cap:** with `MaxSlotsPerObject` (15) slots, for any two Zs at least 1/8 apart the lower-Z character's minimum order exceeds the higher-Z character's maximum, over a range sweep.
- **Prop lane invariant at the cap (not at 7):** a prop in row `k` is behind every layer of a character in row `k`, in front of **every** layer of a character at the cap in row `k+1`, and in front of a prop in row `k+1`.
- Clamping: `BaseOrder(1e9f)`, `BaseOrder(float.MaxValue)`, `+-257`, `+-Infinity`, `NaN` all land in `[-32768, 32752]` with `clamped == true`; `OrderFor` never exceeds int16 for any base/offset (including a large negative author offset).

### EditMode `SpriteLayerStateTests`
- Constructor guards: null/empty/duplicate/invalid/16 slots throw; 15 accepted; order preserved.
- Appearance set/clear/clear-all; `Layers` in slot order; empty not `Visible`; null sprite reported; unknown slot reported; a state with **no `Report` set never throws**.
- Equipment over appearance; clearing restores the appearance beneath.
- Two keys painting one slot: higher priority wins; equal priority -> smaller ordinal key; removing the winner restores the other, **both removal orders**; **bind/rebind determinism:** setting the same sets in every permutation of order yields the same winner.
- One set painting several slots (armor = clothes + feet) appears and is removed as a unit.
- `SetEquipment` under an existing key replaces atomically (no leftover paints/hides); an identical re-set is a full no-op (no `Version` bump).
- Duplicate slot inside one `paints` list: last wins + reported. Hides-only and paints-only sets work. `ClearEquipment` on an unknown key: silent, no bump.
- Hides: a helmet set hides hair (appearance and any hair paint); unequip restores; **two hiders removed in either order**; hidden slot not `Visible` but its data survives; a set hiding its own painted slot paints nothing.
- Tints: appearance vs paint tint apply to the right winner; a sub-epsilon tint change bumps `Version` (exact compare).
- `Version` semantics; `Layers` is the **same array instance** across reads and after changes; `CopyVisibleTo` matches the visible layers in order.
- Mutation checks (by hand): drop the priority compare -> the priority test fails; unequip also clears the appearance -> the restore test fails; releasing on the first hider removed -> the two-hider test fails; tie-break by set order instead of key -> the permutation test fails.

### PlayMode `SpriteLayerCompositorTests`
- One renderer per slot, named, parented under `layersRoot` (also a non-self `layersRoot`), identity local transform and unit scale, sorting orders ascending in slot order with `1 + slotIndex` offsets; **existing `Layer_<slot>` children are reused, not doubled**.
- Created renderers' material is non-null with a Cull-Off default shader (URP `Sprite-Unlit-Default` or `Sprites/Default`).
- State changes reach renderers at the next `LateUpdate` (sprite, color, enabled); hidden -> disabled; empty -> sprite null.
- **No half-done frame:** a probe with `[DefaultExecutionOrder(1000)]` samples the renderer in **`LateUpdate`** every frame while a `ClearEquipment` + `SetEquipment` pair runs back to back with no `yield return null` between; the probe never sees an empty layer between old and new. (The real `EquipmentService` ordering is exercised again in 2.3.)
- `SortingBase` applies to every renderer immediately, survives a rebuild, and an unchanged value does not rewrite.
- Sorting layer: with the real `World` layer present no warning; a made-up name warns exactly once and `renderer.sortingLayerID == 0`.
- Slot handling: an 8th slot works; empty list -> default set + one warning; `default(DefinitionId)` entry / duplicate / 16 slots -> one error, component disabled, **no per-frame errors** across frames.
- Lazy `State` works before `Awake` (edit-time shape: `AddComponent` on an inactive object then read `State`).

### PlayMode `SpriteDepthSorterTests`
- Two characters: lower Z in front (all its renderers' orders exceed the other's); **moving CharacterRoot flips it** by the next `LateUpdate`.
- **Card offset and rock cannot change the order:** move the card several units in Z and tilt the pivot; orders unchanged. Mutation: read the card's position -> fails.
- Anchor resolution: the `CharacterView` parent (also with the sorter on CharacterRoot, on VisualRoot, and on the card), explicit override, no view -> own transform, inactive parent found, destroyed anchor re-resolves without throwing.
- Props: a lone `SpriteRenderer` gets `OrderFor(BaseOrder(z), offset)`; same-row prop behind the character, row above behind both, **with a full 15-slot character in the row behind the prop**; **tall prop as two one-cell sprites** sorts correctly against a character between and beside them; explicit-anchor tall prop.
- Clamp: extreme Z warns once, order stays in int16 for character and prop; NaN position skips the frame with no exception or spam.
- **Order independence:** a state change and a Z change on the same frame yield the same orders whichever of the two LateUpdates runs first.
- Yaw guard: a camera yaw of 45 warns once; yaw 0 does not.

## Deliverables and acceptance

Files under `Runtime/Presentation/Sprites/`: `SpriteSortingContract.cs`, `SpriteLayerState.cs` (with the value structs), `SpriteLayerCompositor.cs`, `SpriteDepthSorter.cs`; tests `Tests/Editor/SpriteSortingContractTests.cs`, `Tests/Editor/SpriteLayerStateTests.cs`, `Tests/Runtime/SpriteLayerCompositorTests.cs`, `Tests/Runtime/SpriteDepthSorterTests.cs`; `TestProject/ProjectSettings/TagManager.asset` gains `Ground`/`World`. No asmdef changes (verified: `CharacterView` is in the same assembly). Acceptance: new tests green, **full EditMode and PlayMode suites still green** (baselines EditMode 749, PlayMode 309 + 3 skipped), mutation checks recorded. A visual sorting check on real sprites is folded into Gate B (Task 2.7).

## Risks / open questions

1. **Yaw 0 is a hard precondition** of the formula and nothing enforces it (Phase 3 risk: lock yaw in `CRPGCameraSettings` or change the formula).
2. **The retained appearance option ids and replace-vs-merge semantics** are decided in 2.4 together with `ApplyAppearance` (moved there deliberately).
3. **Prefab-authored layer children** (2.5) must be named `Layer_<slot>` to be reused; 2.5 owns that.
4. **The prop lane changes the spike's numbers** (`base + 1 + slot`, props at `base`, instead of `base + slot`, props at `base - 8`); relative order of a character's own layers is unchanged, so no visual change is expected; Gate B confirms.
5. **Per-frame cost**: sorter and compositor write only on change and `Layers` is reused; asserted for the state, measured informally for the components.

## Review record (Opus, 2026-09-19)

- **B1 `PropBias -8` broke at >8 slots** - fixed (prop lane at offset 0, slots `1..15`, cap 15, tested at the cap). **B2 int overflow before the clamp** - fixed (clamp in float space first; huge-Z tests). **B3 prop path bypassed int16** - fixed (`OrderFor` for every write path). **B4 `NameToID`/`IsValid` cannot detect a missing layer** - fixed (enumerate `SortingLayer.layers`; assert `sortingLayerID == 0`).
- **S1 sequence tie-break non-idempotent / order-dependent** - fixed (ordinal key tie-break, no sequence; permutation test). **S2 list mutation during enumeration** - fixed (array-backed `Layers`). **S3 `State` built in `Awake`** - fixed (lazy property, 2.1 convention). **S4 `ApplyAppearance` shape** - resolved by moving it and `ISpriteOptionSource` to 2.4. **S5 yaw-0 assumption** - documented, guarded, Phase 3 risk. **S6 compositor throws on reachable inspector states** - fixed (default fallback + disabled flag). **S7 hard-coded 16** - fixed (contract constant).
- Minor: URP material rationale corrected + material test added; unified `SpriteOption` value shape; duplicate slot in one paints list, unknown-key `ClearEquipment`, hides-only sets, exact colour compare, `Report?.Invoke` + null-handler test, `GetComponentInParent(true)`, find-or-create layer children, banker's-rounding note, tall-prop rule and tests, probe samples in `LateUpdate`, cached `Renderers` wrapper, Sprite/Texture cleanup, permanent `Ground`/`World` in the TestProject TagManager.
- Verified correct by the review and unchanged: sort direction and range arithmetic, double-sided default material under both projects, anchor != card, assembly/namespace assumptions, hierarchy assumption, `SlotId` payload compatibility, the no-half-done-frame claim, compositor/sorter order independence, baselines.

## Results (built 2026-09-19)

**Files:** `Runtime/Presentation/Sprites/{SpriteSortingContract,SpriteLayerState,SpriteLayerCompositor,SpriteDepthSorter}.cs`; tests `Tests/Editor/SpriteSortingContractTests.cs` (16 cases), `Tests/Editor/SpriteLayerStateTests.cs` (21), `Tests/Runtime/SpriteLayerCompositorTests.cs` (13), `Tests/Runtime/SpriteDepthSorterTests.cs` (10); `TestProject/ProjectSettings/TagManager.asset` gained `Ground` and `World`. No asmdef changes.

**Suites:** EditMode 786/786 (749 + 37); PlayMode 332 passed + 3 skipped (309 + 23); 0 failed.

**Mutation checks (all caught):** priority compare removed -> 1 fails; unequip also clears appearance -> 3 fail; a non-hiding set overwriting another set's hide -> 1 fails (the first version of this mutation, "last set's hide wins", slipped through because both test hiders hid the same slot; a test with a non-hiding set present was added, then it is caught); ties by set order instead of key -> 1 fails; sorter anchored on its own transform instead of CharacterRoot -> 3 fail (card offset/rock, explicit anchor, footprint anchor).

**Small deviations while building:** NaN Z is covered only in the pure contract tests (Unity refuses to assign a NaN transform position, so a PlayMode NaN case cannot be constructed); the "inactive parent" anchor case was dropped (a sorter under an inactive parent does not run); the compositor's `State` stays usable after a configuration error (built from the default slots) so callers never null-reference; the sorter warns about camera yaw once, from the first frame a main camera exists.

**Deferred as planned:** real-sprite sorting is judged visually at Gate B; `ApplyAppearance`/`ISpriteOptionSource` come with 2.4; layer children authoring with 2.5.

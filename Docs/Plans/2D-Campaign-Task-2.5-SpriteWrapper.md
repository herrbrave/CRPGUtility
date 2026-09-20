# Task 2.5 Detailed Plan: Sprite character wrapper, builder options, and the campaign base prefab

> Status: BUILT (2026-09-19); plan v2 approved by the user, results at the end. (Was: DRAFT v2, post-Opus review.) Child of `Docs/Plans/2D-Campaign-Phase2-SpritePresentation.md` (Task 2.5, decision D7, findings 4 and 6, Opus S5/N8).
> Tasks 2.1-2.4 are committed (`ccfc6d7`, `bff5550`, `818546c`, `8144260`). Every runtime piece a sprite character needs now exists as a component; this task makes them **one buildable, tested
> prefab**. The "Review record" at the end lists every Opus finding and its disposition.

## Goal

1. `CharacterWrapperBuilder` / `CharacterSetupSpec` can build a **Grid + Sprite** wrapper (and, independently, NavMesh or Grid movement with Humanoid or Sprite presentation), and refuses an update that would leave two movers, two presentation pipelines or two collider kinds on one wrapper.
2. `CharacterView` correctly unbinds the humanoid animator when a Model swap brings a Model without one, and keeps the Model as VisualRoot's **first** child across builds and swaps.
3. The campaign gets its real base character prefab (`BarrowsEnd`), built through the builder.
4. The whole stack is proven on a real prefab in a real grid: move, rock, sort, billboard, appearance, equipment, model swap, click targeting.

Non-goals: combat cues (2.6), `CharacterSetupWindow` UI for the new options (the spec is filled by authoring scripts; the window only gets the two fixes below), the phase gate scene (2.7).

## Deviations from the Phase 2 doc (flagged)

- **No separate monster prefab variant.** Appearance is data and every monster uses the same layer slots, so a monster is the same wrapper with a different `NpcDefinition.appearance`. Cut as unneeded.
- **Binders live on CharacterRoot, not inside the swappable Model.** They re-resolve their compositor when a swap destroys it, which only works if they survive the swap. The builder adds `SpriteEquipmentBinder` and `SpriteAppearanceBinder` to CharacterRoot. (Task 2.6 places `SpriteActionCues` on CharacterRoot too, resolving the card/renderers by lookup, so the builder is not re-opened for it; noted as a 2.6 dependency.)
- **The card is `Model`, VisualRoot's first child, generated with an identity transform; the 0.02 world-Y lift lives on `VisualRoot`.** `CharacterView.SetModel` forces the swapped-in Model's local position/rotation to zero (`CharacterView.cs:78-79`), so a lift or rotation on the card itself would be lost on the first swap. The lift on VisualRoot survives it; the card's rotation is owned by `CameraFacingBillboard` at runtime anyway (its `LateUpdate` runs the same frame the card first exists), so nothing authored on the card's rotation matters at play time. (The prefab therefore looks edge-on in the editor until Play - accepted.)
- **The collider is keyed to "top-down", not to the movement backend:** a `BoxCollider` whenever the wrapper is Grid **or** Sprite (NavMesh + Sprite included), a capsule only for NavMesh + Humanoid. The box is **0.7 of a cell** wide (`GridColliderFill`), so a Y-rotation by `FacingController` (a 0.7 box's diagonal is 0.99) can never reach the neighbouring cell.
- **The builder owns CharacterRoot's `BoxCollider` on Grid/Sprite wrappers** (as it already owns the capsule on humanoid ones): it patches an existing one instead of adding a second. A consumer's extra trigger box belongs on a child, and the existing preservation test's marker component is not a `BoxCollider`.

## Verified facts this design rests on

- `CharacterWrapperBuilder` add-or-patches tool-owned components on CharacterRoot, finds-or-creates VisualRoot, **always replaces the `Model` child** but does `SetParent(visualRoot, false)`, which **appends last** (`:144-157`), so with other VisualRoot children the Model is not `GetChild(0)`; `CharacterView.TryBindInitialModel` binds `transform.GetChild(0)` (`CharacterView.cs:60-61`) and `SetModel` also appends via `Instantiate(prefab, transform)`. Nothing runs `Awake`/`LateUpdate` during an edit-mode build (no `[ExecuteAlways]` in Runtime), so the builder must author the `Layer_<slot>` children itself.
- The only `[RequireComponent]` in Runtime is `NavMeshCharacterMotor -> NavMeshAgent`; `GridCharacterMotor`, `CharacterAgent`, `FacingController`, `CharacterPlayableAnimator` declare none; `CharacterView` is `[DisallowMultipleComponent]`. So "no NavMeshAgent on a grid wrapper" holds as long as `NavMeshCharacterMotor` is not added.
- `FacingController` null-checks the `NavMeshAgent` and resolves any `IMovable`; `CharacterView` is fully null-safe for a Model with no `Animator` (`playableAnimator`, `visualEquipment`, `motionSource` resolve null) and `BindModel` calls `playableAnimator?.BindPresentation` only when an Animator exists, never `UnbindPresentation()` otherwise (`CharacterView.cs:38-90`) - the gap. The bound flag on the animator is **`CharacterPlayableAnimator.IsPresentationBound`** (`IsBound` is the identity flag on the sprite binders).
- `WorldSpawnService.BindView` hands every grid motor the pathfinder and occupant id and initializes the 3D binder, the sprite equipment binder and the sprite appearance binder found anywhere under the instance (`:249-273`).
- The approved spike geometry (`BarrowsEnd/Assets/Editor/LookSpikeBuilder.cs:79-85`): card `(0, 0.02, 0)`, `RockPivot (0,-0.5,0)`, `Layers (0,0.5,0)`. Inserting an identity-transform `VisualRoot` between root and card changes nothing; the lift on `VisualRoot` is still a true world-Y lift (CharacterRoot is unrotated about X/Z). `SpriteDepthSorter` on the card resolves anchor = `CharacterView.transform.parent` = CharacterRoot and finds the compositor in its children; `RockingMotionDriver` finds the motor with `GetComponentInParent<IMovable>(true)`.
- `PlayerCommandRouter.ResolveClick` takes the **closest** hit and calls `GetComponentInParent<CharacterAgent>` (`:119-127`), as does `AbilityTargetingGate` (`:72`), so a collider on CharacterRoot or a child both work; at pitch 90 the box top is hit before the floor. Cost to note for Gate B: a box makes the character's central tile unclickable as ground.
- `m_AutoSyncTransforms: 0` in both projects (`DynamicsManager.asset:23`): a freshly placed collider is not queryable until `Physics.SyncTransforms()` (precedent: `PointerCommandTests.cs:113-122`).
- The PlayMode assembly cannot reference Editor code (`includePlatforms: []`), so prefab-based PlayMode tests use `Resources.Load` of checked-in fixtures authored by an Editor script (`SceneTravelTestFixtureAuthor.AuthorGridFixtures` pattern).
- `CharacterSetupWindow` calls `BuildOrUpdate` with no try/catch (`:141`) and previews to one shared path `Assets/CharacterLab/_PreviewWrapper.prefab` (`:173-174`): a refusal would escape `OnGUI`, and one preview kind would block every later preview of the other kind.
- `Ground` and `World` sorting layers exist in both `TagManager.asset`s; `BarrowsEnd.Content.Editor.asmdef` references no `CRPG.Editor`; `BarrowsEnd.EditModeTests.asmdef` references no `CRPG.Navigation`/`CRPG.Gameplay`.

## Design

### 1. `CharacterView` fixes

- **Unbind:** in `BindModel`, when the new Model has **no** `Animator`, call `playableAnimator?.UnbindPresentation()` (Animator is null as today); with one, unchanged.
- **First child:** `SetModel` puts the new instance at sibling index 0 (`SetSiblingIndex(0)`), so `GetChild(0)` is the new Model even before the old one's deferred `Destroy` completes and whatever other children VisualRoot has.

### 2. `CharacterSetupSpec` additions

`enum MovementBackend { NavMesh, Grid }` (default `NavMesh`), `enum PresentationKind { Humanoid, Sprite }` (default `Humanoid`), plus `GridCellSize = 1f`, `GridColliderFill = 0.7f`, `GridColliderHeight = 1f`, `GridSpeed = 4f`; `SpriteSortingLayer = SpriteSortingContract.WorldLayer`, `SpriteCardHeight = 0.02f`, `SpriteLayerSlots` (a `List<DefinitionId>`, default `SpriteLayerCompositor.DefaultSlots()`, so a game's custom layer set survives the always-rebuilt Model). Existing fields untouched. A `Sprite` spec with a non-null `ModelPrefab` ignores it (a validator Warning says so).

### 3. Builder behaviour

Always: kinematic/no-gravity `Rigidbody`, `FacingController`, `CharacterAgent`. Movement: NavMesh -> `NavMeshAgent` + `NavMeshCharacterMotor`; Grid -> `GridCharacterMotor` (`Speed = spec.GridSpeed`), no agent/motor. Collider: box (size `(cell*fill, height, cell*fill)`, centre `y = height/2`) when Grid **or** Sprite, capsule otherwise. Presentation: Humanoid -> `CharacterPlayableAnimator` + `EquipmentVisualBinder` + the Model from `ModelPrefab`; Sprite -> `SpriteEquipmentBinder` + `SpriteAppearanceBinder` on CharacterRoot and a generated card as `Model`.

- **Generated card** (`SpriteCardBuilder.Build(spec)`, public, also used by tests to make a swap prefab): `Model` (identity transform; `CameraFacingBillboard`, `SpriteDepthSorter`, `RockingMotionDriver` with `rockPivot` set) -> `RockPivot` (local `(0,-0.5,0)`) -> `Layers` (local `(0,0.5,0)`, `SpriteLayerCompositor` with the spec's sorting layer and slot list) -> one pre-authored `Layer_<slot>` child per slot, each a `SpriteRenderer` on the sorting layer with the default sprite material (the compositor documents that URP's default is the double-sided `Sprite-Unlit-Default`; the builder assigns nothing explicitly and a test asserts the shader is one of the two known Cull-Off defaults). The pre-authoring gives deterministic prefab structure and no runtime GameObject allocation (the renderers have no sprites yet, so it is not a preview aid). `VisualRoot` gets `localPosition = (0, spec.SpriteCardHeight, 0)` and `CharacterView` as for humanoids.
- **Model placement:** the builder puts `Model` at **sibling index 0** (for humanoid and sprite alike) so consumer children under VisualRoot never displace it. Model replacement stays unconditional (hand-edit a *copy* of the prefab instead of the generated card).
- **Update refusal** is evaluated on the *existing asset* (`AssetDatabase.LoadAssetAtPath`, already loaded in `BuildOrUpdate`) **before** `LoadPrefabContents` is called, using `GetComponentInChildren<T>(true)`: no preview scene is opened, nothing can leak, and "the asset is unchanged" is true by construction. It throws `InvalidOperationException` ("create a new wrapper instead") for: Grid spec on a wrapper with `NavMeshAgent`/`NavMeshCharacterMotor`; NavMesh spec with `GridCharacterMotor`; Sprite spec with `CharacterPlayableAnimator`/`EquipmentVisualBinder`; Humanoid spec with `SpriteEquipmentBinder`/`SpriteAppearanceBinder`; and a box-collider spec on a wrapper with a `CapsuleCollider` (or the reverse).
- "Defaults unchanged" = the existing builder tests pass unchanged and the NavMesh + Humanoid component set is identical (prefab YAML is not byte-stable).
- **`CharacterSetupWindow`** gets two fixes only: both `BuildOrUpdate` call sites catch `InvalidOperationException` and show an `EditorUtility.DisplayDialog`; the preview path becomes kind-specific (`_PreviewWrapper_<backend>_<presentation>.prefab`) so a preview of one kind never blocks another. It does not gain fields for the new options.

### 4. Validation (`CharacterRigValidator`)

For `PresentationKind.Sprite` the model/animation-profile/rig-family/socket checks are skipped. **Error:** `GridCellSize <= 0`, `GridColliderFill` not in `(0,1]`, `GridColliderHeight <= 0`, `GridSpeed <= 0` (Grid or Sprite as applicable), an empty/duplicate/over-cap `SpriteLayerSlots`. **Warning:** the sorting layer is not a project sorting layer (enumerated by name; the package ships none, so a fresh consumer project must not fail hard - the compositor itself falls back with a warning), `SpriteCardHeight < 0`, a non-null `ModelPrefab` on a Sprite spec. Capsule/agent checks apply to the NavMesh backend, box checks to the top-down collider. Existing humanoid results are unchanged.

### 5. Campaign prefab (`BarrowsEnd`)

`BarrowsEnd/Assets/Editor/Content/BarrowsEndCharacterAuthor.cs` (`Barrows End/Content/Author Base Sprite Character`) builds `Assets/Content/Characters/SpriteCharacter.prefab` through the builder with Grid + Sprite. **Asmdef changes (deliverables):** `BarrowsEnd.Content.Editor` gains `CRPG.Editor`, `CRPG.Gameplay`, `CRPG.Navigation`; `BarrowsEnd.EditModeTests` gains `CRPG.Gameplay`, `CRPG.Navigation`. One prefab serves party, NPCs and monsters.

## Test plan (tests first; mutation checks recorded)

### EditMode (toolkit, `Tests/Editor/SpriteWrapperBuilderTests.cs`; scratch folder like `CharacterWrapperBuilderTests`)
- **Grid + Sprite:** exactly one `IMovable` (`GridCharacterMotor`); none of `NavMeshAgent`, `NavMeshCharacterMotor`, `CapsuleCollider`, `CharacterPlayableAnimator`, `EquipmentVisualBinder` anywhere in the hierarchy; a `BoxCollider` with the spec's size/centre; kinematic `Rigidbody`; `FacingController`, `CharacterAgent`, both sprite binders **on CharacterRoot**; `Speed` from the spec.
- **Hierarchy/geometry:** `VisualRoot` (local `(0,0.02,0)`) with `CharacterView`; `Model` at **sibling index 0** with identity local transform; `CameraFacingBillboard`/`SpriteDepthSorter`/`RockingMotionDriver` (`rockPivot` -> `RockPivot`); `RockPivot (0,-0.5,0)`, `Layers (0,0.5,0)`; compositor with the sorting layer; `Layer_<slot>` children equal the slot list, each a `SpriteRenderer` on the sorting layer with a Cull-Off default shader.
- **First child:** pre-add a consumer child to VisualRoot, update, and assert `GetChild(0).name == "Model"` (the existing preservation test only adds children under CharacterRoot, so it could not catch this).
- **Matrix:** NavMesh + Sprite (capsule -> **box** because top-down, agent + motor, sprite set) and Grid + Humanoid (`GridCharacterMotor`, box, animator + 3D equipment binder + Model) produce their expected sets; NavMesh + Humanoid defaults match the existing tests, which run unchanged.
- **Idempotent update:** GUID kept, no duplicate components/children, consumer components (a **`SphereCollider`** marker, not a `BoxCollider`) and external references survive, a changed `GridSpeed`/`SpriteSortingLayer`/`SpriteLayerSlots` is applied; a pre-existing root `BoxCollider` on a Grid wrapper is **patched, not duplicated** (documented ownership).
- **Refusals:** each conflict case (movers, presentation, collider kind) throws `InvalidOperationException` with the "create a new wrapper" message; the asset file is byte-for-byte unchanged; detection also fires for a conflicting component on a *child*.
- `SpriteCardBuilder.Build` returns a self-contained `Model` hierarchy with an identity transform; a `Sprite` spec with a `ModelPrefab` builds the same wrapper (ignored).
- **Validator:** sprite kind skips the humanoid checks (a sprite spec with no model/profile has no errors); each new Error/Warning; humanoid results unchanged.
- **Drift guard:** the checked-in PlayMode fixtures have the same component/hierarchy signature as a fresh build.
- **`CharacterView` unbind (PlayMode):** bind a Model with an Animator, `SetModel` to one without -> `IsPresentationBound == false` and the animator stays unbound after a frame; swap back -> rebinds; `SetModel` puts the new Model at sibling index 0 even with another VisualRoot child.

### PlayMode (fixtures in `TestProject/Assets/Resources/Fixtures/`: `SpriteCharacter.prefab`, `SpriteCard.prefab`; authored by an Editor menu script, checked in)
- **`SpriteCharacterPrefabTests`** (real prefab, real `WalkabilityGrid` + `GridPathfindingService`): instantiate, hand the motor its pathfinder, `SetDestination` across cells -> it arrives; **`RockingMotionDriver.Motion.IsMoving` while moving and level after arrival**; two instances sort by CharacterRoot Z and a prop lane sits between rows; the card matches a pitch-90 camera; after `Physics.SyncTransforms()` a top-down `Physics.Raycast` hits the character's `BoxCollider` and `GetComponentInParent<CharacterAgent>` on the hit finds it; the collider's **local size/center never change** while rocking/turning, its **world bounds stay within the character's own cell** on every sampled frame of a move including turns (the 0.7 fill), and `bounds` are compared unrotated only on an axis-aligned leg; two characters cannot share a cell.
- **`SpriteCharacterSpawnTests`:** through `WorldSpawnService.BindRestoredView` (`startInactive: true`) with catalogs, an equipped item and a saved appearance on the real prefab: appearance and gear show once activated; a later real `Equip` updates it; **`SetModel(SpriteCard)` swaps the card, the character keeps its appearance and gear (the test waits >= 2 frames: `Destroy` is deferred, so for one frame two compositors exist and the binders re-resolve on the next), the new card is at VisualRoot's sibling index 0, and its world Y equals the lift (the lift is on VisualRoot)**; `SpawnNpc` works with the same prefab.
- **BarrowsEnd EditMode test:** `SpriteCharacter.prefab` exists with the expected components/hierarchy, one `IMovable`, layer children equal the compositor's default slots.
- **Mutation checks (by hand):** remove one refusal case -> its test fails; drop `UnbindPresentation` -> the unbind test fails; drop `SetSiblingIndex(0)` (builder or `SetModel`) -> the first-child tests fail; move a binder into the Model -> the swap-survival test fails; capsule for Grid -> the box test fails; box fill 1.0 -> the within-own-cell test fails; lift on the card instead of VisualRoot -> the world-Y-after-swap test fails.

## Deliverables and acceptance

Toolkit: `CharacterView.cs` (unbind, sibling index); `CharacterSetupSpec.cs`; `CharacterWrapperBuilder.cs` (matrix, refusal, sibling index); new `Editor/Character/SpriteCardBuilder.cs`; `CharacterRigValidator.cs`; `CharacterSetupWindow.cs` (dialog + kind-specific preview path); tests. TestProject: the fixture author script and the two fixture prefabs (a pattern that already makes the package's own PlayMode suite fail in a consumer project, e.g. `GateAFixtureTests`; consistent, noted). BarrowsEnd: `BarrowsEndCharacterAuthor.cs`, `SpriteCharacter.prefab`, the asmdef reference additions, its EditMode test. Acceptance: new tests green; **full toolkit EditMode and PlayMode suites green** (baselines EditMode 853, PlayMode 363 + 3 skipped); BarrowsEnd EditMode green (baseline 8); mutation checks recorded. Visual confirmation on real tiles is Task 2.7 / Gate B (including click-targeting feel: a box makes the character's central tile unclickable as ground).

## Risks / open questions

1. **The generated card is rebuilt on every update**; hand edits are lost (escape hatch: edit a copy).
2. **The card looks edge-on in the editor** until Play, because its rotation is owned by the billboard and cannot be authored on a swappable Model.
3. **0.7 box fill** trades click-target size for guaranteeing a turning character never spills into a neighbouring cell; Gate B judges it.
4. **NavMesh + Sprite** is supported (box collider, sprite set) but proven structurally only; the campaign uses Grid.
5. **`SpriteActionCues` (2.6)** will be added on CharacterRoot by extending the builder then; the placement is decided here so 2.6 does not re-open this design.

## Review record (Opus, 2026-09-19)

- **B1 builder cannot keep Model first** - `SetSiblingIndex(0)` in the builder and in `SetModel`, tests with a pre-existing VisualRoot child. **B2 `SetModel` zeroes the card's transform** - identity card, the lift on `VisualRoot`, world-Y-after-swap test. **B3 asmdefs wrong** - reference additions listed as deliverables. **B4 `BoxCollider` hijack** - ownership rule documented, `SphereCollider` marker in the preservation test, patch-not-duplicate test.
- **S1 `IsBound` vs `IsPresentationBound`** - corrected. **S2 missing `Physics.SyncTransforms()`** - added. **S3 false collider-bounds claim** - local size/center asserted, world bounds asserted within the cell, box fill 0.7. **S4 window not merely unextended** - dialog on refusal + kind-specific preview path. **S5 refusal detection** - read from the loaded asset before `LoadPrefabContents`, `GetComponentInChildren(true)`. **S6 collider keyed to the backend** - keyed to top-down (Grid or Sprite). **S7 swap timing** - >= 2 frames, sibling-index fix. **S8 material unspecified** - stated and asserted.
- Minor: matrix wording, `SpriteSortingContract.WorldLayer`, missing sorting layer a Warning, pre-authoring rationale corrected, redundant `layersRoot` dropped, `SpriteLayerSlots` on the spec, Sprite spec with `ModelPrefab` warned, click-targeting cost and fixture-pattern noted, 2.6 cue placement decided.
- Verified correct by the review and unchanged: `FacingController`/`CharacterView` null-safety, the sorter and rocking-driver lookups on the card, compositor find-or-create, the `RequireComponent` audit and no `ExecuteAlways`, sorting layers in both projects, `WorldSpawnService.BindView` wiring, existing builder tests staying valid, no consumer of `CharacterSetupSpec` outside the window/validator/tests, spike geometry, the deviations (monster variant cut; binders on the root).

## Results (built 2026-09-19)

**Toolkit files:** `CharacterView.cs` (unbind fix + `SetSiblingIndex(0)`); `CharacterSetupSpec.cs` (`MovementBackend`, `PresentationKind`, grid/sprite fields, `UsesTopDownCollider`); `CharacterWrapperBuilder.cs` (component matrix, generated card as Model at sibling index 0, refusal from the existing asset before `LoadPrefabContents`); new `Editor/Character/SpriteCardBuilder.cs`; `CharacterRigValidator.cs` (sprite/top-down checks); `CharacterSetupWindow.cs` (dialog on refusal, kind-specific preview path). Tests: `SpriteWrapperBuilderTests` (26), `SpriteFixtureDriftTests` (2), `SpriteCharacterPrefabTests` (9, PlayMode).
**TestProject:** `Assets/Editor/SpriteCharacterFixtureAuthor.cs` and the checked-in `Assets/Resources/Fixtures/{SpriteCharacter,SpriteCard}.prefab`.
**BarrowsEnd:** `Assets/Editor/Content/BarrowsEndCharacterAuthor.cs`, `Assets/Content/Characters/SpriteCharacter.prefab`, asmdef reference additions, `SpriteCharacterPrefabContentTests` (3).

**Suites:** toolkit EditMode 878/878 (853 + 25 new); toolkit PlayMode 372 passed + 3 skipped (363 + 9); BarrowsEnd EditMode 11/11 (8 + 3). The existing `CharacterWrapperBuilderTests`, `CharacterRigValidatorTests` and `CharacterWrapperTests` pass unchanged.

**Mutation checks (all caught):** one refusal case removed -> its test fails; `UnbindPresentation` dropped -> the unbind test fails; sibling-index removed from the builder -> the first-child EditMode test fails, from `SetModel` -> the PlayMode first-child test fails; capsule for Grid -> the Grid + Humanoid test fails; collider fill 1.0 -> the size tests fail; world-Y lift on the card instead of VisualRoot -> the geometry test fails.

**Findings while building:**
- The sorting contract limits |world Z| to 256 and the sorter clamps beyond that, so the first version of the PlayMode arena (at Z = 6000) made both characters clamp to the same order and the sort test failed. The arena now sits at small Z. Real maps must keep positions inside +-256 units of Z (a 60x60-cell map is well inside; noted for Phase 3 map authoring).
- The fixture drift guard initially compared root names (the asset name legitimately differs from a scratch build); it now compares structure from a normalized root.
- Not built (as planned): `CharacterSetupWindow` fields for the new options, a monster prefab variant, and `SpriteActionCues` (2.6 adds it on CharacterRoot).
- Not verified here (Gate B): the look on real tiles, click-targeting feel with the 0.7 box (the character's central tile is unclickable as ground), and NavMesh + Sprite beyond its structural test.

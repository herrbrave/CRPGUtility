# Task 2.3 Detailed Plan: Equipment on sprite characters

> Status: BUILT (2026-09-19) as the ADDITIVE design the user chose (see the amendment below); results at the end. Child of `Docs/Plans/2D-Campaign-Phase2-SpritePresentation.md` (Task 2.3, decision D5,
> findings 5-6; Opus S7/S8). Tasks 2.1 (`ccfc6d7`) and 2.2 (`bff5550`) are committed: `SpriteLayerState.SetEquipment/ClearEquipment` (keyed by inventory slot,
> priority + ordinal-key tie-break, per-set hides) and `SpriteLayerCompositor` (renders only in LateUpdate) exist. This task connects them to the real
> equip/unequip flow. The "Review record" at the end lists every Opus finding and its disposition.

## AMENDMENT (user decision, 2026-09-19): additive, with a custom editor - supersedes the separate-asset design below

The user chose the additive shape (D5 as originally approved) **with the custom editor**. Wherever the sections below say `SpriteEquipmentProfile`, read it as the sprite fields on `EquipmentVisualProfile`. Concrete deltas:

- `EquipmentVisualKind.Sprite` appended (value 3; `None`/`Rigid`/`Skinned` keep 0/1/2, asserted by a test). `EquipmentVisualProfile` gains `spriteLayers` (`List<SpriteLayerEntry>`), `spritePriority` (int, tooltip carries the priority convention), `hiddenLayerSlots`, with read-only accessors `SpriteLayers`, `SpritePriority`, `HiddenLayerSlots`. `SpriteLayerEntry` (class with initializers, public constructor) lives beside the profile.
- **3D binder:** `EquipmentVisualBinder.EquipSlot` treats a `Sprite` profile like "no 3D visual" (removes the slot's 3D visual - a sprite item is not a 3D mesh) and **warns once per profile id** (set cleared in `Unbind`), so a mis-authored 3D character gets one accurate message ("'x' is a Sprite profile; sprite characters use SpriteEquipmentBinder") instead of a misleading "no VisualPrefab" on every equip. The wrapper builder (2.5) must not add the 3D binder to sprite wrappers, or that message would fire for every sprite character; recorded as a 2.5 dependency.
- **Sprite binder:** resolves `EquipmentVisualProfile`; kind `Sprite` -> `SetEquipment`; missing/`None` -> `ClearEquipment`; `Rigid`/`Skinned` -> `ClearEquipment` + one warning per profile id.
- **Custom editor** (`Editor/Equipment/EquipmentVisualProfileEditor`): a `[CustomEditor(typeof(EquipmentVisualProfile))]` that shows only the fields relevant to the chosen `kind` (the field-relevance rule is a pure static function, unit-tested); the `DefinitionAsset` base fields always show.
- **Validator** (`SpriteEquipmentProfileValidator`) additionally warns on wrongly filled kinds: sprite layers/hides authored on a non-`Sprite` profile (the "kind left at None" trap), and prefab/socket/bone-map/body-region fields authored on a `Sprite` profile.
- The rest (binder contract, target re-resolution, reconcile-on-subscribe, `None`-character guard, `WorldSpawnService` line, window wiring, test plan) stands as written.

## Decision for your review: a separate `SpriteEquipmentProfile` asset, not a new kind on `EquipmentVisualProfile`

D5 (approved with the Phase 2 doc) said "add `EquipmentVisualKind.Sprite` and layer entries to `EquipmentVisualProfile`". The Opus review found a cleaner shape, verified in
source: `RulesDatabase.TryResolve<T>` is **type-filtered** (`RulesDatabase.cs:35-47`, `found is TDefinition typed`) and definition ids are globally unique with no type
qualifier. So a separate `SpriteEquipmentProfile : DefinitionAsset` can be pointed at by the **existing** `ItemDefinition.EquipmentVisualProfileId` with no change to
`ItemDefinition` and no second id, and:

- the 3D `EquipmentVisualBinder` resolves `TryResolve<EquipmentVisualProfile>` -> false for a sprite profile -> its existing "no profile" path runs (`profile == null` ->
  `RemoveVisual`). **No enum change, no edit to the 3D binder, no regression branch or test, and no per-equip warning on every sprite character** (the wrapper builder adds
  a 3D binder to every wrapper it builds; with the additive design each sprite item would log a misleading "no VisualPrefab" warning on every equip/reconcile/model swap);
- a sprite profile shows only sprite fields (no eight irrelevant 3D fields, no `kind`-left-at-`None` trap, no two near-identically named hide lists on one asset);
- the sprite binder ignores 3D profiles just as cleanly.

Cost: one more small ScriptableObject class, and it reverses a detail of D5. I recommend it; **this is the one design call I'd like you to confirm.** If you prefer additive,
the plan needs three things back: the enum + 3D-binder branch (warn once per profile), a `[CustomEditor]` that shows only the fields for the chosen kind, and validator rules for a
wrongly filled kind.

## Goal

When `EquipmentService` equips or unequips an item on a sprite character, the character's layers change: a sword paints `layer-weapon`, a tunic paints `layer-clothes`, a helmet
paints `layer-helmet` and hides `layer-hair`; unequipping restores what lay beneath. Spawning or loading a character with gear already on shows it immediately. Nothing about the
3D pipeline changes.

Non-goals: appearance storage/catalogs (2.4), prefab authoring and the wrapper builder (2.5), combat cues (2.6), the creator UI (Phase 4).

## Deviations from the Phase 2 doc (flagged)

- **Separate `SpriteEquipmentProfile` asset** instead of `EquipmentVisualKind.Sprite` on `EquipmentVisualProfile` (above; needs your confirmation).
- **Priority is per profile, not per layer entry** (D5 said per entry). 2.2's `SetEquipment(key, priority, paints, hides)` is per set, and an item is one set. Convention
  documented in the field tooltip: body 0, clothes 10, armor 20, helmet 30; equal priorities tie-break on the smaller ordinal inventory-slot id, which is deterministic but
  arbitrary from an author's view - the convention exists so authors do not lean on it.
- **The validator is wired into `PackageValidatorWindow`** (the review noted the window's loop would otherwise never run it over already-authored content).

## Verified facts this design rests on

- `EquipmentService.Equip` publishes `ItemUnequipped` for a displaced occupant (`EquipmentService.cs:46`) then `ItemEquipped` (`:52`), synchronously inside one call; both carry
  `Target == Source == character`, `Data["ItemDefinitionId"]` and `Data["SlotId"]` as `DefinitionId` (`:81-87`); the occupant is back in `SharedInventory` first (`:44`). `Unequip`
  also carries both keys (`:61-66`) and publishes nothing for an item that was not equipped (`:63`). A same-instance re-equip publishes no `ItemUnequipped` (`:42`). `SlotId` can be
  `DefinitionId.None` for an equippable item with no slot authored (`:38` compares equality, not validity).
- `EquipmentVisualBinder` (3D) contract to mirror: identical-tuple `Initialize` no-op, different tuple while bound throws, deferred subscribe in `OnEnable` (deliberately not
  unsubscribed in `OnDisable`), idempotent `Unbind` + `OnDestroy`, full reconcile from `ItemsEquippedBy` + `GetLocation().Slot` (`:80-117, 150-162, 248-257`). Its `isSubscribed`
  flag is mandatory: `GameEventBus.Subscribe` does not dedupe.
- `PartyInventoryState.ItemsEquippedBy(character)` filters only on `Location.Character == character`; shared items have `Character == RuntimeEntityId.None`
  (`PartyInventoryState.cs:44-45`, `ItemLocation.cs:17,23`), so it is safe for a valid character and wrong for `None`.
- `BindView` is the only place a binder is initialized (`WorldSpawnService.cs:265-266`); `SpawnNpc`, `BindRestoredView` (incl. `SessionLoadCoordinator`), `LocationInitializer` and the
  bootstraps all funnel through it. `startInactive: true` (`:246`, the real load path) deactivates the instance **before** `Initialize`, so `isActiveAndEnabled` is false at `Initialize`.
  Scene travel never rebuilds party members (`SceneTravelCoordinator.cs:17-23`), so a surviving binder keeps its subscription and state.
- `CharacterView.SetModel` destroys and re-instantiates the Model (`CharacterView.cs:74`), and the sprite card is VisualRoot's first child, i.e. the Model - so a sprite
  character's compositor (and its `SpriteLayerState`) can be destroyed and replaced under a long-lived binder.
- `SpriteLayerState` is a plain C# object (it does not go Unity-null when its compositor is destroyed); `SpriteLayerCompositor.State` is lazy and works before `Awake`.
- Editor validators live in `Editor/Validation/` (`ValidationResult`, `CharacterRigValidator`); `PackageValidatorWindow.RunChecks` (`:53-95`) loops `AssetDatabase.FindAssets` per asset type.

## Design

### 1. `SpriteEquipmentProfile` (`CRPG.Presentation.Equipment`, `DefinitionAsset`)

`[CreateAssetMenu("CRPG Toolkit/Presentation/Sprite Equipment Profile")]`, referenced from `ItemDefinition.EquipmentVisualProfileId` (the id is a plain `DefinitionId`; the field's name and tooltip
keep saying "visual profile", which is accurate for both asset types - a tooltip sentence is added noting it may point at either).
- `[Serializable] class SpriteLayerEntry { DefinitionId layerSlot; Sprite sprite; Color tint = Color.white; }` with a **public constructor** `(slot, sprite, tint)` (so tests and the 2.4b content author
  need no reflection for entries; the profile itself follows the repo's reflection-set convention).
- `List<SpriteLayerEntry> layers`, `int priority` (tooltip carries the convention above), `List<DefinitionId> hiddenLayerSlots`; read-only accessors `Layers`, `Priority`, `HiddenLayerSlots`.
- The inventory-slot -> layer-slot mapping is authored content: each profile names the layers it paints (a sword -> `layer-weapon`; an outfit -> clothes + feet; a helmet -> `layer-helmet` + hides `layer-hair`).
- **No changes** to `EquipmentVisualProfile`, `EquipmentVisualKind` or `EquipmentVisualBinder`.

### 2. `SpriteEquipmentBinder` (MonoBehaviour, `Runtime/Presentation/Equipment/`, `[DefaultExecutionOrder(-10)]`)

Public contract mirrors `EquipmentVisualBinder`:
- `Initialize(RuntimeEntityId character, IRulesDatabase rulesDatabase, PartyInventoryState inventory, IGameEventBus eventBus)`: identical tuple = no-op; a different tuple while bound throws
  `InvalidOperationException`; **an invalid (`None`) character or a null argument logs one warning and leaves the binder unbound** (a `None` character would otherwise reconcile the whole shared inventory onto it).
- `Unbind()` - unsubscribes, forgets the binding, and **clears exactly the equipment keys this binder set** from whichever state it last wrote to (appearance untouched); clears the warn-once set. Idempotent; from `OnDestroy`.
- `Refresh()` - public re-reconcile (for a profile changed at runtime, and for the Phase 4 preview).
- `SetTarget(SpriteLayerState)` - explicit override for renderer-less consumers; clears owned keys on the previous target and reconciles onto the new one.

Target resolution (fixes the "orphaned state" hole): the binder holds the **`SpriteLayerCompositor`** (a Unity object) and reads `compositor.State` per use. Every `LateUpdate` (ordered before the compositor's), if it is bound and
the compositor reference is null (never found, or **destroyed by a model swap**) it re-resolves with `GetComponentInChildren<SpriteLayerCompositor>(true)`; when the resolved `State` differs from the one it last synced, it
**reconciles onto the new state** (and forgets the old one). So a swapped Model that brings a fresh compositor picks up the character's gear on the next frame with no `CharacterView` change. If no compositor and no explicit target
exist, one warning is logged from the first `LateUpdate` that finds it unresolved, then nothing (no per-event errors).

Subscription and reconciliation:
- Subscribe in `OnEnable` (deferred when inactive at `Initialize`, exactly like 3D), never unsubscribed in `OnDisable`. **`Subscribe()` itself reconciles on every not-subscribed -> subscribed transition** (plus `Initialize` when already
  enabled), so the real load path (`startInactive: true`: deactivate -> `Initialize` -> activate) cannot lose the events that fire in between.
- Reconciliation: clear this binder's owned keys on the target state, then apply every `inventory.ItemsEquippedBy(character)` item at its `GetLocation().Slot`. Invalid `SlotId`s are skipped. Because the state's winner rule is order-independent
  (priority, then ordinal key), the result does not depend on enumeration order, so load-then-bind and bind-then-load agree.

Events:
- `OnItemEquipped`: ignore other characters and payloads missing/`None` `SlotId` or missing `ItemDefinitionId`. Resolve `ItemDefinition` -> `TryResolve<SpriteEquipmentProfile>(EquipmentVisualProfileId)`. A **sprite profile**:
  `state.SetEquipment(slot, profile.Priority, paints, profile.HiddenLayerSlots)` (atomically replaces that slot's previous set). **Unresolved / no profile / an item that resolves to a 3D `EquipmentVisualProfile`**: `state.ClearEquipment(slot)`
  (the slot is now bare); the 3D case warns **once per profile id** ("a 3D profile on a sprite character").
- `OnItemUnequipped`: ignore other characters; `state.ClearEquipment(slot)`.
- **No pending-removal machinery.** `Publish` is synchronous, `EquipmentService` delivers `ItemUnequipped(old)` then `ItemEquipped(new)` inside one call, and the compositor renders only in `LateUpdate`, so Unity cannot present the bare
  intermediate state (verified for the real service, direct bus publishes and reconcile; re-entrant publish/unbind from a handler is also safe since `Action` invocation runs on a snapshot). A sprite set cannot half-fail (a paint with a
  missing sprite is dropped and reported), so the 3D binder's "keep the known-good mesh on a failed replacement" protection has no sprite equivalent. **A genuine unequip followed by a later equip does present a bare frame in between - by design.**
- Bookkeeping: `HashSet<DefinitionId> ownedKeys` (slot keys this binder has set), updated on every set/clear, used by `Unbind`, `SetTarget` and reconciliation. Two binders on one state are unsupported (documented).

### 3. `WorldSpawnService.BindView` wiring

One line after the 3D binder: `instance.GetComponentInChildren<SpriteEquipmentBinder>(true)?.Initialize(entityId, session.RulesDatabase, session.Inventory, session.EventBus);` - covers spawn and every restore path. No public API change (`Gameplay` already references `Presentation`).

### 4. `SpriteEquipmentProfileValidator` (`Editor/Validation/`, pure logic) + window wiring

`Validate(SpriteEquipmentProfile profile, string assetPath, IReadOnlyCollection<DefinitionId> knownLayerSlots = null)` -> `List<ValidationResult>`; `knownLayerSlots` defaults to `SpriteLayerCompositor.DefaultSlots()` (a typo such as `layer-cloths` is the failure this exists to catch, and the state only reports and ignores it at runtime). 
**Error:** a layer with no sprite; an invalid layer slot; a slot painted twice; a painted slot not in the known set; a profile that hides a slot it also paints (it would paint nothing). **Warning:** no layers **and** no hides ("does nothing"); a hidden slot not in the known set; a layer tint with alpha 0 (the usual symptom of a bad copied inspector entry). `PackageValidatorWindow.RunChecks` gets a `t:SpriteEquipmentProfile` loop in the existing per-asset try/catch shape, and the window's help text lists the new check.

## Test plan (tests first; mutation checks recorded)

### EditMode
- `SpriteEquipmentProfileValidatorTests`: each Error and Warning above (including the default known-slot set and an override), a clean profile is silent.
- `SpriteEquipmentProfile` defaults: empty lists non-null, priority 0; `SpriteLayerEntry` constructor round-trip.
- **Wiring, cheap layer** (mirroring `GameSessionTests.cs:478-487` for the 3D binder): a `WorldSpawnService.BindView` path initializes a `SpriteEquipmentBinder` found on a child, including an inactive child.

### PlayMode `SpriteEquipmentBinderTests` (real `EquipmentService`, `GameEventBus`, `PartyInventoryState`, `RulesDatabase`; each test asset gets a distinct reflection-set `id`)
- Equip paints exactly the named layers; unequip restores the appearance beneath.
- **Same-slot replacement through the real service:** a LateUpdate probe never sees an empty presented frame between old and new (kept as a regression guard), and the displaced item is back in the shared inventory. **A multi-frame `Unequip` -> yield -> `Equip` does present the bare frame** (pinning that "no pending-removal" is deliberate). A direct-bus `ItemEquipped` with no preceding `ItemUnequipped` replaces the slot cleanly.
- **Reconciliation:** bind-before-load and load-before-bind show the same picture; one integration case with two equal-priority items proves the winner is stable across insertion order (the exhaustive permutations are already covered by the 2.2 state tests).
- **Deferred subscription:** an instance that is inactive at `Initialize` and activated later receives events fired **between** `Initialize` and activation (the `startInactive` load shape); a binder disabled via the component checkbox tracks gear on re-enable.
- **Model swap:** destroy the compositor's object and add a new one under the same binder: the next frame the new state carries the gear; and the old state's owned keys are not touched afterwards. `SetTarget` swaps: owned keys cleared on the old target, applied to the new one.
- Item with no profile, unresolved profile id, unknown item definition, missing/`None` `SlotId`: slot cleared/ignored, no throw, no stray log. A 3D `EquipmentVisualProfile` id: slot cleared and **one** warning per profile id (cleared by `Unbind`).
- **Hides:** helmet hides hair; unequip restores; head + chest both hiding the same layer released only when both are gone, either order, via the real service.
- Another character's events change nothing. `Initialize(RuntimeEntityId.None, ...)` warns once, stays unbound, and never reconciles the shared inventory onto the target.
- **Initialize contract:** identical re-`Initialize` is a no-op (one equip event applies once - no duplicate subscription); a different tuple while bound throws; `Unbind()` removes exactly the equipment (appearance stays) and later events do nothing; no compositor and no target -> one warning, no exceptions on events; a paint with a null sprite -> the rest of the set applies with one reported warning.
- **`WorldSpawnService` (PlayMode)**: `BindRestoredView` with `startInactive: true`, a template carrying compositor + binder, and an inventory that already has gear equipped shows it once activated, and a later real `Equip` updates it.
- Mutation checks (by hand): cleanup skipped in `Unbind` -> the Unbind test fails; reconcile-on-subscribe removed -> the deferred-subscription test fails; compositor re-resolution removed -> the model-swap test fails; `ItemUnequipped` handler removed -> the restore test fails; `None`-character guard removed -> the `None` test fails.

## Deliverables and acceptance

`SpriteEquipmentProfile.cs` (+ `SpriteLayerEntry`), `SpriteEquipmentBinder.cs` in `Runtime/Presentation/Equipment/`; one line in `WorldSpawnService.cs`; `Editor/Validation/SpriteEquipmentProfileValidator.cs` and a loop + help text in `PackageValidatorWindow.cs`; a tooltip sentence on `ItemDefinition.equipmentVisualProfileId`; tests as above. No asmdef changes (`Presentation` references Core/Actors/Inventory; `CRPG.Editor` references `CRPG.Presentation`; both test asmdefs reference what they need). Acceptance: new tests green, **full EditMode and PlayMode suites green** (baselines EditMode 786, PlayMode 332 + 3 skipped), mutation checks recorded. Visual confirmation on real sprites comes in 2.5's prefab and Gate B.

## Risks / open questions

1. **Reverses a detail of D5** (separate asset). Needs your confirmation; the additive fallback is described above.
2. **The wrapper builder (2.5) must not add the 3D `EquipmentVisualBinder` to sprite wrappers, and must add the `SpriteEquipmentBinder` only to sprite wrappers** (D7). With the separate-asset design the 3D binder is harmless if it slips through (it just sees no profile), which is a deliberate robustness gain.
3. **Priority values are a convention**, not enforced. A content-wide check (two items sharing a layer at equal priority) cannot live in a per-asset validator; 2.4b's authoring notes carry the convention.
4. **Two-handed / off-hand art** does not exist in the kit (Gate A finding); profiles paint `layer-weapon`/`layer-shield` only.
5. **A profile changed at runtime** is picked up only via `Refresh()`; nothing in Phase 2 changes profiles at runtime.

## Review record (Opus, 2026-09-19)

- **B1 cached `SpriteLayerState` orphaned by a model swap** - fixed (hold the compositor, re-resolve every LateUpdate when null, reconcile when the resolved state changes; test). **B2 deferred subscription loses events in the `startInactive` gap** - fixed (reconcile on every subscribe transition; test on the real load shape).
- **S1 per-equip 3D warning spam on every wrapper-built sprite character** - eliminated by the separate-asset design (the 3D binder resolves no profile). **S2/S3 validator silent on kind mistakes / null known-slots** - kind mistakes cannot occur any more; known slots default to the compositor's. **S4 unflagged per-profile priority, undefined semantics** - flagged + tooltip convention. **S5 `None` character reconciles the shared inventory** - guarded + tested. **S6 near-vacuous no-empty-frame test** - kept as a guard, plus the multi-frame bare-frame and direct-bus cases. **S7 warn-once set pins assets** - keyed by profile id, cleared in `Unbind`.
- Minor: M1 moot; M2 replaced by the alpha-0 tint warning; M3 public `SpriteLayerEntry` constructor; M4 window wiring; M5 covered by the `None`-slot guards; M6 public `Refresh()`; M7 no-compositor warning moved to the first unresolved `LateUpdate`; M8 EditMode wiring test added; M9 citations corrected.
- Risk 1 (e): the review recommended raising the separate-asset alternative with the user; adopted here as the plan's default, with the additive fallback recorded.
- Verified correct by the review and unchanged: event order and payloads, `ItemsEquippedBy` semantics for valid characters, the 3D binder contract, `BindView` as the only `Initialize` site, no other `EquipmentVisualKind` consumers, serialization safety, asmdef references, order-independent reconciliation, synchronous-publish safety, lazy `State`, baselines.

## Results (built 2026-09-19, additive design)

**Files:** `Runtime/Presentation/Equipment/SpriteEquipmentBinder.cs` (new); `EquipmentVisualProfile.cs` (+`Sprite` kind, `SpriteLayerEntry`, sprite fields); `EquipmentVisualBinder.cs` (Sprite profile handled as "no 3D visual" + one warning per profile id); `Runtime/Gameplay/WorldSpawnService.cs` (one binder line); `Editor/Equipment/EquipmentVisualProfileEditor.cs` (custom inspector); `Editor/Validation/SpriteEquipmentProfileValidator.cs` + `PackageValidatorWindow.cs` loop and help text. Tests: `Tests/Editor/SpriteEquipmentProfileTests.cs` (32 cases), `Tests/Runtime/SpriteEquipmentBinderTests.cs` (21). No asmdef changes.

**Suites:** EditMode 818/818 (786 + 32); PlayMode 353 passed + 3 skipped (332 + 21); 0 failed.

**Mutation checks (all caught):** `Unbind` cleanup removed -> the Initialize-contract test fails; reconcile-on-enable removed -> the inactive-load test fails; compositor/model-swap recovery removed -> the model-swap test fails (the first attempt at this mutation broke the compile and had to be redone); `ItemUnequipped` handler removed -> 4 tests fail; `None`-character guard removed -> the `None` test fails; the 3D binder's Sprite branch removed -> its regression test fails.

**Notes from building:**
- The inspector rule test also guards the profile against drift: every serialized field on `EquipmentVisualProfile` must be shown for some kinds and hidden for others, so a field added later cannot silently skip the custom inspector's kind decision.
- `PackageValidatorWindow` loops `t:EquipmentVisualProfile` and runs only the sprite-side rules; nothing about the 3D checks changed.
- The 3D binder warns once per profile id and clears that slot's 3D visual for a Sprite profile. **2.5's wrapper builder must not add the 3D binder to sprite wrappers** (otherwise every sprite character logs that once-per-profile message), and must add the sprite binder only to sprite wrappers; recorded as a 2.5 dependency.
- Not covered here by design: real Kenney sprites (Task 2.5 prefab and Gate B).

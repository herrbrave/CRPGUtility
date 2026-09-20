# Task 2.6 Detailed Plan: Sprite combat cues (attack, hit, down, die, recover)

> Status: DRAFT v2 (2026-09-19), post-Opus review, pending user review. Child of `Docs/Plans/2D-Campaign-Phase2-SpritePresentation.md` (Task 2.6, decision D8, finding 7, Opus B1/B3).
> Tasks 2.1-2.5 are committed (`ccfc6d7`, `bff5550`, `818546c`, `8144260`, `f4e2bf8`). The wrapper carries everything except combat feedback; this task adds it.
> The "Review record" at the end lists every Opus finding and its disposition.

## Goal

A sprite character visibly reacts in combat, driven by the real event bus and the real action hook:
- **Attack** - committing an attack/hostile ability: hop + scale punch.
- **Hit** - taking damage: red flash.
- **Down** - dropping to 0 HP (`CreatureDroppedToZeroHP`): tips over, stays down.
- **Die** - `CreatureDied`: completes the tip (if needed), fades to a corpse, terminal.
- **Recover** - genuinely back on its feet (HP above 0 after `CreatureStabilized` or `ConditionRemoved`): stands up.
- **A defeated character is not deleted before its death plays** (small opt-in seam in `WorldSpawnService.MarkDefeated`, Stage B - see the decision below).

Non-goals: directional lunges (needs `IActionPresentationHook` to carry a target - a signature change, deferred to Phase 5), heal-from-0 revival (an engine gap), sound/particles, `CharacterAnimationPresentationHook` changes.

## Decision for your review: defer the destruction of a defeated character (Stage B)

**The problem (verified, Opus B1):** on a real death `TacticalEncounterController.OnCreatureDied` calls `WorldSpawnService.MarkDefeated`, which, synchronously inside the `CreatureDied` publish, does `SetActive(false)`, `agent.Unbind()` and `Destroy` (`TacticalEncounterController.cs:91-95`, `WorldSpawnService.cs:294-314`; the bus invokes handlers synchronously). So **no death cue can ever play in a real encounter**: not one frame of it renders. Every enemy, and every party member that fails three death saves, simply vanishes on the fatal event. (Party members are affected too: every `BindRestoredView` caller supplies a `spawnId`.) A death animation that never plays is not worth building, so either the fade is dropped or the destruction is deferred.

**Recommended (in this plan): a small opt-in seam.** `IDefeatPresentation { void BeginDefeat(Action onFinished); }` in `CRPG.Gameplay`. `MarkDefeated` keeps all its gameplay bookkeeping exactly as today (tombstone the record, forget the faction, `agent.Unbind()`, drop it from `agentsByCharacter`), but if the agent's hierarchy contains an `IDefeatPresentation` it does **not** `SetActive(false)`/`Destroy` immediately: it makes the object inert (disables its colliders, its `IMovable` mover - which releases its grid cell - and the `CharacterAgent` component, which removes it from `CharacterAgent.AllLive`), then calls `BeginDefeat` and destroys the GameObject when the callback fires. Anything without the interface behaves **byte-for-byte as today**, so the humanoid path and every existing `MarkDefeated` test are unchanged. `SpriteActionCues` implements it (plays Die, then invokes the callback after the fade plus a short hold).
**Cost:** a small edit to core spawn-service code (about 20 lines) and its own tests, in a separately labelled stage so it can be dropped. **If you would rather not touch `MarkDefeated`:** drop Stage B, Die becomes "complete the tip, hold" and only lasts as long as the object lives (in the tactical controller that is zero frames, so death would still be invisible for anything the controller removes), and the cue is effectively limited to the *Down* state for characters that get stabilized/revived. I recommend Stage B.

## Facts that shape the design (verified in source)

- **`DamageEffect`** publishes `AfterDamage(Source = attacker, Target = damaged)` after **every applied hit, including zero-damage ones** (resistance/immunity, and the `Amount = 0` that `DeathSaveService` forces on an already-down target) - no payload - and then, only for the hit taking HP from above 0 to 0, `CreatureDroppedToZeroHP` (`Data["Overkill"]`, `Data["IsCritical"]`) (`DamageEffect.cs:78-92`). A miss publishes nothing (`AttackRollEffect` narrows to hits first).
- **`DeathSaveService`**: overkill >= max HP -> `CreatureDied` **from inside its own `CreatureDroppedToZeroHP` handler**, so a subscriber sees **`CreatureDied` first, then `CreatureDroppedToZeroHP`**; otherwise Unconscious + death saves. **Three failed saves -> `CreatureDied`. Three successes -> `CreatureStabilized` with the character still at 0 HP and unconscious. A natural 20 -> HP restored to 1, Unconscious removed, then `CreatureStabilized`** (`DeathSaveService.cs:53-125`). A hit on an already-down character is recorded in `BeforeDamage` and can publish `CreatureDied` **before** `AfterDamage` (a critical counts two failures), so the killing blow on a downed character produces no flash. `CreatureStabilized` alone therefore does not mean "stand up": the check is **HP > 0** (this corrects the phase doc's "Recover on `CreatureStabilized`").
- **`HealEffect`** restores HP, publishes nothing, and clears neither death saves nor the Unconscious condition (`HealEffect.cs:26-33`). **`ConditionRemoved`** *is* published by `RemoveConditionEffect`, `DispelEffect`, `ConditionDurationService` and `ConcentrationEffectCleanup` (Target = the affected character; no condition id in the payload), but not by `DeathSaveService`'s own removal. The realistic revive in content is a heal plus `RemoveConditionEffect(unconscious)` in one ability, which publishes a usable event.
- **`IActionPresentationHook.RequestActionCue(source, ability)`** carries no target, is called once at commit, fire-and-forget, wrapped in try/catch (`AbilityActionExecution.cs:147, 152-166`); exactly one hook per `ActionScheduler`/`TacticalEncounterController`. `CharacterAgent.AllLive` is the live-agent set (`OnEnable`/`OnDisable`). `AbilityTag` is a `[Flags]` enum (`Attack`, `Hostile`, `Healing`, ...). Nothing in Runtime calls `RequestTerminal` for humanoids either: death presentation is composition-root work today.
- `GameEventBus.Publish` is a synchronous multicast invoke with **no** try/catch and duplicate subscription double-fires; a handler exception aborts later handlers and faults the publisher (`DamageEffect.Apply`, `DeathSaveService.RecordFailure`).
- The card (2.5): `Model` -> `RockPivot` (owned by `RockingMotionDriver`: rotation only) -> `Layers` (baseline local `(0, 0.5, 0)`, the compositor's transform and its `layersRoot` when unset) -> layer renderers. Nothing writes `Layers`. The default sprite materials (URP `Sprite-Unlit-Default` in the editor, `Sprites/Default` in a player) are alpha-blended and multiply vertex colour, so a `SpriteRenderer.color` multiplier and an alpha fade render.
- No `resource-hp`/`condition-unconscious` literal exists anywhere in `Runtime/` or `Editor/`; the ruleset ids live only in `Rules/SRD5e2014` and tests.
- Restore order is safe: `SaveService.Restore` (which restores `DeathSaves`) runs before `BindRestoredView`, which throws for an unregistered character.

## Design

### 1. `SpriteCueState` + `SpriteCueSettings` (`CRPG.Presentation.Sprites`, pure)

`[Serializable] SpriteCueSettings` (class with initializers, sanitized by the state): `hopHeight 0.15`, `attackSeconds 0.25`, `attackScaleBoost 0.2`, `hitSeconds 0.20`, `hitTint (1, 0.35, 0.35, 1)`, `tipDegrees 90`, `tipSeconds 0.25`, `dieFadeSeconds 0.6`, `deadAlpha 0.4`, `defeatHoldSeconds 0.5`.

`SpriteCueState(settings)`: `PlayAttack()`, `PlayHit()`, `Down()`, `Die()`, `Recover()`, `SnapDown()`, `Reset()`, `Tick(dt)`; read-only `IsDown`, `IsDead`, `IsFadeComplete`, and the pose: `HopOffset`, `Scale`, `Tint` (a colour multiplier including alpha), `TipDegrees`. Rules (each a test):
- **Attack:** half-sine over `attackSeconds`: hop `0 -> hopHeight -> 0`, scale `1 -> 1+boost -> 1`; re-trigger restarts; ignored while Down/Dead; Down/Die cancel a running attack.
- **Hit:** tint lerps `hitTint -> white` over `hitSeconds`; re-trigger restarts; allowed while Down; ignored once Dead. (`AfterDamage` fires for zero-damage hits too, so an immune target flashes; recorded and accepted - the event carries no amount.)
- **Down:** tip eases to `tipDegrees` over `tipSeconds` and persists until Recover or Die; idempotent.
- **Die:** **terminal**: completes the tip if needed, fades tint alpha to `deadAlpha` over `dieFadeSeconds`, stays. After Die: attack, hit, Down and **Recover are ignored**; only `Reset()` clears it. (So the real event order for massive damage - `CreatureDied` then `CreatureDroppedToZeroHP` - leaves the character Dead.)
- **Recover:** only from Down: tip eases back to 0, state Neutral; a no-op when not Down; ignored when Dead.
- **`SnapDown()`:** Down with the tip complete (restored while unconscious).
- `Tick`: `dt <= 0`/`NaN` no-op, clamped to 0.1 s; settings sanitized (non-finite/non-positive fall back, `deadAlpha` clamped to 0..1). The state never sees events, HP or the bus.

### 2. Event mapping - `SpriteCueEventBinding` (pure C# over the bus, EditMode-testable)

`SpriteCueEventBinding(RuntimeEntityId character, IGameEventBus bus, ICharacterRegistry characters, DefinitionId hitPointResourceId, SpriteCueState state)`; `Dispose()` unsubscribes **and sets a `disposed` flag every handler checks first** (an EditMode `DestroyImmediate` runs `OnDestroy -> Dispose` inside the very publish being handled, and `Delegate.Remove` does not affect the already-captured invocation list). Filter `context.Target == character`:
- `AfterDamage` -> `PlayHit()`; `CreatureDroppedToZeroHP` -> `Down()`; `CreatureDied` -> `Die()`.
- `CreatureStabilized` **and `ConditionRemoved`** -> `Recover()` **only if the character's current HP is above 0** (the payloads carry nothing else to filter on). A stable-at-0 character stays Down; an unknown character, a resource missing from it, or **no configured hit-point id -> never Recover** (do not guess). (D8's second Recover trigger, the unconscious condition being removed, is kept here; it is the only event a heal-plus-`RemoveCondition` revive publishes.)
- **Every handler body is wrapped in try/catch + `Debug.LogException`** and never touches a `UnityEngine.Object` member: the bus has no isolation, so a throwing presentation subscriber would otherwise abort the remaining handlers and fault `DamageEffect.Apply`.
- Subscribing twice must not double-fire (guarded).

### 3. `SpriteActionCues` (MonoBehaviour on CharacterRoot, `[DefaultExecutionOrder(10)]`; implements `IDefeatPresentation`)

- Fields: `SpriteCueSettings settings`, `DefinitionId hitPointResourceId` (**default `DefinitionId.None`**: the toolkit engine stays ruleset-agnostic; the campaign supplies `resource-hp` via its authoring spec).
- `Initialize(character, bus, characters)`: invalid/null args warn once and stay unbound; identical re-init no-op; a different tuple while bound throws. **Unlike the equipment/appearance binders it subscribes in `Initialize` (there is nothing to reconcile) and `OnDisable` does not unsubscribe** - stated explicitly rather than claimed as parity. It starts `SnapDown()` if the character is already down: **`DeathSaves != null` or (with a configured hit-point id) current HP <= 0** - so a character restored stable-at-0 (three successes leaves `DeathSaves == null`, HP 0, Unconscious) also appears Down. `Unbind()`/`OnDestroy` dispose the binding.
- `PlayAttack()` for the hook.
- `LateUpdate`: `state.Tick(Time.deltaTime)`; resolve the compositor (`GetComponentInChildren<SpriteLayerCompositor>(true)`) and apply to its **`LayersRoot`** (a small public accessor added to the compositor: its `layersRoot` if set, else its own transform). **The baseline (local position/scale) is captured per compositor instance, strictly before the first pose write to that instance** (a model swap leaves two compositors alive for a frame, so identity-keyed capture is required or the baseline would absorb a live pose); on a new compositor the current pose is reapplied, so a swap while Down keeps the corpse down. Pose: `Layers.localRotation = Euler(0,0,-tip)`; `Layers.localScale = baseScale * s`; **`Layers.localPosition = Rot(-tip) * (baseline * s) + (0, hop, 0)`** - the tip is a rotation about the character's **feet** (the `RockPivot` origin, without touching `RockPivot`, which the rocking driver owns) and the attack punch also scales about the feet, so it grows upward, not down through the ground. `compositor.TintMultiplier = state.Tint`. Writes are skipped while the pose is unchanged; the neutral pose restores the baseline exactly. Collider, `CharacterRoot`, `RockPivot` and the sorter anchor are never touched.
- **Corpse footprint (anticipated Gate B item):** at tip 90 the card extends a full unit from the feet along screen-right, i.e. half a cell past its own cell.
- **`BeginDefeat(Action onFinished)`:** `state.Die()`; when the fade is complete plus `defeatHoldSeconds`, invoke `onFinished` exactly once (also invoked immediately if the state is already Dead and faded). Scaled time only.

### 4. Compositor additions (2.2 code, additive)

`SpriteLayerCompositor.TintMultiplier` (default white; every renderer's colour is `layer.Tint * TintMultiplier`; the setter re-applies unless unchanged, since `Apply` is otherwise version-gated) and `LayersRoot`. Default behaviour is unchanged (all 2.2/2.4 tests pass unchanged). A multiplier cannot brighten, so the hit flash is a red tint, not a white flash.

### 5. `SpriteActionPresentationHook` (`CRPG.Gameplay`)

`IActionPresentationHook`: finds the source's live `CharacterAgent` (the `CharacterAnimationPresentationHook` lookup), then its `SpriteActionCues`, and calls `PlayAttack()` when `ability.Tags` has `Attack` or `Hostile`; other abilities play nothing; a null ability, missing agent or missing cues never throws. It ignores `AnimationCueId` (humanoid clip names). Mixed humanoid + sprite parties compose two hooks themselves; no composite is built until something needs one.

### 6. Stage B - the defeat seam (`CRPG.Gameplay`)

`IDefeatPresentation { void BeginDefeat(Action onFinished); }` and the `WorldSpawnService.MarkDefeated` change described above. **Unchanged when no implementer exists.** When one exists: gameplay bookkeeping identical to today; the object is made inert first (colliders off, `IMovable` mover disabled so its grid cell is released, `CharacterAgent` component disabled so it leaves `AllLive`); destruction happens in the callback (`DestroyGameObject`, the existing Editor-safe helper); a throwing `BeginDefeat` falls back to the old immediate destroy; session-swap teardown is unaffected (the corpse is no longer in `agentsByCharacter`, so it finishes and destroys itself; a `Destroy` on an already-destroyed object is harmless).

### 7. Wiring and authoring

- `WorldSpawnService.BindView`: `instance.GetComponentInChildren<SpriteActionCues>(true)?.Initialize(entityId, session.EventBus, session.Characters);`.
- `CharacterSetupSpec.SpriteHitPointResourceId` (**default `DefinitionId.None`**); `CharacterWrapperBuilder` adds `SpriteActionCues` to CharacterRoot for Sprite presentation with that id; the "Humanoid spec on a sprite wrapper" refusal lists it. **`BarrowsEndCharacterAuthor` sets `resource-hp`.** The 2.5 fixtures, the drift guard and the BarrowsEnd `SpriteCharacter.prefab` are regenerated (the BarrowsEnd test re-authors its prefab itself; only the committed asset needs a manual re-author).

## Test plan (tests first; mutation checks recorded)

### EditMode
- **`SpriteCueStateTests`:** every rule above with time-stepped assertions (attack peak at half time and exact return; restart; ignored while Down/Dead; hit tint/restart; hit allowed while Down, ignored when Dead; Down persists; Die from neutral and from Down; **after Die, attack/hit/Down/Recover are ignored**; Recover; `SnapDown`; fade completion flag; dt 0/negative/NaN/huge; garbage settings stay finite; `Reset`).
- **`SpriteCueEventBindingTests`:** only `Target == me` reacts; each event mapping; `CreatureStabilized` and `ConditionRemoved` with HP > 0 -> Recover, HP 0 -> Down, unknown character/resource or **no hit-point id configured** -> Down; `Dispose` stops reacting **including a dispose issued from inside a handler for the same publish**; no double subscription; **a throwing state/handler does not fault the publisher or stop later handlers.**
- **Real-engine integration** (real `DamageEffect` + SRD5 `DeathSaveService` + `GameEventBus`, the `DeathSaveServiceTests` fixtures): non-lethal hit -> Hit only; hit to exactly 0 HP -> **Down, not Dead**; a further hit while down -> flash, still Down; three failed saves -> Die; **overkill >= max HP -> the binding sees `CreatureDied` then `CreatureDroppedToZeroHP` and ends Dead** (named test); **the killing blow on a downed character produces `CreatureDied` before `AfterDamage` and ends Dead with no flash** (named test, including a critical counting two failures); three successes -> Stabilized at 0 HP -> **still Down**; natural 20 -> HP 1 -> **Recover**; a heal plus `RemoveConditionEffect` on a downed character -> Recover via `ConditionRemoved`; a Die is never undone by a later Stabilized/ConditionRemoved.
- **`SpriteActionPresentationHookTests`:** the tag rule (Attack, Hostile play; Healing/Utility/Movement not; Healing|Hostile plays), null ability, no agent, no cues component.
- **Compositor:** `TintMultiplier` multiplies every layer colour, survives a rebuild, white changes nothing, an unchanged set does not rewrite; `LayersRoot` honours `layersRoot`.
- **Builder/validator/drift:** sprite wrappers carry `SpriteActionCues` **with the spec's id value asserted** (the drift guard compares type names only); the refusal lists it.

### PlayMode (real fixture prefab, real `WorldSpawnService`)
- Hit -> renderer colours go red then return; attack via the hook (real `CharacterAgent.AllLive`) -> `Layers` hops/scales about the feet and returns to its exact baseline; Down -> `Layers` rotation reaches -90 about z and its position equals the baseline rotated about the feet, while the collider bounds and the `CharacterRoot`/`RockPivot` transforms are unchanged; Recover -> exact baseline; Die -> alpha fades to `deadAlpha` and stays.
- **Model swap:** baseline recaptured per compositor instance before any write (the two-compositor frame), a swap while Down keeps the new card tipped; **restored unconscious** (`DeathSaves` set) **and restored stable-at-0** (`DeathSaves == null`, HP 0) appear Down.
- Another character's events change nothing; `Time.timeScale = 0` freezes the pose (timeScale restored in teardown); Initialize contract (identical re-init no-op, different tuple throws, `Unbind` stops reacting, invalid args warn once; `OnDisable` does not unsubscribe).
- **Stage B:** `MarkDefeated` on a cues character: the record is tombstoned, the agent unbound and out of `AllLive`, colliders and mover disabled (its grid cell released), the object still alive and fading, destroyed after the fade + hold; a character **without** cues is destroyed immediately exactly as before; a throwing `BeginDefeat` falls back to immediate destroy; **driven through a real `TacticalEncounterController`** (the existing `TacticalEncounterControllerTests` scaffolding) so the test covers the path the real composition root takes (the bare-spawn-service path would have passed while the real one destroyed the object).
- **Mutation checks (by hand):** Recover on Stabilized regardless of HP -> the stable-at-0 test fails; Die undone by Recover -> the terminal test fails; attack allowed while Down -> its test fails; tip without the feet compensation -> the position assertion fails; scale about the centre instead of the feet -> the punch-position assertion fails; `TintMultiplier` ignored -> the hit-flash tests fail; `BindView` line removed -> the wired-through-spawn test fails; `SnapDown` skipped -> the restored tests fail; the `disposed` flag removed -> the dispose-inside-handler test fails; the try/catch removed -> the throwing-handler test fails; `MarkDefeated` ignoring `IDefeatPresentation` -> the deferral test fails.

## Deliverables and acceptance

New in `Presentation/Sprites/`: `SpriteCueSettings.cs`, `SpriteCueState.cs`, `SpriteCueEventBinding.cs`, `SpriteActionCues.cs`; edits to `SpriteLayerCompositor.cs`; new `Gameplay/Presentation/SpriteActionPresentationHook.cs` and `Gameplay/IDefeatPresentation.cs`; edits to `WorldSpawnService.cs`, `CharacterSetupSpec.cs`, `CharacterWrapperBuilder.cs`, `BarrowsEndCharacterAuthor.cs`; regenerated fixtures and the BarrowsEnd prefab; tests. No asmdef changes (`CRPG.Presentation` references Core/Actors, `CRPG.Gameplay` references Abilities, the EditMode test assembly references the SRD5 assembly). Acceptance: new tests green; **full toolkit EditMode and PlayMode suites green** (baselines EditMode 878, PlayMode 372 + 3 skipped); BarrowsEnd EditMode green (baseline 11); mutation checks recorded. The look (tip direction, red flash readability, fade, the half-cell corpse) is judged at Gate B.

## Risks / open questions

1. **Heal-from-0** revives nothing in the engine (`HealEffect` clears no death-save state) and publishes nothing; the visual recovers only through a `CreatureStabilized`/`ConditionRemoved` event with HP > 0. Recorded as an engine gap, not fixed.
2. **Down outside combat is permanent:** only `TacticalAiTurnDriver` routes a downed character to death saves, so a character dropped to 0 HP in exploration stays down with no save loop. Not this task's bug; recorded.
3. **Red flash, not white** (a multiplier cannot brighten); a white flash needs a shader.
4. **One hook per scheduler, no target on it**: attack is a hop in place; mixed humanoid + sprite parties need a composite hook that does not exist.
5. **Tag rule for attack cues** (`Attack` or `Hostile`) is a heuristic over ability tags.
6. **Stage B touches core spawn-service code** (kept opt-in and separately tested); declining it leaves death invisible in the real encounter flow.
7. **Zero-damage hits flash** (the event carries no amount).

## Review record (Opus, 2026-09-19)

- **B1 the object is destroyed inside the `CreatureDied` publish, so the Die fade is unreachable** - new decision + Stage B (opt-in `IDefeatPresentation` seam), the real-controller PlayMode test, risk 6 corrected. **B2 an SRD5 id in the engine** - the hit-point id defaults to `None` in the component and the spec; the campaign author sets `resource-hp`.
- **S1 the D8 condition-removed Recover dropped without recording** - restored as a `ConditionRemoved` trigger with the same HP gate. **S2 `SnapDown` predicate misses stable-at-0** - `DeathSaves != null || HP <= 0`, tested. **S3 nested-publish orderings** - both stated as facts and named tests. **S4 no exception isolation** - try/catch per handler + `disposed` flag, tests. **S5 scale about the centre vs tip about the feet** - both about the feet; the half-cell corpse footprint recorded for Gate B.
- Minor: zero-damage flashes recorded; baseline captured per compositor instance before the first write, tested; subscription lifecycle stated (in `Initialize`, `OnDisable` keeps it); execution order 10; `LayersRoot` accessor; fixture value asserted in the builder test; citations fixed; down-outside-combat risk added.
- Verified correct by the review and unchanged: the `DamageEffect`/`DeathSaveService`/`HealEffect` claims, other death-event publishers, `GameEventBus` semantics, the hook contract and `AllLive`, no writer of `Layers`, the rotate-about-feet math, alpha rendering, asmdef needs, the builder insertion points, PlayMode timing approach, the scope.

## Results (built 2026-09-19)

**Toolkit runtime:** `Core/IDefeatPresentation.cs` (in `CRPG.Core`, not `CRPG.Gameplay` as this plan first said: Presentation cannot reference Gameplay, and `SpriteActionCues` implements it); `Presentation/Sprites/{SpriteCueSettings, SpriteCueState, SpriteCueEventBinding, SpriteActionCues}.cs`; `Gameplay/Presentation/SpriteActionPresentationHook.cs`; `SpriteLayerCompositor` (`LayersRoot`, `TintMultiplier`); `WorldSpawnService` (`BindView` initializes the cues, `MarkDefeated` defers the destroy via `TryDeferDestroy`, with the KNOWN LIMITATIONS comment block the user asked for).
**Toolkit editor:** `CharacterSetupSpec.SpriteHitPointResourceId`; `CharacterWrapperBuilder` adds `SpriteActionCues` for Sprite wrappers, sets its hit-point id, and lists it in the humanoid-on-sprite refusal.
**BarrowsEnd:** `BarrowsEndCharacterAuthor` sets `resource-hp`; the committed `SpriteCharacter.prefab` was re-authored (it carries `hitPointResourceId: resource-hp`).
**TestProject:** fixtures regenerated.

**Tests:** EditMode `SpriteCueStateTests` (16), `SpriteCueEventBindingTests` (16, incl. real `DamageEffect` + SRD5 `DeathSaveService` for: massive damage publishes Died before DroppedToZero; killing blow on a downed character publishes Died before AfterDamage; a critical on a downed character with one failure kills; 3 successes stays Down; natural 20 recovers; heal + `RemoveConditionEffect` recovers via `ConditionRemoved`; no hit-point id never recovers; dispose from an earlier handler in the same publish), 2 builder tests + 1 refusal test. PlayMode `SpriteActionCuesTests` (21, on the real fixture prefab through the real `WorldSpawnService`, incl. a real `TacticalEncounterController` receiving `CreatureDied`), 2 compositor tests.

**Suites:** toolkit EditMode 912/912 (878 + 34); toolkit PlayMode 395 passed + 3 skipped (372 + 23); BarrowsEnd EditMode 11/11.

**Mutation checks (all caught):** Recover ignoring HP (4 fail); Recover undoing Die (1); attack allowed while Down (1); `disposed` flag removed (1); tip without feet compensation (1); `TintMultiplier` ignored in Apply (1); `BindView` Initialize line removed (9); `SnapDown` skipped at bind (3); `MarkDefeated` ignoring `IDefeatPresentation` (3). **Not mutation-checked:** the per-handler try/catch in `SpriteCueEventBinding` (no test can make `SpriteCueState` throw; it is a guard against a faulting subscriber aborting the bus).

**Findings / deviations:**
- `IDefeatPresentation` moved to `CRPG.Core` (see above).
- A layer with no sprite has a transparent (0,0,0,0) tint, so a tint multiplier is unobservable on it; PlayMode tests give the body layer a sprite.
- Instantiating an inactive template yields an inactive clone; `BindRestoredView(startInactive: false)` does not activate it, so the test helper activates it.
- The try/catch guard above and the "no timeout" limitation of the deferred destroy are the known lingering risks (documented in `WorldSpawnService.MarkDefeated` and `IDefeatPresentation`).
- Engine gaps (recorded, not fixed): `HealEffect` publishes nothing and clears no death-save state, so a heal from 0 HP does not revive a character in the presentation unless something also removes the unconscious condition; a character downed in exploration stays down.
- Not verified here (Gate B, Task 2.7): how the hop, red flash, tip and fade look and feel on real tiles.

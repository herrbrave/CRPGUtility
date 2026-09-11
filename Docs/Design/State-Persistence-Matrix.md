# State Persistence Matrix

Companion to `Docs/Verification/Production-Baseline.md` (Task 0.1). Every field below was read from current source this session, not carried over from design-doc claims. Classification:

- **Persisted** — round-trips through `SaveService.Capture`/`Restore` today.
- **Missing** — mutable and gameplay-relevant, but no save path exists yet.
- **Derived** — recomputed from other persisted state; correctly excluded from saves.
- **Transient** — legitimately session-only (UI/animation-request bookkeeping); correctly excluded from saves.

## CharacterState (`Runtime/Actors/CharacterState.cs`)

| Field | Class | Capture/restore code | Notes |
|---|---|---|---|
| `Stats` (StatBlock) | Persisted | `SaveService.cs:76,103` | |
| `Resources` (dict) | Persisted | `SaveService.cs:77-78,104-105` | Max + Current both captured |
| `Conditions` (list) | Persisted | `SaveService.cs:79-90,106-114` | Source/Target/RemainingDuration/StackCount all captured |
| `Proficiencies` (set) | Persisted | `SaveService.cs:91,115` | |
| `Features` (list) | Persisted | `SaveService.cs:92,116` | |
| `Level` | **Missing** | none | Not read anywhere in `CaptureCharacters`/`RestoreCharacters`. A restored character's `Level` reverts to the constructor default (1) regardless of actual level — proficiency bonus, which is level-derived (`ProficiencyBonus.cs`), would silently recompute wrong after load. |
| `DeathSaves` (nullable) | **Missing** | none | A character mid-death-save sequence loses that state entirely on reload; would appear at 0 successes/failures un-dying on next damage instead of continuing the existing sequence. |
| `SpellcastingStatId` | **Missing** | none | Reverts to `DefinitionId.None` (non-caster) on reload for any actual caster restored via `RestoreCharacters` — `AttackRollEffect`/`SavingThrowEffect` would resolve spell attacks/DCs against the wrong (default/zero) stat. |
| `ConcentratingOnAbilityId` | **Missing** | none | A save mid-encounter (once combat saves are supported) would silently drop active concentration with no matching effect cleanup signal. |
| `CurrentAnimationCueId` | Transient (correctly excluded) | — | Purely a "last requested visual cue" record per its own doc comment; not gameplay-authoritative. Reasonable to leave out, but should be explicitly documented as transient rather than just absent. |
| `KnownAbilities` (set) | **Missing** | none | A restored character forgets every spell/ability it permanently knows — for a Known-caster this is total spell-list loss, not a cosmetic gap. |
| `PreparedAbilities` (set) | **Missing** | none | A Prepared-caster's current day's spell selection is lost; would need to re-prepare every reload even though this isn't derivable from anything else persisted. |

**Net: 5 of 12 fields persisted, 6 missing, 1 correctly transient.** This confirms the design doc's Repository-grounded-baseline table entry for `Saving/SaveService.cs` — independently reproduced here, not just cited.

## PartyState (`Runtime/Party/PartyState.cs`)

| Field | Class | Capture/restore code |
|---|---|---|
| `Members` | Persisted | `SaveService.cs:51,59` |
| `Selected` | Persisted | `SaveService.cs:52,62` |
| `ActiveCharacter` | Persisted | `SaveService.cs:53,64-66` |
| group membership (`groupByMember`) | Persisted | `SaveService.cs:54,60` |

No gaps found — every field `PartyState` exposes has a matching capture/restore line.

## PartyInventoryState / ItemInstance (`Runtime/Inventory/`)

| Field | Class | Capture/restore code |
|---|---|---|
| `InstanceId`, `DefinitionId`, `Quantity`, `Charges` | Persisted | `SaveService.cs:127-136,147-151` |
| `CustomState` (dict) | Persisted | `SaveService.cs:137,152` |
| Location (shared vs. equipped char+slot) | Persisted | `SaveService.cs:134-136,154-156` |

No gaps found in `ItemInstance` itself.

## World/scene inventory state — not modeled at all

| State | Class | Capture/restore code | Notes |
|---|---|---|---|
| `StoreInventoryState.Listings`, `BuyPriceMultiplier`, `SellPriceMultiplier` | **Missing** | none | Confirmed via grep: `StoreInventoryState`/`StoreListing` appear nowhere under `Runtime/Saving/`. A merchant's stock and any story-driven price change (`SetStorePriceMultiplierAction`) reset to construction-time defaults every reload. |
| `LootContainerState.Items` | **Missing** | none | Same grep confirms no reference. An unlooted chest/corpse's contents are not persisted; whoever owns the container has no save path to call. |

Both types are plain runtime objects with no save-DTO counterpart in `SaveData.cs` at all (only `Party`, `Characters`, `InventoryItems`, `TimelineEvents`, `Quests` exist there today).

## Timeline (`Runtime/Core/Timeline/`)

| Field | Class | Capture/restore code |
|---|---|---|
| Every `TimelineEvent` (Key/Sequence/Value/Amount/Source/Timestamp) | Persisted | `SaveService.cs:162-196`, via `LoadEvent` preserving original ordering |

No gaps. Already designed for save/restore from the outset (see `[[project-crpg-timeline-system]]`).

## QuestLog / QuestState / QuestObjectiveState (`Runtime/Quests/`)

| Field | Class | Capture/restore code | Notes |
|---|---|---|---|
| `QuestId`, `Status` | Persisted | `SaveService.cs:202,221` | |
| `Objectives[].CurrentCount`, `.IsComplete` | Persisted | `SaveService.cs:205-210,224-228` | |
| Activation cursor (since-activation objective start point) | N/A — field doesn't exist yet | — | `QuestObjectiveState` (`Runtime/Quests/QuestObjectiveState.cs`) only has `CurrentCount`/`IsComplete` today; there is no cursor field to be missing from the save path. This is a **design gap**, not a save gap — confirms Task 3.4's premise directly from current source rather than from the design doc's claim alone. |
| Reward-claim marker (dedupe reload/repeated refresh) | N/A — field doesn't exist yet | — | Same: nothing in `QuestState`/`QuestObjectiveState` tracks whether a reward was already granted. Needed before Task 3.4's "reload cannot duplicate reward" acceptance criterion is even representable. |

## Scene/world continuity — no representation at any layer

Confirmed by reading `NpcSpawnService.Spawn` end to end and grepping for "spawn" across `Runtime/`:

- No stable authored spawn IDs independent of per-session `RuntimeEntityId`s exist anywhere.
- No persisted position/facing for any character (player or NPC) exists anywhere.
- No defeated/removed-spawn tracking exists.
- No current-scene identity is captured.

This is not a save-service omission to patch — per the design doc §7 and Task 3.5, `WorldSaveData`/spawn-ID plumbing has to be designed and built from zero. Recorded here so Phase 1 (which introduces `WorldSpawnService`/`CharacterAgent`) has an accurate "there is nothing here yet" starting point rather than assuming partial existing coverage.

## Derived state correctly outside any save (sanity check, not a gap)

- `AbilityModifier`/`ProficiencyBonus`/`PassiveScore` (`Runtime/Actors/`) — pure functions of `Stats`/`Level`, correctly never serialized directly.
- `MotionSnapshot`-equivalent runtime velocity/path state (not yet built — see design §4.3) — will be derived from the motor each frame, should never be persisted once it exists.
- Combat-session state (`CombatEncounter`, initiative order, turn resources) — `SaveData` deliberately has no `CombatState`; matches the design doc's explicit call-out that combat-save is out of scope for the initial safe-point-save gate (§7, §8 Gate C "post-combat save" implies saves happen *after* combat ends, not mid-encounter).

## Summary of what Phase 1/3 work must not silently skip

1. `CharacterState`: add `Level`, `DeathSaves`, `SpellcastingStatId`, `ConcentratingOnAbilityId`, `KnownAbilities`, `PreparedAbilities` to `CharacterSaveData`/`CaptureCharacters`/`RestoreCharacters`. (`CurrentAnimationCueId` stays deliberately transient.)
2. Add `StoreSaveData`/`LootContainerSaveData` (or equivalent) and wire them into `SaveData`/`SaveService` — currently entirely unbuilt.
3. Add an activation-cursor field and a reward-claim marker to `QuestObjectiveState`/`QuestState` before Task 3.4's save-schema work, since the save gap and the design gap are the same underlying missing field, not two separate problems.
4. Design `WorldSaveData` (spawn IDs, position/facing, defeated-spawn set, current scene) from zero — there is no partial implementation to extend.

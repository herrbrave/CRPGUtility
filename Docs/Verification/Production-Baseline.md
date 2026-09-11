# Production Foundation — Task 0.1 Baseline

Date: 2026-09-10. Recorded by Sonnet 5 per `Docs/Plans/Sonnet-Production-Foundation-Handoff.md` and `Docs/Plans/Production-Foundation-Implementation.md`. This is a point-in-time snapshot, not a certification.

## 1. Source-control state at start of session

- No other Claude session was reachable/running (`ListAgents` returned none besides self).
- Live Unity Editor connected via Pipeline CLI: `6000.6.0f1`, project `TestProject`, port 7800, PID 4640, state `ready`. Did not close it; used `unity command ... run_tests -- editor` against the live instance per the plan's verified command.
- Working tree has substantial uncommitted work already present (not created this session): modified files across `crpg-toolkit/Runtime/Dialogue/`, `Runtime/Actors/CharacterState.cs`, `Runtime/Core/Events/GameEvent.cs`, `Runtime/Inventory/{EquipmentService,ItemDefinition}.cs`, Oakhaven dialogue assets, Editor dialogue graph views, plus untracked new files: `CharacterPlayableAnimator.cs`, `CharacterAnimationProfile.cs`, `AnimationOrderService.cs`, `CharacterVisualEquipment.cs`, `SerializableGuid.cs` + drawer, four peasant-outfit `ItemDefinition`s and clothing equipment slots, `BaseHumanMaleCharacter.prefab`/`BaseNPC.prefab`, three TestProject `Assets/Editor/*Author.cs` one-shot scripts, and the three planning docs (`Production-Foundation.md`, `Production-Foundation-Implementation.md`, `Sonnet-Production-Foundation-Handoff.md`) themselves. None of this was touched, reset, or committed this task. Full `git status --porcelain=v1 -uall` output was captured but is not reproduced here since it will drift immediately.

## 2. EditMode test baseline (reproduced independently)

Command run against the live Editor:

```
unity.exe --no-banner --no-pager --format json command --project-path 'C:\Users\Peter\wkspaces\CRPGUnity\TestProject' --timeout 360 run_tests -- editor
```

Result: `success: true`, inner `Summary`: **Total 367, Passed 367, Failed 0, Skipped 0, Inconclusive 0**. Matches the handoff's cited historical number exactly — confirmed fresh, not assumed. Full JSON archived by the tool at `C:\Users\Peter\.claude\projects\c--Users-Peter-wkspaces-CRPGUnity\9fe8855e-0e8a-4205-a964-bd9182d53a24\tool-results\b2grxjrrd.txt`.

## 3. PlayMode test inventory

`crpg-toolkit/Tests/Runtime/CRPG.Tests.PlayMode.asmdef` exists (references `CRPG.Core`, `CRPG.Rules`, `CRPG.Actors`, `CRPG.Party`, `CRPG.Navigation`, `CRPG.Combat`, `UnityEngine.TestRunner`) but **contains zero `.cs` files** — confirmed via `find crpg-toolkit/Tests/Runtime -iname "*.cs"` returning nothing. The asmdef is scaffolding only; there is currently no PlayMode test coverage at all. This is the literal case the plan warned about ("an asmdef is not test coverage").

## 4. Mutable state inventory

See `Docs/Design/State-Persistence-Matrix.md` for the full field-by-field classification. Headline findings, each independently confirmed by reading the current source (not assumed from the design doc):

- `CharacterState` (`Runtime/Actors/CharacterState.cs`) has 12 pieces of mutable state; `SaveService.CaptureCharacters`/`RestoreCharacters` (`Runtime/Saving/SaveService.cs:69-120`) round-trips only 5 of them (Stats, Resources, Conditions, Proficiencies, Features). **Missing from save/restore: `Level`, `DeathSaves`, `SpellcastingStatId`, `ConcentratingOnAbilityId`, `CurrentAnimationCueId`, `KnownAbilities`, `PreparedAbilities`.** A restored character reverts to Level default(0)/unset, loses concentration/dying state, forgets which spells it knows, and can't cast anything level-gated.
- `StoreInventoryState` and `LootContainerState` (`Runtime/Inventory/`) have no capture/restore path anywhere in `SaveService` or `SaveData` — confirmed by grep, neither type is referenced in `Saving/`. A merchant's stock/price multipliers and any unlooted container contents do not survive save/load.
- No scene/world-level state exists as a concept at all: no spawn IDs, no persisted position/facing, no defeated/removed-spawn tracking. `NpcSpawnService.Spawn` (`Runtime/Actors/Npc/NpcSpawnService.cs`) builds a `CharacterState` only — confirmed by reading it end to end — and documents in its own doc comment that visual instantiation, equipping starting items, and dialogue wiring are all left to the caller. There is nothing to migrate into `WorldSaveData` yet; it has to be designed from zero, not extracted from an existing partial implementation.
- `PartyState`/`PartyInventoryState`/Timeline/`QuestLog` round-trip appears complete against their current field sets (Members/Selected/ActiveCharacter/Groups; all `ItemInstance` fields including `CustomState`; full event log via `LoadEvent`; quest status + per-objective count/complete). No missing fields found in these four.
- `QuestObjectiveState` currently has no activation-cursor field (`CurrentCount`/`IsComplete` only) — confirms the design doc's Task 3.4 premise that "since-activation" objective semantics don't exist yet; today's counts are the pre-existing absolute/historical style.

## 5. Real third-party art fixtures and redistribution status

`TestProject/Assets/ThirdPartyAssets/` currently holds 7 kits, all read directly from their bundled license files this session (not assumed from prior memory, which referenced a now-nonexistent top-level `third_party_assets/` folder — that path no longer exists; the folder was relocated under `TestProject/Assets/` at some point and its license terms are more permissive than that stale memory recorded):

| Kit | License | Publisher |
|---|---|---|
| Universal Base Characters (Standard) | CC0 1.0 Universal (public domain) | Quaternius |
| Modular Character Outfits - Fantasy (Standard) | CC0 1.0 Universal | Quaternius |
| Bestiary - Dungeon Monsters Kit (Standard) | CC0 1.0 Universal | Quaternius |
| Fantasy Props MegaKit (Standard) | CC0 1.0 Universal | Quaternius |
| Medieval Village MegaKit (Standard) | CC0 1.0 Universal | Quaternius |
| Stylized Nature MegaKit (Standard) | CC0 1.0 Universal | Quaternius |
| Universal Animation Library (Standard) | CC0 1.0 Universal | Quaternius |

All seven are CC0 — no attribution or redistribution restriction. **Correction to `[[project-crpg-third-party-assets]]` memory**: that memory's "never ship with the package" rule was written against a different, differently-licensed folder that no longer exists in the repo; it does not describe these kits' actual terms. That said, CC0 licensing alone doesn't settle a shipping decision (Asset Store review, whether a *modified/rigged* derivative changes anything, whether the user wants third-party-branded content in a paid package) — treat "these seven kits are technically redistributable" as a fact, not as authorization to ship them; that remains Phase 5's call.

**Gate A fixture selection, confirmed with the user this session:**
- Source 1 (already wired): `Universal Base Characters[Standard]/.../Superhero_Male_FullBody.fbx`, imported Humanoid via `TestProject/Assets/Editor/BaseHumanMaleCharacterAuthor.cs`, feeds `BaseHumanMaleCharacter.prefab` and `GilroyMasters.asset` (an `NpcDefinition`).
- Source 2: `Superhero_Female_FullBody.fbx` from the same Universal Base Characters pack. User's explicit choice over the Bestiary Puglin/Imp alternative I raised — same pack/skeleton family as source 1, so it exercises less cross-rig generalization than a truly different skeleton would, but was picked deliberately for lower import risk. Not yet imported/wired as a character fixture.
- Reserved for later: `Bestiary - Dungeon Monsters Kit`'s Puglin/Imp remain the natural candidate for the design's separately-required "Generic creature" fixture (§8 Gate A: "A Generic creature is an additional acceptance fixture before claiming generic-rig support") — distinct from the two-Humanoid-source requirement just decided above.

**User's rig dissatisfaction, confirmed this session, covers:** locomotion/turning, animation blending, and equipment/clothing fit. Explicitly **not** flagged: skinning/import quality itself. This scopes Phase 2 (2.1–2.4) as the direct target; skin-binding correctness (2.4's rig-family validation) still matters for the equipment-fit complaint but isn't itself the reported problem.

## 6. Design defaults — confirmed with the user this session

All four of the design doc's proposal defaults (§9) are approved as-is, no deviations:
- Playables remains the animation backend.
- Motor-driven, in-place locomotion (no root motion) for the initial slice.
- Turn-based tactical scope (no RTwP).
- Safe-point-only saves (rejected during combat/action/dialogue/scene transition, with a reason).

## 7. Not done in this task

No runtime code was changed. No regression tests were added yet (that's Task 0.2). No files were committed, reset, or cleaned.

## Next proposed task (not started)

Task 0.2 — lock regressions before migration: add tests to `SaveServiceTests.cs`/`AbilityResolutionServiceTests.cs` and a new `ProductionContractTests.cs` exposing the seven missing `CharacterState` save fields and the Store/Loot capture gap found above, so Phase 1+ migration work has a red baseline to work against rather than silently changing behavior.

# Toolkit promotion, batch 1 (items 1-7)

Status: DESIGN (2026-10-05). Source: the cross-project review (Barrow's End, ConsumerGame, Viking Raid). Goal: move seven mechanisms that were
worked around or duplicated in the games into `crpg-toolkit/`, then switch the games over so the duplicates are deleted and the games' own
suites prove the toolkit versions. Everything is ADDITIVE and opt-in: no existing call site, test or authored asset changes behaviour.

Process: tests first in the toolkit (`TestProject` runs them), then migrate Viking Raid, Barrow's End and ConsumerGame, then run every suite.

## 1. `UtilityAIDecisionProvider`: optional score floor

Problem (verified, `Runtime/AI/UtilityAIDecisionProvider.cs:75-83`): `bestScore` starts at `-Infinity` and a candidate wins on `score > bestScore`, so a
decision scoring 0 (a consideration vetoed it, e.g. "target already down") is still RETURNED when it is the only legal one. Viking Raid wraps the provider
in `VetoZeroScoreDecisionProvider` (`decision.Score <= 0 -> null`); the Task 2 balance sim found it (a Troll runs out of movement, then hits the downed
hero beside it).

Design: new optional constructor parameter `float scoreFloor = float.NegativeInfinity` (appended after `combatLog`): a candidate (UseAbility or Move) must
score STRICTLY ABOVE the floor to be considered; it is still added to the trace. Default keeps today's behaviour because authored behaviours with an empty
`considerations` list score 0 and currently act (Barrow's End's `HostileBrainKit` comment documents exactly this). Flipping the default is a separate,
breaking decision. Viking Raid passes `scoreFloor: 0f`, deletes the wrapper.

Tests (EditMode, `UtilityAIDecisionProviderTests` exists): a lone zero-scored legal attack returns null with floor 0, returns the decision with the default;
a positive candidate still beats a zero one either way; a Move at zero is also vetoed; trace still lists the vetoed candidate.

## 2. Heal revives the dying (SRD5)

Problem: `HealEffect.Apply` only `hp.Restore(n)` and publishes nothing. A hero healed from 0 HP stays `DeathSaves != null`/Unconscious until the game
clears it (Barrow's End `GateSessionServices.HealAndStand`; Viking Raid `PartyVitals.Revive` + a per-frame sweep). `DeathSaveService.Stabilize` is the existing
"end dying" operation but forces HP to 1 and is only reachable by a nat-20 or a direct call.

Design: (a) append `GameEvent.AfterHeal` at the END of the enum (nothing casts the enum; appended = no value shifts). `HealEffect` publishes
`AfterHeal(source, target)` with `Data["Amount"]` (HP actually restored) after restoring, via `context.EventBus?.Publish` like `DamageEffect`. (b) SRD5
`DeathSaveService` subscribes to `AfterHeal`: if the target has HP > 0 AND (`DeathSaves != null` OR carries the configured unconscious condition), it clears
`DeathSaves`, removes the Unconscious condition and publishes `CreatureStabilized` (the vocabulary `SpriteCueEventBinding`/`ModelCueEventBinding` already
treat as "recovered when HP > 0"). It does NOT set HP (the heal already did). The "stable at 0 HP, unconscious, `DeathSaves == null`" state is covered by the
condition check. A heal that leaves HP at 0 changes nothing. Engine stays ruleset-agnostic: only the event is generic.

Migration: Viking Raid deletes `PartyVitals.NeedsRevive/Revive` and the `ReviveSweep` revive call (keeps the avoidance-priority half and
`FullHealAndRevive`, the between-fights heal); Barrow's End `HealAndStand` keeps working (its own full-heal debug path) unchanged.

Tests: `HealEffectTests` publishes AfterHeal with the amount; `DeathSaveServiceTests`: heal from dying revives (DeathSaves null, Unconscious gone,
CreatureStabilized published once, HP = healed amount not 1); heal while stable-at-0 revives; heal that restores 0 does not; heal of a healthy character
publishes nothing extra; a non-death-save faction (`usesDeathSaves` false) unaffected. Viking Raid's existing `GameRulesTests` revive tests move to the toolkit.

## 3. Party move facing (opt-in derive-from-travel)

Problem (verified): `PlayerCommandRouter.ResolveClick` always sends `MoveCommand(hit.point, Vector3.forward)` ("whatever consumes `MoveCommand.Facing` derives
real facing itself") and `PartyExplorationController.HandleMove` passes it straight to `PartyMoveOrder` -> `IFormationSolver`: nothing derives it, so a
`WedgeFormationSolver` party always points at world +Z. Viking Raid rewrites it in its command source.

Design: `PartyExplorationController` gets an optional constructor parameter `ISelectableSource facingSource = null` (after `canMove`). When supplied,
`HandleMove` replaces the command's facing with the flat direction from the centre of the ORDERED members' current positions (read from `facingSource`,
the same source `PartyMovementService` already reads) to the destination, unless that vector is shorter than 0.1 m (then the command's own facing stands).
New read-only `Vector3 LastOrderFacing` (the facing actually used for the last order; initial `Vector3.zero`) so a game can turn stopped members to it.
Default null = unchanged (Barrow's End's 2D look is user-validated; every existing test passes `Vector3.forward` explicitly). Viking Raid passes
`new SceneSelectableSource()`; its idle-facing code reads `exploration.LastOrderFacing` instead of the command source's copy.

Tests: with a source, a party sent west gets a wedge whose apex points west (slots computed from the derived facing); a zero-length order keeps the command facing;
LastOrderFacing is set; without the source behaviour is byte-identical (existing tests).

## 4. `CombatCueEventBinding`

`SpriteCueEventBinding` (toolkit) and `ModelCueEventBinding` (Viking Raid) are the same rules (AfterDamage -> hit; dropped-to-zero -> down; died -> died;
stabilized/condition-removed -> recovered only when HP > 0; exception isolation; disposed flag; `IsDownAtBind`). Design: new `CRPG.Presentation.CombatCueEventBinding`
(callbacks `onHit/onDown/onDied/onRecovered` + optional `Action<GameEventContext> onContext` fired for hit/down/died before the callback, as Viking Raid
has it), `IsDownAtBind` static. `SpriteCueEventBinding` keeps its public constructor, `Dispose` and static `IsDownAtBind` and delegates to the new class (so
all its existing tests, which use the real damage/death-save machinery, are the regression suite). Viking Raid deletes `ModelCueEventBinding` and uses
the toolkit one in `ModelActionCues`; its `ModelCueTests` binding cases move into toolkit tests (`CombatCueEventBindingTests`) where not already covered.

## 5. Non-lethal defeat seam

Problem: `TacticalEncounterController.OnCreatureDied` is private and does `MarkDefeated` + `Encounter.RemoveCombatant`. A ruleset without death (Fate's
"Taken Out") must do the same without publishing CreatureDied; `ConsumerGame/FateConflictService` reimplements it from the spawn service and encounter service.
Design: public `void Defeat(RuntimeEntityId id)` on the controller doing exactly those two calls (idempotent; no-op after Dispose); `OnCreatureDied` calls it.
ConsumerGame: `FateConflictService` takes the controller and calls `Defeat`. Tests: Defeat removes the combatant and marks the character defeated without a
CreatureDied event; calling it twice is safe; after Dispose it is a no-op; `CreatureDied` still routes through it.

## 6. `PerCombatantDecisionProvider`

Byte-for-byte the same class in Barrow's End and Viking Raid. Move to `CRPG.AI` (`Runtime/AI`), namespace `CRPG.AI`. Tests: routes each combatant to its own set's provider;
null set -> null decision; builds each distinct provider once; null args throw. Both games delete their copy.

## 7. `PartyCommandFilter` and `TacticalPlayerMoveAdapter` options

`GateCommandSource` (Barrow's End) and `VikingCommandSource` (Viking Raid) are the same wrapper around an `IPlayerCommandSource`.
Design: `CRPG.Gameplay.PartyCommandFilter : IPlayerCommandSource, IDisposable` with constructor `(IPlayerCommandSource inner, Func<PartyState> currentParty)`:
Select filtered to current party members, mode preserved (empty -> silent); Move/Interact/Cancel pass through; `Action<MoveCommand> CombatMoveOverride`
(while set: Move goes there, Interact dropped); `bool MovesLocked` (Move and Interact swallowed). Facing rewriting moves to item 3 (no `partyCentre` here).
Barrow's End and Viking Raid delete their copies (`GateCommandSource`/`VikingCommandSource`) and use the filter; `GateCommandSourceTests` become
`PartyCommandFilterTests` in the toolkit.

`TacticalPlayerMoveAdapter` new optional constructor parameters (after `playerFaction`), all defaulting to today's behaviour:
- `Func<RuntimeEntityId, bool> canMove` - false for the current-turn character refuses silently (Viking Raid: downed hero).
- `Func<RuntimeEntityId, Vector3, (Vector3 Destination, string Refusal)> adjustDestination` - may change the destination or refuse (non-null Refusal;
  the adapter then raises new `event Action<string> MoveRefused` and returns null).
- `bool passReactors = true` - false passes no potential reactors to `StartMove` (Viking Raid: no stop-start opportunity-attack stutter).
New `CRPG.Gameplay.Combat.NavMeshDestinationAdjuster.Create(Func<IEnumerable<RuntimeEntityId>> others)` returning such a delegate: samples the NavMesh within 1.5 m
(refusal "You cannot go there."), and pushes the destination out of any other combatant's `NavMeshAgent.radius + own radius + 0.2` footprint (Viking Raid's
`TryHandleMove` logic, verbatim). Viking Raid's `VikingCombatSession.TryHandleMove` becomes a thin call to the adapter (the public method stays for its tests);
Barrow's End keeps the plain adapter.

Tests: PartyCommandFilter (ported GateCommandSource cases + MovesLocked + Interact dropped under override + dispose unsubscribes); adapter: canMove false -> null
and no move; adjust changes the destination handed to the scheduler; refusal raises MoveRefused and starts nothing; passReactors false passes none; defaults unchanged
(existing tests); the NavMesh adjuster: snap, push-out, unreachable refusal (PlayMode with a small baked mesh like the existing tactical tests).

## Migration and verification order

1. Toolkit: failing tests -> implementation (items 1-7) -> `TestProject` EditMode + PlayMode green (baseline 666 / 208 + the new ones).
2. Viking Raid: swap in the toolkit pieces, delete the five duplicates (`VetoZeroScoreDecisionProvider`, `PerCombatantDecisionProvider`, `ModelCueEventBinding`,
   `VikingCommandSource`, revive half of `PartyVitals`), adapt tests, full EditMode (88) + PlayMode (62).
3. Barrow's End: `PerCombatantDecisionProvider`, `GateCommandSource` -> `PartyCommandFilter`; its EditMode + PlayMode suites (throwaway copy if the Editor is open).
4. ConsumerGame: `FateConflictService` -> `controller.Defeat`; its own tests (not the toolkit tests it also auto-discovers).
5. CHANGELOG entry; plan-doc results.

Not in this batch (decided): flipping `scoreFloor` or facing derivation to default-on (behaviour change for Barrow's End / authored content); the combat session
extraction (item 9 of the review) - this batch deliberately makes its inputs injectable first.

## Review resolutions (Opus hostile review, 2026-10-05) -- these SUPERSEDE the text above where they differ

1. **Item 5 (blocker).** ConsumerGame never builds a `TacticalEncounterController` (`ConsumerGameBootstrap.cs:93-99`, combat runs on a bare `CombatEncounterService`). So: keep
   the public instance `Defeat(id)`, AND add `public static void Defeat(WorldSpawnService, CombatEncounterService, RuntimeEntityId)` on the controller that both the instance
   method and `OnCreatureDied` call; `FateConflictService` calls the static (no bootstrap rewrite). The stale-spawner bug (`FateConflictService` keeps the constructor's spawner,
   `OnLoadRequested` replaces it, `ConsumerGameBootstrap.cs:278`) is fixed in the same edit by giving the service a settable spawner/encounter the bootstrap refreshes.
2. **Item 2 revive condition.** Revive ONLY when `DeathSaves != null`, or the Unconscious condition is present AND HP before the heal was 0 (`hp.Current - Data["Amount"] == 0`), AND HP is now > 0,
   AND the target `usesDeathSaves`. Remove the condition with `RemoveConditions(predicate)` (all instances). Publish `AfterHeal` after `CombatLog.Log`.
   **Opt-in:** new `DeathSaveService` constructor flag `bool reviveOnHeal = false` (the header's "additive and opt-in" stands); Viking Raid passes true. Recommended default-on later.
3. **Item 3.** The centre is the ORDERED members' (selected and `canMove`), a deliberate behaviour change for Viking Raid (it averaged all heroes). `LastOrderFacing` updates ONLY when a direction was derived
   (a sub-0.1 m click keeps the previous value); initial `zero` (fixes checkpoint spawns idle-turning to +X). `VikingGame` exposes `exploration.LastOrderFacing` publicly (GameFacingTests reads it).
4. **Item 4.** Preserve exactly: callback null => return before `onContext`; `onContext` and the callback share one try (a throwing `onContext` skips the callback); `onContext` not fired for recovered;
   `SpriteCueEventBinding` keeps `ArgumentNullException(nameof(state))` and the inner object subscribes in the same order (the dispose-inside-handler test depends on it). New `CombatCueEventBindingTests` cover `onContext`.
5. **Item 7.** The reactor flag is named `provokesReactions = true` and documented as "false disables opportunity attacks against player movement entirely" (crossing stops are the only place they fire,
   `MoveActionExecution.cs:167-177,295-297`); Viking Raid already passes null. Order in `TryHandleMove`: agent/motor check, then `canMove`, then `adjustDestination`. Filter precedence: `MovesLocked` before
   `CombatMoveOverride`. Tests for adjust/provokes use a recording `IMovable` capturing the `SetDestination` argument. `VikingCombatSession.TryHandleMove` null-checks the adapter outside an encounter.
6. **Item 1.** Combat-log entry for a fully vetoed turn now has no decision (score 0): documented. Default unchanged.
7. **Migration step 2 additions (Viking Raid):** `Arena.cs:130` uses `scoreFloor: 0f`; `Arena.cs` revive calls deleted (it already wires `DeathSaveService`; pass `reviveOnHeal: true`) and the balance sim re-run;
   `GameRulesTests.cs:54-81` rewritten as toolkit-backed heal/revive tests (`FullHealAndRevive` tests stay); `PartyVitals` doc comments updated; delete the games' duplicate `.meta` files too.
8. **Baseline.** Re-run `TestProject` first for the real counts (the 666/208 figure is stale). Only one Unity process imports the package at a time (all four projects consume it live).
9. **Docs.** CHANGELOG entry; update `GateSessionServices` "engine gaps" comment and the Gate D note when done. Item 6 wording: "functionally identical", not byte-for-byte.


## Results (2026-10-05) -- all seven items done, verified, migrated

**Toolkit (TestProject).** EditMode 1060 -> **1105** (+45), PlayMode 435 -> **452** passed (+17; the 3 clean-install-harness fixtures still skip by design). New files: `CombatCueEventBinding`,
`PerCombatantDecisionProvider`, `PartyCommandFilter`, `NavMeshDestinationAdjuster` (+ their tests, `.meta` files generated by the first import); changed: `UtilityAIDecisionProvider`, `GameEvent`
(AfterHeal appended), `HealEffect`, `DeathSaveService`, `PartyExplorationController`, `SpriteCueEventBinding` (now a wrapper), `TacticalEncounterController`, `TacticalPlayerMoveAdapter`; CHANGELOG
`[Unreleased]` entry.

**Mutation checks (18 mutations, each fails the matching tests, all restored).** The first round found three weak spots, all fixed: (1) M3/M4 (revive guards) were masked because mutating the
whole revive away made the "must NOT revive" tests pass trivially: they need separate runs; (2) the "centre is the ordered members only" test used +-500 m positions that cancel in an average, so it
could not tell: positions made asymmetric; (3) nothing tested `SpriteCueEventBinding`'s null-state guard: test added. Then every mutation fails its tests: score floor ignored, AfterHeal not published,
healthy sleeper woken, revive ignoring `usesDeathSaves`, `onContext` never fired, recovered at 0 HP, `MovesLocked` ignored, short order deriving anyway, provider rebuilt every call, centre over all members,
null state accepted, `Defeat` ignoring dispose / not marking defeated, adapter ignoring `canMove` / the adjusted destination / a refusal, adjuster never pushing out / never refusing off-mesh.

**One real bug found by running it, not by review.** `NavMeshDestinationAdjuster` first read the agent radius with `GetComponent<NavMeshAgent>() is { } nav`. In the Editor a missing component comes back as a
"fake null" that passes a C# null pattern and throws `MissingComponentException` on first use; Viking Raid never hit it because its agents always have a NavMeshAgent. Now `TryGetComponent`.

**Games migrated, suites green.**
- *Viking Raid* (5 duplicates deleted: `VetoZeroScoreDecisionProvider`, `PerCombatantDecisionProvider`, `ModelCueEventBinding`, `VikingCommandSource`, the revive half of `PartyVitals`): EditMode 87 (88 minus the two
  flag-only revive tests and the model-binding class that the toolkit now covers, plus one new real Rallying-Verse-revives-a-downed-ally test through the real `DeathSaveService`), balance sim identical
  (E1-E4 win 100 / 100 / 93.3 / 66.5 %); PlayMode 56/56 including the scripted full run. `VikingGame` now builds a `PartyCommandFilter`, passes `facingSource`, exposes `LastOrderFacing`;
  `VikingCombatSession.TryHandleMove` is a thin call to the toolkit adapter (`canMove`, `NavMeshDestinationAdjuster`, `provokesReactions: false`).
- *Barrow's End* (`GateCommandSource` and its copy of `PerCombatantDecisionProvider` deleted; `GateCommandSourceTests` ported to the toolkit as `PartyCommandFilterTests`): EditMode 145/146 (the one failure is
  the known pre-existing stale Town_Slice bake), PlayMode 120/121 (the one failure is `PartyPanelDiagnosticTests`, a diagnostic that yields `WaitForEndOfFrame`, which batch mode does not run).
- *ConsumerGame*: `FateConflictService` uses the static `TacticalEncounterController.Defeat` and is re-pointed at the new spawn service after a load (the review's stale-spawner bug). EditMode 9/9,
  PlayMode 2/2 including the full-engagement test. **Found pre-existing drift:** its Editor test assembly had not compiled since 2026-09-19, when the 2D work gave `WorldSpawnService` a
  `GridPathfindingService` property (`CRPG.Navigation` was never referenced); one-line asmdef fix.

**Deviations from the design.** (1) ConsumerGame has no controller, so the migration uses the static `Defeat` (review blocker). (2) Item 2 is opt-in (`reviveOnHeal`) rather than unconditional (review).
(3) `PartyCommandFilter` no longer rewrites the facing (moved to `PartyExplorationController`, item 3). (4) The `provokesReactions` test can only prove "still starts and sends the move": the scheduler gives no way
to observe which reactors it was handed without a live opportunity-attack setup.

**Gotchas recorded.** Barrow's End's EditMode and PlayMode suites re-run its content authors, which regenerate random `rid` ids and reorder lists: a run leaves ~30 content assets, three scenes, `BarrowsEnd.slnx` and a
spike PNG modified or new. They are churn, not changes: `git checkout` them (and delete the strays) after every BE run. Two pre-existing dice-dependent test flakes in Viking Raid (a hero's hit kills the goblin whose
position the test then reads: `GameCaptureTests`, router-click test) were fixed by capturing the position before the action.

**Still open from the review.** Defaults not flipped (`scoreFloor`, `reviveOnHeal`, `facingSource`) pending a decision and a visual check of Barrow's End; items 8-11 (party trigger volume, `TacticalCombatSession`
extraction, retarget fixes for borrowed 3D clips, test kit).

# Gate D results: `ConsumerGame/`

Status: **all four Gate D requirements met** -- a fresh consuming project imports the package; different art (Unity primitives only) and a differently-styled UI skin; an alternate non-d20 ruleset (Fate Condensed); no package-source edits for the ruleset itself, no TestProject script dependency, no copied monolithic bootstrap. Two small, generic, reviewed package corrections were required and are documented below as the frozen baseline this consumer was built against -- everything else in `crpg-toolkit/` is untouched.

## Frozen package baseline

Not yet committed as of this writing (the user commits locally, not this session) -- the exact file set is:

- NEW `crpg-toolkit/Runtime/Gameplay/RulesetSessionFactory.cs`
- Modified `crpg-toolkit/Runtime/Gameplay/GameSessionHost.cs` (delegates to the factory above; no behavior change)
- Modified `crpg-toolkit/Runtime/Gameplay/SessionLoadCoordinator.cs` (new ruleset-aware `Load`/`LoadCoroutine` overloads; old `IRulesDatabase` overloads untouched)
- Modified `crpg-toolkit/Runtime/Gameplay/SessionLoadRequestAdapter.cs` (new `ForRuleset` static factory; old constructor untouched)
- Modified `crpg-toolkit/Runtime/Saving/LoadOutcome.cs` (+`RulesetMismatch`, +`RulesetValidationFailed`)
- Modified `crpg-toolkit/Runtime/Combat/CombatEncounterService.cs` (`initiativeRule` now optional; +`StartCombatWithExplicitOrder`)
- Modified `crpg-toolkit/Runtime/Gameplay/Combat/TacticalEncounterController.cs` (+`useExplicitTurnOrder` constructor parameter)
- New/extended tests: `Tests/Runtime/SessionLoadCoordinatorTests.cs` (+2), `Tests/Editor/CombatEncounterServiceTests.cs` (+2), `Tests/Runtime/TacticalEncounterControllerTests.cs` (+1)

**Why these two, and only these two:** a real consumer relying on `IRulesetDefinition`/`PolicyRegistry` end-to-end (through a save/load cycle, and through starting combat with a ruleset that has no rolled initiative) is the first thing in this whole initiative to actually exercise both seams for real -- every existing demo either used the simpler non-ruleset `StartSession` overload or hand-constructed its policies directly. Both gaps are generic (not Fate-specific; SRD5 or any future ruleset benefits identically) and fully backward compatible (every existing call site, test, and demo bootstrap is untouched). Full rationale, exact line citations, and the rejected alternative (extending `IAttackBonusRule` to fix a D&D-specific crit/fumble concept instead of giving Fate its own effect) are in the approved implementation plan.

**Package suite after these two fixes**: 666 EditMode (+2 from Task 5.1's own baseline of 664) / 208 PlayMode (+3), all green, run from `TestProject/`.

## Where everything else lives

**Nothing else changed in `crpg-toolkit/`.** The alternate ruleset, all game-specific glue, art, and UI are entirely inside `ConsumerGame/` -- a sibling Unity project to `TestProject/`, never referencing it.

```
ConsumerGame/
  Packages/manifest.json          -- minimal: com.crpgtoolkit.core (file: reference), AI Navigation,
                                      Input System, URP, Test Framework. No TestProject dependency --
                                      physically cannot have one; it's a separate Unity project.
  Assets/
    Rules/Fate/                   -- the alternate ruleset (CRPG.Consumer.Fate.asmdef, references only
                                      CRPG.Core/Rules/Actors/Combat/Abilities -- never CRPG.Rules.SRD5)
      FateCheckRule.cs            -- ISkillCheckRule: 4dF via DiceExpression(4,3,-8)
      FateAttackEffect.cs         -- its own opposed-roll AbilityEffect (not the package's AttackRollEffect)
      FateRulesetProvider.cs      -- real ScriptableObject : IRulesetDefinition (not runtime reflection)
      Editor/FateContentAuthor.cs -- one-time authoring script -> Rules/Fate/Content/*.asset
    Game/                         -- consumer-owned composition glue (CRPG.Consumer.Game.asmdef)
      ConsumerGameBootstrap.cs    -- construction/lifetime ONLY
      FateConflictService.cs     -- Taken-Out translation (never GameEvent.CreatureDied)
      ConsumerSaveCoordinator.cs -- SafePointGate/SaveRequestService/ruleset-aware load, isolated
      ProximityTriggerZone.cs    -- compact substitute for the package's full interaction lifecycle
      ConsumerHud.cs              -- the distinct UI skin (procedural UI Toolkit, no .uxml/.uss)
    Editor/                        -- one-time authoring scripts (character prefabs, scene, dialogue)
    Tests/Editor, Tests/Runtime    -- this project's own tests, see below
```

### Script responsibility classification (per Gate D's own requirement to record this)

| Script | Classification |
|---|---|
| `FateCheckRule`, `FateAttackEffect`, `FateRulesetProvider` | Required game-specific ruleset policy -- the actual thing Gate D is proving can be supplied by a consumer |
| `ConsumerGameBootstrap`, `FateConflictService`, `ConsumerSaveCoordinator` | Content/presentation/session binding -- orchestration a real game always needs, using only public package API |
| `ProximityTriggerZone` | Content/presentation binding -- a deliberately smaller substitute for Task 3.3's full interaction lifecycle (click-to-interact, `WorldInteractable`), which this compact game's scope doesn't need. |
| `ConsumerHud` | Pure presentation -- the distinct UI skin. |

**Self-correction, recorded honestly**: this section originally listed a from-scratch `SimpleClickCommandSource` here, justified as "no efficient headless path to generate the Input Actions wrapper." That was wrong -- `crpg-toolkit/Editor/InputSystem/CRPGInputActionsAuthor.cs` ("CRPG Toolkit/Input/Author Default Input Actions", a genuinely package-shipped, consumer-facing tool) had already generated `CRPGInputActions.inputactions` + its wrapper class into the package, and `InputSystemCommandSource` was fully usable in `ConsumerGame` without any extra step -- I simply hadn't checked for it before building a workaround. Caught when the user asked whether the package's own "CRPG Toolkit" Editor menu was meant for consumers at all, which prompted auditing what actually ships (`crpg-toolkit/Editor/`) versus what's TestProject-only dogfood (`TestProject/Assets/Editor/`) -- exactly three tools ship (Character Setup, Refresh SRD5 Ruleset Provider, Author Default Input Actions), and all three are genuinely consumer-facing by design. `ConsumerGameBootstrap`/`ConsumerSceneAuthor.cs` now use the real `InputSystemCommandSource` (`CRPG.InputSystem` namespace); `SimpleClickCommandSource.cs` was deleted rather than left as dead code.

## Fate Condensed mechanics -> what was actually built

Verified against the real Fate Condensed SRD text (fate-srd.com), not recalled from memory.

- **4dF**: `DiceExpression(4, 3, -8)` through the existing, unmodified dice pipeline -- verified exact (not approximate) arithmetic; see `FateCheckRule.cs`'s own doc comment.
- **Opposed attack/defend**: `FateAttackEffect` rolls both sides directly and computes real shifts (`attackTotal - defenseTotal`), spent against the target's Stress. Deliberately does **not** go through the package's `AttackRollEffect` (which hardcodes a natural-1 fumble check that's meaningless for a 4-die pool) -- see the frozen-baseline section above for why that wasn't fixed generically instead.
- **Stress/Taken Out**: a single unified Stress resource (not Fate's real separate Physical/Mental tracks), no Consequences at all. Taken Out never publishes `GameEvent.CreatureDied` -- real Fate Condensed explicitly allows non-lethal outcomes ("the attacker decides... kidnapping, imperiling, or forced consequences").
- **Fate Points**: a real, distinct resource from Action (1/turn) -- "Strike, Invoking a Stunt" costs both Action and 1 Fate Point and adds +2 to the attack total, proven via `AbilityCostPlan`'s existing multi-resource support (zero new engine code).
- **No rolled initiative**: real Fate Condensed's own default is elective/narrated, not rolled -- `CombatEncounterService.StartCombatWithExplicitOrder` (the frozen-baseline fix) is used instead of inventing a fake Fate die roll.
- **Skills**: modeled as a `ResourceState.Max` rating (not `FeatureModifier` tags), specifically to avoid needing `IRulesDatabase` inside `AbilityExecutionContext`/`FateAttackEffect` (which doesn't carry one) -- see Simplifications.
- **Outcome tiers**: the dialogue check collapses Fate's real four tiers (Fail/Tie/Succeed/Succeed-with-Style) to the existing binary success/fail, since neither `DialogueCheckResolver` nor the combat pipeline track a margin anywhere generically.

## Deliberate simplifications (stated, not hidden)

- Skills are flat resource ratings, not `FeatureModifier`-tagged bonuses -- no conditional/stacking skill bonuses possible. Chosen specifically to avoid a third package change (an `IRulesDatabase` reference inside `AbilityExecutionContext`) beyond the two already frozen.
- No Consequences, no separate Physical/Mental stress tracks, no Aspect-compel economy -- a single Stress track and "Taken Out when Stress overflows" only.
- The planned standalone "Stunt `FeatureDefinition`" content item was cut during implementation -- the invoke-bonus is fully expressed as ability data (two ability variants), so a separate asset would have been unused. (Flagged explicitly, per this session's own established practice of naming a cut rather than silently dropping it.)
- No click-to-select UI -- the whole party is selected once via `PartyState.SelectAll()` at boot, since a 2-character party that always moves together doesn't need it for "compact" scope. Movement itself goes through the package's own real `InputSystemCommandSource` (see the correction note below), so `Select`/drag-box selection is fully available if a later game wants it.
- `ProximityTriggerZone` (walk-into-range) substitutes for Task 3.3's full click-to-interact lifecycle -- appropriate for a compact 2-encounter loop, not a claim that the fuller interaction system is unnecessary in general.
- Camera is a static overview, not a follow rig -- the whole compact scene fits in one view.
- Fate Points refresh once at game start only (matching Fate Condensed's session-level Refresh), never mid-game -- there's only one scene in this compact loop, so "refresh at scene end" was never exercised.

## Verification

**ConsumerGame's own tests** (`Assets/Tests/Editor` + `Assets/Tests/Runtime`), run via the standard `-batchmode -runTests` pattern against `ConsumerGame/`:

- EditMode: 675/675 passing (666 are crpg-toolkit's own package tests, auto-discovered via `testables`; 9 are this project's own `FateCheckRuleTests`/`FateAttackEffectTests`/`FateRulesetProviderTests`).
- PlayMode: 210 total, **199 passing, 11 failing** -- both new ConsumerGame tests pass:
  - `ConsumerGameSceneSmokeTests.TheAuthoredSceneBootsWithTwoPartyMembersAndNoConsoleErrors` -- the authored scene boots, session starts via the real `FateRulesetProvider`, 2 party members + hostile + NPC are all real registered characters, zero console errors.
  - `ConsumerGameFullEngagementTests.WalkingToTheHostileStartsCombatWhichResolvesToARealWinThenSavesAndLoads` -- **the actual end-to-end proof**: a party member walks via a real `NavMeshAgent`/`IMovable` path into the hostile's `ProximityTriggerZone`, real combat starts via `StartCombatWithExplicitOrder`, real Fate Strike abilities resolve via the real `AbilityResolutionService.Execute` until the hostile's Stress is overwhelmed and it's Taken Out, the win triggers a real autosave (verified by reading the file back and checking `Header.RulesetId == "fate-condensed"`), and an independent load restores the session with the Fate check rule still registered. Uses `ConsumerGameBootstrap.RequestStrike`/`RequestLoad` (public, added for testability, mirroring `TacticalPlayerMoveAdapter.TryHandleMove`'s own precedent) instead of simulating real mouse clicks -- movement itself still goes through the real NavMeshAgent path, not a teleport.
  - **The 11 failures are crpg-toolkit's own pre-existing PlayMode tests** (`SceneTravelTests`, `CharacterWrapperTests`, `GateAFixtureTests`, `CrossSceneLoadTests`, `UIToolkitPointerCaptureTests`), not anything ConsumerGame or Task 5.2 introduced -- they depend on TestProject-specific fixture scenes/prefabs (e.g. `Assets/Scenes/Tests/SceneTravelTestA.unity`, `Resources/NPCs/BaseHumanMaleCharacter.prefab`) that simply don't exist in a fresh consumer project. **This is a real, newly-surfaced finding worth recording**: crpg-toolkit's own package test suite is not fully environment-independent -- it implicitly assumes TestProject-side fixtures. Confirmed this is not a Task 5.2 regression by re-running the same suite from `TestProject/` (666/208, all green). Fixing this is a separate, package-test-infrastructure task, out of scope here.

**Standalone build**: `StandaloneWindows64`, Development build, direct `BuildPipeline.BuildPlayer` call via `-executeMethod` (`Assets/Editor/ConsumerBuildScript.cs` -- ConsumerGame deliberately has no `com.unity.pipeline`, so the `eval_file` mechanism used for crpg-toolkit's own Gate C perf capture wasn't available here). **Succeeded: 0 errors, 9 warnings, ~169MB, ~3h12m** (almost entirely Unity's first-time URP shader-variant-stripping pass across the whole render pipeline on this machine -- a one-time cost, not something ConsumerGame's own small content drives). Launched the built `.exe` as a real, independent OS process (`Start-Process`, not the Editor) and let it run 15 real seconds: D3D12/PhysX/Input all initialized cleanly, **zero errors or exceptions** in its own log. This confirms the standalone player boots and runs cleanly outside the Editor. **Honest scope limit**: this did not drive an actual mouse-click save/load round trip through the running `.exe` (no practical way to script real OS-level mouse input against a live process in this environment) -- the save/load round trip itself is proven by `ConsumerGameFullEngagementTests` (a real Editor PlayMode session, not the standalone player) plus this standalone boot check, rather than by combining both into one artifact. If a stronger standalone-specific proof is wanted later, it needs either manual play or a UI-automation tool this session doesn't have.

## What's still open (deliberate scope lines, not blockers)

- Fate's real margin-of-success/Consequences/separate stress tracks/Aspect-compel economy -- all named above.
- `crpg-toolkit`'s own PlayMode suite depending on TestProject-specific fixtures for a handful of tests -- a real finding, not something this task fixes.
- No controller/touch input adapter (mouse only, matching the initial supported envelope's own stated scope).

# Task 5.3 results: `TacticalEncounter` sample

## What was built

- `crpg-toolkit/Samples~/TacticalEncounter/`: a real 2-Hero-vs-2-Foe combat encounter -- turn order, real
  rolled initiative, real attack rolls/damage, real reactions (opportunity attacks), real AI decision-making,
  a real win condition. The one major engine subsystem (Combat + AI) neither BasicCRPG nor CharacterCreator
  touches.
  - `Scripts/TacticalEncounterBootstrap.cs`: composition root. Starts a real ruleset-aware session against the
    package's own shipped `SRD5RulesetProvider.asset`, registers the sample's own real authored content on top
    (`((RulesDatabase)session.RulesDatabase).RegisterRange(...)` -- `IRulesDatabase` is deliberately read-only,
    so this casts to the concrete type the ruleset-aware path builds internally; documented in-code), spawns 2
    Heroes (player-controlled -- real click-to-target-and-attack via `AbilityTargetingGate`, real click-to-move
    via `TacticalPlayerMoveAdapter`) and 2 Foes (AI-controlled via a real `UtilityAIDecisionProvider` driving
    the sample's own authored `BehaviorSetDefinition`), and starts combat through the real rolled-initiative
    path. `AbilityResolutionService` is wired from the session's own real resolved policies
    (`session.Policies.Get<IAttackBonusRule>()` etc.), not hand-constructed fresh ones.
  - `Scripts/CombatLogPanel.cs`: a small procedural combat-log/HP UIDocument panel + `SampleCombatLog :
    ICombatLog` -- "logs to the console" isn't enough for a live human review to confirm a click did something.
  - `Editor/TacticalEncounterContentAuthor.cs`: authors the sample's own real content -- a melee-weapon-attack
    `AbilityDefinition` (SRD5e2014 ships none; `AttackRollEffect` is otherwise only ever used by 4 ranged
    spells), Opportunity Attack + Reactive Strikes, a sample-owned Movement `ResourceDefinition` (SRD5e2014
    ships none either), and the "Foe Warrior" `BehaviorDefinition`/`BehaviorSetDefinition` AI content.
  - `Editor/TacticalEncounterPrefabAuthor.cs` / `Editor/TacticalEncounterSceneAuthor.cs`: same
    disposable-authoring-script convention as the other two samples.
- `crpg-toolkit/Tests/Fixtures/TacticalEncounterVerification/`: four real behavioral acceptance tests (see
  below).
- Two small fixes to shared/engine code, found while building this (both real, both verified, neither
  hypothetical):
  - `crpg-toolkit/Runtime/Combat/CombatEncounterService.cs`: `EndCombat()` now nulls `Encounter` *before*
    publishing `CombatEnded` (was the other order). `GameEventBus.Publish` is synchronous, so a `CombatEnded`
    subscriber (e.g. `TacticalCombatUIBridge.RefreshCurrentCharacter`) previously read a stale non-null
    `Encounter`/`CurrentTurn` mid-publish -- could leave the ability bar showing the last active character as
    "still their turn" after combat truly ended, with nothing afterward to correct it. Full package
    EditMode+PlayMode suite re-confirmed green after the change.
  - `crpg-toolkit/Docs/Verification/Scripts/clean-install-harness.sh`: `check_results` now also asserts
    `passed != "0"`, not just `failed="0"` and `total != "0"` -- a wholly `Ignore`d/skipped test previously
    reported exactly that and would have read as a pass.

## A real defect found and fixed before any of the above was built (the plan's own "first implementation gate")

The obvious, naive way to author the Foes' AI -- empty `Considerations` lists on both Behaviors, matching the
existing `TacticalSliceBootstrap` dogfood precedent -- is a real, confirmed bug, not a style preference:
`UtilityMath.CombineScores([])` returns exactly `0f`, and `UtilityAIDecisionProvider.GetDecision` only replaces
its current best on a *strictly greater* score. With every Behavior scoring a flat `0`, ties are broken purely
by which Behavior is listed first in `BehaviorSetDefinition.BehaviorIds` -- not by whether an attack is
actually legal. Combined with `MoveActionExecution`'s real "already within stopDistance" fast path (succeeds
instantly, spends no movement) and `TacticalAiTurnDriver`'s `MaxDecisionsPerTurn = 8` cap, a flat-scored AI can
legitimately spend all 8 decisions issuing free no-op "already there" moves and never attack, silently ending
its turn with no exception anywhere to notice.

Found and proven with a real unit test (`TacticalAiScoringTests.AiPrefersAttackOverMovementWhenALegalAttackExists`)
*before* any scene/content/UI was built, exactly matching this task's own plan ("the first implementation
gate... catch the largest risk before scene/UI work obscures it"). Fixed with real, designed Considerations:
"Melee Strike Nearest" floors at 0.85 whenever a target is in range; "Close Distance" ceilings at 0.6 -- so a
legal attack always structurally outscores movement, and closing distance still correctly prefers the nearest
out-of-range enemy when no attack is legal. Three tests in `TacticalAiScoringTests.cs` each prove one of the
three acceptance properties directly against the real `UtilityAIDecisionProvider`.

## A real test-isolation bug found while running the full suite together (not hypothetical)

`TacticalFullEncounterTests`/`TacticalReactionTests` each passed reliably in isolation but failed when run
together with `TacticalEncounterVerificationTests` (the scene smoke test) in the same batch -- confirmed by
running subsets: `TacticalReactionTests` + `TacticalFullEncounterTests` alone: 2/2 pass; adding the scene smoke
test in front: both fail (one times out at 30 simulated seconds, the other never sees its reaction fire).
Root cause: the scene smoke test loads the real `TacticalEncounter.unity` scene (a full 40x40 ground + wall +
baked NavMesh + `TacticalEncounterBootstrap`'s own spawned `CharacterAgent`s) and never unloaded it -- the next
test's own from-scratch `NavMeshSurface` bake and `CharacterAgent.AllLive`-based position lookups got confused
by the still-live scene content underneath. Fixed with a `[UnityTearDown]` on the scene smoke test that creates
a genuinely empty scene and unloads the real one -- confirmed by two consecutive clean 11/11 runs of the full
`TacticalEncounter` filter afterward.

## Deliberate scope decisions (flagged explicitly, not silently dropped)

- **No `DeathSaveService`/`ConcentrationService`, no spellcasting.** Read `Rules/SRD5e2014/Runtime/Combat/
  DeathSaveService.cs` directly while designing this: once a combatant stabilizes (3 death-save successes),
  `DeathSaves` clears but nothing generic stops it resuming ordinary actions at 0 HP --
  `TacticalAiTurnDriver`'s incapacitation branch keys off `DeathSaves != null`, which becomes false again the
  instant it stabilizes. That's a real, separately-scoped gap in the engine's own death-and-dying loop, not
  something this sample should quietly special-case around. This sample's own combat resolves 0 HP as
  immediate death instead (a ~5-line composition-root-local bridge: `CreatureDroppedToZeroHP` -> republish
  `CreatureDied`), matching `TacticalEncounterController`'s own doc comment that whoever owns the encounter
  subscribes to `CreatureDied` itself -- the same kind of ruleset/game-specific death-semantics choice
  `ConsumerGame`'s Fate "Taken Out" already established as legitimate. This makes "victory" unambiguous. No
  spellcasting either -- CharacterCreator already demonstrates that; this sample's job is combat mechanics.
- **No `EventSystem`/`InputSystemUIInputModule`, despite `TacticalSliceBootstrap`'s own dogfood precedent
  including one.** CharacterCreator's own UI Toolkit buttons already proved live (a real human click-through)
  that this project's runtime `UIDocument` panels receive mouse clicks without one. Adding it would also pull
  in `com.unity.ugui` as a real new package dependency (confirmed missing from both `package.json` and the
  clean-install harness's manifest when first tried) for no proven benefit. If the live review below finds
  `AbilityBarView`'s buttons genuinely not receiving clicks, this needs revisiting -- not assumed fine.
- **Line of sight/line of effect not required** on either combat ability -- at a 2-unit melee range, two
  adjacent combatants have line of sight to each other in every realistic case the sample's one wall obstacle
  produces; wiring a real `PhysicsLineOfSightProvider` for a distinction that barely matters at melee range was
  judged not worth the added complexity for this sample specifically.

## Automated verification (all real, all passing, re-confirmed against a freshly recreated scratch project)

1. Package's own EditMode+PlayMode suites in `TestProject`: 679/679 EditMode, 213/216 PlayMode (3 correctly
   self-ignored -- all three samples' scene smoke tests), 0 failures. No regressions from the
   `CombatEncounterService.EndCombat()` reorder.
2. `clean-install-harness.sh package-only` (freshly recreated scratch project): 679/679 EditMode; PlayMode
   203/216 passed, 10 failed (the exact pre-existing, already-documented TestProject-fixture-dependent gap from
   `Task-5.3-BasicCRPG.md` -- confirmed unrelated, a `package-only` run has no samples imported at all), 3
   ignored.
3. `clean-install-harness.sh import "Tactical Encounter"`: real Sample API import, clean compile, scene added
   to Build Settings by content.
4. `clean-install-harness.sh test "TacticalEncounter"`: **11/11 passing**, confirmed twice consecutively
   against the freshly imported copy in a freshly recreated project. The four fixture classes:
   - `TacticalEncounterVerificationTests` (1 test): scene smoke check -- real `GameSessionHost`/session started
     via the ruleset-aware path, real SRD5 policies (`IArmorClassRule`/`IAttackBonusRule`/`IInitiativeRule`/
     `ISavingThrowRule`) registered, the sample's own real content (`BehaviorSetDefinition`) registered. Read-
     only, no combat started.
   - `TacticalAiScoringTests` (3 tests): the real Foe Warrior AI content directly proven against
     `UtilityAIDecisionProvider` -- a legal attack beats movement, movement prefers the nearest out-of-range
     enemy when no attack is legal, an already-in-range Move decision doesn't repeatedly win once attack
     becomes legal.
   - `TacticalReactionTests` (1 test): a deterministic, controlled scenario (not emergent AI behavior) --
     a real `MoveActionExecution` walks a mover across a reactor's threat boundary; the real Opportunity Attack
     fires, spends the Reaction resource, and logs a real attack roll.
   - `TacticalFullEncounterTests` (1 test): real baked NavMesh with an obstacle, 2v2, both factions driven by
     the sample's own real authored AI content, real rolled initiative, ticked to a real win (one faction's
     `Combatants` count reaches zero) with real attack-roll log entries and at least one real hit along the way
     -- not just "someone eventually vanished somehow."

Every test except the scene smoke check constructs its own genuinely isolated `GameSession`/event bus/content
(built in memory via `TacticalEncounterTestContent.cs`, mirroring the sample's own real authored recipe rather
than loading the shipped `.asset` files, which -- like `Hero.prefab` for CharacterCreator -- only resolve once
the sample is actually imported) -- never reusing the loaded scene's own live session, avoiding the exact
cross-talk (shared `CreatureDied` handlers, a second live `TacticalEncounterController`) a second encounter
sharing that session's event bus would risk.

## Honest scope limits / what the fixture doesn't cover

- The fixture proves the sample's own authored combat/AI content and the shipped engine mechanics it exercises
  -- it does **not** prove `TacticalEncounterBootstrap`'s own wiring, `AbilityTargetingGate` click-to-attack, or
  the HUD. No player exists in a `[UnityTest]`, and Heroes are deliberately player-controlled in the real
  scene.
- `TacticalAiTurnDriver`'s AI-driven `Move` calls never pass `potentialReactors` to `ActionScheduler.StartMove`
  (confirmed by reading the real source) -- only player-driven moves (`TacticalPlayerMoveAdapter`, which does
  pass `OtherLiveCombatants`) can provoke a reaction in the real scene today. Reactive Strikes is still granted
  to both Heroes and Foes (a real consumer with a smarter turn driver could enable the other direction), but
  the live review below only exercises the direction that actually works: a Hero moving out of a Foe's reach.

## Review gate: pending live human review

Everything automatable is green, re-confirmed against a freshly recreated scratch project. This task is not
yet considered closed -- needs:
- HUD (turn order, ability bar, combat log/HP panel) renders and updates as turns pass.
- Click a Hero's Melee Strike, target a Foe through the real click-to-attack flow, confirm visible feedback.
- End Turn; the Foes faction visibly moves/attacks via the real AI -- confirm it actually attacks once in
  range, not just moves indefinitely (the exact failure mode `TacticalAiScoringTests` exists to prevent).
- Provoke a real opportunity attack (move a Hero out of a Foe's melee range without spending an action);
  confirm it fires and is visible in the combat log.
- Play to a real win or loss; confirm the HUD correctly clears/shows a result and
  `TacticalAiTurnDriver`'s decision-cap guards are never hit in a normal play-through.

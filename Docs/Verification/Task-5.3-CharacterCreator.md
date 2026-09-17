# Task 5.3 (continued) results: `CharacterCreator` sample

## What was built

- `crpg-toolkit/Samples~/CharacterCreator/`: the package's second sample -- a real, interactive SRD5 character
  creation flow exercising the engine's generic `ChoiceRequirement`/`CharacterBuildPlan`/`CharacterBuildService`
  machinery, something no prior demo in this repo actually did (`FighterDemoBootstrap` only ever *auto-resolves*
  every choice by taking its first N options).
  - `Scripts/CharacterCreatorBootstrap.cs`: composition root. Starts a real ruleset-aware session directly
    against the package's own shipped `Rules/SRD5e2014/Content/SRD5RulesetProvider.asset` (same "real consumer"
    precedent as Task 5.2's Fate ruleset), and is the sole owner of build/register/spawn once the player
    finishes -- a one-shot `hasBuilt` guard prevents ever building/spawning a second character.
  - `Scripts/CharacterCreatorView.cs`: the whole interactive flow (Race -> Class -> Background -> Ability
    Scores -> every generically-rendered pending `ChoiceRequirement` -> a Start Over button -> a summary with
    Create Character), built procedurally on `UIDocument`, mirroring `DialogueView`'s established
    rebuild-on-refresh convention. One shared generic multi-select picker (toggle buttons restricted to real
    options, Confirm gated on an exact selection count) drives every screen, including ability-score assignment.
    Deliberately linear/one-way -- `CharacterBuildPlan.RefreshPendingChoices` only ever adds to
    PendingChoices/ResolvedChoices, so an in-place "go back and change an earlier answer" was rejected in favor
    of Start Over discarding and rebuilding the whole plan.
  - `Content/Prefabs/Hero.prefab`: one capsule-primitive party member (same minimal shape as BasicCRPG's
    Hero1/Hero2), `Content/character-creator-panel-settings.asset`, `Scenes/CharacterCreator.unity`.
  - `Editor/CharacterCreatorPrefabAuthor.cs` / `Editor/CharacterCreatorSceneAuthor.cs`: disposable
    "regenerate if you customize this" authoring scripts, same convention as every other `*Author.cs` in this
    project.
- `crpg-toolkit/Tests/Fixtures/CharacterCreatorVerification/CharacterCreatorVerificationTests.cs`: the real
  behavioral acceptance check, same non-sample-referencing convention as `BasicCrpgVerificationTests`.
- `Docs/Verification/Scripts/clean-install-harness.sh`: two small hardening fixes, benefiting `TacticalEncounter`
  too (see below) -- reused otherwise unchanged.

## Design decisions made while building this (verified against real content before assuming)

- **Read every real SRD5 choice before designing the picker.** Every class's level-1 skill/style/cantrip choice
  requires 2, 3, or 4 selections (Fighter skills=2, Rogue skills=4, Wizard cantrips=3, etc.) -- "resolve by
  taking the first option" would throw the moment `Count > 1`, which is the norm here, not an edge case. The
  picker is a real toggle-based multi-select (a `HashSet`-backed selection restricted to the real options list,
  Confirm gated on an exact count), not a single-click affordance.
- **Race deliberately excludes Elf.** `RaceDefinition_Elf` unconditionally grants `feature-high-elf-traits`,
  whose one choice (a Wizard cantrip) has `Options: []` and relies on `FilterTag: "wizard-cantrip"` -- the
  engine has no generic FilterTag-resolution support today (`AbilityDefinition` carries a typed `AbilityTag`,
  not a free-string tag to match against). Implementing that is real engine work, out of scope for a sample;
  Human/Dwarf/Halfling are offered instead, and the exclusion is commented in-code with the reason, not silent.
  Every other reachable race/class/background choice in the real content has a fully explicit, satisfiable
  `Options` list -- confirmed by reading all 45 `FeatureDefinition_*.asset` files' `choices:` blocks directly,
  not assumed.
- **`CharacterBuildPlan.IsComplete` is not a completeness gate** -- it's just "zero pending choices," true even
  for a freshly-constructed empty plan. The sample's linear one-way flow (Race -> Class -> Background -> six
  ability scores -> the pending-choice loop -> Summary) makes an explicit "ready to build" boolean redundant:
  Summary is structurally unreachable until every step is genuinely done.
- **The character-sheet summary has two different data sources and doesn't conflate them.** `CharacterState`
  carries no Race/Class/Background identity field at all (confirmed by reading `CharacterState.cs`) -- those
  names come from the `CharacterBuildPlan` the view already holds. Final ability scores, granted features
  (including the *chosen* Fighting Style, which resolves via `SelectFeature` and lands in `character.Features`,
  not `character.Proficiencies`), proficiencies, and known spells are read from the real built `CharacterState`,
  proving `Build()` actually applied everything rather than just echoing the plan back.
- Confirmed Human's real `FeatureDefinition_HumanTraits` grants +1 to all six stats via a `FeatureModifier`
  (`queryKey: stat-<name>, operation: 0 (Add), value: 1`) and Dwarf's is +2 Constitution / Hill Dwarf +1 Wisdom
  (not a flat +1-to-all like Human) -- the verification fixture asserts final scores accordingly, not raw
  Standard-Array equality.

## The clean-install harness: two small hardening fixes (shared, benefits TacticalEncounter too)

Found by re-reading `clean-install-harness.sh` closely before reusing it:

1. `ensure_project` reused the same scratch project directory across runs unconditionally -- a `package-only`
   run after an earlier sample import was **not actually sample-free**, silently invalidating that step's whole
   point. Fixed: `package-only` now always deletes and recreates the scratch project first (`ensure_project 1`).
2. Every `-runTests` invocation ended in `|| true` (Unity's own exit code isn't a reliable pass/fail signal) and
   the script only ever printed the result summary line -- it never actually asserted `failed="0"` or that the
   intended fixture ran at all. A silently-ignored or zero-matched `-testFilter` would print a harmless-looking
   `total="0"` and the script would still exit 0. Fixed: a new `check_results` helper asserts `failed="0"` and a
   nonzero `total`, used for the scoped `test` (fixture-specific) runs where that's unambiguously correct.
   `package-only`/`full-suite` keep a separate non-asserting `report_results` print, since their own PlayMode
   run legitimately carries the already-documented, pre-existing 10 TestProject-fixture-dependent failures
   (see `Task-5.3-BasicCRPG.md`) that are unrelated to whatever a given harness invocation is actually verifying.

## A real, non-obvious authoring gotcha found this task (new, worth remembering for TacticalEncounter)

The `PrefabFolder`/`ContentFolder`/`ScenesFolder` constants in every `*Author.cs` script use an AssetDatabase
path like `"Packages/com.crpgtoolkit.core/Samples/<SampleName>/..."` -- **no tilde**. Running the author menu
items (via `-executeMethod`) does **not** write into the real `Samples~/<SampleName>/` folder on disk --
Unity's AssetDatabase has no special-case redirect for this; it just creates a brand-new, literal
`crpg-toolkit/Samples/` folder (no tilde) alongside the real `Samples~/`, since `Samples~` is invisible to the
asset pipeline entirely (that's the whole point of the tilde suffix) while `Samples` is a perfectly normal
Packages-relative path Unity is happy to create content under. The generated `Content`/`Scenes` assets (and
their `.meta` files, with real, usable GUIDs) land there, not in `Samples~`.

**The actual required process, confirmed working this task:**
1. Run the author menu items -- content lands in `crpg-toolkit/Samples/<SampleName>/...` (no tilde).
2. Manually move (not copy -- preserve `.meta` files intact, they carry the real GUIDs) that generated content
   into `crpg-toolkit/Samples~/<SampleName>/...`, merging alongside the hand-written `Scripts/`/`Editor/` code.
3. Delete the now-empty stray `crpg-toolkit/Samples/` folder (and its own `.meta`) -- it must not be left
   behind or committed.
4. The hand-written `.cs`/`.asmdef` source files under `Samples~/` never go through AssetDatabase directly
   (same invisibility), so they have no `.meta` of their own yet. Their first-ever, now-permanent GUIDs come
   from importing the sample into a scratch project (`Sample.Import()`, which **does** generate fresh `.meta`
   files for its copy under `Assets/Samples/.../`) and copying those generated `.meta` files back into the real
   `Samples~/` source tree alongside the originals.
5. Re-import into a clean project afterward to confirm the whole round-trip is stable and nothing regressed.

This explains the pre-existing, previously-unexplained stray `crpg-toolkit/Assets.meta` file sitting at the
repo root from the original BasicCRPG authoring pass (evidently the same class of artifact, never cleaned up) --
flagged here for visibility, not touched, since it predates this task and isn't this sample's concern. This
task's own equivalent stray (`crpg-toolkit/Samples.meta`) was found and deleted before finishing.

## Automated verification (all real, all passing)

1. Package's own EditMode+PlayMode suites in `TestProject` after the change: **671/671 EditMode, 208/210
   PlayMode (2 ignored -- BasicCrpgVerificationTests and CharacterCreatorVerificationTests correctly
   self-ignoring, since TestProject's own Build Settings register neither sample's scene), 0 failures.** No
   regressions.
2. `clean-install-harness.sh package-only` against a **freshly deleted and recreated** scratch project: 671/671
   EditMode; PlayMode 198/210 passed, 10 failed, 2 ignored -- the 10 failures are exactly the pre-existing,
   already-documented TestProject-fixture-dependent gap from `Task-5.3-BasicCRPG.md`, confirmed unrelated to
   this sample (same count as before, and a `package-only` run has no samples imported at all).
3. `clean-install-harness.sh import "Character Creator"`: real Sample API import, clean compile, scene added to
   Build Settings by content.
4. `clean-install-harness.sh test "CharacterCreatorVerificationTests"`: **1/1 passing** against the freshly
   imported copy. This single `[UnityTest]` proves, end to end, against real content:
   - The scene's live session really started via the ruleset-aware path with real SRD5 races/classes/
     backgrounds registered (not a hand-rolled empty database).
   - A Human Fighter Soldier build resolves its real Fighting Style (SelectFeature, count 1) and skill
     (SelectSkill, count 2) choices correctly, and the built `CharacterState`'s final ability scores correctly
     include Human's real +1-to-all modifier.
   - A Dwarf Wizard Sage build resolves its real cantrip (SelectSpell, count 3) and skill (count 2) choices
     correctly, with final scores reflecting Dwarf's different (+2 Con, Hill Dwarf +1 Wis) modifier profile --
     proving the modifier application isn't hardcoded to Human's shape.
   - The first built character, once registered/spawned as a from-scratch `CharacterAgent`, actually relocates
     on a real ground click through the real `PartyExplorationController` pipeline.
   - Re-run twice (once mid-session, once against a from-scratch fresh scratch project) with identical results.

## Honest scope limits / what's still open

- **This fixture does not exercise `CharacterCreatorView`'s UI, `CharacterCreatorBootstrap`'s own completion
  handler, or the shipped `Hero.prefab`** -- it builds its own `CharacterBuildPlan` directly against the scene's
  real `RulesDatabase` and spawns its own from-scratch capsule, deliberately labeled as real-content
  build/movement integration coverage, not full sample-pipeline proof. That gap is closed only by a live human
  review, which **has not yet happened this session** -- unlike BasicCRPG's report, this task cannot be marked
  fully closed yet.
- **Live human review still needed** (same bar as BasicCRPG's own closed gate): open the imported
  `Character Creator` sample in a real Editor session and confirm --
  - Race screen offers only Human/Dwarf/Halfling.
  - At least one multi-select choice screen genuinely requires 2+ picks before Confirm enables.
  - Start Over mid-flow truly resets to a blank plan.
  - The character-sheet summary matches what was actually picked.
  - Create Character produces **exactly one** spawned party member (not zero, not duplicated by a stray double
    click), using the real shipped `Hero.prefab`.
  - Clicking the creation UI panel does not also move a party member (pointer capture working); once creation
    is complete, clicking open ground elsewhere does move the created character.
- No standalone build/perf capture was attempted for this sample -- not part of this task's scope (matches
  BasicCRPG's own scope: samples validate the package's API surface and content pipeline, not shippable-build
  readiness).

## Review gate: closed

The user opened the imported `Character Creator` sample in a real Editor session, created a **Rogue** through
the full flow (a class not covered by the automated fixture's own two scenarios -- exercises `feature-rogue-
skill-choice`'s 4-of-11 skill pick and `feature-expertise`'s independent 2-of-11 pick, the widest multi-select
counts in the real content), confirmed the process read as accurate, and confirmed the resulting character
responds to real mouse clicks and moves. `CharacterCreator` is considered done as of this checkpoint.

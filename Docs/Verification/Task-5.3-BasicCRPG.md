# Task 5.3 results: `BasicCRPG` sample + the clean-install harness

## What was built

- `crpg-toolkit/Samples~/BasicCRPG/`: the package's first real sample. A minimal composition root
  (`Scripts/BasicCrpgBootstrap.cs`) wires a real `GameSession`, a two-member party (`Hero1`/`Hero2`, plain
  capsule primitives -- no third-party art), and one interactable prop (`Treasure_Chest`, a real
  `WorldInteractable` + the package's own `LootInteractionAdapter`) through the package's actual public API --
  the same seams a real consumer's composition root would use (`GameSessionHost.StartSession`,
  `PartyExplorationController`/`PartyMovementService`, `InteractionControllerBehaviour`,
  `InputSystemCommandSource`). Separate runtime (`Scripts/`) and editor-only (`Editor/`) assemblies, no
  `Tests/` folder shipped inside the sample itself.
- `Editor/BasicCrpgCharacterAuthor.cs` / `Editor/BasicCrpgSceneAuthor.cs`: the disposable authoring scripts
  that generated the shipped `Content/` (two hero prefabs, one `ItemDefinition`) and the shipped
  `Scenes/BasicCRPG.unity` -- kept alongside the sample as "regenerate if you customize this" tools, matching
  every other `*Author.cs` convention in this project.
- `Editor/Validation/RulesetRegistrationValidator.cs` + `Editor/Validation/PackageValidatorWindow.cs` +
  their tests (Task 5.3 step 2, already reported at an earlier checkpoint -- 671/671 EditMode passing at that
  point).
- `crpg-toolkit/LICENSE.md` + `crpg-toolkit/Licenses/{OGL-1.0a.txt,SRD5-Section15.txt}` (Task 5.3 step 1,
  already reported).
- `crpg-toolkit/Tests/Fixtures/BasicCrpgVerification/BasicCrpgVerificationTests.cs`: the real behavioral
  acceptance check (see below) -- lives in the package's own test-fixture area, not inside `Samples~/`, and
  ignores itself cleanly (`Assert.Ignore`, not a failure) whenever no scene matching "BasicCRPG" is registered
  in Build Settings, so a normal run of crpg-toolkit's own suite (TestProject, which never imports its own
  samples) stays green.
- `Docs/Verification/Scripts/clean-install-harness.sh`: the reusable orchestration script (see below).

## The clean-install harness

A genuinely fresh Unity project (`_scratch/CleanInstallHarness/`, created via `-createProject`, never copied
from TestProject/ConsumerGame -- gitignored, never committed), with `com.crpgtoolkit.core` added the same way
a real consumer would (`"file:../../../crpg-toolkit"` in `Packages/manifest.json`), `activeInputHandler` set
to the Input System package explicitly (not left at whatever a brand-new project defaults to), and
`com.unity.test-framework`/`"testables": ["com.crpgtoolkit.core"]` added so the package's own tests
(including the new fixture) are reachable.

Steps actually run (all via `clean-install-harness.sh`, all repeatable):
1. **Package-only compile check**: clean, zero errors.
2. **Package-only full test suite**, standalone, before any sample is imported:
   - EditMode: **671/671 passing**, zero dependency on TestProject/ConsumerGame content.
   - PlayMode: **198/209 passing, 10 failed** -- all 10 are pre-existing tests (`CharacterWrapperTests`,
     `CrossSceneLoadTests`, `GateAFixtureTests`, `SceneTravelTests`) that reference fixture assets living in
     *TestProject's own* `Assets/` folder (e.g. `BaseHumanMaleCharacter.prefab`, the `SceneTravelTestA/B`
     scenes) by path -- not inside the package itself. **This is a real, newly-confirmed finding**: crpg-toolkit's
     own PlayMode suite has never been fully self-contained; only EditMode is. Out of scope to fix as part of
     Task 5.3 (these are pre-existing tests, not sample code), but recorded here and in `COMPATIBILITY.md`
     rather than silently ignored.
3. **Import `BasicCRPG`** via the real, scriptable Package Manager Sample API
   (`UnityEditor.PackageManager.UI.Sample.FindByPackage`/`.Import()` -- the same codepath the Package Manager
   window's "Import" button drives, not a hand copy). Landed at
   `Assets/Samples/CRPG Toolkit/0.1.0/Basic CRPG/...`, exactly where a real consumer's import would.
4. **Compile check with the sample imported**: clean.
5. Imported scene added to Build Settings (by content, not a hardcoded index).
6. **Real behavioral acceptance** (`BasicCrpgVerificationTests.PartyMovesOnGroundClickAndCanLootTheChestOnInteractClick`,
   run against the *imported* copy): constructs a real `PlayerCommandRouter`/`PartyExplorationController`
   wired to the sample's real, live `GameSession`/`PartyState`/`CharacterAgent`s, drives a real ground click
   through it and confirms the party actually relocates (not just that `SetDestination` was called); then
   constructs a real `InteractionControllerBehaviour` the same way, clicks the chest's own collider, and
   confirms the treasure genuinely lands in `session.Inventory` after a real approach/turn/execute cycle.
   **Passing** as of this writing.

## Real bugs this harness caught (not hypothetical -- each one failed first, was root-caused, then fixed)

1. **`treasureItem` read back as `null` at runtime, only in the imported copy.** `BasicCrpgSceneAuthor`
   passed the freshly-`AssetDatabase.CreateAsset`'d `ItemDefinition` across an `EditorSceneManager.NewScene()`
   call as a plain parameter; the reference reads back as a Unity "fake null" once the domain reset happens --
   the same class of bug `AdventureSliceSceneAuthor` already documented once for `PanelSettings`. Fixed by
   reloading it fresh by path inside `AuthorScene`, exactly like the already-working `Hero1`/`Hero2` prefab
   loads right next to it, plus an explicit `AssetDatabase.SaveAssets()` between `AuthorContent()` and
   `AuthorScene()`. Confirmed fixed by inspecting the regenerated scene's serialized field directly (not just
   "the author script logged success") before moving on.
2. **Camera-based click simulation is unreliable in `-batchmode -nographics`.** The first version of the
   fixture built its click ray via `Camera.WorldToScreenPoint`/`ScreenPointToRay`; this silently produced a
   ray that hit nothing. Fixed by building the ray directly (a straight line down from above the target
   point), matching `PointerCommandTests`' own already-proven `RayFromAbove` convention -- which never
   round-trips through screen space either, for exactly this reason.
3. **Frame count is not a proxy for simulated time in batchmode.** The first passing-movement attempt used a
   300-frame cap and still failed -- diagnostic logging showed the click *did* register (`MoveStatus.Moving`)
   but 300 frames had only accumulated well under half a second of simulated `Time.deltaTime`, nowhere near
   enough for a real, speed-limited `NavMeshAgent` to cover a double-digit-unit distance. Fixed by raising the
   frame cap generously (5000/8000) rather than guessing a wall-clock `WaitForSeconds` duration.
4. **Clicking `WorldInteractable.InteractionPoint` clicks the wrong thing.** That property is the *approach
   anchor* standing beside the object, deliberately not on top of it (so the anchor itself was never a valid
   click target). The fixture's interaction check was clicking the anchor -- which only ever hit the empty
   ground next to the chest -- instead of the chest's own collider. Fixed by clicking `chest.transform.position`.

None of these were pre-existing package bugs; all four were bugs in the *verification harness/authoring
script itself*, caught precisely because the harness insists on running the imported copy for real rather than
trusting a compile-clean log.

## Honest scope limits

- **Live human click-to-move check**: performed. The user opened `_scratch/CleanInstallHarness/` (the
  imported `Basic CRPG` sample, `Assets/Samples/CRPG Toolkit/0.1.0/Basic CRPG/Scenes/BasicCRPG.unity`) in a
  real Editor session, entered Play mode, and confirmed both party members respond to real mouse clicks and
  move around the ground plane correctly.
  - **A real, previously-latent gap surfaced during this exact check**: everything rendered pink. Root cause --
    the scratch project's `manifest.json` lists `com.unity.render-pipelines.universal` as a dependency, but a
    fresh `-createProject` project still defaults to the **Built-in** Render Pipeline; nothing had ever
    activated URP as the *active* pipeline there, so the sample's authored materials (which hardcode the
    `"Universal Render Pipeline/Lit"` shader, matching every other authoring script in this project --
    `ConsumerCharacterAuthor`, `AdventureSliceSceneAuthor`, etc.) rendered as Unity's pink
    shader-incompatible-with-active-pipeline fallback. This is the concrete, previously-hypothetical
    consequence of the already-known gap: **`package.json` does not declare URP as a dependency at all**, even
    though every authoring script in the whole project assumes it. Fixed for this harness specifically via a
    small one-off menu item (`CRPG Toolkit/Harness/Activate URP`, `_scratch/Assets/HarnessEditor/SetupUrp.cs`,
    not part of the package) that creates a `UniversalRenderPipelineAsset` and assigns it via
    `GraphicsSettings.defaultRenderPipeline`/`QualitySettings.renderPipeline`. This does **not** fix the
    underlying package-level gap -- a real consumer starting from a fresh project with no render pipeline set
    up would hit the identical pink-material issue. Recorded as a real, now-doubly-confirmed finding for
    `COMPATIBILITY.md`'s "Known limits," not something to silently paper over.
  - **Interaction (click-the-chest-to-loot) confirmed** from the same live session. The user's own first
    read was "nothing visible happens" -- a real, legitimate finding on its own: `LootInteractionAdapter`
    only ever touches data (`session.Inventory`), so a successful loot changed nothing observable at all.
    Fixed in `BasicCrpgBootstrap.cs` by subscribing to `InteractionControllerBehaviour.Controller.RequestEnded`
    and, on `InteractionOutcome.Completed`, tinting the chest's renderer gray and logging
    `"[BasicCRPG] Treasure Coin looted!"`. Confirmed working two ways: (a) the user reported that re-clicking
    an already-looted chest correctly does nothing, which only happens if `LootInteractionAdapter.IsAvailable`
    is correctly seeing an emptied container -- i.e. the loot really happened; (b) reading the running
    session's own `Editor.log` directly showed `[BasicCRPG] Treasure Coin looted!` logged immediately after
    the approach/turn-to-face sequence, with no exception anywhere in the surrounding stack -- confirming the
    full approach -> turn -> execute -> loot -> visible-feedback chain ran for real, from a real mouse click,
    without needing to fabricate or guess at the result.
- **PlayMode suite's TestProject-fixture dependency** (finding #2 above) is now confirmed but not fixed --
  fixing it would mean relocating/duplicating fixture assets the whole existing PlayMode suite depends on,
  well beyond this task's scope.
- **`CharacterCreator`/`TacticalEncounter` samples**: not yet built. The harness above is designed to be reused
  unchanged for both.

## Review gate: closed

The plan's own step-3 gate ("reviewed live -- including the explicit human click-to-move check -- before
anything else uses the harness") is satisfied: both click-to-move and click-to-loot were confirmed by a real
person clicking with a real mouse in a real Editor session, not just by the automated `BasicCrpgVerificationTests`
fixture. `BasicCRPG` and the clean-install harness are both considered done as of this checkpoint.

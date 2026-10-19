# Spirit of Vengeance: save and load (P1)

Status: **Opus-reviewed 2026-10-19; section 9 SUPERSEDES sections 3-6 wherever they differ.** Nothing is built. Written 2026-10-19. This replaces W3 of `SpiritOfVengeance-Foundations.md`, which the user has made the first priority. The Stormpoint hand-off (W1) and everything after it come later; the user decided the party cannot go back aboard while in town for now, so Stormpoint is a one-way location until the sailing map exists.

User direction: use the **Timeline** as part of saving and loading.

## 1. Goal and scope

A player can save the game at a safe moment, quit, start the game, press Continue (or choose a slot) and be where they were, with the same party, inventory, gold, equipment, quests, story progress and world state, without replaying the opening.

In v1:
- Save slots: slot 0 = autosave, slots 1-3 = manual. A quick-save key. A small pause/system menu with Save, Load, Quit to Menu. Main menu: Continue (most recent valid slot) and Load Game.
- Autosave when the party enters a new area or location, if it is safe.
- Saving works anywhere the party is free (the ship decks, Stormpoint once it is a location).

Not in v1: saving during combat, a dialogue, a cutscene, the opening hold (confined party), or while a transition is running; cloud saves; screenshots; saving options (they stay in PlayerPrefs); saving NPC routine phase.

## 2. Facts this design stands on (verified in source this session)

- The game never calls `SaveService`. Continue loads `ShipInteriors` with `startConfined` (back in chains). The save/load code in the toolkit is complete and tested but unused here: `SaveService.Capture/Restore/RestoreStores/RestoreLootContainers`, `SaveFileStore` (atomic replace, `.bak`, migrations), `SaveSlotCatalog` (`save-slot-N.json`), `SaveRequestService` (`RequestSave(path)` using a `SafePointGate`), `SessionLoadCoordinator`.
- `SaveData`: header (format version, time, **ScenePath**, ruleset id, content compatibility id), party, characters, inventory, **timeline events**, quests, world spawns, stores, loot containers. No custom section.
- `SessionLoadCoordinator` is **not usable for the ship**: it builds a brand-new `GameSession` + `WorldSpawnService` and destroys every agent the old service owned, party included, while `ShipSession` holds its own session, party list, player agent, confinement, motors, interactions, travel coordinator and ability policy bound to the old one.
- Much story state is already Timeline facts: the captain's defeat, the guard's alerts, the stash taken, breakout events (`ShipCombat.DefeatedKey`, `GuardPatrol.AlertedKey`, `ShipStash.TakenKey`, the prisoner-talk keys), loot containers (via `session.GetOrCreateLootContainer`). Inventory and gold are session state the toolkit saves.
- State that is **not** saved today and matters: the created character profile (a global JSON file, `PlayerCharacterStore`, persistentDataPath), the opening/prologue "seen" flags (global PlayerPrefs), the current area id and the party's positions on the ship, and confinement (session-local).

## 3. Design

### 3.1 A load is a fresh boot with a payload
No in-place swap and no `SessionLoadCoordinator`. Loading means: read and validate the slot, put the result in a static `PendingLoad`, load the saved scene (`Header.ScenePath`) with a Single scene load, and let the normal boot run: `ShipLocation.Start` boots ShipCore, and `ShipSession.Start` sees `PendingLoad` and **restores instead of starting fresh**. Because Single load destroys the old ShipCore, `ShipSession.OnDestroy` ends the old session cleanly. The same code path serves Continue from the menu and Load from the pause menu.

`ShipSession.Start` with a payload:
1. Create the session as today (`StartSession(new ShipRuleset(...))`), then `SaveService.Restore(data, party, characters, inventory, timeline, quests)`.
2. Rebuild the party views from the saved spawn records with `WorldSpawnService.BindRestoredView` (the saved character ids, the saved positions and facings), companions from their NPC definitions and the created character from the **embedded profile** (3.2), instead of `SpawnNpc` and `ShipPlayer.Spawn` with new ids.
3. Skip: `startConfined` and the brig seating, `PrologueArmed`, the `ResolvePlayer()` read of the global file, the starting-area reset. Set the area from the saved area id (3.3) and place the camera.
4. Restore stores and loot containers: construct each saved store/container id first (their lazy `GetOrCreate*` must run before `RestoreStores/RestoreLootContainers`, which only apply onto existing instances), then restore.
5. Then the existing tail: input, interactions, quests tracker, `Ready = true`, location bind.

### 3.2 The Timeline carries what the toolkit does not
Facts and text on the Timeline are saved and restored for free. Use them for the game's extra state:
- `player.profile` (a Text value, the created character's profile JSON), recorded when the player is first spawned. The save embeds the character; a later New Game rewrites the global file without damaging old slots. On load, `ShipSession` rebuilds the character from this value.
- `loc.area` (Text, read with `GetLatest`): the current ship area or town area id, written on every area change/arrival. The scene path is already in the header; **only the area id is new**.
- `story.opening.seen` and `story.prologue.seen` (Flags) replace the PlayerPrefs flags as the source of truth for "has this save passed the opening/prologue". PlayerPrefs stay only as a menu hint for New Game routing and are no longer consulted by Continue (Continue routes by "a valid slot exists"). `ClearSeenFlags` then cannot affect any save.
- Anything a future system needs follows the same rule: a Timeline key, never PlayerPrefs, never a new `SaveData` field.

### 3.3 What is deliberately not restored
- Confinement and the opening hold: saves are blocked while anyone is confined, so a save never contains a confined party. (A player who quits in the hold replays it; it is the opening.)
- Running sequences, combat, dialogue, camera director shots: blocked by the safe-point check.
- Anything derived from Timeline facts (guard patrol mode, stash taken, captain defeated) is rebuilt by the scene's own scripts from those facts on boot. Verification item: confirm each such script reads its Timeline key on start and does not rely on session-local fields (see section 6).

### 3.4 Safe point
`ShipSession.CheckSafeToSave()` composes the toolkit `SafePointGate` (combat, an interaction in progress, a transition) with game checks: any captive confined, a sequence/prologue/captain director active, the camera director running, a modal window open, and `Ready == false`. It returns the toolkit's `SafePointBlockReason` where one exists plus a game reason string for the UI ("Not while you are restrained", "Not during a fight").

### 3.5 Slots, triggers and UI
- Paths: `Application.persistentDataPath/saves/save-slot-N.json` via `SaveSlotCatalog.SlotPath`; slot 0 autosave, 1-3 manual; `SaveFileStore` gives the atomic write and the `.bak` recovery.
- Autosave: on arrival in a new area/location when `CheckSafeToSave()` allows it, after a short delay, written on a background-safe path (the JSON build is on the main thread, the write is cheap). Never during a transition.
- Quick save/load keys, and a **pause/system menu** (Esc when no window is open): Resume, Save (slot list), Load (slot list with time, area, character name), Quit to Menu. It takes the ship's input lease (`AcquireInputBlock`) and reads Escape itself. Slot list entries read time from the header and the character name from the saved `player.profile`.
- Main menu: Continue = the most recent valid slot, disabled when none; Load Game = the slot list. New Game starts fresh as today and never touches slots.
- Errors surface as a message in the menu (corrupt slot: offer the `.bak`; newer format: refuse politely; content mismatch: say so).

### 3.6 Versioning
`SaveData.CurrentFormatVersion` (3) and `ISaveMigration` handle the toolkit's own layout. The game's extras are Timeline keys, so adding one needs no format bump; a key missing in an old save reads as "not occurred", which each reader must treat as the safe default. `ContentCompatibilityId` = a game content constant bumped when definitions a save references change incompatibly.

## 4. Tests (written first)

EditMode (pure): save JSON round trip including a Text value on the Timeline with a realistic profile; `loc.area` read with `GetLatest` after several writes; slot path and catalog listing in a temp directory; the safe-point composition with fakes; "missing key = default" for each new reader; slot ordering for Continue.

PlayMode (the real scenes): boot ShipInteriors in the released state (test fixture), then: move the party, take gold from a container, record a fact, equip an item, change area across scenes (ShipExterior and back), save, tear down by loading a Single scene, load the slot and assert: same party members and **same character names**, the created character rebuilt from the embedded profile, positions within tolerance, area and active scene, inventory and gold, the Timeline facts, the quest log, the container's looted state, no duplicate agents, ShipCore's session healthy (a click-move still works, the HUD shows the party). Blocked cases: saving while confined, in combat, in a dialogue, in a transition returns a block reason and writes nothing. Corrupt slot and `.bak` recovery. The Stormpoint scenes join this suite when they become ship-hosted.

## 5. Slices (stop at a checkpoint after each)

1. **Pure model**: `SaveSlots` helper (paths, most recent valid slot), Timeline keys (`player.profile`, `loc.area`, `story.*`) and their readers, the safe-point composition, EditMode tests.
2. **Restore path**: `ShipSession` `PendingLoad` boot, the embedded profile, `BindRestoredView` party rebuild, store/loot ordering, `RequestSave` from `ShipSession`; the PlayMode round trip test. This is the risky slice; it ends only when the round trip is green.
3. **UI and triggers**: autosave, quick keys, pause menu, main-menu Continue and Load Game, New Game semantics, messages.
4. **Hardening**: corrupt/newer/mismatch handling, `.bak` recovery, a scripted two-scene save/load, a regression run.

## 6. Verification items for review (things I have not proved)

- `WorldSpawnService`: can a saved non-party spawn record (a location NPC) be registered without a view so that `LocationInitializer.Prepare` takes its restore branch on the next arrival? What does `BindRestoredView` require for the party members, and does `ShipPlayer.Spawn` have a restore equivalent?
- Order dependencies in `ShipSession.Start`: which fields (`companions`, `Confinement`, `exploration`, `Abilities`) assume freshly spawned agents and a brig; what must change for a payload boot with `StartConfined` false.
- Whether `GuardPatrol`, `PrologueDirector`, `CaptainEncounter`, `ShipStash`, the door scripts and the ship HUD rebuild correctly from Timeline facts alone on boot, or keep session-local state that a save would silently lose (open doors, patrol position, alarm counters, equipment visuals).
- The `Timeline` `Text` value: is it persisted by `SaveService.CaptureTimeline/RestoreTimeline` with its text intact, and any size limits?
- Whether `SafePointGate` can be built against `ShipSession`'s interaction controller and its `WorldTransitionGate`, and how ship combat (not the toolkit's `CombatEncounterService`?) signals "in combat".
- `PendingLoad` surviving the Single scene load and the `ShipLocation` -> `ShipSession` start order; behaviour when the saved scene path is no longer in Build Settings.
- Anything in the Stormpoint content that creates state outside the Timeline, stores or quests (NPC routines, shop discounts via `SetStorePriceMultiplierAction`) and whether it is captured.

## 7. Decisions and alternatives

- **Load as fresh boot, not an in-place swap.** An in-place swap would have to rebind every ShipSession component and HUD presenter to a new session; a fresh boot reuses the normal start path and tears the old world down with the scene. Cost: a load shows a scene transition (acceptable).
- **Timeline for extras, not a new SaveData section.** The user asked for the Timeline; it avoids a format bump and keeps one atomic file.
- **Embed the profile in the save** (Timeline Text) rather than a sidecar file: one atomic write, no divergence from a later New Game.
- **No saves in the opening hold.** Restoring a confined party needs brig seating and the release sequence state; the opening is short and replayable.
- **Slot 0 autosave + 3 manual slots**: small, predictable; the catalog supports more.

## 8. Open questions for the user

1. Is "no saving until the party is out of the brig" acceptable for v1?
2. Autosave only on area arrival, or also periodically (every N minutes when safe)?
3. Is a quick-save key (F5) / quick-load (F9) wanted, or menu only?

## 9. Opus review outcome and the revised design (2026-10-19; SUPERSEDES sections 3-6 wherever they differ)

The central idea held: a load is a fresh boot with a payload, restoring through `SaveService` into ShipCore's own new session and rebinding the party with `BindRestoredView`. The review found that the "skip a few things" description was incomplete and that several pieces of scene state I said were rebuilt from Timeline facts are written but never read back. Spot-checked by me: `CaptainEncounter` only records `DefeatedKey` (never `HasOccurred`), `ShipStash.IsAvailable` tests the instance field `Taken` not the Timeline, `ShipFeelBootstrap.Init` is a once-per-domain `RuntimeInitializeOnLoadMethod`.

### Must-fix findings
1. **The captain fight would restart after a load.** `CaptainEncounter` always boots `NotMet` and arms on any LowerDeck->ShipExterior arrival (`CaptainEncounter.cs:82-85`); it never reads `captain-defeated`/`captain-met`. Add a boot-time reader (`Defeated` if the key occurred). (My doc also cited the wrong key: `ShipCombat.DefeatedKey` is the guard's `guard-defeated`.) The round-trip test asserts "captain defeated -> stairs round trip -> no fight".
2. **Continue must not strand a player who quit before any save exists.** Saves are blocked while confined, so a player who made a character and quit in the hold has no slot. Continue = the newest valid slot, otherwise the **existing route** (character file + PlayerPrefs seen-flags: opening, or the hold). **Drop `story.opening.seen` and `story.prologue.seen`**: no save can exist before both happened, and the opening flag is set before any session exists. PlayerPrefs flags stay for the no-save route only.
3. **Ship feel is lost after a load from inside the game** (and already after Quit to Menu -> New Game): the bootstrap's waiter destroys itself after the first spawn and the ShipFeel instance lives in ShipCore. Spawn it from `ShipSession` when Ready (`ShipFeelBootstrap.Spawn(this)`), or keep the waiter alive. Test: a `ShipMotion` exists after load.
4. **The `ShipSession.Start` payload walk** (all line refs in `ShipSession.cs`):
   - ShipInteriors is authored `startConfined: 1`; the payload boot must override `location.StartConfined` everywhere (slots, rotations, `ConfineAll`, the brig validation at :135).
   - **Do not skip `Confinement.Initialize` with seats**: `CanActorMove`, `TryTransferParty`, `OnSelectCommand`, `GuardPatrol`, `HoldCaptives` all dereference `Confinement`, and `CanReconfine` lets the player re-chain in the hold. Run it as on the ShipExterior path (it confines nobody).
   - `ShipPlayer.Spawn` always mints a new `RuntimeEntityId` and `CharacterState` (a sixth character on restore). Add `ShipPlayer.BindRestored(session, spawner, savedId, prefab, pos, rot)` = `BindRestoredView("ship-player", savedId, default, prefab, ..., HeroesFaction, false)`.
   - Companions: `BindRestoredView("ship-"+id, savedCharId, def.Id, def.VisualPrefab, pos, rot, def.Faction, false)`; pass the faction explicitly. Build `party` by iterating `companions[]` in authored order, looking up each `ship-<id>` record, then the player last (`DisplayNameOf` is index-based; PrisonerTalk and ShipStash depend on the order). Do not iterate `data.WorldSpawns`. Fail if a companion record is missing.
   - Skip `Session.Party.SelectAll()` on restore (it overwrites the restored selection/active member). Skip `ResolvePlayer()`; rebuild `PlayerCreated` from the embedded profile with `profile.Rebuild` and discard its `CharacterState` (the restored one is authoritative). Replace `SetArea(StartingArea)` and `ValidatePlacements` (still `NavMesh.SamplePosition` each saved position).
   - Unchanged: animation profile clone, exploration motors, the Abilities policy, interactions, travel runner, camera follow, the readiness wait. **Equipment visuals return correctly provided `Restore` runs before binding** (`ModelAppearanceBinder` reads `CharacterState.Appearance` and `ItemsEquippedBy` at bind).
5. **Non-party actors pollute every save.** `RequestSave` passes every non-defeated spawn record as an additional character (guard, hold captives, deck captain); `Restore` registers them, the game respawns them with new ids, and their starting gear (cudgel, blade, cutlass), granted by `SpawnNpc` and equipped to their ids, stays in the party inventory: one orphan item per actor per save/load cycle, invisible in the UI. After `Restore`: unregister characters not in `data.Party.MemberIds` and remove items equipped to non-members. Test: same item count after two cycles.
6. **Loot containers**: the store/loot restore ordering is right (`Restore*` only applies onto existing instances, so pre-create from `data.Stores`/`data.LootContainers` ids; matters for Stormpoint). But the ship's only containers are corpse containers (`corpse-<spawnId>`) created on `FightEnded`; after a load the body is gone, so unlooted corpse loot is lost. **v1 limitation, handled by blocking saves while any corpse container still holds items.** (Loot containers are session dictionaries, not Timeline facts.)
7. **Errors and the payload lifecycle**: `ContentValidator.Validate` needs an `IRulesDatabase` the menu does not have, and `profile.Rebuild` can throw, so mismatches surface inside `ShipSession.Start`, after the old world is gone. Validate in `ShipSession`; on failure Single-load the menu with a static `LoadError` message (or have the menu build the ruleset database from the provider). A static `PendingLoad` survives a failed `LoadScene`: check `SceneUtility.GetBuildIndexByScenePath` first, clear on consume and on failure, reset in `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` (house precedent `Game/Checkpoint.cs:30`), and reset it in the test bases (`StartTestBase`) like `PrologueArmed`. Dice: `ShipSession` seeds `SeededRandomSource(7351)` on every boot, so every load replays identical rolls; seed from time (or save the seed).
8. **Safe point**: `SafePointGate(combat: null, interactions: Interactions.Controller, loadCoordinator: null, transitionGate: ship.Gate)`; combat is `ShipSession.InCombat` (`VikingCombatSession` inside `ShipCombat`), not the toolkit service. `SafePointBlockReason` has None/CombatActive/InteractionInProgress/LoadInProgress/SceneTransitionInProgress only, so confinement, modals and the rest need a game-side reason. PrisonerTalk and PrologueDirector hold `Gate` busy, so a separate "camera director" check is redundant. **Trap**: do not count the pause menu's own input lease in "a modal is open" (`InputBlocks > 0`) or Save is blocked from the pause menu. Also block while any member is down (`DeathSaves != null`; the down pose is not restored).
9. **Session-local state a save silently loses** (all verified): `CaptainEncounter` (`Current`, `KnockedOutCount`, bodies: finding 1); **`HoldGate`** locked/open fields (the brig re-locks and clicking it replays the breakout scene; already true on any Interiors<->Exterior trip); **`HoldCaptives.freed`** (freed prisoners return to their benches); **`ShipStash.IsAvailable`** uses the instance field (the pile is still offered, then `Take` fails: read the key in `IsAvailable`); **`GuardPatrol`** (mode/waypoint/position lost; `alarmsHandled` starts at 0 while `CountOccurrences(AlarmKey)` is above 0, so he goes straight to investigate the cell: initialise it from the Timeline; `AlertedKey` is write-only); ship loot bodies (finding 6); `PrisonerTalk.handled`; camera zoom. **There is no `QuestTracker` in `ShipSession`**: my "existing tail" claim was wrong (the QuestLog itself is restored). Each of these needs a Timeline-reading initialiser or a documented limitation, and a PlayMode assertion.
10. **Escape has three existing users**: `ShipHud` (closes the character and loot windows, reads `Keyboard.current` directly, `:284-285`), the toolkit `Exploration/Cancel` action bound to Escape (`InputSystemCommandSource.OnCancel`), and `PrologueDirector`'s skip-hold. A separate component reading Escape in the same frame would close a window and open the pause menu at once, and the HUD's I/B/P/C hotkeys ignore the input lease. **One Escape owner inside `ShipHud`**, in order: character window -> loot window -> (if not `Gate.IsBusy` and no targeting) pause; gate the hotkeys on "pause open". Decide whether pause sets `timeScale` 0: much of the ship runs on unscaled/realtime timers (GuardPatrol, ResumeRound, PrologueDirector), so "pause" would not pause them; v1 pause is a modal, not a freeze.
11. **`loc.area` is redundant**: `OnLocationChanged` derives the area as the unique `ShipArea` whose bounds contain the active member; use the same rule on restore and drop the key (it would grow the append-only log on every stair trip).
12. **Stormpoint and location NPCs**: `WorldSpawnService` can import a record without a view only via `BindRestoredView` (needs a prefab, instantiates) or `ImportDefeatedTombstone`. After a load, the NPCs of an unloaded town have restored `CharacterState`s but no record, so `LocationInitializer.Prepare` takes the `SpawnNpc` branch: duplicate characters, re-granted items, orphaned states. Needs a toolkit `ImportSpawnRecord(spawnId, charId, npcDefId, pos, yaw)`. Not needed for the ship (no spawn points); **deferred to the Stormpoint ship-hosting work and listed there**.
13. **The embedded profile**: acceptable as a Timeline Text (the only extensible channel without a toolkit change). Record it with `source = playerId`. It is needed for `PlayerName` (display names, `{player}`, HUD), the CharacterWindow race/background/class and the ShipStash kit if not yet taken. Nothing else in the ship reads the global file (only `ResolvePlayer`, the menu and the opening cinematic).
14. **Smaller**: the slot list's area and character name need a full parse per slot (`ListSlots` reads each file but returns only the time): read once and cache; the pause-menu load needs a black cover (`ArrivalOverlay` lives in the Cinematic assembly, which the ship cannot see: move it to Game); autosave needs no background path (`SaveFileStore` is synchronous and small).

### Verified correct
`SaveData` shape (no custom section, header has `ScenePath`); `SessionLoadCoordinator` is unsuitable; Timeline Text round trip (`OfText`, `CaptureTimeline/RestoreTimeline`; `LoadEvent` keeps sequence, timestamp and amount so `GetLatest`, `Sum` and `Count` rebuild identically; `Record` continues after a load; `JsonUtility` escapes embedded JSON; no size limit; cast `Session.Timeline` to `Timeline` for `Restore`); `CharacterState` is captured completely (stats, resources, conditions, features, level, death saves, concentration, abilities, appearance); party members, order, groups, selection, active restore; the spawn ids `ship-<npcId>` and `ship-player` exist; a Single load tears ShipCore down and `ShipLocation.Start` re-adds it; `PrologueDirector` does nothing unless `PrologueArmed`, and only `MainMenuController.Choose` and the opening arm it, so load paths must not call `ArmPrologueIfUnseen`. Tests that pin Continue today and must change: `StartFlowTests.cs:38-48`, `StartRouteTests.cs:69-76`; `PrologueRunTests`/`StartTestBase` reset the statics and must also reset `PendingLoad`.

### Revised slices
1. **Slice 1, pure and small**: the slot helper (paths, newest valid slot), the `player.profile` Timeline text and its reader, the game-side safe-point composition, EditMode tests. No `story.*`, no `loc.area`.
2. **Slice 2a, the smallest vertical proof**: boot ShipInteriors released (fixture), record one fact, equip one item, `RequestSave`, Single-load ShipInteriors with `PendingLoad`; assert five members with the same ids and names, positions, the equipped visual, the fact, a **new** `ShipSession` instance, ShipFeel present, and **no orphan characters or items after two cycles**. This proves findings 3, 4, 5 and the teardown order before anything else is built.
3. **Slice 2b, scene-state readers and cross-scene**: `CaptainEncounter`, `ShipStash.IsAvailable`, `GuardPatrol` alarms, `HoldGate`/`HoldCaptives`, the store/loot pre-create, a ShipExterior save, the corpse-loot block, dice seeding; PlayMode assertions for each.
4. **Slice 3, UI**: the Continue fallback route, error-to-menu, the single Escape owner, the pause menu, autosave, `ArrivalOverlay` moved to Game, quick keys if wanted, the slot list.
5. **Slice 4, hardening**: corrupt/newer/mismatch handling, `.bak` recovery, a scripted two-scene save/load, a full regression. Stormpoint's `ImportSpawnRecord` (toolkit) belongs to the Stormpoint ship-hosting work, not here.

### Questions this changes
1. Saving is unavailable while the party is confined, and Continue falls back to the existing character/opening route in that case. Acceptable?
2. Corpse loot: block saving while a body still holds items (simple), or build "left behind" handling later?
3. Pause is a modal (the ship world keeps running behind it, as many ship timers use real time) rather than a freeze. Acceptable for v1?

## 10. As built: slices 1 and 2a (2026-10-19, uncommitted)

**Slice 1 (pure):** `Assets/Game/Saving/GameSaves.cs` (slot folder via `SaveSlotCatalog`, newest usable slot, `TryReadSlot` with plain-language failures and `.bak` fallback, the static pending-load hand-over reset on `SubsystemRegistration`, `TryBeginLoad` = read + build-settings check + park payload + Single scene load, never leaving a payload after a failure), `SaveGate.cs` (the game's save rule as a pure function: not ready / combat / busy / confined / member down / loot left behind, each with a player message), `Ship/ShipSaves.cs` (`player.profile` Timeline text with `source = playerId`, `TryReadProfile`, `PruneToParty`). `CRPG.Saving` and `CRPG.Quests` added to the Game and Ship asmdefs (and test asmdefs). 14 EditMode tests (`SaveSlotsAndGateTests`).

**Slice 2a (the restore path, `ShipSession.Start` with a payload):** consumes `GameSaves.TakePending()`; `confinedStart = location.StartConfined && !restoring`; time-seeded dice on a load; `RestoreFromSave` = `PruneToParty` (extra characters, non-party spawn records and the gear equipped to non-members are dropped BEFORE validation: the hold captives' definitions are only registered when their scene spawns them, so validating them fails) -> `ContentValidator` -> `SaveService.Restore` -> stores/loot pre-created then restored -> the created character rebuilt from the Timeline profile; `TryRestorePlacements` (saved position sampled onto the NavMesh, saved facing); companions rebound with `BindRestoredView("ship-<id>", savedId, ...)` in authored order and the player with the new `ShipPlayer.BindRestored`; `SelectAll()` skipped; the area derived from the active member's position (no `loc.area` key); a failure sets `GameSaves.LoadError` and Single-loads the menu. `ShipSession.CheckSave()` / `TrySave(slot, out message)` (game gate + the toolkit `SafePointGate` + `SaveRequestService`). `ShipFeelBootstrap`'s waiter now keeps running and spawns ship feel for every new ship session (it destroyed itself after the first, so a load or Quit-to-Menu/New Game lost it).

**Proof (`ShipSaveLoadTests`, PlayMode, real ShipInteriors):** with a created character, a released party, a changed active member and position, a worn dagger, 37 gold, a Timeline fact and a gear-carrying guard on deck: save, Single-load the slot, and again: same five ids in the same order and names, same active member and area, positions within 0.6 m, same carried and worn items, the purse, the blade worn by the same companion, the fact, a NEW `GameSession`, one `ShipSession`, nobody in the brig, ship feel present, no gear worn by anyone not in the game, the created character rebuilt, the profile not re-recorded, the party can still move, the boot consumed the payload, and the restored ship can be saved again. A refused save in the brig writes nothing. **Mutation checks:** disabling the gear pruning fails the test (the validator rejects an item worn by an absent character); the first version of the test was vacuous (the opening wears no gear) and was fixed by equipping a real item and adding the guard.

**Found on the way:** registering the Stormpoint cast in the ruleset (earlier) broke four ContentIntegrity/ally/boss/crew design tests that assert the ruleset's NPC list is exactly `GameDesign.Units` (32 vs 15). I had not run the full suite after that step. `ContentFixture` now excludes `Assets/Content/Stormpoint`. Full EditMode: 936 run, those four were the only failures. Ship PlayMode regression (traversal, player, confinement, start route): 26/28 earlier; the two failures are the known Captain bedroom door path (Hermes' furniture move) and, until fixed, nothing else.

**Not built yet (slices 2b-4):** the scene-state readers (CaptainEncounter reading `captain-defeated`, `ShipStash.IsAvailable` reading the Timeline, GuardPatrol `alarmsHandled` from the Timeline, HoldGate/HoldCaptives state), a ShipExterior cross-scene save test, the corpse-loot rule is only the save gate for now, the Continue fallback route and menu, the pause/system menu with the single Escape owner, autosave on arrival, `ArrivalOverlay` moved to Game, hardening.

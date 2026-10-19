# Spirit of Vengeance: foundations for voyages and a political campaign

Status: **reviewed by Opus 2026-10-19; section 8 SUPERSEDES sections 2-4 wherever they differ** (W4 is done). Original status: plan for review (Opus), then the user. Written 2026-10-19 after the Opus review of `Docs/Design/Sailing-Map-System.md` (section 12 there) found gaps that block the sailing map and, more generally, any campaign that moves between the ship and towns. The sailing map itself stays PARKED until the story is written; these are the prerequisites that are worth having whatever the story turns out to be.

## 0. The four gaps and the order

| # | Gap | Size | Why it comes here |
|---|---|---|---|
| W4 | The map crop's import settings fail `LocationMapDefinition.Validate` | tiny | Independent; do immediately. |
| W1 | Stormpoint must be a **location under ShipCore** (one persistent session) | medium, riskiest | Every town, voyage arrival and crew scene depends on it. Spike first. |
| W2 | Dialogue gaps for crew scenes on the ship: `{player}` token, "party member present", quests wired on the ship | small-medium | Needed the moment the party talks in the ship session (W1 makes Stormpoint conversations depend on it too). |
| W3 | **Save/load** (the game has none; Continue is a placeholder) | large | Design now, build after W1/W2 because what it saves (location, session shape) is defined by them. |

Rule from the user's process: tests first, verify with real Unity runs, stop at a checkpoint after each workstream, commit only when asked.

## 1. W4: map asset import (do now)

- Re-crop to a width that is a multiple of 4: **2876 x 3284** (3284 is already one). Update the Sailing doc's bounds to Rect(0, 0, 2876, 3284) and the preview scale note.
- Import settings: Texture2D, **NPOT scale = None**, max size 4096, mipmaps on, bilinear, clamp, sRGB, compression normal quality (block compression now applies).
- EditMode test: load `Assets/World/Map/AnuireWestCoast.png` and run `LocationMapDefinition.Validate` against bounds (0,0,2876,3284) (the test creates the definition in memory; no committed asset until the map is un-parked).

## 2. W1: Stormpoint as a location under ShipCore

### What exists (verified in source)
- `SceneTravelCoordinator` (toolkit): Core scene persists; a location scene loads additively; the same `GameSession`/`WorldSpawnService` survive; on arrival it runs `LocationInitializer.Prepare(content, session, spawner, scene)` (spawns the NPCs of the scene's `ILocationContent`, binds stores and loot) and sets the camera bounds from `content.CameraBoundsXZ`.
- `ShipLocation` (game): an entry point per location scene; if no `ShipSession` exists it additively loads ShipCore, waits for `Ready`, and binds the scene's `TravelInteractionAdapter`s to the session's runner and gate.
- `ShipSession` (ShipCore): owns the session, party, input, camera rig, interactions and `TravelRunner`. On a location change (`activeSceneChanged`) it requires exactly one `ShipLocation` and a `ShipArea` whose `CameraBounds` contain the active party member.
- Stormpoint today: a standalone scene with its own `VillageBootstrap`, camera rig and input and the four Viking Raid heroes. It is not in Build Settings.

### Design
1. **Stormpoint gets a `ShipLocation` and a `ShipArea`** (AreaId `stormpoint`, `CameraBounds` = the town's playable rect; `PartyStart` = the existing anchor; `StartConfined` false; no brig). This reuses every existing hook; the names are ship-flavoured but the contract (a location with a start, an area and bounds) is exactly what a town is. Rename later if it bothers anyone, not now.
2. **Remove the village-style composition from Stormpoint**: the `CRPG` camera rig, `InputSystemCommandSource`, `VillageBootstrap` and the borrowed `PartyStart`/panel wiring. ShipCore provides the rig, input and party. The authored `LocationContent` (16 spawn points, shops) and the NavMesh stay.
3. **A location-side host, `StormpointLocationHost`**, waits for `ShipSession.Ready` then does for the ship session what `VillageBootstrap` did for the village: `NpcRoutines.AttachAll`, `NpcDialogues.AttachAll` (panel settings from the ship HUD, currency item, quests, skill-check rule), and keeps a `QuestTracker` ticking and refreshed after each interaction (`Interactions.Controller.RequestEnded`). It must be re-runnable (each visit) and release what it attached on unload. Shared plumbing that both the village and Stormpoint need (`AttachAll` options, gold item id) is lifted out of `VillageBootstrap` into one small helper so neither copies it.
4. **A `QuestTracker` in the ship session** (ShipSession currently has none; `StartQuestAction` is a silent no-op without it). Created once with the session; also fixes crew-scene quest hooks later.
5. **Travel**: add Stormpoint to Build Settings; a `TravelInteractionAdapter` at the dock gangway in ShipExterior targeting Stormpoint (destination = the quay `PartyStart`) and one in Stormpoint (at the berth) targeting ShipExterior. A debug menu travels without art, so the spike does not wait for gangway visuals. The docked-versus-at-sea look of ShipExterior is out of scope (noted).
6. **Edge cases the spike must settle**: the `.25` agent radius that `ShipSession` forces versus the town NavMesh baked at the default 0.5 (decide: rebake with a 0.25 agent type, or leave conservative); the active-party-member-outside-area exception on arrival; ship-only HUD status text ("Click deck to move") in a town; confinement code is inert when nobody is confined but must not throw without a brig; camera zoom/pitch settings are the ship's, the town may want the village-style settings (`CRPGCameraSettings` swap on arrival or one shared asset).
7. **Standalone dev start stays**: opening `Stormpoint.unity` directly boots ShipCore through `ShipLocation.Start` with the real party (companions plus the created character) at the quay. `VillageBootstrap` stays for the village scene only.

### Tests (first)
- PlayMode, a new ship-hosted test base: Stormpoint boots with the real party standing on the NavMesh; the harbour-master and Danly conversations open by click; Danly's quest starts through the ship session's tracker; the shop opens.
- Travel: ShipExterior -> Stormpoint -> ShipExterior keeps **the same `GameSession` instance**, a Timeline fact recorded before travel is still present, inventory persists, NPC spawns are released and re-bound correctly on a second visit (no duplicates; Danly's stock does not reseed).
- `StormpointTownTests`/`StormpointCaptureTests` move to this base; the village fixtures are untouched.

### Done when
The party walks off the ship's gangway into Stormpoint, talks to NPCs with working quests and shops, walks back aboard, and nothing about the session was rebuilt.

## 3. W2: dialogue gaps for crew scenes

1. **Text tokens.** A pass that replaces `{player}` (and, once the profile has them, pronoun tokens) in line text before display. Preferred seam: a small `IDialogueTextTransform` applied where the view receives line text. The view lives in the toolkit, so this is a toolkit change (commit + sha bump) unless the review finds a game-side seam; decide in review. Pronouns: `PlayerCharacterProfile` has none today. Default is they/them; adding a creator choice is a separate user decision, so until then write pronoun-free lines.
2. **Party-member present.** The ship session records `party.member.<npcId>` as a 0/1 level key when a member joins or leaves (Timeline, deactivate before activate). A branch can already test a Timeline key, so no new branch source is needed. Wire it in `ShipSession` and `NpcRecruitment`.
3. **Quests on the ship**: done in W1 (item 4).
4. **Timeline predicates (needed by voyage events, not by W1-W3)**: a serialisable `TimelinePredicate {type,key,secondKey,threshold}` (AND over a list) calling the existing `TimelineCondition.Evaluate`. Deferred until voyage events are real; listed so it is not forgotten.

Tests: EditMode for the token transform and the presence keys; PlayMode for a presence-gated line in a ship conversation.

## 4. W3: save/load (design only now)

Facts: the game never calls `SaveService`; Continue loads `ShipInteriors` with `startConfined` (the party back in chains); `SaveData` has party, characters, inventory, timeline, quests, world spawns, stores, loot but no scene or location; the toolkit has `SessionLoadCoordinator` for cross-scene loads; the created character lives in `PlayerCharacterStore` (PlayerPrefs) and the opening/prologue flags are PlayerPrefs.

Questions for its own design doc (written after W1/W2, then Opus-reviewed):
- **Location in the save**: record the current location scene path and area id as Timeline facts at every arrival (the Timeline is saved) so load can travel to it; "at sea" is a location too.
- **Load path**: build on `SessionLoadCoordinator`; ShipSession's start must skip `startConfined`, the Prologue arming, the starting-area reset and `ResolvePlayer` re-spawn when restoring.
- **Which state is outside `SaveData`** and must be handled explicitly: the created character profile, PlayerPrefs story flags, ship state, any new voyage state, depleted stores (covered), NPC routines (not saved).
- **Slots and triggers**: one manual slot plus an autosave on arrival at a location; Continue = the latest; versioning for old saves.
- **What the main menu needs**: Continue enabled only when a valid save exists.

## 5. Sequencing and checkpoints

1. W4 (now, minutes). 2. W1 spike to a working vertical slice, then stop for review. 3. W2. 4. W3 design doc + review, then build. 5. Un-park the sailing map when the story is ready; its slice 1 then starts on solid ground.

## 6. Decisions and alternatives

- **Reuse `ShipLocation`/`ShipArea` for Stormpoint** rather than a new town-location type: least change, every ship hook already works. Alternative (generalise ShipSession to any location type) is a larger refactor of a 470-line session for the same behaviour; revisit if a second non-ship location needs different rules.
- **Standalone Stormpoint start goes through ShipCore**: one code path for dev and play; the village keeps its own bootstrap.
- **Quests via a session-owned tracker**, not per-location trackers: quest state must outlive a location.
- **Save/load is not folded into W1**: it deserves its own reviewed design.

## 7. Open questions for the user

1. Pronouns: they/them by default with a later creator option, or pronoun-free writing only for now?
2. Is a toolkit change (token transform) acceptable, with a commit + sha bump in CRPGUtility, if the review finds no game-side seam?
3. For Stormpoint's camera, keep the ship's zoom/pitch settings or use the village-style close, steeper ones?

## 8. Opus review outcome and the revised plan (2026-10-19)

Spot-checked by me: `LocationInitializer.Prepare` creates every NPC deactivated (`SetActive(false)`, "until the caller commits") and `SceneTravelCoordinator`'s commit step (release old spawns, SetActiveScene, SetBounds, unload, EndBusy) never reactivates them.

### Findings that change the plan
1. **Travel leaves the destination's NPCs inactive** (invisible, unclickable, absent from `CharacterAgent.AllLive`). It never showed because the ship scenes have no spawn points and Barrow's End only calls Prepare at boot. `SessionLoadCoordinator` does activate and Warp them, which shows this is an omission. Fix either in the coordinator (toolkit commit + sha bump) or game-side from the spawner's owned agents on arrival. **Open question 2 therefore applies to W1.**
2. **No per-location host.** On the travel path a host's `Start` runs in the settle frame before Prepare, so `AttachAll` finds no NPCs; on the standalone boot path nothing calls Prepare. Put the bring-up in `ShipSession`: at boot run Prepare; on every arrival (`OnLocationChanged`) activate the views, run `NpcRoutines.AttachAll` and `NpcDialogues.AttachAll` with the session's `QuestTracker`, then raise an `Arrived` event. The shared-plumbing helper from the village is dropped (about 15 lines).
3. **Ship-only systems key on a session existing, not on ship geometry**: `ShipFeelBootstrap` (camera rocking, hull ambience at >= 0.7x volume, HullRipple foam ring around the whole town area), ShipCore's camera (far clip 250, FOV 60, versus Stormpoint's 2400/39), the HUD subtitle "THE IRON WAKE" and raw area id, ship music. Add a **location kind (ship or shore)** to `ShipLocation` and a per-location presentation profile applied on arrival: ShipFeel on/off and sea state, camera far/FOV/zoom/pitch, HUD subtitle, music.
4. **Quests**: expose `ShipSession.Quests` (one tracker, ticked in the session's `Update`, refreshed in `OnInteractionEnded`) and pass `key => Quests.Start(key)` into every game-built `DialogueContext` (PrisonerTalk-style crew scenes too). "Done in W1" was wrong: a session-owned tracker reaches only `NpcDialogues` adapters.
5. **Dialogue panel**: use the ship conversations' `HudPanelSettings` (1200x800, plain style, RollPopup check presenter) and their gate convention (Gate busy "talk", so the HUD hotkeys are off during a conversation), not the 1920x1080 HUD panel. `NpcDialogueOptions` has no check-presenter hook, so town skill checks need a small toolkit option or a game-side wrapper.
6. **Presence keys (W2.2) deferred.** The ship party is fixed; there is no join/leave; recruiting into the ship session would throw in `CanActorMove`/`OnLocationChanged`, miss the HUD and be destroyed when leaving town (it lives in the location scene). Design join/leave in ShipSession when a story beat needs it. (When built: levels are `Timeline.Sum` deltas read with `SumAtLeast 1`.)
7. **`{player}` seams exist game-side**: `PrologueStage.Expand` already replaces `{player}` (fallback "The swimmer"); reuse that token and fallback. Speaker names and choice text need it too, not just line text. Two seams: a per-session expanded clone of the definition (reflection on private fields) at the `ConversationController`/`TalkInteractionAdapter.Dialogue` handoff, or a small toolkit `Func<string,string>` on `DialogueContext` applied in `DialoguePresenter`. Prefer the toolkit `Func` if a toolkit commit is acceptable (it is also needed by finding 1). Pronouns are still absent from the profile.
8. **W3 corrections**: `SaveHeader.ScenePath` already exists and `SessionLoadCoordinator` already loads across scenes by it; only the **area id** is missing (record it as a Timeline fact; do not record the scene path). The created character is a global JSON file (`PlayerCharacterStore`, persistentDataPath, with .bak/.tmp), not PlayerPrefs: **save slots must embed the profile** or a later New Game overwrites what an older slot needs. The seen-flags are global PlayerPrefs and `ClearSeenFlags` on New Game hits existing saves. **`SessionLoadCoordinator` is a trap for ShipSession**: it builds a new `GameSession`/`WorldSpawnService` and destroys every owned agent, while ShipSession holds its own session, party list, player agent, confinement, motors, interactions, travel coordinator and ability policy bound to the old one, and the ship companions are saved as ordinary NPC records (they would be restored into the location scene and destroyed by the next travel). The W3 design must choose "reboot ShipCore with a restore payload" versus "swap in place" up front, use the existing `SafePointGate`/`SafePointBlockReason` for when saving is allowed and `ISaveMigration` for versioning.
9. **Tests as written would fail**: the Danly purchase needs gold (the village granted 80; the ship grants none; decide the purse); `ShipSession.Start` validates PartyStart tightly (0.45 m sampling, dy <= 0.3, 0.9 m spacing, connected) so the quay anchor may throw; party size depends on the developer's character file (use `PlayerCharacterStore.PathOverride`); the coordinator looks up a `SceneTravelDestination` component by id (Stormpoint has none; the plan named a Transform); `StormpointSceneAuthor` re-adds the rig and `VillageBootstrap` on every run, so a re-run would undo W1 unless the author is updated.
10. **NavMesh radius question is moot**: all three scenes use agent type 0 (radius 0.5); `NavMeshAgent.radius` only affects avoidance, so 0.25 on a 0.5-eroded mesh is merely conservative. Leave it; do not rebake with another agent type.
11. **Smaller**: the debug-menu travel must call `SetArea` itself (ShipSession restores area bounds only when an interaction completes); Stormpoint's extent contains ShipExterior's deck so both overlap during the transition (fade over it, check NavMesh sampling near the origin); `OnLocationChanged` needs exactly one `ShipLocation` and exactly one area containing the active member (overlapping areas throw).
12. **W4 done** (importer settings verified in the meta); the test also asserts the native size.

### Revised order
1. **W1-0, a cheap spike before touching Stormpoint (a 99k-line scene with 4 tests):**
   - a toolkit-level PlayMode test proving travel leaves arriving NPCs inactive (red first);
   - a small **TownStub** scene: plane, baked NavMesh, one NPC with a dialogue, a quest and a shop, `ShipLocation` + `ShipArea` + `SceneTravelDestination`, and a gangway adapter in ShipExterior;
   - prove: NPC activation on arrival, arrival bring-up in `ShipSession` (findings 1 and 2), a session-owned `QuestTracker`, the shore presentation profile (3), second-visit rebind with no duplicates and no store reseed.
   Then port the pattern to Stormpoint, update `StormpointSceneAuthor` (no rig/bootstrap re-add) and move the tests to a ship-hosted base (real party via `PlayerCharacterStore.PathOverride`, a decided purse).
2. **W2**: quest wiring for game-built contexts, then the `{player}` token pass; presence keys deferred.
3. **W3**: its own design doc with the corrected facts and an explicit reboot-vs-swap decision, then Opus review, then build.
4. Un-park the sailing map afterwards.

### Questions that now block starting W1-0
1. **Toolkit change acceptable?** The NPC-activation fix is best made in `SceneTravelCoordinator` (other consumers would want it too). It needs a commit + push to CRPGUtility and a bumped sha in the game's manifest. The alternative is a game-side workaround in ShipSession, which leaves the toolkit bug in place. Recommendation: fix it in the toolkit.
2. **Pronouns**: they/them default with a later creator option, or pronoun-free writing only for now?
3. **Camera for Stormpoint**: keep the ship's zoom/pitch, or the village-style closer, steeper one (it feeds the shore presentation profile)?
4. **Purse**: the ship session grants no gold. Start the party with a small purse (the village used 80) when aboard, or let the Danly shop test grant gold itself?

## 9. As built: Stormpoint hosted by the ship (2026-10-19)

**User decisions applied:** toolkit changes allowed; the party cannot go back aboard from the town for now (a bounded "on the ship, not yet sailing" camera is back-burnered); Stormpoint gets a city camera; the party's first gold is a chest in the captain's quarters; pronouns by "natural assignment" (no creator option yet, nothing built for it).

**Toolkit (crpg-toolkit, committed locally in the monorepo; push and sha bump pending the user):**
- `SceneTravelCoordinator.ActivateStagedViews`: `LocationInitializer.Prepare` stages every NPC view inactive "until the caller commits" and the coordinator's commit never activated them, so a location's people were invisible and unclickable after a travel (only a save load activated them). Now activated and warped on commit. Proven by a mutation check: with the call commented out `StormpointShipTests.TravellingFromTheShip...` fails ("harbour-master is alive after the trip: null").
- `CameraRigState.UseSettings` + `IsometricCameraRig.UseSettings`: a location can swap the camera settings (the same `Rig` object lives on). `BasePitch` is now `private set`.
- The game's `Packages/manifest.json` temporarily used `file:../../crpg-toolkit` while developing; restore the pinned git URL with the new sha before committing the manifest.

**Game:**
- `ShoreProfile` (camera settings, field of view, near/far clip, HUD subtitle) on a `ShipLocation` makes it a shore location. `ShipSession.ApplyPresentation` applies it on arrival (and puts the ship's own camera back); `ShipFeelBootstrap` switches the ship-feel object off while `IsShore`; the HUD subtitle comes from `ShipSession.LocationSubtitle` ("TAEGHAS") and the area title "Stormpoint".
- `ShipSession.BringUpLocation`: on boot (a town scene opened first) it runs `LocationInitializer.Prepare` and activates the views; on every arrival it attaches `NpcRoutines` and `NpcDialogues` (conversation panel assigned on the ShipCore `ShipSession`, gold as the currency, a session-owned `QuestTracker`, SRD5 skill checks). `ShipSession.Quests` is ticked in `Update`, refreshed after every interaction. The HUD's `Talking()` check keeps the hotkeys and the menu out of a townsperson's conversation.
- `StormpointSceneAuthor` now removes the village-style rig/input/bootstrap and cameras and builds the hosting (`ShipHost`: `ShipLocation` with the shore profile, `ShipArea` "Stormpoint", `SceneTravelDestination` "stormpoint-quay"); `WireShipCoreAndBuildSettings` assigns the conversation panel and adds Stormpoint to Build Settings. Assets: `Assets/Scenes/Stormpoint/StormpointCamera.asset`, `StormpointShore.asset`.
- `ShipChest` (+ `CaptainChestAuthor`): 100 gold in the captain's quarters against the left wall, taken once (`ship-chest-taken:captain` on the Timeline). `ShoreTravelAdapter` (+ `ShoreLeaveAuthor`): "Go ashore at Stormpoint" at the starboard rail amidships, available once `captain-defeated`; the town has no way back.
- Tests (`StormpointShipTests` 6, `ChestAndTownTests` 3): opened directly the town boots the ship session at the quay with the real party and the city camera; sixteen contacts active, reachable, talkable; the harbour-master conversation; Danly's quest through the session tracker, vessels, a shop purchase for the party's gold; travel from the ship keeps the same session, Timeline and party; save and load inside the town; the chest and the way ashore. The old village-bootstrap Stormpoint tests were replaced.

**Still open:** the real arrival from a voyage (the way ashore is a stand-in until the sailing map); NPC routines and Rusk; spore pickups (the goblin camp); evidence pickups, sanctuary and the other request consumers; Danly's graph ends the conversation after the shop; the healing potion has no use ability; a toolkit-level test for the activation fix (the TestProject was not run); the captain's-quarters door error the user reported (exact text still needed; the "Captain bedroom door" path test fails on Hermes' furniture); two leap PlayMode tests fail independently of this work.

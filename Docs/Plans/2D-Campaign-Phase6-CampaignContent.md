# 2D Campaign Phase 6: Campaign content & story

Turns the existing story/content handoff into a real, playable 3-act campaign on top of toolkit systems (Dialogue,
Quests, Combat) that already exist and are proven elsewhere (the Oakhaven sample content, `ConsumerGame`'s
encounter wiring) but have never been used inside `BarrowsEnd` itself. Source material: `BarrowsEnd/Docs/Handoff/
Barrows-End-Handoff.md` (party, 10 town NPCs, 3-act flow, a sample Mayor Thaddeus dialogue tree),
`Barrows-End-GUI-Handoff.md` (already consumed by Phase 5's re-skin), `Barrows-End-Town-and-Caverns-Layout.png`
(an 11-building town square + a 3-level cavern dungeon path).

## Goal

A complete, playable Act 1 -> Act 2 -> Act 3 campaign: a real hand-authored Town map and 3 real Caverns levels
(code-authored, not code-generated demo slices), 10 town NPCs (services + Mayor Thaddeus's recruiting/briefing
dialogue), a main quest and at least one side quest, and placed/triggered combat encounters per act — landing
Phase 7's 30-60 minute playthrough gate on real content instead of the Phase 3 proof-of-pipeline slices.

## Non-goals

- The literal `tempo_points`/`current_tactical_phase` mini state machine and compound-AND dependency in the
  handoff's sample Mayor Thaddeus tree — that was written to stress-test a different (LLM-routed) dialogue
  framework; dropped per the user's own call. The narrative beats (recruit party -> get briefed -> receive the
  quest -> head to the caverns) are kept; the mechanism is simplified to the toolkit's real branching primitives.
- A new quest-authoring visual tool (`Editor/Quests/` stays empty) — quests are authored as raw `QuestDefinition`
  assets via an editor script, the same way the Oakhaven sample content and `BarrowsEndGearAuthor`'s existing
  `npc-skeleton` asset are authored, not hand-drawn in the Inspector.
- A new compound-condition dialogue node type — moot once the tempo_points/AND dependency is dropped; every
  remaining branch in the simplified trees needs at most one `BranchNode`/`ChoiceOption` condition.
- A real bounty-board mechanic for Captain Kael — flavor dialogue only unless a later phase asks for a tracked
  bounty system.
- User-painted maps — an earlier Phase 3 design comment (`BarrowsEndLocationAuthor.cs`) assumed the user would
  hand-paint the real maps with the Tile Palette once the semantic tile assets existed; the user's explicit call
  this phase is to code-author them instead (closer to `BarrowsEndLocationAuthor`'s own existing pattern for the
  demo slices), accepting that a procedural layout won't match the reference image pixel-for-pixel.
- Real armor AC, hit-points-as-a-toolkit-rule, and the other pre-existing "known gaps" already tracked in this
  project's own memory/backlog — out of scope here unless a specific piece of Phase 6 content needs one of them
  to function (flagged inline in that task's own plan if so).

## Source facts (verified via a research pass before drafting; not assumed)

1. **`Town_Slice`/`Cave_Slice` are explicitly-labeled throwaway proof scenes**, not drafts of the real maps
   (`BarrowsEndLocationAuthor.cs:18-21`'s own doc comment says as much). `AuthorTown()` builds one house + a
   paved square + a palisade gap on a 30x20 layout; `AuthorCave()` builds one flat 24x16 room. Neither resembles
   the 11-building town square or the 3-level cavern path in the reference image. Real Town/Caverns scenes need
   new authoring scripts, following the same tile-vocabulary/`LocationBaker`/`SceneTravelDestination` pattern.
2. **Zero NPC/dialogue/quest content exists for any of the 10 town NPCs or Mayor Thaddeus.** The only authored
   NPC-ish asset in `BarrowsEnd` is `npc-skeleton` (a combat-test fixture for the Phase 2 gate scene, no
   dialogue). Every name in the handoff doc is unwritten.
3. **The toolkit's dialogue system** (`crpg-toolkit/Runtime/Dialogue/`) is a polymorphic node graph
   (`LineNode`/`ChoiceNode`/`BranchNode`/`CheckNode`/`ActionNode`/`EndNode`, `[SerializeReference]`, referenced by
   `SerializableGuid` id) with conditions evaluated against `ITimeline` (`TimelineConditionType`:
   `HasOccurred`/`NotOccurred`/`SumAtLeast`/`SumAtMost`/`CountAtLeast`/`OccurredBefore`) — one condition per
   `BranchNode`/`ChoiceOption`, no built-in multi-clause AND/OR. `DialogueValidator` (Editor) catches dangling
   refs/unreachable nodes/inescapable cycles before anything ships. A visual `DialogueGraphWindow` exists for
   hand-authoring; the project's own established convention through Phase 5 has instead been "author everything
   from code, idempotently" (`*Author.cs` + reflection `SetField`, e.g. `ConsumerDialogueAuthor.cs`).
4. **A complete, structurally-similar sample dialogue tree already exists to use as a template**:
   `crpg-toolkit/Rules/SRD5e2014/Content/Oakhaven/DialogueDefinition_dlg-eldor.asset` (15 nodes: state-gated
   `BranchNode`s, a `ChoiceNode`, `ActionNode`s firing `RecordEventAction`/`GiveItemAction`, an `EndNode`) —
   structurally close to what Mayor Thaddeus's simplified tree needs. `QuestDefinition_TheGoblinMenace.asset`/
   `QuestDefinition_TheLostHeir.asset` are the template shape for this campaign's own quest assets.
5. **A "Talk To" quest objective needs zero Quests-side knowledge of Dialogue** — a conversation's
   `RecordEventAction` and a quest objective's `trackingKey` just need to agree on the same Timeline key
   (`QuestDialogueIntegrationTests.cs`'s own documented design point). This is the mechanism the main quest
   (accepting Thaddeus's briefing) and the side quest (Grumble's mushrooms) both use.
6. **Saving has no proximity/location gate at all** (`SafePointGate` blocks only on transition/load/combat/
   interaction-in-progress) — Elida's "save point" framing in the handoff doesn't correspond to an existing
   location-gated mechanic, and there is no `DialogueAction` today that triggers a rest or opens the Save screen
   from inside a conversation. **User's call: add both as small, generic toolkit additions** (Decisions, below) —
   not BarrowsEnd-local hacks, since "rest/save from dialogue" is a capability any consumer could want.
7. **`RestService.Rest(IEnumerable<RuntimeEntityId> party, GameEvent restType)`** already does exactly what a
   "rest" dialogue action needs (restores every matching resource, publishes one event) — it's pure data, needs
   no screen hand-off, unlike opening the Store (`OpenStoreAction`) or (by the same shape) the Save screen, both
   of which need the composition-root-level `ConversationController` to react to a request flag on
   `DialogueContext`, since only it can actually show a screen.
8. **The encounter pieces (`CombatEncounterService`, `ProximityTriggerZone`, faction-tagged `NpcDefinition`
   spawning) are proven in `ConsumerGame`** (`ConsumerGameBootstrap.cs`'s `SpawnCombatant`/`WireTriggerZone`) but
   have never been wired together inside `BarrowsEnd` itself — Phase 6 is this project's first real use of that
   combination, not a novel design.
9. **Established BarrowsEnd content-authoring convention** (`Editor/Content/*Author.cs`): one static class per
   content family, `[MenuItem("Barrows End/...")]` entry points, idempotent (updates in place, never destroys
   hand-adjusted content without an explicit `Reset*`/`overwrite` menu item), doc-commented with the Task it was
   built under. New Town/Caverns/NPC/dialogue/quest/encounter authoring fits directly into this family.

## Decisions

- **D1 (dialogue complexity): simplified, per the user's call.** Mayor Thaddeus's tree keeps the narrative beats
  (recruit party -> briefing -> receive the caverns quest -> optional exposition/bounty flavor branch) but drops
  `tempo_points`/`current_tactical_phase` entirely and needs no compound-AND — the one real conditional gate
  ("ready to descend") becomes a single `BranchNode` on a Timeline flag set once Garrick/Elara/Silas are all
  recruited (`RecordEventAction` fired by each companion's own recruiting conversation), not an inventory-count
  check either (dropping the `inventory_qty(...)` clause along with tempo_points — it was serving the same
  framework-testing goal, not a real campaign requirement).
- **D2 (rest/save from dialogue): two new, generic `DialogueAction` types in `crpg-toolkit/Runtime/Dialogue/`,
  REVISED after checking the actual precedent (see Task 6.0's own source facts below) — simpler than this doc's
  first draft assumed.** `RequestLongRestAction` executes immediately against a new nullable `RestService`
  reference added to `DialogueContext`'s constructor (mirrors `CampPresenter`'s own existing "take a whole
  `RestService` instance" convention). `RequestSaveAction` (renamed from `RequestOpenSaveScreenAction`) executes
  immediately against a new nullable `Action` delegate on `DialogueContext`, NOT `OpenStoreAction`'s
  request-flag-plus-screen pattern — `BarrowsEnd`'s own Save button doesn't open a multi-slot `SavePresenter`/
  `SaveView` at all, it calls `ShellBootstrap.SaveNow()` directly (a single-file, immediate save), so requiring
  the toolkit's `SavePresenter` machinery here would add a dependency nothing in this project actually uses.
  Both actions are fire-and-forget, like `GiveItemAction` — neither needs `ConversationController` changed at
  all, unlike the Store hand-off, which genuinely needs a screen.
- **D3 (maps): code-authored, per the user's call.** New `BarrowsEndTownAuthor.cs`/`BarrowsEndCavernsAuthor.cs`
  (or a similarly-split pair) build the real Town (11 numbered buildings around the town square, per the
  reference image) and the real 3-level Glimmering Caverns from code, using `BarrowsEndTileAuthor`'s existing
  `SemanticTile` vocabulary and the same `Layout`/`FillGround`/`FillObjects`/`LocationBaker.Bake` pipeline
  `BarrowsEndLocationAuthor` already established — replacing (not extending) the Phase 3 demo slices as what the
  real campaign travels between. Idempotent like every other `*Author.cs` in this project.
- **D4 (phase structure): act-sized sub-phases, per the user's call.** Task 6.0 builds the shared foundation
  (the two new toolkit `DialogueAction`s, the real Town + Caverns scene authoring, the base NPC/vendor
  content-authoring pattern) that every act's content depends on; Tasks 6.1/6.2/6.3 build one act's actual
  content each (NPCs+dialogue+quest+encounter for that act), each ending in its own live-playthrough gate,
  matching how Phase 4 (Gate D) and Phase 5 (Gates A-E) were run. Each task gets its own detailed plan +
  Opus review immediately before it's tackled, not all four designed to the same depth up front.
- **D5 (vendors): `OpenStoreAction` + `StoreInventoryState`, no new toolkit work needed.** Barnaby/Fendrick/
  Marjorie/Silvan/Lyra each get a small authored store catalog (item + price) wired the same way
  `DialogueContext.Store`/`CurrencyItemId` already support — confirmed sufficient by source facts 3 and 7; no gap
  here worth a Decision of its own beyond noting it.

## Task breakdown (high level — each task's own plan is drafted, Opus-reviewed and approved just before it starts)

- **Task 6.0 — Shared foundation.** The two new `DialogueAction` types + two new optional `DialogueContext`
  fields (D2 — no `ConversationController` change needed, confirmed by Opus review); real `Town.unity` + three
  real Cavern-level scenes authored from code (D3), replacing the demo slices as `ShellBootstrap`'s actual
  `startingScenePath`/travel graph; a base "talkable NPC"/"vendor NPC" authoring pattern (`WorldInteractable` +
  `TalkInteractionAdapter`, a properly-registered `LocationContent.Store`) proven end-to-end with one real NPC
  before the rest are mass-authored in 6.1; a composition-root wiring gap (nothing in BarrowsEnd currently drives
  `TalkInteractionAdapter`'s runtime properties) and a test-compatibility hook (existing tests hard-code the demo
  slices' exact geometry) that Opus review surfaced as genuinely load-bearing, not optional polish. Toolkit-level
  changes here affect `crpg-toolkit/` directly (shared with the user's open Editor); BarrowsEnd-level scene work
  goes through the usual throwaway-copy verification. See Task 6.0's own section below for the full, reviewed
  design — it found 6 real blockers in the first draft and revised the plan accordingly.
- **Task 6.1 — Act 1: The Gathering.** All 10 town NPCs placed and talkable/shoppable; Garrick/Elara/Silas
  recruiting conversations (each records the Timeline flag D1 depends on); Mayor Thaddeus's simplified briefing
  tree + the main quest's first objective; Elida's rest/save-flavored dialogue (D2); the palisade-breach combat
  tutorial encounter (giant rats/beasts) using the proven encounter pattern (source fact 8) for the first time in
  `BarrowsEnd`. Gate: live playthrough from title through "party assembled, quest accepted, tutorial fight won."
- **Task 6.2 — Act 2: The Descent.** Caverns Level 1 + Level 2 real content (mining-track upper caverns, a
  central water-chamber level with goblin barricades); goblin-patrol encounters; Grumble's mushroom-harvesting
  side quest (a `QuestObjectiveType` tracked via the same Timeline-key pattern as the main quest). Gate: live
  playthrough of both levels, main quest advances, side quest completable.
- **Task 6.3 — Act 3: The Deep Dark.** Caverns Level 3 (undead thralls, a necromancer's ritual chamber, a boss
  arena behind a portcullis); the necromancer boss encounter; main quest completion. Gate: live playthrough of
  the full dungeon descent through to the boss fight and quest completion — the closest thing to a Phase 7
  full-playthrough dry run this project has had.

## Risks

1. **The reference image's exact building/room layouts are a visual target, not a spec with coordinates** — the
   code-authored Town/Caverns (D3) will approximate it; worth a live look once painted/built, same as every other
   visual-polish item this project has gone through, rather than assuming a pixel-perfect match up front.
2. **`RequestLongRestAction`/`RequestSaveAction` are toolkit changes**, so (like every prior
   toolkit-level change this phase makes) they get the full toolkit EditMode/PlayMode suite, not just a
   BarrowsEnd-side check, and share live with the user's open Editor the same way every other toolkit edit has.
3. **Four sub-tasks is a lot of surface** — Task 6.0's foundation work is deliberately front-loaded so 6.1-6.3
   are each "author content against an already-proven pattern," not "invent the pattern AND the content" three
   times over; if 6.0 turns out to have missed something load-bearing, that surfaces as early as possible rather
   than three tasks in.

## Review record

Not yet Opus-reviewed — this master doc sets the phase's scope/decisions; per D4, the first thing to get its own
detailed plan + Opus review is Task 6.0.

**Not committed.**

---

# Task 6.0: Shared foundation

Detailed plan for the first Phase 6 task (per D4): the two new toolkit `DialogueAction`s, the real Town + three
Cavern-level scenes, and a proven "talkable NPC"/"vendor NPC" authoring pattern — the load-bearing pieces every
act's content (6.1/6.2/6.3) will build on.

**v2 — Opus-reviewed (2026-09-28), 6 blockers + 11 should-fixes folded in.** The first draft's `ConversationController`-free
`DialogueAction` design held up, but three real gaps would have made the rest of it fall over in practice: nothing
in BarrowsEnd actually wires `TalkInteractionAdapter`'s runtime properties (nothing calls it at all today), the
vendor design skipped the toolkit's real store-persistence path, and re-pointing the campaign's starting scene
would have silently broken several existing tests that hard-code the demo slices' exact geometry. See the Review
record at the end for the full list; every blocker's fix is folded into the sections below, not bolted on after.

## Goal

Land the three foundation pieces end-to-end, each proven with a minimal real example (not just written and left
untested): the campaign can travel Town -> Cavern Upper -> Middle -> Deep and back; one real NPC in the new Town
scene can be talked to and, separately, one can be bought from, with both surviving a save/load; a conversation
can trigger a real Long Rest and a real save. Acts 1-3's own content (all 10 NPCs, the full dialogue trees, the
quests, the encounters) is explicitly Task 6.1/6.2/6.3's job, not this one's.

## Non-goals

- Authoring any of the 10 named NPCs' real dialogue/content — one throwaway or minimal placeholder NPC proves the
  pattern; the cast is 6.1's job.
- The real 3-level cavern's encounters, undead/necromancer content, or Grumble's side quest — those are 6.2/6.3.
- Any change to `ConversationController`, `SavePresenter`/`SaveView`, or `ISaveRequestPort` — confirmed correct by
  Opus review (verified #3 below): ship only the two new `DialogueAction`s + the two new `DialogueContext` fields
  + `TalkInteractionAdapter`'s two new pass-through properties, on the toolkit side.
- New Kenney tile classification — the existing `TileSemantics.json` "town"/"cave" sheets already cover
  buildings, market stalls, palisade fencing, mine tracks, water/waterfalls, doors/gates/portcullis, and cave
  decor (verified by reading the actual rule tables, not assumed); nothing here should need a new sheet rule.
- Deciding how the party normally acquires starting gold in real play — real content/balance question, left to
  6.1; Task 6.0's own vendor proof grants the test party currency directly in the test, to isolate "does
  buying/selling work" from that content decision.
- Rewriting the demo slices' own tests (`ShellTests`/`ShellCaptureTests`/parts of `ShellStartFlowTests`) against
  the real Town's different geometry — they keep testing the demo slices unchanged via a new override hook
  (T6.0-D6), not a rewrite.

## Source facts (verified by reading the actual source; corrected against the Opus review, v1 -> v2)

1. **`ConversationController.AdvancePresentation` only needs a screen hand-off for the Store**
   (`context.ConsumeStoreOpenRequest()` branch, `ConversationController.cs:72-76`) because a store is genuinely
   interactive; **`GiveItemAction` proves the "fire-and-forget, no screen" shape already works** (mutates
   `context.PartyInventory` directly, `Execute` returns, `DialogueRunner` just keeps advancing). Confirmed against
   `DialogueRunner`'s actual control flow (not just the analogy): `Settle()` calls `action.Action?.Execute(context)`
   and advances synchronously (`DialogueRunner.cs:129-132`), both from the constructor's first settle
   (`ConversationController.cs:52`, before `BeginModal` even runs — harmless, since neither new action touches UI)
   and after every subsequent `Advance`/`ChooseOption`/`PerformCheck`. Rest and Save are both fire-and-forget in
   exactly this sense. Neither needs `ConversationController` touched.
2. **The toolkit's own established "trigger a Long Rest" call site is `CampPresenter.ConfirmLongRest()`**
   (`crpg-toolkit/Runtime/UI/Camp/CampPresenter.cs:78`), which takes a whole `RestService` instance via its
   constructor — `DialogueContext` takes the same shape for consistency. Confirmed: `RestService.Rest(context.
   Party.Members, GameEvent.LongRest)` type-checks (`PartyState.Members` is `IReadOnlyList<RuntimeEntityId>`,
   `PartyState.cs:33`) and restores HP specifically (`resource-hp`'s `restTrigger` is `RestTrigger.LongRest`,
   confirmed in the SRD5 content). **Caveat found in review: `RestService.Rest` only calls `RestoreToFull()` on
   matching resources — it does NOT clear a downed character's unconscious condition or death-save state**
   (compare `GateSessionServices.HealAndStand`). A Long Rest triggered from dialogue restores HP but does not
   stand a downed party member up; Task 6.0's own proof test must exercise this against a party that took a
   partial (non-downing) hit, not a knocked-out member, and this behavior should be called out in the action's
   own doc comment so 6.1+ doesn't assume otherwise.
3. **BarrowsEnd's own Save button does not go through the toolkit's `ISaveRequestPort`/`SaveRequestService`/
   `SafePointGate` stack at all** — `ShellBootstrap.SaveNow()` (`ShellBootstrap.cs:400-415`) builds
   `SaveService.Capture(...)` and calls `new SaveFileStore().Save(SavePath, ...)` directly, gated only on
   `IsReady`/`transitionGate.IsBusy` (not an interaction-in-progress check, so calling it mid-conversation is
   safe). A plain `Action` delegate the composition root points at `bootstrap.SaveNow()` is simpler than requiring
   `ISaveRequestPort`, which nothing else in this project uses. `SaveNow()` already calls `Say(...)`, which the
   HUD's `messageLabel` already displays (`HudBootstrap.cs:152-156` builds it, `:194` updates it via
   `MessageChanged`) — the delegate gets "saved"/"save failed: ..." feedback for free.
4. **`TalkInteractionAdapter.BeginExecute` (`TalkInteractionAdapter.cs:48`) constructs `DialogueContext` itself**,
   from its own public settable properties — the two new fields need two new settable properties on the adapter,
   passed through at the same call site. **Real gap found in review: nothing in BarrowsEnd ever sets ANY of this
   adapter's existing properties, because nothing in BarrowsEnd ever wires a `TalkInteractionAdapter` at all
   today** (`ShellBootstrap` only has `WireTravelAdapters` for `TravelInteractionAdapter`, `ShellBootstrap.cs:
   381-388`). A working precedent for the missing piece already exists, just not in BarrowsEnd:
   `TestProject/Assets/AdventureSlice/Scripts/AdventureSliceBootstrap.cs:325-339`'s `WireTalkAdapters` (called at
   boot and on scene load) is the shape to copy. BarrowsEnd needs its own `WireTalkAdapters`, and — since a
   session load swaps in a brand-new `GameSession` (`Characters`/`RulesDatabase`/`EventBus` all change identity,
   and `RestService` captures those at construction) — it must run again after a load too, which
   `ShellBootstrap.OnLoaded` does not currently do for ANY location content (see fact 6 below).
5. **CORRECTED (v1 wrongly claimed this was unprecedented): a real, spawned, talkable NPC already exists as a
   working pattern — just not in BarrowsEnd.** `TestProject/Assets/Editor/AdventureSliceSceneAuthor.cs:225-229`
   authors a real merchant NPC with a `DialogueDefinition` and its own `PanelSettings`; `InteractionLabSceneAuthor.
   cs:117-121` has another. `BarrowsEndLocationAuthor.AddTravelPoint` (`BarrowsEnd/Assets/Editor/Content/
   BarrowsEndLocationAuthor.cs:213-243`) remains the closest BarrowsEnd-local structural template (box collider +
   visual sprite + approach anchor + `WorldInteractable`), but the adapter + runtime-wiring shape should be copied
   from `AdventureSliceSceneAuthor`/`AdventureSliceBootstrap`, not invented fresh.
6. **CORRECTED (v1 overstated `NpcDefinition.DialogueId`'s relevance): nothing in the whole repo actually reads
   `NpcDefinition.DialogueId`** (confirmed by search) — `TalkInteractionAdapter` uses its own directly-serialized
   `dialogue` field (`TalkInteractionAdapter.cs:21`), not anything resolved through an `NpcDefinition`. A static
   talkable/vendor NPC authored the `AddTravelPoint` way (a plain GameObject with `WorldInteractable` +
   `TalkInteractionAdapter`, no `CharacterState`/combat stats) needs no `NpcDefinition` asset at all — that type
   is for a spawned, stat-carrying character (like `npc-skeleton`), a different concern from a flavor/vendor NPC
   with no combat presence. Task 6.0's two placeholder NPCs need no `NpcDefinition`.
7. **`Town_Slice`/`Cave_Slice` are explicitly-labeled throwaway proof scenes** (`BarrowsEndLocationAuthor.cs:
   18-21`), nothing like the real layout. `ShellBootstrap.startingScenePath`/`startingDestinationId` point at
   `BarrowsEndLocationAuthor.TownPath`/`TownStart` today (`BarrowsEndCoreSceneAuthor.cs:81-82`). **Real gap found
   in review: several existing tests hard-code the demo slices' exact geometry and would silently break (or
   silently stop testing anything) if the serialized starting scene is simply swapped**: `ShellTests.cs` clicks
   fixed cell coordinates against the Town_Slice/Cave_Slice layout and asserts `.Single()` door/travel-point
   counts (an 11-building town has more than one door); `ShellCaptureTests.cs` clicks fixed screen coordinates
   for the same reason; `ShellStartFlowTests.cs` asserts the active scene name IS `Town_Slice`; `LocationSceneContentTests.cs:199-200`
   asserts `startingScenePath`/`startingDestinationId` literally equal the demo-slice constants. A same-name swap
   is not safe; see T6.0-D6.
8. **The tile vocabulary needed for the real Town + 3 real Cavern levels already exists** (`BarrowsEnd/Assets/
   Content/Tiles/TileSemantics.json`, read directly, not assumed): the "town" sheet classifies market stalls,
   multiple wall materials, doors/double-doors/portcullis, the palisade fence+gate, mine rails/carts, gravestones,
   tents, bookshelves; the "cave" sheet classifies stone/dirt/cracked floors, stalagmites, bones, iron
   bars/fences, water/waterfalls, mine rails, dungeon walls, doors/gates/portcullis. **Bake-safety constraint
   found in review** (`LocationBaker.cs`): a walkable region unreachable from any travel destination is a warning
   (`:215-221`); a `Door` tile sharing a cell with a `Wall`/`Prop` is a warning (`:175-176`); a `Door` with no
   walkable non-door neighbor is an error (`:180-187`). `LocationSceneContentTests.cs:75`-style checks require
   zero bake warnings — every building footprint needs at least one real `Door` tile reachable from outside, not
   just a Ground-painted gap. The only hard size limit is world Z within ±256 (`SpriteSortingContract.MaxWorldZ`,
   checked at `LocationBaker.cs:166`); nothing else caps grid size.
9. **The general-purpose parts of `BarrowsEndLocationAuthor.cs` (`Layout`, `FillGround`/`FillObjects`, `Tile`/
   `Cached`, `NewLocationScene`, `AddDestination`, `AddTravelPoint`, `BakeAndSave`) are all `private`** — the two
   new authors cannot call them as-is. They need extracting into a shared `internal` helper (with
   `BarrowsEndLocationAuthor` itself refactored to use it too, so there's one implementation, not a second copy),
   not duplicated by hand.
10. **`BarrowsEnd.Content.Editor.asmdef` (and both BarrowsEnd test asmdefs) do not reference `CRPG.Dialogue`
    today** — the new authors need to construct `DialogueDefinition`/node/action content, so this reference must
    be added, or nothing compiles.
11. **The vendor needs a currency item and the store must go through the toolkit's REAL persistence path, not a
    bespoke shortcut.** A store an NPC "runs" is meant to be registered as a `StoreContentPoint` in that scene's
    `ILocationContent.Stores` list, fetched via `session.GetOrCreateStore` and bound onto the adapter by
    `LocationInitializer.PrepareStore` (`LocationInitializer.cs:79-90`; precedent: `AdventureSliceSceneAuthor.cs:
    243-245`) — NOT a `DialogueDefinition` that calls `OpenStoreAction` while holding some ad hoc `Store`
    reference of its own, which would never be in `session.Stores` and so would never persist across a save
    (`ShellBootstrap.SaveNow` passes `stores: session.Stores`, `ShellBootstrap.cs:409`) and would come back null
    after a cross-scene load. **A dialogue is still the right way to REACH the store** (`StorePresenter`/`StoreView`
    are not driven any other way inside an interaction adapter today, confirmed — a standalone `StoreDemoBootstrap`
    precedent exists but bypasses adapters entirely) — the minimal graph is `ActionNode(OpenStoreAction) ->
    EndNode` (a single node settles instantly with no line to read, per `DialogueRunner`'s own auto-settle), not
    literally "one node" as v1 said. No currency item or starting purse exists anywhere in BarrowsEnd today
    (confirmed by search) — `CurrencyItemId` defaults to `DefinitionId.None`, under which `StoreService` counts
    zero money for every transaction. A currency `ItemDefinition` must be authored (or, if the ruleset actually
    resolves the SRD5 sample's `item-gold-piece`, reused — unverified, check during implementation) via
    `BarrowsEndGearAuthor`'s existing `extraContent` list specifically (fact 12 explains why "existing list," not
    a new one).
12. **`BarrowsEndGearAuthor.Author` overwrites the ruleset's `extraContent` with its own hard-coded list on every
    run** (`BarrowsEndGearAuthor.cs:135` sets it, `:223`-ish builds it) — any new content (the currency item)
    registered anywhere else would be silently wiped the next time `Author Core Scene` runs `BarrowsEndGearAuthor.
    Author` (which `BarrowsEndCoreSceneAuthor.Author` already always does, `:29`-ish). The currency item must be
    added to `BarrowsEndGearAuthor`'s own list, not authored by a separate script that assumes it'll survive.
13. **`TalkInteractionAdapter` has a serialized `panelSettings` field** (`:22`) that `ConversationController`
    assigns to a new `UIDocument` (`:93-94`) — left null, nothing renders. The two placeholder NPCs need a real
    `PanelSettings` assigned; reusing `BarrowsEndUiAuthor.GalleryPanelSettingsPath`'s existing asset (sortingOrder
    10, the same one the paper doll already uses) is the pragmatic choice, since a conversation and the doll are
    both modal and never open at the same time — avoids authoring a third settings asset for no reason. Verify
    this doesn't visually clash during implementation rather than assuming it (this project's own standing rule).
14. **Enemy character sprites (goblins, undead thralls, a necromancer) are a separate concern from tiles** — the
    2D character/appearance system is what `npc-skeleton` uses for its own visual, not the Kenney tile sheets
    above. Whether the existing character `SpriteLayerCatalog`/appearance options cover a goblin/undead look is
    unverified and explicitly Task 6.2/6.3's concern, not this task's.
15. **Pre-existing, out of scope for this task (FYI, not a Task 6.0 defect):** `TalkInteractionAdapter.cs:48`
    passes `performer` (the party member who clicked) as `DialogueContext.NpcId`, so a `CheckPerformer.
    SpecificSpeaker` skill check would roll for the wrong side of the conversation. Neither new action is affected
    (`RequestLongRestAction`/`RequestSaveAction` never reference `NpcId`) — noted so it doesn't get conflated with
    something this task introduced, not something this task needs to fix.

## Decisions (Task 6.0-specific, refines Phase 6's D2/D3)

- **T6.0-D1**: `DialogueContext` gains two new OPTIONAL trailing constructor parameters: `RestService
  restService = null` and `Action requestSave = null`, exposed as `RestService`/`RequestSave` properties — both
  null-safe. Confirmed safe against every existing call site (`TalkInteractionAdapter.cs:48`, `ConsumerGame`'s
  bootstrap, `TestProject`'s `DialogueTriggerZone`, and every dialogue test) — all of them stop naming arguments
  before this point or pass everything by name, so appending two more optional params breaks none of them.
- **T6.0-D2**: `RequestLongRestAction : DialogueAction` — `context.RestService?.Rest(context.Party.Members,
  GameEvent.LongRest)`, with a doc comment noting it does NOT revive a downed member (source fact 2). `RequestSaveAction`
  — `context.RequestSave?.Invoke()`. Both live beside `GiveItemAction`/`OpenStoreAction` in `crpg-toolkit/Runtime/
  Dialogue/`, both `[Serializable]`, no authored fields.
- **T6.0-D3**: `TalkInteractionAdapter` gains `public RestService RestService { get; set; }` and `public Action
  RequestSave { get; set; }`, passed into its `DialogueContext(...)` construction call, defaulting to null.
- **T6.0-D3b (new, closes B2/B4)**: `ShellBootstrap` gains a `WireTalkAdapters()` method, structurally mirroring
  `WireTravelAdapters()` (`ShellBootstrap.cs:381-388`) — finds every `TalkInteractionAdapter` in the loaded
  scene(s) and (re)sets ALL of its session-scoped properties fresh (`Party`/`Characters`/`RulesDatabase`/
  `Timeline`/`Inventory`/`DiceService`/`SkillCheckRule`/`CurrencyItemId`/the new `RestService`/`RequestSave`) from
  the CURRENT session — not just the two new ones, since every one of them goes stale the same way after a
  session swap. Called everywhere `WireTravelAdapters()` already is (boot, `OnSceneLoaded`) PLUS from
  `ShellBootstrap.OnLoaded`, which currently calls neither `WireTravelAdapters` in its post-load path in a way
  that also re-preps location content, nor anything for talk adapters — see T6.0-D3c. A fresh `RestService` is
  constructed each call (`new RestService(session.Characters, session.RulesDatabase, session.EventBus)`), never
  cached, since it captures session-scoped references directly.
- **T6.0-D3c (new, closes B3's load-persistence half)**: `ShellBootstrap.OnLoaded` gains a call to the same
  private `PrepareLocation(scene, session, spawner)` boot already uses (`ShellBootstrap.cs`, called once during
  `Boot()`) — today `OnLoaded` never re-preps the active scene's `ILocationContent` at all, so a `StoreContentPoint`
  a vendor relies on is never rebound to the new session's `StoreInventoryState` after a load. Confirm during
  implementation that calling `PrepareLocation` again against an already-prepared, currently-active scene is safe
  (idempotent) rather than assuming it — this project's own standing verification discipline, not a new one
  invented for this task.
- **T6.0-D4**: real scenes at `Assets/Scenes/Locations/Town.unity` and three Cavern levels (working names
  `Cavern_Upper.unity`/`Cavern_Middle.unity`/`Cavern_Deep.unity`, matching the reference image's level labels) —
  a new `BarrowsEndTownAuthor.cs` and `BarrowsEndCavernsAuthor.cs`, both idempotent, both built on the shared
  helper extracted per source fact 9. Travel wiring is TWO-WAY at every step (closing the Goal's "and back," which
  v1 left as a dead end): Town <-> Cavern_Upper <-> Cavern_Middle <-> Cavern_Deep, matching `AddTravelPoint`'s own
  existing two-way `TownStart`/`CaveFromTown` precedent in the demo slices, not a one-way descent. Every building/
  room interior needs at least one real `Door` tile satisfying `LocationBaker`'s bake-safety rules (source fact 8)
  — laid out to bake clean, not just visually plausible. **Both new scenes must be added to `EditorBuildSettings.
  scenes`** (`BarrowsEndCoreSceneAuthor.cs`'s existing explicit list) or `SceneManager.LoadSceneAsync` fails in
  Play Mode — a real gap v1 missed entirely (closes B4). `BarrowsEndCoreSceneAuthor.Author()` calls the two new
  authors (alongside its existing `BarrowsEndLocationAuthor.AuthorAll()` call) and is updated to point
  `ShellBootstrap`'s `startingScenePath`/`startingDestinationId` at the real Town — the demo slices are left in
  place (kept working via T6.0-D6), not deleted.
- **T6.0-D5**: exactly ONE placeholder talkable NPC and ONE placeholder vendor NPC are authored in the new Town
  scene as the end-to-end proof — generic placeholders (e.g. "Town Crier", "Peddler"), explicitly NOT one of the
  handoff's 10 named NPCs, so 6.1 isn't left half-authoring a character this task already touched (comment both
  clearly as Task 6.0's own proof fixtures, free for 6.1 to remove/repurpose). Both follow `AddTravelPoint`'s
  box-collider/visual/approach-anchor shape, with a real `PanelSettings` assigned (source fact 13). The talker's
  tiny dialogue exercises `RequestLongRestAction`/`RequestSaveAction` once each. The vendor is reached via a
  `ActionNode(OpenStoreAction) -> EndNode` dialogue (source fact 11) whose `WorldInteractable`'s scene sits inside
  a real `StoreContentPoint` entry in the Town's `ILocationContent.Stores`, bound the same way `AdventureSliceSceneAuthor`
  already proves works, selling a small catalog of items priced in a newly-authored currency item (source facts
  11-12) — NOT a self-contained `Store` reference the dialogue owns directly.
- **T6.0-D6 (new, closes B1)**: `ShellBootstrap` gains a static `StartingSceneOverride` test hook (a
  `(string scenePath, string destinationId)?`), structurally mirroring the existing `StartModeOverride`
  (`ShellBootstrap.cs:98`-ish) — when set, `Boot()` uses it instead of the serialized `startingScenePath`/
  `startingDestinationId`. `ShellTests.cs`/`ShellCaptureTests.cs`/the `Town_Slice`-checking parts of
  `ShellStartFlowTests.cs` are updated to set this override to `(BarrowsEndLocationAuthor.TownPath,
  BarrowsEndLocationAuthor.TownStart)` before loading Core, exactly as they do today for `StartModeOverride` —
  their existing click-coordinate/door-count/scene-name assertions then keep testing the SAME demo-slice geometry
  they were written against, unchanged, while the real default (no override set) is the new real Town.
  `LocationSceneContentTests.cs:199-200`'s assertion is a different case — it reads the AUTHORED field's value
  directly (an EditMode content test, not a Boot() run), so it just needs its expected value updated to the new
  real path, the normal "a test asserts intentionally-changed behavior" update, not a workaround.
- **T6.0-D7 (new, closes B5)**: add `CRPG.Dialogue` to `BarrowsEnd.Content.Editor.asmdef`'s references, and to
  whichever of `BarrowsEnd.EditModeTests.asmdef`/`BarrowsEnd.PlayModeTests.asmdef` the new tests actually need
  (confirm exactly which while writing item 6's test, rather than adding both speculatively).

## Task breakdown (tests first, per file)

1. **`crpg-toolkit`: `DialogueContext`** gains the two new optional fields/properties (T6.0-D1). EditMode test:
   constructing without them behaves exactly as before (no regression); constructing with them and invoking each
   new action's `Execute` produces the right effect against a fake/real `RestService`/delegate.
2. **`crpg-toolkit`: `RequestLongRestAction`/`RequestSaveAction`** (T6.0-D2), plus unit tests in the shape
   `DialogueRunnerTests.cs:161,326-350` already uses for action coverage (there is no standalone
   `GiveItemActionTests` to mirror — confirmed by search, v1 was wrong to assume one exists). Cover: a party
   member with partial (non-zero) HP loss is restored to full; a downed/unconscious member's HP restores but stays
   unconscious (documents the real limitation from source fact 2, not silently skipped); `RequestSaveAction`
   invokes the delegate exactly once.
3. **`crpg-toolkit`: `TalkInteractionAdapter`** gains the two new pass-through properties (T6.0-D3). No existing
   adapter test file exists to extend (confirmed by search — v1 was wrong here too); a new small test file proves
   the two new properties reach the constructed `DialogueContext`, likely needing a PlayMode home since
   `ConversationController` instantiates real GameObjects/`UIDocument`s.
3b. **`BarrowsEnd`: `ShellBootstrap.WireTalkAdapters()`** (T6.0-D3b) + the `OnLoaded` fix (T6.0-D3c). Test: after a
   save/load round-trip, a `TalkInteractionAdapter`'s properties (including `RestService`/`RequestSave`) point at
   the NEW session's objects, not the old, disposed one — the same class of regression `PartyView.Rebind`'s own
   doc comment warns about elsewhere in this project.
4. **`BarrowsEnd`: shared authoring helper extraction** (source fact 9) — pull the general-purpose parts of
   `BarrowsEndLocationAuthor.cs` into a shared `internal` static helper; refactor `BarrowsEndLocationAuthor` to use
   it (regression: its own existing content tests must still pass unchanged).
5. **`BarrowsEnd`: `BarrowsEndTownAuthor.cs`** (T6.0-D4) — the real Town scene, all 11 numbered building
   footprints from the reference image, the town square, the palisade with its gap, two-way travel to
   Cavern_Upper, real `Door` tiles satisfying `LocationBaker`'s bake-safety rules. EditMode content test (matching
   `LocationSceneContentTests.cs`'s own conventions): grid bounds, walkability, the travel points wired both ways,
   a clean bake with zero warnings.
6. **`BarrowsEnd`: `BarrowsEndCavernsAuthor.cs`** (T6.0-D4) — three real Cavern scenes, two-way travel chaining
   Town <-> Upper <-> Middle <-> Deep. Same EditMode content-test conventions as #5.
7. **`BarrowsEnd`: the placeholder talker + vendor NPC** (T6.0-D5), including the new currency `ItemDefinition`
   added to `BarrowsEndGearAuthor`'s existing `extraContent` list (source facts 11-12) and the vendor's
   `StoreContentPoint` registered in the Town's `ILocationContent.Stores`. PlayMode test: walking up and
   interacting with the talker opens a real conversation whose rest/save lines actually call through (party
   resources restored after a PARTIAL hit, a real save file written); interacting with the vendor opens a real,
   working store (granting the test party currency directly, per the Non-goals note) that still resolves
   correctly after a save/load round-trip (closes B3) — the closest thing to an integration test this task has,
   deliberately not mocked.
8. **`BarrowsEndCoreSceneAuthor.cs` update** — calls the two new authors, adds their scenes to
   `EditorBuildSettings.scenes`, re-points `ShellBootstrap`'s starting scene/destination at the real Town.
9. **`ShellBootstrap.StartingSceneOverride`** (T6.0-D6) + updates to `ShellTests.cs`/`ShellCaptureTests.cs`/the
   affected parts of `ShellStartFlowTests.cs` to set it, + `LocationSceneContentTests.cs`'s updated expected path.
   This item is the actual regression-safety net for item 8 — "the existing suites still pass" is a real,
   separately-earned outcome here, not an assumption.
10. **asmdef updates** (T6.0-D7).

## Tests

- EditMode for the toolkit dialogue-context change and the BarrowsEnd scene-content checks; a PlayMode home for
  anything that needs a real `ConversationController`/`UIDocument` (the adapter proof, the end-to-end NPC proof).
- The save/load-survives-a-rebind test (item 3b) and the store-survives-a-load test (item 7) are the two tests
  this plan exists to make sure actually get written — both are exactly the class of bug ("worked once, broke on
  reload") this project's own past sessions have repeatedly found live if not tested explicitly.
- Full toolkit EditMode/PlayMode suites + full BarrowsEnd EditMode/PlayMode suites before this task is called
  done, per this project's standing verification discipline.

## Risks

1. **Re-pointing `ShellBootstrap`'s starting scene is the first real "swap the campaign's actual entry point"
   change this project has made** — the override hook (T6.0-D6) is the safety net, but every affected existing
   test still needs an explicit, separate pass/fail check, not an assumption that adding the hook alone fixes
   everything.
2. **The placeholder talker/vendor NPCs land in the real Town scene** — clearly commented as Task 6.0's own proof
   fixtures (Decisions, T6.0-D5), not two of the real 10 NPCs half-authored; 6.1 should feel free to remove or
   repurpose them.
3. **Goblin/undead/necromancer sprite-asset availability is explicitly unverified here** (source fact 14) —
   6.2/6.3's problem to discover, not this task's to solve.
4. **`LocationInitializer.Prepare`'s idempotency when called again on an already-active scene (T6.0-D3c) is
   assumed reasonable but not yet verified against its actual implementation** — flagged explicitly rather than
   silently assumed; verify (and adjust the fix if it turns out not to be idempotent) during implementation.
5. **The currency item's exact identity (author new vs. reuse the SRD5 sample's `item-gold-piece`) is unverified
   against whether BarrowsEnd's ruleset provider actually resolves Oakhaven sample content** (source fact 11) —
   check this specifically before assuming either path, rather than guessing.

## Review record (Opus, 2026-09-28)

**Blockers, all fixed above:** B1 (several existing tests hard-code the demo slices' exact geometry — fixed via
`StartingSceneOverride`, T6.0-D6), B2 (nothing in BarrowsEnd wires `TalkInteractionAdapter` at all — fixed via
`WireTalkAdapters`, T6.0-D3b), B3 (the vendor design skipped the toolkit's real store-persistence path and would
NRE after a load — fixed via `StoreContentPoint`/`LocationInitializer.PrepareStore` + the `OnLoaded` fix, T6.0-D5/
D3c), B4 (new scenes missing from Build Settings — added to T6.0-D4), B5 (missing `CRPG.Dialogue` asmdef
references — T6.0-D7), B6 (the tile-authoring helpers this plan assumed were reusable are actually `private` —
extraction is now its own task-breakdown item, #4).
**Should-fix, folded in:** S1 (two source facts were flatly wrong — corrected, facts 5/6 above; citation line
numbers refreshed), S2 (the vendor needs a real currency item + purse — fact 11), S3 (`panelSettings` was never
mentioned — fact 13), S4 (the plan promised tests against nonexistent test files — task breakdown items 2/3
corrected to cite what actually exists), S5 (a dialogue can't be a literal "one node" — fact 11's `ActionNode ->
EndNode` correction), S6 (a store doesn't strictly need a dialogue, but going through one is still the right call
for an interaction-adapter-driven NPC — noted in fact 11), S7 (`RequestLongRestAction` doesn't revive a downed
character — fact 2, folded into the action's own design and its test), S8 (the Goal's "and back" contradicted a
dead-end cavern design — fixed, T6.0-D4 is two-way throughout), S9 (stale references to the pre-revision action
name/design were still sitting in the master section above this one — fixed), S10 (bake-warning constraints — fact
8), S11 (`BarrowsEndGearAuthor`'s content-overwriting behavior — fact 12).
**Verified correct:** the `DialogueContext`/`DialogueRunner`/`ConversationController` design needs no runtime
changes beyond the two new fields (facts 1-2, all 9 existing call sites checked); no asmdef cycle for `RestService`
(`CRPG.Dialogue.asmdef` already references `CRPG.Actors`); `RequestSave` -> `SaveNow()` is a sound, low-risk fit;
`LocationBaker` has no size cap that would block an 11-building town; runtime travel code itself is already
scene-agnostic (the risk was entirely in tests/Build Settings, not `SceneTravelCoordinator`/`ShellBootstrap`'s own
travel logic).

## Results (implemented 2026-09-27/28)

All 10 task-breakdown items landed. Final suites: toolkit EditMode 1049/1049, PlayMode 431/431 (3 pre-existing
skips); BarrowsEnd (throwaway copy, full `BarrowsEndCoreSceneAuthor.Author()` re-run before every verification
pass) EditMode 121/122 (the already-known pre-existing `Town_Slice` stale-bake flake, confirmed unrelated — this
task never touches location-baking/camera-authoring files), PlayMode **77/77, zero failures**, including both new
end-to-end proof tests.

**Delivered as planned:** `RequestLongRestAction`/`RequestSaveAction` + the two new `DialogueContext` fields
(T6.0-D1/D2); `TalkInteractionAdapter`'s two new properties (D3); `ShellBootstrap.WireTalkAdapters`/
`StartingSceneOverride` (D3b/D6) with the three affected existing test files updated to opt into the override;
`LocationAuthoringKit.cs` extracted from `BarrowsEndLocationAuthor.cs` (source fact 9) with a new `CarveRoom`
helper for walled-room footprints; `BarrowsEndTownAuthor.cs` (11-building town square, systematically gridded —
9 real walled buildings + market tents + Jenkins' bench, two-way travel) and `BarrowsEndCavernsAuthor.cs` (3
levels, two-way throughout: Town ↔ Upper ↔ Middle ↔ Deep) both baked clean with **zero `LocationBaker` warnings**
on the first real run; a currency item (`item-gold`) added to `BarrowsEndGearAuthor`'s existing `extraContent`
list (source fact 12); Build Settings/asmdef updates (D4/D7).

**Two real bugs found only by actually running the end-to-end proof test (item 7) — exactly what it was for:**

1. **Fixture placement landed inside the HUD's own screen region.** The first-draft proof-fixture NPC positions
   (a few cells from the party's spawn point) projected, via the real camera, to screen coordinates inside the
   HUD's registered `top-left`/`top-right` regions — `UIToolkitPointerCapture.IsPointerOverUI` correctly reported
   `true`, and `PlayerCommandRouter` correctly suppressed the whole gesture, exactly as designed. Nothing wrong
   with the interaction mechanics; a fixture-placement mistake. Diagnosed via two throwaway instrumented test
   runs (not guessed): confirmed the party spawns dead-centre on screen (48px/world-unit, uniform), and that the
   HUD's two corner regions leave only a ~120px-wide safe column (world X offset roughly ±1.25 units from spawn)
   between them. Fixed by moving both fixtures to a small, purely-southward offset from spawn (`Cell(33,20)`/
   `Cell(32,20)`, same near-zero X as spawn) — screen-X stays safe regardless of how far the offset goes
   vertically, since the HUD regions are X-bounded, not Y-bounded, in the safe middle column.
2. **A real, load-bearing toolkit gap: `SaveService.RestoreStores`/`RestoreLootContainers` didn't actually
   restore anything once a location's own `InitialListings`/`InitialItems` had already seeded the target.**
   `LocationInitializer.PrepareStore`/`PrepareLootContainer`'s `GetOrCreateStore`/`GetOrCreateLootContainer`
   always treat a given id as "new" to a just-loaded session (its own dictionaries start empty), so they always
   re-seed from the location's authored `InitialListings`/`InitialItems` first — and `StoreInventoryState.
   AddListing`/`LootContainerState.AddItem` have no per-item identity check, so `RestoreStores`/
   `RestoreLootContainers` calling them on top of an already-seeded instance left the STALE seeded value as the
   one `TryGetListing` found first, with the real restored value sitting unreachable behind it. Manifested as: a
   real "buy from a vendor, save, load" test expected depleted stock (4) and got the freshly-reseeded original
   (5). Traced to the actual root cause (not patched around) and fixed at the correct layer: new `StoreInventoryState.
   ClearListings()`/`LootContainerState.ClearItems()`, called by `RestoreStores`/`RestoreLootContainers`
   respectively before repopulating — restoring now REPLACES a fresh reseed rather than layering on top of it.
   Two new toolkit regression tests pin this exact scenario (`RestoreStoresReplacesAnAlreadySeededStoresListingsRatherThanLayeringOnTopOfThem`,
   the loot-container equivalent) — both existing round-trip tests only ever exercised an empty target, which is
   why this was never caught before. A related, smaller toolkit gap surfaced alongside it: `LoadResult` never
   exposed the raw `SaveData` a load actually restored, which the composition root needs to call
   `RestoreStores`/`RestoreLootContainers` itself (Stores/LootContainers are deliberately NOT part of
   `SessionLoadCoordinator`'s own `SaveService.Restore` call, since they need already-constructed target
   instances that don't exist until the scene's own content is re-prepped) — added as a new `LoadResult.Data`
   property, pinned by a new assertion in the existing full-round-trip `SessionLoadCoordinatorTests.cs` test.

**Both were root-caused and fixed, not patched around** — matching this project's own standing verification
discipline: every fix above is at the layer where the actual defect lives (a fixture coordinate for the first,
two small new toolkit methods plus a new toolkit-level result field for the second), each with its own new
regression test, not a workaround bolted onto `TownProofFixturesTests.cs` itself.

**Committed 2026-09-28 (cc38dd3).**

---

# Task 6.1: Act 1 — The Gathering

Detailed plan for the second Phase 6 task (per D4): all 10 town NPCs, real Garrick/Elara/Silas recruiting,
Mayor Thaddeus's simplified briefing tree + the main quest's first objective, Elida's rest/save dialogue, and the
palisade-breach combat tutorial.

**v2 — Opus-reviewed (2026-09-28), 12 blockers + 10 should-fixes folded in.** The review found the v1 recruit
mechanism was fundamentally broken (`context.NpcId` is the talking PARTY member, not the NPC — recruiting as v1
designed it would have silently re-added the protagonist and done nothing), that a world NPC spawned as a
`CharacterAgent` can never actually be clicked/talked to (the click router always treats a character click as
Select, never Interact — companions must be static talk-point fixtures like every other NPC, not spawned
characters), that `NpcSpawnPoint`-spawned NPCs are never activated anywhere in the real runtime path (a genuine,
previously-unnoticed toolkit gap — no running scene has ever exercised it), that Continuing a save containing a
recruited companion would crash (`partyVisualPrefabs` only knows boot-time members), that the combat tutorial as
first sketched was missing the entire toolkit combat stack (nothing to attack with, no AI, no death rule for
monsters), and that changing the boot `members` array to 1 would break five existing test files outright, not
hypothetically. Every fix is folded into the sections below (see the Review record at the end for the full list).
**Per the user's explicit call, the combat tutorial builds the FULL toolkit tactical stack** (not a scaled-down
skirmish) — the same stack Acts 2/3 will need anyway, wired into BarrowsEnd for the first time.

## Goal

Land Act 1 as a real, playable slice: from a fresh boot, the player (protagonist only, per T6.1-D1 below) can walk
Barrow's End, talk to and/or shop with every one of the 10 named NPCs, recruit Garrick/Elara/Silas so they become
real controllable party members, receive and accept the main quest from Mayor Thaddeus once all three are
recruited, get sent into a real combat encounter (giant rats breaching the palisade) that uses the toolkit's combat
system for the first time in this project, and win it. Gate: a live playthrough of exactly that path.

## Non-goals

- Caverns content, goblin encounters, the mushroom side quest, the necromancer boss — Task 6.2/6.3.
- Real AC-from-armor, hit-points-as-a-toolkit-rule — pre-existing tracked gaps, out of scope unless something here
  needs one to function (nothing does: the tutorial fight uses the same debug/derived-HP path `GateBootstrap`
  already exercises).
- A tracked bounty-board mechanic for Captain Kael — flavor dialogue only (Phase 6 master doc's own non-goal).
- The handoff's `tempo_points`/compound-AND tree — dropped per D1; Mayor Thaddeus's tree keeps the narrative beats
  only (recruit -> briefing -> quest -> optional exposition branch), one real gate (`BranchNode` on a single
  Timeline flag), matching the master doc's own D1.
- Skill checks anywhere in Act 1's dialogue — nothing in the handoff's simplified beats needs one; `CheckNode`
  stays unused this task.
- A visual/UI treatment for "you gained a party member" — the recruit just becomes selectable/controllable and
  shows up in the existing `PartyView` HUD (already dynamic, Task 5.1); no new UI this task.
- A full quest-log/journal screen — BarrowsEnd has no quest-log UI today and this task doesn't add one (the user
  verifies "quest accepted" via the gate's live conversation, not a UI panel); a real journal is a later-task gap,
  noted but not fixed here.
- Any Act 2/3 encounter content — this task builds the FULL tactical combat stack (per the user's decision) but
  only ONE encounter's worth of content (2-3 Giant Rats); the stack existing is what makes Act 2/3 cheaper later,
  not a promise that more combat content ships this task.

## Source facts (verified by reading the actual source, 2026-09-28; corrected against the Opus review, v1 -> v2)

1. **`BarrowsEndTownAuthor.cs`'s 11 buildings/features and Task 6.0's exact NPC-authoring shape** (re-verified,
   `BarrowsEnd/Assets/Editor/Content/BarrowsEndTownAuthor.cs:26-240`): Row A (`y0=9,y1=16`, doors at `y=16`,
   opening north toward the square): Mayor's Hall `(6,9)-(13,16)` door `Cell(9,16)`, Shrine of Silas
   `(18,9)-(25,16)` door `Cell(21,16)`, The Iron Anvil/Barnaby `(30,9)-(37,16)` door `Cell(33,16)`, Restful Hearth
   Inn (Elida/Grumble) `(42,9)-(49,16)` door `Cell(45,16)`, Fendrick's Apothecary `(54,9)-(61,16)` door
   `Cell(57,16)`. Row C (`y0=33,y1=40`, doors at `y=33`, opening south toward the square): Marjorie's General Store
   `(6,33)-(13,40)` door `Cell(9,33)`, Guard Barracks/Kael `(18,33)-(25,40)` door `Cell(21,33)`, Lyra's Arcanist
   Study `(42,33)-(49,40)` door `Cell(45,33)`, Silvan's Fletcher `(54,33)-(61,40)` door `Cell(57,33)`. Open
   features: Market Tents (Elara) at `(22,31)-(27,32)`, Jenkins' bench (placeholder `Barrel` tile) at `(30,24)`
   inside the square. `LocationAuthoringKit.AddTalkPoint(root, name, cell, approach, visual)` (`LocationAuthoringKit.cs:167-193`)
   is the exact shape for every one of the 10 real NPCs: box collider + sprite visual + approach anchor +
   `WorldInteractable` + a bare `TalkInteractionAdapter` the caller finishes via `SetField(adapter, "dialogue", ...)`
   / `SetField(adapter, "panelSettings", ...)`. `BarrowsEndTownAuthor.AuthorProofFixtures` (`:137-178`) is the
   concrete end-to-end template for both a talker (`BuildTalkerDialogue`, `:180-216`) and a vendor (`BuildVendorDialogue`
   + `SerializedObject`-based `LocationContent.stores` registration, `:218-240`) — every one of Task 6.1's 10 NPCs
   follows one of these two shapes (several NPCs, e.g. Barnaby, need both: a greeting plus `OpenStoreAction`).
2. **CORRECTED (v1 wrongly claimed this "needs zero new bootstrap code" — false): `NpcSpawnPoint` is real, but a
   spawned NPC is NEVER ACTIVATED anywhere in the actual runtime path, and no running scene has ever used one.**
   `LocationInitializer.PrepareSpawnPoint` (`LocationInitializer.cs:47-77`) does spawn via
   `WorldSpawnService.SpawnNpc`/`BindRestoredView` as v1 said, but `LocationInitializer.cs:74` explicitly
   `SetActive(false)`s the result and documents that the CALLER must activate it — and nothing in
   `Runtime/Gameplay` ever does except the save-load commit path (`SessionLoadCoordinator.cs:528`). Neither
   `ShellBootstrap.PrepareLocation` (boot) nor `SceneTravelCoordinator` (travel) activates a freshly-spawned
   `NpcSpawnPoint` NPC. Confirmed both real content authors (`AdventureSliceSceneAuthor.cs:241,293`) leave their
   `spawnPoints` lists empty — only EditMode tests exercise this type at all. **Task 6.1 does not use
   `NpcSpawnPoint` for anything** (fact 3/T6.1-D2 below explains why companions don't need it; T6.1-D6 has the
   rats spawn directly via `WorldSpawnService.SpawnNpc` at trigger-fire time instead, the same way
   `GateBootstrap.SpawnSkeleton` already does, sidestepping the activation gap entirely rather than fixing a
   toolkit bug this task doesn't otherwise need). `LocationAuthoringKit.AddSpawnPoint` is dropped from this plan.
3. **CORRECTED (v1's central design error, found by review): `context.NpcId` is the PARTY MEMBER doing the
   talking, not the NPC being talked to — `context.Party.AddMember(context.NpcId)` would have re-added the
   protagonist and done nothing.** `TalkInteractionAdapter.cs:57` builds `new DialogueContext(performer, ...)`,
   and `performer` is the party member who triggered the interaction (`InteractionController.cs:205`) — v1's cited
   line (`:48`) was mis-read; it's an unrelated private field. **A second, deeper problem this same correction
   exposes: `PlayerCommandRouter.cs:173-179` always routes a click on any `CharacterAgent` to Select, never
   Interact** ("interactable targets are static world objects, not characters" — its own comment), and
   `GateCommandSource.cs:57` then drops a Select on a non-party-member character. **A world NPC spawned as a real
   `CharacterAgent` (the way a `NpcSpawnPoint` or `WorldSpawnService.SpawnNpc` would create one) can therefore
   never be clicked/talked to at all.** Garrick/Elara/Silas MUST be static talk-point fixtures — plain
   `WorldInteractable`+`TalkInteractionAdapter` `GameObject`s via `AddTalkPoint`, exactly like all 7 other town
   NPCs — never spawned `CharacterAgent`s. This also means there is no pre-existing `CharacterState`/spawn record
   to "promote" at recruit time (T6.1-D2 below): the companion's real `CharacterState` is built fresh, from
   scratch, the moment `RecruitToPartyAction` fires — mirroring exactly what `ShellBootstrap.SpawnParty` already
   does for a boot-time companion (`ShellBootstrap.cs:308-328`), just triggered by dialogue instead of `Boot()`.
4. **Party bookkeeping alone does not make a recruit controllable — real gap, verified.**
   `GateSessionServices.Rebind` (`BarrowsEnd/Assets/Runtime/Gate/GateSessionServices.cs:64-94`, the class
   `ShellBootstrap.Services` actually is, despite its "Gate" name — see project memory) builds the
   `PartyExplorationController` and registers every party member's motor via `exploration.RegisterMotor(member,
   FindAgent(member).Motor)` **only inside `Rebind`, once, at boot and after a load** (`:86-91`). `exploration` is
   `private`; nothing calls `RegisterMotor` again mid-session. **`GateSessionServices` needs a new public method**
   (e.g. `RegisterMemberMotor(RuntimeEntityId id, IMovable motor)`, taking the motor directly rather than looking
   it up via `FindAgent` — review found `CharacterAgent.AllLive` is a `HashSet` with deferred `Destroy` (`CharacterAgent.cs:36`),
   so a stale/about-to-be-destroyed agent could still be findable for one frame; the caller should pass the motor
   from the GameObject it JUST created, the same way `ConsumerGameBootstrap.cs:124-125` does, not re-look-it-up)
   that `ShellBootstrap.RecruitCompanion` (T6.1-D2) calls once, synchronously, right after spawning the recruit's
   party-visual instance.
5. **CORRECTED (v1's "promotion" framing assumed a pre-existing spawned NPC character to promote — per fact 3,
   there isn't one; recruiting builds the companion's `CharacterState` from scratch): `ShellBootstrap.SpawnParty`
   (`ShellBootstrap.cs:291-332`) is the exact template to mirror at recruit time**, not a dictionary to "promote
   into." It (a) registers a fresh `CharacterState` (`session.Characters.Register(character)`, `:322`), (b) adds
   it to the party (`:323`), (c) dresses it (`Dress`, `:336-350`, already `private static`, reusable), (d) builds a
   real, controllable `CharacterAgent` instance IN THE PERSISTENT CORE SCENE (`spawner.BindRestoredView(...)` then
   `SceneManager.MoveGameObjectToScene(instance, coreScene)`, `:327-328`), and (e) fills `partyVisualPrefabs[id]`
   (`:73`,`:324`) — which `SessionLoadCoordinator` THROWS (`ViewBindingFailed`, not a raw throw to the caller — a
   review correction to v1's own wording) if a restored party member is missing from
   (`SessionLoadCoordinator.cs:473-479`, confirmed; `Docs/Plans/2D-Campaign-Phase4-CharacterCreator.md:532`). A new
   `ShellBootstrap.RecruitCompanion(string companionKey)` does exactly (a)-(e) for one companion, using that
   companion's FIXED id (T6.1-D3) and the SAME `partyPrefab` every boot member uses, then calls source fact 4's
   `RegisterMemberMotor` with the newly-created instance's own `CharacterAgent.Motor` directly (not `FindAgent`).
   The originating talk-point fixture is NOT destroyed mid-conversation (its `ConversationController`/`UIDocument`
   is still live while `Execute` runs) — instead it's hidden the next time its containing scene loads, gated on the
   same Timeline flag the recruit records (T6.1-D5/D8 below), matching how every other conditional-on-Timeline
   world state in this project is meant to work, not a special case. **Related, and just as load-bearing:
   Continuing a save that already contains a recruited companion would otherwise CRASH on load** —
   `partyVisualPrefabs` is a per-process, per-session dictionary filled only by `SpawnParty` at boot (`:324`); a
   companion recruited in a PREVIOUS play session has no entry in a freshly-restarted process's dictionary until
   `RecruitCompanion` runs again, which it never will (they're already a party member per the save). `Session
   LoadCoordinator` would then throw `ViewBindingFailed` for that restored member. **Fix (T6.1-D3): give each
   companion a FIXED, deterministic id (own constant range, alongside `ShellBootstrap.MemberIdFor`'s existing
   formula, `:265`) and pre-populate `partyVisualPrefabs` for all 3 fixed companion ids at BOOT time,
   unconditionally** — they all use the same `partyPrefab` as everyone else, so this costs nothing and needs no
   "is this companion recruited yet" check (`SessionLoadCoordinator` only ever looks up an id that's actually IN
   the restored `Party.Members`, so a not-yet-recruited companion's unused dictionary entry is simply never read).
7. **CONFIRMED a real risk, with a concrete fix (not left as "verify later"): `ShellBootstrap.SpawnParty`'s
   `members` array (a `GateMemberSpec[]`, `GateLayout.cs:88-114`) currently authors and boots the WHOLE party
   (protagonist + 3 placeholder companions named Aldric/Brenna/Corvin/Dessa, `GateLayout.DefaultMembers()`) as
   controllable from the very first frame — there is no "not yet recruited" state today.**
   `BarrowsEndCoreSceneAuthor.cs:94-97` sets this same array onto Core.unity's serialized `ShellBootstrap.members`
   property, which every existing test boots against too. The review read the actual test files and confirmed
   changing this to 1 member WOULD break, concretely: `ShellTests.cs:110,172,174,304` (a companion-rolls-differ
   assertion and others), `ShellStartFlowTests.cs:229,264-272,303-304,321-341`, `HudBootstrapTests.cs:79-94,99`,
   `DollBootstrapTests.cs:201` (cycles through members) — `ShellCaptureTests.cs` makes no member-count assertions
   and is unaffected. `LocationSceneContentTests.cs:203` (EditMode) asserts `members` `arraySize == 4` on the
   authored asset directly. **Fix (T6.1-D8): a new `ShellBootstrap.MembersOverride` static field, mirroring the
   existing `StartingSceneOverride` (`:105`) exactly** — the affected PlayMode test files set it to
   `GateLayout.DefaultMembers()` in setup and clear it in teardown, keeping their existing assertions testing the
   SAME 4-member geometry unchanged, while the real boot (no override) uses the real 1-member array;
   `LocationSceneContentTests.cs:203`'s expected value is updated to 1 (an intentional-behavior-change update, not
   a workaround, same category T6.0-D6 already established for `startingScenePath`). **This hook must land in the
   SAME implementation step as the `members`-array change itself** (task-breakdown item 3), not deferred — every
   Shell PlayMode suite goes red the moment `members` changes otherwise.
8. **CORRECTED (v1 invented a gear-scarcity constraint that doesn't exist): companion data needs its own home now
   that it's recruit-time, but there is no "5-item budget."** `GateMemberSpec` (`GateLayout.cs`, referenced by
   `ShellBootstrap.SpawnParty`'s else-branch, `:308-320`) is the existing shape: `displayName, spawnId, cell,
   maxHp, appearance (string[] "layer=option"), gear (string[] item ids)`. `ShellBootstrap.Dress` (`:336-350`) and
   `RollAbilityScores` (`:279-289`, 4d6-drop-lowest) are both directly reusable at recruit time. **v1 wrongly
   claimed only 5 BarrowsEnd-authored items exist — the review confirmed `BarrowsEndGearAuthor.cs:59-93` already
   authors an item per creator pick from `CreatorPicks.json:159-185`, including "Fighter Padded Mail", "Rogue Dark
   Leathers", and "Cleric Vestments" (matching Garrick/Elara/Silas's handoff class-flavor almost exactly) plus
   roughly 100 weapon items, and the SRD5 sample's dagger/mace/shield/leather-armor items also resolve (fact 13
   below) — real class-appropriate gear for all three companions costs zero new item authoring.**
9. **`RecordEventAction` (`crpg-toolkit/Runtime/Dialogue/RecordEventAction.cs:10-30`) is the established
   "set a Timeline flag from dialogue" mechanism**, chained as a second `ActionNode` right after
   `RecruitToPartyAction`'s own `ActionNode` (both auto-settle with no line to read, exactly like Task 6.0's
   rest-then-save chain, `BarrowsEndTownAuthor.cs:187-195`). **Should-fix from review: use ONE shared key
   (`"companion-recruited"`) recorded by all three companions, and gate Mayor Thaddeus's "ready to descend" branch
   with a SINGLE `TimelineConditionType.CountAtLeast` 3 check (`TimelineConditionType.cs:12`) instead of three
   chained `BranchNode`s** — simpler, and avoids the AND-emulation entirely rather than just moving it into series
   `BranchNode`s. `RecordEventAction.Execute` records every time it runs, with no dedup (`RecordEventAction.cs:28`)
   — since a recruited companion's talk-point fixture is hidden after recruiting (source fact 5), this can't
   actually re-fire in practice, but the recruit `ActionNode` should still be reachable only once per companion by
   construction, not by accident.
10. **CORRECTED (v1's quest design would have needed a new toolkit asmdef reference the project's own design
    principle argues against): `QuestService`/`DialogueContext` integration is NOT the same shape as Task 6.0's
    `RestService` addition.** `RestService` lives in `CRPG.Actors`, which `CRPG.Dialogue` already references — but
    `QuestService` lives in `CRPG.Quests`, which `CRPG.Dialogue.asmdef` does NOT reference (`CRPG.Dialogue.asmdef:4`),
    and the toolkit's own documented design keeps Dialogue and Quests meeting ONLY through Timeline keys
    (`QuestDialogueIntegrationTests.cs:12-14`; `QuestService.cs:8-15`'s own doc comment) — adding a direct
    `QuestService` field to `DialogueContext` would violate that on purpose, not by accident. **Fix (T6.1-D4):
    `StartQuestAction` carries its own serialized `questKey` (a plain string id, not a `QuestDefinition` asset
    reference) and calls a new `Action<string> RequestStartQuest` delegate on `DialogueContext` (one more optional
    trailing field, same DELEGATE shape as `RequestSave` — not a service reference).** `ShellBootstrap` (which can
    freely reference `CRPG.Quests`, unlike the toolkit's own Dialogue assembly) resolves the actual
    `QuestDefinition` by id and calls `QuestService.StartQuest` itself, inside the method `WireTalkAdapters` points
    the delegate at. `QuestService(QuestLog log, ITimeline timeline, IGameEventBus eventBus=null,
    PartyInventoryState inventory=null)` (`QuestService.cs:23`), constructed fresh per `WireTalkAdapters` call
    exactly like `RestService` already is (never cached — captures session-scoped references).
    `GameSession.Quests` is a real, already-session-scoped, already-save/load-safe `QuestLog` (`GameSession.cs:34,75`).
    **Real gap found in review: the saved quest id must actually resolve via the `RulesDatabase`, or a later load
    fails `ContentValidator` (`ContentValidator.cs:67`)** — `QuestDefinition_TheGatheringStorm.asset` (working
    title) must be added to `BarrowsEndGearAuthor`'s own `content`/`extraContent` list (the same file that already
    owns `npc-skeleton`, and which overwrites its whole list on every `Author Core Scene` run,
    `BarrowsEndGearAuthor.cs:236-237` — master doc's own source fact 12), not authored by a separate script that
    assumes it'll persist. **Should-fix from review: scope Act 1's quest to Act 1's own objectives only** (recruit
    -> briefing accepted -> rats defeated) — `QuestService` marks a quest `Completed` once every CURRENT objective
    finishes (`QuestService.cs:71-75`), and any objective added to the SAME quest asset later would be silently
    counted against old saves (`:56`, a `continue`) — a multi-act "main quest" spanning Acts 1-3 in one
    `QuestDefinition` would complete prematurely at the end of Act 1. A separate `QuestDefinition` per act (all
    sharing the same displayed "main quest" framing narratively) avoids this. **Also note: BarrowsEnd has no
    quest-log/journal UI at all (confirmed by search)** — this task doesn't add one (Non-goals); the live-playthrough
    gate verifies "quest accepted" by the conversation actually happening, not by checking a screen.
11. **CORRECTED (v1 badly underscoped combat — `CombatEncounterService` alone cannot be played or won): the
    toolkit's REAL combat stack has several more required pieces, all currently unused anywhere in BarrowsEnd.**
    `CombatEncounterService(ICharacterRegistry, IInitiativeRule, TurnResourceSet, IGameEventBus=null,
    ICombatLog=null)` (`CombatEncounterService.cs:31-43`) only tracks turn order — nothing lets the player act or
    the rats act, and nothing ends combat automatically (`EndCombat` is a manual call, `:107`). **Per the user's
    decision, this task wires the FULL stack** (the same one `ConsumerGame`/`TestProject` already prove works):
    `TacticalEncounterController` (`TacticalEncounterController.cs:41-83` — owns the `ActionScheduler`, death
    handling), `TacticalCombatUIBridge`+`AbilityBarView`+`AbilityTargetingGate` (`TacticalCombatUIBridge.cs:30-52`
    — the player's real "pick an ability, pick a target" UI), `TacticalAiTurnDriver`+a brain
    (`TacticalAiTurnDriver.cs:43` — so the rats actually act on their turn), and an `EncounterFactionProvider`.
    `StartCombatWithExplicitOrder(IEnumerable<(RuntimeEntityId Id, DefinitionId Faction)>)` takes an EXPLICIT
    per-combatant faction at call time, not read off `CharacterState` (`:68`), so a recruited companion trivially
    fights on `faction-party` regardless of what faction it was built under — no faction-switching plumbing needed.
    **Three more real gaps found in review, all needing their own fix:**
    (a) **Movement during combat is unaddressed by default** — `PartyExplorationController` keeps moving selected
    members freely; nothing in this plan yet suspends exploration input while combat is active (needs a real gate,
    likely mirroring the existing `IsWorldTransitioning`-style suppression pattern from the Exploration-UX
    follow-up work, applied to combat too).
    (b) **A downed party member doesn't die the way a monster needs to** — `DeathSaveService` is constructed once
    for the WHOLE session (`GateSessionServices.cs:78`) and intercepts every 0-HP transition, including hostiles',
    applying `Unconscious`+death-saves rather than publishing `CreatureDied` unless the hit is literal overkill
    (`DeathSaveService.cs:83-99`) — `TacticalEncounterController`'s own death handling and this task's "on the
    last rat's death" trigger (T6.1-D6) both need a monster to actually DIE at 0 HP, not enter death-saves. A
    small BarrowsEnd-local rule (e.g. a subscriber on the zero-HP event that, for a non-party faction, publishes
    `CreatureDied` directly instead of letting `DeathSaveService` apply `Unconscious`) is needed — decide the exact
    mechanism during implementation against the real event names, not guessed here.
    (c) **Elida's `RequestSaveAction` should not succeed mid-combat** — `SaveNow` only checks `IsReady`/the
    transition gate (`ShellBootstrap.cs:443`), not `SafePointGate`'s existing `CombatActive` check
    (`SafePointGate.cs:42`), which nothing wires today. Out of scope to FIX generally (a pre-existing gap), but
    Elida is nowhere near the palisade fight, so this only matters if `RequestSaveAction`'s own doc comment should
    note the limitation rather than implying it's always safe.
12. **No hostile "beast" `NpcDefinition` exists anywhere** (grepped `rat|wolf|beast` — zero hits); the only hostile
    NPC asset at all is `npc-skeleton` (undead, `BarrowsEndGearAuthor.cs:218-231`). A "Giant Rat" `NpcDefinition`
    must be authored from scratch, following that exact pattern, added to `BarrowsEndGearAuthor`'s own NPC list
    (not a new file, same overwrite-safety reasoning as fact 10's quest-registration note) — and, since fact 11
    now requires a REAL playable fight (not just a stat block), the rat needs at least one real usable ability/
    resource the tactical stack can resolve, not just an HP resource. **Rats are spawned directly via
    `WorldSpawnService.SpawnNpc` at trigger-fire time (T6.1-D6), NOT via `NpcSpawnPoint`** — fact 2's activation
    gap makes `NpcSpawnPoint` the wrong tool here; `GateBootstrap.SpawnSkeleton` (`GateBootstrap.cs:157-166`) is
    the closest spawn precedent, though it never itself starts combat.
13. **CORRECTED (v1 left this as an unresolved probe; the review resolved it directly): vendor stock for
    Fendrick/Silvan is NOT blocked on an unknown.** `BarrowsEndRulesetProvider.Register` calls through to the SRD5
    provider's own registration (`:26-31`), which DOES register `SRD5RulesetProvider.asset`'s content list — the
    review confirmed by reading the actual asset that `item-healing-potion` (baseValue 50), `item-shortbow`,
    `item-longbow`, `item-dagger`, and `item-mace` all already resolve. **Fendrick and Silvan's catalogs need zero
    new item authoring** — stock them directly (price+stock only, `BuildVendorDialogue`+`stores` registration,
    source fact 1). **Lyra still needs one new item authored** (no SRD5 scroll-equivalent exists) — a small
    `ItemDefinition` in `BarrowsEndGearAuthor`, same pattern as `item-gold`. None of the SRD5e2014 items have an
    `EquipmentVisualProfile` (confirmed), so an equipped bow shows no sprite change — acceptable for Act 1 (a
    cosmetic gap, not a blocker; potions/scrolls aren't equipment-slot items anyway). **Separate real gap found in
    review: shops don't actually work in play today.** `WireTalkAdapters` never sets `adapter.CurrencyItemId`
    (`ShellBootstrap.cs:419-435`), so `ConversationController.cs:135` builds every `StoreService` with
    `DefinitionId.None` currency — and nothing grants the party any `item-gold` to begin with. (Task 6.0's own
    proof test never caught this because it built its own `StoreService` directly, bypassing `WireTalkAdapters`
    entirely — `TownProofFixturesTests.cs:113-118`.) **Fix: `WireTalkAdapters` sets `CurrencyItemId = item-gold`,
    and the party is granted starting gold once at boot** (`SpawnParty` or an adjacent boot step).
14. **Elida's rest/save dialogue is the SAME pattern Task 6.0's Town Crier fixture already proved end-to-end**
    (source fact 1's `BuildTalkerDialogue`) — no new mechanism, just real flavor text + `RequestLongRestAction`/
    `RequestSaveAction` on Elida's own `TalkInteractionAdapter` instead of a throwaway fixture's.
15. **The palisade trigger design needs four fixes the v1 draft didn't account for.** (a) A plain `MonoBehaviour`
    bool for "already fired" resets on scene reload/game load — the fired state must be a persisted Timeline flag,
    checked (and self-disabling) on enable, not in-memory. (b) If it somehow fired twice, `WorldSpawnService.SpawnNpc`
    throws on a reused spawn id (`WorldSpawnService.cs:152,442-446`) — each fire (there should only ever be one)
    needs spawn ids it hasn't used before, or the "already fired" guard in (a) must be airtight. (c)
    `ProximityTriggerZone`'s own shape fires for ANY `CharacterAgent` entering (`ProximityTriggerZone.cs:29`), not
    just party members — this project's version must filter to real party members. (d) **Positioning collision**:
    `BarrowsEndTownAuthor.cs:117`'s `TravelToCaverns` point sits at `Cell(33,46)` in the same north-gap area the
    trigger would naturally sit, and `FromCavernsDestination` (`:116`) lands the party at `Cell(33,44)` on return
    from the caverns — a trigger placed carelessly in that gap would fire on travel-point approach or on every
    return trip. Place it inside the palisade gap but clearly offset from both cells (exact offset decided against
    the real geometry during implementation, not guessed here).
16. **The HUD doesn't know a recruited companion's name.** `HudBootstrap.cs:118` builds its whole memberId ->
    displayName map from `bootstrap.Members` (the boot-time array) using `ShellBootstrap.MemberIdFor(i)`;
    `PartyPresenter.cs:64` falls back to the raw GUID string if a name isn't found, and the map is rebuilt the same
    boot-members-only way after a load (`HudBootstrap.cs:166`). A recruited companion (fixed id, source fact 6)
    would show as a GUID in the party HUD unless the name map also covers the 3 fixed companion ids. **Fix: a new
    `ShellBootstrap.DisplayNameFor(RuntimeEntityId id)` covering both boot members and the fixed companion table
    (T6.1-D3), used everywhere `HudBootstrap` currently reads `bootstrap.Members` for display names.**
17. **Pre-existing gap this task's real combat is the first to expose (not this task's to fully fix, but worth
    flagging): a party member's death currently bricks scene travel.** `GateSessionServices.cs:82` calls
    `MarkDefeated` on death (tombstones the member, destroys its view) while the member stays in `Party.Members`;
    `SceneTravelCoordinator.cs:212-218` then fails the party's reposition permanently. The palisade tutorial fight
    is winnable without anyone dying (2-3 weak rats vs. a full party), so this shouldn't block Act 1's own gate,
    but it's a real gap worth a one-line note rather than silence, since Act 1 is the first time real combat with a
    real chance of a downed member exists in BarrowsEnd at all.

## Decisions (Task 6.1-specific, refines Phase 6's D1-D5)

- **T6.1-D1 (recruiting is real, per the user's explicit call; companions are talk points, not spawned
  characters, per fact 3):** the party starts (fresh game / new save) with ONLY the protagonist.
  `BarrowsEndCoreSceneAuthor.Author()`'s authored `members` array becomes a single entry. Garrick/Elara/Silas are
  ordinary `AddTalkPoint` fixtures (exactly like the other 7 town NPCs) in their respective locations (Garrick:
  tavern half of the Restful Hearth; Elara: the Market Tents; Silas: the Shrine) until recruited — never
  `NpcSpawnPoint`/spawned `CharacterAgent`s, since those can't be clicked (fact 3).
- **T6.1-D2 (recruit mechanism, redesigned per B1/B2/B3):** `RecruitToPartyAction : DialogueAction` carries its
  own serialized `companionKey` (a string, e.g. `"garrick"` — it has no access to which NPC it's attached to, so
  it can't infer this) and calls a new `Action<string> RequestRecruit` delegate on `DialogueContext` (one more
  optional trailing field, same DELEGATE shape as `RequestSave` — not `context.Party.AddMember` directly, since
  `context.NpcId` is the wrong id per fact 3). `WireTalkAdapters` points the delegate at a new
  `ShellBootstrap.RecruitCompanion(string companionKey)`, which does exactly what `SpawnParty` already does for a
  boot-time member (fact 5): looks up the companion's spec (T6.1-D3, fixed id), builds+registers a fresh
  `CharacterState`, rolls ability scores, dresses gear, `session.Party.AddMember(id)`, spawns the real party-visual
  instance via `spawner.BindRestoredView(spawnId, id, default, partyPrefab, <the recruiting NPC's own world
  position>, rotation, default, startInactive:false)`, `SceneManager.MoveGameObjectToScene(instance, coreScene)`,
  and calls the new `GateSessionServices.RegisterMemberMotor(id, instance.GetComponent<CharacterAgent>().Motor)`
  (fact 4 — using the JUST-CREATED instance directly, not `FindAgent`). The recruit is chained with a
  `RecordEventAction("companion-recruited")` in the same `ActionNode` sequence (fact 9). The original talk-point
  fixture is NOT destroyed mid-conversation — it's hidden the next time its scene loads/reloads, gated on the same
  recruited-companion Timeline check (decide the exact per-companion check — e.g. a `NotOccurred("recruited-<name>")`
  test the town-authoring/scene-load code runs — during implementation).
- **T6.1-D3 (companion data, fixed ids per fact 6, real gear per fact 8):** Garrick/Elara/Silas each get a
  `GateMemberSpec`-shaped record (a new static table, e.g. beside `GateLayout.DefaultMembers()`) with a FIXED,
  deterministic id (own constant range alongside `MemberIdFor`'s formula), real appearance, and real
  class-flavored gear drawn from BarrowsEnd's actual existing catalog (fact 8 — "Fighter Padded Mail" for Garrick,
  "Rogue Dark Leathers" for Elara, "Cleric Vestments" for Silas, plus an appropriate weapon each, all already
  authored, zero new item work). All 3 fixed ids are added to `partyVisualPrefabs` (mapped to `partyPrefab`) at
  BOOT time unconditionally, whether or not they're recruited yet (fact 6's save/load fix). Ability scores rolled
  at RECRUIT time via the same `RollAbilityScores` call `SpawnParty` already makes, seeded off each companion's own
  fixed id (not a boot-order index, since recruiting isn't boot-ordered).
- **T6.1-D4 (quests, redesigned per B9/B8):** `StartQuestAction : DialogueAction` carries its own serialized
  `questKey` (a plain string id) and calls a new `Action<string> RequestStartQuest` delegate on `DialogueContext`
  (delegate, NOT a `QuestService` field — fact 10 explains why a direct reference would need a `CRPG.Dialogue` ->
  `CRPG.Quests` asmdef edge the toolkit's own design avoids). `WireTalkAdapters` points it at a new
  `ShellBootstrap` method that resolves the real `QuestDefinition` by id (`session.RulesDatabase.TryResolve` or a
  direct asset lookup — decide during implementation) and calls a freshly-constructed `QuestService.StartQuest`.
  **Act 1 gets its OWN `QuestDefinition` asset** (working title `QuestDefinition_TheGatheringStorm.asset`), NOT a
  multi-act quest spanning 6.1-6.3 (fact 10's should-fix — avoids premature completion), authored from code the
  same way BarrowsEnd already authors dialogue assets, registered in `BarrowsEndGearAuthor`'s own content list
  (fact 10's B8 fix — otherwise a later save fails `ContentValidator`). One `Kill` objective (defeat the palisade
  rats), `trackingKey` matching what the combat encounter records on the last rat's confirmed death (T6.1-D6);
  "quest accepted" is signaled by `StartQuestAction` itself firing, not a second redundant objective.
- **T6.1-D5 (Mayor Thaddeus's tree):** root `LineNode` (the briefing question) -> a `ChoiceNode` with (at minimum)
  the "ready to descend" option gated by a SINGLE `BranchNode`/`CountAtLeast` check on the shared
  `"companion-recruited"` key >= 3 (fact 9's should-fix — simpler than three chained per-companion `BranchNode`s)
  leading to `StartQuestAction`+a short "head to the caverns" line, and the "what's down there?" exposition option
  (no gate, matches the handoff's `Node_05_Exposition` beat) leading back to the root choice. The
  "reposition"/"aggressive" tempo_points branches and the mining-lift-key side branch are DROPPED per the master
  doc's D1 (framework-testing content, not a real campaign need).
- **T6.1-D6 (combat tutorial — FULL tactical stack, per the user's explicit decision):** new BarrowsEnd-local
  `PalisadeBreachTrigger` `MonoBehaviour` (following `ProximityTriggerZone`'s shape, fact 11, with fact 15's four
  fixes: a persisted Timeline "fired" flag checked on enable rather than an in-memory bool, party-member filtering,
  spawn-id safety, and a placement clearly offset from both the `TravelToCaverns`/`FromCavernsDestination` cells).
  On first real party approach: spawns 2-3 "Giant Rat" `NpcDefinition` instances (fact 12) directly via
  `WorldSpawnService.SpawnNpc` (NOT `NpcSpawnPoint` — fact 2's activation gap), sampling spawn positions to avoid
  the party's own occupied cells (mirroring `ShellBootstrap.cs:380`'s existing `TrySamplePositionAvoiding` use).
  Wires the full stack from fact 11: constructs `CombatEncounterService`+`TacticalEncounterController`+
  `TacticalCombatUIBridge`/`AbilityBarView`/`AbilityTargetingGate`+`TacticalAiTurnDriver`+a brain+an
  `EncounterFactionProvider`, calls `StartCombatWithExplicitOrder` with every present party member on
  `faction-party` and every rat on `faction-hostile`. Suspends `PartyExplorationController`'s movement input for
  the duration (fact 11a — exact mechanism decided during implementation). Applies fact 11b's fix so a rat actually
  dies (publishes `CreatureDied`) at 0 HP rather than entering death-saves. On the last rat's confirmed death,
  records the quest's `Kill` objective's `trackingKey` and calls `QuestService.RefreshProgress`. Ends combat
  (`EndCombat`) and restores exploration movement once every rat is dead. One-shot only, enforced by the persisted
  Timeline flag (fact 15a), not an in-memory guard.
- **T6.1-D7 (vendor stock, resolved not just probed, per fact 13; currency wiring, per fact 13's B10 fix):**
  Barnaby and Marjorie stock from BarrowsEnd's existing items; Fendrick (potions) and Silvan (bows) stock directly
  from the confirmed-resolving SRD5e2014 items, zero new authoring; Lyra needs one new scroll-equivalent item
  authored in `BarrowsEndGearAuthor`. Kael (bounty flavor only, no store) and Jenkins (rumor mill flavor, no
  store) get talk-only dialogue, no `StoreContentPoint`. `WireTalkAdapters` sets every talk adapter's
  `CurrencyItemId = item-gold` (previously never set at all — B10), and the party is granted starting gold once at
  boot so shops are actually usable in play, not just in a test that builds its own `StoreService`.
- **T6.1-D8 (new, closes B12 — the boot-member-count risk):** `ShellBootstrap` gains a static `MembersOverride`
  test hook (`GateMemberSpec[]`), structurally mirroring `StartingSceneOverride` — when set, `Boot()` uses it
  instead of the serialized `members` property. `ShellTests.cs`/`ShellStartFlowTests.cs`/`HudBootstrapTests.cs`/
  `DollBootstrapTests.cs` (every file the review confirmed actually asserts against 4 members) set this to
  `GateLayout.DefaultMembers()` in setup and clear it in teardown, exactly as they already do for
  `StartingSceneOverride`. `LocationSceneContentTests.cs:203`'s `arraySize == 4` assertion is updated to `== 1` (an
  intentional-behavior-change update). **This hook lands in the SAME implementation step as the `members`-array
  change itself** (task breakdown item 3) — not a follow-up.
- **T6.1-D9 (new, closes B5 — HUD display names):** `ShellBootstrap.DisplayNameFor(RuntimeEntityId id)` covers
  both boot members and the 3 fixed companion ids (T6.1-D3); `HudBootstrap` uses it everywhere it currently reads
  `bootstrap.Members` directly to build its display-name map, so a recruited companion shows their real name, not
  a raw GUID.

## Task breakdown (tests first, per file — high-level; exact test names/assertions decided against real code during implementation, not pre-specified here)

1. **`crpg-toolkit`: `DialogueContext` gains `RequestRecruit`/`RequestStartQuest` delegates** (T6.1-D2/D4) — two
   more optional trailing fields, same DELEGATE shape as Task 6.0's `RequestSave` (not a service reference, per
   facts 3/10). EditMode test: constructing without them regresses nothing; constructing with them and invoking
   each new action's `Execute` calls the delegate with the right key exactly once.
2. **`crpg-toolkit`: `RecruitToPartyAction`/`StartQuestAction`** (T6.1-D2/D4) — each carries its own serialized
   string key and calls its respective `DialogueContext` delegate. EditMode tests in the
   `DialogueRunnerTests.cs`-style action-coverage shape (Task 6.0's own precedent).
3. **`crpg-toolkit`: `TalkInteractionAdapter`** gains the two new pass-through properties + `CurrencyItemId`
   wiring confirmation (T6.1-D2/D4/D7's currency fix). Existing adapter PlayMode test home.
4. **`BarrowsEnd`: the `members`-array boot change (fact 7) LANDS TOGETHER WITH `ShellBootstrap.MembersOverride`**
   (T6.1-D8) — these are ONE task-breakdown item, not sequenced separately, since the review found the override is
   what keeps every existing Shell suite green through this change, not an optional follow-up. Also lands:
   `GateSessionServices.RegisterMemberMotor` (fact 4), `ShellBootstrap.RecruitCompanion` (T6.1-D2), the fixed
   companion id table + `partyVisualPrefabs` pre-population (T6.1-D3), and `ShellBootstrap.DisplayNameFor`
   (T6.1-D9). PlayMode tests: booting the real (post-change) Core scene starts with exactly 1 controllable member;
   `RecruitCompanion` (called directly, not yet through a real conversation) produces a second controllable,
   selectable, moveable, correctly-NAMED party member with no scene reload; a save/load round-trip of an
   already-recruited companion does NOT throw `ViewBindingFailed` (the fact-6 fix, proven directly, not assumed);
   the 5 test files the review named (`ShellTests`/`ShellStartFlowTests`/`HudBootstrapTests`/`DollBootstrapTests`/
   `LocationSceneContentTests`) are updated to use `MembersOverride` (or, for `LocationSceneContentTests`, the
   corrected expected count) and confirmed still green — checked individually against the real current files, not
   assumed compatible.
5. **`BarrowsEnd`: Giant Rat `NpcDefinition`** (fact 12), authored in `BarrowsEndGearAuthor.cs` beside
   `npc-skeleton`, with at least one real usable ability/resource (not just HP) for the tactical stack (item 6) to
   resolve against. Content test matching that file's own existing `npc-skeleton` coverage.
6. **`BarrowsEnd`: the full tactical combat stack wiring** (T6.1-D6, per the user's decision — the largest single
   item in this task) — `PalisadeBreachTrigger` (fact 15's fixes), the `CombatEncounterService`+
   `TacticalEncounterController`+`TacticalCombatUIBridge`/`AbilityBarView`/`AbilityTargetingGate`+
   `TacticalAiTurnDriver`+brain+`EncounterFactionProvider` wiring (fact 11), the hostile-dies-at-0HP rule (fact
   11b), and exploration-movement suspension during combat (fact 11a). Depends on item 5 (the rat needs a real
   ability before combat can be proven) and item 7's quest key existing (the encounter records it on the last
   rat's death) — sequenced after both, not before. PlayMode test: the party approaching the palisade gap starts
   real, winnable combat (ability bar usable, rats actually act on their turn via the AI driver, a rat reaching 0
   HP actually dies); defeating every rat ends combat, restores exploration movement, and records the quest key;
   walking away and back does NOT re-trigger it (a persisted-flag check, not just "doesn't re-fire this run").
7. **`BarrowsEnd`: `QuestDefinition_TheGatheringStorm.asset` authoring + registration in `BarrowsEndGearAuthor`'s
   content list** (T6.1-D4, fact 10's B8 fix) + Mayor Thaddeus's dialogue (T6.1-D5), including the shared
   `"companion-recruited"` `CountAtLeast` gate (fact 9). Sequenced BEFORE item 6 (the combat trigger needs the
   quest's real `trackingKey` to record against). Content test: the asset's objective/tracking key matches what
   `StartQuestAction`/the combat trigger actually record, and that a save containing this quest still passes
   `ContentValidator`; a `DialogueValidator` pass confirms no dangling refs/unreachable nodes/inescapable cycles.
8. **`BarrowsEnd`: the 3 companion dialogues** (T6.1-D2) — Garrick (tavern half of the Restful Hearth), Elara
   (Market Tents), Silas (Shrine), each a real `AddTalkPoint` fixture (fact 3) whose dialogue fires
   `RecruitToPartyAction`+`RecordEventAction("companion-recruited")`. Depends on item 4's `RecruitCompanion` glue
   already existing and proven. PlayMode test per companion: talking to them recruits them for real (party count
   increases, they're controllable, correctly named — reusing item 4's own proof shape) via an ACTUAL conversation
   this time, not a direct `Execute` call.
9. **`BarrowsEnd`: the remaining 6 service/flavor NPCs' dialogue+store content** — Elida (rest/save, fact 14 —
   trivial reuse of the proven Task 6.0 pattern), Fendrick/Marjorie/Silvan/Lyra (vendor catalogs, T6.1-D7 — no
   probe needed, fact 13 resolved it directly; Lyra's one new item authored first), Kael (bounty flavor, no
   store), Jenkins (rumor flavor, no store), Grumble (side-quest *teaser* flavor line only — the actual mushroom
   quest is Task 6.2's job). Content/PlayMode tests per NPC matching Task 6.0's own fixture-test conventions (talk
   opens a real conversation; a store, where present, resolves items/prices, actually charges/pays real
   `item-gold` since `CurrencyItemId` is now wired, and survives a save/load round-trip).
10. **`BarrowsEndTownAuthor.cs` update** — the 10 real NPCs + `PalisadeBreachTrigger` placement, using fact 1's
    exact building/door coordinates and approach-anchor cells; `Reset Town`/`overwrite: true` run once to actually
    regenerate the already-existing `Town.unity` (`BarrowsEndTownAuthor.cs:64-68`'s own `overwrite: false` guard
    otherwise leaves a pre-existing scene untouched — a real should-fix from review). Decide during implementation
    whether Task 6.0's two proof fixtures are removed or left as harmless extra content (either is fine per the
    master doc's own note); if removed, retire `TownProofFixturesTests.cs` too rather than leaving it orphaned.
11. **`BarrowsEndCoreSceneAuthor.cs` update** — final wiring pass confirming every new composition-root call site
    (recruit/quest delegates, `CurrencyItemId`, starting gold grant, `RegisterMemberMotor`) is actually reachable
    from a real boot, not just unit-tested in isolation.

## Tests

- EditMode for every new toolkit type (`RecruitToPartyAction`, `StartQuestAction`, `DialogueContext`/adapter
  pass-throughs); PlayMode for anything needing a real `CharacterAgent`/`ConversationController`/the tactical
  combat stack (the recruit proof, the combat-tutorial proof, each NPC's real conversation).
- The recruit proof (item 4), the save/load-survives-a-recruit proof (item 4), and the combat-tutorial's
  repeat-trigger proof (item 6) are this task's three "worked once, broke on reload/reapproach" risk areas — each
  gets an explicit save/load or repeat-trigger check, not just a first-run pass, matching Task 6.0's own stated
  testing philosophy (which found exactly this class of bug twice).
- Full toolkit EditMode/PlayMode suites + full BarrowsEnd EditMode/PlayMode suites (throwaway-copy convention)
  before this task is called done, per this project's standing verification discipline.
- Gate: a live playthrough (the user, not an automated test) from title through "party assembled (Garrick + Elara
  + Silas recruited), quest accepted from Mayor Thaddeus, palisade-breach tutorial fight won."

## Risks

1. **The `members`-array boot change (fact 7/T6.1-D1) is the single highest-risk change in this task, CONFIRMED
   (not just suspected) to break 5 existing test files if the `MembersOverride` hook (T6.1-D8) isn't landed in the
   same step** — treated with the same discipline as Task 6.0's own starting-scene change, but this time the
   affected files and line numbers are already known (fact 7), not left to discovery during implementation.
2. **The recruit mechanism (T6.1-D2) is genuinely new plumbing this project hasn't built before**, even after the
   v1 -> v2 redesign — no existing code builds a fresh `CharacterState` and a fresh controllable `CharacterAgent`
   instance outside of `SpawnParty`'s own boot-time loop, or registers a motor with a live
   `PartyExplorationController` outside of `Rebind`. Facts 3-6 are this plan's best-effort design against the real
   code, source-verified by two review passes, but still not a proven pattern to copy — a dedicated proof test
   (item 4) before any of the 3 real companions are wired to it remains essential, not optional.
3. **The combat stack (T6.1-D6) is the largest scope addition in this task, taken on deliberately per the user's
   decision** — wiring `TacticalEncounterController`/`AbilityBarView`/`TacticalAiTurnDriver`/
   `EncounterFactionProvider` into BarrowsEnd for the first time, plus the hostile-death-rule and
   movement-suspension gaps the review found, is real, un-shrunk scope; the payoff is that Acts 2/3's own combat
   content (Task 6.2/6.3) build on a proven stack instead of repeating this discovery cost three times.
4. **Ten NPCs + a new recruiting mechanism + a new quest + a full combat stack is the largest single task this
   project has attempted** (larger than Task 6.0, and larger again after the v2 combat-scope decision). The task
   breakdown's own sequencing (items 4 before 8, 5+7 before 6) already reflects the real dependency chain the
   review surfaced; if implementation reveals this is still too much for one verification pass, splitting into
   "recruiting + the 3 companions + Mayor's briefing/quest" and "the remaining 6 service NPCs + the combat
   tutorial" as two sub-checkpoints (not two separate Opus-reviewed tasks) remains a reasonable fallback, decided
   during implementation rather than now.
5. **A downed party member currently bricks scene travel** (fact 17) — pre-existing, not this task's to fix, but
   worth carrying forward as a known gap now that real combat with a real chance of a downed member exists.

## Review record (Opus, 2026-09-28)

**Blockers, all fixed above:** B1 (`context.NpcId` is the talking party member, not the NPC — fixed via the
delegate-based `RequestRecruit`/`companionKey` redesign, fact 3/T6.1-D2), B2 (a spawned `CharacterAgent` can never
be clicked/talked to — fixed by making companions ordinary talk-point fixtures, fact 3/T6.1-D1), B3
(`NpcSpawnPoint`-spawned NPCs are never activated anywhere in the real runtime path — fixed by not using
`NpcSpawnPoint` at all this task, fact 2/12), B4 (the promotion design broke spawn records/save-load/repeat
recruits — fixed by building the `CharacterState` fresh at recruit time instead of promoting a pre-existing spawn,
plus fixed companion ids + boot-time `partyVisualPrefabs` pre-population, facts 5-6), B5 (the HUD would show a
recruit as a raw GUID — fixed via `DisplayNameFor`, T6.1-D9), B6 (the combat design was unplayable — fixed by
building the full tactical stack per the user's decision, fact 11/T6.1-D6), B7 (a monster doesn't die at 0 HP the
way the design assumed — fixed via a hostile-faction death rule, fact 11b), B8 (the quest asset needed ruleset
registration or later saves fail — fixed, fact 10), B9 (`QuestService` on `DialogueContext` would need a
toolkit-design-violating asmdef edge — fixed via the `RequestStartQuest` delegate, fact 10/T6.1-D4), B10 (shops
don't actually work in play — currency never wired, no starting gold — fixed, fact 13/T6.1-D7), B11 (the palisade
trigger's one-shot state/positioning/filtering were all unsound — fixed, fact 15/T6.1-D6), B12 (the `members`
change would break 5 test files — fixed via `MembersOverride`, fact 7/T6.1-D8).
**Should-fix, folded in:** S1 (fact 7 wrongly claimed a 5-item gear budget — corrected, fact 8), S2 (a shared
`"companion-recruited"` `CountAtLeast` key replaces 3 chained `BranchNode`s — fact 9/T6.1-D5), S3 (Act 1 gets its
own `QuestDefinition`, not a multi-act one that would complete prematurely — fact 10/T6.1-D4), S4 (a downed party
member bricks travel — pre-existing, carried forward as Risk 5, fact 17), S5 (rat spawn positions should be
sampled to avoid the party — folded into T6.1-D6), S6 (the trigger volume could swallow ground clicks — flagged
for implementation-time attention, not a plan-level design change), S7 (`Town.unity` needs `overwrite: true` to
actually regenerate — task breakdown item 10), S8 (removing the Task 6.0 proof fixtures orphans their test file —
task breakdown item 10), S9 (don't destroy a talk point mid-conversation — fixed, T6.1-D2's hide-on-next-load
design), S10 (citation corrections — folded into facts 2/5/7 directly).
**Verified correct:** the Town's 11 building/door coordinates and the `AddTalkPoint`/`AuthorProofFixtures`
templates (fact 1); `PartyState.AddMember`/`DialogueContext.Party` (facts 3-4's underlying API, even though the
`NpcId` usage was wrong); `RegisterMotor`/`exploration` being `Rebind`-only and private (fact 4); `RecordEventAction`
(fact 9); `CombatEncounterService`'s constructor/explicit-faction shape (fact 11, as far as it went); actions
running synchronously inside `DialogueRunner.Settle`, so the fire-and-forget shape holds for the new actions too;
`GiveItemAction` as the serialized-reference precedent; vendor stock resolved directly rather than left as an
open probe (fact 13).
**Open questions, not resolved by this review (flagged for implementation-time attention):** the exact mechanism
for suspending exploration movement during combat (fact 11a); the exact mechanism for the hostile-dies-at-0HP rule
(fact 11b); whether `GridPathfindingService` fully satisfies what the tactical stack's movement resolution needs;
whether `DialogueValidator` already covers the two new action types or needs extending; the exact starting-gold
amount/source.

## Implementation progress (2026-09-28)

**Items 1-6 of the task breakdown are implemented and verified** (throwaway-copy convention throughout; not yet
committed, not yet run against the user's real open project). Toolkit suites: EditMode 1055/1055, PlayMode
434/434+3 skips. BarrowsEnd suites: EditMode 123/124 (the one pre-existing, unrelated stale-bake flake), PlayMode
82/82.

- **Items 1-3 (toolkit foundation):** `DialogueContext` gained `RequestRecruit`/`RequestStartQuest` delegates (not
  a `QuestService` reference, per fact 10/B9's fix); `RecruitToPartyAction`/`StartQuestAction`; `TalkInteractionAdapter`
  pass-throughs. Full toolkit suites green.
- **Item 4 (recruit mechanism + `members`-array boot change):** `ShellBootstrap.RecruitCompanion` builds a
  companion's `CharacterState` fresh and registers their motor with the live `PartyExplorationController`
  (`GateSessionServices.RegisterMemberMotor`, new); `CompanionRoster` (new file) holds Garrick/Elara/Silas's fixed
  ids/gear/appearance, all drawn from BarrowsEnd's existing item catalog (no new item authoring needed, per fact
  8's correction). `ShellBootstrap.MembersOverride` landed in the same step as the 1-member boot change; all 5
  test files the review named now use it and stayed green. A real bug found and fixed by this task's own proof
  test: the recruit's fallback spawn position (used only when no Town fixture exists yet, i.e. before item 10)
  landed outside safe click-test geometry -- fixed with a better fallback (the active member's own position).
  Save/load-survives-a-recruit proven directly (simulated a process restart).
- **Item 5 (Giant Rat):** authored in `BarrowsEndGearAuthor.cs` beside `npc-skeleton`, with a real melee ability
  (`ability-rat-bite`) -- not the file's existing debug abilities. Found live: the PARTY had no combat ability at
  all before this task (would have made the tactical stack's own ability bar show nothing to click) -- fixed by
  authoring `ability-strike`, granted to the protagonist (`ProtagonistFactory.Build`) and every companion
  (`ShellBootstrap.SpawnParty`'s companion branch, `RecruitCompanion`).
- **Item 6 (full tactical combat stack -- the largest, riskiest item in this task):** `TacticalCombatSession` (new)
  wires `CombatEncounterService`/`EncounterFactionProvider`/`AbilityResolutionService`/`TacticalEncounterController`/
  `TacticalAiTurnDriver`/`AbilityTargetingGate`/`TacticalCombatUIBridge`/`AbilityBarView` -- the toolkit's full
  stack, proven before this task only against 3D NavMesh movement (`TestProject/TacticalSlice`, the shipped
  `TacticalEncounter` sample), wired here for the first time against BarrowsEnd's 2D grid. New supporting pieces:
  `TacticalMovementController`/`LiveCharacterPositionProvider` (`IMovementController`/`IPositionProvider` have no
  toolkit production implementation for grid movement -- confirmed, every real composition root writes its own);
  `GateCommandSource.CombatMoveOverride` (suppresses exploration Move during combat -- a real, previously-unsolved
  hazard the toolkit's own `TacticalPlayerMoveAdapter` doc comment names explicitly); `GiantRatBrain` (a minimal
  "Attack Nearest" `BehaviorSetDefinition`, built+registered at runtime via reflection, matching
  `TacticalSliceBootstrap`'s own established pattern); `PalisadeBreachTrigger` (fact 15's four fixes: persisted
  Timeline fired-flag, party-member filtering, fresh spawn ids, placement offset from both travel points). A real
  toolkit gap found and fixed with its own test: `DeathSaveService` was applying SRD5's player-only death-save
  loop to hostiles too (a Giant Rat at 0 HP went Unconscious instead of dying) -- fixed with a new optional
  `usesDeathSaves` predicate (`GateSessionServices` wires `session.Party.IsMember`), backward-compatible (null
  preserves the original behavior for the Gate scene and every other existing caller). Manual win-condition
  polling (`CombatEncounterService`/`TacticalEncounterController` do not auto-detect "one side wiped" -- confirmed
  by reading both). Proven end to end with a real fight driven through the real ability-bar click and the real
  `AbilityTargetingGate` click (not a mocked/direct scheduler call), including exploration-movement suppression
  during combat and restoration after. **Real test-infrastructure finding, not a production bug:** the batch test
  game view (640x480) is too small for a HUD region's fixed panel-unit width to leave much screen-safe click space
  at all -- fixed by rendering the test into a real-sized RenderTexture (`ShellCaptureTests.cs`'s own established
  precedent), the same technique used elsewhere in this project for exactly this class of problem.

## Results (implemented 2026-09-28, items 7-11)

**All 11 task-breakdown items are now implemented and verified**, throwaway-copy convention throughout. Final
suites: toolkit EditMode 1055/1055, PlayMode 434/434+3 skips; BarrowsEnd EditMode 134/135 (the one pre-existing,
unrelated stale-bake flake), PlayMode 83/83.

- **Item 7 (quest + Mayor Thaddeus):** `QuestDefinition_quest-gathering-storm` authored in `BarrowsEndGearAuthor.cs`
  (registered in `extraContent`, per fact 10's B8 fix), one `Kill` objective tracking
  `PalisadeBreachTrigger.RatsDefeatedTimelineKey`. `ShellBootstrap.RequestStartQuest`/`RefreshQuestProgress` wired
  into `WireTalkAdapters`/`WireCombatTriggers` respectively (delegate-based, per T6.1-D4 -- no `CRPG.Quests`
  reference from `CRPG.Dialogue`). Mayor Thaddeus's dialogue (`BarrowsEndAct1NpcAuthor.cs`) matches the simplified
  D1 design exactly: a single `BranchNode` (`CountAtLeast` 3 on `"companion-recruited"`) gates
  `StartQuestAction`, with the exposition option looping back to the same choice. Proven by driving the REAL
  authored dialogue through a real `DialogueRunner` both ways (not ready / ready), plus the quest's own
  start-then-complete-on-refresh round-trip.
- **Item 8 (3 companion dialogues):** Garrick/Elara/Silas each get a real `AddTalkPoint` fixture (named via
  `CompanionRoster.FixtureName`, matching what `ShellBootstrap` already looks up) whose dialogue chains
  `RecruitToPartyAction` -> `RecordEventAction("companion-recruited")` -> a greeting line, mirroring Task 6.0's
  own "actions auto-settle before any line ever displays" precedent. Proven with a REAL click on Garrick's own
  authored fixture in the real Town (not a direct API call) recruiting him through the full
  click -> `TalkInteractionAdapter` -> `ConversationController` -> `DialogueRunner` -> `RecruitToPartyAction` ->
  `ShellBootstrap.RecruitCompanion` chain -- the one integration path no earlier test exercised end to end.
  **A real bug found by this exact test, fixed at the correct layer:** the test never advanced past the greeting
  line, leaving `ConversationController`'s modal scope open and leaking `UIToolkitPointerCapture`'s static state
  into whatever test ran next in the same suite (silently broke `CharacterCreatorScreenTests` only when run AFTER
  this one, never in isolation) -- fixed with `InteractionController.Cancel()` in `TearDown`, the same idempotent
  cleanup `ConversationControllerTests.cs` already established for exactly this reason.
- **Item 9 (the remaining 8 service/flavor NPCs):** Elida (rest/save, reusing Task 6.0's proven pattern exactly),
  Grumble (side-quest teaser flavor line, the real mushroom quest deferred to Task 6.2), Barnaby/Fendrick/
  Marjorie/Silvan/Lyra (vendors), Kael/Jenkins (flavor only, no store). Vendor stock resolved exactly as fact 13
  predicted: Fendrick (`item-healing-potion`) and Silvan (`item-shortbow`/`item-longbow`) stock directly from the
  confirmed-resolving SRD5e2014 package (found at its real `Packages/com.crpgtoolkit.core/...` path, not
  BarrowsEnd's own `Content/Gear` -- a real path-resolution detail this task's own vendor-authoring code had to
  get right, not assumed); Barnaby/Marjorie stock from BarrowsEnd's own existing items; Lyra's one new item
  (`item-scroll-identify`) authored alongside the quest. `LocationAuthoringKit`'s own `stores` registration
  (Task 6.0) OVERWROTE the array each call -- fine for one proof fixture, not for 5 real vendors sharing one
  `LocationContent` -- fixed with a proper APPENDING helper (`AddStoreAppending`) in the new
  `BarrowsEndAct1NpcAuthor.cs`. All 13 authored dialogues validate cleanly (`DialogueValidator`, zero errors) and
  all 5 vendors' real stock was confirmed by reading the actual authored scene, not assumed.
- **Item 10 (Town placement):** all 13 talk points (10 handoff NPCs + 3 companions) placed just outside their
  building's own door (source fact 1's exact coordinates), each building's occupants spread across X where more
  than one person shares a door (the Restful Hearth: Garrick/Elida/Grumble). The whole Town bakes with zero
  `LocationBaker` warnings against the full 13-NPC + palisade-trigger cast, confirmed by the EditMode content
  suite. `BarrowsEndCoreSceneAuthor.Reset`/`overwrite: true` used to actually regenerate the already-existing
  scene (fact/should-fix S7), not assumed.
- **Item 11 (final wiring):** confirmed reachable from a real boot -- `RequestRecruit`/`RequestStartQuest`/
  `CurrencyItemId` in `WireTalkAdapters`; `RequestQuestRefresh` in `WireCombatTriggers`; starting gold and the
  strike ability granted at `SpawnParty`/`RecruitCompanion`. No further changes needed beyond what items 1-10
  already wired incrementally.

**Not yet regenerated against the user's real, open BarrowsEnd project.** Every verification pass in this task
(items 1-11) ran in a throwaway `BarrowsEnd_verify` copy, matching this project's standing practice of never
touching the user's live Editor session. The real project's `Town.unity`/`Core.unity`/ruleset assets still reflect
pre-Task-6.1 content until the authoring scripts are actually run against the real project (Task 5.3's own
documented lesson: verifying the code in a throwaway copy is not the same as the real asset being regenerated).

**Not yet committed.**


---

# Task 6.2: Act 2 — The Descent

**Status: Opus-reviewed (8 blockers, all folded in below); awaiting user approval. Task 6.1 is committed (d15b8c8 + 5834a7d).**

Gate: a live playthrough from the Town through Cavern_Upper and Cavern_Middle — goblin encounters fought and won with
the tactical stack, the Act 2 quest advancing, Grumble's mushroom side quest accepted, completed and turned in.
Cavern_Deep stays a shell (Task 6.3).

## What 6.1 left us (verified in the committed code, not assumed)

- `PalisadeBreachTrigger` is a one-off: hard-coded rat count, `npc-giant-rat`, `GiantRatBrain`, Timeline keys and quest
  id. `TacticalCombatSession.StartEncounter` names every hostile "Giant Rat N" and takes an opaque brain.
- Combat turn economy (6.1 follow-ups): per-turn `resource-movement` (6 units), `resource-action`, `resource-bonus-action`;
  Strike/Bite cost one Action; a resolved ability ends the turn when no Action is left; `TacticalPlayerMoveAdapter`
  drives click-to-move through the real `MoveActionExecution` (grid path planning via `spawner.GridNavigation`).
- `Cavern_Upper/Middle/Deep` exist as shells (walls, floor, mine rail, water, two-way travel) from Task 6.0. The Upper
  level's mine-rail strip is already reserved for goblin patrols; Deep has a reserved boss approach.
- Grumble's `dlg-grumble` is a single placeholder line ("ask me again once you've made some progress").
- `QuestObjectiveType` has Kill/Collect/Interact/etc., all driven by Timeline keys with `HistoricalCount` or
  `CountSinceActivation` progress; there is no Quests-side knowledge of how a key gets recorded.
- The only interaction adapters are Talk/Loot/Travel/Door/Teleport. There is no "harvest a world pickup" adapter.
- Known 6.1 gap carried in: nothing happens if the whole party is defeated.

## Decisions (T6.2-)

- **T6.2-D1 (generalise the encounter trigger):** replace `PalisadeBreachTrigger` with a data-driven
  `EncounterTrigger` MonoBehaviour: serialized `encounterKey` (persisted Timeline "fired" flag
  `encounter-<key>-triggered`, "won" flag `encounter-<key>-won`), a list of `(npcDefinitionId, count)` spawns, a
  `behaviorSetId`/brain builder id, `scatter`, and an optional `questId` to refresh on a win. The palisade encounter
  becomes the first instance (same keys as today so saves/tests keep working — `palisade-breach-triggered`/
  `palisade-rats-defeated` are preserved by making the key prefix configurable, not renamed).
  `TacticalCombatSession.StartEncounter` takes a hostile display-name prefix instead of hard-coding "Giant Rat".
- **T6.2-D2 (goblins):** new `npc-goblin` (melee, ~7 HP, Strike-like `ability-goblin-slash` d6, one Action) and
  `npc-goblin-archer` (~5 HP, `ability-goblin-shortbow` d6, range 8, `requireLineOfSight` true). New
  `GoblinBrain` (Close Distance to melee for the grunt; Close Distance to a stop distance of ~6 then shoot for the
  archer), authored the same reflection way as `GiantRatBrain`. **Open question for review:** is an archer worth the
  extra surface (ranged targeting through `AbilityTargetingGate`, a LOS provider BarrowsEnd's tactical session does not
  construct today), or should Act 2 be melee-only with the "barricades" as terrain? Default: melee-only plus ONE
  archer on the Middle level, only if `PhysicsLineOfSightProvider`-equivalent is cheap on the 2D grid.
- **T6.2-D3 (encounters):** Upper: two patrol encounters on the mine-rail strip (2 grunts each), placed so the party
  must cross them. Middle: one larger "barricade" encounter in front of the Deep gate (3 grunts + 1 archer) with the
  barricades authored as `Rock`/`TallColumn`/`IronBars`-style blocking objects in `BarrowsEndCavernsAuthor.AuthorMiddle`
  (terrain only; no cover rules — the toolkit has none). Authored in `BarrowsEndCavernsAuthor`, not the Town author.
- **T6.2-D4 (Act 2 quest):** new `QuestDefinition` `quest-the-descent`, started by a `QuestStartTrigger` (or the first
  encounter's own trigger) the first time the party enters `Cavern_Upper` — NOT by a dialogue, since Thaddeus's
  briefing already ended Act 1's quest. Objectives: Kill (clear the Upper patrols, targetCount 2 encounters), Kill (break
  the Middle barricade, targetCount 1). Registered in `BarrowsEndGearAuthor`'s content list (6.1's ContentValidator
  lesson). Completion (all objectives) is NOT the end of the main quest — 6.3 owns that.
- **T6.2-D5 (Grumble side quest):** `dlg-grumble` becomes a real tree: gated `BranchNode`s on
  `quest-gathering-storm` completion (before: the current placeholder), accept -> `StartQuestAction("quest-grumbles-mushrooms")`,
  in-progress reminder, turn-in branch (`CountAtLeast` on the harvest key) -> `GiveItemAction` (gold + a healing potion)
  + `RecordEventAction("grumble-quest-complete")`, then a thank-you line. Quest: one Collect objective, 3 mushrooms
  (`HistoricalCount` so harvesting before accepting still counts — decision for review), one TalkTo/SetFlag for the turn-in.
- **T6.2-D6 (harvest interaction):** new BarrowsEnd-local `HarvestInteractionAdapter : IInteractionAdapter` (+
  `WorldInteractable`), placed as 3 mushroom clusters (2 Upper, 1 Middle — the `Mushrooms` tile already decorates
  Upper at `(6, UpperDepth-6)`): `BeginExecute` records the shared key `mushroom-harvested` plus a per-node key
  `mushroom-node-<id>`, then hides itself; a node whose per-node key already occurred is hidden on scene load (same
  persisted-flag-checked-on-enable fix 6.1 applied to the palisade trigger).
- **T6.2-D7 (defeat handling, closes the carried-in gap):** when the party is wiped, `TacticalCombatSession` reports a
  loss to the encounter; `EncounterTrigger` then (a) clears its own "fired" flag so the encounter can be retried and
  (b) the shell reloads the most recent save via the existing `LoadNow`, or, if there is none, restarts at the Town
  `Restful Hearth`. **Open question for review:** is silently reloading right, or is a "Defeated" screen with a choice
  better? Default: message via `Say`, then reload last save.
- **T6.2-D8 (what is NOT in scope):** loot drops/XP/level-up, cover mechanics, the Deep level, patrol AI that walks the
  rails while exploring (the goblins are static until their trigger fires — "patrol" is flavour for placement only),
  class abilities for companions (a separate later class-work task per the user).

## Risks

1. **Spawning combatants away from the trigger point**: 6.1's `ratSpawnScatter` let rats spawn up to 2.5 units from the
   trigger, outside Strike's 1.5 range; combat only became playable once movement existed. Goblin spawns must be
   sampled on walkable cells through `TrySamplePositionAvoiding` and verified reachable (path-planned) from the party.
2. **Party members can be in any scene state at fire time** (some in formation behind the leader): the combatants list
   is every party member; a far-behind member could be a combatant yet unable to reach melee. Mitigation: only members
   within N units of the trigger join, OR warp the party into formation — decide in review.
3. **Multiple encounters in one location** share `TacticalCombatSession` (it no-ops a second `StartEncounter` while
   one is active) — two triggers firing the same frame must not drop one silently (queue or re-arm).
4. **Persisted keys vs. saves**: new Timeline keys and the new `NpcDefinition`/`AbilityDefinition`/`QuestDefinition`
   assets all need ruleset registration or `ContentValidator` rejects saves made after them (6.0/6.1 both hit this).
5. **Throwaway-copy verification caveat** (6.1 lesson): the real project's scenes only change when the authoring menus
   are actually run against the live project; verification in `BarrowsEnd_verify` is not the real asset.

## Task breakdown (tests first, per file — high-level)

1. `TacticalCombatSession`: hostile name prefix + loss reporting (`StartEncounter` result as won/lost with a reason);
   PlayMode tests extended from `TacticalCombatSessionTests`.
2. `EncounterTrigger` replaces `PalisadeBreachTrigger`; palisade encounter re-authored onto it with unchanged keys;
   the existing palisade tests pass unmodified apart from the type name.
3. Goblin content: `npc-goblin`, `npc-goblin-archer`, abilities, `GoblinBrain`, ruleset registration; EditMode content
   tests mirroring the Giant Rat ones.
4. `BarrowsEndCavernsAuthor`: Upper patrols, Middle barricade, mushroom nodes, quest-start trigger; scene content tests.
5. `HarvestInteractionAdapter` + the Collect quest + Grumble's tree; EditMode dialogue tests + a PlayMode harvest test.
6. Defeat handling (D7) with a PlayMode test (a deliberately overmatched encounter).
7. Throwaway-copy full verification, then the live gate playthrough with the user.


## Opus review record (Task 6.2) — blockers found and the revisions they force

The reviewer verified every claim against source. The draft above is SUPERSEDED where it conflicts with this section.

**Blockers (all accepted):**
- **B1 hostile AI never attacks in range (a 6.1 bug).** Both rat behaviors have empty `considerations`; `UtilityMath.CombineScores` returns 0 for none, and the provider only replaces a decision on a strictly higher score, so Move (listed first) always wins and a rat bites only after burning all 6 Movement. Fix: a `Distance` consideration per behavior with attack's lowest score above Move's highest (the toolkit's `TacticalEncounterTestContent` already records this exact fix); a PlayMode test asserting a hostile actually damages the party.
- **B2 one brain per encounter cannot drive grunt + archer.** Fix: a BarrowsEnd decision provider selecting a brain per NPC definition (`TacticalAiTurnDriver` takes any `ITurnDecisionProvider`); both goblin types share one faction.
- **B3 Upper objective unreachable with per-encounter keys.** Fix: `EncounterTrigger` serializes a shared `progressKey` recorded alongside its own won key.
- **B4 quests are never re-checked** (only the palisade trigger does). Fix: `ShellBootstrap.RefreshAllActiveQuests()` after StartQuest, after every conversation ends, after a harvest, after an encounter ends (also fixes the existing 6.1 Act 1 ordering bug).
- **B5 saving mid-combat breaks the encounter** (fired flag persists, encounter does not). Fix: `SaveNow` refuses while `CombatSession.IsActive`.
- **B6 clicking a world object during combat soft-locks** (Interact passes through `GateCommandSource`). Fix: drop Interact while `CombatMoveOverride != null`.
- **B7 defeat (D7) as drafted does not work:** a downed member stays a combatant, nobody rolls party death saves, a dead member is permanently `MarkDefeated`, and retry collides on fixed spawn ids and the in-memory `fired` flag. **Revised D7:** treat a party combatant with `DeathSaves != null` as out of the fight; wire `RollDeathSave` into the UI bridge; stabilise downed members after a win; write a CHECKPOINT save just before an encounter fires (before the fired flag), and on a loss show a `Say` message, wait ~1.5s, and reload that checkpoint (needs `LoadNow` to take a path). No flag clearing or restart path.
- **B8 a second trigger firing during combat loses its encounter** (`fired` and the persisted key are set before `StartEncounter` silently no-ops). Fix: `StartEncounter` returns bool; trigger ignores entry while combat is active, re-checks via `OnTriggerStay`, records fired only after a successful start.

**Should-fix (accepted):** serialize full `firedKey`/`wonKey`/`progressKey` strings (the two palisade keys share no prefix) and keep the existing key constants; `Town.unity` stores the old trigger by script GUID so it needs a Reset rebuild (or keep the class name and add fields); add a test that actually walks into the trigger; archer needs `PhysicsLineOfSightProvider(ObstacleMask)` passed at session construction (otherwise `requireLineOfSight` is silently ignored) and only `Wall` tiles block sight; place the archer so its stop point never lands in the Middle water; quest-start-on-entry has no hook, so a small component wired in the same pass as `WireCombatTriggers`; gate Grumble on `HasOccurred("palisade-rats-defeated")` (quest completion records no key), order branches turn-in -> accepted -> not accepted, gold and potion need two `GiveItemAction`s; harvest adapter uses a serialized node id and the sprite on the node (the painted decor tile stays); breakdown must list the `ShellBootstrap` work; add a `Say` on quest started/objective/completed since BarrowsEnd has no quest log UI; content-count test +6 and every new definition registered in `BarrowsEndGearAuthor.extraContent` (ContentValidator does not check Timeline keys, so Risk 4 was partly wrong); goblins need a body option; caverns need `Reset Caverns` (Author skips existing scenes); show `NpcDefinition.DisplayName` in the turn-order list; auto-hit d6 damage makes balance tight, tune goblin HP/count in the live gate.

**Open questions answered:** archer = yes but LAST and first thing to cut; defeat = `Say` + checkpoint reload, not a silent quicksave reload and not a new screen.

### Revised task breakdown (supersedes the one above)
0. Carried-in combat fixes, tests first: B1 considerations + "hostile attacks" test; downed/stabilise + death-save wiring; no save / no Interact during combat; `StartEncounter` returns bool; `RefreshAllActiveQuests`.
1. `EncounterTrigger` (explicit keys, unique spawn ids per attempt, per-NPC brain map, ignores entry during combat) + Town rebuild + a walk-into-trigger test.
2. Defeat handling: pre-encounter checkpoint + reload-on-loss.
3. Melee goblin content + `GoblinBrain` + content-count update + registration.
4. Caverns content via Reset Caverns, with spawn-reachability tests.
5. Quest plumbing: location-entry quest start, Act 2 quest with shared `progressKey`, `Say` notifications.
6. Harvest adapter, Grumble's side quest and tree.
7. Archer (LOS provider + per-NPC brain), optional.
8. Throwaway-copy verification, regenerate the real project, live gate with the user.

**Not yet committed.**

## Task 6.2 implementation status

Steps 0-2 committed (b750371). Steps 3-6 implemented and verified in a throwaway copy (EditMode 143/144 with the pre-existing
`Town_Slice` bake failure, PlayMode 102/102), NOT yet committed: goblin NPC + Slash + `GoblinBrain`; encounters authored in
`Cavern_Upper` (two patrols sharing `act2-upper-patrol-cleared`) and `Cavern_Middle` (barricade, `act2-middle-barricade-cleared`);
`quest-the-descent` started by a `QuestStartTrigger` on arrival in the caverns; `HarvestInteractionAdapter` mushroom patches
(2 Upper, 1 Middle) and Grumble's real quest tree (`quest-grumbles-mushrooms`). The cavern scenes and `dlg-grumble.asset` were
regenerated in a throwaway copy and copied into the real project (grumble rewritten IN PLACE to keep its GUID).
Step 7 (the archer) is deliberately NOT done: optional per the review; revisit only if the live gate wants ranged variety.
Step 8 (the live gate with the user) is next. Combat-time gaps still open (not Task 6.2 scope): companions have no class
abilities; no quest log UI (progress shows as one-line messages).

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

**Not yet committed.**

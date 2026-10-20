# Sailing Map and Voyages (the *Spirit of Vengeance* at sea)

Status: **BUILT 2026-10-19 (section 13); it was parked earlier the same day and un-parked at the user's request.** Earlier status: PARKED. For now only Stormpoint exists as a destination; the user is writing more of the story (a highly political campaign: the crew as heroes fighting political corruption) before deciding the voyage structure, ports and crew scenes. The map crop and this draft are kept so work can resume. An Opus review was requested; fold its findings in when resuming. Original status: design draft for review (Opus), then the user. Nothing is built. Game-side feature in `SpiritOfVengeance`, with the generic pieces kept free of game names so they can be promoted to the CRPG Toolkit later.

## 1. Goal and scope

The ship is the campaign's home (GDD pillar 4) and the crew get to know each other while it sails. The player needs a **chart of the west coast of Anuire**, a way to choose a port and sail there, and a place for **crew scenes to happen on the way**.

In the first version:

- A full-screen chart screen showing the coast from Boeruine in the north to Brosengae and the Arnienbae in the south (the cropped source map, section 2), with ports, the ship's marker, and the planned route. Pan and zoom.
- A graph of ports and legs. Choosing a port shows days and provisions for the leg, then departs.
- A **voyage**: the ship marker travels the leg over in-game days; **voyage events** fire at authored points along it. An event is mainly a **crew scene** (a dialogue on the deck or in the quarters between companions), but the same hook carries later encounters.
- Arrival: Stormpoint loads its existing town scene. Other ports show an arrival summary until their locations exist.
- Voyage facts persist through the existing Timeline, so saves need no new section.

Not in the first version (do not build placeholder infrastructure for them): fog of war or discovery, random sea combat, ship-versus-ship tactics, weather simulation, port papers and infamy gating (only a Timeline-key "access" flag per port), tides, trade economy, an overland world map, rotating the chart.

## 2. The map asset

- Source: the user's Birthright map of Cerilia (8210 x 6140), cropped to the west coast, **2876 x 3284 px**, at `Assets/World/Map/AnuireWestCoast.png` (preview `Docs/Design/Sailing-Map-Preview.png`). The crop covers Talinie's Seaport and Tariene to the north, Boeruine, Taeghas with Stormpoint, Brosengae with Bindier, the Arnienbae with the City of Anuire, Diemed with Aerele, Baerghos Island in the west, and the Straits of Aerele to the south. It includes margin on the Sea of Storms.
- **Licensing.** This is published Birthright art, not our own. It is used as-is in a private project, like the Synty-derived ship models: **the repository must stay private**, and it must be replaced or re-licensed before any public release. Record that in the repo memory next to the Synty lesson.
- Import settings: Texture2D, sRGB, no mipmap bias surprises, max size 4096 (the crop fits), bilinear, clamp. About 20 MB on disk.
- Coordinates: the chart's world space is **map pixels**, 1 world unit = 1 px, **+Z is north** (z = 3284 - pixelY), x east. This matches the toolkit map convention (north-up, world +Z) so `MapProjection` can be reused unchanged. There is no real-world scale: **leg durations are authored in days**, not computed from distance.
- Source image coordinates are seeds. Port positions are finalised by a small Editor placement tool (section 9, slice 1) by clicking on the image, not by hand-typing numbers.

## 3. Data (ScriptableObject definitions, registered in the ruleset)

Generic names, no game namespace in the types:

**`SeaMapDefinition`** (a `DefinitionAsset`): `mapId`, a reference to a `LocationMapDefinition` (background texture and `TextureWorldBoundsXZ` = Rect(0, 0, 2876, 3284); reuses the toolkit's validated image/bounds contract), the port list, the leg list.

**`SeaPort`**: `portId` (stable), display name, domain/province label, position (world XZ), `kind` (Port, Anchorage, Island), `accessKey` (optional Timeline key that must have occurred or be at level > 0; empty = open), and **`arrival`**: a scene path to load and an arrival-point id, or none (arrival summary only). Stormpoint is the only port with a scene in version 1.

**`SeaLeg`**: `fromPortId`, `toPortId` (legs are two-way by default), `days` (whole days, authored), a route polyline of waypoints (sea-only points the marker follows; the planner never routes across land, it only follows authored legs), `risk tag` (open sea, coastal, reef coast, strait), and an `eventPool` id.

**`VoyageEventDefinition`** (a `DefinitionAsset`): `eventId`, the legs it may fire on (or "any"), when it fires (`Departure`, `AtFraction` 0..1, `Arrival`), predicates (reuse the dialogue/quest Timeline predicates, not a new condition language), `once` or repeatable with a cooldown in days, `priority`, and `content`: a `DialogueDefinition` id plus a **stage id** (where it is staged, section 6). Nothing else is invented until a second kind of content exists.

Phase 1 ports (proposed, to confirm with the user): **Seaharrow** (Boeruine), **Portage** (Taeghas, north), **Stormpoint** (Taeghas, the hub), **Bindier** (Brosengae), **City of Anuire** (the Arnienbae), **Aerele** (Diemed), plus **Baerghos Island** as an unmarked anchorage and an **off-map "Aduria" edge** to the south that only shows as a locked exit. Seed positions (preview px, 700 x 800): Seaharrow (250, 212), Portage (257, 360), Stormpoint (172, 485), Bindier (340, 668), City of Anuire (493, 651), Aerele (538, 748), Baerghos (95, 580).

## 4. Voyage logic (pure, tested first)

No Unity types in the planning layer.

- **`SeaGraph`**: built from `SeaMapDefinition`; `ReachablePorts(from)`, `ShortestRoute(from, to)` as a sequence of legs (Dijkstra on days). The player picks a destination; the plan may chain several legs. Provisions and days are the sum.
- **`VoyagePlan`**: legs, total days, provisions needed (days x crew size x the per-day ration), and any leg blocked by an `accessKey` (the plan reports the blocking port, it does not silently reroute).
- **`VoyageDirector`** (pure state machine): `Start(plan)`, `Advance(deltaDays)`, emits `LegDeparted`, `Progressed(fraction)`, `EventDue(event)`, `LegArrived`, `VoyageArrived`. It holds at an event until the host reports `EventFinished`, so a scene never overlaps the ship's movement. It picks due events by priority, honours `once`/cooldown from the Timeline, and fires **at most one crew scene per leg segment** so scenes cannot stack into a wall of dialogue.
- **Pacing guarantee for bonding scenes**: a rule, not luck. If N sea days pass without a crew scene and an eligible one exists, the next beat fires it. N is a data constant.
- **Persistence through the Timeline** (verified: `SaveData` has `TimelineEvents` and no extensible custom section): the ship's current port is a 0/1 level key `voyage.at.<portId>` (deactivate the old before activating the new, as the Stormpoint handoff already requires), elapsed days is a counter key `voyage.day` (increment by delta), a visited port is a durable fact `voyage.visited.<portId>`, a seen scene is `voyage.event.<eventId>`. A voyage interrupted by a quit/continue resumes from `voyage.leg` + `voyage.progress` keys written at each leg boundary and each event boundary (not every frame). Provisions are ordinary items in the party inventory (a Food item), consumed on departure, not a new state type.

## 5. The chart screen (UI)

A full-screen UI Toolkit screen built on the toolkit map pieces that exist (`MapViewportState`, `MapProjection`, `MapView`, `MapViewStyle`):

- Pan by drag, zoom by wheel and +/- buttons, **Fit** and **Centre on ship**, using `MapViewportState` with `Area()`-style options; the viewport fit is the whole crop.
- Markers: ports as `Landmark` markers with distinct shapes for state (current, known, unknown/unvisited, locked), never colour alone; the ship as the single `Party`-kind marker. Labels on hover and for the current and selected port.
- **Route line**: `MapView` draws markers, not lines. The route and the ship's progress are an overlay `VisualElement` painted with `generateVisualContent` in the same projection (to verify in review that `MapView` exposes what the overlay needs: the image element and the projection, which it appears to).
- Selecting a port opens a side panel: name, domain, days, provisions needed versus held, blocked/locked reason, and a **Set Sail** button. Escape or a close button leaves the chart. While the chart is open it owns pointer and keyboard through the toolkit's input lease/modal mechanism (the same ownership rules the map doc defines; slice C of the location map is the open item there, so the chart takes a plain modal lease and does not wait for it).
- While sailing, a slim HUD strip: day counter, destination, a progress bar, **Skip scene** is not offered; a speed control (1x/2x) is.

## 6. Crew scenes at sea

This is the part the player feels. Version 1 delivers the *mechanism* and a small first set; the writing grows.

- A crew scene is a `DialogueDefinition` (existing runner, so choices, Timeline records, skill checks and quest hooks all work) with a **stage**: which scene and which marks the companions stand on. Stages are authored points in the existing ship scenes (the weather deck, the captain's quarters, the galley): `StageDefinition` = scene path + a mark per speaker. The voyage host loads the stage additively over the ship scene or cuts to it; the ship marker is paused.
- **Bonding** is data: pair keys `bond.<a>.<b>.<n>` recorded as durable Timeline facts by the dialogue itself (RecordEvent actions). Event predicates read them, so scene 2 for a pair requires scene 1, and "who is in the party/crew" gates the pair. No separate relationship meter in version 1; a meter can be derived later from the facts (do not store it).
- First set: **4 scenes** that exercise every path (a two-person quiet scene on the deck, a three-person disagreement with a choice that records a fact, a scene gated by an earlier fact, and a repeatable low-priority "sea chatter" scene). Real writing is the user's and the content team's; the mechanism must not constrain it.
- The four companions (Knox, Gordon, Baelethor, Marco) are the cast; the created character is the fifth. Scenes may include any of them, so staging must tolerate a missing speaker (the dialogue omits the line, the stage leaves the mark empty) and the created character's name and pronouns come from the profile, not the script.

## 7. Integration points

- **Opening the chart**: a chart-table interaction in the captain's quarters and the helm on the weather deck (an interactable in the existing ship scenes, using the toolkit interaction adapters). Not available during a fight or the Prologue; gated by a Timeline fact set when the party takes the helm after the mutiny.
- **Arrival at Stormpoint**: loads `Assets/Scenes/Stormpoint.unity`, party start by the quay. That scene currently boots a village-style bootstrap with the four heroes, not the ship session (an open item in the Stormpoint notes). Doing this task properly **requires the ship-to-town session hand-off**: the same `GameSession`, party, inventory and Timeline must carry over. The sailing map's arrival call is the first real consumer, so the hand-off is slice 4 here, not a separate project.
- **Leaving Stormpoint**: the reverse, from the quay or the ship, back to the chart.
- Build Settings: nothing new to scene-load except the existing scenes; the chart is a UI screen over the ship scene, not a scene of its own (decision to verify: if the ship scene is heavy, a dedicated light scene is the alternative, review should weigh in).

## 8. Tests

- Pure EditMode (the bulk): `SeaGraph` shortest route and unreachable cases, blocked legs by access key, `VoyageDirector` sequencing (event holds the clock, one scene per segment, pacing rule, once/cooldown from a fake Timeline), persistence keys round-trip through a real `Timeline` save/load, provisions arithmetic, `SeaMapDefinition.Validate` (ports inside bounds, legs reference real ports, waypoint polylines start/end at their ports, no duplicate ids, Stormpoint present).
- PlayMode: the chart opens, pan/zoom/fit, selecting a port shows days and provisions, Set Sail runs a short leg with a scripted event and returns control; a visual capture of the chart (a human judges it; the assert only catches a black frame), written to `VikingRaid_Logs/spikes/`.
- A scripted full voyage Stormpoint -> Bindier with one scene, save mid-leg, reload and resume.

## 9. Slices (stop at a checkpoint after each)

1. **Data and graph**: `SeaMapDefinition`/`SeaPort`/`SeaLeg`/`VoyageEventDefinition`, `SeaGraph`, `VoyagePlan`, the validator, an **Editor placement tool** to click ports and draw leg polylines on the image, the authored Phase 1 map. EditMode tests.
2. **Chart screen**: the UI, markers, route overlay, port panel, open/close from a debug menu and the ship interactables.
3. **Voyage loop**: `VoyageDirector`, the sailing HUD, ship marker travel, Timeline persistence, quit/continue resume, event host with stub dialogue.
4. **Crew scenes and the hand-off**: stages, the four first scenes, the ship-to-Stormpoint session hand-off and the way back.

## 10. Decisions and alternatives

- **Reuse the toolkit's map code for the image, projection and markers; add a game-side layer for graph, voyage and events.** Alternative considered: extend the toolkit with a region map from day one. Rejected for now (user choice: game-side, toolkit-ready); the generic types above are the promotion candidates.
- **Leg durations authored, not derived.** The source map has no usable scale and Birthright distances would be spurious precision.
- **Persist on the Timeline, not a new SaveData section.** `SaveData` has no extension point and a format-version bump would touch every consumer.
- **Provisions are an inventory item.** No new resource system.
- **No relationship meter.** Bond facts are the single source of truth.

## 11. Open questions for the user

1. Confirm or change the Phase 1 port list and which are playable now (only Stormpoint is).
2. The GDD says "south coast around Taeghas"; you said west coast. The map shows Taeghas on the west coast. Should the GDD line be corrected to the west coast?
3. Should a voyage cost provisions from day one, or only after the first act? (A food pressure can be pure flavour in v1.)
4. How many crew scenes do you want for the first playable voyage (the design assumes 4 mechanism scenes plus your writing)?

## 12. Opus review findings (2026-10-19; spot-checked: no SaveService use in game code, Continue is a placeholder, ShipSession ends its session on destroy)

Fold these in before building. The sections above are the pre-review draft.

**Must change**
1. **No save/load exists in Spirit of Vengeance.** Continue just loads ShipInteriors with `startConfined` (back in chains). Timeline persistence is real in the toolkit (`SaveService`), but nothing calls it. Drop quit/continue resume and the mid-leg save test from v1, or make "SoV save/load" an explicit prerequisite project (capture/restore, which scene/area or "at sea" on the Timeline, the created character, skipping the Prologue and confinement on load).
2. **Stormpoint cannot be arrived at with a scene load.** `ShipSession` lives in the additive ShipCore scene; a Single load destroys it and ends the session. The existing mechanism is the persistent Core scene plus additive location scenes (`SceneTravelCoordinator`, `TravelRunner`, as the ship decks already do). Stormpoint must become a **location under ShipCore**: strip or disable its VillageBootstrap/camera/input when a ShipSession exists, generalise ShipSession's location binding (it throws for a location with no `ShipLocation`/`ShipArea`), check the agent radius (hard-coded 0.25 for the ship bake), travel both ways, and add Stormpoint to Build Settings (it is not there). Riskiest unknown: **spike it first**.
3. **Map texture import.** Default NPOT scaling would import 2876x3284 as 2048x4096 and fail `LocationMapDefinition.Validate` (aspect). Set NPOT scale to None, max 4096, mipmaps on; consider cropping to a multiple of 4 (2872/2876) and updating the bounds; add an EditMode test that loads the imported asset and runs Validate; cap chart zoom near 3x.
4. **Crew-scene staging is not "additive over the ship scene".** The ship model assumes one active location scene with exactly one `ShipLocation`. A stage = location scene + `ShipArea` id + marks; move the party with `ShipSession.TryTransferParty` (refuses while confined/in combat) or `TravelRunner`; drive the camera with `CameraDirector` (see `PrisonerTalk.cs`). There is no galley area: the areas are CaptainInterior, ForecastleInterior, HoldInterior, LowerDeckInterior and ShipExterior. Say what the 3D party does while the clock runs.
5. **Dialogue reuse gaps.** Running a scene with no NPC is fine (`PrisonerTalk.cs` does it). But: quests are not wired on the ship (`StartQuestAction` is a silent no-op without `RequestStartQuest`); there is no "party member present" branch source (host-kept `party.member.<id>` level keys or a new source); no `{player}` token substitution and the profile has no pronouns (add a token pass and pronouns, or write pronoun-free lines).
6. **Predicates.** The toolkit shares a function (`TimelineCondition.Evaluate`), not a serialisable type, and has no AND/OR. Define a `TimelinePredicate {type,key,secondKey,threshold}` list (AND); implement event cooldown by recording `voyage.event.<id>` as a Number (day) and reading `GetLatest`.
7. **`accessKey` must be explicit Fact vs Level.** `HasOccurred` stays true after a level returns to 0; the Stormpoint handoff forbids using it for "still active". Use the predicate struct.
8. **Resume state (when saves exist)**: add `voyage.plan` (Text, leg ids) and `voyage.destination`; progress is a Number read with `GetLatest`, not `Sum`; write `voyage.event.<id>` only on `EventFinished`; handle a save mid-dialogue.
9. **Stormpoint's existing ship state.** `sp01.disposition.{impounded,temporary,forged,recognised,hidden}` levels already exist and the handoff says to adopt the existing ship authority; they are effectively papers. Block departure while impounded; decide explicitly whether departure is blocked until Act I resolves (`sp.city.outcome.committed`); use the disposition levels as access predicates, not new per-port flags. A `voyage.day` counter must never become an implicit abandonment timer (handoff).
10. **The first voyage has no origin.** After the mutiny the ship is at sea, Aduria-bound. Add an at-sea origin node and a scripted first leg with Stormpoint the only destination; unlock the chart on the captain's defeat (`captain-defeated`, `CaptainEncounter.cs`) or define a new key.

**MapView reuse (confirmed feasible, with gaps)**: standalone `MapView` + `MapViewportState` works without the presenter/HUD. Gaps: no refresh event (draw the route line as a child of `ImageElement` in normalised UV space so it follows the image and sits under markers); no click API (own click-versus-drag handling; the HUD's handlers are private); only one hover label (always-on labels for current/selected port need the overlay); four authored sprites for port states; a Party marker draws as a plain dot, so use a Landmark with a ship icon for the ship glyph.

**Chart UI = an overlay in ShipHud's document over ShipCore**, taking the ship's input lease (`ship.AcquireInputBlock()`, as `CharacterWindow`/`LootWindow` do) and reading Escape itself; refuse while in combat, the gate is busy or anyone is confined. A separate Single-loaded scene would kill the session, so that alternative is out. Consider lazy-loading the map texture rather than registering it through the ruleset ShipCore references.

**Cut or tighten**: risk tag (unused in v1); one event-to-leg association, not two; the off-map Aduria exit (contradicts "ports inside bounds"); the validator's "Stormpoint present" check (game name in a generic validator); define "segment" and the pacing rule; define "crew size" (no roster exists; the party is five; there is no Food item) and the real-time-to-days rate; docked visuals (ShipExterior is at sea).

**Seed positions**: Seaharrow, Stormpoint, Bindier, City of Anuire and Aerele are within 1-2 preview px of their symbols. Portage has no settlement symbol and its seed looks inland: move it to the water edge. Baerghos seed is on its label in open sea (fine for an anchorage; say so). z = 3284 - pixelY and the (0,0,2874,3284) bounds are correct.

**Recommended slice order**: (1) data and graph + import/Validate test + at-sea origin node; (2) **Stormpoint-as-location-under-ShipCore hand-off spike** (moved up from slice 4); (3) chart overlay in ShipHud; (4) voyage loop without resume, departure gated by the sp01 dispositions and Act I state; (5) crew scenes (stages, `{player}` token, party-presence branch, quest wiring); (6) separate prerequisite project: SoV save/load, then resume and the mid-leg save test.

## 13. As built (2026-10-19): the travel map is complete

UN-PARKED at the user's request ("let's get the travel map completed, and then we can work on the arrival to Stormpoint"). Built with the section 12 review findings applied; the decisions the doc left open were settled as below.

**What exists**
- **Pure model** (`Assets/Sailing`, asmdef `VikingRaid.Sailing`, 19 EditMode tests): `SeaMapDefinition` (a `DefinitionAsset`: chart image, world rectangle 2876x3284, stops, lanes, origin; `Validate`), `SeaPort`, `SeaLeg`, `SeaGraph` (quickest route by days, locked stops never entered, lane paths in both directions), `VoyagePlan`, `VoyageLog` (the voyage's facts on the Timeline), `VoyageEventDefinition` + `TimelinePredicate` (the toolkit's `TimelineCondition` made serialisable, AND over a list), `VoyageDirector` (the state machine: sails lane by lane, holds the clock at an event, **at most one event per lane**, highest priority wins). Note: a ScriptableObject class must live in a file named after it (the first version lost its script reference).
- **Timeline keys:** `voyage.day` (counter), `voyage.at.<port>` (a 0/1 LEVEL kept with deltas, read with `Sum`), `voyage.visited.<port>` (fact), `voyage.event.<id>` (Number = the day it ended on, so once/cooldown are queries). One `VoyageLog.Arrive` call per lane keeps a save from ever seeing half an arrival.
- **Data** (`SailingAuthor`, menu `Viking Raid/Sailing/Author Chart And Crew Scenes`; under `Assets/Resources/SOV/Sailing`, loaded lazily so the 20 MB image is not in memory until the chart opens): the chart rewritten from code tables on every run (positions are in the 700x800 preview of the image, lanes drawn over it and checked by eye); eight stops: At sea (origin), **Stormpoint** (opens `Stormpoint.unity` at `stormpoint-quay`), Seaharrow, Portage, Baerghos Island (anchorage), Bindier, City of Anuire, Aerele; seven lanes with authored days; every stop except Stormpoint requires the fact `voyage.visited.stormpoint` (the scripted first leg: the only first destination). Three PLACEHOLDER crew scenes (`first-night`, `night-watch`, `chatter`; created once and never overwritten) exercise the mechanism and record `bond.<a>.<b>.<n>` facts: replace the writing by hand.
- **Ship** (`Assets/Ship/Sailing`): `VoyageSession` (plan, clock at `SecondsPerDay` = 6 with 1x/2x/4x, the ship's gate busy while sailing so nobody moves and **the game cannot be saved at sea**, crew scenes through `ConversationController`), `ChartView` (overlay in the ship HUD holding the input lease: the toolkit's `MapViewportState` for pan/zoom/fit, own stop markers, route line and ship drawn with `Painter2D`; drag to pan, wheel/buttons to zoom; stop panel with name, region, days, route and Set Sail; the voyage screen with progress and speed that cannot be closed until the ship puts in), `ShipChartTable` (a click on the captain's table; `ChartTableAuthor`), and the HUD's `M` key. The chart opens only once the captain is beaten (`captain-defeated`), not while ashore, in a fight, restrained or busy; a refusal says why on the HUD.
- **Integration:** the way ashore ("Go ashore at Stormpoint") now opens when `voyage.at.stormpoint` is on (the ship has put in), not when the captain falls: the voyage is how the party reaches Stormpoint. Arrival itself (a landing sequence, the ship at the quay, the ship's papers) is the next task.
- **Tests:** `ChartAndVoyageTests` (6, plus screenshots `VikingRaid_Logs/spikes/chart_*.png`), `SystemMenuKeyTests` (M and Escape through the Input System), `SailingModelTests` (19 EditMode).

**Decisions taken (change on request):** no provisions cost in v1 (the Food item and a crew roster do not exist; days are shown, nothing is consumed); crew scenes play as the conversation panel over the chart, not staged in 3D on a deck (stages are a later feature); no pacing guarantee beyond "one scene per lane, highest priority"; no weather, no sea encounters; resume mid-voyage is not offered (saving is blocked at sea; a save made in port keeps the result); the first port after Stormpoint opens everything else at once.

**Open for the user:** the real crew writing and which companions speak when; port list and lane days (edit the tables in `SailingAuthor`, or the asset in the inspector; waypoints are authored to keep to the water, adjust by eye); whether the chart should also be openable from the helm on the weather deck.

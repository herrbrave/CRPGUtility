# CRPG Toolkit

A ruleset-agnostic, party-based CRPG engine for Unity 6.6+: exploration, party control, tactical combat, dialogue, quests and inventory, all driven by data.

The engine has no built-in knowledge of any specific tabletop ruleset — no Fighter, no Fireball, no Strength, no Hit Points. Those concepts are supplied entirely by a ruleset package. This repo ships one reference ruleset, `CRPG.Rules.SRD5`, implementing the 2014-era 5e SRD 5.1 (OGL 1.0a) content — see [Licenses/TODO.md](../Licenses/TODO.md) for the licensing status of that content.

## Package layout

- `Runtime/` — the engine itself, with one assembly per subsystem (Core, Actors, Party, Exploration, Navigation, Camera, Interaction, Inventory, Abilities, Combat, Dialogue, Quests, Saving, UI).
- `Rules/Core` (`CRPG.Rules`) — the generic rule-query/modifier/dice infrastructure any ruleset plugs into.
- `Rules/SRD5e2014` (`CRPG.Rules.SRD5`) — the SRD 5.1 ruleset. Deletable and replaceable without touching `Runtime/`.
- `Editor/` — authoring tools: database editors, character/ability/dialogue/quest/encounter editors, project validation.
- `Integrations/` — optional adapters (Input System, Localization, Addressables) so core code never depends on them directly.
- `Tests/` — `CRPG.Tests.EditMode` for rules logic, `CRPG.Tests.PlayMode` for navigation/presentation.
- `Samples~/` — BasicCRPG, CharacterCreator, TacticalEncounter.

## Status

Past early scaffolding. Core rules/actors/combat/AI/dialogue/quests/inventory/saving subsystems exist and are test-backed — 661 EditMode + 205 PlayMode tests passing as of 2026-09-14 (see `Docs/Verification/Gate-C-Results.md`). This is regression evidence, not a 1.0 claim: a dedicated production-hardening pass is underway — see `Docs/Design/Production-Foundation.md` for the current gap analysis and `Docs/Plans/Production-Foundation-Implementation.md` for the phased plan. Phase 4 (combat/tactical fixture, Gate C) is now complete: a full 4-vs-8 tactical encounter with real navigation, melee actions, a reaction, a temporary effect, AI sharing the player's validation path, and a post-combat save/load round trip is proven end to end. Treat any older "Phase N" status text elsewhere as historical; the Docs/Verification gate results plus the verified test count are the current source of truth.

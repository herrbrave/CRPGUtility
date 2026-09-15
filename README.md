# CRPG Toolkit

A ruleset-agnostic, party-based CRPG engine for Unity — exploration, party control, tactical combat, dialogue, quests, inventory, and saves, all driven by data.

The engine itself has no built-in knowledge of any specific tabletop ruleset: no Fighter, no Fireball, no Strength, no Hit Points. Those concepts are supplied entirely by a pluggable ruleset module. This repo ships one reference ruleset, `CRPG.Rules.SRD5`, implementing 2014-era D&D 5e content under the SRD 5.1 / OGL 1.0a — deletable and replaceable without touching engine code.

## Repository layout

- [`crpg-toolkit/`](crpg-toolkit) — the Unity package itself (`com.crpgtoolkit.core`). See its own [README](crpg-toolkit/README.md) for the package layout (`Runtime/`, `Rules/`, `Editor/`, `Integrations/`, `Tests/`, `Samples~/`).
- [`TestProject/`](TestProject) — a full Unity project used to dogfood the package: several demo/slice scenes (character lab, AI combat demo, adventure slice, tactical slice, interaction lab) that exercise the toolkit end to end outside of unit tests.
- [`Docs/Design/`](Docs/Design) — architecture and gap-analysis docs, notably [`Production-Foundation.md`](Docs/Design/Production-Foundation.md) (the current production-hardening initiative and its acceptance gates).
- [`Docs/Plans/`](Docs/Plans) — phased implementation plans.
- [`Docs/Verification/`](Docs/Verification) — gate-by-gate verification results, written as honest checklists (what's proven, what's deliberately deferred) rather than pass/fail claims alone.
- [`Docs/Rules/`](Docs/Rules) — the SRD5 supported-rules-subset ledger: exactly which 5e mechanics are modeled vs. explicitly out of scope.
- [`Licenses/`](Licenses) — OGL 1.0a and SRD 5.1 license texts; see [`Licenses/TODO.md`](Licenses/TODO.md) for outstanding licensing decisions.

## Requirements

- Unity **6000.6.0f1** (Unity 6.6)
- Input System, AI Navigation packages (pulled in automatically as package dependencies)

## Getting started

The fastest way to explore the toolkit is to open [`TestProject/`](TestProject) directly in Unity — it already references `crpg-toolkit` as a local package and contains several working demo scenes under `Assets/*/Scenes/`.

To use the toolkit in your own project, add `crpg-toolkit` as a local package via Unity's Package Manager (`Add package from disk...` → `crpg-toolkit/package.json`).

## Status

Past early scaffolding — this is an active production-hardening pass, not yet a 1.0 release. Core subsystems (rules, actors, combat, AI, dialogue, quests, inventory, saving) exist and are test-backed: **661 EditMode + 205 PlayMode tests, all passing.** A full 4-party-member-vs-8-opponent tactical encounter — real navigation around an obstacle, melee actions, a reaction, a temporary effect, AI driven through the same validation path as the player, and a post-combat save/load round trip — is proven end to end (see [`Docs/Verification/Gate-C-Results.md`](Docs/Verification/Gate-C-Results.md)).

For the current gap analysis and phased plan toward production readiness, see [`Docs/Design/Production-Foundation.md`](Docs/Design/Production-Foundation.md) and [`Docs/Plans/Production-Foundation-Implementation.md`](Docs/Plans/Production-Foundation-Implementation.md) — treat those two documents plus the verified test count as the current source of truth over any older status text elsewhere in the repo.

## License

Not yet finalized — see [`Licenses/TODO.md`](Licenses/TODO.md). The SRD 5.1 content under `crpg-toolkit/Rules/SRD5e2014/` ships under the [OGL 1.0a](Licenses/OGL-1.0a.txt); the toolkit's own engine-code license is still to be decided.

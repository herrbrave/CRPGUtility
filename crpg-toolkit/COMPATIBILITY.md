# Compatibility, versioning, and migration policy

This is the package's supported-version/API/migration policy (Task 5.3). It's checked automatically for your
own project by **CRPG Toolkit > Validate Project Assets** in the Editor menu (`CompatibilityValidator` -- see
`Editor/Validation/CompatibilityValidator.cs`), which compares your project's actual Unity version and
installed dependency package versions against what this package's own `package.json` declares.

## Supported Unity version

`package.json`'s `"unity"`/`"unityRelease"` fields are the single source of truth (currently `6000.6`/`0f1`).
This package is developed and verified against exactly that release. A different patch/release within the same
`6000.6.x` line is likely fine but not independently verified; a different major/minor Unity version is not
supported and the validator will flag it as an error.

## Dependency versions

`package.json`'s `dependencies` are pinned to **exact versions**, not ranges:

```
com.unity.inputsystem:                1.20.0
com.unity.ai.navigation:              2.0.14
com.unity.render-pipelines.universal: 17.6.0
```

These are exact pins, not floors, because older versions of `com.unity.inputsystem`/`com.unity.ai.navigation`
have genuinely failed to compile against this Unity release before (their Editor-only code called APIs Unity
6.6 turned into hard `error CS0619` obsolete-as-error failures) -- a version range would have silently let a
consumer resolve an incompatible version. `com.unity.render-pipelines.universal` was added as a dependency in
Task 5.3 specifically because every sample/dogfood authoring script in this project hardcodes the `"Universal
Render Pipeline/Lit"` shader; without it declared, a genuinely fresh consumer project has no render pipeline
active at all and every authored material renders as Unity's pink fallback (found and confirmed live during
BasicCRPG's own clean-install verification -- see `Docs/Verification/Task-5.3-BasicCRPG.md`).

If you need a newer version of any of these, bump it in `package.json`, then re-run this package's own full
EditMode+PlayMode suite (`Docs/Verification/Scripts/clean-install-harness.sh full-suite <EditMode|PlayMode>`) on
that version before trusting it -- this package does not maintain compatibility shims for older/newer versions
of its own dependencies.

## API stability (pre-1.0)

This package is `0.x` -- there is no stability guarantee yet, and `CHANGELOG.md` is the authoritative record of
what changed release to release. In practice:

- **`Runtime/`, `Rules/Core`, `Rules/SRD5e2014`** (the shipped assemblies) are the public surface. Anything
  `public` there can change or be removed between `0.x` releases without a deprecation cycle -- read
  `CHANGELOG.md` before upgrading, not just the version number.
- **`Editor/`, `Integrations/`** are Editor-time/optional-adapter surfaces, held to the same `0.x` bar.
- Types/members marked `internal` (e.g. `RulesetSessionFactory`) are never part of the public surface at any
  version, regardless of what a consumer's own assembly could technically reach via reflection.
- Once this package reaches `1.0`, the intent is standard semver: a breaking change to public API bumps the
  major version and is called out explicitly in `CHANGELOG.md`'s `### Changed`/`### Removed` sections, not
  silently folded into a patch release. That policy isn't enforced by tooling yet -- there's nothing to enforce
  until there's a `1.0` baseline to be breaking *from*.

## Migration policy

Two genuinely different things can need migrating, and this package treats them differently:

1. **Save data** (a player's actual save file) has a real, built migration system today:
   `CRPG.Saving.ISaveMigration` + `SaveMigrationRunner`, driven by `SaveHeader.FormatVersion`.
   `SaveFileStore.Load` refuses to load a save that can't be walked all the way to
   `SaveData.CurrentFormatVersion` via a registered migration chain (`NoMigrationPath`), rather than loading
   a partially-migrated or stale-format save silently. If you change `SaveData`'s shape, add a migration and a
   test proving the old-format-in, new-format-out round trip -- see `Tests/Editor` for the shipped
   v1-to-v2 and v2-to-v3 migrations. Format history: v2 added the world/store/loot sections and per-character
   fields; **v3 adds per-character `Appearance` (layer slot -> option id) for the 2D sprite presentation** - a save
   written by a newer package version is refused (`UnsupportedNewerVersion`) rather than loaded lossily.
2. **Package API** (your own code calling into this package's classes) has no automated migration tooling, and
   isn't expected to until `1.0`. Pre-1.0, the only "migration path" is: read `CHANGELOG.md`'s entry for the
   version you're upgrading past, and update your own call sites by hand. This is a deliberate, stated scope
   limit, not an oversight -- building real API-migration tooling (codemods, obsolete-attribute shims, etc.)
   for a pre-1.0 package with no external consumers yet would be speculative engineering with nothing real to
   migrate from.

## Known limits (tracked here, not silently worked around)

- **The package's own PlayMode suite is not fully environment-independent.** A handful of PlayMode tests
  (`CharacterWrapperTests`, `CrossSceneLoadTests`, `GateAFixtureTests`, `SceneTravelTests`, and in a consumer
  project without them, `UIToolkitPointerCaptureTests`) reference fixture scenes/prefabs that never lived inside
  the package itself (e.g. `BaseHumanMaleCharacter.prefab`, `SceneTravelTestA/B` scenes). They were kept in the
  repository's old `TestProject/Assets/`, which was removed when Spirit of Vengeance became the toolkit's test bed
  (the fixtures remain in git history before commit `1f2eee8`). Confirmed via the clean-install harness
  (`Docs/Verification/Task-5.3-BasicCRPG.md`) and separately from inside `ConsumerGame/`
  (`Docs/Verification/Consumer-Game.md`): running the package's own EditMode+PlayMode suites from a project that
  does not carry those fixtures will show these as failures, not a regression in your own project. Not fixed --
  would mean relocating/duplicating fixture assets the whole existing suite depends on. The consumer game
  `SpiritOfVengeance` exercises the toolkit through its own tests, pinning the package by git URL.

## Checking your own project

Run **CRPG Toolkit > Validate Project Assets** and click "Run Supported Checks." The Compatibility section
reports:
- An **Error** if your Unity major.minor version differs from what `package.json` declares, or if a declared
  dependency isn't installed in your project at all.
- A **Warning** if your Unity patch/release or a dependency's installed version differs from what's declared
  (same major.minor -- likely fine, just not independently verified by this package).
- Nothing if your project matches exactly what this package was built and verified against.

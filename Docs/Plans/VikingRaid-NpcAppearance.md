# NPC appearance creator (Viking humans)

Status: BUILT 2026-10-06 (Section 12): toolkit hook, catalog + applier + binder, inspector creator with live URP preview, villagers migrated; EditMode/PlayMode green, every option reviewed on contact sheets. Remaining: see Section 12 "Open". (Original status: DESIGN v1 + review resolutions (Section 10) + SPIKE RESULTS (Section 11, which settle the gate and the structure).) Decisions already made with the user: Viking humans first (goblins later, same system), a fairly rigid structure, a few sub-categories to rotate through
(`< Title: Value >` rows, like the in-game creator), edited from the `NpcDefinition` inspector.

## 1. What it is

An NPC is authored from the ground up as DATA: `NpcDefinition.appearance` (the toolkit's existing list of `(layerSlot, option)` DefinitionId pairs). At spawn the toolkit already copies that list into
`CharacterState.Appearance` (`NpcSpawnService.Spawn`, so it is saved and restored with the character). A new project-local binder reads it and dresses a shared "master rig" prefab. In the editor, the
NpcDefinition inspector gets a creator section: tabs, rotate-through rows, a live 3D preview, Randomize and Reset. Nothing is baked: no prefab per NPC.

## 2. Facts this rests on (verified in the source, 2026-10-06)

- Every Polygon Viking Realm character prefab is the same MASTER RIG: one skeleton carrying all 10 bodies (`SM_Chr_{Warrior,Peasant,Leader}_{Male,Female}_0N`) and the skinned hair/fur/pelt options
  (`SM_Chr_Attach_Hair_01..05` + alts, `Fur_01..04`, `Wolf_01`, `Bear_01`) as separate SkinnedMeshRenderer children toggled by their active flag. All use ONE palette material.
- Static attachments (beards, helmets, horns, wings, crowns, eyepatch, pouches, capes, and War Camp's hats, bags, backpacks, bottles, masks, goggles) are separate prefabs with a single MeshRenderer
  (local pose identity). War Camp's hoods and shawl are RIGGED (their own skeleton): they must be re-skinned onto the master skeleton by bone name. The Polygon rigs share one skeleton.
- Colour: the Polygon shader has a global `_Color_Tint`; 15 palette-variant materials (`PolygonVikingRealm/Materials/Alts/PolygonVikingRealm_0{1..5}_{A,B,C}.mat`). Hair and beard are separate meshes (independent
  tint is possible); skin is baked into each dressed body (no separate skin tone).
- Toolkit: `AppearanceEntry`, `CharacterState.SetAppearance`, `CreatorRow`/`CreatorOption` (a tested wraparound rows model with optional swatches), `NpcDefinitionEditor` (a UI Toolkit inspector; one custom editor per type).

## 3. The rigid structure: three tabs, eight rows

| Tab | Row (slot id) | Options (curated; "None" first where optional) |
|---|---|---|
| **Body** | `body` | the 10 bodies |
| | `outfit-colour` | the 15 palette materials (swatch = a representative colour) |
| **Head** | `hair` | skinned Hair_01..05 + alts (master toggles), static Viking Hair_06..09, War Camp Hair_01..07 |
| | `hair-colour` | swatches (tint of hair and beard): natural browns, blond, red, grey, white, black |
| | `beard` | Viking Beard_01..05, War Camp Beard_01..02 |
| | `headwear` | Viking helmets 4 + horns 3 + wings 1 + crowns 3; War Camp hats 7, helmets 2, crowns 5; rigged hoods 3 |
| | `face` | eyepatch (Viking, War Camp), goggles, masks 2 |
| **Wear** | `shoulders` | skinned furs 1-4, wolf and bear pelts (master toggles); capes (Viking 2, War Camp 5); rigged shawl |
| | `gear` | pouches 2, bags 5, backpacks 2, bottles 2 |

Not offered, deliberately: Generic's modern items (headsets, sunglasses, beanies), skulls/rope/glider/straw (no clear mount), mixing outfit pieces (a body comes dressed), face sculpting, a separate skin tone.
Option ids are stable strings (`body-warrior-male-01`, `headwear-helmet-horns-02`); the catalog is the only place they map to meshes, so renaming a mesh never breaks saved NPCs.

## 4. Data and runtime (project-local, `Assets/Appearance/`, assembly `VikingRaid.Appearance`)

- `ModelAppearanceCatalog` (ScriptableObject): ordered `Slot { id, title, tab, optional, options[] }`, `Option { id, title, kind, childName | prefab, bone, localPosition, localEuler, scale, material, colour }`
  where `kind` is one of `ToggleChild` (activate a named child of the master; deactivate its slot siblings), `AttachStatic` (clone the prefab under a bone), `AttachRigged` (clone, re-skin onto the master
  skeleton by bone name), `Palette` (assign a material to every renderer), `Tint` (a `_Color_Tint` property block on the hair and beard renderers). Authored by one re-runnable author script from the pack folders.
- `ModelAppearanceApplier` (pure Unity, no session): `Apply(root, catalog, IReadOnlyDictionary<DefinitionId,DefinitionId> choices, bool pruneUnused)`. Idempotent and total: applying twice or in any order gives the
  same result; an unknown slot or option degrades to "nothing" with a logged warning, never throws; new objects get the root's layer and lose their colliders (mirrors `CharacterRosterAuthor`); created objects are
  named `Appearance_<slot>` so a slot can be replaced. At runtime `pruneUnused` destroys the master's inactive body and hair meshes (about 26 skinned meshes per instance otherwise). The editor preview does not prune.
- `ModelAppearanceBinder` (MonoBehaviour on CharacterRoot, next to `ModelActionCues`): self-binds from the bound `CharacterAgent` (the same pattern `ModelActionCues` uses, since `WorldSpawnService.BindView` only
  initialises binders it knows about), reads `CharacterState.Appearance`, calls the applier, exposes `Refresh()`.
- The NPC's `visualPrefab` is one shared base wrapper `Assets/Characters/Villagers/VikingNpcBase.prefab` (the master model wrapped by `CharacterWrapperBuilder` + hero animation profile + `ModelActionCues` +
  yaw/phase components + the binder + the catalog reference), built by the existing roster author path.

## 5. The inspector

One small TOOLKIT change: `NpcDefinitionEditor` gains an extension point, `INpcAppearanceEditor` (`bool Handles(NpcDefinition)`, `VisualElement Create(SerializedObject)`), discovered with `TypeCache`, whose UI
is added after the existing Appearance foldout (the foldout stays: it is the raw data). Generic, additive, with a toolkit EditMode test. The project implements it (`VikingNpcAppearanceEditor`):

- `Handles` is true when the NPC's `visualPrefab` carries a `ModelAppearanceBinder`; otherwise the section shows "This NPC's visual prefab has no appearance binder" and a button that assigns the Viking base prefab.
- Tab buttons, then one `< Title: Value >` row per slot of the tab, built on the toolkit's `CreatorRow` (wraparound, swatches for the colour rows). Every change writes the `appearance` list through the
  `SerializedObject` (undo works; the raw foldout updates live).
- Preview: an `IMGUIContainer` over `PreviewRenderUtility` (drag to rotate, wheel to zoom, head/body framing toggle), showing the base prefab dressed by the applier (no pruning), in the idle pose.
- Randomize (per tab or all; seeded so the same seed gives the same NPC) and Reset.

## 6. Villagers

The three demo villagers move to the base prefab plus appearance entries (Elder: Leader_Male_01, a beard, a hat; Hild and Orm similar), proving the path end to end. Their old fixed prefabs are removed.

## 7. Tests

EditMode: catalog integrity (unique slot and option ids; every `ToggleChild` names a child that exists in the master; every prefab/material reference resolves; every slot has at least one option; required slots
have no "None"); the applier on the real master in a scene (each slot's choice shows exactly one object of that slot, switching replaces, unknown ids are tolerated, applying twice is a no-op, pruning removes only
unused meshes, attachments end up within a head-sized distance of the head bone, rigged attachments have no missing bones); the toolkit hook. PlayMode: spawn an NPC from an NpcDefinition with a chosen appearance and
check the visible parts; save and restore keeps them; render a contact sheet of all options per slot (a human looks).

## 8. Gate: the attachment spike (`Viking Raid/Appearance/Spike: Attachment Contact Sheets`)

Open questions only a render answers, so nothing else is built until it is judged: (1) which mounting convention is right for static attachments (as authored at the world origin then parented to the Head
bone, versus identity under the bone), (2) whether War Camp and Generic attachments fit the Viking bodies (hair on a female body, hats over Viking hair), (3) whether re-skinning rigged hoods by bone name works,
(4) whether `_Color_Tint` visibly tints hair and beard under URP, (5) whether the 15 palettes are different looks or the same atlas recoloured. Options that do not fit are dropped from the catalog, not forced.

## 9. Risks and non-goals

Non-goals: goblins (Sidekick, a separate catalog and a mesh combiner), equipment visuals (the toolkit's `EquipmentVisualBinder` stays separate), animation changes, an in-game player creator (the data path would
serve one, but its UI is a different job), promotion to the toolkit (later, after use). Risks: pivots differ per attachment family (the spike); hair under helmets clips (rows are independent: a validator warns,
it does not forbid); the master prefab is heavy (pruned at runtime); `Appearance` is slot to option only, so colours are swatch OPTIONS, not free colours (a free colour would need a toolkit data change).

## 10. Review resolutions (Opus hostile review, 2026-10-06) -- these SUPERSEDE the text above where they differ

Verified true by the review: the 10 masters carry the same 24 skinned children toggled by active flag on one material (`ead525b9`), no static children; static attachments are single-MeshRenderer prefabs with an identity root;
one skeleton (the Viking master and War Camp rigs have the same 50 bones with identical local transforms, male and female); the master Animator has a Humanoid avatar and exactly one transform is named `Head`;
`NpcSpawnService.Spawn` copies appearance, it is saved and restored (save format v3), `BindRestoredView` goes through `BindView`; `CreatorRow`/`CreatorOption` are public, usable from editor code and adequate;
`NpcDefinitionEditor` is the only custom editor for the type; destroying inactive renderers does not touch the humanoid binding, `CharacterView`, `ModelYawCorrection` or `ModelActionCues`.

1. **Two material families (blocker).** Viking parts use the Viking palette material and shader; War Camp parts use `PolygonGoblinWarCamp_01_A` (a different atlas, shader `Generic_Basic`). `Palette` replaces only renderers whose
   material belongs to the Viking palette family; War Camp parts keep their own material. (A War Camp palette row is a later option; its 12 alts are 01-04 A/B/C.)
2. **Tint (blocker).** `_Color_Tint` is a per-material property (a property block works) but the graph MULTIPLIES it, so it can darken and cannot lighten brown hair to blond or white; `Generic_Basic` has `_BaseColor` and no
   `_Color_Tint`. So: the property name is per shader (`TintProperty` on the option's family), the hair-colour swatches are limited to what the spike's tint sheet proves works (it includes a multiplier above 1), and if the range is
   too poor the row is DROPPED (a rigid structure with one row fewer is fine).
3. **Capes are rigged (blocker).** Viking `Cape_01/02` and War Camp `Cape_01..05` are SkinnedMeshRenderers on their own cape rig (`Cape_Rig`, `Cape_01..06`: bones the master lacks). Fourth option kind `AttachOwnRig`: mount the whole cape
   prefab (Animators and colliders stripped) under `Spine_03` at an authored local offset, rigid (no cloth). The spike tests it; if it looks wrong, capes are dropped from v1. Backpacks and the shawl follow the same rule if they are rigged.
4. **`AttachRigged` is "extract one renderer".** Hoods and shawl (and the Viking Hair/Fur/Wolf/Bear prefabs) are full masters (17-24 renderers, one active). Take only the active renderer, remap `bones` BY ORDER (index i stays
   bone i: bind poses), remap `rootBone`, reparent it under the master, destroy the rest of the clone (other bodies, second skeleton, Animator). Reuse the toolkit's `EquipmentVisualBinder.TryBuildSkinned` approach (ordered remap,
   reject the whole binding if a bone is uncovered, root-bone fallback). The spike now does exactly this.
5. **No pruning (reverses the `pruneUnused` in Section 4).** After destroying a body or hair mesh, `Refresh()` could never show it again (`CharacterState.cs` says to refresh the binder after a change). Unused master parts are DEACTIVATED, not destroyed.
6. **Unknown ids.** A missing or unknown id in a REQUIRED slot (`body`) falls back to the catalog's default option (an invisible NPC is worse than a wrong one); only optional slots fall back to None. Always with a logged warning.
7. **Idempotency at runtime.** `Destroy` is deferred, so a second `Apply` in the same frame would still find the old `Appearance_<slot>`: unparent and rename before `Destroy` at runtime, `DestroyImmediate` in edit mode. Clones also lose their Animators (cape and hood roots carry one),
   not only colliders (`ModelYawCorrection` takes the first Animator with an avatar).
8. **Mounting (the head bone's local rotation is (0.4586, -0.5382, 0.4586, 0.5382), not identity).** "Identity under the bone" is wrong. The catalog stores a LOCAL pose per option, computed at AUTHOR time: stand the master in its rest pose at the origin,
   put the attachment at the origin as authored, parent it to the bone keeping its world pose, record the resulting local position/rotation/scale. The runtime applier ONLY ever sets local transforms; it never keeps a world pose. The mount bone is chosen by category
   (Head: hair, beards, helmets, hats, crowns, masks, eyepatch, goggles; Spine_03: capes, backpacks, shawl, collar; Hips: pouches, bags, bottles). The spike measures these offsets.
9. **Inspector hook.** (a) `Handles` is true for an NpcDefinition whose `visualPrefab` is NULL (a new NPC) or carries a `ModelAppearanceBinder`; for those the section shows the creator, or (null prefab) a button "Use the Viking NPC base prefab". Other NPCs (the goblins) are not handled and see nothing new.
   (b) The toolkit re-evaluates when `visualPrefab` changes (`TrackPropertyValue` rebuilds the section). (c) The hook returns an `IDisposable`-capable element and `NpcDefinitionEditor.OnDisable` disposes it (the preview utility must be cleaned up).
   (d) The toolkit wraps every hook call in try/catch and tolerates a null `visualPrefab`; the existing test `EverySerializedFieldOnNpcDefinitionIsDrawnByTheCustomInspector` (`AppearanceStorageTests.cs:343-358`) builds the inspector on a bare NpcDefinition and must still pass inside VikingRaid, where the project hook is found.
10. **Preview.** `PreviewRenderUtility` has never been used in this repo, so the first job is to prove it under URP 17.6 (it is a spike step, not an assumption). Idle pose: a `PlayableGraph` with an `AnimationClipPlayable` and `Evaluate()` (or `AnimationClip.SampleAnimation`) on an
    `Object.Instantiate` copy, NOT `AnimationMode` (a global editor mode) and not `InstantiatePrefab`. An inspector must not touch `EditorSettings.asyncShaderCompilation`: repaint while `ShaderUtil.anythingCompiling`. (The spike restores the setting it changes.)
11. **The 15 palettes are different albedo TEXTURES and change the face tile too** (skin and face paint, not only cloth). The row is "Look (skin and cloth)", options labelled by palette; the "no separate skin tone" claim becomes "skin comes with the palette".
12. **Villager coupling.** `VillageContentAuthor` hard-codes the three prefab paths and throws if they are missing; `VillageSceneAuthor.RunUnattended` calls `RunVillagers`. Both change first, the old prefabs are deleted last. The appearance entries are SEEDED ONCE when an NPC asset is created
    (an author re-run never overwrites what was edited in the inspector).
13. **Tests and assemblies.** VikingRaid has no save/load path: the save test calls `SaveService` capture/restore and `WorldSpawnService.BindRestoredView` directly. Code in the predefined Assembly-CSharp-Editor cannot be referenced by test assemblies, so the catalog author and the inspector
    go in an editor-only asmdef `VikingRaid.Appearance.Editor` (references `VikingRaid.Appearance`, `CRPG.Editor`, `CRPG.Core`, `CRPG.Actors`, `CRPG.Presentation`), the runtime in `VikingRaid.Appearance` (CRPG.Core, CRPG.Actors, CRPG.Gameplay; autoReferenced); PlayModeTests gains `CRPG.Saving` and
    `VikingRaid.Appearance`, EditModeTests gains `VikingRaid.Appearance`, `VikingRaid.Appearance.Editor` and `CRPG.Gameplay`.
14. **Catalog corrections.** Viking crowns are 4 (`Crown_01_Alt` exists); War Camp `Collar_01` is a static piece (goes with the Wear tab if it fits); Generic also has beards, hair 04-11, bun, ponytail, chops, moustache, which are dropped on purpose (only the spike's fit decides whether any come back). Skinned hair is 8
    (01, 01_Alt, 02, 03, 03_alt, 04, 04_Alt, 05) and `ToggleChild` names come from the MASTER's child names (`SM_Chr_Attach_Hair_03_alt` is lower case there, `_Alt` in the prefab file). Stock looks that a single row cannot express (Warrior_Male_01 starts with both Wolf_01 and Fur_04 active) are not reproduced.
15. **First frame.** The binder binds in `Start` (the agent is already bound synchronously by `BindView`) with `Update` as the fallback, so no frame shows the master's default look.
16. **Later collisions (noted, not solved here):** `CharacterWrapperBuilder` adds an `EquipmentVisualBinder`, whose helmets will overlap the headwear row; property blocks take renderers out of the SRP Batcher (hair and beard only).

## 11. Spike results (2026-10-06, judged from the contact sheets in `VikingRaid_Logs/spikes/appearance_*.png`) -- these SETTLE Section 8 and supersede Sections 3 and 10 where they differ

**Answers to the gate questions**
1. **Mounting.** Static attachments and own-rig capes are authored with their pivot AT the bone (mounting them at their authored world pose put them 1.5 m below the head). The one rule that works for every family and every bone (Head, Spine_03, Hips; Viking, War Camp, Generic):
   local position zero, local rotation = the inverse of the bone's rest WORLD rotation (so the attachment stands upright in the world at the rest pose, and follows the bone when animated). Measured: Head rest rotation is effectively identity (B and C are identical for every head item); Spine_03 needs euler (86, 270, 0); Hips needs (279, 90, 0). The author computes the rotation per bone from the master prefab and stores it in the catalog as plain local data; the runtime only ever sets local transforms.
2. **Fit.** War Camp hats (7), helmets (2), crowns, hoods (3, re-skinned by bone order), masks, goggles, eyepatch, hair, capes (5), backpacks, bags, bottles and Generic hair all sit correctly on the Viking bodies. Viking helmets, horns, wings, crowns, eyepatch, pouches, static hair 06-09, capes (2) and the skinned hair, furs, wolf and bear (extracted from the master clones by renderer) fit too.
3. **Rigged extraction works** (`all bones mapped` for every hood, shawl, hair, fur, wolf, bear); the rigid cape rig mounts and hangs correctly (the red, brown, tan and blue capes are all visible from behind).
4. **Colour.** The 15 palettes are genuinely different looks, each bundling beard/hair colour (red, brown, blond), face-paint colour and style (blue, red, green, purple, none), tunic colour and skin tone: so there is NO separate hair-colour row (the tint idea is dropped: it could only darken, and the palettes already give the colour range). War Camp and Generic parts keep their own materials (black/grey/brown hair etc.).
5. **Every one of the 10 bodies has the same bald head with a BIG BEARD baked into the body mesh**; only face paint, headband and outfit differ. Beard attachments (Viking 5, War Camp 2, Generic) therefore only overlay a second beard on top of the baked one (they look like a muddle). The beard row is DROPPED; facial hair is whatever the body and palette give.
6. Not verified and deliberately left for the build: the `PreviewRenderUtility` under URP (a first task of the inspector), and the Viking `Hair_*`/`Fur_*` names inside the master versus the prefab files (toggle names are read from the master).

**The final structure: three tabs, eight rows** (every row has "None" first unless noted)

| Tab | Row | Options |
|---|---|---|
| Body | `body` (required) | the 10 bodies |
| | `look` (required) | the 15 Viking palettes (skin, beard/hair colour, face paint, tunic) |
| Head | `hair` | Viking skinned 8 (01, 01_Alt, 02, 03, 03_alt, 04, 04_Alt, 05) + Viking static 06-09 + War Camp 01-07 + Generic hair (Hair_04..11 incl. 09_alt, Bun, Ponytail) |
| | `headwear` | Viking helmets 4, horns 3, wings 1, crowns 4 (incl. 01_Alt), wolf and bear pelts (skinned); War Camp hats 7, helmets 2, crowns 5, hoods 3 (rigged) |
| | `face` | eyepatch (Viking, War Camp), goggles, masks 2 |
| Wear | `cloak` | capes: Viking 2, War Camp 5 (rigid rig on Spine_03) |
| | `shoulders` | furs 1-4 (skinned), shawl (rigged), collar (static, Spine_03) |
| | `gear` | pouches 2, bags 5, backpacks 2, bottles 2 |

Dropped on purpose: beards of every pack, Generic's modern items (headsets, sunglasses, beanies, caps, hats, goggles), glider, skull, rope, straw, the hair-colour row. `Look` replaces the material of Viking-family renderers only (body, skinned hair/fur, Viking static parts).

## 12. As built (2026-10-06)

**Toolkit (`crpg-toolkit`)**: `CRPG.Editor.Npc.INpcAppearanceEditor` + the extension host in `NpcDefinitionEditor` (TypeCache discovery, rebuild on `visualPrefab` change, dispose, try/catch). 6 hook tests; CHANGELOG entry.

**Viking Raid (`VikingRaid/Assets`)**
- `Appearance/` (`VikingRaid.Appearance`): `ModelAppearanceCatalog` (8 rows, 3 tabs, 115 options; plain data), `ModelAppearanceApplier` (pure Unity, total and idempotent, local poses only), `ModelAppearancePart`, `ModelAppearanceBinder` (self-binds in `Start`, `Update` fallback, `Refresh()`), seeded `ModelAppearanceRandomizer`.
- `Editor/AppearanceCreator/` (`VikingRaid.Appearance.Editor`): `ModelAppearanceCatalogAuthor` (menu `Viking Raid/Appearance/Build Catalog`; throws listing every missing pack asset; asserts unique ids; bone rest rotations computed from the master), `VikingNpcAppearanceModel` (rows on `CreatorRow`, quiet `Load`), `ModelAppearancePreview` (`PreviewRenderUtility`, idle pose, orbit/zoom, head framing on the Head tab), `VikingNpcAppearanceElement` + `VikingNpcAppearanceEditor` (the inspector UI), `AppearancePreviewProbe` (graphics diagnostics: preview samples and per-row contact sheets in `VikingRaid_Logs/spikes/`).
- `Assets/Characters/Villagers/VikingNpcBase.prefab`: the master rig behind the toolkit character wrapper + hero animation profile + `ModelActionCues` + yaw/phase fixes + the binder. The three villagers use it; `VillageContentAuthor` seeds their `appearance` ONCE (only when empty) and migrates an NPC that still points at an old `Villager_*` prefab; the three old prefabs are deleted.

**Findings while building**
- `PreviewRenderUtility` works under URP 17.6 with `utility.Render(true)` (no extra setup); it also constructs and disposes cleanly under the test runner's `-nographics`.
- A humanoid clip's `SampleAnimation` writes the clip's root motion onto the model (a 197 degree turn for the UAL idle): reset the model root to identity after sampling or the preview shows the character's back.
- Looks are fully expressed by the 15 palette materials; the palette family must include the MASTER's own material(s) (the author unions the Alts, the master's and the Viking attachments' materials) or the Look row silently does nothing for the stock look.
- Tuple names are lost through `Concat` of differently named tuples (a compile error, not a bug).

**Tests**: EditMode `ModelAppearanceTests` (22: catalog integrity, mesh coverage, every option applies without a log line, idempotence, mounting rule, layer/Animator/collider stripping, palette scoping, fallbacks, randomizer), `VikingNpcAppearanceEditorTests` (14: handled NPCs, base prefab, real inspector integration, rows/tabs, writes, foreign entries kept, Load never raises Changed, Reset, tab-only Randomize); PlayMode `VillageAppearanceTests` (4, real village scene: dressed from the definition, distinct looks, state change + `Refresh`, a restored view dressed from the saved STATE not the definition).

**Verification (2026-10-06)**: VikingRaid 122 EditMode + 66 PlayMode green; toolkit 1111 EditMode + 452 PlayMode (3 harness-only skips) green; 12 mutations of the applier/model/element/randomizer all killed (the missing-bone guard needed a synthetic-mesh test); 7 mutations of the toolkit hook all killed (the `visualPrefab` tracker needs a shown window, so that one test runs only with a graphics device and ignores itself under -nographics). Barrow's End EditMode: 1253 pass, 1 skipped, 4 fail; the 4 failures (stale Town_Slice bake fingerprint, BE assemblies referencing SRD5, missing sprite fixture prefabs) do not touch appearance code and were NOT checked against a baseline.

**Open**
- Palette swatches (the catalog supports one per option; none authored: the look names are 01-A..05-C, the preview is the real guide).
- Goblins (Sidekick, needs a mesh combiner), promotion of the generic parts to the toolkit.
- The inspector's reload when the raw Appearance foldout is edited relies on `TrackPropertyValue` (not exercised by a test: it fires on the editor's poll).

## 13. Beard on every body, and the Sidekick packages (2026-10-06, after the user's first look)

- **The user's first look**: loved it; spotted that the female bodies have a beard. Confirmed on `appearance_body_heads.png` (`AppearancePreviewProbe.RunBodyHeads`): ALL 10 bodies, female included, carry the same full red beard, as the spike found. Two attempts to isolate it failed: it does not sit in a distinct palette cell (the cells that look like it are clothing; deleting them removed clothing and left the beard) and the head triangles sample only skin colours, so the beard colour/shape is not separable by UV cell or vertex colour. Mesh surgery would need a different discriminator (bone weights, normals); not pursued. **Decision (user): leave the bodies as they are.** Experimental probes were deleted.
- **Sidekick Starter package** (`thirdpartyassets/SIDEKICK_Starter_Unity_2021_3_v1_0_4.unitypackage`, added 2026-10-06, NOT imported): modular Sidekick humans (11 base species `SK_HUMN_BASE_01..11`, per-part meshes HEAD HAIR EBRL/R EYEL/R EARL/R NOSE TETH FCHR (facial hair, optional) TONG WRAP + torso/arms/hands/hips/legs/feet) and outfits `SK_FANT_KNGT_17` (knight), `SK_SCFI_CIVL_09/10` (sci-fi civilian), one `SK_HORR_VILN_01` (villain): none are Viking. It would give clean faces and optional facial hair but not Viking clothing.
- **Sidekick Character Tool**: `Synty/Sidekick Character Tool` is a separate download (`ToolDownloader`, from SyntyStudios/SidekicksToolRelease on GitHub); it is NOT installed in the project (only the downloader and the goblin prefabs are). Its output prefabs (like `GoblinSpecies_01.prefab`) are raw models: to be NPCs they still need the toolkit wrapper (`CharacterWrapperBuilder` + animation profile + `ModelActionCues` + yaw fixes), which is what `CharacterRosterAuthor.BuildPrefab` does for the roster. A natural next step is an inspector button that wraps a raw model prefab for an NPC.

## 14. Sidekick spike: can the Viking-master approach carry Sidekick humans and goblins? (2026-10-06)

The user asked to use the Sidekick base models "the same as we do with the Viking villagers". The Starter package was imported (Synty folder, gitignored). Spike: `Assets/Editor/Appearance/SidekickSpike.cs` (structure) and `SidekickAssembleSpike.cs` (assembly + sheets `appearance_sidekick_{faces,outfits,colours,shapes}.png`, `sidekick_report.txt`, `sidekick_assemble_report.txt`).

**Findings**
- The assembled prefabs (`HumanSpecies_01`, `Starter_01`, `GoblinSpecies_01`) are ONE combined skinned mesh each (thousands of duplicated bones): useless as a master. The raw per-region part FBXs are the material: each is a skinned mesh with ~88 NAMED bones on one shared skeleton (`root pelvis spine_01..03 head neck_01 jaw hand_l ...`, 92 transforms), so the existing "bind a renderer to the master skeleton by bone name" mechanism works unchanged. Assembling a human from raw parts worked first try (faces, outfits, colours rendered).
- **Structure (human)**: 24 region codes. Body regions (TORS AUPL/R ALWL/R HNDL/R HIPS LEGL/R FOTL/R) have ONE base mesh each (a single body); species variation (10-11 "species" 01..11) is only in face parts: HEAD (2), HAIR 10, EBRL/R 10, EARL/R 10, NOSE 11, TETH 10, FCHR (facial hair) 10, plus shared EYEL/R, TONG. Outfits are per-region meshes in sets: Starter has `FANT_KNGT_17` (knight), `SCFI_CIVL_09`, `SCFI_CIVL_10` (sci-fi civilians), one `HORR_VILN_01` head; none Viking. **Goblins** mirror this (97 species parts: faces 10) but with 10 full outfit sets `GOBL_FIGT_01..10` x 33 regions (330 files): the richer family and it fits the game.
- **Body shape** is blend shapes: torso/arms/etc. have 4 (`defaultBuff`, `defaultHeavy`, `defaultSkinny`, `masculineFeminine`); the head has 72 (mostly face-animation). So a "Build" row set is cheap (weights on the body parts).
- **Colour** is a COLOUR-MAP texture per character set (`T_HumanSpecies_0N`, `T_Starter_0N`, goblin equivalents) on one Sidekick shader graph material (`_ColorMap`, plus `_SkinColor`/`_SkinColorAmount` tint and dirt/dark/eyeliner amounts); the maps are UV-layout specific: a species map on a Starter outfit gives nonsense red, so colour must be one family-matched map for the WHOLE character (Starter maps carry skin + outfit).
- **Snags**: some hair parts have dynamic bones the HumanSpecies skeleton lacks (1-5 unmapped bones: stray strands down the legs); use the Starter skeleton (has `abac_dyn_*`) or map a missing bone to its nearest present ancestor instead of rejecting the mesh (the Viking applier rejects). Part materials are placeholders (`Lit`): assign the family colour-map material. Humans' Viking-unfitting outfits are the main product problem; goblins are the better first family.

**Implication for the design (not built)**: generalise the creator into FAMILIES (Viking done; Sidekick Goblin; Sidekick Human) sharing the catalog/applier/binder/inspector: add option kinds for a part SET (an outfit = N region meshes bound together, replacing the body parts for those regions), a blend-shape weight set (Build), and a family colour-map material; tabs maybe Face / Outfit / Colour / Build. Needs design + Opus review before building (standing process).

## 15. Viking pieces on Sidekick humans, Step 1 spike (2026-10-06, no Blender)

The user liked the plan: (1) try the Polygon Viking attachments on a Sidekick human with no Blender, (2) if clothing is the only gap, one outfit in Blender and a go/no-go from the render. Spike: `Assets/Editor/Appearance/SidekickVikingSpike.cs` (menu items under `Viking Raid/Appearance/Spike: ...`; sheets `appearance_sidekick_viking_{head,body}.png`, `appearance_sidekick_viking2_{head,body}.png`, reports alongside). Mounting rule unchanged from the Polygon work: clone under a Sidekick bone (`head`, `spine_03`, `pelvis`), local position zero, local rotation = the inverse of the bone's rest world rotation (upright in the world), plus ONE uniform scale = Sidekick human height / Polygon height (1.759 / 1.88 = 0.935; the head bones sit at nearly the same height, 1.559 vs 1.569 m).

**Results (judged on the sheets, T-pose)**
- FIT with no per-piece tuning: Polygon Viking static hair 06-09, Generic hair (Hair_04-11, Bun, Ponytail), War Camp hair 01-07, **Viking helmets 4, horns 3, wings, crowns 4, eyepatch**, War Camp hats 7, helmets 2, crowns 5, masks, goggles, capes (Viking 2, War Camp 5), backpacks 2, bags 5, pouches 2, bottles 2, collar.
- FIT after BAKING (a skinned Polygon piece is baked at rest with `SkinnedMeshRenderer.BakeMesh` into a rigid mesh and carried over at its pose relative to the Polygon `Head` bone, scaled): the **8 Viking skinned hairs (braids, long, swept)**, **wolf pelt**, and every **beard** (Viking 5, War Camp 2, Generic 2, chops, moustache; these were dropped for the Viking bodies because they overlaid a baked beard, and on the clean-shaven Sidekick face they work).
- NEEDS WORK: **shoulder furs** (Fur_01-04) land small and high at the armpits (the Sidekick shoulders are narrower: they need a per-piece offset/scale); **bear pelt** and **Viking Crown_01** render bright cyan (a Synty shader/material issue with those two, to look at); the War Camp RIGGED hoods and shawl were not tried.
- NOT FIT (by design): Viking skinned hair 01-05 mounted as whole characters (invisible: they must be baked, as above).
- THE GAP: clothing (tunic, trousers, boots, cloak body). The Starter pack's outfits are knight / sci-fi / villain. That is what Step 2 (one outfit in Blender) would answer.
- Pipeline implication: skinned Polygon pieces need an author-time bake into mesh assets (one per piece) under our own folder (derived assets stay local like the packs).

## 16. Step 2: one Viking outfit for the Sidekick human, built in Blender (2026-10-06)

Scripts and method: `Blender/SidekickVikingOutfit/` (README, `build_outfit.py`, `common.py`, diagnostics); Unity side: `SidekickHumanImportSettings` (postprocessor), `SidekickHumanOutfitSpike` (in-memory human catalog through the REAL applier, preview renders), `PartImportAudit`. Output FBXs: `Assets/Appearance/SidekickHumans/Outfits/VikingTunic/SK_VKNG_TUNC_01_{10TORS,11AUPL,12AUPR,13ALWL,14ALWR,17HIPS,18LEGL,19LEGR,20FOTL,21FOTR}_HU01.fbx`; `Assets/Appearance/SidekickHumans/HumanMaster.prefab` (skeleton-only human master, built like the goblin master).

**Result: the whole path works.** The outfit (tunic with belt, collar and cuffs, a skirt flaring to the hem, trousers, fur-cuffed boots) is the body regions' own meshes inflated along unified normals; skin weights, shape keys and topology are the body's, so no weight painting was needed. It binds through the existing part-set applier and bind bones, deforms in the idle pose, follows the body shape keys, recolours with the four Starter colour maps, and carries the Polygon accessories (horned helm, beard, cape, pouch) on top. Evidence: `VikingRaid_Logs/spikes/appearance_human_viking_*.png`, `blender_outfit_*.png`.

**What it cost / what to know**
- Blender's headless FBX round trip needs care: apply scale and rotation first and bake the axis conversion (`bake_space_transform=True`), or the bones keep a 0.01 scale and the bind poses are in centimetres (the applier's bind bones then land 114 m away and the part vanishes). Unity's `animationType None` imports the meshes as STATIC renderers: use Generic + NoAvatar (postprocessor, with `GetVersion` so old imports refresh).
- The clothing is a "second skin": same surface, flat colour cells. It reads as a Viking with the accessories; fuller garments (extruded belt, hem, buckles, a hood, looser sleeves, fur trim) are the next artistic layer and are edits to the script's classification and thickness plus extrusions (the bmesh extrude must also move every shape-key layer).
- Colours: cells map to different colours in each Starter palette (maps 2-4 turn this outfit orange/purple), so a curated look list per outfit is needed. Starter 1 is the earthy one.
- The Sidekick human faces -Z after the idle clip: the preview needs a per-family yaw (today 0, the goblin faces the camera). Outfit pieces are not yet in a human catalog (the spike used an in-memory one).
- First frames of Polygon accessories render white or cyan until their shader graphs compile (the inspector already repaints while compiling).

**To productise (Step 3, needs its own design + review)**: `Human` family (catalog author, `HumanNpcBase.prefab`, rows Face (face, hair, facial hair), Outfit (torso, legs), Gear (headwear, cloak, gear), Colour (look, build)); Polygon static accessories mounted by bone name with the author-computed upright rotation and the 0.935 scale; an author-time BAKE of the skinned Polygon hairs, pelts and furs into rigid mesh assets; a family yaw; more outfits.

## 17. Opus review of the Sidekick-human plan (2026-10-06) and what it changed

The user asked for the Step 1-2 solution and the proposed Step 3 to be put to an Opus subagent (read-only). The claims below were checked against the files by me before acting; the first three were wrong in my own work.

**Corrections (verified)**
1. **The accessories in the Step 2 renders were mounted backwards.** `SidekickHumanOutfitSpike.Mount` used the bone's LIVE rotation after the idle pose had turned the human (it faces -Z) and tilted the head, so the cape hung in front of the legs and the beard sat on the back of the neck. FIXED: the piece is made upright for the bone's REST rotation read from the master PREFAB ASSET (the same rule as the applier's `RestPose`); re-rendered, the cape is behind, the beard on the face, the pouch at the hip. The Step 1 T-pose sheets were unaffected (unposed skeleton). The Section 16 line "carries the accessories on top" held only after this fix.
2. **The derived meshes were NOT gitignored** (`.gitignore` covered only `VikingRaid/Assets/Synty/`; origin is GitHub). FIXED: `VikingRaid/Assets/Appearance/SidekickHumans/` and its `.meta` are now ignored (`git check-ignore` confirms the tunic FBXs and `HumanMaster.prefab`); the README had claimed they were. Any future baked-accessory folder (derived Polygon meshes) needs the same rule BEFORE a commit.
3. **"Follows the body shape keys" is unverified in Unity.** The maths is sound (the same offset is added to the Basis and every key) but the spike catalog had no Build row, the exported shape names were not resolved through `BlendShapeIndex`, and `importBlendShapeNormals` is not set (recalculated normals can smooth the faceted mesh). To verify in Step 3 tests.
4. **Seams close only within ONE outfit.** Unified normals plus a position-only thickness close seams inside an outfit; they do NOT close at the wrist (the hands are not garments: the sleeve ring moves out 1.5 cm, the hand stays) or at the waist/ankle when torso and legs come from different outfits (1-1.5 cm cracks). Policy for Step 3: one `outfit` row for the whole body at first, or taper the offset to 0 at every boundary that can meet a foreign part.
5. **"Skirt flaring to the hem" is over-claimed:** `17HIPS` contains the thighs (y 39..99), so the flare inflates each thigh separately (flared shorts, a gap at the crotch). Belt, collar, cuffs and fur cuff are colour bands at the tunic's own thickness; the collar actually thins toward the neck (the taper starts at y 138, the band is 137.5-141).
6. **Submeshes.** Verified with `PartImportAudit`: the outfit parts have 4 (torso) and 3 (leg) submeshes (one per preview material that survives into the FBX); Synty's own parts already have 2. Collapse to one material before export.
7. Minor: the seam key rounds to 0.5 mm bins (a KD-tree merge is safer); the neck taper uses `abs(x) < 11` while `classify` uses 12; the feet inflate 1.7 cm so the soles sink; exported renderers keep the name `SK_HUMN_BASE_01_*`; the Viking hair tile with a cyan crystal material is real and was unlisted.
8. The -Z facing is likely the HERO (UAL) clip, not the Sidekick human (the goblin was posed with the goblin idle). Do NOT correct it with `ModelYawCorrection` (it returns to 0 during actions, so the character would flip on every attack). Find the cause in game, then store one family constant that both the preview and the runtime read.

**Ideas (ranked by Opus; my notes in brackets)**
1. A Blender "transfer" helper: bind ANY mesh to the body with Surface Deform, bake each body shape key by evaluating the deformed mesh with the key at 1, copy weights with Data Transfer (nearest face): turns any mesh (a hand-built skirt cone, a Polygon shawl/hood/fur/pelt) into a skinned, shape-keyed part on the same rig. [wanted first, about 40 lines]
2. Real relief in the existing script: edge loops at the band edges (`bmesh.ops.bisect_plane`, check the shape-key layers), category-dependent thickness (belt +1 cm, raised collar, cuffs, a noisy fur ring, a sole that only grows downward). [wanted]
3. Layer existing Polygon pieces (capes, shawls, bags, pelts) on top, rigidly or through idea 1. [Step 1 already does the rigid version]
4. Woven trim: alternate two tan cells and a dark stripe on hem and collar faces (free). [cheap]
5. The Starter civilian with ITS OWN map. [DONE, result below]
6. Goblin outfits on humans: probably no (the rest poses differ, bind bones would keep goblin proportions); measure with `BindPoseAudit` first. [not pursued]

**Check done: the Starter civilian and knight outfits with their own Starter colour maps** (`VikingRaid_Logs/spikes/appearance_human_starter_outfits.png`, `SidekickStarterOutfitCheck`): both are much more detailed than the inflated suit (sash, shoulder cloth, bracers, studded leather breastplate, tall boots) and recolour well across maps 1-4, but they read as fantasy adventurer, not Viking, and the hair colour is baked into the map (blue in maps 2-4). Implication: the best Viking human is probably a KITBASH: the Starter knight leather and civilian clothes in the earthy map (1), plus the Polygon Viking helmets, beards, capes, furs and pouches (Step 1), plus our own plain tunic (Step 2) for peasants.

**Risks and requirements for Step 3 (Opus plus mine)**
- Colour: the Starter maps carry skin and clothing together, so Look cannot change skin tone alone; consider script-generated 32x32 palettes with Synty's face/skin/hair cells kept in place and an outfit block we own; avoid MaterialPropertyBlock tints (they break the SRP Batcher).
- Cost: `updateWhenOffscreen = true` on every part is costly for a village of NPCs (set `localBounds` from the largest blend-shape extent instead); a mesh merge is easy later (one material, one skeleton; blend shapes need zero-padded frames).
- Rigid accessories clip on Buff (8.6 cm) and Heavy (16 cm) bodies (idea 1 fixes it). `masculineFeminine` range is still unverified.
- The female-beard lesson: no base or outfit mesh may bake in a gendered or optional feature (render every base at every Build as an audit); Sidekick facial hair and Polygon beards must share ONE slot; helmets must limit hair; the randomizer must not put beards on feminine builds unless asked.
- Ids are permanent (the author keeps a retired-to-replacement map and fails when a published id disappears).
- Tests that would have caught this session's bugs: every allowed cross-slot mix has coincident boundary vertices (<= 1 mm) at rest and at each Build weight; an idle-sampled accessory points the same way as the character; every outfit part resolves the 4 shape names; `BindPoseAudit` on the Blender parts; a rerun of the script gives the same mesh.

# Sidekick human: Viking outfit (headless Blender)

Builds Viking clothing for the Sidekick human from the pack's own base body parts and exports one FBX per region into
`VikingRaid/Assets/Appearance/SidekickHumans/Outfits/VikingTunic/` (our folder; the Synty files are never touched). These meshes are DERIVED from Synty geometry: `.gitignore` excludes `VikingRaid/Assets/Appearance/SidekickHumans/` so they are never committed or redistributed; read the Synty EULA before distributing anything.

```
BLENDER="/c/Program Files/Blender Foundation/Blender 5.2/blender.exe"
# preview renders only (Workbench, front/back/side):
"$BLENDER" --background --python Blender/SidekickVikingOutfit/build_outfit.py -- "" "C:/.../VikingRaid_Logs/spikes/blender_outfit"
# export the FBXs:
"$BLENDER" --background --python Blender/SidekickVikingOutfit/build_outfit.py -- "C:/.../VikingRaid/Assets/Appearance/SidekickHumans/Outfits/VikingTunic" ""
```
Always pass ABSOLUTE output paths (Blender resolves relative ones against its own cwd). `explore_parts.py` and `render_base.py` are diagnostics.

## Method
- The garment of a region IS that region's body mesh, inflated along UNIFIED vertex normals (summed over all regions at each shared position: separate region meshes otherwise crack at the seams) by a height-dependent thickness (`thickness()`): tunic 1.5 cm, skirt flaring to 4.5 cm at the hem, trousers 1.0, boots 1.6-1.7, the neck stump tapers to zero so the neck stays skin.
- The SAME offset is added to every shape key (`defaultBuff`, `defaultHeavy`, `defaultSkinny`, `masculineFeminine`), so the garment follows the body shapes. Vertex groups (skin weights) are untouched: the topology is the body's own.
- Colour = UVs: every face points at one cell of the Sidekick colour map (`CELLS`, `(uv x, uv row from the bottom)`, read off the Starter palette sheet), so the outfit recolours with whichever colour map the character wears. Faces left skin keep their original UVs.
- Flat shading (low-poly look); custom split normals cleared.

## Export gotchas (each cost a debugging round)
1. The importer leaves the armature and meshes at scale 0.01 / rotation 90 degrees (centimetre data, Y-up). Exporting that gives bones with scale 0.01 and bind poses in CENTIMETRES; Unity's applier assumes the rig frame in METRES. `apply_transforms()` applies scale and rotation to the armature and all meshes together, and the export uses `bake_space_transform=True`.
2. Unity: `animationType = None` imports the meshes as STATIC renderers (no SkinnedMeshRenderer at all). `SidekickHumanImportSettings` uses Generic + NoAvatar for `Assets/Appearance/SidekickHumans/`; bump its `GetVersion()` when the settings change, or old imports stay cached.
3. Verify an export with `PartImportAudit` (Unity): SMR scale 1, bone lossyScale 1, mesh bounds in metres, bind pose translations equal to Synty's.

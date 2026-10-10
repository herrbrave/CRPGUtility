"""Diagnostic: how do the Sidekick human body-part FBXs look inside Blender (units, size, shape keys, weights, armature)?
Run:  blender --background --python explore_parts.py -- <out.txt>
"""
import sys
import bpy

ROOT = "C:/Users/Peter/wkspaces/CRPGUnity/VikingRaid/Assets/Synty/SidekickCharacters/Resources/Meshes/Species/Humans/"
REGIONS = ["10TORS", "11AUPL", "13ALWL", "15HNDL", "17HIPS", "18LEGL", "20FOTL"]
out_path = sys.argv[sys.argv.index("--") + 1]
lines = []

for region in REGIONS:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=f"{ROOT}SK_HUMN_BASE_01_{region}_HU01.fbx")
    scene = bpy.context.scene
    lines.append(f"== {region}: unit scale {scene.unit_settings.scale_length}, objects: " + ", ".join(f"{o.name}[{o.type}]" for o in bpy.data.objects))
    for o in bpy.data.objects:
        if o.type == 'MESH':
            me = o.data
            xs = [v.co.x for v in me.vertices]; ys = [v.co.y for v in me.vertices]; zs = [v.co.z for v in me.vertices]
            keys = [k.name for k in me.shape_keys.key_blocks] if me.shape_keys else []
            lines.append(f"  mesh {o.name}: verts {len(me.vertices)} faces {len(me.polygons)} vertex groups {len(o.vertex_groups)} uv layers {[u.name for u in me.uv_layers]}")
            lines.append(f"    bounds x {min(xs):.3f}..{max(xs):.3f} y {min(ys):.3f}..{max(ys):.3f} z {min(zs):.3f}..{max(zs):.3f}; world matrix scale {o.matrix_world.to_scale()}, rotation {o.matrix_world.to_euler()}")
            lines.append(f"    shape keys: {keys}")
            lines.append(f"    modifiers: {[m.type for m in o.modifiers]}, parent {o.parent.name if o.parent else None}, materials {[m.name for m in me.materials]}")
            lines.append(f"    sharp edges: {sum(1 for e in me.edges if e.use_edge_sharp)}, auto smooth/normals domain: {me.normals_domain}")
        elif o.type == 'ARMATURE':
            a = o.data
            lines.append(f"  armature {o.name}: {len(a.bones)} bones, scale {o.matrix_world.to_scale()}, rotation {o.matrix_world.to_euler()}; e.g. head head={tuple(round(c, 3) for c in a.bones['head'].head_local) if 'head' in a.bones else None}")

open(out_path, "w").write("\n".join(lines))
print("\n".join(lines))

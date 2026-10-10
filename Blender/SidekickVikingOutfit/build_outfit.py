"""Builds the first Viking outfit for the Sidekick human ("Viking tunic": tunic with belt, collar and cuffs, trousers, fur-cuffed boots) from the base body parts, exports one FBX per region
and writes Workbench preview renders.

Method (all in the parts' local centimetre, Y-up space):
  * every garment region is the BODY region's own mesh, inflated along UNIFIED vertex normals (summed over all regions at each shared position, so the seams between the separate region meshes stay
    closed) by a thickness that depends on height (tunic 1.5 cm with the skirt flaring to 4.5 cm at the hem, trousers 1.0, boots 1.6-1.7; the neck stump tapers to zero so the neck stays skin);
  * the SAME offset is added to every shape key (Buff, Heavy, Skinny, masculineFeminine), so the garment follows the body shapes; vertex groups (skin weights) are untouched;
  * each face gets its colour by pointing all its UVs at one cell of the Sidekick colour map (a 32x32 palette), so the outfit recolours with whichever colour map the character uses; faces that stay
    skin (the neck) keep their original UVs;
  * flat shading, like the rest of the low-poly style.

Run: blender --background --python build_outfit.py -- <export folder> <preview prefix>
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy
import common
from mathutils import Vector

OUT = common.arg(0)
PREVIEW = common.arg(1)

GARMENTS = ["10TORS", "11AUPL", "12AUPR", "13ALWL", "14ALWR", "17HIPS", "18LEGL", "19LEGR", "20FOTL", "21FOTR"]
# colour map cells as (uv x, uv row from the BOTTOM), read off the Starter palette sheet; the RGB is only for the Workbench preview
CELLS = {
    "tunic": ((4, 28), (0.61, 0.51, 0.38)),
    "trim": ((8, 16), (0.43, 0.31, 0.20)),
    "belt": ((8, 16), (0.43, 0.31, 0.20)),
    "trousers": ((4, 18), (0.31, 0.24, 0.18)),
    "boots": ((7, 16), (0.56, 0.40, 0.27)),
    "fur": ((12, 18), (0.50, 0.44, 0.38)),
    # the trim split by place: the same colour cell, but each stands proud by its own amount (RELIEF below)
    "collar": ((8, 16), (0.43, 0.31, 0.20)),
    "cuff": ((8, 16), (0.43, 0.31, 0.20)),
    "hem": ((8, 16), (0.43, 0.31, 0.20)),
    "buckle": ((6, 4), (0.62, 0.64, 0.68)),
}
# RELIEF: how far (cm, along the unified normal) the vertices of each band's faces are raised on top of the garment thickness. The body meshes are low-poly with irregular spacing, so the bands are
# raised by their VERTICES (no new geometry): the band faces stand proud and the neighbouring faces slope down from them, a bevelled step, and because the lift is keyed by position across ALL regions
# the seams between region meshes stay closed. The lift fades with the thickness taper (neck, wrist, sole) so the cuff still meets the hand.
RELIEF = {"belt": 1.2, "collar": 1.2, "cuff": 0.9, "hem": 1.0, "fur": 1.6, "buckle": 2.4}
SKIN_RGB = (0.83, 0.64, 0.45)


def smoothstep(t):
    t = max(0.0, min(1.0, t))
    return t * t * (3.0 - 2.0 * t)


def thickness(co):
    x, y, z = co
    if abs(x) > 66.0:                  # the sleeve ends at the wrist, where the BARE hand begins: taper to nothing so the cuff meets the hand without a gap
        return 1.5 * max(0.0, (72.8 - abs(x)) / 6.8)
    if y > 138.0 and abs(x) < 12.0:   # the neck stump of the torso mesh tapers to nothing (same width as classify())
        return max(0.0, 1.5 * (143.5 - y) / 5.5)
    if y >= 100.0: return 1.5          # tunic: torso and sleeves, a little loose
    if y >= 68.0: return 1.5 + 3.0 * smoothstep((100.0 - y) / 32.0)   # the skirt flares out toward the hem
    if y >= 24.0: return 1.0           # trousers
    if y >= 12.0: return 1.6           # boot shafts
    return 1.7 * min(1.0, y / 2.5)     # feet: the sole does not grow downward through the floor


def classify(region, c):
    """The category of a face by its centroid, or None to leave it skin."""
    x, y, z = c
    if region == "10TORS":
        if y > 141.0 and abs(x) < 12.0: return None
        if y > 137.5 and abs(x) < 12.0: return "collar"
        if y < 97.0: return "belt"
        return "tunic"
    if region in ("11AUPL", "12AUPR"): return "tunic"
    if region in ("13ALWL", "14ALWR"): return "cuff" if abs(x) > 66.0 else "tunic"   # cuff at the wrist
    if region == "17HIPS":
        if y >= 93.0: return "belt"
        if y >= 70.0: return "tunic"
        if y >= 65.0: return "hem"
        return "trousers"
    if region in ("18LEGL", "19LEGR"):
        if y > 24.0: return "trousers"
        if y >= 20.0: return "fur"                          # boot cuff
        return "boots"
    return "boots"                                          # feet


def cell_uv(cell):
    return ((cell[0] + 0.5) / 32.0, (cell[1] + 0.5) / 32.0)


def unified_normals(meshes):
    """Position-keyed summed face normals over ALL regions, so a ring shared by two region meshes gets one normal."""
    sums = {}
    key = lambda p: (round(p.x * 20), round(p.y * 20), round(p.z * 20))
    for mesh in meshes.values():
        me = mesh.data
        basis = common.basis_positions(mesh)
        for poly in me.polygons:
            pts = [basis[i] for i in poly.vertices]
            n = Vector((0, 0, 0))
            for k in range(1, len(pts) - 1):
                n += (pts[k] - pts[0]).cross(pts[k + 1] - pts[0])   # area-weighted
            for i in poly.vertices:
                sums[key(basis[i])] = sums.get(key(basis[i]), Vector((0, 0, 0))) + n
    return sums, key


def categorise(meshes):
    """{(region, polygon index): category} for every garment face, decided on the ORIGINAL body (before inflating); faces left skin are absent. The buckle is the front-centre part of the belt."""
    result = {}
    belt = []
    for region in GARMENTS:
        mesh = meshes[region]
        basis = common.basis_positions(mesh)
        for poly in mesh.data.polygons:
            c = sum((basis[i] for i in poly.vertices), Vector((0, 0, 0))) / len(poly.vertices)
            category = classify(region, c)
            if category is None:
                continue
            result[(region, poly.index)] = category
            if category == "belt" and abs(c.x) < 3.2 and c.z > 0.0:   # the front centre of the belt (+Z is the front), by a box so the asymmetric triangulation cannot make it lopsided
                belt.append(((region, poly.index), c))
    for k, c in belt:
        result[k] = "buckle"
    return result


def relief_lifts(meshes, categories, key):
    """{position key: lift (cm)}: the largest RELIEF of any garment face that uses the position, in any region."""
    lifts = {}
    for (region, index), category in categories.items():
        amount = RELIEF.get(category, 0.0)
        if amount <= 0.0:
            continue
        mesh = meshes[region]
        basis = common.basis_positions(mesh)
        for i in mesh.data.polygons[index].vertices:
            k = key(basis[i])
            lifts[k] = max(lifts.get(k, 0.0), amount)
    return lifts


def inflate(meshes, categories):
    sums, key = unified_normals(meshes)
    lifts = relief_lifts(meshes, categories, key)
    for region in GARMENTS:
        mesh = meshes[region]
        me = mesh.data
        basis = common.basis_positions(mesh)
        offsets = []
        for p in basis:
            n = sums[key(p)]
            t = thickness(p)
            lift = lifts.get(key(p), 0.0) * min(1.0, t / 1.5)
            offsets.append(n.normalized() * (t + lift) if n.length > 1e-9 else Vector((0, 0, 0)))
        if me.shape_keys:
            for kb in me.shape_keys.key_blocks:   # the SAME offset for every key: the garment follows the body shapes
                for i, off in enumerate(offsets):
                    kb.data[i].co = kb.data[i].co + off
        for i, off in enumerate(offsets):
            me.vertices[i].co = me.vertices[i].co + off if not me.shape_keys else Vector(me.shape_keys.key_blocks[0].data[i].co)
        me.update()


def colour_and_flatten(meshes, categories):
    mats = {}
    for name, (cell, rgb) in CELLS.items():
        m = bpy.data.materials.new("preview_" + name)
        m.diffuse_color = (rgb[0], rgb[1], rgb[2], 1.0)
        mats[name] = m
    skin = bpy.data.materials.new("preview_skin"); skin.diffuse_color = (*SKIN_RGB, 1.0)
    stats = {}
    for region, mesh in meshes.items():
        me = mesh.data
        me.materials.clear()
        slots = list(mats.keys()) + ["skin"]
        for name in slots:
            me.materials.append(mats[name] if name in mats else skin)
        uv = me.uv_layers["map1"].data
        basis = common.basis_positions(mesh)
        for poly in me.polygons:
            poly.use_smooth = False
            if region not in GARMENTS:
                poly.material_index = slots.index("skin"); continue
            category = categories.get((region, poly.index))
            if category is None:
                poly.material_index = slots.index("skin"); continue
            poly.material_index = slots.index(category)
            u, v = cell_uv(CELLS[category][0])
            for loop in poly.loop_indices:
                uv[loop].uv = (u, v)
            stats[(region, category)] = stats.get((region, category), 0) + 1
    return stats


def clear_custom_normals(meshes):
    """The imported meshes carry custom split normals that no longer match the inflated shape."""
    for mesh in meshes.values():
        bpy.ops.object.select_all(action='DESELECT')
        mesh.select_set(True)
        bpy.context.view_layer.objects.active = mesh
        try:
            bpy.ops.mesh.customdata_custom_splitnormals_clear()
        except RuntimeError as e:
            print("could not clear custom normals on", mesh.name, e)


def apply_transforms(meshes, arm):
    """The importer leaves the armature and meshes with a 0.01 scale and a 90 degree rotation (centimetre data, Y-up). Unity needs what Synty ships: unit-scale bones, bind poses in METRES and the mesh in
    the rig frame (the applier places a part's renderer at the rig's identity). Applying scale and rotation to the armature and all meshes together turns the data into metres; the FBX export then bakes the
    axis conversion into the data instead of leaving it as a node rotation."""
    bpy.ops.object.select_all(action='DESELECT')
    arm.select_set(True)
    for mesh in meshes.values():
        mesh.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)


def single_material(meshes):
    """One material (one submesh, one draw call) per part: the categories live in the UVs, the preview materials were only for the Workbench render."""
    flat = bpy.data.materials.new("outfit")
    for mesh in meshes.values():
        me = mesh.data
        me.materials.clear()
        me.materials.append(flat)
        for poly in me.polygons:
            poly.material_index = 0


def export(meshes, arm):
    os.makedirs(OUT, exist_ok=True)
    single_material(meshes)
    apply_transforms(meshes, arm)
    for region in GARMENTS:
        mesh = meshes[region]
        bpy.ops.object.select_all(action='DESELECT')
        arm.select_set(True)
        mesh.select_set(True)
        bpy.context.view_layer.objects.active = arm
        path = os.path.join(OUT, f"SK_VKNG_TUNC_01_{region}_HU01.fbx")
        bpy.ops.export_scene.fbx(
            filepath=path, use_selection=True, object_types={'ARMATURE', 'MESH'}, use_mesh_modifiers=False, add_leaf_bones=False,
            bake_anim=False, use_armature_deform_only=False, mesh_smooth_type='FACE', apply_scale_options='FBX_SCALE_ALL',
            axis_forward='-Z', axis_up='Y', bake_space_transform=True, path_mode='AUTO')
        print("exported", path)


meshes, arm = common.import_body()
categories = categorise(meshes)
inflate(meshes, categories)
clear_custom_normals(meshes)
stats = colour_and_flatten(meshes, categories)
for k, v in sorted(stats.items()):
    print("faces", k, v)
if PREVIEW:
    common.render_views(PREVIEW, color_type='MATERIAL')
if OUT:
    export(meshes, arm)

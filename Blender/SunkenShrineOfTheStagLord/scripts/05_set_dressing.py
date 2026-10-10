"""
Task 5 - Set dressing pass for Sunken Shrine of the Stag Lord.

Opens the blend produced by Tasks 1-4 and scatters non-structural props: no
traps or enemies (explicitly out of scope for this whole pipeline), and no
door leaves (lower priority than the rest of this list; the boolean-cut
openings from Task 3 stand as open archways for now).

Placement is DATA-DRIVEN off each zone's own "features" list from layout.json
(the same list that has driven every earlier task) rather than inventing
dressing a zone was never described as having - PROP_KEYWORDS maps a feature
substring to a builder function and how many to place. The stag shrine's
statue/altar/dais is bespoke (the level's one hero prop) rather than generic
scatter, called directly instead of through the keyword table.

Run headless (after 01/02/03/04 have produced blend/sunken_shrine.blend):
    blender --background --factory-startup --python 05_set_dressing.py

Produces:
    ../blend/sunken_shrine.blend              (opened, modified, saved back in place)
    ../renders/05_set_dressing_top.png
    ../renders/05_set_dressing_persp.png
"""
import bpy
import bmesh
import json
import math
import os
import random
import zlib
from mathutils import Vector

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
PROJECT_DIR = os.path.dirname(SCRIPT_DIR)
LAYOUT_PATH = os.path.join(SCRIPT_DIR, "layout.json")
BLEND_PATH = os.path.join(PROJECT_DIR, "blend", "sunken_shrine.blend")
RENDER_DIR = os.path.join(PROJECT_DIR, "renders")
os.makedirs(RENDER_DIR, exist_ok=True)

OWN_PREFIXES = [
    "prop_pine_", "prop_stump_", "prop_rock_", "prop_crate_", "prop_brazier_",
    "prop_torch_", "prop_lantern_", "prop_barrel_", "prop_gate_", "prop_bench_",
    "prop_sarc_", "prop_well_", "prop_hoard_", "prop_root_", "prop_clutter_",
    "prop_nest_", "prop_brush_", "prop_pool_", "prop_font_", "stag_statue_",
    "stag_altar_", "stag_dais_",
]

# -- helpers shared with earlier pipeline scripts ---------------------------


def deterministic_seed(text):
    return zlib.crc32(text.encode("utf-8"))


def get_or_create_material(name, color, roughness=0.85, alpha=1.0, emission=None, emission_strength=0.6):
    if name in bpy.data.materials:
        return bpy.data.materials[name]
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Base Color"].default_value = color
        if "Roughness" in bsdf.inputs:
            bsdf.inputs["Roughness"].default_value = roughness
        if alpha < 1.0 and "Alpha" in bsdf.inputs:
            bsdf.inputs["Alpha"].default_value = alpha
            mat.blend_method = "BLEND"
        if emission and "Emission Color" in bsdf.inputs:
            bsdf.inputs["Emission Color"].default_value = emission
            bsdf.inputs["Emission Strength"].default_value = emission_strength
    return mat


def get_or_create_collection(name, parent=None):
    if name in bpy.data.collections:
        return bpy.data.collections[name]
    coll = bpy.data.collections.new(name)
    (parent or bpy.context.scene.collection).children.link(coll)
    return coll


def remove_objects_with_prefixes(prefixes):
    to_remove = [o for o in bpy.data.objects if any(o.name.startswith(p) for p in prefixes)]
    for obj in to_remove:
        mesh = obj.data
        bpy.data.objects.remove(obj, do_unlink=True)
        if mesh and mesh.users == 0 and isinstance(mesh, bpy.types.Mesh):
            bpy.data.meshes.remove(mesh)


def link_only(obj, collection):
    for c in list(obj.users_collection):
        c.objects.unlink(obj)
    collection.objects.link(obj)


def add_box(name, center, size_xyz, collection, material, rotation=(0, 0, 0)):
    bpy.ops.mesh.primitive_cube_add(size=2.0, location=center)
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = (size_xyz[0] / 2.0, size_xyz[1] / 2.0, size_xyz[2] / 2.0)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    obj.rotation_euler = rotation
    obj.data.materials.append(material)
    link_only(obj, collection)
    return obj


def add_cylinder(name, center, radius, height, collection, material, verts=10, rotation=(0, 0, 0)):
    bpy.ops.mesh.primitive_cylinder_add(radius=radius, depth=height, location=center, vertices=verts)
    obj = bpy.context.active_object
    obj.name = name
    obj.rotation_euler = rotation
    obj.data.materials.append(material)
    link_only(obj, collection)
    return obj


def add_cone(name, center, radius, height, collection, material, verts=7, rotation=(0, 0, 0)):
    bpy.ops.mesh.primitive_cone_add(radius1=radius, radius2=0.0, depth=height, location=center, vertices=verts)
    obj = bpy.context.active_object
    obj.name = name
    obj.rotation_euler = rotation
    obj.data.materials.append(material)
    link_only(obj, collection)
    return obj


def ring_loft(name, rings, collection, material, verts_per_ring=10, subsurf_levels=2):
    """rings: list of (x, y, y_radius, z_radius, z_center). Builds a closed,
    capped tube through a sequence of elliptical cross-sections and recalcs
    outward normals (reliable for any closed manifold regardless of winding),
    then adds Subdivision Surface + Shade Smooth - turns a handful of blocky
    cross-sections into an organic tapered form (used for the stag statue's
    body/head, built this way rather than freeform bmesh vertex-pushing since
    it's far more predictable to get right without a live viewport)."""
    bm = bmesh.new()
    ring_verts = []
    for (x, y, ry, rz, zc) in rings:
        verts = []
        for i in range(verts_per_ring):
            ang = 2 * math.pi * i / verts_per_ring
            vy = y + math.sin(ang) * ry
            vz = zc + math.cos(ang) * rz
            verts.append(bm.verts.new((x, vy, vz)))
        ring_verts.append(verts)
    for r in range(len(ring_verts) - 1):
        a, b = ring_verts[r], ring_verts[r + 1]
        n = len(a)
        for i in range(n):
            j = (i + 1) % n
            bm.faces.new((a[i], a[j], b[j], b[i]))
    bm.faces.new(ring_verts[0])
    bm.faces.new(ring_verts[-1])
    bm.normal_update()
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])

    mesh = bpy.data.meshes.new(name + "_mesh")
    bm.to_mesh(mesh)
    bm.free()
    for p in mesh.polygons:
        p.use_smooth = True
    obj = bpy.data.objects.new(name, mesh)
    obj.data.materials.append(material)
    collection.objects.link(obj)
    mod = obj.modifiers.new("Subsurf", type="SUBSURF")
    mod.levels = subsurf_levels
    mod.render_levels = subsurf_levels
    return obj


def tapered_curve(name, points_radii, collection, material, bevel_resolution=4):
    """points_radii: list of (Vector, radius) - a Bezier curve with per-point
    radius for a tapered tube (curve-native bevel, the same technique Task 7's
    vines use). Used for the stag statue's neck/legs/antlers, which read much
    more organic as tapered curves than as uniform cylinders."""
    curve_data = bpy.data.curves.new(name, type="CURVE")
    curve_data.dimensions = "3D"
    curve_data.bevel_depth = 1.0
    curve_data.bevel_resolution = bevel_resolution
    curve_data.fill_mode = "FULL"
    spline = curve_data.splines.new("BEZIER")
    spline.bezier_points.add(len(points_radii) - 1)
    for i, (pt, rad) in enumerate(points_radii):
        bp = spline.bezier_points[i]
        bp.co = pt
        bp.radius = rad
        bp.handle_left_type = "AUTO"
        bp.handle_right_type = "AUTO"
    obj = bpy.data.objects.new(name, curve_data)
    obj.data.materials.append(material)
    collection.objects.link(obj)
    return obj


def add_rock_chunk(name, center, size, collection, material, seed):
    rnd = random.Random(seed)
    bpy.ops.mesh.primitive_cube_add(size=2.0, location=center)
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = (size[0] / 2.0, size[1] / 2.0, size[2] / 2.0)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    obj.rotation_euler = (rnd.uniform(-0.2, 0.2), rnd.uniform(-0.2, 0.2), rnd.uniform(0, math.pi * 2))
    obj.data.materials.append(material)
    link_only(obj, collection)
    return obj


def interior_point(zone, rnd, frac=0.6):
    cx, cy = zone["center"]
    w, d = zone["size"]
    return cx + rnd.uniform(-w / 2.0 * frac, w / 2.0 * frac), cy + rnd.uniform(-d / 2.0 * frac, d / 2.0 * frac)


def corner_point(zone, rnd, margin=0.8, index=None):
    """index cycles through the 4 corners explicitly rather than letting
    repeated random choices land on the same corner - a review caught two
    braziers stacking in the same spot a quarter of the time."""
    cx, cy = zone["center"]
    w, d = zone["size"]
    if index is None:
        sx, sy = rnd.choice([-1, 1]), rnd.choice([-1, 1])
    else:
        sx, sy = [(-1, -1), (1, -1), (-1, 1), (1, 1)][index % 4]
    return cx + sx * max(w / 2.0 - margin, 0.3), cy + sy * max(d / 2.0 - margin, 0.3)


def west_biased_point(zone, rnd, frac=0.6):
    """For entrance_clearing's pine trees: cluster toward the west third of
    the footprint, away from the stream which runs along the east side."""
    cx, cy = zone["center"]
    w, d = zone["size"]
    px = cx - w * 0.15 - rnd.uniform(0, w * 0.3)
    py = cy + rnd.uniform(-d / 2.0 * frac, d / 2.0 * frac)
    return px, py


# Natural-cave zones have an irregular silhouette (built in 02_cave_shell.py)
# that's much smaller than their rectangular layout.json footprint - a review
# found torches landing outside the actual cave, in the void beyond its walls.
# This replicates that script's cave_outline() with the same default params and
# the same deterministic_seed(zone_id), so it reproduces the identical polygon,
# then samples points from its fan-triangulation exactly as that script's
# stalactite scatter does, guaranteeing every point lands inside the cave.
CAVE_OUTLINE_POINTS = 16
_cave_outline_cache = {}


def _cave_outline(zone, seed):
    rnd = random.Random(seed)
    cx, cy = zone["center"]
    rx, ry = zone["size"][0] / 2.0, zone["size"][1] / 2.0
    lobe_count = rnd.randint(2, 4)
    lobes = [
        (rnd.uniform(0, 2 * math.pi), rnd.uniform(0.45, 0.9), rnd.uniform(1.2, 2.0))
        for _ in range(lobe_count)
    ]
    pts = []
    for i in range(CAVE_OUTLINE_POINTS):
        angle = 2 * math.pi * i / CAVE_OUTLINE_POINTS + rnd.uniform(-0.1, 0.1)
        r_scale = 1.0
        for la, lw, amp in lobes:
            dd = abs(((angle - la + math.pi) % (2 * math.pi)) - math.pi)
            if dd < lw:
                r_scale *= 1.0 + (amp - 1.0) * (1.0 - dd / lw)
        r_scale *= 1.0 + rnd.uniform(-0.14, 0.14)
        pts.append((cx + math.cos(angle) * rx * r_scale, cy + math.sin(angle) * ry * r_scale))
    return pts


def random_point_in_cave(zone, rnd):
    key = zone["id"]
    if key not in _cave_outline_cache:
        _cave_outline_cache[key] = _cave_outline(zone, deterministic_seed(zone["id"]))
    pts = _cave_outline_cache[key]
    cx, cy = zone["center"]
    n = len(pts)
    i = rnd.randrange(n)
    j = (i + 1) % n
    r1, r2 = rnd.random(), rnd.random()
    if r1 + r2 > 1:
        r1, r2 = 1.0 - r1, 1.0 - r2
    px = cx + r1 * (pts[i][0] - cx) + r2 * (pts[j][0] - cx)
    py = cy + r1 * (pts[i][1] - cy) + r2 * (pts[j][1] - cy)
    return px, py


def rect_bounds(zone):
    cx, cy = zone["center"]
    w, d = zone["size"]
    return (cx - w / 2.0, cx + w / 2.0, cy - d / 2.0, cy + d / 2.0)


def doorway_for_gate(zone, zone_lookup):
    """A barred gate belongs IN a doorway, not floating mid-room. Picks the
    zone's first neighbor (no semantic data says which connection has the
    gate) and finds where their footprints actually meet, the same
    overlap-based approach 03_built_architecture.py uses for door cuts."""
    for nid in zone.get("neighbors", []):
        neighbor = zone_lookup.get(nid)
        if not neighbor:
            continue
        ax0, ax1, ay0, ay1 = rect_bounds(zone)
        bx0, bx1, by0, by1 = rect_bounds(neighbor)
        x_overlap = min(ax1, bx1) - max(ax0, bx0)
        y_overlap = min(ay1, by1) - max(ay0, by0)
        if x_overlap < y_overlap:
            gate_x = (min(ax1, bx1) + max(ax0, bx0)) / 2.0
            gate_y = (max(ay0, by0) + min(ay1, by1)) / 2.0
            return (gate_x, gate_y), "y"
        else:
            gate_y = (min(ay1, by1) + max(ay0, by0)) / 2.0
            gate_x = (max(ax0, bx0) + min(ax1, bx1)) / 2.0
            return (gate_x, gate_y), "x"
    return None


# -- materials --------------------------------------------------------------


def materials():
    return dict(
        wood=get_or_create_material("Prop_wood", (0.35, 0.23, 0.14, 1.0), roughness=0.85),
        wood_dark=get_or_create_material("Prop_wood_dark", (0.22, 0.15, 0.1, 1.0), roughness=0.9),
        stone=get_or_create_material("Prop_stone", (0.56, 0.54, 0.51, 1.0), roughness=0.8),
        iron=get_or_create_material("Prop_iron", (0.12, 0.12, 0.13, 1.0), roughness=0.55),
        bone=get_or_create_material("Prop_bone", (0.78, 0.74, 0.62, 1.0), roughness=0.75),
        foliage=get_or_create_material("Prop_foliage", (0.14, 0.32, 0.16, 1.0), roughness=0.9),
        foliage_dead=get_or_create_material("Prop_foliage_dead", (0.3, 0.26, 0.16, 1.0), roughness=0.95),
        blue_flame=get_or_create_material(
            "Prop_blue_flame", (0.25, 0.55, 0.95, 1.0), roughness=0.3,
            emission=(0.35, 0.65, 1.0, 1.0), emission_strength=2.5,
        ),
        orange_flame=get_or_create_material(
            "Prop_orange_flame", (0.95, 0.5, 0.15, 1.0), roughness=0.3,
            emission=(1.0, 0.45, 0.1, 1.0), emission_strength=2.5,
        ),
        warm_flame=get_or_create_material(
            "Prop_warm_flame", (0.95, 0.75, 0.3, 1.0), roughness=0.3,
            emission=(1.0, 0.75, 0.3, 1.0), emission_strength=2.2,
        ),
        gold=get_or_create_material(
            "Prop_gold", (0.75, 0.6, 0.15, 1.0), roughness=0.4,
            emission=(0.6, 0.45, 0.1, 1.0), emission_strength=0.5,
        ),
        water=get_or_create_material(
            "Water_pool", (0.08, 0.32, 0.38, 1.0), roughness=0.15, alpha=0.82,
            emission=(0.05, 0.25, 0.28, 1.0), emission_strength=0.4,
        ),
    )


# -- prop builders ------------------------------------------------------


def pine_tree(pos, tier_z, collection, mats, seed, idx):
    rnd = random.Random(seed + idx)
    px, py = pos
    trunk_h = rnd.uniform(1.6, 2.4)
    add_cylinder(f"prop_pine_trunk_{idx:02d}", (px, py, tier_z + trunk_h / 2.0), 0.14, trunk_h, collection, mats["wood_dark"], verts=6)
    tiers = 3
    base_r = rnd.uniform(0.9, 1.2)
    for t in range(tiers):
        tz = tier_z + trunk_h * 0.5 + t * (trunk_h * 0.45)
        tr = base_r * (1.0 - t * 0.28)
        add_cone(f"prop_pine_foliage_{idx:02d}_{t}", (px, py, tz + 0.6), tr, 1.3, collection, mats["foliage"], verts=8)


def tree_stump(pos, tier_z, collection, mats, seed, idx):
    px, py = pos
    add_cylinder(f"prop_stump_{idx:02d}", (px, py, tier_z + 0.2), 0.3, 0.4, collection, mats["wood_dark"], verts=8)


def rock_scatter(pos, tier_z, collection, mats, seed, idx):
    rnd = random.Random(seed + idx)
    px, py = pos
    size = (rnd.uniform(0.3, 0.6), rnd.uniform(0.3, 0.6), rnd.uniform(0.2, 0.4))
    add_rock_chunk(f"prop_rock_{idx:02d}", (px, py, tier_z + size[2] / 2.0), size, collection, mats["stone"], seed + idx)


def crate_stack(pos, tier_z, collection, mats, seed, idx, broken=False):
    rnd = random.Random(seed + idx)
    px, py = pos
    base_size = 0.55
    if broken:
        # A knocked-over crate on its side reads as "broken" distinctly from
        # a neat stack, rather than reusing the identical upright model.
        add_box(
            f"prop_crate_{idx:02d}_0", (px, py, tier_z + base_size * 0.3),
            (base_size, base_size, base_size), collection, mats["wood"],
            rotation=(math.radians(rnd.uniform(70, 100)), 0, rnd.uniform(0, math.pi * 2)),
        )
        return
    n = rnd.randint(1, 2)
    for i in range(n):
        off_x = rnd.uniform(-0.15, 0.15) if i else 0.0
        off_y = rnd.uniform(-0.15, 0.15) if i else 0.0
        z = tier_z + base_size / 2.0 + i * base_size * 0.95
        add_box(
            f"prop_crate_{idx:02d}_{i}", (px + off_x, py + off_y, z), (base_size, base_size, base_size),
            collection, mats["wood"], rotation=(0, 0, rnd.uniform(-0.3, 0.3)),
        )


def broken_crate_stack(pos, tier_z, collection, mats, seed, idx):
    crate_stack(pos, tier_z, collection, mats, seed, idx, broken=True)


def add_fixture(pos, tier_z, collection, mats, seed, idx, kind, color):
    px, py = pos
    flame_mat = {"blue": mats["blue_flame"], "orange": mats["orange_flame"], "warm": mats["warm_flame"]}[color]
    if kind == "brazier":
        pedestal_h = 0.8
        add_cylinder(f"prop_brazier_pole_{idx:02d}", (px, py, tier_z + pedestal_h / 2.0), 0.08, pedestal_h, collection, mats["iron"], verts=6)
        add_cylinder(f"prop_brazier_bowl_{idx:02d}", (px, py, tier_z + pedestal_h + 0.06), 0.26, 0.14, collection, mats["iron"], verts=8)
        add_cone(f"prop_brazier_flame_{idx:02d}", (px, py, tier_z + pedestal_h + 0.3), 0.16, 0.4, collection, flame_mat, verts=6)
    elif kind == "torch":
        pole_h = 1.4
        add_cylinder(f"prop_torch_pole_{idx:02d}", (px, py, tier_z + pole_h / 2.0), 0.05, pole_h, collection, mats["iron"], verts=6)
        add_cone(f"prop_torch_flame_{idx:02d}", (px, py, tier_z + pole_h + 0.18), 0.12, 0.32, collection, flame_mat, verts=6)
    elif kind == "lantern":
        mount_h = 1.8
        add_cylinder(f"prop_lantern_body_{idx:02d}", (px, py, tier_z + mount_h), 0.14, 0.26, collection, mats["iron"], verts=6)
        add_cone(f"prop_lantern_flame_{idx:02d}", (px, py, tier_z + mount_h), 0.08, 0.2, collection, flame_mat, verts=6)


def barrel(pos, tier_z, collection, mats, seed, idx):
    px, py = pos
    add_cylinder(f"prop_barrel_{idx:02d}", (px, py, tier_z + 0.4), 0.3, 0.8, collection, mats["wood"], verts=10)


def barred_gate(pos, tier_z, collection, mats, seed, idx, orientation="x"):
    px, py = pos
    width, height = 1.6, 2.0
    n_bars = 6
    for b in range(n_bars):
        t = (b + 0.5) / n_bars - 0.5
        if orientation == "x":  # bars spread along x: gate spans an opening in an east/west-facing wall
            bx, by = px + t * width, py
        else:
            bx, by = px, py + t * width
        add_cylinder(f"prop_gate_bar_{idx:02d}_{b}", (bx, by, tier_z + height / 2.0), 0.03, height, collection, mats["iron"], verts=6)


def bench(pos, tier_z, collection, mats, seed, idx):
    rnd = random.Random(seed + idx)
    px, py = pos
    rot = rnd.uniform(0, math.pi)
    add_box(f"prop_bench_seat_{idx:02d}", (px, py, tier_z + 0.35), (1.1, 0.35, 0.08), collection, mats["wood"], rotation=(0, 0, rot))
    for s in (-1, 1):
        add_cylinder(
            f"prop_bench_leg_{idx:02d}_{s}",
            (px + s * math.cos(rot) * 0.45, py + s * math.sin(rot) * 0.45, tier_z + 0.16),
            0.04, 0.32, collection, mats["wood_dark"], verts=6,
        )


def sarcophagus(pos, tier_z, collection, mats, seed, idx):
    rnd = random.Random(seed + idx)
    px, py = pos
    rot = rnd.uniform(0, math.pi)
    add_box(f"prop_sarc_base_{idx:02d}", (px, py, tier_z + 0.3), (1.9, 0.75, 0.6), collection, mats["stone"], rotation=(0, 0, rot))
    add_box(f"prop_sarc_lid_{idx:02d}", (px, py, tier_z + 0.68), (2.0, 0.85, 0.16), collection, mats["stone"], rotation=(0, 0, rot))


def well_ring(pos, tier_z, collection, mats, seed, idx):
    px, py = pos
    add_cylinder(f"prop_well_wall_{idx:02d}", (px, py, tier_z + 0.5), 0.8, 1.0, collection, mats["stone"], verts=14)
    add_cylinder(f"prop_well_void_{idx:02d}", (px, py, tier_z + 0.95), 0.6, 0.1, collection, mats["iron"], verts=14)


def treasure_pile(pos, tier_z, collection, mats, seed, idx):
    rnd = random.Random(seed + idx)
    px, py = pos
    for i in range(5):
        ox, oy = rnd.uniform(-0.35, 0.35), rnd.uniform(-0.35, 0.35)
        size = (rnd.uniform(0.15, 0.3), rnd.uniform(0.15, 0.3), rnd.uniform(0.1, 0.2))
        add_rock_chunk(f"prop_hoard_{idx:02d}_{i}", (px + ox, py + oy, tier_z + size[2] / 2.0), size, collection, mats["gold"], seed + idx * 10 + i)


def hanging_root(pos, tier_z, collection, mats, seed, idx):
    """Anchored near the top of a 2.6m wall (not an arbitrary floating
    height) and placed via corner_point so it reads as coming down from the
    wall/ceiling junction rather than hanging in open air."""
    rnd = random.Random(seed + idx)
    px, py = pos
    anchor_z = tier_z + 2.4
    for i in range(2):
        length = rnd.uniform(0.4, 0.9)
        add_cylinder(
            f"prop_root_{idx:02d}_{i}", (px + rnd.uniform(-0.2, 0.2), py + rnd.uniform(-0.2, 0.2), anchor_z - length / 2.0),
            0.03, length, collection, mats["wood_dark"], verts=5, rotation=(rnd.uniform(-0.15, 0.15), rnd.uniform(-0.15, 0.15), 0),
        )


def clutter_pile(pos, tier_z, collection, mats, seed, idx, tone="stone"):
    rnd = random.Random(seed + idx)
    px, py = pos
    mat = mats["bone"] if tone == "bone" else (mats["wood"] if tone == "wood" else mats["stone"])
    for i in range(3):
        ox, oy = rnd.uniform(-0.3, 0.3), rnd.uniform(-0.3, 0.3)
        size = (rnd.uniform(0.2, 0.45), rnd.uniform(0.2, 0.45), rnd.uniform(0.12, 0.25))
        add_rock_chunk(f"prop_clutter_{idx:02d}_{i}", (px + ox, py + oy, tier_z + size[2] / 2.0), size, collection, mat, seed + idx * 10 + i)


def nest_pile(pos, tier_z, collection, mats, seed, idx):
    px, py = pos
    add_cylinder(f"prop_nest_{idx:02d}", (px, py, tier_z + 0.1), 0.7, 0.2, collection, mats["wood_dark"], verts=10)


def spiky_brush(pos, tier_z, collection, mats, seed, idx):
    rnd = random.Random(seed + idx)
    px, py = pos
    for i in range(3):
        ox, oy = rnd.uniform(-0.3, 0.3), rnd.uniform(-0.3, 0.3)
        h = rnd.uniform(0.3, 0.6)
        add_cone(f"prop_brush_{idx:02d}_{i}", (px + ox, py + oy, tier_z + h / 2.0), 0.1, h, collection, mats["foliage_dead"], verts=5)


def small_pool(pos, tier_z, collection, mats, seed, idx):
    px, py = pos
    add_cylinder(f"prop_pool_{idx:02d}", (px, py, tier_z + 0.03), 0.9, 0.06, collection, mats["water"], verts=14)


def stone_font(pos, tier_z, collection, mats, seed, idx):
    px, py = pos
    add_cylinder(f"prop_font_base_{idx:02d}", (px, py, tier_z + 0.45), 0.22, 0.9, collection, mats["stone"], verts=10)
    add_cylinder(f"prop_font_basin_{idx:02d}", (px, py, tier_z + 0.95), 0.4, 0.2, collection, mats["stone"], verts=12)


# -- keyword-driven scatter ----------------------------------------------

PROP_KEYWORDS = [
    ("pine trees", pine_tree, 5, west_biased_point),
    ("tree stump", tree_stump, 1, interior_point),
    ("rocks", rock_scatter, 2, interior_point),
    ("stacked crates", crate_stack, 2, corner_point),
    ("broken crates", broken_crate_stack, 1, corner_point),
    ("barrel", barrel, 1, corner_point),
    ("bench", bench, 1, corner_point),
    ("burial niches", sarcophagus, 2, interior_point),
    ("well/sinkhole", well_ring, 1, interior_point),
    ("golden hoard", treasure_pile, 1, interior_point),
    ("hanging roots", hanging_root, 1, corner_point),
    ("nest/bedding pit", nest_pile, 1, interior_point),
    ("spiky dead brush", spiky_brush, 1, interior_point),
    ("small glowing pool", small_pool, 1, interior_point),
    ("stone pedestal/font", stone_font, 1, interior_point),
]
# "barred gate" is handled separately in scatter_zone(), via doorway_for_gate(),
# since it needs neighbor data a plain (zone, rnd) placer doesn't have access to.

CLUTTER_KEYWORDS = {
    "bone/nest debris": "bone",
    "weapon/bone clutter": "bone",
    "rubble/bone pile": "bone",
    "wooden planks/debris": "wood",
    "wooden barricades": "wood",
    "shelves/racks": "wood",
    "scattered debris": "stone",
    "rubble": "stone",
}

FIXTURE_KEYWORDS = [
    ("glowing blue braziers", "brazier", "blue", 2),
    ("blue brazier", "brazier", "blue", 1),
    ("orange braziers", "brazier", "orange", 1),
    ("orange fire pit", "brazier", "orange", 1),
    ("standing iron torches", "torch", "warm", 3),
    ("hanging lantern", "lantern", "warm", 1),
]


def scatter_zone(zone, zone_lookup, tier_height, collection, mats):
    if zone["id"] == "stag_shrine":
        return  # bespoke hero prop, handled separately
    tier_z = tier_height[zone["elevation_tier"]]
    seed = deterministic_seed(zone["id"])
    rnd = random.Random(seed)
    features_text = " / ".join(zone.get("features", [])).lower()
    idx = 0
    # Natural caves have an irregular silhouette much smaller than their
    # rectangular footprint - every prop in one uses cave-constrained
    # placement instead of whatever the table's default placer would do.
    is_cave = zone["kind"] == "natural_cave"

    for keyword, builder, count, placer in PROP_KEYWORDS:
        if keyword in features_text:
            for _ in range(count):
                pos = random_point_in_cave(zone, rnd) if is_cave else placer(zone, rnd)
                builder(pos, tier_z, collection, mats, seed, idx)
                idx += 1

    if "barred gate" in features_text:
        result = doorway_for_gate(zone, zone_lookup)
        if result:
            pos, orientation = result
            barred_gate(pos, tier_z, collection, mats, seed, idx, orientation=orientation)
            idx += 1

    for keyword, tone in CLUTTER_KEYWORDS.items():
        if keyword in features_text:
            pos = random_point_in_cave(zone, rnd) if is_cave else interior_point(zone, rnd)
            clutter_pile(pos, tier_z, collection, mats, seed, idx, tone=tone)
            idx += 1

    for keyword, kind, color, count in FIXTURE_KEYWORDS:
        if keyword in features_text:
            for k in range(count):
                if is_cave:
                    pos = random_point_in_cave(zone, rnd)
                else:
                    pos = corner_point(zone, rnd, margin=0.7, index=k)
                add_fixture(pos, tier_z, collection, mats, seed, idx, kind, color)
                idx += 1


def build_stag_shrine(zone, tier_height, collection, mats):
    cx, cy = zone["center"]
    floor_z = tier_height[zone["elevation_tier"]]
    dais_h = 1.0
    dais_top = floor_z + dais_h
    dais_radius = 2.2
    stone = mats["stone"]

    add_cylinder("stag_dais", (cx, cy, floor_z + dais_h / 2.0), dais_radius, dais_h, collection, stone, verts=16)

    # Entrance faces south (both neighbors - crypt_hall, south_ward - sit south
    # of this zone), so the approach steps go on the south side of the dais.
    step_count = 3
    for i in range(step_count):
        step_h = dais_h * (i + 1) / step_count
        add_box(
            f"stag_dais_step_{i}", (cx, cy - dais_radius - 0.5 + i * 0.5, floor_z + step_h / 2.0),
            (1.6, 0.5, step_h), collection, stone,
        )

    # Statue v2: a review against the user's reference art found the original
    # box-and-cylinder version read as "grazing goat" rather than a heraldic
    # stag - built via Opus art direction (checked against the reference
    # image) and iterated live via the Blender MCP bridge before porting back
    # here. Body/head are ring-lofted cross-sections (Subdivision Surface
    # turns a handful of blocky ellipses into an organic barrel torso and
    # wedge head - far more predictable to get right without a live viewport
    # than freeform bmesh vertex-pushing); neck/legs/antlers are tapered
    # Bezier curves (the same curve-native-bevel technique Task 7's vines
    # use), which read as tapered organic limbs for free.
    statue_mat = get_or_create_material("Stag_statue_stone", (0.82, 0.8, 0.76, 1.0), roughness=0.5)
    sx, sy = cx, cy + 0.9
    leg_len = 0.78
    spine_z = dais_top + leg_len

    ring_loft(
        "stag_statue_body",
        [
            (sx + 0.58, sy, 0.14, 0.18, spine_z - 0.02),   # rear cap (tail base)
            (sx + 0.38, sy, 0.26, 0.30, spine_z),          # haunch
            (sx + 0.02, sy, 0.19, 0.25, spine_z + 0.03),   # waist
            (sx - 0.32, sy, 0.24, 0.32, spine_z + 0.08),   # chest / withers (deepest, highest)
            (sx - 0.52, sy, 0.15, 0.22, spine_z + 0.06),   # chest front cap, neck attaches here
        ],
        collection, statue_mat,
    )

    neck_base = Vector((sx - 0.52, sy, spine_z + 0.10))
    head_base = Vector((sx - 1.12, sy, spine_z + 0.95))  # up and forward of the chest - alert, heraldic pose
    head_mid = head_base + Vector((-0.17, 0, -0.03))
    head_muzzle = head_base + Vector((-0.33, 0, -0.08))
    ring_loft(
        "stag_statue_head",
        [
            (head_base.x, sy, 0.14, 0.15, head_base.z),
            (head_mid.x, sy, 0.10, 0.11, head_mid.z),
            (head_muzzle.x, sy, 0.045, 0.055, head_muzzle.z),
        ],
        collection, statue_mat, verts_per_ring=8,
    )

    tapered_curve(
        "stag_statue_neck",
        [(neck_base, 0.16), ((neck_base + head_base) / 2 + Vector((0, 0, 0.02)), 0.12), (head_base, 0.13)],
        collection, statue_mat,
    )

    hoof_z = dais_top
    for side in (-1, 1):
        hip = Vector((sx - 0.3, sy + side * 0.17, spine_z - 0.1))
        knee = Vector((sx - 0.32, sy + side * 0.17, dais_top + leg_len * 0.45))
        hoof = Vector((sx - 0.3, sy + side * 0.17, hoof_z))
        tapered_curve(f"stag_statue_leg_front_{side}", [(hip, 0.09), (knee, 0.055), (hoof, 0.045)], collection, statue_mat)

        hip_h = Vector((sx + 0.42, sy + side * 0.19, spine_z - 0.05))
        hock = Vector((sx + 0.48, sy + side * 0.19, dais_top + leg_len * 0.42))
        hoof_h = Vector((sx + 0.34, sy + side * 0.19, hoof_z))
        tapered_curve(f"stag_statue_leg_hind_{side}", [(hip_h, 0.11), (hock, 0.065), (hoof_h, 0.045)], collection, statue_mat)

    # Antlers: a tall sweeping beam (up-back-then-forward) with a brow tine,
    # two mid tines and a two-point crown fork at the tip, ~30deg spread per
    # side so the rack reads as a full crown rather than a single horn.
    for side in (-1, 1):
        base = head_base + Vector((0.02, side * 0.08, 0.08))
        mid = base + Vector((-0.05, side * 0.18, 0.42))
        tip = mid + Vector((0.12, side * 0.10, 0.34))
        tapered_curve(f"stag_statue_antler_{side}_beam", [(base, 0.045), (mid, 0.03), (tip, 0.016)], collection, statue_mat, bevel_resolution=3)

        def tine_from(along_t, out_dir, length, base_r, k):
            start = base.lerp(tip, along_t) if along_t < 1.0 else tip
            end = start + out_dir.normalized() * length
            tapered_curve(f"stag_statue_antler_{side}_tine_{k}", [(start, base_r), (end, base_r * 0.25)], collection, statue_mat, bevel_resolution=3)

        tine_from(0.18, Vector((-0.22, side * 0.02, -0.05)), 0.18, 0.022, 0)   # brow tine
        tine_from(0.5, Vector((-0.14, side * 0.05, 0.22)), 0.22, 0.02, 1)     # mid tine 1
        tine_from(0.72, Vector((-0.1, side * 0.08, 0.2)), 0.2, 0.018, 2)      # mid tine 2
        tine_from(1.0, Vector((-0.08, side * 0.14, 0.18)), 0.2, 0.015, 3)     # crown fork
        tine_from(1.0, Vector((0.1, side * 0.1, 0.22)), 0.18, 0.015, 4)       # crown fork

    # Altar sits between the statue and the entrance steps.
    altar_mat = mats["stone"]
    add_box("stag_altar", (cx, cy - 0.5, dais_top + 0.4), (1.2, 0.6, 0.8), collection, altar_mat)

    for side in (-1, 1):
        add_fixture(
            (cx + side * 1.7, cy - 0.8), dais_top, collection, mats,
            deterministic_seed("stag_shrine_brazier"), 0 if side < 0 else 1, "brazier", "blue",
        )


def set_render_engine():
    scene = bpy.context.scene
    for candidate in ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE", "BLENDER_EEVEE_LEGACY"):
        try:
            scene.render.engine = candidate
            return candidate
        except TypeError:
            continue
    return scene.render.engine


def render_views(bounds):
    scene = bpy.context.scene
    scene.render.resolution_x = 1600
    scene.render.resolution_y = 1280
    scene.render.image_settings.file_format = "PNG"

    top_cam = bpy.data.objects.get("TopDownCam")
    if top_cam:
        scene.camera = top_cam
        scene.render.filepath = os.path.join(RENDER_DIR, "05_set_dressing_top.png")
        bpy.ops.render.render(write_still=True)

    persp_cam = bpy.data.objects.get("CaveCheckPerspCam")
    if not persp_cam:
        cx = (bounds["min"][0] + bounds["max"][0]) / 2.0
        cy = (bounds["min"][1] + bounds["max"][1]) / 2.0
        cam_data = bpy.data.cameras.new("CaveCheckPerspCam")
        cam_data.type = "PERSP"
        cam_data.lens = 24
        persp_cam = bpy.data.objects.new("CaveCheckPerspCam", cam_data)
        persp_cam.location = (cx - 28, cy - 42, 26)
        persp_cam.rotation_euler = (1.15, 0, -0.55)
        get_or_create_collection("Cameras").objects.link(persp_cam)
    scene.camera = persp_cam
    scene.render.filepath = os.path.join(RENDER_DIR, "05_set_dressing_persp.png")
    bpy.ops.render.render(write_still=True)

    stag_cam = bpy.data.objects.get("StagShrineCam")
    if not stag_cam:
        cam_data = bpy.data.cameras.new("StagShrineCam")
        cam_data.type = "PERSP"
        cam_data.lens = 35
        stag_cam = bpy.data.objects.new("StagShrineCam", cam_data)
        get_or_create_collection("Cameras").objects.link(stag_cam)
    # Point-at-target via quaternion track-to rather than hand-picked Euler
    # angles, which put the previous attempt looking at the wrong thing entirely.
    cam_pos = Vector((6.5, -20.4, 7.0))
    target = Vector((12.5, -11.0, -0.5))
    stag_cam.location = cam_pos
    stag_cam.rotation_euler = (target - cam_pos).to_track_quat("-Z", "Y").to_euler()
    scene.camera = stag_cam
    scene.render.filepath = os.path.join(RENDER_DIR, "05_stag_shrine_closeup.png")
    bpy.ops.render.render(write_still=True)


def main():
    with open(LAYOUT_PATH, "r", encoding="utf-8") as f:
        layout = json.load(f)
    zone_lookup = {z["id"]: z for z in layout["zones"]}
    tier_height = layout["elevation_tiers"]

    bpy.ops.wm.open_mainfile(filepath=BLEND_PATH)

    root = get_or_create_collection("SunkenShrine_Blockout")
    prop_coll = get_or_create_collection("SetDressing", root)

    remove_objects_with_prefixes(OWN_PREFIXES)

    mats = materials()

    for zone in layout["zones"]:
        scatter_zone(zone, zone_lookup, tier_height, prop_coll, mats)

    build_stag_shrine(zone_lookup["stag_shrine"], tier_height, prop_coll, mats)

    engine = set_render_engine()
    render_views(layout["bounds"])
    bpy.ops.wm.save_as_mainfile(filepath=BLEND_PATH)
    print(f"Set dressing complete. Engine={engine}. Saved {BLEND_PATH}")


if __name__ == "__main__":
    main()

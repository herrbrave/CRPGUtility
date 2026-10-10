"""
Task 7 - Detail geometry pass for Sunken Shrine of the Stag Lord.

Opens the blend produced by Tasks 1-6 and raises geometric fidelity without
touching layout or sourcing any external assets:
  - Bevels sharp edges on architecture and props (bmesh, applied in place) so
    stone blocks, pillars, steps and crates stop reading as raw CG primitives
    and catch a highlight along their edges - the single cheapest, highest-
    impact move available before touching materials at all.
  - Scatters actual vine geometry (Bezier curves with curve-native bevel_depth,
    not mesh scatter) climbing walls and the statue, and flattened moss-clump
    blobs at floor/wall junctions, matching the reference image's overgrown-
    ruin look. Counts are modest and placement reuses the same deterministic
    per-zone seeding and cave-aware/corner placement machinery Task 5 built,
    so a rerun is reproducible and nothing lands outside a cave's silhouette.

Explicitly NOT attempted here (flagged rather than silently skipped): true
per-brick geometry (thousands of individually modeled blocks) - that's done
via a procedural Brick Texture shader in Task 8 instead, since modeling every
block as real geometry would be an enormous mesh-count/render-time cost for a
visual effect a shader already achieves; and no weapon-rack/furniture props,
since the user's ask was specifically bevels/vines/moss/statue detail.

Run headless (after 01-06 have produced blend/sunken_shrine.blend):
    blender --background --factory-startup --python 07_detail_geometry.py

Produces:
    ../blend/sunken_shrine.blend              (opened, modified, saved back in place)
    ../renders/07_detail_top.png
    ../renders/07_detail_persp.png
    ../renders/07_detail_stag_closeup.png
"""
import bpy
import bmesh
import json
import math
import os
import random
import zlib

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
PROJECT_DIR = os.path.dirname(SCRIPT_DIR)
LAYOUT_PATH = os.path.join(SCRIPT_DIR, "layout.json")
BLEND_PATH = os.path.join(PROJECT_DIR, "blend", "sunken_shrine.blend")
RENDER_DIR = os.path.join(PROJECT_DIR, "renders")
os.makedirs(RENDER_DIR, exist_ok=True)

BEVEL_COLLECTIONS = ["Blockout_built_room", "BuiltArchitecture", "WaterVerticality", "SetDressing"]
BEVEL_EXCLUDE_SUBSTRINGS = ["pool", "stream", "waterfall", "splash", "water_void", "font_basin"]

OWN_PREFIXES = ["detail_vine_", "detail_moss_"]


def deterministic_seed(text):
    return zlib.crc32(text.encode("utf-8"))


def get_or_create_material(name, color, roughness=0.85, emission=None, emission_strength=0.3):
    if name in bpy.data.materials:
        return bpy.data.materials[name]
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Base Color"].default_value = color
        if "Roughness" in bsdf.inputs:
            bsdf.inputs["Roughness"].default_value = roughness
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
        data = obj.data
        bpy.data.objects.remove(obj, do_unlink=True)
        if data and data.users == 0:
            if isinstance(data, bpy.types.Mesh):
                bpy.data.meshes.remove(data)
            elif isinstance(data, bpy.types.Curve):
                bpy.data.curves.remove(data)


def link_only(obj, collection):
    for c in list(obj.users_collection):
        c.objects.unlink(obj)
    collection.objects.link(obj)


# -- bevel pass -------------------------------------------------------------


def bevel_mesh_object(obj, width=0.025, segments=2, angle_limit=math.radians(28)):
    if obj.type != "MESH" or not obj.data.polygons:
        return False
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bm.edges.ensure_lookup_table()
    targets = []
    for e in bm.edges:
        if len(e.link_faces) == 1:
            targets.append(e)  # open/boundary edge - always worth a soft edge
        elif len(e.link_faces) == 2:
            try:
                if e.calc_face_angle() > angle_limit:
                    targets.append(e)
            except ValueError:
                pass
    if not targets:
        bm.free()
        return False
    bmesh.ops.bevel(bm, geom=targets, offset=width, segments=segments, affect="EDGES", clamp_overlap=True)
    bm.to_mesh(obj.data)
    obj.data.update()
    bm.free()
    return True


def run_bevel_pass():
    count = 0
    for coll_name in BEVEL_COLLECTIONS:
        coll = bpy.data.collections.get(coll_name)
        if not coll:
            continue
        for obj in list(coll.objects):
            if any(s in obj.name.lower() for s in BEVEL_EXCLUDE_SUBSTRINGS):
                continue
            if bevel_mesh_object(obj):
                count += 1
    return count


# -- vines & moss -------------------------------------------------------


def add_vine(name, base_pos, height, collection, material, seed, segments=6, sway=0.18, radius=0.025):
    rnd = random.Random(seed)
    curve_data = bpy.data.curves.new(name, type="CURVE")
    curve_data.dimensions = "3D"
    curve_data.bevel_depth = radius
    curve_data.bevel_resolution = 2
    curve_data.resolution_u = 4
    spline = curve_data.splines.new("BEZIER")
    spline.bezier_points.add(segments - 1)
    drift_x, drift_y = rnd.uniform(-1, 1), rnd.uniform(-1, 1)
    for i in range(segments):
        t = i / (segments - 1)
        x = base_pos[0] + drift_x * sway * t
        y = base_pos[1] + drift_y * sway * t
        z = base_pos[2] + height * t
        pt = spline.bezier_points[i]
        pt.co = (x, y, z)
        pt.handle_left_type = "AUTO"
        pt.handle_right_type = "AUTO"
    obj = bpy.data.objects.new(name, curve_data)
    obj.data.materials.append(material)
    collection.objects.link(obj)
    return obj


def add_moss_clump(name, pos, collection, material, seed):
    rnd = random.Random(seed)
    bpy.ops.mesh.primitive_ico_sphere_add(radius=rnd.uniform(0.18, 0.32), subdivisions=1, location=pos)
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = (1.0, 1.0, rnd.uniform(0.3, 0.45))
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    obj.data.materials.append(material)
    for c in list(obj.users_collection):
        c.objects.unlink(obj)
    collection.objects.link(obj)
    return obj


def rect_bounds(zone):
    cx, cy = zone["center"]
    w, d = zone["size"]
    return (cx - w / 2.0, cx + w / 2.0, cy - d / 2.0, cy + d / 2.0)


# Reuses the exact cave-outline math from Task 5 (which itself replicates
# Task 2's cave_shell.py) so vine/moss placement in natural caves lands
# inside the actual irregular silhouette, not the rectangular layout.json
# footprint - the same bug class a review caught in Task 5's prop placement.
CAVE_OUTLINE_POINTS = 16
_cave_outline_cache = {}


def _cave_outline(zone, seed):
    rnd = random.Random(seed)
    cx, cy = zone["center"]
    rx, ry = zone["size"][0] / 2.0, zone["size"][1] / 2.0
    lobe_count = rnd.randint(2, 4)
    lobes = [(rnd.uniform(0, 2 * math.pi), rnd.uniform(0.45, 0.9), rnd.uniform(1.2, 2.0)) for _ in range(lobe_count)]
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
    return (cx + r1 * (pts[i][0] - cx) + r2 * (pts[j][0] - cx), cy + r1 * (pts[i][1] - cy) + r2 * (pts[j][1] - cy))


def corner_point(zone, rnd, margin=0.5, index=None):
    cx, cy = zone["center"]
    w, d = zone["size"]
    if index is None:
        sx, sy = rnd.choice([-1, 1]), rnd.choice([-1, 1])
    else:
        sx, sy = [(-1, -1), (1, -1), (-1, 1), (1, 1)][index % 4]
    return cx + sx * max(w / 2.0 - margin, 0.3), cy + sy * max(d / 2.0 - margin, 0.3)


def scatter_overgrowth(zones, tier_height, collection, vine_mat, moss_mat):
    vine_count = 0
    moss_count = 0
    eligible = [z for z in zones if z["kind"] in ("built_room", "natural_cave") and z["id"] != "boss_pit"]
    for zone in eligible:
        tier_z = tier_height[zone["elevation_tier"]]
        seed = deterministic_seed(zone["id"] + "_overgrowth")
        rnd = random.Random(seed)
        is_cave = zone["kind"] == "natural_cave"
        n_vines = rnd.randint(1, 2)
        for v in range(n_vines):
            pos2d = random_point_in_cave(zone, rnd) if is_cave else corner_point(zone, rnd, index=v)
            height = rnd.uniform(1.2, 2.2)
            add_vine(
                f"detail_vine_{zone['id']}_{v}", (pos2d[0], pos2d[1], tier_z), height,
                collection, vine_mat, seed + v, segments=rnd.randint(5, 7),
            )
            vine_count += 1
        n_moss = rnd.randint(1, 2)
        for m in range(n_moss):
            pos2d = random_point_in_cave(zone, rnd) if is_cave else corner_point(zone, rnd, margin=0.3, index=m + 2)
            add_moss_clump(f"detail_moss_{zone['id']}_{m}", (pos2d[0], pos2d[1], tier_z + 0.02), collection, moss_mat, seed + m + 100)
            moss_count += 1
    return vine_count, moss_count


def add_statue_vine(zone, tier_height, collection, vine_mat):
    cx, cy = zone["center"]
    floor_z = tier_height[zone["elevation_tier"]]
    dais_top = floor_z + 1.0
    # Wraps loosely up the front-right statue leg (sx+0.45, sy+0.18 in Task 5's
    # build_stag_shrine - reconstructed here since that function doesn't
    # return its coordinates).
    base = (cx + 0.45, cy + 0.9 + 0.18, dais_top)
    add_vine("detail_vine_stag_shrine_statue", base, 1.1, collection, vine_mat, deterministic_seed("stag_statue_vine"), segments=6, sway=0.1, radius=0.018)


def set_render_engine_eevee():
    scene = bpy.context.scene
    for candidate in ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE", "BLENDER_EEVEE_LEGACY"):
        try:
            scene.render.engine = candidate
            return candidate
        except TypeError:
            continue
    return scene.render.engine


def render_views():
    scene = bpy.context.scene
    scene.render.resolution_x = 1600
    scene.render.resolution_y = 1280
    scene.render.image_settings.file_format = "PNG"
    views = [
        ("TopDownCam", "07_detail_top.png"),
        ("CaveCheckPerspCam", "07_detail_persp.png"),
        ("StagShrineCam", "07_detail_stag_closeup.png"),
    ]
    for cam_name, filename in views:
        cam = bpy.data.objects.get(cam_name)
        if not cam:
            continue
        scene.camera = cam
        scene.render.filepath = os.path.join(RENDER_DIR, filename)
        bpy.ops.render.render(write_still=True)


def main():
    with open(LAYOUT_PATH, "r", encoding="utf-8") as f:
        layout = json.load(f)
    tier_height = layout["elevation_tiers"]
    zone_lookup = {z["id"]: z for z in layout["zones"]}

    bpy.ops.wm.open_mainfile(filepath=BLEND_PATH)

    root = get_or_create_collection("SunkenShrine_Blockout")
    detail_coll = get_or_create_collection("DetailGeometry", root)
    remove_objects_with_prefixes(OWN_PREFIXES)

    bevel_count = run_bevel_pass()

    vine_mat = get_or_create_material("Detail_vine", (0.12, 0.3, 0.14, 1.0), roughness=0.85)
    moss_mat = get_or_create_material("Detail_moss", (0.17, 0.34, 0.16, 1.0), roughness=0.95)

    vine_count, moss_count = scatter_overgrowth(layout["zones"], tier_height, detail_coll, vine_mat, moss_mat)
    add_statue_vine(zone_lookup["stag_shrine"], tier_height, detail_coll, vine_mat)
    vine_count += 1

    engine = set_render_engine_eevee()
    render_views()
    bpy.ops.wm.save_as_mainfile(filepath=BLEND_PATH)
    print(f"Detail geometry complete. Engine={engine}. {bevel_count} objects beveled, {vine_count} vines, {moss_count} moss clumps.")
    print(f"Saved {BLEND_PATH}")


if __name__ == "__main__":
    main()

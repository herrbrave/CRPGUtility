"""
Task 2 - Natural cave shell pass for Sunken Shrine of the Stag Lord.

Opens the blockout produced by 01_blockout.py, strips the flat greybox
floor/walls for zones that are genuinely natural caves (kind=="natural_cave",
plus pool_cavern's rocky rim since the layout explicitly calls it a cavern),
and replaces them with an open-top cave shell per zone: a jittered, two-octave
irregular-polygon floor and a wall ring with no ceiling cap - this is an
isometric-cutaway level, so a closed roof would block the camera entirely.
Walls are biased taller to the north (the painted convention's "back" of each
space) and lower to the south (camera-facing), and each zone leaves an open
gap in its wall ring toward every neighbor zone so caves actually connect
instead of forming sealed rings. Stalactites hang from an implied ceiling
height (tracked per-point but never meshed) and stalagmites rise from the
floor, scattered by sampling the floor's own fan-triangulation so every point
is guaranteed inside the footprint even for concave outlines.

Normals are set explicitly (floor up, walls inward) rather than relying on
recalc-for-closed-manifold, since there is no longer a closed shell once the
ceiling and neighbor-facing gaps are gone.

Deterministic: each zone's jitter/scatter uses a seed derived from its id via
crc32 (not Python's hash(), which is randomized per-process), so re-running
this script produces identical geometry.

Run headless (after 01_blockout.py has produced blend/sunken_shrine.blend):
    blender --background --factory-startup --python 02_cave_shell.py

Produces:
    ../blend/sunken_shrine.blend        (opened, modified, saved back in place)
    ../renders/02_cave_shell_top.png
    ../renders/02_cave_shell_persp.png
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

CAVE_SHELL_ZONES = {"goblin_cave_camp", "ritual_cavern", "boss_pit"}
RIM_ONLY_ZONES = {"pool_cavern"}  # existing water floor kept; only wall ring replaced

OUTLINE_POINTS = 16

# Defaults, overridden per-zone below. tall_bias adds extra height toward +Y
# (north = painted "back" of each space); south stays low for the camera.
DEFAULTS = dict(
    fine_jitter=0.14,
    lobe_count_range=(2, 4),
    lobe_amp_range=(1.2, 2.0),
    lobe_width_range=(0.45, 0.9),
    base_wall_height=2.6,
    wall_height_jitter=0.5,
    tall_bias=1.4,
    min_wall_height=0.9,
    opening_half_width=0.42,
    ceiling_headroom=0.9,
    max_top_abs=None,
    formation_scale=1.0,
)

ZONE_OVERRIDES = {
    "pool_cavern": dict(
        fine_jitter=0.08,
        lobe_count_range=(0, 2),
        lobe_amp_range=(1.0, 1.1),
        base_wall_height=1.6,
        wall_height_jitter=0.35,
        tall_bias=0.6,
        min_wall_height=0.6,
        opening_half_width=0.55,
        formation_scale=0.5,
    ),
    "boss_pit": dict(
        base_wall_height=1.4,
        wall_height_jitter=0.2,
        tall_bias=0.3,
        min_wall_height=0.6,
        opening_half_width=0.6,
        max_top_abs=-0.4,  # must stay clear of the rubble_bridge deck at ground (0)
        formation_scale=0.12,
    ),
}


def params_for(zone_id):
    p = dict(DEFAULTS)
    p.update(ZONE_OVERRIDES.get(zone_id, {}))
    return p


def deterministic_seed(text):
    return zlib.crc32(text.encode("utf-8"))


def get_or_create_material(name, color, roughness=0.95):
    if name in bpy.data.materials:
        return bpy.data.materials[name]
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Base Color"].default_value = color
        if "Roughness" in bsdf.inputs:
            bsdf.inputs["Roughness"].default_value = roughness
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


def angle_diff(a, b):
    return abs(((a - b + math.pi) % (2 * math.pi)) - math.pi)


def cave_outline(center, size, seed, params, radius_override=None):
    rnd = random.Random(seed)
    cx, cy = center
    if radius_override:
        rx, ry = radius_override
    else:
        rx, ry = size[0] / 2.0, size[1] / 2.0

    lobe_count = rnd.randint(*params["lobe_count_range"])
    lobes = [
        (
            rnd.uniform(0, 2 * math.pi),
            rnd.uniform(*params["lobe_width_range"]),
            rnd.uniform(*params["lobe_amp_range"]),
        )
        for _ in range(lobe_count)
    ]

    pts = []
    angles = []
    for i in range(OUTLINE_POINTS):
        base_angle = 2 * math.pi * i / OUTLINE_POINTS
        angle = base_angle + rnd.uniform(-0.1, 0.1)
        r_scale = 1.0
        for la, lw, amp in lobes:
            d = angle_diff(angle, la)
            if d < lw:
                r_scale *= 1.0 + (amp - 1.0) * (1.0 - d / lw)
        r_scale *= 1.0 + rnd.uniform(-params["fine_jitter"], params["fine_jitter"])
        pts.append((cx + math.cos(angle) * rx * r_scale, cy + math.sin(angle) * ry * r_scale))
        angles.append(angle)
    return pts, angles


def neighbor_opening_angles(zone, zone_lookup):
    cx, cy = zone["center"]
    openings = []
    for nid in zone.get("neighbors", []):
        neighbor = zone_lookup.get(nid)
        if not neighbor:
            continue
        nx, ny = neighbor["center"]
        openings.append(math.atan2(ny - cy, nx - cx))
    return openings


def wall_top_height(floor_z, angle, params, rnd):
    # +Y (north) is the painted "back" of each space; bias taller there, lower
    # (but never below min_wall_height) toward -Y (south, camera-facing).
    north_component = max(0.0, math.sin(angle))
    h = params["base_wall_height"] + params["tall_bias"] * north_component
    h += rnd.uniform(-params["wall_height_jitter"], params["wall_height_jitter"])
    h = max(h, params["min_wall_height"])
    top_abs = floor_z + h
    if params["max_top_abs"] is not None:
        top_abs = min(top_abs, params["max_top_abs"])
    return top_abs


def build_cave_shell(zone, floor_z, collection, floor_mat, wall_mat, seed, params,
                      build_floor=True, radius_override=None):
    pts, angles = cave_outline(zone["center"], zone["size"], seed, params, radius_override)
    n = len(pts)
    rnd = random.Random(seed + 1000)
    tops = [wall_top_height(floor_z, angles[i], params, rnd) for i in range(n)]
    cx, cy = zone["center"]
    ceiling_ref = [t + params["ceiling_headroom"] for t in tops]
    center_ceiling_ref = sum(ceiling_ref) / n

    mesh = bpy.data.meshes.new(f"{zone['id']}_cave_shell_mesh")
    bm = bmesh.new()

    bottom_v = [bm.verts.new((x, y, floor_z)) for x, y in pts]
    top_v = [bm.verts.new((pts[i][0], pts[i][1], tops[i])) for i in range(n)]

    floor_faces = []
    if build_floor:
        center_bottom = bm.verts.new((cx, cy, floor_z))
        for i in range(n):
            j = (i + 1) % n
            floor_faces.append(bm.faces.new((center_bottom, bottom_v[i], bottom_v[j])))

    wall_faces = []
    wall_face_mid_angle = []
    for i in range(n):
        j = (i + 1) % n
        seg_angle = math.atan2(
            (pts[i][1] + pts[j][1]) / 2.0 - cy, (pts[i][0] + pts[j][0]) / 2.0 - cx
        )
        is_open = any(angle_diff(seg_angle, o) < params["opening_half_width"] for o in _CURRENT_OPENINGS)
        if is_open:
            continue
        f = bm.faces.new((bottom_v[i], bottom_v[j], top_v[j], top_v[i]))
        wall_faces.append(f)
        wall_face_mid_angle.append(seg_angle)

    bm.normal_update()
    for f in floor_faces:
        if f.normal.z < 0:
            f.normal_flip()
    for f, seg_angle in zip(wall_faces, wall_face_mid_angle):
        desired = Vector((-math.cos(seg_angle), -math.sin(seg_angle), 0.0))
        if f.normal.dot(desired) < 0:
            f.normal_flip()
    bm.normal_update()

    for f in floor_faces:
        f.material_index = 0
    for f in wall_faces:
        f.material_index = 1

    bm.to_mesh(mesh)
    bm.free()

    mesh.materials.append(floor_mat)
    mesh.materials.append(wall_mat)

    obj = bpy.data.objects.new(f"{zone['id']}_cave_shell", mesh)
    collection.objects.link(obj)
    return obj, pts, tops, ceiling_ref, center_ceiling_ref


def random_point_in_fan(center, pts, rnd):
    n = len(pts)
    i = rnd.randrange(n)
    j = (i + 1) % n
    r1, r2 = rnd.random(), rnd.random()
    if r1 + r2 > 1:
        r1, r2 = 1.0 - r1, 1.0 - r2
    px = center[0] + r1 * (pts[i][0] - center[0]) + r2 * (pts[j][0] - center[0])
    py = center[1] + r1 * (pts[i][1] - center[1]) + r2 * (pts[j][1] - center[1])
    return px, py, i, j, r1, r2


def scatter_formations(zone, pts, tops, ceiling_ref, center_ceiling_ref, floor_z, collection, material, seed, params):
    rnd = random.Random(seed + 2000)
    area_scale = max(zone["size"][0] * zone["size"][1], 1.0) * params["formation_scale"]
    stalactite_count = max(0, min(16, int(area_scale * 0.09)))
    stalagmite_count = max(0, min(8, int(area_scale * 0.045)))
    cx, cy = zone["center"]
    min_clearance = 1.6

    for k in range(stalactite_count):
        px, py, i, j, r1, r2 = random_point_in_fan((cx, cy), pts, rnd)
        local_ceiling = center_ceiling_ref * (1 - r1 - r2) + ceiling_ref[i] * r1 + ceiling_ref[j] * r2
        available = max(0.2, local_ceiling - floor_z - min_clearance)
        length = min(rnd.uniform(0.4, 1.3), available)
        if length < 0.25:
            continue
        radius = rnd.uniform(0.1, 0.26)
        sides = rnd.randint(5, 8)
        bpy.ops.mesh.primitive_cone_add(
            radius1=0.0, radius2=radius, depth=length, vertices=sides,
            location=(px, py, local_ceiling - length / 2.0 + 0.05),
        )
        obj = bpy.context.active_object
        obj.name = f"{zone['id']}_stalactite_{k:02d}"
        obj.data.materials.append(material)
        for c in list(obj.users_collection):
            c.objects.unlink(obj)
        collection.objects.link(obj)

    for k in range(stalagmite_count):
        px, py, _, _, _, _ = random_point_in_fan((cx, cy), pts, rnd)
        length = rnd.uniform(0.3, 1.0)
        radius = rnd.uniform(0.13, 0.35)
        sides = rnd.randint(5, 8)
        bpy.ops.mesh.primitive_cone_add(
            radius1=radius, radius2=0.0, depth=length, vertices=sides,
            location=(px, py, floor_z + length / 2.0 - 0.03),
        )
        obj = bpy.context.active_object
        obj.name = f"{zone['id']}_stalagmite_{k:02d}"
        obj.data.materials.append(material)
        for c in list(obj.users_collection):
            c.objects.unlink(obj)
        collection.objects.link(obj)


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
        scene.render.filepath = os.path.join(RENDER_DIR, "02_cave_shell_top.png")
        bpy.ops.render.render(write_still=True)

    persp_coll = get_or_create_collection("Cameras")
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
        persp_coll.objects.link(persp_cam)
    scene.camera = persp_cam
    scene.render.filepath = os.path.join(RENDER_DIR, "02_cave_shell_persp.png")
    bpy.ops.render.render(write_still=True)


_CURRENT_OPENINGS = []


def main():
    global _CURRENT_OPENINGS

    with open(LAYOUT_PATH, "r", encoding="utf-8") as f:
        layout = json.load(f)
    zone_lookup = {z["id"]: z for z in layout["zones"]}
    tier_height = layout["elevation_tiers"]

    bpy.ops.wm.open_mainfile(filepath=BLEND_PATH)

    root = get_or_create_collection("SunkenShrine_Blockout")
    cave_coll = get_or_create_collection("CaveShell", root)
    remove_objects_with_prefixes([f"{zid}_stalactite" for zid in CAVE_SHELL_ZONES | RIM_ONLY_ZONES])
    remove_objects_with_prefixes([f"{zid}_stalagmite" for zid in CAVE_SHELL_ZONES | RIM_ONLY_ZONES])
    remove_objects_with_prefixes([f"{zid}_cave_shell" for zid in CAVE_SHELL_ZONES | RIM_ONLY_ZONES])

    floor_mat = get_or_create_material("Cave_dirt_floor", (0.33, 0.26, 0.18, 1.0))
    wall_mat = get_or_create_material("Cave_rock_wall", (0.42, 0.38, 0.34, 1.0))
    formation_mat = get_or_create_material("Cave_rock_formation", (0.46, 0.42, 0.38, 1.0))

    for zone_id in CAVE_SHELL_ZONES:
        zone = zone_lookup[zone_id]
        remove_objects_with_prefixes([f"{zone_id}_floor", f"{zone_id}_wall"])
        floor_z = tier_height[zone["elevation_tier"]]
        seed = deterministic_seed(zone_id)
        params = params_for(zone_id)
        _CURRENT_OPENINGS = neighbor_opening_angles(zone, zone_lookup)
        obj, pts, tops, ceiling_ref, center_ceiling_ref = build_cave_shell(
            zone, floor_z, cave_coll, floor_mat, wall_mat, seed, params, build_floor=True
        )
        scatter_formations(
            zone, pts, tops, ceiling_ref, center_ceiling_ref, floor_z, cave_coll, formation_mat, seed, params
        )

    for zone_id in RIM_ONLY_ZONES:
        zone = zone_lookup[zone_id]
        remove_objects_with_prefixes([f"{zone_id}_wall"])
        floor_z = tier_height[zone["elevation_tier"]]
        seed = deterministic_seed(zone_id)
        params = params_for(zone_id)
        _CURRENT_OPENINGS = neighbor_opening_angles(zone, zone_lookup)
        water_radius = max(zone["size"][0], zone["size"][1]) / 2.0
        rim_radius = water_radius + 1.2
        obj, pts, tops, ceiling_ref, center_ceiling_ref = build_cave_shell(
            zone, floor_z, cave_coll, floor_mat, wall_mat, seed, params,
            build_floor=False, radius_override=(rim_radius, rim_radius),
        )
        shrunk_zone = {**zone, "size": [zone["size"][0] * 0.6, zone["size"][1] * 0.6]}
        scatter_formations(
            shrunk_zone, pts, tops, ceiling_ref, center_ceiling_ref, floor_z, cave_coll, formation_mat, seed, params
        )

    engine = set_render_engine()
    render_views(layout["bounds"])
    bpy.ops.wm.save_as_mainfile(filepath=BLEND_PATH)
    print(f"Cave shell complete. Engine={engine}. Saved {BLEND_PATH}")


if __name__ == "__main__":
    main()

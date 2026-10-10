"""
Task 4 - Water & verticality pass for Sunken Shrine of the Stag Lord.

Opens the blend produced by Tasks 1-3 and:
  - Upgrades the pool's flat placeholder color to a proper semi-transparent
    water material.
  - Builds a stream winding across the entrance clearing from its north edge
    to a cliff lip at its south edge, a waterfall dropping from there down to
    ground level, a splash pool at the base, and a log crossing the stream
    (entrance_clearing has no listed neighbor to pool_cavern - this is scenic
    verticality, not a walkable connector, matching the layout's own note
    that the drop is "not walkable").
  - Replaces the smooth ramp/flat-deck placeholders Task 1 built for five
    vertical-connector zones with actual stepped geometry: stone steps for
    stair_connector/crypt_stair/west_watchtower, a rough earthen stepped ramp
    for gallery_ramp, and wooden steps + a stilted landing + rails for
    wobbly_bridge.
  - Replaces rubble_bridge's flat deck with an irregular rock/plank span, and
    scatters a few fallen-masonry chunks at the bottom of boss_pit.

Run headless (after 01/02/03 have produced blend/sunken_shrine.blend):
    blender --background --factory-startup --python 04_water_verticality.py

Produces:
    ../blend/sunken_shrine.blend                  (opened, modified, saved back in place)
    ../renders/04_water_verticality_top.png
    ../renders/04_water_verticality_persp.png
"""
import bpy
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

FLOOR_THICKNESS = 0.2
STEP_RISER = 0.35
STAIR_ZONES = {
    "stair_connector": "stone",
    "crypt_stair": "stone",
    "west_watchtower": "stone",
    "gallery_ramp": "earthen",
}

# -- helpers shared with earlier pipeline scripts ---------------------------


def deterministic_seed(text):
    return zlib.crc32(text.encode("utf-8"))


def get_or_create_material(name, color, roughness=0.85, alpha=1.0, emission=None):
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
            mat.show_transparent_back = False
        if emission and "Emission Color" in bsdf.inputs:
            bsdf.inputs["Emission Color"].default_value = emission
            bsdf.inputs["Emission Strength"].default_value = 0.4
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


def add_box(name, center, size_xyz, collection, material, rotation_z=0.0):
    bpy.ops.mesh.primitive_cube_add(size=2.0, location=center)
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = (size_xyz[0] / 2.0, size_xyz[1] / 2.0, size_xyz[2] / 2.0)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if rotation_z:
        obj.rotation_euler = (0, 0, rotation_z)
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


def add_rock_chunk(name, center, size, collection, material, seed):
    rnd = random.Random(seed)
    bpy.ops.mesh.primitive_cube_add(size=2.0, location=center)
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = (size[0] / 2.0, size[1] / 2.0, size[2] / 2.0)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    obj.rotation_euler = (
        rnd.uniform(-0.2, 0.2), rnd.uniform(-0.2, 0.2), rnd.uniform(0, math.pi * 2)
    )
    obj.data.materials.append(material)
    link_only(obj, collection)
    return obj


# -- stepped connectors -------------------------------------------------


def rect_bounds(zone):
    cx, cy = zone["center"]
    w, d = zone["size"]
    return (cx - w / 2.0, cx + w / 2.0, cy - d / 2.0, cy + d / 2.0)


def higher_neighbor_dir(zone, zone_lookup, tier_height):
    self_h = tier_height[zone["elevation_tier"]]
    best = None
    for nid in zone.get("neighbors", []):
        n = zone_lookup.get(nid)
        if not n:
            continue
        nh = tier_height[n["elevation_tier"]]
        if nh > self_h and (best is None or nh > tier_height[best["elevation_tier"]]):
            best = n
    if not best:
        return None

    # Pick the travel axis from actual footprint overlap, not center-to-center
    # distance - a review caught this using the same flawed shortcut Task 3's
    # doorway picker originally used: a zone far from its neighbor's center but
    # directly touching it along one edge (e.g. wobbly_bridge sits far west of
    # goblin_cave_camp's center but touches its southern edge exactly) got the
    # wrong axis, sending the stair climbing in a direction with no connection
    # at all. Whichever axis has the SMALLER range-overlap is the one they're
    # actually adjacent across, matching shared_edge() in 03_built_architecture.py.
    ax0, ax1, ay0, ay1 = rect_bounds(zone)
    bx0, bx1, by0, by1 = rect_bounds(best)
    x_overlap = min(ax1, bx1) - max(ax0, bx0)
    y_overlap = min(ay1, by1) - max(ay0, by0)
    if x_overlap < y_overlap:
        axis = "x"
        sign = 1 if best["center"][0] > zone["center"][0] else -1
    else:
        axis = "y"
        sign = 1 if best["center"][1] > zone["center"][1] else -1
    return axis, sign, tier_height[best["elevation_tier"]], best


def build_steps(zone, low_z, high_z, axis, sign, collection, material, cross_scale=1.0, jitter=0.0, seed=0):
    cx, cy = zone["center"]
    w, d = zone["size"]
    length = d if axis == "y" else w
    cross_dim = (w if axis == "y" else d) * cross_scale
    total_rise = high_z - low_z
    step_count = max(4, int(round(total_rise / STEP_RISER)))
    step_run = length / step_count
    rnd = random.Random(seed)

    for i in range(step_count):
        t = (i + 0.5) / step_count
        local_along = sign * (-length / 2.0 + t * length)
        step_top_z = low_z + total_rise * (i + 1) / step_count
        wobble = rnd.uniform(-jitter, jitter) if jitter else 0.0
        if axis == "y":
            pos = (cx + wobble, cy + local_along, (low_z + step_top_z) / 2.0)
            size = (cross_dim, step_run * 1.08, step_top_z - low_z)
        else:
            pos = (cx + local_along, cy + wobble, (low_z + step_top_z) / 2.0)
            size = (step_run * 1.08, cross_dim, step_top_z - low_z)
        add_box(f"{zone['id']}_step_{i:02d}", pos, size, collection, material)


def build_wood_rails(zone, low_z, high_z, axis, sign, collection, material, cross_dim):
    """A cylinder's local Z axis is its length; these rotations tilt that axis
    to point from the low end to the high end of the stair run, derived from
    Rx/Ry applied to (0,0,1) matching the normalized (run, rise) direction."""
    cx, cy = zone["center"]
    w, d = zone["size"]
    length = d if axis == "y" else w
    half_cross = cross_dim / 2.0 + 0.1
    rail_h = 0.9
    rise = high_z - low_z
    rail_len = math.hypot(length, rise)
    angle = math.atan2(rise, length)  # positive: incline from horizontal

    if axis == "y":
        rot = (sign * (angle - math.pi / 2.0), 0, 0)
    else:
        rot = (0, sign * (math.pi / 2.0 - angle), 0)

    for side in (-1, 1):
        if axis == "y":
            start = (cx + side * half_cross, cy - sign * length / 2.0, low_z + rail_h)
            end = (cx + side * half_cross, cy + sign * length / 2.0, high_z + rail_h)
        else:
            start = (cx - sign * length / 2.0, cy + side * half_cross, low_z + rail_h)
            end = (cx + sign * length / 2.0, cy + side * half_cross, high_z + rail_h)
        mid = tuple((s + e) / 2.0 for s, e in zip(start, end))
        add_cylinder(f"{zone['id']}_rail_{side}", mid, 0.05, rail_len, collection, material, verts=6, rotation=rot)


def bridge_landing_gap(zone, neighbor, axis, sign, high_z, collection, material):
    """A stair's top step sits at its own footprint edge, but the layout's
    zones aren't always touching there (e.g. west_watchtower's footprint
    stops 0.8m short of west_hall's). Add a small flat landing spanning any
    such gap so the stair actually reaches the neighbor instead of ending at
    a drop. A no-op when the footprints already meet or overlap there."""
    cx, cy = zone["center"]
    w, d = zone["size"]
    length = d if axis == "y" else w
    zone_edge = (cy if axis == "y" else cx) + sign * length / 2.0

    nx0, nx1, ny0, ny1 = rect_bounds(neighbor)
    if axis == "y":
        neighbor_edge = ny0 if sign > 0 else ny1
    else:
        neighbor_edge = nx0 if sign > 0 else nx1

    gap = sign * (neighbor_edge - zone_edge)
    if gap <= 0.15:
        return  # already touching or overlapping; nothing to bridge

    mid = zone_edge + sign * gap / 2.0
    cross_dim = (w if axis == "y" else d) * 0.8
    if axis == "y":
        pos, size = (cx, mid, high_z + 0.1), (cross_dim, gap + 0.3, 0.2)
    else:
        pos, size = (mid, cy, high_z + 0.1), (gap + 0.3, cross_dim, 0.2)
    add_box(f"{zone['id']}_gap_landing", pos, size, collection, material)


# -- main -----------------------------------------------------------------


def build_pool_water(zone_lookup):
    water_mat = get_or_create_material(
        "Water_pool", (0.08, 0.32, 0.38, 1.0), roughness=0.15, alpha=0.82,
        emission=(0.05, 0.25, 0.28, 1.0),
    )
    floor_obj = bpy.data.objects.get("pool_cavern_floor")
    if floor_obj:
        floor_obj.data.materials.clear()
        floor_obj.data.materials.append(water_mat)
    return water_mat


def build_stream_and_waterfall(zone_lookup, tier_height, collection):
    clearing = zone_lookup["entrance_clearing"]
    cx, cy = clearing["center"]
    w, d = clearing["size"]
    tier_z = tier_height[clearing["elevation_tier"]]
    top_z = tier_z + FLOOR_THICKNESS / 2.0 + 0.03

    water_mat = get_or_create_material(
        "Water_stream", (0.25, 0.55, 0.58, 1.0), roughness=0.2, alpha=0.85,
        emission=(0.15, 0.4, 0.4, 1.0),
    )
    foam_mat = get_or_create_material("Water_foam", (0.75, 0.85, 0.85, 1.0), roughness=0.3, alpha=0.9)
    wood_mat = get_or_create_material("Wood_log", (0.32, 0.2, 0.12, 1.0), roughness=0.8)

    half_d = d / 2.0
    points = [
        (cx + 1.5, cy + half_d - 0.8),
        (cx + 2.0, cy + half_d * 0.4),
        (cx + 2.7, cy - half_d * 0.15),
        (cx + 3.0, cy - half_d + 0.3),  # cliff lip, just inside the true south edge
    ]
    stream_width = 1.2
    for i in range(len(points) - 1):
        p0, p1 = points[i], points[i + 1]
        mx, my = (p0[0] + p1[0]) / 2.0, (p0[1] + p1[1]) / 2.0
        seg_len = math.hypot(p1[0] - p0[0], p1[1] - p0[1])
        angle = math.atan2(p1[1] - p0[1], p1[0] - p0[0])
        add_box(
            f"stream_seg_{i:02d}", (mx, my, top_z), (seg_len * 1.1, stream_width, 0.06),
            collection, water_mat, rotation_z=angle,
        )

    lip = points[-1]
    waterfall_bottom_z = tier_height["ground"]
    fall_height = tier_z - waterfall_bottom_z
    # The sheet must hang past the clearing's true south edge (cy - half_d), not
    # just past the lip point (which sits 0.3m inside that edge) - otherwise it
    # clips straight through the floor slab instead of pouring over its lip.
    sheet_y = cy - half_d - 0.5
    add_box(
        "waterfall_sheet", (lip[0], sheet_y, (tier_z + waterfall_bottom_z) / 2.0),
        (stream_width * 0.9, 0.15, fall_height), collection, water_mat,
    )
    add_cylinder(
        "waterfall_splash_pool", (lip[0], sheet_y, waterfall_bottom_z + 0.05),
        1.1, 0.1, collection, foam_mat, verts=14,
    )

    log_t = points[1]
    log_mid = ((points[0][0] + points[1][0]) / 2.0, (points[0][1] + points[1][1]) / 2.0)
    seg_angle = math.atan2(points[1][1] - points[0][1], points[1][0] - points[0][0])
    add_cylinder(
        "entrance_log_bridge", (log_mid[0], log_mid[1], top_z + 0.18),
        0.16, stream_width + 0.9, collection, wood_mat, verts=8,
        rotation=(math.pi / 2.0, 0, seg_angle),
    )
    for sign in (-1, 1):
        ox = -math.sin(seg_angle) * (stream_width + 0.9) / 2.0 * sign
        oy = math.cos(seg_angle) * (stream_width + 0.9) / 2.0 * sign
        add_rock_chunk(
            f"entrance_log_bridge_support_{sign}", (log_mid[0] + ox, log_mid[1] + oy, top_z + 0.05),
            (0.4, 0.4, 0.3), collection, get_or_create_material("Cave_rock_formation", (0.46, 0.42, 0.38, 1.0)),
            deterministic_seed(f"log_support_{sign}"),
        )


def build_rubble_span(zone, tier_height, collection):
    rock_mat = get_or_create_material("Cave_rock_formation", (0.46, 0.42, 0.38, 1.0))
    wood_mat = get_or_create_material("Wood_log", (0.32, 0.2, 0.12, 1.0), roughness=0.8)
    cx, cy = zone["center"]
    w, d = zone["size"]
    tier_z = tier_height[zone["elevation_tier"]]
    rnd = random.Random(deterministic_seed(zone["id"]))
    n = 5
    for i in range(n):
        t = (i + 0.5) / n
        py = cy - d / 2.0 + t * d
        mat = wood_mat if i % 2 == 0 else rock_mat
        # Width must reach both edges of the crossing (w) with a little
        # overhang, not just a fraction of it - a narrower piece leaves a
        # visible gap at the side of the span rather than a rough edge.
        size = (w * rnd.uniform(0.95, 1.15), d / n * 1.3, 0.18 + rnd.uniform(0, 0.08))
        add_rock_chunk(
            f"{zone['id']}_span_{i:02d}", (cx + rnd.uniform(-0.2, 0.2), py, tier_z + size[2] / 2.0),
            size, collection, mat, deterministic_seed(f"{zone['id']}_{i}"),
        )


def scatter_pit_debris(zone, tier_height, collection):
    rock_mat = get_or_create_material("Cave_rock_formation", (0.46, 0.42, 0.38, 1.0))
    tier_z = tier_height[zone["elevation_tier"]]
    cx, cy = zone["center"]
    w, d = zone["size"]
    rnd = random.Random(deterministic_seed(zone["id"] + "_debris"))
    for i in range(4):
        px = cx + rnd.uniform(-w / 2.0 * 0.6, w / 2.0 * 0.6)
        py = cy + rnd.uniform(-d / 2.0 * 0.6, d / 2.0 * 0.6)
        size = (rnd.uniform(0.3, 0.6), rnd.uniform(0.3, 0.6), rnd.uniform(0.22, 0.4))
        add_rock_chunk(
            f"{zone['id']}_debris_{i:02d}", (px, py, tier_z + size[2] / 2.0),
            size, collection, rock_mat, deterministic_seed(f"{zone['id']}_debris_{i}"),
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
        scene.render.filepath = os.path.join(RENDER_DIR, "04_water_verticality_top.png")
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
    scene.render.filepath = os.path.join(RENDER_DIR, "04_water_verticality_persp.png")
    bpy.ops.render.render(write_still=True)


def main():
    with open(LAYOUT_PATH, "r", encoding="utf-8") as f:
        layout = json.load(f)
    zone_lookup = {z["id"]: z for z in layout["zones"]}
    tier_height = layout["elevation_tiers"]

    bpy.ops.wm.open_mainfile(filepath=BLEND_PATH)

    root = get_or_create_collection("SunkenShrine_Blockout")
    water_coll = get_or_create_collection("WaterVerticality", root)

    # Idempotency: remove this script's own output from any prior run before
    # regenerating, so re-running doesn't pile up .001 duplicates of every
    # step/rail/stream segment/span/debris chunk.
    remove_objects_with_prefixes([
        "stream_seg_", "waterfall_sheet", "waterfall_splash_pool", "entrance_log_bridge",
        "wobbly_bridge_landing", "wobbly_bridge_stilt",
    ])
    for zone_id in list(STAIR_ZONES) + ["wobbly_bridge"]:
        remove_objects_with_prefixes([f"{zone_id}_step_", f"{zone_id}_rail_", f"{zone_id}_gap_landing"])
    remove_objects_with_prefixes(["rubble_bridge_span_", "boss_pit_debris_"])

    build_pool_water(zone_lookup)
    build_stream_and_waterfall(zone_lookup, tier_height, water_coll)

    stone_mat = get_or_create_material("Stone_step", (0.58, 0.56, 0.53, 1.0))
    earthen_mat = get_or_create_material("Earthen_step", (0.38, 0.3, 0.2, 1.0), roughness=0.95)
    wood_mat = get_or_create_material("Wood_log", (0.32, 0.2, 0.12, 1.0), roughness=0.8)

    for zone_id, style in STAIR_ZONES.items():
        zone = zone_lookup[zone_id]
        remove_objects_with_prefixes([f"{zone_id}_ramp"])
        low_z = tier_height[zone["elevation_tier"]]
        result = higher_neighbor_dir(zone, zone_lookup, tier_height)
        if not result:
            continue
        axis, sign, high_z, neighbor = result
        mat = stone_mat if style == "stone" else earthen_mat
        jitter = 0.0 if style == "stone" else 0.12
        build_steps(zone, low_z, high_z, axis, sign, water_coll, mat, jitter=jitter, seed=deterministic_seed(zone_id))
        bridge_landing_gap(zone, neighbor, axis, sign, high_z, water_coll, mat)

    wb = zone_lookup["wobbly_bridge"]
    remove_objects_with_prefixes(["wobbly_bridge_ramp", "wobbly_bridge_deck"])
    low_z = tier_height[wb["elevation_tier"]]
    result = higher_neighbor_dir(wb, zone_lookup, tier_height)
    if result:
        axis, sign, high_z, _wb_neighbor = result
        build_steps(wb, low_z, high_z, axis, sign, water_coll, wood_mat, cross_scale=0.7, seed=deterministic_seed("wobbly_bridge"))
        build_wood_rails(wb, low_z, high_z, axis, sign, water_coll, wood_mat, wb["size"][0] * 0.7)
        cx, cy = wb["center"]
        w, d = wb["size"]
        length = d if axis == "y" else w
        landing_along = sign * (length / 2.0 + 0.9)
        if axis == "y":
            landing_pos = (cx, cy + landing_along, high_z + 0.1)
        else:
            landing_pos = (cx + landing_along, cy, high_z + 0.1)
        add_box("wobbly_bridge_landing", landing_pos, (1.6, 1.6, 0.2), water_coll, wood_mat)
        for lx in (-0.6, 0.6):
            for ly in (-0.6, 0.6):
                stilt_h = 2.0
                add_cylinder(
                    "wobbly_bridge_stilt", (landing_pos[0] + lx, landing_pos[1] + ly, high_z - stilt_h / 2.0 + 0.1),
                    0.08, stilt_h, water_coll, wood_mat, verts=6,
                )

    rb = zone_lookup["rubble_bridge"]
    remove_objects_with_prefixes(["rubble_bridge_floor"])
    build_rubble_span(rb, tier_height, water_coll)

    scatter_pit_debris(zone_lookup["boss_pit"], tier_height, water_coll)

    engine = set_render_engine()
    render_views(layout["bounds"])
    bpy.ops.wm.save_as_mainfile(filepath=BLEND_PATH)
    print(f"Water & verticality complete. Engine={engine}. Saved {BLEND_PATH}")


if __name__ == "__main__":
    main()

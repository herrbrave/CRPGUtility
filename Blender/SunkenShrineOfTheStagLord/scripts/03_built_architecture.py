"""
Task 3 - Built shrine architecture pass for Sunken Shrine of the Stag Lord.

Opens the blend produced by 01_blockout.py + 02_cave_shell.py and upgrades
every "built_room" zone's walls from the blockout's sealed box into an
actually-connected room.

Doorways are computed PER NEIGHBOR PAIR from actual rectangle overlap, not
independently by each side from center-to-center direction (the first version
of this script did that, and a review caught it: two zones' doors would pick
different walls or different positions along the "right" wall and simply not
line up - a 1.8m-wide fixed doorway centered off a raw direction vector has no
reason to land on the same spot as the neighbor's own pick). For a pair of
zones, the shared boundary is found by comparing how much their X-ranges and
Y-ranges overlap: whichever axis has the SMALLER overlap is the one they're
actually adjacent across (a touching/thin-overlap axis), and the opening is
centered in the OTHER axis's overlap range, which both zones agree on by
construction since it's computed once per pair and applied to both sides. A
neighbor that's a vertical connector (stairs/bridges, no walls of its own)
gets a doorway sized to the connector's own footprint overlap instead of the
fixed door width, so the wall doesn't cut across the middle of a ramp.

Pillars are added only where the layout's own "features" list says so
(currently just north_gallery's "stone pillars") rather than inventing
structure the source map didn't call for, and are placed flanking the room's
long axis rather than straddling the main thoroughfare between its two doors.

Run headless (after 01_blockout.py and 02_cave_shell.py have produced
blend/sunken_shrine.blend):
    blender --background --factory-startup --python 03_built_architecture.py

Produces:
    ../blend/sunken_shrine.blend              (opened, modified, saved back in place)
    ../renders/03_built_architecture_top.png
    ../renders/03_built_architecture_persp.png
"""
import bpy
import json
import os

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
PROJECT_DIR = os.path.dirname(SCRIPT_DIR)
LAYOUT_PATH = os.path.join(SCRIPT_DIR, "layout.json")
BLEND_PATH = os.path.join(PROJECT_DIR, "blend", "sunken_shrine.blend")
RENDER_DIR = os.path.join(PROJECT_DIR, "renders")
os.makedirs(RENDER_DIR, exist_ok=True)

WALL_HEIGHT = 2.6
WALL_THICKNESS = 0.3
DOOR_WIDTH = 1.8
DOOR_HEIGHT = 2.2

# Only zones whose "features" list explicitly calls for pillars get them.
PILLAR_FEATURE_KEYWORDS = ("pillar",)


def get_or_create_material(name, color, roughness=0.85):
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


def rect_bounds(zone):
    cx, cy = zone["center"]
    w, d = zone["size"]
    return (cx - w / 2.0, cx + w / 2.0, cy - d / 2.0, cy + d / 2.0)


def opposite_side(side):
    return {"n": "s", "s": "n", "e": "w", "w": "e"}[side]


def shared_edge(zone_a, zone_b):
    """Find the axis these two zones are actually adjacent across: whichever
    of X/Y has the SMALLER range-overlap is the touching/pinch axis (the wall
    sits there); the opening is centered in the other axis's overlap range,
    which both zones agree on since this is computed once per pair."""
    ax0, ax1, ay0, ay1 = rect_bounds(zone_a)
    bx0, bx1, by0, by1 = rect_bounds(zone_b)
    x_overlap = min(ax1, bx1) - max(ax0, bx0)
    y_overlap = min(ay1, by1) - max(ay0, by0)

    if x_overlap < y_overlap:
        perp_lo, perp_hi = max(ay0, by0), min(ay1, by1)
        side_a = "e" if zone_b["center"][0] > zone_a["center"][0] else "w"
    else:
        perp_lo, perp_hi = max(ax0, bx0), min(ax1, bx1)
        side_a = "n" if zone_b["center"][1] > zone_a["center"][1] else "s"
    return side_a, opposite_side(side_a), perp_lo, perp_hi


def doorway_center_width(perp_lo, perp_hi, max_width, margin=0.5):
    span = perp_hi - perp_lo
    center = (perp_lo + perp_hi) / 2.0
    if span <= 0.3:
        return center, 0.6  # pathological near-zero overlap; cut a minimal opening rather than none
    # A fixed margin shrinks a door toward uselessness on a narrow span (a 1.6m
    # connector overlap minus a 0.5m margin on each side leaves only 0.6m) -
    # scale the margin down once the span itself is tight.
    effective_margin = min(margin, span * 0.15)
    usable = span - 2.0 * effective_margin
    width = min(max_width, usable) if usable > 0.5 else min(max_width, span)
    return center, max(width, 0.6)


def cut_doorway(zone_id, side, along_coord, tier_z, door_width=DOOR_WIDTH):
    wall_obj = bpy.data.objects.get(f"{zone_id}_wall_{side}")
    if not wall_obj:
        return False

    if side in ("n", "s"):
        loc = (along_coord, wall_obj.location.y, tier_z + DOOR_HEIGHT / 2.0)
        size = (door_width, WALL_THICKNESS * 4.0, DOOR_HEIGHT)
    else:
        loc = (wall_obj.location.x, along_coord, tier_z + DOOR_HEIGHT / 2.0)
        size = (WALL_THICKNESS * 4.0, door_width, DOOR_HEIGHT)

    bpy.ops.mesh.primitive_cube_add(size=2.0, location=loc)
    cutter = bpy.context.active_object
    cutter.scale = (size[0] / 2.0, size[1] / 2.0, size[2] / 2.0)

    bpy.context.view_layer.objects.active = wall_obj
    mod = wall_obj.modifiers.new("DoorCut", type="BOOLEAN")
    mod.object = cutter
    mod.operation = "DIFFERENCE"
    mod.solver = "EXACT"
    bpy.ops.object.modifier_apply(modifier=mod.name)

    bpy.data.objects.remove(cutter, do_unlink=True)
    return True


def zone_wants_pillars(zone):
    return any(
        any(kw in feature.lower() for kw in PILLAR_FEATURE_KEYWORDS)
        for feature in zone.get("features", [])
    )


def add_pillars(zone, tier_z, collection, material):
    cx, cy = zone["center"]
    w, d = zone["size"]
    pillar_h = WALL_HEIGHT - 0.2
    # Push the pillars close against the room's long-axis walls (engaged
    # columns) rather than mid-room, which for a small room still chokes the
    # door-to-door path even when offset from dead-center along that axis.
    if w >= d:
        offsets = [(min(w / 2.0 - 0.4, 1.4) * sign, 0.0) for sign in (-1, 1)]
    else:
        offsets = [(0.0, min(d / 2.0 - 0.4, 1.4) * sign) for sign in (-1, 1)]
    for i, (ox, oy) in enumerate(offsets):
        bpy.ops.mesh.primitive_cylinder_add(
            radius=0.32, depth=pillar_h, vertices=8,
            location=(cx + ox, cy + oy, tier_z + pillar_h / 2.0),
        )
        obj = bpy.context.active_object
        obj.name = f"{zone['id']}_pillar_{i:02d}"
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
        scene.render.filepath = os.path.join(RENDER_DIR, "03_built_architecture_top.png")
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
    scene.render.filepath = os.path.join(RENDER_DIR, "03_built_architecture_persp.png")
    bpy.ops.render.render(write_still=True)


def main():
    with open(LAYOUT_PATH, "r", encoding="utf-8") as f:
        layout = json.load(f)
    zone_lookup = {z["id"]: z for z in layout["zones"]}
    tier_height = layout["elevation_tiers"]

    bpy.ops.wm.open_mainfile(filepath=BLEND_PATH)

    root = get_or_create_collection("SunkenShrine_Blockout")
    arch_coll = get_or_create_collection("BuiltArchitecture", root)
    pillar_mat = get_or_create_material("Built_stone_pillar", (0.55, 0.53, 0.5, 1.0))

    built_zones = [z for z in layout["zones"] if z["kind"] == "built_room"]

    processed_pairs = set()
    cut_count = 0
    for zone in built_zones:
        tier_z = tier_height[zone["elevation_tier"]]
        for nid in zone.get("neighbors", []):
            neighbor = zone_lookup.get(nid)
            if not neighbor:
                continue

            if neighbor["kind"] == "built_room":
                pair_key = frozenset((zone["id"], nid))
                if pair_key in processed_pairs:
                    continue
                processed_pairs.add(pair_key)
                side_a, side_b, perp_lo, perp_hi = shared_edge(zone, neighbor)
                center, width = doorway_center_width(perp_lo, perp_hi, DOOR_WIDTH)
                neighbor_tier_z = tier_height[neighbor["elevation_tier"]]
                if cut_doorway(zone["id"], side_a, center, tier_z, width):
                    cut_count += 1
                if cut_doorway(neighbor["id"], side_b, center, neighbor_tier_z, width):
                    cut_count += 1
            else:
                side_a, _, perp_lo, perp_hi = shared_edge(zone, neighbor)
                max_w = 3.5 if neighbor["kind"] == "vertical_connector" else DOOR_WIDTH
                center, width = doorway_center_width(perp_lo, perp_hi, max_w)
                if cut_doorway(zone["id"], side_a, center, tier_z, width):
                    cut_count += 1

    pillar_zones = 0
    for zone in built_zones:
        if zone_wants_pillars(zone):
            add_pillars(zone, tier_height[zone["elevation_tier"]], arch_coll, pillar_mat)
            pillar_zones += 1

    engine = set_render_engine()
    render_views(layout["bounds"])
    bpy.ops.wm.save_as_mainfile(filepath=BLEND_PATH)
    print(f"Built architecture complete. Engine={engine}. {cut_count} doorways cut, {pillar_zones} zones pillared.")
    print(f"Saved {BLEND_PATH}")


if __name__ == "__main__":
    main()

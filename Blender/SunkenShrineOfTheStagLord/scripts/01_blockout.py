"""
Task 1 - Blockout pass for Sunken Shrine of the Stag Lord.

Reads layout.json (zone footprints/adjacency produced from the reference map)
and builds a greybox volume per zone: a floor slab + perimeter walls, colored
by zone "kind" so the layout reads clearly in a top-down render. Any zone of
kind "vertical_connector" gets a sloped ramp toward whichever neighbor sits at
a higher elevation_tier (or a flat deck if none do, e.g. a bridge with a pit
below rather than a climb above).

Run headless:
    blender --background --factory-startup --python 01_blockout.py

Produces:
    ../blend/sunken_shrine.blend   (overwritten fresh each run of this script)
    ../renders/01_blockout_top.png
"""
import bpy
import bmesh
import json
import math
import os

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
PROJECT_DIR = os.path.dirname(SCRIPT_DIR)
LAYOUT_PATH = os.path.join(SCRIPT_DIR, "layout.json")
BLEND_DIR = os.path.join(PROJECT_DIR, "blend")
RENDER_DIR = os.path.join(PROJECT_DIR, "renders")
os.makedirs(BLEND_DIR, exist_ok=True)
os.makedirs(RENDER_DIR, exist_ok=True)

WALL_HEIGHT = 2.6
WALL_THICKNESS = 0.3
FLOOR_THICKNESS = 0.2

KIND_COLOR = {
    "natural_cave": (0.45, 0.35, 0.25, 1.0),
    "built_room": (0.6, 0.6, 0.63, 1.0),
    "water_feature": (0.12, 0.45, 0.55, 1.0),
    "exterior": (0.30, 0.55, 0.25, 1.0),
    "vertical_connector": (0.8, 0.5, 0.15, 1.0),
    "reference_ground": (0.2, 0.2, 0.2, 1.0),
}

# Zones with no perimeter walls at blockout stage (open cavern lip / outdoor).
NO_WALL_KINDS = {"water_feature", "exterior"}


def clear_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for block_collection in (bpy.data.meshes, bpy.data.materials, bpy.data.cameras, bpy.data.lights):
        for block in list(block_collection):
            if block.users == 0:
                block_collection.remove(block)


def get_or_create_collection(name, parent=None):
    if name in bpy.data.collections:
        coll = bpy.data.collections[name]
    else:
        coll = bpy.data.collections.new(name)
        (parent or bpy.context.scene.collection).children.link(coll)
    return coll


def get_material(kind):
    mat_name = f"Blockout_{kind}"
    if mat_name in bpy.data.materials:
        return bpy.data.materials[mat_name]
    mat = bpy.data.materials.new(mat_name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Base Color"].default_value = KIND_COLOR.get(kind, (0.8, 0.8, 0.8, 1.0))
        if "Roughness" in bsdf.inputs:
            bsdf.inputs["Roughness"].default_value = 0.9
    mat.diffuse_color = KIND_COLOR.get(kind, (0.8, 0.8, 0.8, 1.0))
    return mat


def link_only(obj, collection):
    for c in list(obj.users_collection):
        c.objects.unlink(obj)
    collection.objects.link(obj)


def add_box(name, center, size_xyz, collection, material):
    bpy.ops.mesh.primitive_cube_add(size=2.0, location=center)
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = (size_xyz[0] / 2.0, size_xyz[1] / 2.0, size_xyz[2] / 2.0)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    obj.data.materials.append(material)
    link_only(obj, collection)
    return obj


def add_cylinder(name, center, radius, height, collection, material, verts=28):
    bpy.ops.mesh.primitive_cylinder_add(
        radius=radius, depth=height, location=center, vertices=verts
    )
    obj = bpy.context.active_object
    obj.name = name
    obj.data.materials.append(material)
    link_only(obj, collection)
    return obj


def build_floor(zone, collection, material):
    cx, cy = zone["center"]
    tier_z = TIER_HEIGHT[zone["elevation_tier"]]
    w, d = zone["size"]
    floor_z = tier_z - FLOOR_THICKNESS / 2.0
    if zone["shape"] == "circular":
        radius = max(w, d) / 2.0
        add_cylinder(
            f"{zone['id']}_floor", (cx, cy, floor_z), radius, FLOOR_THICKNESS, collection, material
        )
    else:
        add_box(
            f"{zone['id']}_floor", (cx, cy, floor_z), (w, d, FLOOR_THICKNESS), collection, material
        )


def build_walls(zone, collection, material):
    cx, cy = zone["center"]
    tier_z = TIER_HEIGHT[zone["elevation_tier"]]
    w, d = zone["size"]
    wall_z = tier_z + WALL_HEIGHT / 2.0

    if zone["shape"] == "circular":
        radius = max(w, d) / 2.0
        outer = add_cylinder(
            f"{zone['id']}_wall_ring_outer",
            (cx, cy, wall_z),
            radius,
            WALL_HEIGHT,
            collection,
            material,
        )
        # Thin the ring down to a rim by scaling an inset cylinder as a visual cue only
        # (actual boolean cut happens in the cave-shell pass; here it's just a placeholder rim).
        outer.scale = (1.0, 1.0, 1.0)
        return

    half_w, half_d = w / 2.0, d / 2.0
    edges = [
        (f"{zone['id']}_wall_n", (cx, cy + half_d, wall_z), (w, WALL_THICKNESS, WALL_HEIGHT)),
        (f"{zone['id']}_wall_s", (cx, cy - half_d, wall_z), (w, WALL_THICKNESS, WALL_HEIGHT)),
        (f"{zone['id']}_wall_e", (cx + half_w, cy, wall_z), (WALL_THICKNESS, d, WALL_HEIGHT)),
        (f"{zone['id']}_wall_w", (cx - half_w, cy, wall_z), (WALL_THICKNESS, d, WALL_HEIGHT)),
    ]
    for name, loc, size in edges:
        add_box(name, loc, size, collection, material)


def build_ramp(zone, collection, material, low_z, high_z, axis, sign):
    """Sloped placeholder block rising from low_z to high_z, oriented toward
    whichever neighbor sits at the higher tier (sign flips the tilt direction
    when that neighbor is on the -axis side instead of +axis)."""
    cx, cy = zone["center"]
    w, d = zone["size"]
    length = d if axis == "y" else w
    rise = high_z - low_z
    angle = math.atan2(rise, max(length, 0.01)) * sign

    bpy.ops.mesh.primitive_cube_add(size=2.0, location=(cx, cy, (low_z + high_z) / 2.0))
    obj = bpy.context.active_object
    obj.name = f"{zone['id']}_ramp"
    if axis == "y":
        obj.scale = (w / 2.0, d / 2.0 / max(math.cos(angle), 0.2), 0.15)
    else:
        obj.scale = (w / 2.0 / max(math.cos(angle), 0.2), d / 2.0, 0.15)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if axis == "y":
        obj.rotation_euler = (angle, 0, 0)
    else:
        obj.rotation_euler = (0, -angle, 0)
    obj.data.materials.append(material)
    link_only(obj, collection)
    return obj


def build_zone(zone, root_collection, zone_lookup):
    kind = zone["kind"]
    material = get_material(kind)
    zone_coll = get_collection_for_zone(zone, root_collection)

    if kind == "vertical_connector":
        # Ramp toward whichever neighbor sits at a higher tier than this zone's
        # own (lower-end) tier; a connector with no higher neighbor (e.g. a
        # bridge deck with a pit zone below it, not above) gets a flat deck.
        self_h = TIER_HEIGHT[zone["elevation_tier"]]
        higher_neighbor = None
        for nid in zone.get("neighbors", []):
            neighbor = zone_lookup.get(nid)
            if not neighbor:
                continue
            nh = TIER_HEIGHT[neighbor["elevation_tier"]]
            if nh > self_h and (higher_neighbor is None or nh > TIER_HEIGHT[higher_neighbor["elevation_tier"]]):
                higher_neighbor = neighbor
        if higher_neighbor:
            dx = higher_neighbor["center"][0] - zone["center"][0]
            dy = higher_neighbor["center"][1] - zone["center"][1]
            axis = "x" if abs(dx) >= abs(dy) else "y"
            sign = 1 if (dx if axis == "x" else dy) >= 0 else -1
            build_ramp(zone, zone_coll, material, self_h, TIER_HEIGHT[higher_neighbor["elevation_tier"]], axis, sign)
            return
        build_floor(zone, zone_coll, material)
        return

    build_floor(zone, zone_coll, material)
    if kind not in NO_WALL_KINDS:
        build_walls(zone, zone_coll, material)


_zone_collections = {}


def get_collection_for_zone(zone, root_collection):
    kind = zone["kind"]
    if kind not in _zone_collections:
        _zone_collections[kind] = get_collection_for_kind(kind, root_collection)
    return _zone_collections[kind]


def get_collection_for_kind(kind, root_collection):
    return get_or_create_collection(f"Blockout_{kind}", root_collection)


def add_reference_floor(bounds):
    coll = get_or_create_collection("Reference")
    mat = get_material("reference_ground")
    cx = (bounds["min"][0] + bounds["max"][0]) / 2.0
    cy = (bounds["min"][1] + bounds["max"][1]) / 2.0
    lowest_tier = min(TIER_HEIGHT.values())
    add_box(
        "ground_reference_plane",
        (cx, cy, lowest_tier - 0.25),
        (bounds["width"] + 8, bounds["depth"] + 8, 0.1),
        coll,
        mat,
    )


def setup_camera_and_light(bounds):
    cam_coll = get_or_create_collection("Cameras")
    cx = (bounds["min"][0] + bounds["max"][0]) / 2.0
    cy = (bounds["min"][1] + bounds["max"][1]) / 2.0
    span = max(bounds["max"][0] - bounds["min"][0], bounds["max"][1] - bounds["min"][1])

    cam_data = bpy.data.cameras.new("TopDownCam")
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = span * 1.15
    cam_obj = bpy.data.objects.new("TopDownCam", cam_data)
    cam_obj.location = (cx, cy, 40)
    cam_obj.rotation_euler = (0, 0, 0)
    cam_coll.objects.link(cam_obj)
    bpy.context.scene.camera = cam_obj

    light_coll = get_or_create_collection("Lights")
    light_data = bpy.data.lights.new("SunTop", type="SUN")
    light_data.energy = 3.0
    light_obj = bpy.data.objects.new("SunTop", light_data)
    light_obj.location = (cx, cy, 30)
    light_obj.rotation_euler = (0.4, 0.2, 0)
    light_coll.objects.link(light_obj)


def set_render_engine():
    scene = bpy.context.scene
    for candidate in ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE", "BLENDER_EEVEE_LEGACY"):
        try:
            scene.render.engine = candidate
            return candidate
        except TypeError:
            continue
    return scene.render.engine


def main():
    with open(LAYOUT_PATH, "r", encoding="utf-8") as f:
        layout = json.load(f)

    clear_scene()
    root = get_or_create_collection("SunkenShrine_Blockout")

    global TIER_HEIGHT
    TIER_HEIGHT = layout["elevation_tiers"]

    zone_lookup = {z["id"]: z for z in layout["zones"]}
    for zone in layout["zones"]:
        build_zone(zone, root, zone_lookup)

    add_reference_floor(layout["bounds"])
    setup_camera_and_light(layout["bounds"])
    engine = set_render_engine()

    scene = bpy.context.scene
    scene.render.resolution_x = 1600
    scene.render.resolution_y = 1280
    scene.render.filepath = os.path.join(RENDER_DIR, "01_blockout_top.png")
    scene.render.image_settings.file_format = "PNG"

    blend_path = os.path.join(BLEND_DIR, "sunken_shrine.blend")
    bpy.ops.wm.save_as_mainfile(filepath=blend_path)

    bpy.ops.render.render(write_still=True)
    print(f"Blockout complete. Engine={engine}. Saved {blend_path}")
    print(f"Render written to {scene.render.filepath}")


if __name__ == "__main__":
    main()

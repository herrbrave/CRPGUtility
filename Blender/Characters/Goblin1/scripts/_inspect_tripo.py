"""Import the Tripo-generated OBJ and render a turnaround for review,
using vertex-color shading since the OBJ bakes color into vertex colors
rather than a texture/material."""
import bpy
import mathutils

OBJ_PATH = r"C:\Users\Peter\wkspaces\CRPGUnity\Blender\Characters\Goblin1\generated\tripo_convert_4300fe2a-8764-48d7-b08e-e9221e4b24fe.obj"
OUT_DIR = r"C:\Users\Peter\wkspaces\CRPGUnity\Blender\Characters\Goblin1\renders"

for obj in list(bpy.data.objects):
    bpy.data.objects.remove(obj, do_unlink=True)

bpy.ops.wm.obj_import(filepath=OBJ_PATH)
imported = [o for o in bpy.context.selected_objects if o.type == "MESH"]
print("IMPORTED", [o.name for o in imported], "vert_counts", [len(o.data.vertices) for o in imported])

mins = mathutils.Vector((1e9, 1e9, 1e9))
maxs = mathutils.Vector((-1e9, -1e9, -1e9))
for obj in imported:
    for corner in obj.bound_box:
        wc = obj.matrix_world @ mathutils.Vector(corner)
        mins.x, mins.y, mins.z = min(mins.x, wc.x), min(mins.y, wc.y), min(mins.z, wc.z)
        maxs.x, maxs.y, maxs.z = max(maxs.x, wc.x), max(maxs.y, wc.y), max(maxs.z, wc.z)
center = (mins + maxs) / 2
size = maxs - mins
radius = max(size.x, size.y, size.z) / 2 * 1.25
print("BOUNDS", mins[:], maxs[:], "CENTER", center[:], "RADIUS", radius)

scene = bpy.context.scene
scene.render.engine = "BLENDER_WORKBENCH"
scene.display.shading.light = "STUDIO"
scene.display.shading.color_type = "VERTEX"
scene.display.shading.show_shadows = False
scene.render.resolution_x = 900
scene.render.resolution_y = 1200
scene.world = scene.world or bpy.data.worlds.new("World")


def setup_cam(name, offset_dir, look_center, look_radius):
    cam_data = bpy.data.cameras.new(name)
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = look_radius * 2
    cam_obj = bpy.data.objects.new(name, cam_data)
    bpy.context.collection.objects.link(cam_obj)
    offset_dir = offset_dir.normalized()
    cam_obj.rotation_euler = offset_dir.to_track_quat("Z", "Y").to_euler()
    cam_obj.location = look_center + offset_dir * (look_radius * 4)
    return cam_obj


views = {
    "tripo_front": mathutils.Vector((0, 1, 0)),
    "tripo_side": mathutils.Vector((1, 0, 0)),
    "tripo_3q": mathutils.Vector((0.65, 0.65, 0.4)),
    "tripo_back": mathutils.Vector((0, -1, 0)),
}
for name, offset in views.items():
    cam = setup_cam(f"Cam_{name}", offset, center, radius)
    scene.camera = cam
    scene.render.filepath = f"{OUT_DIR}\\{name}.png"
    bpy.ops.render.render(write_still=True)

print("TRIPO_INSPECT_OK")

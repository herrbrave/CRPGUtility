"""Reusable review tool: orthographic front/side/3-quarter renders of
whatever is parented under 'GoblinRoot', using Workbench solid shading
(random per-object color) so geometry reads clearly without needing
materials or lighting to be set up yet. Re-run any time via
execute_blender_code to get an updated turnaround after editing the mesh.

Camera placement uses an explicit view-offset-direction + to_track_quat
(point local +Z at the offset dir, so -Z/forward looks back at the
subject) rather than hand-picked Euler angles - picking Euler signs by
hand previously placed every "front" camera behind the character looking
through it, so renders silently showed the character's BACK the whole
time. to_track_quat removes that whole class of sign error.

Writes directly to disk via bpy.ops.render.render (not the sandboxed MCP
render_*_to_path tools), to:
  Blender/Characters/Goblin1/renders/check_front.png
  Blender/Characters/Goblin1/renders/check_side.png
  Blender/Characters/Goblin1/renders/check_3q.png
  Blender/Characters/Goblin1/renders/check_face.png
  Blender/Characters/Goblin1/renders/check_face_side.png  (profile closeup)
  Blender/Characters/Goblin1/renders/check_hand_top.png   (left hand)
  Blender/Characters/Goblin1/renders/check_hand_front.png (left hand)
"""
import bpy
import mathutils

OUT_DIR = r"C:\Users\Peter\wkspaces\CRPGUnity\Blender\Characters\Goblin1\renders"

root = bpy.data.objects.get("GoblinRoot")

# compute world-space bounding box across all mesh children of root
mins = mathutils.Vector((1e9, 1e9, 1e9))
maxs = mathutils.Vector((-1e9, -1e9, -1e9))
mesh_objs = [o for o in bpy.data.objects if o.type == "MESH"]
for obj in mesh_objs:
    for corner in obj.bound_box:
        world_co = obj.matrix_world @ mathutils.Vector(corner)
        mins.x, mins.y, mins.z = min(mins.x, world_co.x), min(mins.y, world_co.y), min(mins.z, world_co.z)
        maxs.x, maxs.y, maxs.z = max(maxs.x, world_co.x), max(maxs.y, world_co.y), max(maxs.z, world_co.z)
center = (mins + maxs) / 2
size = maxs - mins
radius = max(size.x, size.y, size.z) / 2 * 1.25

scene = bpy.context.scene
scene.render.engine = "BLENDER_WORKBENCH"
scene.display.shading.light = "STUDIO"
scene.display.shading.color_type = "RANDOM"
scene.display.shading.show_shadows = False
scene.render.resolution_x = 900
scene.render.resolution_y = 1200
scene.render.film_transparent = False
scene.world = scene.world or bpy.data.worlds.new("World")

# Head/Nose/Ears get joined into Goblin1_Body by stage 1, so they can't be
# found by name/bbox after that - 01_body_base.py stashes the head center
# and radius as custom properties on GoblinRoot instead.
head_center = mathutils.Vector(root["head_center"])
head_radius = root["head_radius"] * 1.5


def setup_cam(name, offset_dir, look_center, look_radius):
    """offset_dir: unit-ish vector from the subject TO the camera (where the
    camera sits). The camera is rotated so it looks back at look_center."""
    cam_data = bpy.data.cameras.get(name) or bpy.data.cameras.new(name)
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = look_radius * 2
    # fit the (narrower) image width, so the T-posed arms/hands aren't cropped
    cam_data.sensor_fit = "HORIZONTAL"
    cam_obj = bpy.data.objects.get(name)
    if cam_obj is None:
        cam_obj = bpy.data.objects.new(name, cam_data)
        bpy.context.collection.objects.link(cam_obj)
    cam_obj.data = cam_data
    offset_dir = offset_dir.normalized()
    # local +Z points along offset_dir, so local -Z (camera forward) points
    # back toward the subject.
    cam_obj.rotation_euler = offset_dir.to_track_quat("Z", "Y").to_euler()
    cam_obj.location = look_center + offset_dir * (look_radius * 4)
    return cam_obj


views = {
    "check_front": ("CamFront", mathutils.Vector((0, 1, 0)), center, radius),
    "check_side": ("CamSide", mathutils.Vector((1, 0, 0)), center, radius),
    "check_3q": ("CamThreeQuarter", mathutils.Vector((0.65, 0.65, 0.4)), center, radius),
    "check_face": ("CamFace", mathutils.Vector((0, 1, 0)), head_center, head_radius),
    # profile closeup - the nose/beak silhouette only reads from the side
    # (framed wider and shifted forward/down so the beak + grin fit)
    "check_face_side": ("CamFaceSide", mathutils.Vector((1, 0, 0)),
                        head_center + mathutils.Vector((0, 0.10, -0.02)), root["head_radius"] * 1.6),
}
# hand closeups (top-down and front), if stage 1 stashed the hand center
if "hand_center_L" in root.keys():
    hand_center = mathutils.Vector(root["hand_center_L"])
    hand_radius = root["H"] * 0.8
    views["check_hand_top"] = ("CamHandTop", mathutils.Vector((0, 0.15, 1)), hand_center, hand_radius)
    views["check_hand_front"] = ("CamHandFront", mathutils.Vector((0.3, 1, 0.25)), hand_center, hand_radius)

for filename, (cam_name, offset_dir, look_center, look_radius) in views.items():
    cam_obj = setup_cam(cam_name, offset_dir, look_center, look_radius)
    scene.camera = cam_obj
    scene.render.filepath = f"{OUT_DIR}\\{filename}.png"
    bpy.ops.render.render(write_still=True)

print("CHECK_VIEWS_OK", center[:], radius)

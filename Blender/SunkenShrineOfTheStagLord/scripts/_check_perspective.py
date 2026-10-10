"""Dev-only check: render the saved blockout .blend from an angled perspective
to verify elevation tiers (ground / upper_deck / lower_pit) and the stair ramp
and bridge platform read correctly in 3D, not just in top-down silhouette.
Not part of the numbered pipeline - rerun any time as a sanity check.
"""
import bpy
import os

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
PROJECT_DIR = os.path.dirname(SCRIPT_DIR)
BLEND_PATH = os.path.join(PROJECT_DIR, "blend", "sunken_shrine.blend")
RENDER_PATH = os.path.join(PROJECT_DIR, "renders", "_check_perspective.png")

bpy.ops.wm.open_mainfile(filepath=BLEND_PATH)

cam_data = bpy.data.cameras.new("PerspCheckCam")
cam_data.type = "PERSP"
cam_data.lens = 28
cam_obj = bpy.data.objects.new("PerspCheckCam", cam_data)
cam_obj.location = (-28, -42, 26)
cam_obj.rotation_euler = (1.15, 0, -0.55)
bpy.context.scene.collection.objects.link(cam_obj)
bpy.context.scene.camera = cam_obj

scene = bpy.context.scene
scene.render.resolution_x = 1600
scene.render.resolution_y = 1200
scene.render.filepath = RENDER_PATH
scene.render.image_settings.file_format = "PNG"
bpy.ops.render.render(write_still=True)
print(f"Perspective check written to {RENDER_PATH}")

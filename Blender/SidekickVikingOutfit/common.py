"""Shared helpers for the Sidekick human Viking outfit scripts (run inside `blender --background --python <script> -- <args>`).

The Sidekick human body is ONE base mesh per region on a shared 88-bone skeleton (FBX units: centimetres, mesh data Y-up, the armature object rotated 90 degrees about X by the importer).
Everything here works in the mesh's LOCAL (centimetre, Y-up) coordinates.
"""
import math
import os
import sys

import bpy
import bmesh
from mathutils import Vector

PACK = "C:/Users/Peter/wkspaces/CRPGUnity/VikingRaid/Assets/Synty/SidekickCharacters/Resources/Meshes/Species/Humans/"
BODY_REGIONS = ["10TORS", "11AUPL", "12AUPR", "13ALWL", "14ALWR", "15HNDL", "16HNDR", "17HIPS", "18LEGL", "19LEGR", "20FOTL", "21FOTR"]
LEFT_RIGHT = {"L": "R", "R": "L"}


def arg(index=0, default=None):
    """The index-th argument after `--`."""
    if "--" not in sys.argv:
        return default
    rest = sys.argv[sys.argv.index("--") + 1:]
    return rest[index] if index < len(rest) else default


def import_body(regions=BODY_REGIONS):
    """Imports the base-01 body parts into an empty scene; returns ({region: mesh object}, armature object).

    Every FBX brings its own copy of the armature: the first is kept, the others deleted, and every mesh is re-pointed at the kept one (they are identical).
    """
    bpy.ops.wm.read_factory_settings(use_empty=True)
    meshes, armatures = {}, []
    for region in regions:
        before = set(bpy.data.objects)
        bpy.ops.import_scene.fbx(filepath=f"{PACK}SK_HUMN_BASE_01_{region}_HU01.fbx")
        new = [o for o in bpy.data.objects if o not in before]
        armatures += [o for o in new if o.type == 'ARMATURE']
        meshes[region] = next(o for o in new if o.type == 'MESH')

    keep = armatures[0]
    for region, mesh in meshes.items():
        mesh.parent = keep
        for modifier in mesh.modifiers:
            if modifier.type == 'ARMATURE':
                modifier.object = keep
    for extra in armatures[1:]:
        bpy.data.objects.remove(extra, do_unlink=True)
    return meshes, keep


def basis_positions(mesh_object):
    """The Basis shape's vertex positions (local space)."""
    me = mesh_object.data
    if me.shape_keys:
        return [Vector(d.co) for d in me.shape_keys.key_blocks[0].data]
    return [Vector(v.co) for v in me.vertices]


def set_material_colour(obj, rgb):
    """A flat display colour for Workbench renders (object colour)."""
    obj.color = (rgb[0], rgb[1], rgb[2], 1.0)


def render_views(path_prefix, views=("front", "back", "side"), resolution=(640, 960), objects=None, color_type='OBJECT'):
    """Workbench renders of the whole scene from the chosen sides, saved as <prefix>_<view>.png. Object colours drive the shading."""
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.render.resolution_x, scene.render.resolution_y = resolution
    scene.render.image_settings.file_format = 'PNG'
    scene.display.shading.light = 'STUDIO'
    scene.display.shading.color_type = color_type
    scene.display.shading.show_cavity = False
    scene.display.render_aa = '8'
    scene.world = bpy.data.worlds.new("w") if scene.world is None else scene.world
    scene.world.color = (0.2, 0.22, 0.26)
    cam_data = bpy.data.cameras.new("cam")
    cam_data.type = 'ORTHO'
    cam_data.ortho_scale = 1.95
    cam = bpy.data.objects.new("cam", cam_data)
    scene.collection.objects.link(cam)
    scene.camera = cam
    centre = Vector((0.0, 0.0, 0.88))   # Z-up scene: the character stands on Z=0
    directions = {"front": Vector((0, -1, 0)), "back": Vector((0, 1, 0)), "side": Vector((1, 0, 0))}
    for view in views:
        d = directions[view]
        cam.location = centre + d * 6.0
        cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
        scene.render.filepath = f"{path_prefix}_{view}.png"
        bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(cam, do_unlink=True)

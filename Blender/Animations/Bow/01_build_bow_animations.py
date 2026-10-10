"""Builds Bow_Draw (46 frames, release on frame 29) and Bow_Idle (91-frame loop) from scratch and exports them as a skeleton-only FBX.

Run headless:   blender --background --python Blender/Animations/Bow/01_build_bow_animations.py
(also works in a live session: it starts from an empty scene.) Inputs: the Quaternius UAL FBX (CC0) inside the VikingRaid Unity project.
Output: Blender/Animations/Bow/export/BowAnimations.fbx -- the UAL armature (no mesh) carrying ONLY the two baked clips, plain bone keyframes
(IK was baked away). Unity imports it as Humanoid; see VikingRaid/Assets/Editor/BowAnimationImport.cs.
"""
import math
import os
import sys

import bpy

HERE = os.path.dirname(os.path.abspath(__file__)) if "__file__" in globals() else "C:/Users/Peter/wkspaces/CRPGUnity/Blender/Animations/Bow"
sys.path.insert(0, HERE)
import importlib

import bowlib
import bowanim

importlib.reload(bowlib)
importlib.reload(bowanim)

EXPORT = HERE + "/export/BowAnimations.fbx"


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    arm = bowlib.ensure_rig()
    bpy.context.scene.render.fps = 30

    report = {}
    for kind in ("draw", "idle"):
        (f0, f1), poles = bowanim.build_source(kind)
        action = bowanim.bake(kind, f0, f1)
        report[kind] = (action.name, f0, f1, poles)

    # strip everything the FBX must not carry: the rig constraints + IK empties, the stand-in bow, the 43 UAL actions, the mesh
    bowanim.drop_constraints()
    for name in ("IK_H_l", "IK_P_l", "IK_H_r", "IK_P_r", "BowProp", "BowString"):
        o = bpy.data.objects.get(name)
        if o is not None:
            bpy.data.objects.remove(o, do_unlink=True)
    mannequin = bpy.data.objects.get("Mannequin")
    if mannequin is not None:
        bpy.data.objects.remove(mannequin, do_unlink=True)
    keep = {"Bow_Draw", "Bow_Idle"}
    for action in list(bpy.data.actions):
        if action.name not in keep:
            action.use_fake_user = False
            bpy.data.actions.remove(action)
    for pb in arm.pose.bones:
        pb.rotation_quaternion = (1, 0, 0, 0)
    arm.animation_data.action = bpy.data.actions["Bow_Idle"]

    os.makedirs(os.path.dirname(EXPORT), exist_ok=True)
    bpy.context.view_layer.update()  # removed objects leave stale entries in the view layer until it updates
    for o in bpy.context.view_layer.objects:
        if o is not None:
            o.select_set(False)
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.export_scene.fbx(
        filepath=EXPORT, use_selection=True, object_types={"ARMATURE"}, add_leaf_bones=False,
        bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False, bake_anim_force_startend_keying=True,
        bake_anim_step=1.0, bake_anim_simplify_factor=0.0, axis_forward="-Z", axis_up="Y",
        apply_unit_scale=True, global_scale=1.0)
    print("[01_build_bow_animations] exported", EXPORT, report)
    return report


if __name__ == "__main__":
    main()

"""Bow animation library (Viking Raid). Runs in a live Blender session via the MCP bridge or headless.

Builds `Bow_Draw` (one-shot: raise, draw, hold, release, lower) and `Bow_Idle` (ready stance loop) on the Quaternius UAL skeleton
(CC0, imported from the UAL FBX) so the clips retarget to every Humanoid Synty character in Unity.

Method: both hands are placed with IK targets (empties) in RIG space, the left hand keeps the orientation that holds a VERTICAL bow while the
arm rises, the right hand pulls to a cheek anchor; spine / head are FK. Everything is then BAKED to plain FK keyframes (no constraints left),
so the FBX needs nothing but bone rotations.

Rig space (armature-local; the imported armature object itself is rotated 180 deg about Z): forward = -Y, character's LEFT = +X,
character's RIGHT = -X, up = +Z. Rest pose is a T-pose, palms down. hand_l local axes at rest: X = down (the palm normal), Y = along the
arm to the fingers, Z = forward (thumb side); hand_r: X = up, Y = along the arm (-X world), Z = forward.
"""
import math

import bpy
from mathutils import Euler, Matrix, Quaternion, Vector

FBX = "C:/Users/Peter/wkspaces/CRPGUnity/VikingRaid/Assets/ThirdParty/UAL/UAL1_Standard.fbx"
OUT = "C:/Users/Peter/wkspaces/CRPGUnity/Blender/Animations/Bow"
ARM = "Armature"

F = Vector((0, -1, 0))   # forward
L = Vector((1, 0, 0))    # character's left
R = Vector((-1, 0, 0))   # character's right
U = Vector((0, 0, 1))    # up


# ------------------------------------------------------------------ scene / rig access
def arm_obj():
    return bpy.data.objects[ARM]


def ensure_rig():
    """Imports the UAL FBX once (armature + mannequin + its 43 actions)."""
    if ARM in bpy.data.objects:
        return arm_obj()
    bpy.ops.import_scene.fbx(filepath=FBX, use_anim=True)
    return arm_obj()


def bone_head(name):
    return arm_obj().data.bones[name].head_local.copy()


def shoulder_centre():
    return (bone_head("upperarm_l") + bone_head("upperarm_r")) * 0.5


def basis_euler(x, y, z):
    """Euler (XYZ) of the rotation whose columns are the given orthonormal axes."""
    return Matrix((x, y, z)).transposed().to_euler("XYZ")


def hand_basis(side, finger_dir, palm_normal):
    """The pose-space orientation a hand bone needs so its fingers point along finger_dir and its palm faces palm_normal.
    Left: palm normal = local +X; right: palm normal = local -X. Y = fingers; Z = X x Y (thumb side)."""
    y = Vector(finger_dir).normalized()
    p = Vector(palm_normal)
    p = (p - y * p.dot(y)).normalized()  # orthogonalise
    x = p if side == "l" else -p
    z = x.cross(y)
    return basis_euler(x, y, z)


# ------------------------------------------------------------------ empties (IK targets)
def ensure_empty(name):
    o = bpy.data.objects.get(name)
    if o is None:
        o = bpy.data.objects.new(name, None)
        o.empty_display_type = "ARROWS"
        o.empty_display_size = 0.06
        bpy.context.scene.collection.objects.link(o)
    o.parent = arm_obj()
    o.matrix_parent_inverse = Matrix.Identity(4)  # locations are ARMATURE-LOCAL (= rig space)
    o.rotation_mode = "XYZ"
    return o


def place(name, loc=None, euler=None):
    o = ensure_empty(name)
    if loc is not None:
        o.location = Vector(loc)
    if euler is not None:
        o.rotation_euler = euler
    return o


def setup_ik(side):
    """Arm rig for one side. IK on lowerarm_<side> (chain: lowerarm + upperarm) puts the WRIST on IK_H_<side>; Blender's IK drives a bone's
    TAIL, so constraining the hand bone itself would put the fingertips there instead. The hand bone then copies the empty's world rotation.
    IK_P_<side> is the elbow pole."""
    pbs = arm_obj().pose.bones
    for name in ("lowerarm_" + side, "hand_" + side):
        for c in list(pbs[name].constraints):
            pbs[name].constraints.remove(c)
    target = ensure_empty("IK_H_" + side)
    pole = ensure_empty("IK_P_" + side)
    ik = pbs["lowerarm_" + side].constraints.new("IK")
    ik.target = target
    ik.pole_target = pole
    ik.pole_angle = 0.0
    ik.chain_count = 2
    ik.use_stretch = False
    ik.iterations = 80
    rot = pbs["hand_" + side].constraints.new("COPY_ROTATION")
    rot.target = target
    rot.target_space = "WORLD"
    rot.owner_space = "WORLD"
    return ik


def solve():
    bpy.context.view_layer.update()
    return arm_obj().evaluated_get(bpy.context.evaluated_depsgraph_get())


def tune_pole(side):
    """Picks the pole angle that puts the solved elbow closest to the pole empty (the angle depends on the bone roll, so measure it)."""
    ik = next(c for c in arm_obj().pose.bones["lowerarm_" + side].constraints if c.type == "IK")
    pole = bpy.data.objects["IK_P_" + side]
    best = (1e9, 0)
    for deg in range(-180, 181, 15):
        ik.pole_angle = math.radians(deg)
        ev = solve()
        elbow = ev.pose.bones["lowerarm_" + side].head
        d = (elbow - pole.location).length
        if d < best[0]:
            best = (d, deg)
    ik.pole_angle = math.radians(best[1])
    return best


def wrist_error(side):
    ev = solve()
    w = ev.pose.bones["lowerarm_" + side].tail
    return (w - bpy.data.objects["IK_H_" + side].location).length


def hand_axes(side):
    """The solved hand bone's pose-space (X, Y, Z) axes, for checking the orientation constraint."""
    m = solve().pose.bones["hand_" + side].matrix.to_3x3()
    return [Vector(m.col[i]) for i in range(3)]


# ------------------------------------------------------------------ FK helpers
def fk_rot(bone_name, world_axis, degrees):
    """Quaternion for pose bone <bone_name> that rotates it `degrees` about a RIG-SPACE axis (valid for small angles with unrotated parents)."""
    b = arm_obj().data.bones[bone_name]
    rest = b.matrix_local.to_3x3()
    rw = Quaternion(Vector(world_axis).normalized(), math.radians(degrees)).to_matrix()
    return (rest.inverted() @ rw @ rest).to_quaternion()


# ------------------------------------------------------------------ rendering
def ensure_cameras():
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "OBJECT"
    scene.render.resolution_x = 480
    scene.render.resolution_y = 600
    scene.render.film_transparent = False
    world = bpy.data.worlds.get("World") or bpy.data.worlds.new("World")
    scene.world = world
    world.color = (0.25, 0.28, 0.32)
    cams = {}
    # world space: the armature object is rotated 180 deg about Z, so the character faces +Y in the world.
    spec = {
        "CamFront": ((0.0, 4.2, 1.15), (math.radians(90), 0, math.radians(180))),
        "CamSide": ((4.2, 0.0, 1.15), (math.radians(90), 0, math.radians(90))),
        "CamThree": ((3.0, 3.0, 1.45), (math.radians(80), 0, math.radians(135))),
    }
    for name, (loc, rot) in spec.items():
        o = bpy.data.objects.get(name)
        if o is None:
            data = bpy.data.cameras.new(name)
            o = bpy.data.objects.new(name, data)
            bpy.context.scene.collection.objects.link(o)
        o.data.type = "ORTHO"
        o.data.ortho_scale = 2.4
        o.location = loc
        o.rotation_euler = rot
        cams[name] = o
    return cams


def render_views(prefix, views=("CamFront", "CamSide")):
    cams = ensure_cameras()
    scene = bpy.context.scene
    paths = []
    for v in views:
        scene.camera = cams[v]
        path = "%s/renders/%s_%s.png" % (OUT, prefix, v)
        scene.render.filepath = path
        bpy.ops.render.render(write_still=True)
        paths.append(path)
    return paths

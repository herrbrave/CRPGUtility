"""Bow animation builder (Viking Raid): key poses, keyed IK/FK sources, bake, a stand-in bow and filmstrip renders. See bowlib.py for the
rig-space conventions. `import bowanim` after putting this folder on sys.path.

Revision 2 (after an animator review of the first pass): the bow hand sits on the SIGHT LINE (not straight out from the shoulder), the chest
turns -45 deg at full draw (the archer 'T-line') and holds that through release, the head counter-turns split over neck and head, the string
hand is hooked to the jaw and the release draws it back along the jaw line with a relaxed open hand, the bow tips forward on release, the
hold breathes, and the limbs run on staggered timelines with arc keys so the arm does not shorten mid-raise. Release is on frame 29
(fire the projectile / VFX there)."""
import math

import bpy
from mathutils import Matrix, Vector

from bowlib import (F, L, R, U, OUT, arm_obj, bone_head, shoulder_centre, hand_basis, place, setup_ik, solve, tune_pole,
                    fk_rot, ensure_cameras)

FINGERS = ("thumb", "index", "middle", "ring", "pinky")
RELEASE_FRAME = 29


def finger_bones(side):
    return ["%s_%02d_%s" % (f, i, side) for f in FINGERS for i in (1, 2, 3)]


def drop_constraints():
    arm = arm_obj()
    for side in "lr":
        for n in ("lowerarm_" + side, "hand_" + side):
            for c in list(arm.pose.bones[n].constraints):
                arm.pose.bones[n].constraints.remove(c)


def capture_finger_pose(action_name, frame=1):
    """{bone: quaternion} of every finger bone (both hands) of a reference UAL action at `frame`."""
    arm = arm_obj()
    drop_constraints()
    arm.animation_data.action = bpy.data.actions["Armature|Armature|" + action_name]
    bpy.context.scene.frame_set(frame)
    out = {n: arm.pose.bones[n].rotation_quaternion.copy() for s in "lr" for n in finger_bones(s)}
    arm.animation_data.action = None
    return out


# ------------------------------------------------------------------ geometry helpers
UPPER, LOWER = 0.274, 0.273  # upperarm / lowerarm bone lengths of the UAL rig
SPINE_PIVOT = Vector((0.0, 0.03, 0.0))


def twisted(point, degrees):
    """The point after the chest turns `degrees` about the vertical spine axis (negative = the bow shoulder leads, chest turns toward the right)."""
    m = Matrix.Rotation(math.radians(degrees), 3, "Z")
    return SPINE_PIVOT + m @ (Vector(point) - SPINE_PIVOT)


def aim(ls, x=-0.035, reach=0.535, dz=0.03):
    """The bow hand ON THE SIGHT LINE: lateral position x (just right of the face centre), `reach` from the (turned) bow shoulder, slightly
    above it. Straight out from the shoulder would aim the arrow ~20 deg left of the head's direction."""
    dx = ls.x - x
    dy = math.sqrt(max(reach ** 2 - dx ** 2 - dz ** 2, 0.0))
    return Vector((x, ls.y - dy, ls.z + dz))


def solve_elbow(shoulder, wrist, hint):
    """Analytic two-bone elbow: the point at UPPER from the shoulder and LOWER from the wrist that lies furthest toward `hint` (a direction).
    The IK pole is then placed exactly there, so the elbow goes where we choose."""
    d = wrist - shoulder
    dist = min(d.length, UPPER + LOWER - 1e-3)
    axis = d.normalized()
    along = (UPPER ** 2 - LOWER ** 2 + dist ** 2) / (2 * dist)
    h = math.sqrt(max(UPPER ** 2 - along ** 2, 0.0))
    n = Vector(hint) - axis * Vector(hint).dot(axis)
    n = n.normalized() if n.length > 1e-6 else axis.orthogonal().normalized()
    return shoulder + axis * along + n * h


def solve_elbow_at_height(shoulder, wrist, height, prefer):
    """Like solve_elbow, but picks the point on the elbow circle whose height is closest to `height`, preferring the direction `prefer`
    (a full draw has the elbow back at about arrow height; a loose direction hint lets it float up over the head)."""
    d = wrist - shoulder
    dist = min(d.length, UPPER + LOWER - 1e-3)
    axis = d.normalized()
    along = (UPPER ** 2 - LOWER ** 2 + dist ** 2) / (2 * dist)
    h = math.sqrt(max(UPPER ** 2 - along ** 2, 0.0))
    e1 = axis.orthogonal().normalized()
    e2 = axis.cross(e1)
    best, best_score = None, -1e9
    for i in range(360):
        t = math.radians(i)
        e = shoulder + axis * along + (e1 * math.cos(t) + e2 * math.sin(t)) * h
        score = -abs(e.z - height) * 8.0 + (e - shoulder).normalized().dot(Vector(prefer).normalized())
        if score > best_score:
            best, best_score = e, score
    return best


def _pole(shoulder, wrist, spec):
    if isinstance(spec, tuple):
        return solve_elbow_at_height(shoulder, wrist, spec[0], spec[1])
    return solve_elbow(shoulder, wrist, spec)


# ------------------------------------------------------------------ the key poses
def anchors():
    """Named poses. LH/RH = wrist targets (rig space), LHe/RHe = hand orientations, LPh = elbow direction hint, RPh = elbow (height, prefer-dir)
    or a direction. Shoulders are TURNED with the chest, so targets are relative to where the shoulders really are in that pose."""
    C0 = shoulder_centre()
    LS0 = bone_head("upperarm_l")
    RS0 = bone_head("upperarm_r")
    HEAD = Vector((0.0, -0.006, 1.61))  # head centre; the head counter-turns so it keeps facing the target
    LHE = hand_basis("l", F, R)                       # fist around a VERTICAL bow: fingers forward, palm to the midline, thumb up
    LHE_TILT = hand_basis("l", F + U * -0.35, R)      # bow tipped forward ~20 deg (ready stance; the release 'dip')
    LHE_ARC = hand_basis("l", F + U * -0.15, R)

    def rh(finger_dir, palm):
        return hand_basis("r", finger_dir, palm)

    hook = rh(F * 0.9 + L * 0.3, L + U * 0.3)
    anchor_hand = rh(F * 0.9 + L * 0.35, L + U * 0.1)  # fingers along the string, palm toward the jaw

    def pose(twist, lean, head_roll, open_r, LH, RH, LHe, RHe, LPh, RPh):
        LS, RS, C = twisted(LS0, twist), twisted(RS0, twist), twisted(C0, twist)
        return dict(LS=LS, RS=RS, LH=LH(LS, C), RH=RH(LS, RS, C), LHe=LHe, RHe=RHe, LPh=LPh, RPh=RPh,
                    twist=twist, lean=lean, head_roll=head_roll, open_r=open_r)

    lowarm_hint = L * 0.7 + U * -0.7
    P = {}
    # ready: bow hangs forward and tipped, string hand rests at the right hip
    P["ready"] = pose(-10.0, 0.0, 0.0, 0.0,
                      lambda LS, C: LS + F * 0.30 + L * -0.02 + U * -0.40,
                      lambda LS, RS, C: C + R * 0.16 + F * 0.10 + U * -0.48,
                      LHE_TILT, hook, L * 0.4 + U * -1.0, R * 0.3 + U * -1.0 + F * -0.2)
    # arc keys for the raise / lower (a straight Cartesian lerp shortens the arm ~5 cm mid-motion)
    P["arc_up"] = pose(-12.0, 0.0, 0.0, 0.0,
                       lambda LS, C: LS + F * 0.47 + U * -0.23,
                       lambda LS, RS, C: C + R * 0.10 + F * 0.16 + U * -0.30,
                       LHE_ARC, hook, L * 0.5 + U * -0.9, R * 0.5 + U * -0.7 + F * -0.2)
    P["arc_down"] = pose(-20.0, 0.0, 0.0, 0.0,
                         lambda LS, C: LS + F * 0.45 + U * -0.27,
                         lambda LS, RS, C: C + R * 0.14 + F * 0.12 + U * -0.42,
                         LHE_ARC, hook, L * 0.5 + U * -0.9, R * 0.4 + U * -0.9 + F * -0.2)
    # nock: bow up on the sight line, string hand to the string (~15 cm behind the grip)
    P["nock"] = pose(-15.0, 0.0, 0.0, 0.0,
                     lambda LS, C: aim(LS, reach=0.52, dz=0.0),
                     lambda LS, RS, C: C + F * 0.30 + U * -0.10,
                     LHE, hook, lowarm_hint, R * 1.0 + U * -0.4 + F * -0.4)
    # full draw: hand hooked under the jaw, elbow back at arrow height, chest turned
    P["draw"] = pose(-45.0, 3.0, 3.0, 0.0,
                     lambda LS, C: aim(LS),
                     lambda LS, RS, C: HEAD + R * 0.085 + F * -0.01 + U * -0.06,
                     LHE, anchor_hand, lowarm_hint, (1.55, R * 0.4 + F * -1.0))
    # end of the hold: a touch more expansion (back tension, a held breath)
    P["hold"] = pose(-46.0, 4.0, 3.0, 0.0,
                     lambda LS, C: aim(LS, reach=0.538),
                     lambda LS, RS, C: HEAD + R * 0.085 + F * -0.025 + U * -0.06,
                     LHE, anchor_hand, lowarm_hint, (1.56, R * 0.4 + F * -1.0))
    # release (frame 29 = 85% of the travel; 31 = the extreme): the hand springs back along the jaw line, fingers relax open, the bow dips forward
    rel_hand = rh(F * 0.7 + U * 0.2 + R * 0.1, L + F * -0.2)
    P["release_a"] = pose(-46.0, 3.0, 3.0, 0.8,
                          lambda LS, C: aim(LS, reach=0.538, dz=0.02),
                          lambda LS, RS, C: HEAD + R * 0.185 + F * -0.16 + U * -0.035,
                          LHE, rel_hand, lowarm_hint, (1.55, R * 0.5 + F * -1.0))
    P["release"] = pose(-45.0, 2.0, 3.0, 0.7,
                        lambda LS, C: aim(LS, reach=0.535, dz=0.0) + L * 0.02 + U * -0.03,
                        lambda LS, RS, C: HEAD + R * 0.20 + F * -0.18 + U * -0.03,
                        hand_basis("l", F + U * -0.35, R), rel_hand, lowarm_hint, (1.54, R * 0.5 + F * -1.0))
    P["follow"] = pose(-45.0, 1.0, 2.0, 0.6,
                       lambda LS, C: aim(LS, reach=0.53, dz=0.01),
                       lambda LS, RS, C: HEAD + R * 0.20 + F * -0.20 + U * -0.08,
                       LHE, rel_hand, lowarm_hint, (1.50, R * 0.5 + F * -1.0))
    return P


# staggered per-limb timelines (frame, pose): the bow arm leads the raise and lowers later; the string hand trails
DRAW_FK = [(1, "ready"), (8, "nock"), (19, "draw"), (28, "hold"), (29, "release_a"), (31, "release"), (36, "follow"), (46, "ready")]
DRAW_L = [(1, "ready"), (4, "arc_up"), (8, "nock"), (19, "draw"), (28, "hold"), (29, "release_a"), (31, "release"), (36, "follow"), (41, "arc_down"), (46, "ready")]
DRAW_R = [(1, "ready"), (3, "ready"), (9, "nock"), (19, "draw"), (28, "hold"), (29, "release_a"), (31, "release"), (36, "follow"), (43, "ready"), (46, "ready")]
DRAW_RANGE = (1, 46)
IDLE_RANGE = (1, 91)   # a 3 s breathing loop; frame 91 == frame 1 == the ready pose the draw starts and ends on


def clear_empty_animation():
    for n in ("IK_H_l", "IK_P_l", "IK_H_r", "IK_P_r"):
        o = bpy.data.objects.get(n)
        if o and o.animation_data:
            o.animation_data_clear()


def key_limb(side, frame, a):
    """Keys one arm's IK empties (wrist target + hand orientation + elbow pole) for an anchor dict."""
    if side == "l":
        pole, wrist, eul = _pole(a["LS"], a["LH"], a["LPh"]), a["LH"], a["LHe"]
    else:
        pole, wrist, eul = _pole(a["RS"], a["RH"], a["RPh"]), a["RH"], a["RHe"]
    for name, loc, e in (("IK_H_" + side, wrist, eul), ("IK_P_" + side, pole, None)):
        o = place(name, loc, e)
        o.keyframe_insert("location", frame=frame)
        if e is not None:
            o.keyframe_insert("rotation_euler", frame=frame)


def key_fk(frame, a, fist, open_r_pose, breathe=0.0):
    pbs = arm_obj().pose.bones
    # torso: turn the chest so the bow shoulder leads and lean back a little; spread over the three spine bones
    for name, share in (("spine_01", 0.25), ("spine_02", 0.35), ("spine_03", 0.40)):
        pbs[name].rotation_mode = "QUATERNION"
        pbs[name].rotation_quaternion = fk_rot(name, U, a["twist"] * share) @ fk_rot(name, L, -(a["lean"] + breathe) * share)
        pbs[name].keyframe_insert("rotation_quaternion", frame=frame)
    # the head counter-turns completely (it keeps looking down the arrow), split over neck and head so no muscle range is exceeded
    counter = -a["twist"]
    pbs["neck_01"].rotation_mode = "QUATERNION"
    pbs["neck_01"].rotation_quaternion = fk_rot("neck_01", U, counter * 0.4)
    pbs["neck_01"].keyframe_insert("rotation_quaternion", frame=frame)
    pbs["Head"].rotation_mode = "QUATERNION"
    pbs["Head"].rotation_quaternion = fk_rot("Head", F, a["head_roll"]) @ fk_rot("Head", U, counter * 0.6)
    pbs["Head"].keyframe_insert("rotation_quaternion", frame=frame)
    # fingers: a loose fist on both hands (the UAL default grip); the string hand relaxes open on release
    w = a["open_r"]
    for side in "lr":
        for n in finger_bones(side):
            q = fist[n].copy()
            if side == "r" and w > 0:
                q = q.slerp(open_r_pose[n], w)
            pbs[n].rotation_mode = "QUATERNION"
            pbs[n].rotation_quaternion = q
            pbs[n].keyframe_insert("rotation_quaternion", frame=frame)


def reset_pose():
    for pb in arm_obj().pose.bones:
        pb.rotation_mode = "QUATERNION"
        pb.rotation_quaternion = (1, 0, 0, 0)
        pb.location = (0, 0, 0)


def build_source(kind):
    """Builds the keyed (IK + FK, not yet baked) source for 'draw' or 'idle'. Returns ((first, last) frame, pole fits)."""
    arm = arm_obj()
    A = anchors()
    fist = capture_finger_pose("Idle_Loop")
    open_r = capture_finger_pose("Spell_Simple_Idle_Loop")
    arm.animation_data.action = None
    reset_pose()
    clear_empty_animation()
    setup_ik("l")
    setup_ik("r")
    # the pole angle depends on the bone roll, so measure it once on the aim pose
    n = A["nock"]
    place("IK_H_l", n["LH"], n["LHe"]); place("IK_P_l", _pole(n["LS"], n["LH"], n["LPh"]))
    place("IK_H_r", n["RH"], n["RHe"]); place("IK_P_r", _pole(n["RS"], n["RH"], n["RPh"]))
    poles = {"l": tune_pole("l"), "r": tune_pole("r")}

    src = bpy.data.actions.new("Bow_%s_src" % kind)
    arm.animation_data.action = src
    if kind == "draw":
        f0, f1 = DRAW_RANGE
        for frame, name in DRAW_L:
            key_limb("l", frame, A[name])
        for frame, name in DRAW_R:
            key_limb("r", frame, A[name])
        for frame, name in DRAW_FK:
            key_fk(frame, A[name], fist, open_r)
    else:
        f0, f1 = IDLE_RANGE
        base = A["ready"]
        # slow breathing and weight drift: s rises 0 -> 1 -> 0 (a raised cosine), so frame 1 is exactly the ready pose and the loop seam is a
        # smooth extreme, not a stall at a zero crossing
        for frame in range(f0, f1 + 1, 10):
            s = 0.5 * (1.0 - math.cos(2.0 * math.pi * (frame - f0) / (f1 - f0)))
            a = dict(base)
            a["LH"] = base["LH"] + U * (0.020 * s) + F * (0.010 * s)
            a["RH"] = base["RH"] + U * (0.012 * s) + F * (0.006 * s)
            key_limb("l", frame, a)
            key_limb("r", frame, a)
            key_fk(frame, a, fist, open_r, breathe=2.0 * s)
    return (f0, f1), poles


def bake(kind, f0, f1):
    """Bakes the IK/constraint result of the current source into plain FK keyframes on a NEW action named Bow_Draw / Bow_Idle."""
    arm = arm_obj()
    if bpy.context.object is not None and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    for o in bpy.context.view_layer.objects:
        o.select_set(False)
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode="POSE")
    before = set(bpy.data.actions.keys())
    bpy.ops.nla.bake(frame_start=f0, frame_end=f1, step=1, only_selected=False, visual_keying=True,
                     clear_constraints=False, clear_parents=False, use_current_action=False, bake_types={"POSE"})
    bpy.ops.object.mode_set(mode="OBJECT")
    new = [bpy.data.actions[n] for n in bpy.data.actions.keys() if n not in before]
    action = new[-1]
    final_name = "Bow_Draw" if kind == "draw" else "Bow_Idle"
    old = bpy.data.actions.get(final_name)
    if old is not None and old is not action:
        old.name = final_name + "_old"
        bpy.data.actions.remove(old)
    action.name = final_name
    action.use_fake_user = True
    return action


# ------------------------------------------------------------------ stand-in bow + filmstrips (for judging only; never exported)
def ensure_bow_prop():
    arm = arm_obj()
    bow = bpy.data.objects.get("BowProp")
    if bow is None:
        curve = bpy.data.curves.new("BowProp", "CURVE")
        curve.dimensions = "3D"
        curve.bevel_depth = 0.012
        spline = curve.splines.new("POLY")
        pts = 17
        spline.points.add(pts - 1)
        for i in range(pts):
            z = -0.65 + 1.3 * i / (pts - 1)
            spline.points[i].co = (0.0, -0.15 * (z / 0.65) ** 2, z, 1.0)  # limbs curve toward the archer (-Y of the hand)
        bow = bpy.data.objects.new("BowProp", curve)
        bpy.context.scene.collection.objects.link(bow)
        mat = bpy.data.materials.new("BowPropMat")
        mat.diffuse_color = (0.45, 0.28, 0.12, 1.0)
        curve.materials.append(mat)
    bow.parent = arm
    bow.parent_type = "BONE"
    bow.parent_bone = "hand_l"
    bow.matrix_parent_inverse = Matrix.Identity(4)
    bow.location = (0.0, -0.06, 0.0)  # a bone-parented child's origin is the bone TAIL; step back to the palm
    bow.rotation_euler = (0, 0, 0)

    string = bpy.data.objects.get("BowString")
    if string is None:
        mesh = bpy.data.meshes.new("BowString")
        mesh.from_pydata([(0, 0, 0), (0, 0, 0.1), (0, 0, 0.2)], [(0, 1), (1, 2)], [])
        string = bpy.data.objects.new("BowString", mesh)
        bpy.context.scene.collection.objects.link(string)
    return bow, string


def update_string(w):
    """Polyline top tip -> (string hand at weight w) -> bottom tip, in world space (judging only)."""
    bow, string = ensure_bow_prop()
    ev = solve()
    mw = bow.evaluated_get(bpy.context.evaluated_depsgraph_get()).matrix_world
    top = mw @ Vector((0, -0.15, 0.65))
    bottom = mw @ Vector((0, -0.15, -0.65))
    hand = arm_obj().matrix_world @ ev.pose.bones["hand_r"].head
    mid = ((top + bottom) * 0.5).lerp(hand, w)
    me = string.data
    me.vertices[0].co, me.vertices[1].co, me.vertices[2].co = top, mid, bottom
    me.update()


def string_weight(frame, kind):
    return 1.0 if kind == "draw" and 9 <= frame < RELEASE_FRAME else 0.0


def filmstrip(prefix, frames, kind, views=("CamFront", "CamSide")):
    """Renders each frame from each camera (Workbench) and joins the frames of one view into a single PNG strip."""
    import numpy as np
    cams = ensure_cameras()
    scene = bpy.context.scene
    ensure_bow_prop()
    out = []
    for v in views:
        scene.camera = cams[v]
        tiles = []
        for f in frames:
            scene.frame_set(f)
            update_string(string_weight(f, kind))
            scene.render.filepath = "%s/renders/_tmp.png" % OUT
            bpy.ops.render.render(write_still=True)
            img = bpy.data.images.load(scene.render.filepath)
            img.reload()
            w, h = img.size
            tiles.append(np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4))
            bpy.data.images.remove(img)
        strip = np.concatenate(tiles, axis=1)
        path = "%s/renders/%s_%s.png" % (OUT, prefix, v)
        im = bpy.data.images.new("strip_" + v, strip.shape[1], strip.shape[0], alpha=True)
        im.pixels = strip.flatten().tolist()
        im.filepath_raw = path
        im.file_format = "PNG"
        im.save()
        bpy.data.images.remove(im)
        out.append(path)
    return out

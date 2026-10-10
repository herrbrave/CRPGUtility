"""Goblin 1 - stage 1: low-poly body blockout (skin only).

Flat-shaded, low-segment-count primitives (no subsurf) to match the
faceted low-poly style of the reference sheet. Builds torso, head, ears,
arms+hands, legs+feet as separate objects parented to an empty root, then
joins them into a single "Goblin1_Body" mesh. Cloth/gear/weapon/face
detail are later stages.

Rebuilt after an Opus art-direction review against the reference sheet
(the first pass read as a stiff mannequin - ball head, skirt-shaped
torso, stilt legs, ball joints). This pass targets a hunched, crouching
imp: forward-jutting head/muzzle on a short tilted neck, rounded-back
shoulders, bow-legged crouching stance, big clawed hands/feet, no visible
ball joints.

Proportioned in units of H = head crown-to-chin height (0.30m here).
Goblin faces +Y ("front"), stands on the ground at Z=0.
"""
import bpy
import bmesh
import math
import mathutils
from mathutils import Vector

H = 0.30

# ---------------------------------------------------------------------------
# cleanup
# ---------------------------------------------------------------------------
for obj in list(bpy.data.objects):
    bpy.data.objects.remove(obj, do_unlink=True)
for mesh in list(bpy.data.meshes):
    bpy.data.meshes.remove(mesh)

root = bpy.data.objects.new("GoblinRoot", None)
bpy.context.collection.objects.link(root)

created = []


def new_obj(name):
    mesh = bpy.data.meshes.new(name)
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    obj.parent = root
    created.append(obj)
    return obj, bmesh.new()


def finish(obj, bm):
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()


def cone_segment(name, diam1, diam2, depth, segments=8, cap_ends=True):
    """Tapered cylinder along local +Z, base centered at origin."""
    obj, bm = new_obj(name)
    bmesh.ops.create_cone(
        bm, cap_ends=cap_ends, cap_tris=False, segments=segments,
        radius1=diam1 / 2, radius2=diam2 / 2, depth=depth,
    )
    bmesh.ops.translate(bm, verts=bm.verts, vec=(0, 0, depth / 2))
    finish(obj, bm)
    return obj


def icosphere(name, radius, subdiv=2):
    obj, bm = new_obj(name)
    bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=radius)
    finish(obj, bm)
    return obj


def box(name, sx, sy, sz):
    obj, bm = new_obj(name)
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, verts=bm.verts, vec=(sx, sy, sz))
    finish(obj, bm)
    return obj


def aim(obj, direction):
    """Orient obj's local +Z axis to point along `direction` (world space),
    via track-to rather than hand-picked Euler angles."""
    obj.rotation_euler = Vector(direction).normalized().to_track_quat("Z", "Y").to_euler()


def lerp(a, b, t):
    return a + (b - a) * t


def build_digit(prefix, base_loc, direction, length, base_r, tip_r, curl_deg, claw_len,
                segments=2, knuckle_scale=0.0, claw_r_scale=1.0):
    """A multi-segment tapered digit (finger/toe) that curls progressively
    toward the ground (positive curl_deg = tip bends down), ending in a
    sharp claw cone. knuckle_scale > 0 adds a bony knuckle bump at each
    joint (radius = knuckle_scale * local digit radius). Returns the tip
    location and direction for optional further chaining."""
    cur_loc = Vector(base_loc)
    cur_dir = Vector(direction).normalized()
    # Z x dir: rotating dir about this axis by +angle tips it toward -Z
    side_axis = Vector((0, 0, 1)).cross(cur_dir)
    if side_axis.length < 1e-4:
        side_axis = Vector((1, 0, 0))
    side_axis.normalize()
    seg_len = length / segments
    for i in range(segments):
        r1 = lerp(base_r, tip_r, i / segments)
        r2 = lerp(base_r, tip_r, (i + 1) / segments)
        if knuckle_scale > 0:
            knuckle = icosphere(f"{prefix}_knuckle{i}", r1 * knuckle_scale, subdiv=1)
            knuckle.location = cur_loc
        seg = cone_segment(f"{prefix}_{i}", r1 * 2, r2 * 2, seg_len, segments=5)
        seg.location = cur_loc
        aim(seg, cur_dir)
        cur_loc = cur_loc + cur_dir * seg_len
        curl_q = mathutils.Quaternion(side_axis, math.radians(curl_deg))
        cur_dir = (curl_q @ cur_dir).normalized()
    claw = cone_segment(f"{prefix}_claw", tip_r * 2 * claw_r_scale, 0.0015, claw_len, segments=4)
    claw.location = cur_loc
    aim(claw, cur_dir)
    return cur_loc, cur_dir


# ---------------------------------------------------------------------------
# torso: crotch -> hip -> belly (forward) -> chest -> shoulders (hunched
# back), narrow sloped shoulders instead of a flat shelf, no flat hem
# ---------------------------------------------------------------------------
HIP_Z = 1.25 * H  # 0.375

torso, bm = new_obj("Torso")
RING_SEGS = 8
# (z, half_width_x, y_offset, squash_y) - y_offset shifts the ring
# forward(+)/back(-) for the pot-belly and the hunch; squash_y flattens
# front-back like before.
rings_def = [
    (HIP_Z, 0.072, 0.0, 0.8),              # crotch taper (not a flat hem)
    (HIP_Z + 0.33 * H, 0.145, 0.0, 0.85),  # hip
    (HIP_Z + 0.60 * H, 0.155, 0.05, 0.9),  # belly - widest, pushed forward
    (HIP_Z + 0.90 * H, 0.125, -0.01, 0.85),  # chest
    (HIP_Z + 1.20 * H, 0.105, -0.025, 0.8),  # shoulder base - narrow, modest hunch
]
SHOULDER_Z = rings_def[-1][0]
SHOULDER_W = rings_def[-1][1]
SHOULDER_Y = rings_def[-1][2]

ring_verts = []
ring_edges = []
for z, r, y_off, squash in rings_def:
    ring = []
    for i in range(RING_SEGS):
        ang = (2 * math.pi * i) / RING_SEGS
        x = math.cos(ang) * r
        y = y_off + math.sin(ang) * r * squash
        ring.append(bm.verts.new((x, y, z)))
    ring_verts.append(ring)
    edges = [bm.edges.new((ring[i], ring[(i + 1) % RING_SEGS])) for i in range(RING_SEGS)]
    ring_edges.append(edges)
bm.verts.ensure_lookup_table()
for a_edges, b_edges in zip(ring_edges, ring_edges[1:]):
    bmesh.ops.bridge_loops(bm, edges=a_edges + b_edges)
bmesh.ops.edgeloop_fill(bm, edges=ring_edges[0])
bmesh.ops.edgeloop_fill(bm, edges=ring_edges[-1])
bm.verts.ensure_lookup_table()
# slope the shoulders down from the neckline instead of a flat shelf: lower
# the outer-x verts of the top ring
top_ring = ring_verts[-1]
for v in top_ring:
    t = abs(v.co.x) / SHOULDER_W
    v.co.z -= 0.12 * H * t
finish(torso, bm)

# ---------------------------------------------------------------------------
# neck: short, tilted forward - almost no visible gap between jaw and chest
# ---------------------------------------------------------------------------
NECK_BASE = Vector((0, SHOULDER_Y, SHOULDER_Z))
neck_dir = Vector((0, 0.45, 0.85)).normalized()
NECK_LEN = 0.35 * H
neck = cone_segment("Neck", 0.30 * H, 0.34 * H, NECK_LEN, segments=8)
neck.location = NECK_BASE
aim(neck, neck_dir)
NECK_TIP = NECK_BASE + neck_dir * NECK_LEN

# ---------------------------------------------------------------------------
# head: icosphere reshaped into a pear/shield silhouette - narrow cranium,
# wide cheek/jaw-hinge band, blunt chin, forward-projecting lower face
# (muzzle) and a brow shelf. Chin sits near shoulder height; head center is
# pushed forward and slightly up from the neck so the whole head juts past
# the chest in profile (the hunch's counter-lean).
# ---------------------------------------------------------------------------
HEAD_R = 0.5 * H  # 0.15
HEAD_CENTER = NECK_TIP + Vector((0, 0.14 * H, 0.14 * H))

head = icosphere("Head", HEAD_R, subdiv=3)
head.location = HEAD_CENTER
bm = bmesh.new()
bm.from_mesh(head.data)
bm.verts.ensure_lookup_table()
for v in bm.verts:
    t = v.co.z / HEAD_R  # -1 chin .. +1 crown
    # width profile: narrow crown -> wide cheek/jaw-hinge (t~-0.3) -> blunt chin
    if t > -0.3:
        tt = (t - (-0.3)) / (1.0 - (-0.3))
        width_scale = lerp(1.18, 0.85, tt)
    else:
        tt = (t - (-0.3)) / (-1.0 - (-0.3))
        tt = min(1.0, tt)
        width_scale = lerp(1.18, 0.68, tt)
    v.co.x *= width_scale

    is_front = v.co.y > 0.015
    # muzzle: lower face pushes forward, ramping in from t=0.1 down to t=-0.5.
    # Kept modest - the separate Nose/Eye/Brow/Teeth objects (stage 2) sit
    # proud of this surface and carry most of the facial detail; a strong
    # push here would bury them inside the skin instead.
    if is_front:
        m = (0.1 - t) / (0.1 - (-0.5))
        m = max(0.0, min(1.0, m))
        v.co.y += 0.18 * H * m * (v.co.y / HEAD_R)
bm.to_mesh(head.data)
bm.free()
head.data.update()

# ---------------------------------------------------------------------------
# nose: big hooked beak, lofted along a curved spine from the brow down
# past the upper lip
# ---------------------------------------------------------------------------
# Spine points are local to HEAD_CENTER. The root is buried in the face
# between the eyes (it's skin, joined into the body); the bulk projects
# well forward of the skin surface (~0.15-0.18 local y at these heights)
# and hooks down over the centre of the grin. Kept narrow at the root
# (half-width 0.03 < the eyes' inner edge at x~0.045) so it doesn't swallow
# the separate Face_Eye objects, and its underside stays in front of the
# teeth (y~0.20) so they aren't buried either.
nose_spine_local = [
    (Vector((0, 0.115, 0.055)), 0.040),   # root, buried between the brows
    (Vector((0, 0.168, 0.038)), 0.050),   # bulbous base emerging from the face
    (Vector((0, 0.208, 0.012)), 0.053),   # widest point - the bulbous hump
    (Vector((0, 0.238, -0.022)), 0.043),  # narrowing into the hook
    (Vector((0, 0.252, -0.058)), 0.027),  # hooking down
    (Vector((0, 0.230, -0.088)), 0.0),    # tip, curling back toward the lip
]
nose, bm = new_obj("Nose")
nose.location = HEAD_CENTER
prev_ring = None
NOSE_SEGS = 7
NOSE_WIDTH = 1.0  # cross-section scale across the face (x) - round, not a keel
NOSE_DEPTH = 1.05
for idx, (pt, r) in enumerate(nose_spine_local):
    if r <= 0.0001:
        tip_vert = bm.verts.new(pt)
        bm.verts.ensure_lookup_table()
        if prev_ring:
            for a, b in zip(prev_ring, prev_ring[1:] + prev_ring[:1]):
                bm.faces.new((a, b, tip_vert))
        continue
    # rings perpendicular to the spine tangent (a horizontal ring would
    # flatten to nothing where the hook turns downward)
    p_prev = nose_spine_local[max(0, idx - 1)][0]
    p_next = nose_spine_local[min(len(nose_spine_local) - 1, idx + 1)][0]
    tangent = (p_next - p_prev).normalized()
    ax_x = Vector((1, 0, 0))
    ax_n = tangent.cross(ax_x).normalized()
    ring = []
    for i in range(NOSE_SEGS):
        ang = (2 * math.pi * i) / NOSE_SEGS
        off = ax_x * (math.cos(ang) * r * NOSE_WIDTH) + ax_n * (math.sin(ang) * r * NOSE_DEPTH)
        ring.append(bm.verts.new(pt + off))
    if prev_ring:
        for i in range(NOSE_SEGS):
            a, b = prev_ring[i], prev_ring[(i + 1) % NOSE_SEGS]
            c, d = ring[i], ring[(i + 1) % NOSE_SEGS]
            bm.faces.new((a, b, d, c))
    else:
        bm.faces.new(ring)
    prev_ring = ring
bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
# recalc_face_normals only makes normals mutually CONSISTENT, not outward -
# check one side-wall quad against the known-outward direction (ring vert
# minus its spine point) and flip the whole solid if it came out inverted.
bm.faces.ensure_lookup_table()
sample_face = bm.faces[len(bm.faces) // 2]
sample_pt = nose_spine_local[len(nose_spine_local) // 2][0]
outward_ref = (sample_face.calc_center_median() - sample_pt)
if sample_face.normal.dot(outward_ref) < 0:
    bmesh.ops.reverse_faces(bm, faces=bm.faces)
finish(nose, bm)

# ---------------------------------------------------------------------------
# ears: broad leaf-shaped blades, 3-ring taper (not a straight cone),
# oriented purely via direction vector
# ---------------------------------------------------------------------------
EAR_LEN = 0.9 * H
for side, sign in (("L", 1), ("R", -1)):
    ear, bm = new_obj(f"Ear_{side}")
    # local spine along +Z before orienting: base (wide) -> mid (widest) -> tip (point)
    spine = [(0.0, 0.19 * H), (0.45 * EAR_LEN, 0.22 * H), (EAR_LEN, 0.0)]
    EAR_SEGS = 6
    prev_ring = None
    for z, r in spine:
        if r <= 0.0001:
            tip_vert = bm.verts.new((0, 0, z))
            bm.verts.ensure_lookup_table()
            if prev_ring:
                for a, b in zip(prev_ring, prev_ring[1:] + prev_ring[:1]):
                    bm.faces.new((a, b, tip_vert))
            continue
        ring = []
        for i in range(EAR_SEGS):
            ang = (2 * math.pi * i) / EAR_SEGS
            x = math.cos(ang) * r * 0.22  # thin blade depth
            y = math.sin(ang) * r
            ring.append(bm.verts.new((x, y, z)))
        if prev_ring:
            for i in range(EAR_SEGS):
                a, b = prev_ring[i], prev_ring[(i + 1) % EAR_SEGS]
                c, d = ring[i], ring[(i + 1) % EAR_SEGS]
                bm.faces.new((a, b, d, c))
        else:
            # cap the base - leaving it open created a genuine hole through
            # to the hollow tube interior, visible as a dark notch even
            # though the ring sits inside the head surface (open boundary
            # edges aren't blocked just because they're spatially embedded).
            bm.faces.new(ring)
        prev_ring = ring
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    # recalc only makes normals mutually consistent, not outward - verify
    # against a known-outward reference (ring vert minus the ear's local
    # Z-axis base point) and flip the whole solid if it came out inverted.
    bm.faces.ensure_lookup_table()
    sample_face = bm.faces[len(bm.faces) // 2]
    sample_center = sample_face.calc_center_median()
    outward_ref = sample_center - Vector((0, 0, sample_center.z))  # radial from the spine axis
    if sample_face.normal.dot(outward_ref) < 0:
        bmesh.ops.reverse_faces(bm, faces=bm.faces)
    finish(ear, bm)

    # base ring (radius 0.19*H) must stay fully inside the head sphere or
    # its (now capped, but still geometrically real) base pokes through the
    # skin - attach distance from HEAD_CENTER + ring radius must stay
    # under HEAD_R (0.5*H).
    ear.location = HEAD_CENTER + Vector((sign * 0.22 * H, -0.01 * H, 0.04 * H))
    ear_target = Vector((sign * 0.85, -0.3, 0.38))
    aim(ear, ear_target)

# ---------------------------------------------------------------------------
# arms: droop slightly from horizontal, forearm thickens toward the wrist,
# no elbow ball (tiny seam-filler sphere sized to the limb radius only)
# ---------------------------------------------------------------------------
UPPER_ARM_LEN = 0.9 * H
FOREARM_LEN = 0.8 * H
ARM_DROOP = Vector((1, 0, -0.22))

for side, sign in (("L", 1), ("R", -1)):
    shoulder_loc = Vector((sign * SHOULDER_W, SHOULDER_Y, SHOULDER_Z - 0.05 * H))
    arm_dir = Vector((ARM_DROOP.x * sign, ARM_DROOP.y, ARM_DROOP.z)).normalized()

    upper = cone_segment(f"UpperArm_{side}", 0.4 * H, 0.32 * H, UPPER_ARM_LEN, segments=8)
    upper.location = shoulder_loc
    aim(upper, arm_dir)

    elbow_loc = shoulder_loc + arm_dir * UPPER_ARM_LEN
    seam = icosphere(f"ElbowSeam_{side}", 0.16 * H, subdiv=1)
    seam.location = elbow_loc

    fore = cone_segment(f"Forearm_{side}", 0.32 * H, 0.38 * H, FOREARM_LEN, segments=8)
    fore.location = elbow_loc
    aim(fore, arm_dir)

    wrist_loc = elbow_loc + arm_dir * FOREARM_LEN

    # big bony clawed hand, ~0.8H long (wrist -> fingertip, excluding claw)
    # and ~0.45H wide across the knuckles. Palm is a lofted slab in the
    # arm's frame (wide/flat, palm facing down) rather than a scaled box,
    # flaring from the wrist to a wide knuckle line; three thick knuckled
    # fingers spread across that line plus a forward-set thumb.
    hand_fwd = arm_dir
    hand_side = Vector((0, 1, 0))  # toward the goblin's front
    hand_up = hand_fwd.cross(hand_side).normalized()
    if hand_up.z < 0:
        hand_up = -hand_up
    PALM_LEN = 0.36 * H
    WRIST_W, WRIST_T = 0.38 * H, 0.34 * H
    KNUCK_W, KNUCK_T = 0.45 * H, 0.15 * H
    palm_start = wrist_loc - hand_fwd * (0.10 * H)  # tuck into the forearm cap
    palm_end = wrist_loc + hand_fwd * PALM_LEN

    palm, bm = new_obj(f"Palm_{side}")
    rings = []
    # wrist ring, mid ring (slight back-of-hand hump), knuckle ring
    for c, w, t, hump in (
        (palm_start, WRIST_W, WRIST_T, 0.0),
        (lerp(palm_start, palm_end, 0.6), lerp(WRIST_W, KNUCK_W, 0.75), KNUCK_T * 1.1, 0.02 * H),
        (palm_end, KNUCK_W, KNUCK_T, 0.0),
    ):
        hw, ht = w / 2, t / 2
        # 6-vert hexagonal cross-section: flat-ish palm, ridged back of hand
        ring_pts = [
            c + hand_side * hw - hand_up * ht * 0.6,
            c + hand_side * hw * 0.85 + hand_up * ht * 0.6,
            c + hand_up * (ht + hump),
            c - hand_side * hw * 0.85 + hand_up * ht * 0.6,
            c - hand_side * hw - hand_up * ht * 0.6,
            c - hand_up * ht,
        ]
        rings.append([bm.verts.new(p) for p in ring_pts])
    for ra, rb in zip(rings, rings[1:]):
        for i in range(6):
            bm.faces.new((ra[i], ra[(i + 1) % 6], rb[(i + 1) % 6], rb[i]))
    bm.faces.new(list(reversed(rings[0])))
    bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    finish(palm, bm)

    # fingers: three thick 3-joint digits spread across the knuckle line,
    # middle one longest, splaying outward slightly and curling down into
    # big hooked claws
    for i, (side_off, spread_deg, f_len) in enumerate((
        (-0.15 * H, -12, 0.40 * H),
        (0.0, 0, 0.46 * H),
        (0.15 * H, 12, 0.42 * H),
    )):
        q = mathutils.Quaternion(hand_up, math.radians(spread_deg))
        f_dir = (q @ hand_fwd).normalized()
        f_base = palm_end + hand_side * side_off - hand_fwd * (0.03 * H)
        build_digit(
            f"Finger_{side}_{i}", f_base, f_dir,
            length=f_len, base_r=0.075 * H, tip_r=0.045 * H,
            curl_deg=16, claw_len=0.17 * H, segments=3,
            knuckle_scale=1.08, claw_r_scale=1.1,
        )

    # thumb: from the front edge near the wrist, angling forward and out
    thumb_dir = (hand_fwd * 0.55 + hand_side * 0.8 - hand_up * 0.15).normalized()
    build_digit(
        f"Thumb_{side}", lerp(palm_start, palm_end, 0.35) + hand_side * (WRIST_W * 0.45), thumb_dir,
        length=0.30 * H, base_r=0.08 * H, tip_r=0.05 * H,
        curl_deg=14, claw_len=0.14 * H, segments=2,
        knuckle_scale=1.08, claw_r_scale=1.1,
    )
    if side == "L":
        root["hand_center_L"] = tuple(wrist_loc + hand_fwd * (0.4 * H))

# ---------------------------------------------------------------------------
# legs: crouched and bow-legged - knee bent forward and bowed out, ankle
# pulled back in under the body; thigh thicker than the skinny shin
# ---------------------------------------------------------------------------
HIP_X = 0.40 * H
KNEE_X = 0.46 * H
KNEE_Y = 0.10 * H
ANKLE_X = 0.42 * H
ANKLE_Y = 0.02 * H
KNEE_DROP = 0.62 * H
ANKLE_DROP = 0.60 * H

for side, sign in (("L", 1), ("R", -1)):
    hip_loc = Vector((sign * HIP_X, 0, HIP_Z))
    knee_loc = Vector((sign * KNEE_X, KNEE_Y, HIP_Z - KNEE_DROP))
    ankle_loc = Vector((sign * ANKLE_X, ANKLE_Y, HIP_Z - KNEE_DROP - ANKLE_DROP))

    thigh_dir = knee_loc - hip_loc
    thigh = cone_segment(f"Thigh_{side}", 0.5 * H, 0.38 * H, thigh_dir.length, segments=8)
    thigh.location = hip_loc
    aim(thigh, thigh_dir)

    seam = icosphere(f"KneeSeam_{side}", 0.19 * H, subdiv=1)
    seam.location = knee_loc

    shin_dir = ankle_loc - knee_loc
    shin = cone_segment(f"Shin_{side}", 0.32 * H, 0.22 * H, shin_dir.length, segments=8)
    shin.location = knee_loc
    aim(shin, shin_dir)

    # big splayed foot: long, flares wider at the toes than the heel
    foot_len = 1.0 * H
    foot_toe_w = 0.55 * H
    foot_dir = Vector((0, 1, 0))
    heel = ankle_loc + Vector((0, -0.12 * H, -0.06 * H))
    toe_base = ankle_loc + Vector((0, foot_len * 0.75, -0.08 * H))

    sole, bm = new_obj(f"Foot_{side}")
    heel_ring = [
        bm.verts.new(heel + Vector((-0.16 * H, 0, 0))),
        bm.verts.new(heel + Vector((0.16 * H, 0, 0))),
        bm.verts.new(heel + Vector((-0.12 * H, 0, 0.13 * H))),
        bm.verts.new(heel + Vector((0.12 * H, 0, 0.13 * H))),
    ]
    toe_ring = [
        bm.verts.new(toe_base + Vector((-foot_toe_w * 0.5, 0, 0))),
        bm.verts.new(toe_base + Vector((foot_toe_w * 0.5, 0, 0))),
        bm.verts.new(toe_base + Vector((-foot_toe_w * 0.42, 0, 0.11 * H))),
        bm.verts.new(toe_base + Vector((foot_toe_w * 0.42, 0, 0.11 * H))),
    ]
    bm.faces.new((heel_ring[0], heel_ring[1], toe_ring[1], toe_ring[0]))  # sole
    bm.faces.new((heel_ring[2], toe_ring[2], toe_ring[3], heel_ring[3]))  # top
    bm.faces.new((heel_ring[0], toe_ring[0], toe_ring[2], heel_ring[2]))  # side
    bm.faces.new((heel_ring[1], heel_ring[3], toe_ring[3], toe_ring[1]))  # side
    bm.faces.new((heel_ring[0], heel_ring[2], heel_ring[3], heel_ring[1]))  # heel cap
    finish(sole, bm)

    for i, spread in enumerate([-0.22, -0.07, 0.07, 0.22]):
        toe_start = toe_base + Vector((spread * foot_toe_w, 0.02 * H, 0.03 * H))
        toe_dir = Vector((spread * 0.6, 1.0, -0.15)).normalized()
        build_digit(
            f"Toe_{side}_{i}", toe_start, toe_dir,
            length=0.4 * H, base_r=0.08 * H, tip_r=0.045 * H,
            # negative = curl up: the toes sit at ground level, so they keep
            # the upward hook they had before build_digit's curl sign was
            # fixed to match its docstring
            curl_deg=-22, claw_len=0.1 * H, segments=2,
        )

root["head_center"] = tuple(HEAD_CENTER)
root["head_radius"] = HEAD_R
root["H"] = H

# ---------------------------------------------------------------------------
# shade flat (low-poly faceted look) + join body into one object
# ---------------------------------------------------------------------------
for obj in created:
    for poly in obj.data.polygons:
        poly.use_smooth = False

bpy.ops.object.select_all(action="DESELECT")
for obj in created:
    obj.select_set(True)
bpy.context.view_layer.objects.active = created[0]
bpy.ops.object.join()
body = bpy.context.view_layer.objects.active
body.name = "Goblin1_Body"
body.parent = root

print("BODY_BLOCKOUT_OK", body.name, len(body.data.polygons))

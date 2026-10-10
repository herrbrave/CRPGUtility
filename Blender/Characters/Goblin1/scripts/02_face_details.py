"""Goblin 1 - stage 2: face details (eyes+brows, mouth cavity, teeth).

Requires 01_body_base.py to have already run (reads head center/radius/H
off GoblinRoot's custom properties). Adds separate objects (different
materials come later) for eyes, pupils, brow wedges, mouth cavity, and a
teeth fan - all parented to GoblinRoot but NOT joined into the body mesh.
Re-running this script removes and rebuilds just these face parts so it
is safe to iterate without re-running stage 1.

Rebuilt after an Opus art-direction review: the mouth should be a huge
crescent grin (~75-80% of face width) with two interlocking rows of
teeth and no separate tusks (folded into slightly bigger corner teeth);
eyes should be bigger, closer-set, sunk under angled brow wedges.
"""
import bpy
import bmesh
import math
import mathutils
from mathutils import Vector

FACE_NAMES_PREFIX = "Face_"
for obj in list(bpy.data.objects):
    if obj.name.startswith(FACE_NAMES_PREFIX):
        bpy.data.objects.remove(obj, do_unlink=True)

root = bpy.data.objects["GoblinRoot"]
HEAD_CENTER = Vector(root["head_center"])
HEAD_R = root["head_radius"]
H = root["H"]
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
    for poly in obj.data.polygons:
        poly.use_smooth = False


def icosphere(name, radius, subdiv=1):
    obj, bm = new_obj(name)
    bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=radius)
    finish(obj, bm)
    return obj


def cone_segment(name, r1, r2, depth, segments=6):
    obj, bm = new_obj(name)
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=segments,
                           radius1=r1 / 2, radius2=r2 / 2, depth=depth)
    bmesh.ops.translate(bm, verts=bm.verts, vec=(0, 0, depth / 2))
    finish(obj, bm)
    return obj


def box(name, sx, sy, sz):
    obj, bm = new_obj(name)
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, verts=bm.verts, vec=(sx, sy, sz))
    finish(obj, bm)
    return obj


def aim(obj, direction):
    obj.rotation_euler = Vector(direction).normalized().to_track_quat("Z", "Y").to_euler()


# ---------------------------------------------------------------------------
# eyes: bigger, closer-set, sunk slightly into the brow's shadow, angry
# inward-down slant; each sits under its own angled brow wedge
# ---------------------------------------------------------------------------
EYE_SPACING = 0.225 * H  # half center-to-center distance
EYE_FRONT_Y = 0.150  # absolute meters - must clear the skin surface (~0.146
                      # at this height) with margin, or the eye gets buried
EYE_Z = 0.12 * H

for side, sign in (("L", 1), ("R", -1)):
    sclera = icosphere(f"{FACE_NAMES_PREFIX}Eye_{side}", 0.090 * H, subdiv=1)
    sclera.scale = (1.0, 0.5, 0.65)
    sclera.rotation_euler = (0, 0, math.radians(sign * -16))  # inner corner down
    sclera.location = HEAD_CENTER + Vector((sign * EYE_SPACING, EYE_FRONT_Y, EYE_Z))

    pupil = icosphere(f"{FACE_NAMES_PREFIX}Pupil_{side}", 0.026 * H, subdiv=1)
    pupil.scale = (1.0, 0.55, 0.85)
    pupil.rotation_euler = sclera.rotation_euler
    pupil.location = HEAD_CENTER + Vector((sign * EYE_SPACING, EYE_FRONT_Y + 0.03 * H, EYE_Z - 0.003 * H))

    brow = box(f"{FACE_NAMES_PREFIX}Brow_{side}", 0.15 * H, 0.03 * H, 0.025 * H)
    brow.location = HEAD_CENTER + Vector((sign * EYE_SPACING * 0.95, EYE_FRONT_Y - 0.01, EYE_Z + 0.065 * H))
    brow.rotation_euler = (math.radians(-18), 0, math.radians(sign * -22))

# ---------------------------------------------------------------------------
# grin: a wide upward-curving crescent arc drives both the mouth cavity
# strip and the two interlocking rows of teeth, instead of hand-placed
# per-tooth coordinates
# ---------------------------------------------------------------------------
GRIN_HALF_SPAN = 0.52 * H
GRIN_BASE_Z = -0.18 * H
GRIN_FRONT_Y = 0.188  # absolute meters - clears the skin surface at jaw
                       # height (~0.18 at center, deeper near the corners)


def grin_point(t):
    """t in [-1, 1] across the mouth; corners curve upward (smile)."""
    x = t * GRIN_HALF_SPAN
    z = GRIN_BASE_Z + (t * t) * 0.16 * H
    y = GRIN_FRONT_Y - abs(t) * 0.05 * H
    return HEAD_CENTER + Vector((x, y, z))


# mouth cavity: a thin dark strip lofted along the grin arc, recessed
# slightly into the face
cavity, bm = new_obj(f"{FACE_NAMES_PREFIX}Mouth")
CAVITY_STEPS = 10
outer_ring = []
inner_ring = []
for i in range(CAVITY_STEPS + 1):
    t = -1.0 + 2.0 * i / CAVITY_STEPS
    p = grin_point(t)
    outer_ring.append(bm.verts.new(p))
    inner_ring.append(bm.verts.new(p - Vector((0, 0.045 * H, 0.02 * H))))
for i in range(CAVITY_STEPS):
    a, b = outer_ring[i], outer_ring[i + 1]
    c, d = inner_ring[i], inner_ring[i + 1]
    bm.faces.new((a, b, d, c))
finish(cavity, bm)

# teeth: upper + lower rows along the arc, offset so they interlock,
# corner teeth slightly bigger (standing in for tusks)
teeth_objs = []
N_TEETH = 13
for i in range(N_TEETH):
    t = -0.88 + (1.76 * i / (N_TEETH - 1))
    p = grin_point(t)
    size_boost = 1.0 + 0.6 * max(0.0, abs(t) - 0.55) / 0.33  # bigger near corners
    tooth_len = 0.09 * H * size_boost
    tooth_r = 0.028 * H * size_boost
    t_up = cone_segment(f"{FACE_NAMES_PREFIX}ToothU_{i}", tooth_r * 2, tooth_r * 0.3, tooth_len, segments=4)
    t_up.location = p + Vector((0, -0.01 * H, 0.01 * H))
    aim(t_up, Vector((0, -0.15, -1.0)))
    teeth_objs.append(t_up)

    t_lo = -1.0 + (2.0 * (i + 0.5) / N_TEETH)
    if -1.0 < t_lo < 1.0:
        p_lo = grin_point(t_lo)
        size_boost_lo = 1.0 + 0.6 * max(0.0, abs(t_lo) - 0.55) / 0.33
        tooth_len_lo = 0.075 * H * size_boost_lo
        tooth_r_lo = 0.024 * H * size_boost_lo
        t_dn = cone_segment(f"{FACE_NAMES_PREFIX}ToothL_{i}", tooth_r_lo * 2, tooth_r_lo * 0.3, tooth_len_lo, segments=4)
        t_dn.location = p_lo + Vector((0, -0.015 * H, -0.015 * H))
        aim(t_dn, Vector((0, -0.1, 1.0)))
        teeth_objs.append(t_dn)

bpy.ops.object.select_all(action="DESELECT")
for obj in teeth_objs:
    obj.select_set(True)
bpy.context.view_layer.objects.active = teeth_objs[0]
bpy.ops.object.join()
teeth_joined = bpy.context.view_layer.objects.active
teeth_joined.name = f"{FACE_NAMES_PREFIX}Teeth"

print("FACE_DETAILS_OK")

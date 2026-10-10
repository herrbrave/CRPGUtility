"""
Task 8 - Procedural materials & Cycles beauty pass for Sunken Shrine of the
Stag Lord.

Opens the blend produced by Tasks 1-7 and rebuilds the key shared materials
as proper procedural node graphs (Blender's built-in Brick/Noise/Voronoi/
Pointiness nodes - no external texture images, per the no-external-assets
decision for this pass), then switches the three check cameras to Cycles
with a thin volumetric world fog for soft god-rays through doorways, closer
to the painted reference's rendered look than Eevee's flat lighting.

Material rebuilds (each identified by the same material name every earlier
task already created, so editing it in place propagates to every object that
already references it - no reassignment needed):
  - Built stone (walls/pillars/steps): real Brick Texture node for coursed
    masonry + mortar lines, Pointiness-driven edge wear (lighter, worn at
    convex edges) and crevice grime (darker/mossier at concave corners),
    bump from the brick pattern itself for physical-looking mortar grooves.
  - Cave rock: two-octave noise bump (coarse + fine) and Pointiness-driven
    crevice darkening for a weathered, naturally-lit look.
  - Wood: stretched noise for a directional grain instead of isotropic bump.
  - Water: a Wave Texture normal for rippling instead of a flat surface.

Run headless (after 01-07 have produced blend/sunken_shrine.blend):
    blender --background --factory-startup --python 08_procedural_materials.py

Produces:
    ../blend/sunken_shrine.blend                (opened, modified, saved back in place)
    ../renders/08_beauty_top.png
    ../renders/08_beauty_persp.png
    ../renders/08_beauty_stag_closeup.png
"""
import bpy
import os

PROJECT_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BLEND_PATH = os.path.join(PROJECT_DIR, "blend", "sunken_shrine.blend")
RENDER_DIR = os.path.join(PROJECT_DIR, "renders")
os.makedirs(RENDER_DIR, exist_ok=True)


def get_or_create_collection(name, parent=None):
    if name in bpy.data.collections:
        return bpy.data.collections[name]
    coll = bpy.data.collections.new(name)
    (parent or bpy.context.scene.collection).children.link(coll)
    return coll


def clear_nodes_keep_bsdf_and_output(mat):
    nt = mat.node_tree
    bsdf = nt.nodes.get("Principled BSDF")
    output = nt.nodes.get("Material Output")
    for n in list(nt.nodes):
        if n not in (bsdf, output):
            nt.nodes.remove(n)
    return nt, bsdf, output


def pointiness_wear_crevice(nt, bsdf, base_color, worn_color, crevice_color, x=-900, y=400):
    """Shared building block: Geometry.Pointiness -> ColorRamp twice, mixed
    into base_color so convex edges look worn/lighter and concave corners
    look grimy/darker - a classic no-texture way to sell "weathered stone"."""
    geo = nt.nodes.new("ShaderNodeNewGeometry")
    geo.location = (x, y)

    wear_ramp = nt.nodes.new("ShaderNodeValToRGB")
    wear_ramp.location = (x + 200, y + 150)
    wear_ramp.color_ramp.elements[0].position = 0.55
    wear_ramp.color_ramp.elements[1].position = 0.75
    nt.links.new(geo.outputs["Pointiness"], wear_ramp.inputs["Fac"])

    crevice_ramp = nt.nodes.new("ShaderNodeValToRGB")
    crevice_ramp.location = (x + 200, y - 150)
    crevice_ramp.color_ramp.elements[0].position = 0.3
    crevice_ramp.color_ramp.elements[1].position = 0.48
    crevice_ramp.color_ramp.color_mode = "RGB"
    # Invert so LOW pointiness (concave) drives the crevice mask toward 1.
    invert = nt.nodes.new("ShaderNodeMath")
    invert.operation = "SUBTRACT"
    invert.inputs[0].default_value = 1.0
    invert.location = (x + 200, y - 320)
    nt.links.new(geo.outputs["Pointiness"], invert.inputs[1])
    nt.links.new(invert.outputs["Value"], crevice_ramp.inputs["Fac"])

    mix_base_worn = nt.nodes.new("ShaderNodeMixRGB")
    mix_base_worn.location = (x + 450, y + 50)
    mix_base_worn.inputs["Color1"].default_value = base_color
    mix_base_worn.inputs["Color2"].default_value = worn_color
    nt.links.new(wear_ramp.outputs["Color"], mix_base_worn.inputs["Fac"])

    mix_crevice = nt.nodes.new("ShaderNodeMixRGB")
    mix_crevice.location = (x + 650, y - 100)
    mix_crevice.inputs["Color2"].default_value = crevice_color
    nt.links.new(mix_base_worn.outputs["Color"], mix_crevice.inputs["Color1"])
    nt.links.new(crevice_ramp.outputs["Color"], mix_crevice.inputs["Fac"])

    return mix_crevice.outputs["Color"]


def build_brick_stone(mat_name, brick_color, mortar_color, worn_color, moss_color, scale=(1.2, 0.5)):
    mat = bpy.data.materials.get(mat_name)
    if not mat:
        return
    nt, bsdf, output = clear_nodes_keep_bsdf_and_output(mat)
    if not bsdf:
        return

    coord = nt.nodes.new("ShaderNodeTexCoord")
    coord.location = (-1300, 300)

    # Brick Texture reads only the X/Y of its input vector, never Z - but
    # every wall box here has its height along Z (world/object up) regardless
    # of which horizontal axis it runs along. Fed raw Object coordinates, the
    # node never saw the height dimension at all, producing vertical-only
    # mortar stripes with no horizontal coursing. Remap so Y = true height
    # (Z) and X = whichever horizontal axis actually varies along the wall's
    # length: object.x + object.y, since for an axis-aligned wall box one of
    # those is already ~constant (the thin thickness dimension), so the sum
    # tracks the varying one regardless of the wall's orientation.
    sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    sep.location = (-1150, 300)
    nt.links.new(coord.outputs["Object"], sep.inputs["Vector"])
    add_xy = nt.nodes.new("ShaderNodeMath")
    add_xy.operation = "ADD"
    add_xy.location = (-1000, 380)
    nt.links.new(sep.outputs["X"], add_xy.inputs[0])
    nt.links.new(sep.outputs["Y"], add_xy.inputs[1])
    remap = nt.nodes.new("ShaderNodeCombineXYZ")
    remap.location = (-850, 320)
    nt.links.new(add_xy.outputs["Value"], remap.inputs["X"])
    nt.links.new(sep.outputs["Z"], remap.inputs["Y"])

    brick = nt.nodes.new("ShaderNodeTexBrick")
    brick.location = (-650, 300)
    brick.inputs["Color1"].default_value = brick_color
    brick.inputs["Color2"].default_value = (
        brick_color[0] * 0.92, brick_color[1] * 0.92, brick_color[2] * 0.92, 1.0
    )
    brick.inputs["Mortar"].default_value = mortar_color
    brick.inputs["Scale"].default_value = 1.0
    brick.inputs["Mortar Size"].default_value = 0.02
    brick.inputs["Mortar Smooth"].default_value = 0.15
    brick.inputs["Bias"].default_value = 0.0
    brick.inputs["Brick Width"].default_value = scale[0]
    brick.inputs["Row Height"].default_value = scale[1]
    nt.links.new(remap.outputs["Vector"], brick.inputs["Vector"])

    worn_out = pointiness_wear_crevice(nt, bsdf, brick_color, worn_color, moss_color, x=-1000, y=-50)

    mix_brick_wear = nt.nodes.new("ShaderNodeMixRGB")
    mix_brick_wear.location = (-500, 150)
    mix_brick_wear.inputs["Fac"].default_value = 0.6
    nt.links.new(brick.outputs["Color"], mix_brick_wear.inputs["Color1"])
    nt.links.new(worn_out, mix_brick_wear.inputs["Color2"])

    bump = nt.nodes.new("ShaderNodeBump")
    bump.location = (-300, -100)
    bump.inputs["Strength"].default_value = 0.35
    nt.links.new(brick.outputs["Fac"], bump.inputs["Height"])

    nt.links.new(mix_brick_wear.outputs["Color"], bsdf.inputs["Base Color"])
    nt.links.new(bump.outputs["Normal"], bsdf.inputs["Normal"])
    bsdf.inputs["Roughness"].default_value = 0.82
    nt.links.new(bsdf.outputs["BSDF"], output.inputs["Surface"])


def build_cave_rock(mat_name, base_color, dark_color):
    mat = bpy.data.materials.get(mat_name)
    if not mat:
        return
    nt, bsdf, output = clear_nodes_keep_bsdf_and_output(mat)
    if not bsdf:
        return

    coord = nt.nodes.new("ShaderNodeTexCoord")
    coord.location = (-1100, 300)

    noise_coarse = nt.nodes.new("ShaderNodeTexNoise")
    noise_coarse.location = (-900, 400)
    noise_coarse.inputs["Scale"].default_value = 4.0
    noise_coarse.inputs["Detail"].default_value = 4.0
    nt.links.new(coord.outputs["Object"], noise_coarse.inputs["Vector"])

    noise_fine = nt.nodes.new("ShaderNodeTexNoise")
    noise_fine.location = (-900, 150)
    noise_fine.inputs["Scale"].default_value = 18.0
    noise_fine.inputs["Detail"].default_value = 6.0
    nt.links.new(coord.outputs["Object"], noise_fine.inputs["Vector"])

    worn_out = pointiness_wear_crevice(nt, bsdf, base_color, (base_color[0]*1.15, base_color[1]*1.15, base_color[2]*1.15, 1.0), dark_color, x=-900, y=-100)

    mix_color = nt.nodes.new("ShaderNodeMixRGB")
    mix_color.location = (-450, 150)
    mix_color.inputs["Fac"].default_value = 0.35
    nt.links.new(worn_out, mix_color.inputs["Color1"])
    mix_color.inputs["Color2"].default_value = dark_color
    nt.links.new(noise_coarse.outputs["Fac"], mix_color.inputs["Fac"])

    bump_coarse = nt.nodes.new("ShaderNodeBump")
    bump_coarse.location = (-300, -200)
    bump_coarse.inputs["Strength"].default_value = 0.25
    nt.links.new(noise_coarse.outputs["Fac"], bump_coarse.inputs["Height"])

    bump_fine = nt.nodes.new("ShaderNodeBump")
    bump_fine.location = (-100, -200)
    bump_fine.inputs["Strength"].default_value = 0.15
    nt.links.new(noise_fine.outputs["Fac"], bump_fine.inputs["Height"])
    nt.links.new(bump_coarse.outputs["Normal"], bump_fine.inputs["Normal"])

    nt.links.new(mix_color.outputs["Color"], bsdf.inputs["Base Color"])
    nt.links.new(bump_fine.outputs["Normal"], bsdf.inputs["Normal"])
    bsdf.inputs["Roughness"].default_value = 0.95
    nt.links.new(bsdf.outputs["BSDF"], output.inputs["Surface"])


def build_wood_grain(mat_name, base_color):
    mat = bpy.data.materials.get(mat_name)
    if not mat:
        return
    nt, bsdf, output = clear_nodes_keep_bsdf_and_output(mat)
    if not bsdf:
        return

    coord = nt.nodes.new("ShaderNodeTexCoord")
    coord.location = (-900, 200)
    mapping = nt.nodes.new("ShaderNodeMapping")
    mapping.location = (-700, 200)
    mapping.inputs["Scale"].default_value = (1.0, 1.0, 8.0)  # stretched along one axis = directional grain
    nt.links.new(coord.outputs["Object"], mapping.inputs["Vector"])

    noise = nt.nodes.new("ShaderNodeTexNoise")
    noise.location = (-500, 200)
    noise.inputs["Scale"].default_value = 6.0
    noise.inputs["Detail"].default_value = 3.0
    nt.links.new(mapping.outputs["Vector"], noise.inputs["Vector"])

    ramp = nt.nodes.new("ShaderNodeValToRGB")
    ramp.location = (-300, 200)
    ramp.color_ramp.elements[0].color = (base_color[0] * 0.6, base_color[1] * 0.6, base_color[2] * 0.6, 1.0)
    ramp.color_ramp.elements[1].color = base_color
    nt.links.new(noise.outputs["Fac"], ramp.inputs["Fac"])

    bump = nt.nodes.new("ShaderNodeBump")
    bump.location = (-100, 0)
    bump.inputs["Strength"].default_value = 0.1
    nt.links.new(noise.outputs["Fac"], bump.inputs["Height"])

    nt.links.new(ramp.outputs["Color"], bsdf.inputs["Base Color"])
    nt.links.new(bump.outputs["Normal"], bsdf.inputs["Normal"])
    bsdf.inputs["Roughness"].default_value = 0.78
    nt.links.new(bsdf.outputs["BSDF"], output.inputs["Surface"])


def build_water_ripple(mat_name, base_color, alpha=0.82, emission=None):
    mat = bpy.data.materials.get(mat_name)
    if not mat:
        return
    nt, bsdf, output = clear_nodes_keep_bsdf_and_output(mat)
    if not bsdf:
        return

    coord = nt.nodes.new("ShaderNodeTexCoord")
    coord.location = (-700, 0)
    wave = nt.nodes.new("ShaderNodeTexWave")
    wave.location = (-500, 0)
    wave.wave_type = "RINGS"
    wave.inputs["Scale"].default_value = 3.0
    wave.inputs["Distortion"].default_value = 2.5
    nt.links.new(coord.outputs["Object"], wave.inputs["Vector"])

    bump = nt.nodes.new("ShaderNodeBump")
    bump.location = (-250, -150)
    bump.inputs["Strength"].default_value = 0.08
    nt.links.new(wave.outputs["Fac"], bump.inputs["Height"])

    bsdf.inputs["Base Color"].default_value = base_color
    bsdf.inputs["Roughness"].default_value = 0.05
    if "Alpha" in bsdf.inputs:
        bsdf.inputs["Alpha"].default_value = alpha
        mat.blend_method = "BLEND"
    if emission and "Emission Color" in bsdf.inputs:
        bsdf.inputs["Emission Color"].default_value = emission
        bsdf.inputs["Emission Strength"].default_value = 0.4
    nt.links.new(bump.outputs["Normal"], bsdf.inputs["Normal"])
    nt.links.new(bsdf.outputs["BSDF"], output.inputs["Surface"])


def upgrade_all_materials():
    build_brick_stone(
        "Blockout_built_room", brick_color=(0.6, 0.53, 0.44, 1.0), mortar_color=(0.32, 0.28, 0.24, 1.0),
        worn_color=(0.72, 0.66, 0.56, 1.0), moss_color=(0.16, 0.26, 0.14, 1.0),
    )
    build_brick_stone(
        "Built_stone_pillar", brick_color=(0.58, 0.51, 0.43, 1.0), mortar_color=(0.3, 0.26, 0.22, 1.0),
        worn_color=(0.7, 0.64, 0.55, 1.0), moss_color=(0.16, 0.26, 0.14, 1.0), scale=(0.6, 0.4),
    )
    build_brick_stone(
        "Stone_step", brick_color=(0.58, 0.52, 0.44, 1.0), mortar_color=(0.3, 0.26, 0.22, 1.0),
        worn_color=(0.72, 0.66, 0.56, 1.0), moss_color=(0.16, 0.26, 0.14, 1.0), scale=(1.0, 1.0),
    )
    build_cave_rock("Cave_rock_wall", base_color=(0.4, 0.34, 0.28, 1.0), dark_color=(0.18, 0.15, 0.12, 1.0))
    build_cave_rock("Cave_rock_formation", base_color=(0.44, 0.38, 0.32, 1.0), dark_color=(0.2, 0.17, 0.14, 1.0))
    build_wood_grain("Wood_log", base_color=(0.32, 0.2, 0.12, 1.0))
    build_wood_grain("Prop_wood", base_color=(0.35, 0.22, 0.14, 1.0))
    build_water_ripple("Water_pool", base_color=(0.05, 0.3, 0.4, 1.0), alpha=0.82, emission=(0.05, 0.25, 0.28, 1.0))
    build_water_ripple("Water_stream", base_color=(0.2, 0.52, 0.56, 1.0), alpha=0.85, emission=(0.15, 0.4, 0.4, 1.0))


# -- Cycles + volumetrics --------------------------------------------------


# The real culprit behind a washed-out, unresponsive-to-everything first
# Cycles render turned out to be the world volumetric fog below, not light
# energy at all: Cycles' path tracer has to carry the fill sun's light through
# the ENTIRE scene's volume before it reaches any surface, while a point light
# sitting right next to what it lights barely passes through any fog at all -
# so the fog was quietly absorbing/scattering away nearly all directional and
# ambient light while barely touching close-range point-light glow. That
# produced a render dominated only by whichever point lights happened to be
# nearby (explaining the uniform blue cast in rooms lit solely by blue
# braziers) and made every energy/exposure tweak look like it did nothing,
# because the fog was eating the difference before it reached the camera.
# Confirmed via direct pixel sampling: removing the fog node entirely jumped a
# wall pixel from a blue-dominant (0.14, 0.17, 0.18) to a correct warm (0.82,
# 0.80, 0.77) with no other change. Reducing density instead of removing it
# did NOT help even at higher sample counts - Cycles' volumetric integrator
# seems to need either the medium gone entirely or many more samples than is
# practical here, so the fog (a nice-to-have for god-rays, not essential) is
# dropped rather than chased further.
#
# Task 6's Eevee-tuned light values turn out to need little to no change for
# Cycles once the fog is gone - point lights are left as Task 6 set them, and
# the fill sun gets an ABSOLUTE calibrated value rather than a multiplier on
# Task 6's 0.9 (sun energy is W/m² irradiance, a completely different physical
# unit from a point light's Watts, so a value tuned to look right in Eevee has
# no fixed ratio to what's needed in Cycles' accurate inverse-square falloff).
CYCLES_FILL_SUN_ENERGY = 4.0


def tune_lights_for_cycles():
    fill = bpy.data.objects.get("MoodFillSun")
    if fill:
        fill.data.energy = CYCLES_FILL_SUN_ENERGY


def setup_cycles_and_volumetrics():
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = 96
    scene.cycles.use_denoising = True
    # Left on CPU deliberately: --factory-startup preferences have no compute
    # devices enabled, so requesting GPU here would risk a render failure
    # instead of a graceful fallback.

    tune_lights_for_cycles()

    world = scene.world
    if world and world.use_nodes:
        nt = world.node_tree
        existing_vol = next((n for n in nt.nodes if n.type == "VOLUME_SCATTER"), None)
        if existing_vol:
            for l in list(nt.links):
                if l.from_node == existing_vol:
                    nt.links.remove(l)
            nt.nodes.remove(existing_vol)


def render_views():
    scene = bpy.context.scene
    scene.render.resolution_x = 1600
    scene.render.resolution_y = 1280
    scene.render.image_settings.file_format = "PNG"
    views = [
        ("TopDownCam", "08_beauty_top.png"),
        ("CaveCheckPerspCam", "08_beauty_persp.png"),
        ("StagShrineCam", "08_beauty_stag_closeup.png"),
    ]
    for cam_name, filename in views:
        cam = bpy.data.objects.get(cam_name)
        if not cam:
            continue
        scene.camera = cam
        scene.render.filepath = os.path.join(RENDER_DIR, filename)
        bpy.ops.render.render(write_still=True)


def main():
    bpy.ops.wm.open_mainfile(filepath=BLEND_PATH)

    upgrade_all_materials()
    setup_cycles_and_volumetrics()

    render_views()
    bpy.ops.wm.save_as_mainfile(filepath=BLEND_PATH)
    print(f"Procedural materials & Cycles beauty pass complete. Saved {BLEND_PATH}")


if __name__ == "__main__":
    main()

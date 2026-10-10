"""
Task 6 - Materials & lighting pass for Sunken Shrine of the Stag Lord.

Opens the blend produced by Tasks 1-5 and:
  - Upgrades the shared materials every earlier task created IN PLACE (by
    name, via bpy.data.materials.get) rather than reassigning materials on
    every object - every wall/floor/prop across the whole pipeline already
    references these same named materials, so editing the node tree once
    propagates everywhere automatically. Adds a noise-driven bump for surface
    variation and nudges base colors/roughness toward the reference map's
    warm sandstone-and-firelight palette.
  - Adds a real Light object at every flame/torch/brazier/lantern prop
    (found by the "_flame_" substring every add_fixture() call uses),
    colored from that prop's own emission material rather than guessed, so
    the blue/orange/warm lighting cues the layout data calls for actually
    illuminate the rooms instead of just glowing themselves.
  - Sets a dark moody world background and a dim cool fill light so unlit
    areas read as cave gloom rather than flat black.

Run headless (after 01-05 have produced blend/sunken_shrine.blend):
    blender --background --factory-startup --python 06_materials_lighting.py

Produces:
    ../blend/sunken_shrine.blend              (opened, modified, saved back in place)
    ../renders/06_lighting_top.png
    ../renders/06_lighting_persp.png
    ../renders/06_lighting_stag_closeup.png
"""
import bpy
import math
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


def remove_objects_with_prefixes(prefixes):
    to_remove = [o for o in bpy.data.objects if any(o.name.startswith(p) for p in prefixes)]
    for obj in to_remove:
        bpy.data.objects.remove(obj, do_unlink=True)


# -- material upgrades --------------------------------------------------


def add_bump(mat, scale=9.0, strength=0.12):
    nt = mat.node_tree
    bsdf = nt.nodes.get("Principled BSDF")
    if not bsdf or any(n.type == "BUMP" for n in nt.nodes):
        return
    # Noise Texture's Vector input, left unconnected, defaults to Generated
    # coordinates - normalized 0-1 per OBJECT BOUNDING BOX, so a long thin
    # wall and a small prop get wildly different noise frequency from the
    # same Scale value (this produced a stretched stripe artifact on walls).
    # Object coordinates are unnormalized local space, giving consistent
    # real-world-ish frequency regardless of the object's own dimensions.
    coord = nt.nodes.new("ShaderNodeTexCoord")
    coord.location = (bsdf.location.x - 600, bsdf.location.y - 200)
    noise = nt.nodes.new("ShaderNodeTexNoise")
    noise.inputs["Scale"].default_value = scale
    noise.location = (bsdf.location.x - 400, bsdf.location.y - 200)
    bump = nt.nodes.new("ShaderNodeBump")
    bump.inputs["Strength"].default_value = strength
    bump.location = (bsdf.location.x - 200, bsdf.location.y - 200)
    nt.links.new(coord.outputs["Object"], noise.inputs["Vector"])
    nt.links.new(noise.outputs["Fac"], bump.inputs["Height"])
    nt.links.new(bump.outputs["Normal"], bsdf.inputs["Normal"])


def set_look(name, color=None, roughness=None, bump=False, bump_scale=9.0, bump_strength=0.12):
    mat = bpy.data.materials.get(name)
    if not mat or not mat.use_nodes:
        return
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    if not bsdf:
        return
    if color is not None:
        bsdf.inputs["Base Color"].default_value = color
    if roughness is not None and "Roughness" in bsdf.inputs:
        bsdf.inputs["Roughness"].default_value = roughness
    if bump:
        add_bump(mat, scale=bump_scale, strength=bump_strength)


def upgrade_materials():
    # Built architecture: warm sandstone-ruin tan rather than neutral grey,
    # matching the reference map's painted stone, with subtle surface noise.
    set_look("Blockout_built_room", color=(0.62, 0.55, 0.46, 1.0), roughness=0.78, bump=True, bump_scale=14.0, bump_strength=0.08)
    set_look("Built_stone_pillar", color=(0.6, 0.53, 0.45, 1.0), roughness=0.7, bump=True, bump_scale=18.0, bump_strength=0.08)
    set_look("Stone_step", color=(0.6, 0.54, 0.47, 1.0), roughness=0.75, bump=True, bump_scale=16.0, bump_strength=0.08)

    # Cave rock: warmer earthy brown-grey with coarser bump for a rougher read.
    set_look("Cave_rock_wall", color=(0.4, 0.34, 0.28, 1.0), roughness=0.95, bump=True, bump_scale=6.0, bump_strength=0.2)
    set_look("Cave_rock_formation", color=(0.44, 0.38, 0.32, 1.0), roughness=0.9, bump=True, bump_scale=10.0, bump_strength=0.15)
    set_look("Cave_dirt_floor", color=(0.3, 0.23, 0.15, 1.0), roughness=0.98, bump=True, bump_scale=20.0, bump_strength=0.1)
    set_look("Earthen_step", color=(0.34, 0.26, 0.17, 1.0), roughness=0.95, bump=True, bump_scale=12.0, bump_strength=0.15)

    # Exterior ground: slightly richer green, soft bump for grass texture.
    set_look("Blockout_exterior", color=(0.32, 0.47, 0.26, 1.0), roughness=0.9, bump=True, bump_scale=25.0, bump_strength=0.06)

    # Water: a bit more saturated/glowing to match "glowing blue-green pool".
    set_look("Water_pool", color=(0.05, 0.3, 0.4, 1.0), roughness=0.1)
    set_look("Water_stream", color=(0.2, 0.52, 0.56, 1.0), roughness=0.15)

    # Wood: warmer, slightly less uniform.
    set_look("Wood_log", color=(0.3, 0.19, 0.11, 1.0), roughness=0.82, bump=True, bump_scale=30.0, bump_strength=0.05)
    set_look("Prop_wood", color=(0.33, 0.21, 0.13, 1.0), roughness=0.82)

    # Statue: cool pale stone so it reads distinct from the warm architecture.
    set_look("Stag_statue_stone", color=(0.8, 0.79, 0.76, 1.0), roughness=0.4)

    # The Task-1 visibility-check ground plane pulls focus in the perspective
    # render (a review flagged it) - darken it rather than remove the geometry,
    # since it still catches shadows under the whole site usefully.
    set_look("Blockout_reference_ground", color=(0.03, 0.03, 0.035, 1.0), roughness=0.95)


# -- lighting -------------------------------------------------------------


FLAME_ENERGY = {"brazier": 90.0, "torch": 65.0, "lantern": 45.0}


def light_flames(collection):
    flame_objs = [o for o in bpy.data.objects if "_flame_" in o.name]
    count = 0
    for obj in flame_objs:
        color = (1.0, 0.8, 0.5)
        if obj.data and obj.data.materials:
            mat = obj.data.materials[0]
            if mat and mat.use_nodes:
                bsdf = mat.node_tree.nodes.get("Principled BSDF")
                if bsdf and "Emission Color" in bsdf.inputs:
                    ec = bsdf.inputs["Emission Color"].default_value
                    color = (ec[0], ec[1], ec[2])

        kind = next((k for k in FLAME_ENERGY if k in obj.name), "torch")

        light_data = bpy.data.lights.new(f"Light_{obj.name}", type="POINT")
        light_data.color = color
        # A review caught 300W blowing out to near-white (AgX desaturates
        # hard highlights toward white, and two shrine braziers sit close
        # enough to stack). Tuned down per fixture type, and shadow_soft_size
        # raised substantially - that's a point light's physical radius in
        # Blender, so a bigger value softens the falloff into a wider, gentler
        # pool instead of a harsh hot center with dark bands between fixtures.
        light_data.energy = FLAME_ENERGY[kind]
        light_data.shadow_soft_size = 0.7
        light_obj = bpy.data.objects.new(f"Light_{obj.name}", light_data)
        # World-space position, not obj.location (which is parent-relative) -
        # safe here since no flame is parented, but correct either way.
        light_obj.location = obj.matrix_world.translation
        collection.objects.link(light_obj)
        count += 1
    return count


def set_world_and_fill(collection):
    world = bpy.context.scene.world
    if not world:
        world = bpy.data.worlds.new("SunkenShrineWorld")
        bpy.context.scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    if bg:
        bg.inputs["Color"].default_value = (0.015, 0.02, 0.035, 1.0)
        # Raised further - a review found the perspective render too dark to
        # read as a finished environment once the flame lights were toned
        # down; the dark world color itself still carries the moody feel.
        bg.inputs["Strength"].default_value = 0.7

    fill = bpy.data.objects.get("MoodFillSun")
    if not fill:
        fill_data = bpy.data.lights.new("MoodFillSun", type="SUN")
        fill = bpy.data.objects.new("MoodFillSun", fill_data)
        collection.objects.link(fill)
    fill.data.energy = 0.9
    # Warmed from a cool blue-grey - that, combined with the blue braziers,
    # was cancelling the sandstone base color out of the walls entirely
    # (a review found them reading blue-grey/charcoal instead of warm tan).
    fill.data.color = (0.85, 0.78, 0.68)
    fill.data.angle = math.radians(5)
    fill.rotation_euler = (math.radians(55), 0, math.radians(35))

    # The blockout-stage visibility sun (Task 1) is flat and bright - dim it
    # now that real mood lighting exists, rather than letting it wash it out.
    old_sun = bpy.data.objects.get("SunTop")
    if old_sun:
        old_sun.data.energy = 0.5


def set_render_engine():
    scene = bpy.context.scene
    for candidate in ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE", "BLENDER_EEVEE_LEGACY"):
        try:
            scene.render.engine = candidate
            return candidate
        except TypeError:
            continue
    return scene.render.engine


def render_views():
    scene = bpy.context.scene
    scene.render.resolution_x = 1600
    scene.render.resolution_y = 1280
    scene.render.image_settings.file_format = "PNG"

    views = [
        ("TopDownCam", "06_lighting_top.png"),
        ("CaveCheckPerspCam", "06_lighting_persp.png"),
        ("StagShrineCam", "06_lighting_stag_closeup.png"),
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

    root = get_or_create_collection("SunkenShrine_Blockout")
    light_coll = get_or_create_collection("MoodLighting", root)
    remove_objects_with_prefixes(["Light_prop_", "MoodFillSun"])

    upgrade_materials()
    flame_count = light_flames(light_coll)
    set_world_and_fill(light_coll)

    engine = set_render_engine()
    render_views()
    bpy.ops.wm.save_as_mainfile(filepath=BLEND_PATH)
    print(f"Materials & lighting complete. Engine={engine}. {flame_count} flame lights added. Saved {BLEND_PATH}")


if __name__ == "__main__":
    main()

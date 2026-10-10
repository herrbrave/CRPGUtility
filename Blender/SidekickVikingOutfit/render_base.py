"""Renders the base-01 Sidekick human body parts, each region a different colour, front/back/side, to see where the regions begin and end.
Run: blender --background --python render_base.py -- <output prefix>
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import common
import bpy

meshes, arm = common.import_body()
palette = [(0.8, 0.3, 0.3), (0.3, 0.7, 0.35), (0.35, 0.45, 0.85), (0.9, 0.8, 0.3), (0.7, 0.4, 0.8), (0.3, 0.8, 0.8)]
for i, (region, mesh) in enumerate(meshes.items()):
    common.set_material_colour(mesh, palette[i % len(palette)])
for region, mesh in meshes.items():
    me = mesh.data
    ys = [v.co.y for v in me.vertices]
    print(f"{region}: y {min(ys):.1f}..{max(ys):.1f}")
common.render_views(common.arg(0, "base"))

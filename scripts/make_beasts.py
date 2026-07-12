"""Headless Blender: builds the low-poly beasts the CC0 packs never gave us
(brown bear, giant rat) and exports them as FBX for Unity.

    blender -b -P scripts/make_beasts.py

Everything here is original geometry (primitives welded into a quadruped), so it
carries no third-party licence — see IP-CHECKLIST.md. Style matches the KayKit /
Quaternius kits: chunky, flat-shaded, single-material, ~1 unit = 1 metre.

Blender's character convention is "faces -Y"; the FBX exporter's default axis
conversion turns that into Unity's +Z forward, which is what CharacterVisuals
expects. Renders a side-on preview PNG next to each FBX so the result can be
eyeballed without opening Unity.
"""
import math
import os
import sys

import bpy

OUT_DIR = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
                       "game", "Assets", "Art", "Generated")


def reset_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for block in (bpy.data.meshes, bpy.data.materials, bpy.data.objects):
        for item in list(block):
            if item.users == 0:
                block.remove(item)


def ball(name, loc, scale, segments=16, rings=8):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segments, ring_count=rings,
                                         location=loc)
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = scale
    return obj


def box(name, loc, scale, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_cube_add(location=loc, rotation=rot)
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = scale
    return obj


def cone(name, loc, radius, depth, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_cone_add(vertices=10, radius1=radius, depth=depth,
                                    location=loc, rotation=rot)
    obj = bpy.context.active_object
    obj.name = name
    return obj


def finish(parts, name, colour):
    """Join the parts, flat-shade, apply one material, drop it on the ground."""
    for obj in parts:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    body = bpy.context.active_object
    body.name = name

    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    bpy.ops.object.shade_flat()

    mat = bpy.data.materials.new(name=f"M_{name}")
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (*colour, 1.0)
    bsdf.inputs["Roughness"].default_value = 0.85
    body.data.materials.append(mat)

    # Sit the feet exactly on y=0 so Unity can drop it straight onto the grid.
    lowest = min((body.matrix_world @ v.co).z for v in body.data.vertices)
    body.location.z -= lowest
    bpy.ops.object.transform_apply(location=True, rotation=False, scale=False)
    return body


def build_bear():
    """Brown bear: heavy barrel body, humped shoulders, blunt muzzle, stubby legs."""
    parts = []
    parts.append(ball("torso", (0, 0.15, 0.78), (0.46, 0.78, 0.44)))
    parts.append(ball("hump", (0, -0.30, 1.02), (0.34, 0.30, 0.26)))     # shoulder hump
    parts.append(ball("rump", (0, 0.72, 0.80), (0.40, 0.32, 0.38)))
    parts.append(ball("head", (0, -0.92, 1.02), (0.30, 0.30, 0.27)))
    parts.append(ball("muzzle", (0, -1.22, 0.92), (0.16, 0.19, 0.14)))
    parts.append(ball("nose", (0, -1.40, 0.95), (0.07, 0.06, 0.05)))
    parts.append(ball("ear_l", (-0.20, -0.86, 1.27), (0.09, 0.05, 0.09)))
    parts.append(ball("ear_r", (0.20, -0.86, 1.27), (0.09, 0.05, 0.09)))

    for sx in (-1, 1):
        for fy, tag in ((-0.55, "fore"), (0.62, "hind")):
            parts.append(ball(f"leg_{tag}_{sx}", (sx * 0.32, fy, 0.42),
                              (0.16, 0.18, 0.42)))
            parts.append(ball(f"paw_{tag}_{sx}", (sx * 0.32, fy - 0.04, 0.09),
                              (0.18, 0.22, 0.10)))
    parts.append(ball("tail", (0, 1.00, 0.92), (0.09, 0.09, 0.09)))
    return finish(parts, "Bear", (0.28, 0.17, 0.09))


def build_rat():
    """Giant rat: long low body, pointed snout, big ears, naked rope tail."""
    parts = []
    parts.append(ball("torso", (0, 0.10, 0.34), (0.24, 0.44, 0.22)))
    parts.append(ball("head", (0, -0.44, 0.36), (0.17, 0.19, 0.16)))
    parts.append(cone("snout", (0, -0.68, 0.31), 0.11, 0.30,
                      (math.radians(90), 0, 0)))
    parts.append(ball("ear_l", (-0.14, -0.40, 0.53), (0.11, 0.03, 0.11)))
    parts.append(ball("ear_r", (0.14, -0.40, 0.53), (0.11, 0.03, 0.11)))

    for sx in (-1, 1):
        for fy, tag in ((-0.26, "fore"), (0.32, "hind")):
            parts.append(ball(f"leg_{tag}_{sx}", (sx * 0.18, fy, 0.16),
                              (0.07, 0.08, 0.17)))
    # Tail: a chain of shrinking beads curving up and away.
    for i in range(6):
        t = i / 5.0
        parts.append(ball(f"tail_{i}", (0, 0.56 + t * 0.62, 0.30 + t * 0.10),
                          (0.055 - t * 0.03, 0.075, 0.055 - t * 0.03), segments=8,
                          rings=5))
    return finish(parts, "Rat", (0.34, 0.30, 0.27))


def preview(obj, path):
    """Side-on orthographic render, so the shape can be checked at a glance."""
    scene = bpy.context.scene
    cam_data = bpy.data.cameras.new("PreviewCam")
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = 3.2
    cam = bpy.data.objects.new("PreviewCam", cam_data)
    scene.collection.objects.link(cam)
    cam.location = (4.5, -2.6, 1.4)
    cam.rotation_euler = (math.radians(80), 0, math.radians(60))
    scene.camera = cam

    light_data = bpy.data.lights.new("Sun", type="SUN")
    light_data.energy = 4.0
    light = bpy.data.objects.new("Sun", light_data)
    light.rotation_euler = (math.radians(50), 0, math.radians(30))
    scene.collection.objects.link(light)

    # The EEVEE enum id has been renamed across releases; take whichever this build has.
    for engine in ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE", "BLENDER_WORKBENCH"):
        try:
            scene.render.engine = engine
            break
        except TypeError:
            continue
    scene.render.resolution_x = 480
    scene.render.resolution_y = 360
    scene.render.film_transparent = False
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


def export(obj, name):
    os.makedirs(OUT_DIR, exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    fbx = os.path.join(OUT_DIR, f"{name}.fbx")
    bpy.ops.export_scene.fbx(filepath=fbx, use_selection=True,
                             apply_unit_scale=True, global_scale=1.0,
                             axis_forward="-Z", axis_up="Y",
                             object_types={"MESH"}, mesh_smooth_type="FACE")
    print(f"[beasts] wrote {fbx}")


for builder, name in ((build_bear, "Bear"), (build_rat, "Rat")):
    reset_scene()
    body = builder()
    preview(body, os.path.join(OUT_DIR, f"{name}_preview.png"))
    export(body, name)

print("[beasts] done")

"""
Clodi: clean character export for Unity.

1. keeps only Clodi (rig + body parts), removes the swamp scene
2. fixes the feet: names and parents match their real position
3. creates a new looping walk cycle with real steps (diagonal pairs, lift + swing)
4. UV-unwraps body + tail and bakes the node colors (face, top/belly) into one texture
5. exports a small FBX with only Idle, Walk and Attack

Run:  python clodi_export_fix.py <input.blend> <output_dir>
"""
import bpy, bmesh, math, os, sys
from mathutils import Vector, Matrix

src = sys.argv[-2]
out_dir = sys.argv[-1]
os.makedirs(out_dir, exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=src)
scene = bpy.context.scene

rig = bpy.data.objects["Rig_Clodi"]
CLODI_MESHES = [o for o in bpy.data.objects if o.type == "MESH" and o.name.startswith("Clodi_")]
KEEP = set([rig] + CLODI_MESHES)

# ---------------------------------------------------------------- 1. clean scene
for ob in list(bpy.data.objects):
    if ob not in KEEP:
        bpy.data.objects.remove(ob, do_unlink=True)
for ac in list(bpy.data.actions):
    if ac.name not in ("Clodi_Idle_V2", "Clodi_Attack"):
        bpy.data.actions.remove(ac)
rig.animation_data_clear()
for ob in CLODI_MESHES:
    ob.animation_data_clear()
bpy.ops.outliner.orphans_purge(do_recursive=True)

# ---------------------------------------------------------------- 1b. normals
# Body and tail had their faces pointing INWARDS. Blender shows both sides, Unity only the outside,
# so in Unity the body looked see-through and the feet shone through the back.
for ob in CLODI_MESHES:
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(ob.data)
    bm.free()
    ob.data.update()
print("normals recalculated")

# ---------------------------------------------------------------- 2. fix feet
# Clodi looks along Blender -X, so +Y is his right side.
# Rename feet bones + objects by their real position: V = front (vorn), H = back (hinten), L/R = left/right
bpy.context.view_layer.objects.active = rig
bpy.ops.object.mode_set(mode="EDIT")
eb = rig.data.edit_bones
feet = [b for b in eb if b.name.startswith("fuss_") and not b.name.endswith("_end")]
mapping = {}
for b in feet:
    front = b.head.x < -0.5
    right = b.head.y > 0
    mapping[b.name] = "fuss_" + ("v" if front else "h") + ("r" if right else "l")
    # feet hang on the root: body bob, roll and twist never push them into the ground
    b.parent = eb["root"]
bpy.ops.object.mode_set(mode="OBJECT")

# rename in two steps (avoid name clashes); vertex groups follow bone renames
for old in mapping:
    rig.data.bones[old].name = "tmp_" + old
for old, new in mapping.items():
    rig.data.bones["tmp_" + old].name = new
for ob in CLODI_MESHES:
    for vg in ob.vertex_groups:
        if vg.name.startswith("tmp_"):
            vg.name = mapping[vg.name[4:]]
# rename foot objects to match their bone
for ob in CLODI_MESHES:
    if ob.name.startswith("Clodi_Fuss_") and len(ob.vertex_groups) == 1:
        ob.name = "Clodi_Fuss_" + ob.vertex_groups[0].name.split("_")[1].upper() + "_new"
for ob in CLODI_MESHES:
    if ob.name.endswith("_new"):
        ob.name = ob.name[:-4]
print("feet mapping:", mapping)

# ---------------------------------------------------------------- 3. walk cycle
FRAMES = 16          # one full step cycle (16 frames at 24 fps = 0.67 s)
STRIDE = 0.40        # how far a foot swings forward/back (Blender units)
LIFT = 0.15          # foot lift height
BOB = 0.035          # body up/down
ROLL = 0.05          # body roll (radians)
TAIL = 0.22          # tail sway (radians)

walk = bpy.data.actions.new("Clodi_Walk_Cycle")
rig.animation_data_create()
rig.animation_data.action = walk
bpy.ops.object.mode_set(mode="POSE")
for pb in rig.pose.bones:
    pb.rotation_mode = "XYZ"
    pb.location = (0, 0, 0)
    pb.rotation_euler = (0, 0, 0)

bones = rig.data.bones
def to_local(bone_name, world_vec):
    return bones[bone_name].matrix_local.to_3x3().inverted() @ Vector(world_vec)

FORWARD = Vector((-1, 0, 0))   # Clodi looks along -X
UP = Vector((0, 0, 1))
SIDE = Vector((0, 1, 0))
# diagonal pairs move together: front-left + back-right, then front-right + back-left
phase = {"fuss_vl": 0.0, "fuss_hr": 0.0, "fuss_vr": 0.5, "fuss_hl": 0.5}

def foot_offset(p):
    """p in [0,1): 0..0.5 swing (in the air, moving forward), 0.5..1 stance (on the ground, moving back)"""
    if p < 0.5:
        t = p / 0.5
        fwd = -STRIDE + 2 * STRIDE * (0.5 - 0.5 * math.cos(math.pi * t))
        up = LIFT * math.sin(math.pi * t)
        tilt = -0.25 * math.sin(math.pi * t)   # toes point down while lifting
    else:
        t = (p - 0.5) / 0.5
        fwd = STRIDE - 2 * STRIDE * t
        up = 0.0
        tilt = 0.0
    return fwd, up, tilt

for f in range(FRAMES + 1):
    frame = 1 + f
    u = f / FRAMES
    for name, ph in phase.items():
        pb = rig.pose.bones[name]
        fwd, up, tilt = foot_offset((u + ph) % 1.0)
        pb.location = to_local(name, FORWARD * fwd + UP * up)
        axis_local = to_local(name, SIDE)
        pb.rotation_euler = (Matrix.Rotation(tilt, 3, axis_local)).to_euler("XYZ")
        pb.keyframe_insert("location", frame=frame)
        pb.keyframe_insert("rotation_euler", frame=frame)

    # body bob twice per cycle (every step), small roll towards the lifted side
    root = rig.pose.bones["ctrl_root"]
    root.location = to_local("ctrl_root", UP * (BOB * (0.5 + 0.5 * math.cos(4 * math.pi * u))))
    root.rotation_euler = Matrix.Rotation(ROLL * math.sin(2 * math.pi * u), 3, to_local("ctrl_root", FORWARD)).to_euler("XYZ")
    root.keyframe_insert("location", frame=frame)
    root.keyframe_insert("rotation_euler", frame=frame)

    # front and back body twist against each other a little
    for name, sign in (("koerper_vorn", 1), ("koerper_hinten", -1)):
        pb = rig.pose.bones[name]
        pb.rotation_euler = Matrix.Rotation(sign * 0.06 * math.sin(2 * math.pi * u), 3, to_local(name, UP)).to_euler("XYZ")
        pb.keyframe_insert("rotation_euler", frame=frame)

    # tail swings opposite to the body, a bit delayed
    for name, amount, delay in (("schwanz_basis", TAIL, 0.1), ("schwanz_spitze", TAIL * 0.8, 0.2)):
        pb = rig.pose.bones[name]
        pb.rotation_euler = Matrix.Rotation(-amount * math.sin(2 * math.pi * (u - delay)), 3, to_local(name, UP)).to_euler("XYZ")
        pb.keyframe_insert("rotation_euler", frame=frame)

bpy.ops.object.mode_set(mode="OBJECT")
rig.animation_data.action = None
for pb in rig.pose.bones:
    pb.location = (0, 0, 0); pb.rotation_euler = (0, 0, 0)

# ---------------------------------------------------------------- 4. UV + bake colors
scene.render.engine = "CYCLES"
scene.cycles.device = "CPU"
scene.cycles.samples = 8
scene.render.bake.use_pass_direct = False
scene.render.bake.use_pass_indirect = False
scene.render.bake.margin = 8
rig.data.pose_position = "REST"

def bake_object(ob, size):
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    if not ob.data.uv_layers:
        ob.data.uv_layers.new(name="UVMap")
    ob.data.uv_layers.active = ob.data.uv_layers[0]
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.02)
    bpy.ops.object.mode_set(mode="OBJECT")

    img = bpy.data.images.new(ob.name + "_BaseColor", size, size)
    img.generated_color = (0.5, 0.5, 0.5, 1)
    for mat in ob.data.materials:
        node = mat.node_tree.nodes.new("ShaderNodeTexImage")
        node.image = img
        mat.node_tree.nodes.active = node
    bpy.ops.object.bake(type="DIFFUSE", pass_filter={"COLOR"}, use_clear=True)
    img.filepath_raw = os.path.join(out_dir, img.name + ".png")
    img.file_format = "PNG"
    img.save()
    img.pack()

    # one simple material with the baked texture (Unity understands this)
    new_mat = bpy.data.materials.new(ob.name + "_Mat")
    new_mat.use_nodes = True
    bsdf = new_mat.node_tree.nodes["Principled BSDF"]
    tex = new_mat.node_tree.nodes.new("ShaderNodeTexImage")
    tex.image = img
    new_mat.node_tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.55
    ob.data.materials.clear()
    ob.data.materials.append(new_mat)
    print("baked", ob.name, "->", img.filepath_raw)

bake_object(bpy.data.objects["Clodi_Koerper"], 2048)
bake_object(bpy.data.objects["Clodi_Schwanz"], 1024)
rig.data.pose_position = "POSE"

# ---------------------------------------------------------------- 5. NLA strips + export
rig.animation_data_create()
for strip_name, action_name in (("Idle", "Clodi_Idle_V2"), ("Walk", "Clodi_Walk_Cycle"), ("Attack", "Clodi_Attack")):
    track = rig.animation_data.nla_tracks.new()
    track.name = strip_name
    act = bpy.data.actions[action_name]
    strip = track.strips.new(strip_name, int(act.frame_range[0]), act)
    strip.name = strip_name
for ac in bpy.data.actions:
    ac.use_fake_user = True

bpy.ops.wm.save_as_mainfile(filepath=os.path.join(out_dir, "Clodi_Character_Clean.blend"))

bpy.ops.object.select_all(action="DESELECT")
for ob in KEEP:
    ob.hide_select = False  # some spots were locked for selection in the original file
    ob.hide_set(False)
    ob.select_set(True)
print("exporting", sorted(o.name for o in bpy.context.selected_objects))
bpy.context.view_layer.objects.active = rig
bpy.ops.export_scene.fbx(
    filepath=os.path.join(out_dir, "Clodi_Character.fbx"),
    use_selection=True,
    object_types={"ARMATURE", "MESH"},
    use_mesh_modifiers=True,
    add_leaf_bones=False,
    apply_scale_options="FBX_SCALE_ALL",
    bake_anim=True,
    bake_anim_use_all_actions=False,
    bake_anim_use_nla_strips=True,
    bake_anim_force_startend_keying=True,
    bake_anim_simplify_factor=0.0,
    path_mode="COPY",
    embed_textures=True,
)
print("EXPORT DONE")

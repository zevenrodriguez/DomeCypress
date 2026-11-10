# Exports the cypress dome (scene "HybridSwamp" in DomeCypress/domecypress.blend) to the Unity
# project in DomeCypress/domecypress (Assets/Cypress). Run it from Blender's Scripting tab.
# Then in Unity: Tools > Cypress > Build Cypress Dome Scene.
#
#  - Models/CypressDome.fbx   : trunks, branches, leaf cards, knees, ground, ground cover, logs, sticks,
#                               vines and floating leaves - one object per material group, world placed.
#  - Models/CypressGrass.fbx  : the three sawgrass clump LODs (Grass_L0/L1/L2) used by GrassInstancer.
#  - Textures/cypress_leaf_atlas.png : alpha leaf-card atlas.
#  - cypress_scene.json       : camera, sun, water/murk zone and every marsh-ring grass instance,
#                               already converted to Unity coordinates.
# Blender-only data Unity can't read (procedural colours) is baked into vertex colours ("Col", sRGB),
# matching the conventions of BlenderTools/export_unity.py and the Mangrove shaders.
# Units: meters. Water level is y = 0 in Unity.

import bpy, bmesh, json, math, os, random, shutil
from mathutils import Vector, Matrix, noise
from mathutils.bvhtree import BVHTree

SCENE = "HybridSwamp"
TAG = "_hy"
UNITY_ROOT = os.path.join(os.path.dirname(bpy.data.filepath), "domecypress")
UNITY_DIR = os.path.join(UNITY_ROOT, "Assets", "Cypress")
LEAF_ATLAS_SRC = os.path.join(os.path.dirname(bpy.data.filepath), "textures", "cypress_leaf_atlas.png")
AIRPLANT_KEEP = 0.35          # fraction of Tillandsia leaves kept (the Blender mesh is 1.6M verts)
TILT_DEGREES = 30.0           # dome tilt for the DomeMasterCamera marker (0 = zenith straight up)

globals().update(globals().get("OVERRIDES", {}))
MODEL_DIR = os.path.join(UNITY_DIR, "Models")
TEX_DIR = os.path.join(UNITY_DIR, "Textures")
for d in (MODEL_DIR, TEX_DIR):
    os.makedirs(d, exist_ok=True)

scene = bpy.data.scenes[SCENE]
for w in bpy.context.window_manager.windows:
    w.scene = scene

# Blender (x, y, z) -> Unity (-x, z, -y); matches the FBX export below
C = Matrix(((-1, 0, 0), (0, 0, 1), (0, -1, 0)))


def to_unity(v):
    return [round(-v[0], 4), round(v[2], 4), round(-v[1], 4)]


def quat_unity(m3):
    q = (C @ m3 @ C.transposed()).to_quaternion().normalized()
    return [round(q.x, 7), round(q.y, 7), round(q.z, 7), round(q.w, 7)]


# ---------------------------------------------------------------- export collection helpers
def export_coll():
    c = bpy.data.collections.get("CypressUnityExport")
    if c is None:
        c = bpy.data.collections.new("CypressUnityExport")
        scene.collection.children.link(c)
    return c


def clear_coll(c):
    for ob in list(c.objects):
        me = ob.data
        bpy.data.objects.remove(ob)
        if isinstance(me, bpy.types.Mesh) and me.users == 0:
            bpy.data.meshes.remove(me)


def export_fbx(objs, path):
    for ob in bpy.context.view_layer.objects:
        ob.select_set(False)
    for ob in objs:
        ob.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={'MESH'},
        apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
        axis_forward='-Z', axis_up='Y', bake_space_transform=True,
        use_mesh_modifiers=False, mesh_smooth_type='FACE', use_tspace=False,
        colors_type='SRGB', add_leaf_bones=False, bake_anim=False,
        path_mode='AUTO', embed_textures=False)


ecoll = export_coll()
clear_coll(ecoll)


def copy_obj(src_name, new_name):
    src = bpy.data.objects[src_name]
    me = src.data.copy()
    me.materials.clear()
    ob = bpy.data.objects.new(new_name, me)
    ob.matrix_world = src.matrix_world.copy()
    ecoll.objects.link(ob)
    return ob


# ---------------------------------------------------------------- vertex-colour bakes
def mix(a, b, t):
    return tuple(a[i] + (b[i] - a[i]) * t for i in range(3))


def nz(p, s):
    return noise.noise(Vector((p.x * s, p.y * s, p.z * s))) * 0.5 + 0.5   # ~0..1


def bake_cols(me, fn, domain='POINT'):
    """fn(position, normal, index) -> (r, g, b, a) in linear; written as 'Col' (exported sRGB)."""
    for a in list(me.color_attributes):
        me.color_attributes.remove(a)
    col = me.color_attributes.new("Col", 'BYTE_COLOR', domain)
    flat = []
    for i, v in enumerate(me.vertices):
        flat.extend(fn(v.co, v.normal, i))
    col.data.foreach_set("color", flat)
    return col


def plant_col(lo, hi, base_dark=0.55, scale=0.6, ground=None):
    def f(p, n, i):
        c = mix(lo, hi, min(max(nz(p, scale) * 1.4 - 0.2, 0), 1))
        g = ground(p.x, p.y) if ground else 0.0
        h = min(max((p.z - g) / 0.6, 0.0), 1.0)          # darker toward the base of each blade
        k = base_dark + (1 - base_dark) * h
        return (c[0] * k, c[1] * k, c[2] * k, 1.0)
    return f


def thin_strips(me, keep, verts_per_strip=12):
    """Keep a random fraction of the 12-vertex leaf strips (Tillandsia leaves)."""
    bm = bmesh.new(); bm.from_mesh(me)
    bm.verts.ensure_lookup_table()
    rnd = random.Random(5)
    drop = []
    for s in range(len(bm.verts) // verts_per_strip):
        if rnd.random() > keep:
            drop.extend(bm.verts[s * verts_per_strip:(s + 1) * verts_per_strip])
    bmesh.ops.delete(bm, geom=drop, context='VERTS')
    bm.to_mesh(me); bm.free()


# ground height lookup (ray down onto the peat mesh) for grass base darkening.
# One BVH built up front - Object.ray_cast would rebuild it for every call.
_gme = bpy.data.objects["PeatHummocks" + TAG].data
_bvh = BVHTree.FromPolygons([v.co.copy() for v in _gme.vertices], [tuple(p.vertices) for p in _gme.polygons])
_DOWN = Vector((0, 0, -1))
def ground_h(x, y):
    loc, *_ = _bvh.ray_cast(Vector((x, y, 5.0)), _DOWN)
    return loc.z if loc is not None else 0.0


objs = []
# bark-shaded geometry: no colours needed, the Cypress/Bark shader is procedural (height bands use object y)
for src, dst in (("BaldCypressTrunks", "Cypress_BaldTrunks"), ("PondCypressTrunks", "Cypress_PondTrunks"),
                 ("Branches", "Cypress_Branches"), ("CypressKnees", "Cypress_Knees"),
                 ("FallenLogs", "Cypress_Logs"), ("FloatingSticks", "Cypress_Sticks"),
                 ("HangingVines", "Cypress_Vines")):
    objs.append(copy_obj(src + TAG, dst))

# leaf cards keep their atlas UVs
objs.append(copy_obj("Foliage" + TAG, "Cypress_LeafCards"))

# ground cover -> vertex colours for Mangrove/Foliage
ob = copy_obj("ShortGrass" + TAG, "Cypress_ShortGrass")
bake_cols(ob.data, plant_col((0.08, 0.20, 0.03), (0.26, 0.34, 0.08), 0.45, 0.6, ground_h)); objs.append(ob)
ob = copy_obj("SawgrassRosettes" + TAG, "Cypress_Sawgrass")
bake_cols(ob.data, plant_col((0.14, 0.24, 0.05), (0.42, 0.38, 0.14), 0.45, 0.5, ground_h)); objs.append(ob)
ob = copy_obj("Sawgrass" + TAG, "Cypress_SawgrassTufts")
bake_cols(ob.data, plant_col((0.14, 0.24, 0.05), (0.42, 0.38, 0.14), 0.45, 0.5, ground_h)); objs.append(ob)
ob = copy_obj("Ferns" + TAG, "Cypress_Ferns")
bake_cols(ob.data, plant_col((0.05, 0.16, 0.02), (0.14, 0.30, 0.04), 0.7, 0.8)); objs.append(ob)
ob = copy_obj("AirPlants" + TAG, "Cypress_AirPlants")
thin_strips(ob.data, AIRPLANT_KEEP)
bake_cols(ob.data, plant_col((0.20, 0.24, 0.15), (0.33, 0.31, 0.22), 0.75, 1.5)); objs.append(ob)
ob = copy_obj("FloatingLeaves" + TAG, "Cypress_FloatingLeaves")
bake_cols(ob.data, lambda p, n, i: (*mix((0.10, 0.04, 0.015), (0.40, 0.14, 0.03), nz(p, 30)), 1.0)); objs.append(ob)


# peat hummocks -> Mangrove/Ground: rgb tint, alpha = muck, uv0.x = wetness
def ground_col(p, n, i):
    peat = mix((0.018, 0.013, 0.009), (0.07, 0.045, 0.028), nz(p, 3))
    litter = nz(p, 60)
    c = mix(peat, (0.30, 0.13, 0.04), 0.6) if litter > 0.72 else peat
    moss = nz(p, 0.8)
    if moss > 0.6 and p.z > 0.05:
        c = mix(c, (0.06, 0.10, 0.02), min((moss - 0.6) * 8, 1))
    muck = 0.25 if moss > 0.6 else 0.6
    return (*c, muck)

ob = copy_obj("PeatHummocks" + TAG, "Cypress_Ground")
bake_cols(ob.data, ground_col)
uvl = ob.data.uv_layers.new(name="Data")
for loop in ob.data.loops:
    z = ob.data.vertices[loop.vertex_index].co.z
    uvl.data[loop.index].uv = (1.0 - min(max(z / 0.12, 0.0), 1.0), 0.0)
objs.append(ob)

export_fbx(objs, os.path.join(MODEL_DIR, "CypressDome.fbx"))
stats = {o.name: len(o.data.vertices) for o in objs}
clear_coll(ecoll)

# ---------------------------------------------------------------- grass LOD clumps
lods = []
for i in range(3):
    src = bpy.data.objects[f"LOD{i}_Sawgrass{TAG}"]
    me = src.data.copy(); me.materials.clear()
    ob = bpy.data.objects.new(f"Grass_L{i}", me); ecoll.objects.link(ob)
    bake_cols(me, plant_col((0.09, 0.16, 0.03), (0.38, 0.30, 0.12), 0.5, 2.0,
                            ground=lambda x, y: -0.1))
    lods.append(ob)
export_fbx(lods, os.path.join(MODEL_DIR, "CypressGrass.fbx"))
clear_coll(ecoll)
bpy.data.collections.remove(ecoll)

# ---------------------------------------------------------------- grass instances (full render density)
ring = bpy.data.objects["MarshRing" + TAG]
md = ring.modifiers[0]
vp_id = next(it.identifier for it in md.node_group.interface.items_tree
             if getattr(it, "in_out", None) == 'INPUT' and it.name == "Viewport Density")
vp_sock = getattr(md.properties.inputs, vp_id) if hasattr(md, "properties") else None
old_vp = vp_sock.value if vp_sock else md[vp_id]
if vp_sock: vp_sock.value = 1.0
else: md[vp_id] = 1.0
dg = bpy.context.evaluated_depsgraph_get(); dg.update()
grass = []
for inst in dg.object_instances:
    if inst.is_instance and inst.parent and inst.parent.original.name == ring.name:
        m = inst.matrix_world
        loc, rot, scl = m.decompose()
        grass.extend(to_unity(loc) + quat_unity(rot.to_matrix()) + [round(scl.x, 4)])
if vp_sock: vp_sock.value = old_vp
else: md[vp_id] = old_vp

# ---------------------------------------------------------------- scene data
P_GLOBALS = {}
cam = bpy.data.objects["Camera" + TAG]
fwd = cam.matrix_world.to_3x3() @ Vector((0, 0, -1))
sun = bpy.data.objects["Sun" + TAG]
sun_dir = sun.matrix_world.to_3x3() @ Vector((0, 0, -1))
lod_ids = {it.name: it.identifier for it in md.node_group.interface.items_tree if getattr(it, "in_out", None) == 'INPUT'}
def ring_val(name):
    return getattr(md.properties.inputs, lod_ids[name]).value if hasattr(md, "properties") else md[lod_ids[name]]
center = ring_val("Center")
data = {
    "waterLevel": 0.0,
    "domeCenter": to_unity(Vector((center[0], center[1], 0))),
    "camera": {"position": to_unity(cam.location), "forward": to_unity(fwd),
               "fov": round(math.degrees(cam.data.angle_y), 2), "tiltDegrees": TILT_DEGREES},
    "sun": {"direction": to_unity(sun_dir), "intensity": 2.0},
    "murk": {"center": to_unity(Vector((center[0], center[1], 0))), "radii": [ring_val("Outer Radius") + 10, ring_val("Outer Radius") + 10]},
    "grass": {"lod1Distance": ring_val("LOD1 Distance"), "lod2Distance": ring_val("LOD2 Distance"),
              "stride": 8, "instances": grass},
}
with open(os.path.join(UNITY_DIR, "cypress_scene.json"), "w", encoding="utf-8") as f:
    json.dump(data, f, separators=(",", ":"))

if os.path.exists(LEAF_ATLAS_SRC):
    shutil.copyfile(LEAF_ATLAS_SRC, os.path.join(TEX_DIR, "cypress_leaf_atlas.png"))

result = {"meshes": stats, "grass_instances": len(grass) // 8, "out": UNITY_DIR}
print(result)

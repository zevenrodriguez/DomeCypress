"""
Hybrid cypress swamp for Blender 5.x
A cypress dome ringed by a 20 m band of sawgrass marsh (geometry-nodes grass with 3 LODs).
 - combines the pond-cypress dome look
(pale slender trunks covered in air plants, dark tea water reflecting a blue sky,
sawgrass tufts) with the bald-cypress slough look (big brown buttressed trunks,
cypress knees, drifting duckweed rafts, leafy canopy, sticks and vines).
Builds its own scene "HybridSwamp"; the other two scenes are left untouched.

Original duckweed notes:
Builds a separate scene "DuckweedSwamp" (your pond-cypress scene is left untouched):
duckweed-covered water, big buttressed brown cypress trunks, dense cypress knees,
leafy green canopy and background forest, floating sticks, hanging vines,
soft overcast light and a camera looking down across the water.

Run inside Blender (Scripting tab > Open > Run Script). Re-running rebuilds the scene.
"""
import bpy, math, random, os
from mathutils import Vector, Matrix, Euler

P = dict(
    seed=21,
    n_trunks=138,            # cypress trunks ringing the open centre (+25% over the previous 110)
    dome_radius=36,          # radius of the cypress dome (trees grow inside this circle)
    pond_radius=6,           # open-water pond in the middle that the cypress surround
    marsh_width=20,          # sawgrass ring that surrounds the dome, in metres beyond its edge
    marsh_density=8.0,       # sawgrass clumps per m2
    lod1_distance=22,        # beyond this distance from the camera grass switches to LOD1
    lod2_distance=40,        # ... and to LOD2
    viewport_density=0.3,    # fraction of grass shown in the viewport (renders always use 1.0)
    bald_share=0.35,         # fraction of trunks that are big brown bald cypress (rest pale pond cypress)
    duckweed_cover=0.25,     # 0 = open water, 1 = solid duckweed
    n_hummocks=22,           # extra raised peat islands (besides the ones around trees)
    n_ground_samples=30000,  # ground-cover density on hummocks / waterline
    n_logs=4,                # fallen mossy logs
    n_floating_leaves=6000,  # orange leaf litter on the water
    airplants_per_m=2.2,     # Tillandsia per metre on pond cypress (bald get a third)
    area_x=(-18, 18),
    area_y=(2, 18),
    knees_loose=90,         # knees scattered across the water
    knees_per_trunk=(3, 12),
    leaves_per_cluster=70,   # textured spray cards per foliage cluster
    n_sticks=30,
    n_vines=10,
)
random.seed(P["seed"])
DOME_C = (0.0, 8.0)          # centre of the cypress dome


def in_dome(rad_scale=1.0):
    a_ = random.uniform(0, 2 * math.pi); r_ = P["dome_radius"] * rad_scale * math.sqrt(random.random())
    return DOME_C[0] + r_ * math.cos(a_), DOME_C[1] + r_ * math.sin(a_)

SCENE_NAME = "HybridSwamp"
TAG = "_hy"

# ---------------------------------------------------------------- scene setup
scene = bpy.data.scenes.get(SCENE_NAME) or bpy.data.scenes.new(SCENE_NAME)
for w in bpy.context.window_manager.windows:
    w.scene = scene
if SCENE_NAME in bpy.data.collections:
    c = bpy.data.collections[SCENE_NAME]
    for o in list(c.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    bpy.data.collections.remove(c)
for m in list(bpy.data.meshes):
    if m.users == 0:
        bpy.data.meshes.remove(m)
coll = bpy.data.collections.new(SCENE_NAME)
scene.collection.children.link(coll)


def mesh_obj(name, verts, faces, mat=None, smooth=True, uvs=None):
    me = bpy.data.meshes.new(name + TAG)
    me.from_pydata(verts, [], faces)
    me.update()
    if uvs:                                   # one (u, v) per face corner, in face order
        uvl = me.uv_layers.new(name="UVMap")
        uvl.data.foreach_set("uv", [c for uv in uvs for c in uv])
    if smooth:
        me.shade_smooth()
    ob = bpy.data.objects.new(name + TAG, me)
    coll.objects.link(ob)
    if mat:
        me.materials.append(mat)
    return ob

# ---------------------------------------------------------------- materials
def new_mat(name):
    name += TAG
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial"); out.location = (600, 0)
    return m, nt, out


def mat_bark():
    m, nt, out = new_mat("BaldCypressBark")
    N, L = nt.nodes, nt.links
    bsdf = N.new("ShaderNodeBsdfPrincipled"); bsdf.location = (300, 0)
    tc = N.new("ShaderNodeTexCoord")
    mp = N.new("ShaderNodeMapping"); mp.inputs["Scale"].default_value = (2.5, 2.5, 0.25)
    L.new(tc.outputs["Object"], mp.inputs["Vector"])
    wave = N.new("ShaderNodeTexWave"); wave.wave_type = 'BANDS'; wave.bands_direction = 'X'
    wave.inputs["Scale"].default_value = 1.6
    wave.inputs["Distortion"].default_value = 10
    wave.inputs["Detail"].default_value = 8
    noise = N.new("ShaderNodeTexNoise")
    noise.inputs["Scale"].default_value = 4; noise.inputs["Detail"].default_value = 10
    L.new(mp.outputs["Vector"], wave.inputs["Vector"]); L.new(mp.outputs["Vector"], noise.inputs["Vector"])
    mix = N.new("ShaderNodeMix"); mix.data_type = 'FLOAT'; mix.inputs[0].default_value = 0.45
    L.new(wave.outputs["Fac"], mix.inputs[2]); L.new(noise.outputs["Fac"], mix.inputs[3])
    ramp = N.new("ShaderNodeValToRGB")
    e = ramp.color_ramp.elements
    e[0].position = 0.3; e[0].color = (0.035, 0.025, 0.018, 1)   # deep fissures
    e[1].position = 0.75; e[1].color = (0.20, 0.16, 0.12, 1)     # reddish-grey ridges
    L.new(mix.outputs[0], ramp.inputs["Fac"])
    # greenish algae/moss stain at the base (world Z)
    geo = N.new("ShaderNodeNewGeometry"); sep = N.new("ShaderNodeSeparateXYZ")
    L.new(geo.outputs["Position"], sep.inputs[0])
    mr = N.new("ShaderNodeMapRange")
    mr.inputs["From Min"].default_value = 0.0; mr.inputs["From Max"].default_value = 1.5
    L.new(sep.outputs["Z"], mr.inputs["Value"])
    st = N.new("ShaderNodeMix"); st.data_type = 'RGBA'; st.blend_type = 'MULTIPLY'
    L.new(mr.outputs[0], st.inputs[0])
    L.new(ramp.outputs["Color"], st.inputs[6])
    st.inputs[7].default_value = (1, 1, 1, 1)
    # factor 1 (high) -> multiply by white (no change); low -> mix toward A only, so add tint:
    tint = N.new("ShaderNodeMix"); tint.data_type = 'RGBA'
    L.new(mr.outputs[0], tint.inputs[0])
    tint.inputs[6].default_value = (0.06, 0.07, 0.03, 1)
    L.new(ramp.outputs["Color"], tint.inputs[7])
    L.new(tint.outputs[2], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.9
    bump = N.new("ShaderNodeBump"); bump.inputs["Strength"].default_value = 0.9
    L.new(mix.outputs[0], bump.inputs["Height"]); L.new(bump.outputs["Normal"], bsdf.inputs["Normal"])
    L.new(bsdf.outputs[0], out.inputs[0])
    return m


def mat_duckweed():
    m, nt, out = new_mat("Duckweed")
    N, L = nt.nodes, nt.links
    tc = N.new("ShaderNodeTexCoord")
    # individual fronds
    vor = N.new("ShaderNodeTexVoronoi"); vor.inputs["Scale"].default_value = 260
    L.new(tc.outputs["Object"], vor.inputs["Vector"])
    fr = N.new("ShaderNodeValToRGB")
    fr.color_ramp.elements[0].color = (0.06, 0.20, 0.012, 1)
    fr.color_ramp.elements[1].color = (0.16, 0.40, 0.025, 1)
    L.new(vor.outputs["Color"], fr.inputs["Fac"])
    # large-scale tonal variation
    big = N.new("ShaderNodeTexNoise"); big.inputs["Scale"].default_value = 0.25
    big.inputs["Detail"].default_value = 6
    L.new(tc.outputs["Object"], big.inputs["Vector"])
    tone = N.new("ShaderNodeMix"); tone.data_type = 'RGBA'; tone.blend_type = 'MULTIPLY'
    L.new(big.outputs["Fac"], tone.inputs[0])
    L.new(fr.outputs["Color"], tone.inputs[6])
    tone.inputs[7].default_value = (0.6, 0.75, 0.45, 1)
    dw = N.new("ShaderNodeBsdfPrincipled")
    dw.inputs["Roughness"].default_value = 0.45
    L.new(tone.outputs[2], dw.inputs["Base Color"])
    bump = N.new("ShaderNodeBump"); bump.inputs["Strength"].default_value = 0.25
    L.new(vor.outputs["Distance"], bump.inputs["Height"]); L.new(bump.outputs["Normal"], dw.inputs["Normal"])
    # occasional open-water gaps / tracks (dark, reflective)
    gap_n = N.new("ShaderNodeTexNoise"); gap_n.inputs["Scale"].default_value = 0.8
    gap_n.inputs["Detail"].default_value = 15
    gap_n.inputs["Roughness"].default_value = 0.78     # ragged, broken raft edges
    gap_n.inputs["Distortion"].default_value = 0.4
    L.new(tc.outputs["Object"], gap_n.inputs["Vector"])
    gap = N.new("ShaderNodeValToRGB")
    thr = 0.32 + 0.36 * P["duckweed_cover"]
    gap.color_ramp.elements[0].position = thr - 0.03; gap.color_ramp.elements[0].color = (0, 0, 0, 1)
    gap.color_ramp.elements[1].position = thr + 0.03; gap.color_ramp.elements[1].color = (1, 1, 1, 1)
    L.new(gap_n.outputs["Fac"], gap.inputs["Fac"])
    water = N.new("ShaderNodeBsdfPrincipled")
    water.inputs["Base Color"].default_value = (0.025, 0.009, 0.003, 1)   # tea / tannin stained
    water.inputs["Roughness"].default_value = 0.02
    water.inputs["IOR"].default_value = 1.33
    rip = N.new("ShaderNodeTexNoise"); rip.inputs["Scale"].default_value = 25
    L.new(tc.outputs["Object"], rip.inputs["Vector"])
    rb = N.new("ShaderNodeBump"); rb.inputs["Strength"].default_value = 0.02
    L.new(rip.outputs["Fac"], rb.inputs["Height"]); L.new(rb.outputs["Normal"], water.inputs["Normal"])
    ms = N.new("ShaderNodeMixShader")
    L.new(gap.outputs["Color"], ms.inputs[0])
    L.new(dw.outputs[0], ms.inputs[1]); L.new(water.outputs[0], ms.inputs[2])
    L.new(ms.outputs[0], out.inputs[0])
    return m


def mat_simple(name, color, rough=0.6, translucent=0.0, var=None, var_scale=1.5):
    m, nt, out = new_mat(name)
    N, L = nt.nodes, nt.links
    bsdf = N.new("ShaderNodeBsdfPrincipled")
    bsdf.inputs["Base Color"].default_value = (*color, 1)
    bsdf.inputs["Roughness"].default_value = rough
    col_out = None
    if var:
        tc = N.new("ShaderNodeTexCoord")
        nz = N.new("ShaderNodeTexNoise"); nz.inputs["Scale"].default_value = var_scale
        L.new(tc.outputs["Object"], nz.inputs["Vector"])
        rp = N.new("ShaderNodeValToRGB")
        rp.color_ramp.elements[0].color = (*var[0], 1)
        rp.color_ramp.elements[1].color = (*var[1], 1)
        L.new(nz.outputs["Fac"], rp.inputs["Fac"])
        L.new(rp.outputs["Color"], bsdf.inputs["Base Color"])
        col_out = rp.outputs["Color"]
    if translucent > 0:
        tr = N.new("ShaderNodeBsdfTranslucent")
        tr.inputs["Color"].default_value = (*color, 1)
        if col_out:
            L.new(col_out, tr.inputs["Color"])
        mx = N.new("ShaderNodeMixShader"); mx.inputs[0].default_value = translucent
        L.new(bsdf.outputs[0], mx.inputs[1]); L.new(tr.outputs[0], mx.inputs[2])
        L.new(mx.outputs[0], out.inputs[0])
    else:
        L.new(bsdf.outputs[0], out.inputs[0])
    return m


M_BARK = mat_bark()
M_DUCK = mat_duckweed()
M_KNEE = mat_simple("Knee", (0.10, 0.08, 0.06), 0.9, var=((0.05, 0.04, 0.03), (0.18, 0.15, 0.11)), var_scale=6)
def mat_leaf_cards():
    """Alpha-clipped leaf-card material using the cypress spray atlas."""
    blend_dir = os.path.dirname(bpy.data.filepath) or os.getcwd()
    img_path = os.path.join(blend_dir, "textures", "cypress_leaf_atlas.png")
    img = bpy.data.images.load(img_path, check_existing=True)
    if not img.packed_file:
        img.pack()                                    # keep the .blend self-contained
    m, nt, out = new_mat("CypressLeafCards")
    N, L = nt.nodes, nt.links
    uv = N.new("ShaderNodeUVMap"); uv.uv_map = "UVMap"
    tex = N.new("ShaderNodeTexImage"); tex.image = img; tex.interpolation = 'Linear'
    L.new(uv.outputs["UV"], tex.inputs["Vector"])
    # per-cluster colour variation (seasonal / sun vs shade)
    geo = N.new("ShaderNodeNewGeometry")
    nz = N.new("ShaderNodeTexNoise"); nz.inputs["Scale"].default_value = 0.35
    L.new(geo.outputs["Position"], nz.inputs["Vector"])
    tint = N.new("ShaderNodeValToRGB")
    tint.color_ramp.elements[0].color = (0.75, 0.85, 0.65, 1)
    tint.color_ramp.elements[1].color = (1.15, 1.08, 0.85, 1)
    L.new(nz.outputs["Fac"], tint.inputs["Fac"])
    col = _mixc_simple(N, L, tex.outputs["Color"], tint.outputs["Color"])
    bsdf = N.new("ShaderNodeBsdfPrincipled")
    bsdf.inputs["Roughness"].default_value = 0.55
    L.new(col, bsdf.inputs["Base Color"])
    tr = N.new("ShaderNodeBsdfTranslucent"); L.new(col, tr.inputs["Color"])
    leaf = N.new("ShaderNodeMixShader"); leaf.inputs[0].default_value = 0.35
    L.new(bsdf.outputs[0], leaf.inputs[1]); L.new(tr.outputs[0], leaf.inputs[2])
    # alpha clip (cheaper and sharper than blended transparency)
    clip = N.new("ShaderNodeMath"); clip.operation = 'GREATER_THAN'; clip.inputs[1].default_value = 0.5
    L.new(tex.outputs["Alpha"], clip.inputs[0])
    transp = N.new("ShaderNodeBsdfTransparent")
    mx = N.new("ShaderNodeMixShader")
    L.new(clip.outputs[0], mx.inputs[0]); L.new(transp.outputs[0], mx.inputs[1]); L.new(leaf.outputs[0], mx.inputs[2])
    L.new(mx.outputs[0], out.inputs[0])
    m.use_backface_culling = False
    for attr, val in (("surface_render_method", 'DITHERED'), ("use_transparent_shadow", True),
                      ("blend_method", 'CLIP'), ("shadow_method", 'CLIP')):
        try:
            setattr(m, attr, val)
        except Exception:
            pass
    return m


def _mixc_simple(N, L, a, b):
    mm = N.new("ShaderNodeMix"); mm.data_type = 'RGBA'; mm.blend_type = 'MULTIPLY'
    mm.inputs[0].default_value = 1.0
    L.new(a, mm.inputs[6]); L.new(b, mm.inputs[7])
    return mm.outputs[2]


M_LEAF = mat_leaf_cards()
M_STICK = mat_simple("Stick", (0.15, 0.12, 0.09), 0.85, var=((0.08, 0.06, 0.04), (0.30, 0.26, 0.20)))
M_VINE = mat_simple("Vine", (0.12, 0.10, 0.07), 0.8)

# ---------------------------------------------------------------- geometry helpers
def trunk_radius(z, p, ang):
    t = max(z, 0) / p["h"]
    r = p["r"] * (1 - 0.55 * t ** 0.8) + 0.01
    flare = p["flare"] * math.exp(-max(z, -0.3) / p["flare_h"])
    buttress = math.exp(-max(z, 0) / (p["flare_h"] * 0.9))
    flute = 1 + 0.35 * buttress * (math.sin(p["k"] * ang + p["ph"]) * 0.65
                                   + math.sin((p["k"] + 3) * ang + 1.7 * p["ph"]) * 0.35)
    bark = 1 + 0.025 * math.sin(23 * ang + z * 1.3)
    return (r + flare) * flute * bark


def trunk_offset(z, p):
    z = max(z, 0.0)
    lx = p["lean"][0] * (z / p["h"]) ** 1.5 * p["h"] + 0.08 * math.sin(z * 0.5 + p["ph"])
    ly = p["lean"][1] * (z / p["h"]) ** 1.5 * p["h"] + 0.08 * math.cos(z * 0.4 + p["ph"])
    return lx, ly


def build_trunk(V, F, pos, p, seg=24, rings=44):
    base = len(V); z0 = -0.5
    for i in range(rings + 1):
        t = i / rings
        z = z0 + (p["h"] - z0) * t ** 1.9
        ox, oy = trunk_offset(z, p)
        for j in range(seg):
            a = 2 * math.pi * j / seg
            r = trunk_radius(z, p, a)
            V.append((pos[0] + ox + r * math.cos(a), pos[1] + oy + r * math.sin(a), z))
    for i in range(rings):
        for j in range(seg):
            a = base + i * seg + j; b = base + i * seg + (j + 1) % seg
            F.append((a, b, b + seg, a + seg))
    top = len(V); ox, oy = trunk_offset(p["h"], p)
    V.append((pos[0] + ox, pos[1] + oy, p["h"] + 0.05))
    for j in range(seg):
        F.append((base + rings * seg + j, base + rings * seg + (j + 1) % seg, top))


def tube(V, F, pts, r0, r1, seg=5):
    """Tube along a list of Vector points, radius r0 -> r1."""
    base = len(V); n = len(pts)
    for i, c in enumerate(pts):
        d = (pts[min(i + 1, n - 1)] - pts[max(i - 1, 0)]).normalized()
        up = Vector((0, 0, 1)) if abs(d.z) < 0.95 else Vector((1, 0, 0))
        u = d.cross(up).normalized(); w = d.cross(u).normalized()
        r = r0 + (r1 - r0) * i / (n - 1)
        for j in range(seg):
            a = 2 * math.pi * j / seg
            V.append(tuple(c + (u * math.cos(a) + w * math.sin(a)) * r))
    for i in range(n - 1):
        for j in range(seg):
            a = base + i * seg + j; b = base + i * seg + (j + 1) % seg
            F.append((a, b, b + seg, a + seg))


def branch_pts(start, d, length, droop=0.15, steps=6):
    d = d.normalized()
    return [start + d * length * (i / steps) + Vector((0, 0, -droop * length * (i / steps) ** 2))
            for i in range(steps + 1)]


LEAF_UV = []          # UVs for the foliage mesh (filled by leaf_cluster)
# atlas cells: 0 light bald-cypress spray, 1 mid, 2 dark/shade, 3 pond-cypress branchlets
_CELL_RECT = {0: (0.0, 0.5), 1: (0.5, 0.5), 2: (0.0, 0.0), 3: (0.5, 0.0)}   # (u0, v0) of each 0.5x0.5 cell


def leaf_cluster(V, F, center, radius, n, flat=0.6, cells=(0, 1, 2), size=(0.6, 1.2)):
    """Cloud of alpha-textured spray cards. Each card hangs from its attach point, roughly facing
    outward from the cluster centre, so the silhouette reads as drooping feathery cypress sprays."""
    for _ in range(n):
        o = Vector((random.gauss(0, 1), random.gauss(0, 1), random.gauss(0, flat))).normalized()
        c = center + o * radius * random.random() ** 0.5
        # card 'up' axis: outward + downward droop; random roll around it
        up = (o + Vector((0, 0, random.uniform(-0.9, 0.2)))).normalized()
        side = up.cross(Vector((random.gauss(0, 1), random.gauss(0, 1), random.gauss(0, 1)))).normalized()
        if side.length < 1e-4:
            side = Vector((1, 0, 0))
        h = random.uniform(*size); w = h * 0.5
        b = len(V)
        for (sx, sy) in ((-w, 0), (w, 0), (w, h), (-w, h)):
            V.append(tuple(c + side * sx + up * sy))
        F.append((b, b + 1, b + 2, b + 3))
        u0, v0 = _CELL_RECT[random.choice(cells)]
        flip = random.random() < 0.5          # mirror half the cards for more variety
        us = (u0 + 0.5, u0) if flip else (u0, u0 + 0.5)
        LEAF_UV.extend([(us[0], v0), (us[1], v0), (us[1], v0 + 0.5), (us[0], v0 + 0.5)])


def knee(V, F, kx, ky, kh, kr):
    """Knobby conical cypress knee."""
    seg = 10; b = len(V)
    prof = [(-0.3, 1.25), (0.0, 1.0), (kh * 0.35, 0.85), (kh * 0.7, 0.6), (kh * 0.92, 0.45), (kh, 0.32)]
    ph = random.uniform(0, 6.3); lean = (random.gauss(0, 0.05), random.gauss(0, 0.05))
    for i, (zz, rf) in enumerate(prof):
        for j in range(seg):
            a = 2 * math.pi * j / seg
            jit = 1 + 0.18 * math.sin(3 * a + ph + i) + 0.08 * math.sin(7 * a + i * 2)
            lx, ly = lean[0] * max(zz, 0), lean[1] * max(zz, 0)
            V.append((kx + lx + kr * rf * jit * math.cos(a), ky + ly + kr * rf * jit * math.sin(a), zz))
    for i in range(len(prof) - 1):
        for j in range(seg):
            q = b + i * seg + j; w = b + i * seg + (j + 1) % seg
            F.append((q, w, w + seg, q + seg))
    top = len(V); V.append((kx + lean[0] * kh, ky + lean[1] * kh, kh + kr * 0.15))
    k = len(prof) - 1
    for j in range(seg):
        F.append((b + k * seg + j, b + k * seg + (j + 1) % seg, top))


# ---------------------------------------------------------------- extra materials (pond-cypress look)
def mat_pale_bark():
    m, nt, out = new_mat("PondCypressBark")
    N, L = nt.nodes, nt.links
    bsdf = N.new("ShaderNodeBsdfPrincipled")
    tc = N.new("ShaderNodeTexCoord")
    mp = N.new("ShaderNodeMapping"); mp.inputs["Scale"].default_value = (5, 5, 0.9)
    L.new(tc.outputs["Object"], mp.inputs["Vector"])
    noise = N.new("ShaderNodeTexNoise"); noise.inputs["Scale"].default_value = 3
    noise.inputs["Detail"].default_value = 10; noise.inputs["Roughness"].default_value = 0.65
    wave = N.new("ShaderNodeTexWave"); wave.wave_type = 'BANDS'; wave.bands_direction = 'X'
    wave.inputs["Scale"].default_value = 2; wave.inputs["Distortion"].default_value = 8
    L.new(mp.outputs["Vector"], noise.inputs["Vector"]); L.new(mp.outputs["Vector"], wave.inputs["Vector"])
    mix = N.new("ShaderNodeMix"); mix.data_type = 'FLOAT'; mix.inputs[0].default_value = 0.1
    L.new(noise.outputs["Fac"], mix.inputs[2]); L.new(wave.outputs["Fac"], mix.inputs[3])
    ramp = N.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].position = 0.3; ramp.color_ramp.elements[0].color = (0.07, 0.055, 0.04, 1)
    ramp.color_ramp.elements[1].position = 0.62; ramp.color_ramp.elements[1].color = (0.48, 0.47, 0.44, 1)
    L.new(mix.outputs[0], ramp.inputs["Fac"])
    geo = N.new("ShaderNodeNewGeometry"); sep = N.new("ShaderNodeSeparateXYZ")
    L.new(geo.outputs["Position"], sep.inputs[0])
    mr = N.new("ShaderNodeMapRange")
    mr.inputs["From Min"].default_value = 0.6; mr.inputs["From Max"].default_value = 2.2
    L.new(sep.outputs["Z"], mr.inputs["Value"])
    st = N.new("ShaderNodeMix"); st.data_type = 'RGBA'
    L.new(mr.outputs[0], st.inputs[0])
    st.inputs[6].default_value = (0.06, 0.045, 0.03, 1)
    L.new(ramp.outputs["Color"], st.inputs[7])
    L.new(st.outputs[2], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.85
    bump = N.new("ShaderNodeBump"); bump.inputs["Strength"].default_value = 0.6
    L.new(mix.outputs[0], bump.inputs["Height"]); L.new(bump.outputs["Normal"], bsdf.inputs["Normal"])
    L.new(bsdf.outputs[0], out.inputs[0])
    return m


M_PALE = mat_pale_bark()
M_TILL = mat_simple("Tillandsia", (0.22, 0.24, 0.16), 0.5, translucent=0.2,
                    var=((0.20, 0.24, 0.15), (0.33, 0.31, 0.22)))
M_GRASS = mat_simple("Sawgrass", (0.15, 0.25, 0.06), 0.5, translucent=0.3,
                     var=((0.12, 0.24, 0.05), (0.32, 0.33, 0.12)))


# ================================================================ REALISM PASS: layered bark materials
# Based on the Everglades references: pale grey bark with cinnamon-red patches showing through,
# vertical fibrous plates with dark cracks, a mossy green band above the waterline,
# a near-black wet band at the waterline, and moss on upward-facing surfaces (branches/logs).
from mathutils import noise as mnoise


def _mixc(N, L, fac, a, b, blend='MIX'):
    m = N.new("ShaderNodeMix"); m.data_type = 'RGBA'; m.blend_type = blend
    for idx, v in ((0, fac), (6, a), (7, b)):
        if isinstance(v, (int, float)):
            m.inputs[idx].default_value = v
        elif isinstance(v, tuple):
            m.inputs[idx].default_value = (*v, 1) if len(v) == 3 else v
        else:
            L.new(v, m.inputs[idx])
    return m.outputs[2]


def mat_cypress_bark(name, pale=0.55, moss_top=0.6, band_scale=1.0):
    m, nt, out = new_mat(name)
    N, L = nt.nodes, nt.links
    bsdf = N.new("ShaderNodeBsdfPrincipled"); bsdf.location = (400, 0)
    tc = N.new("ShaderNodeTexCoord")
    geo = N.new("ShaderNodeNewGeometry")
    # stretched coordinates -> long vertical bark fibres/plates
    mp = N.new("ShaderNodeMapping"); mp.inputs["Scale"].default_value = (3.5, 3.5, 0.35)
    L.new(tc.outputs["Object"], mp.inputs["Vector"])
    plates = N.new("ShaderNodeTexVoronoi"); plates.feature = 'DISTANCE_TO_EDGE'
    plates.inputs["Scale"].default_value = 5.0
    L.new(mp.outputs["Vector"], plates.inputs["Vector"])
    fib = N.new("ShaderNodeTexNoise"); fib.inputs["Scale"].default_value = 9
    fib.inputs["Detail"].default_value = 14; fib.inputs["Roughness"].default_value = 0.7
    L.new(mp.outputs["Vector"], fib.inputs["Vector"])
    patch = N.new("ShaderNodeTexNoise"); patch.inputs["Scale"].default_value = 1.6
    patch.inputs["Detail"].default_value = 6; patch.inputs["Roughness"].default_value = 0.6
    L.new(mp.outputs["Vector"], patch.inputs["Vector"])
    # pale grey vs cinnamon-red inner bark
    pr = N.new("ShaderNodeValToRGB")
    pr.color_ramp.elements[0].position = 1 - pale - 0.04
    pr.color_ramp.elements[0].color = (0.17, 0.085, 0.05, 1)     # cinnamon
    pr.color_ramp.elements[1].position = 1 - pale + 0.04
    pr.color_ramp.elements[1].color = (0.28, 0.25, 0.21, 1)     # weathered grey
    L.new(patch.outputs["Fac"], pr.inputs["Fac"])
    fib_tint = _mixc(N, L, 0.25, pr.outputs["Color"], fib.outputs["Color"], 'MULTIPLY')
    crack = N.new("ShaderNodeMapRange")
    crack.inputs["From Min"].default_value = 0.0; crack.inputs["From Max"].default_value = 0.035
    L.new(plates.outputs["Distance"], crack.inputs["Value"])
    upper = _mixc(N, L, crack.outputs[0], (0.09, 0.07, 0.05), fib_tint)
    # noisy height bands
    sep = N.new("ShaderNodeSeparateXYZ"); L.new(geo.outputs["Position"], sep.inputs[0])
    bn = N.new("ShaderNodeTexNoise"); bn.inputs["Scale"].default_value = 2.5
    L.new(tc.outputs["Object"], bn.inputs["Vector"])
    zn = N.new("ShaderNodeMath"); zn.operation = 'MULTIPLY_ADD'
    L.new(bn.outputs["Fac"], zn.inputs[0]); zn.inputs[1].default_value = 0.6 * band_scale
    L.new(sep.outputs["Z"], zn.inputs[2])
    def band(lo, hi):
        r = N.new("ShaderNodeMapRange")
        r.inputs["From Min"].default_value = lo * band_scale
        r.inputs["From Max"].default_value = hi * band_scale
        L.new(zn.outputs[0], r.inputs["Value"])
        return r.outputs[0]
    moss_n = N.new("ShaderNodeTexNoise"); moss_n.inputs["Scale"].default_value = 6
    moss_n.inputs["Detail"].default_value = 10
    L.new(tc.outputs["Object"], moss_n.inputs["Vector"])
    moss_col = N.new("ShaderNodeValToRGB")
    moss_col.color_ramp.elements[0].color = (0.05, 0.07, 0.015, 1)
    moss_col.color_ramp.elements[1].color = (0.22, 0.28, 0.04, 1)
    L.new(moss_n.outputs["Fac"], moss_col.inputs["Fac"])
    reddish_wet = _mixc(N, L, moss_n.outputs["Fac"], (0.10, 0.04, 0.02), moss_col.outputs["Color"])
    c1 = _mixc(N, L, band(1.0, 1.9), reddish_wet, upper)                     # moss band -> bark
    c0 = _mixc(N, L, band(0.25, 0.75), (0.012, 0.011, 0.008), c1)           # black wet band
    # moss on top-facing surfaces (branches, logs, buttress shoulders)
    sepn = N.new("ShaderNodeSeparateXYZ"); L.new(geo.outputs["Normal"], sepn.inputs[0])
    top = N.new("ShaderNodeMapRange")
    top.inputs["From Min"].default_value = 1 - moss_top; top.inputs["From Max"].default_value = 1 - moss_top + 0.25
    L.new(sepn.outputs["Z"], top.inputs["Value"])
    tm = N.new("ShaderNodeMath"); tm.operation = 'MULTIPLY'
    L.new(top.outputs[0], tm.inputs[0]); L.new(moss_n.outputs["Fac"], tm.inputs[1])
    final = _mixc(N, L, tm.outputs[0], c0, moss_col.outputs["Color"])
    L.new(final, bsdf.inputs["Base Color"])
    rr = N.new("ShaderNodeMapRange")       # wet = glossy near the waterline
    rr.inputs["To Min"].default_value = 0.35; rr.inputs["To Max"].default_value = 0.88
    L.new(band(0.2, 0.9), rr.inputs["Value"]); L.new(rr.outputs[0], bsdf.inputs["Roughness"])
    hb = N.new("ShaderNodeMath"); hb.operation = 'ADD'
    L.new(plates.outputs["Distance"], hb.inputs[0]); L.new(fib.outputs["Fac"], hb.inputs[1])
    bump = N.new("ShaderNodeBump"); bump.inputs["Strength"].default_value = 0.8
    bump.inputs["Distance"].default_value = 0.02
    L.new(hb.outputs[0], bump.inputs["Height"]); L.new(bump.outputs["Normal"], bsdf.inputs["Normal"])
    L.new(bsdf.outputs[0], out.inputs[0])
    return m


def mat_mud():
    m, nt, out = new_mat("PeatHummock")
    N, L = nt.nodes, nt.links
    bsdf = N.new("ShaderNodeBsdfPrincipled")
    tc = N.new("ShaderNodeTexCoord"); geo = N.new("ShaderNodeNewGeometry")
    base = N.new("ShaderNodeTexNoise"); base.inputs["Scale"].default_value = 3
    base.inputs["Detail"].default_value = 12
    L.new(tc.outputs["Object"], base.inputs["Vector"])
    br = N.new("ShaderNodeValToRGB")
    br.color_ramp.elements[0].color = (0.018, 0.013, 0.009, 1)    # black peat
    br.color_ramp.elements[1].color = (0.07, 0.045, 0.028, 1)     # brown muck
    L.new(base.outputs["Fac"], br.inputs["Fac"])
    # fallen cypress needles / leaf litter speckles
    lit = N.new("ShaderNodeTexVoronoi"); lit.inputs["Scale"].default_value = 70
    lit.inputs["Randomness"].default_value = 1
    L.new(tc.outputs["Object"], lit.inputs["Vector"])
    litm = N.new("ShaderNodeMapRange")
    litm.inputs["From Min"].default_value = 0.25; litm.inputs["From Max"].default_value = 0.12
    L.new(lit.outputs["Distance"], litm.inputs["Value"])
    lc = N.new("ShaderNodeValToRGB")
    lc.color_ramp.elements[0].color = (0.10, 0.045, 0.02, 1)
    lc.color_ramp.elements[1].color = (0.30, 0.13, 0.04, 1)
    L.new(lit.outputs["Color"], lc.inputs["Fac"])
    c = _mixc(N, L, litm.outputs[0], br.outputs["Color"], lc.outputs["Color"])
    # moss patches on higher ground
    mn = N.new("ShaderNodeTexNoise"); mn.inputs["Scale"].default_value = 0.8
    mn.inputs["Detail"].default_value = 8
    L.new(tc.outputs["Object"], mn.inputs["Vector"])
    mm = N.new("ShaderNodeMapRange")
    mm.inputs["From Min"].default_value = 0.55; mm.inputs["From Max"].default_value = 0.65
    L.new(mn.outputs["Fac"], mm.inputs["Value"])
    c = _mixc(N, L, mm.outputs[0], c, (0.06, 0.10, 0.02))
    # wet & dark toward the waterline
    sep = N.new("ShaderNodeSeparateXYZ"); L.new(geo.outputs["Position"], sep.inputs[0])
    wet = N.new("ShaderNodeMapRange")
    wet.inputs["From Min"].default_value = 0.0; wet.inputs["From Max"].default_value = 0.12
    L.new(sep.outputs["Z"], wet.inputs["Value"])
    c = _mixc(N, L, wet.outputs[0], (0.012, 0.009, 0.006), c)
    L.new(c, bsdf.inputs["Base Color"])
    rr = N.new("ShaderNodeMapRange")
    rr.inputs["To Min"].default_value = 0.15; rr.inputs["To Max"].default_value = 0.9
    L.new(wet.outputs[0], rr.inputs["Value"]); L.new(rr.outputs[0], bsdf.inputs["Roughness"])
    hb = N.new("ShaderNodeMath"); hb.operation = 'ADD'
    L.new(base.outputs["Fac"], hb.inputs[0]); L.new(litm.outputs[0], hb.inputs[1])
    bump = N.new("ShaderNodeBump"); bump.inputs["Strength"].default_value = 0.5
    bump.inputs["Distance"].default_value = 0.03
    L.new(hb.outputs[0], bump.inputs["Height"]); L.new(bump.outputs["Normal"], bsdf.inputs["Normal"])
    L.new(bsdf.outputs[0], out.inputs[0])
    return m


M_BARK = mat_cypress_bark("BaldCypressBarkReal", pale=0.5, moss_top=0.55)
M_PALE = mat_cypress_bark("PondCypressBarkReal", pale=0.75, moss_top=0.55, band_scale=0.8)
M_LOG = mat_cypress_bark("MossyLog", pale=0.3, moss_top=0.9, band_scale=0.05)   # tiny band_scale -> no waterline bands
M_KNEE = mat_cypress_bark("KneeBark", pale=0.25, moss_top=0.5, band_scale=0.6)
M_MUD = mat_mud()
M_SHORT = mat_simple("ShortGrass", (0.12, 0.26, 0.05), 0.45, translucent=0.35,
                     var=((0.08, 0.20, 0.03), (0.26, 0.34, 0.08)), var_scale=0.6)
M_GRASS = mat_simple("Sawgrass", (0.20, 0.26, 0.07), 0.45, translucent=0.3,
                     var=((0.14, 0.24, 0.05), (0.42, 0.38, 0.14)), var_scale=0.5)   # golden-green
M_FERN = mat_simple("Fern", (0.07, 0.20, 0.03), 0.5, translucent=0.35,
                    var=((0.05, 0.16, 0.02), (0.14, 0.30, 0.04)), var_scale=0.8)
M_LITTER = mat_simple("FloatingLeaves", (0.25, 0.08, 0.02), 0.35,
                      var=((0.10, 0.04, 0.015), (0.40, 0.14, 0.03)), var_scale=30)


def rosette_template(n_leaves, length, seed):
    rnd = random.Random(seed)
    V, F = [], []
    for k in range(n_leaves):
        az = 2 * math.pi * k / n_leaves + rnd.uniform(-0.2, 0.2)
        el = rnd.uniform(0.25, 1.35); Lr = length * rnd.uniform(0.55, 1.0)
        droop = rnd.uniform(0.2, 0.9); steps = 5; b = len(V)
        for s_ in range(steps + 1):
            t = s_ / steps; e = el - droop * t * t; rr = Lr * t
            cx = rr * math.cos(e) * math.cos(az); cy = rr * math.cos(e) * math.sin(az)
            cz = rr * math.sin(e) * 0.9 + 0.02
            wdt = 0.016 * (1 - t) + 0.0008
            px, py = -math.sin(az) * wdt, math.cos(az) * wdt
            V.append((cx + px, cy + py, cz)); V.append((cx - px, cy - py, cz))
        for s_ in range(steps):
            a = b + 2 * s_
            F.append((a, a + 1, a + 3, a + 2))
    return V, F


ROSETTES = [rosette_template(random.randint(30, 46), random.uniform(0.28, 0.45), i) for i in range(6)]


def add_airplants(V, F, pos, p, per_m):
    for _ in range(int(p["h"] * per_m * random.uniform(0.6, 1.3))):
        z = random.uniform(1.4, p["h"] - 0.3)
        a = random.uniform(0, 6.3)
        rr = trunk_radius(z, p, a); ox, oy = trunk_offset(z, p)
        c = Vector((pos[0] + ox + rr * math.cos(a), pos[1] + oy + rr * math.sin(a), z))
        tilt = Euler((random.uniform(-0.25, 0.25), random.uniform(0.15, 0.55), a), 'XYZ')
        m4 = Matrix.Translation(c) @ tilt.to_matrix().to_4x4() @ Matrix.Scale(random.uniform(0.75, 1.4), 4)
        tv, tf = random.choice(ROSETTES); b = len(V)
        V.extend(tuple(m4 @ Vector(v)) for v in tv)
        F.extend(tuple(i + b for i in f) for f in tf)


gV, gF = [], []
def grass_clump(cx, cy, n, height, spread):
    for _ in range(n):
        a = random.uniform(0, 6.3); rr = random.uniform(0, spread)
        bx, by = cx + rr * math.cos(a), cy + rr * math.sin(a)
        h = height * random.uniform(0.5, 1.1)
        la = random.uniform(0, 6.3); lean = random.uniform(0.1, 0.6) * h
        w = random.uniform(0.008, 0.016); b = len(gV)
        for s_ in range(5):
            t = s_ / 4
            x = bx + math.cos(la) * lean * t * t; y = by + math.sin(la) * lean * t * t
            ww = w * (1 - t) + 0.0005
            gV.append((x - math.sin(la) * ww, y + math.cos(la) * ww, -0.05 + h * t))
            gV.append((x + math.sin(la) * ww, y - math.cos(la) * ww, -0.05 + h * t))
        for s_ in range(4):
            q = b + 2 * s_
            gF.append((q, q + 1, q + 3, q + 2))

# ---------------------------------------------------------------- trunks (two species)
bald_V, bald_F, pond_V, pond_F = [], [], [], []
bV, bF, lV, lF, kV, kF, aV, aF = [], [], [], [], [], [], [], []
trunks = []
# (pos, r, flare, kind)
hero = []      # big feature trunks evenly spaced around the pond rim
for i_, (rr_, fl_, kd_) in enumerate([(0.36, 0.9, "bald"), (0.20, 0.42, "pond"), (0.30, 0.7, "bald"),
                                      (0.15, 0.3, "pond"), (0.28, 0.6, "bald"), (0.18, 0.35, "pond"),
                                      (0.32, 0.75, "bald"), (0.14, 0.28, "pond")]):
    a_ = 2 * math.pi * i_ / 8 + 0.3
    d_ = P["pond_radius"] + random.uniform(1.0, 2.5)
    hero.append(((DOME_C[0] + d_ * math.cos(a_), DOME_C[1] + d_ * math.sin(a_)), rr_, fl_, kd_))
spots = list(hero)
tries = 0
while len(spots) < P["n_trunks"] and tries < 30000:
    tries += 1
    x, y = in_dome()
    if math.dist((x, y), DOME_C) < P["pond_radius"] + random.uniform(0.0, 1.0):   # open pond in the middle
        continue
    kind = "bald" if random.random() < P["bald_share"] else "pond"
    gap = 2.0 if kind == "bald" else 1.0
    if any((x - s_[0][0]) ** 2 + (y - s_[0][1]) ** 2 < gap ** 2 for s_ in spots):
        continue
    if kind == "bald":
        r = random.uniform(0.07, 0.18); spots.append(((x, y), r, r * random.uniform(0.6, 1.6), kind))
    else:
        r = random.uniform(0.05, 0.13); spots.append(((x, y), r, r * random.uniform(0.6, 1.8), kind))

for (pos, r, flare, kind) in spots:
    bald = kind == "bald"
    p = dict(h=random.uniform(16, 24) if bald else random.uniform(13, 19), r=r, flare=flare,
             flare_h=random.uniform(1.2, 2.6) if bald else random.uniform(0.6, 1.3),
             k=random.choice([3, 4, 5, 6]), ph=random.uniform(0, 6.3),
             lean=(random.gauss(0, 0.025), random.gauss(0, 0.03)))
    p["h"] *= 1.0 - 0.35 * (math.dist(pos, DOME_C) / P["dome_radius"]) ** 2   # dome profile: tall centre, short rim
    if bald:
        build_trunk(bald_V, bald_F, pos, p)
    else:
        build_trunk(pond_V, pond_F, pos, p, seg=16, rings=34)
    trunks.append((pos, p, kind))
    add_airplants(aV, aF, pos, p, P["airplants_per_m"] * (0.33 if bald else 1.0))
    # branches + foliage: bald = full leafy, pond = sparse
    for _ in range(random.randint(7, 11) if bald else random.randint(4, 7)):
        z = random.uniform(p["h"] * (0.25 if bald else 0.35), p["h"] * 0.95)
        ox, oy = trunk_offset(z, p); a = random.uniform(0, 6.3)
        d = Vector((math.cos(a), math.sin(a), random.uniform(0.3, 1.0) if bald else random.uniform(0.6, 1.6)))
        Lb = random.uniform(1.5, 4.0) if bald else random.uniform(1.0, 2.8)
        pts = branch_pts(Vector((pos[0] + ox, pos[1] + oy, z)), d, Lb)
        tube(bV, bF, pts, r * 0.3 + 0.015, 0.008)
        if bald:
            leaf_cluster(lV, lF, pts[-1] + Vector((0, 0, 0.3)), random.uniform(1.0, 2.0), P["leaves_per_cluster"])
        else:
            leaf_cluster(lV, lF, pts[-1], random.uniform(0.7, 1.3), P["leaves_per_cluster"] // 2,
                         cells=(3, 3, 3, 0), size=(0.5, 1.0))
    if bald:
        leaf_cluster(lV, lF, Vector((pos[0], pos[1], p["h"])), 2.0, P["leaves_per_cluster"])
    # knees
    nk = random.randint(*P["knees_per_trunk"]) if bald else random.randint(0, 3)
    for _ in range(nk):
        a = random.uniform(0, 6.3); dd = random.uniform(0.8, 3.5 if bald else 1.6)
        knee(kV, kF, pos[0] + dd * math.cos(a), pos[1] + dd * math.sin(a),
             random.uniform(0.2, 1.0) if bald else random.uniform(0.1, 0.4),
             random.uniform(0.06, 0.14) if bald else random.uniform(0.04, 0.08))

for _ in range(P["knees_loose"] // 4):
    cx, cy = in_dome(0.9)
    for _ in range(random.randint(1, 6)):
        knee(kV, kF, cx + random.gauss(0, 0.5), cy + random.gauss(0, 0.5),
             random.uniform(0.15, 0.9), random.uniform(0.05, 0.12))

# background forest wall (lower and thinner than the slough, so sky shows through)

# sawgrass: hero tuft at the pond cypress in front + scattered clumps
grass_clump(DOME_C[0] - P["pond_radius"] * 0.8, DOME_C[1] - P["pond_radius"] * 0.6, 380, 0.9, 0.9)
for _ in range(14):
    pos, p, kind = random.choice([t for t in trunks[:60] if t[2] == "pond"])
    grass_clump(pos[0] + random.uniform(-1, 1), pos[1] + random.uniform(-1, 1),
                random.randint(60, 180), random.uniform(0.5, 1.0), 0.5)

mesh_obj("BaldCypressTrunks", bald_V, bald_F, M_BARK)
mesh_obj("PondCypressTrunks", pond_V, pond_F, M_PALE)
mesh_obj("Branches", bV, bF, M_BARK)
mesh_obj("Foliage", lV, lF, M_LEAF, smooth=False, uvs=LEAF_UV)
mesh_obj("AirPlants", aV, aF, M_TILL)
mesh_obj("CypressKnees", kV, kF, M_KNEE)
mesh_obj("Sawgrass", gV, gF, M_GRASS)

# ================================================================ REALISM PASS: hummocks + native ground cover
# Raised peat hummocks (tree islands) rise out of the water around cypress clusters and at
# random spots; grasses, sawgrass, spiky rosettes and ferns grow on them, with sawgrass and
# maidencane fringing the waterline. Leaf litter floats on the open tea-coloured water.
islands = []
for (pos, p, kind) in trunks:
    if pos[1] < 32 and random.random() < (0.55 if kind == "pond" else 0.35):
        islands.append((pos[0] + random.uniform(-0.6, 0.6), pos[1] + random.uniform(-0.6, 0.6),
                        random.uniform(0.40, 0.65), random.uniform(1.6, 3.2)))
for _ in range(P["n_hummocks"]):
    _ix, _iy = in_dome(0.95)
    if math.dist((_ix, _iy), DOME_C) < P["pond_radius"] + 1.5:
        continue
    islands.append((_ix, _iy,
                    random.uniform(0.35, 0.6), random.uniform(1.5, 4.0)))
# a foreground bank on the right like the third reference



def ground_h(x, y):
    h = -0.45
    for (ix, iy, amp, rad) in islands:
        d2 = (x - ix) ** 2 + (y - iy) ** 2
        if d2 < 9 * rad * rad:
            h = max(h, -0.45 + amp * math.exp(-d2 / (rad * rad)) * 1.0)
    h += 0.10 * mnoise.noise(Vector((x * 0.35, y * 0.35, 0.0))) \
        + 0.03 * mnoise.noise(Vector((x * 2.0, y * 2.0, 1.7)))
    return h


# terrain grid
_ext = P["dome_radius"] + P["marsh_width"] + 4
tx0, tx1, ty0, ty1, step = DOME_C[0] - _ext, DOME_C[0] + _ext, DOME_C[1] - _ext, DOME_C[1] + _ext, 0.4
nx = int((tx1 - tx0) / step) + 1; ny = int((ty1 - ty0) / step) + 1
terV, terF = [], []
for j in range(ny):
    for i in range(nx):
        x = tx0 + i * step; y = ty0 + j * step
        terV.append((x, y, ground_h(x, y)))
for j in range(ny - 1):
    for i in range(nx - 1):
        a = j * nx + i
        terF.append((a, a + 1, a + nx + 1, a + nx))
mesh_obj("PeatHummocks", terV, terF, M_MUD)


# ---- plant builders
shV, shF, sgV, sgF, fnV, fnF = [], [], [], [], [], []


def blade(V, F, base, h, w, lean_a, lean, steps=3, curl=0.0):
    b = len(V)
    for s_ in range(steps + 1):
        t = s_ / steps
        x = base.x + math.cos(lean_a) * lean * t * t
        y = base.y + math.sin(lean_a) * lean * t * t
        z = base.z + h * t - curl * t ** 3
        ww = w * (1 - t) + 0.0004
        V.append((x - math.sin(lean_a) * ww, y + math.cos(lean_a) * ww, z))
        V.append((x + math.sin(lean_a) * ww, y - math.cos(lean_a) * ww, z))
    for s_ in range(steps):
        q = b + 2 * s_
        F.append((q, q + 1, q + 3, q + 2))


def short_tuft(x, y, z):
    for _ in range(random.randint(5, 10)):
        bx = x + random.gauss(0, 0.05); by = y + random.gauss(0, 0.05)
        h = random.uniform(0.08, 0.35)
        blade(shV, shF, Vector((bx, by, z - 0.02)), h, random.uniform(0.003, 0.006),
              random.uniform(0, 6.3), h * random.uniform(0.1, 0.6))


def sawgrass(x, y, z, scale=1.0):
    for _ in range(random.randint(25, 50)):
        bx = x + random.gauss(0, 0.12); by = y + random.gauss(0, 0.12)
        h = random.uniform(0.6, 1.6) * scale
        blade(sgV, sgF, Vector((bx, by, z - 0.05)), h, random.uniform(0.007, 0.013),
              random.uniform(0, 6.3), h * random.uniform(0.15, 0.55), steps=4, curl=h * 0.15)


def spiky_rosette(x, y, z, scale=1.0):
    n = random.randint(14, 24)
    for k in range(n):
        az = 2 * math.pi * k / n + random.uniform(-0.2, 0.2)
        L_ = random.uniform(0.35, 0.8) * scale
        el = random.uniform(0.6, 1.3)
        blade(sgV, sgF, Vector((x, y, z - 0.03)), L_ * math.sin(el), random.uniform(0.012, 0.022),
              az, L_ * math.cos(el), steps=4, curl=L_ * 0.25)


def fern(x, y, z, scale=1.0):
    for _ in range(random.randint(6, 11)):
        az = random.uniform(0, 6.3); Lf = random.uniform(0.45, 0.9) * scale
        el = random.uniform(0.7, 1.2); steps = 10
        dirv = Vector((math.cos(az), math.sin(az), 0))
        side = Vector((-math.sin(az), math.cos(az), 0))
        for s_ in range(1, steps):
            t = s_ / steps
            c = Vector((x, y, z)) + dirv * Lf * t * math.cos(el) \
                + Vector((0, 0, Lf * t * math.sin(el) - Lf * 0.6 * t * t))
            pl = 0.13 * scale * (1 - t) + 0.02
            for sgn in (-1, 1):
                b = len(fnV)
                tip = c + side * sgn * pl + dirv * pl * 0.4 + Vector((0, 0, -0.02))
                w = Vector((0, 0, 0.012)) + dirv * 0.012
                fnV.extend([tuple(c - w), tuple(c + w), tuple(tip + w * 0.3), tuple(tip - w * 0.3)])
                fnF.append((b, b + 1, b + 2, b + 3))


# ---- scatter by terrain height
for _ in range(P["n_ground_samples"]):
    x, y = in_dome(1.05)
    h = ground_h(x, y)
    if h > 0.04:                                  # dry-ish hummock top
        short_tuft(x, y, h)
        r_ = random.random()
        if r_ < 0.035:
            fern(x, y, h)
        elif r_ < 0.06:
            spiky_rosette(x, y, h, random.uniform(0.7, 1.2))
    elif -0.12 < h <= 0.04:                       # waterline fringe
        r_ = random.random()
        if r_ < 0.10:
            sawgrass(x, y, max(h, 0.0), random.uniform(0.6, 1.1))
        elif r_ < 0.16:
            spiky_rosette(x, y, max(h, 0.0), random.uniform(0.8, 1.3))
        elif r_ < 0.5:
            short_tuft(x, y, max(h, 0.0))         # maidencane emerging from shallows

mesh_obj("ShortGrass", shV, shF, M_SHORT)
mesh_obj("SawgrassRosettes", sgV, sgF, M_GRASS)
mesh_obj("Ferns", fnV, fnF, M_FERN, smooth=False)

# ---- fallen mossy logs resting on the hummocks
logV, logF = [], []
for _ in range(P["n_logs"]):
    ix, iy, amp, rad = random.choice(islands[-12:])
    a = random.uniform(0, 6.3); Lg = random.uniform(4, 9); r0 = random.uniform(0.12, 0.25)
    start = Vector((ix - math.cos(a) * Lg * 0.4, iy - math.sin(a) * Lg * 0.4, 0))
    pts = []
    for i in range(16):
        t = i / 15
        c = start + Vector((math.cos(a), math.sin(a), 0)) * Lg * t
        c.z = max(ground_h(c.x, c.y), -0.1) + r0 * 0.6
        pts.append(c)
    tube(logV, logF, pts, r0, r0 * 0.7, seg=14)
mesh_obj("FallenLogs", logV, logF, M_LOG)

# ---- leaf litter floating on open water
litV, litF = [], []
for _ in range(P["n_floating_leaves"]):
    x, y = in_dome(1.1)
    if ground_h(x, y) > -0.02:
        continue
    a = random.uniform(0, 6.3); s_ = random.uniform(0.03, 0.07)
    ca, sa = math.cos(a), math.sin(a); b = len(litV)
    for (u, v) in ((-1, 0), (0, -0.4), (1, 0), (0, 0.4)):
        litV.append((x + (u * ca - v * sa) * s_, y + (u * sa + v * ca) * s_, 0.004))
    litF.append((b, b + 1, b + 2, b + 3))
mesh_obj("FloatingLeaves", litV, litF, M_LITTER, smooth=False)
# distant forest backdrop so gaps read as deep green woods, not white sky
M_BACK = mat_simple("ForestBackdrop", (0.02, 0.05, 0.01), 0.9,
                    var=((0.006, 0.018, 0.004), (0.04, 0.10, 0.02)), var_scale=0.35)

# ================================================================ OPEN SAWGRASS MARSH ("River of Grass") TO THE HORIZON
# The cypress stand is now a dome edge framing the view; beyond it a sawgrass prairie with
# open water sloughs runs to the horizon, dotted with distant tree islands under a cloudy sky.
# Grass is instanced with Geometry Nodes so hundreds of thousands of clumps stay light.
HAZE = (0.62, 0.70, 0.78)


def _haze(N, L, col_socket, dist=1800.0):
    """Aerial perspective: fade toward haze colour with camera distance."""
    cd = N.new("ShaderNodeCameraData")
    d = N.new("ShaderNodeMath"); d.operation = 'DIVIDE'
    L.new(cd.outputs["View Distance"], d.inputs[0]); d.inputs[1].default_value = -dist
    e = N.new("ShaderNodeMath"); e.operation = 'EXPONENT'; L.new(d.outputs[0], e.inputs[0])
    f = N.new("ShaderNodeMath"); f.operation = 'SUBTRACT'; f.inputs[0].default_value = 1.0
    L.new(e.outputs[0], f.inputs[1])
    return _mixc(N, L, f.outputs[0], col_socket, HAZE)


def mat_marsh(name, translucent=0.3, rough=0.55):
    m, nt, out = new_mat(name)
    N, L = nt.nodes, nt.links
    bsdf = N.new("ShaderNodeBsdfPrincipled"); bsdf.inputs["Roughness"].default_value = rough
    geo = N.new("ShaderNodeNewGeometry")
    n1 = N.new("ShaderNodeTexNoise"); n1.inputs["Scale"].default_value = 0.012
    n1.inputs["Detail"].default_value = 6
    L.new(geo.outputs["Position"], n1.inputs["Vector"])
    n2 = N.new("ShaderNodeTexNoise"); n2.inputs["Scale"].default_value = 0.25
    L.new(geo.outputs["Position"], n2.inputs["Vector"])
    r1 = N.new("ShaderNodeValToRGB")
    r1.color_ramp.elements[0].position = 0.35; r1.color_ramp.elements[0].color = (0.09, 0.16, 0.03, 1)  # green sawgrass
    r1.color_ramp.elements[1].position = 0.65; r1.color_ramp.elements[1].color = (0.38, 0.30, 0.12, 1)  # golden-tan sawgrass
    L.new(n1.outputs["Fac"], r1.inputs["Fac"])
    c = _mixc(N, L, 0.35, r1.outputs["Color"], n2.outputs["Color"], 'OVERLAY')
    c = _haze(N, L, c)
    L.new(c, bsdf.inputs["Base Color"])
    if translucent > 0:
        tr = N.new("ShaderNodeBsdfTranslucent"); L.new(c, tr.inputs["Color"])
        mx = N.new("ShaderNodeMixShader"); mx.inputs[0].default_value = translucent
        L.new(bsdf.outputs[0], mx.inputs[1]); L.new(tr.outputs[0], mx.inputs[2])
        L.new(mx.outputs[0], out.inputs[0])
    else:
        L.new(bsdf.outputs[0], out.inputs[0])
    return m


def mat_hammock():
    m, nt, out = new_mat("TreeIsland")
    N, L = nt.nodes, nt.links
    bsdf = N.new("ShaderNodeBsdfPrincipled"); bsdf.inputs["Roughness"].default_value = 0.8
    geo = N.new("ShaderNodeNewGeometry")
    nz = N.new("ShaderNodeTexNoise"); nz.inputs["Scale"].default_value = 0.4
    nz.inputs["Detail"].default_value = 8
    L.new(geo.outputs["Position"], nz.inputs["Vector"])
    rp = N.new("ShaderNodeValToRGB")
    rp.color_ramp.elements[0].color = (0.012, 0.03, 0.008, 1)
    rp.color_ramp.elements[1].color = (0.06, 0.12, 0.025, 1)
    L.new(nz.outputs["Fac"], rp.inputs["Fac"])
    L.new(_haze(N, L, rp.outputs["Color"], 2600), bsdf.inputs["Base Color"])
    L.new(bsdf.outputs[0], out.inputs[0])
    return m


M_MARSH = mat_marsh("MarshSawgrass")

# ---- sawgrass clumps at three levels of detail (LOD0 = full, LOD1 = half, LOD2 = cheap cards)
LOD_COLL = "GrassLOD" + TAG
if LOD_COLL in bpy.data.collections:
    c_ = bpy.data.collections[LOD_COLL]
    for o in list(c_.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    bpy.data.collections.remove(c_)
lod_coll = bpy.data.collections.new(LOD_COLL)      # not linked to the scene: only used as an instance source


def make_clump(name, n_blades, steps, width_mul, seed):
    rnd_state = random.getstate(); random.seed(seed)
    V, F = [], []
    for _ in range(n_blades):
        bx, by = random.gauss(0, 0.15), random.gauss(0, 0.15)
        h = random.uniform(0.8, 1.8)
        blade(V, F, Vector((bx, by, -0.1)), h, random.uniform(0.008, 0.014) * width_mul,
              random.uniform(0, 6.3), h * random.uniform(0.15, 0.5), steps=steps, curl=h * 0.2 if steps > 1 else 0)
    random.setstate(rnd_state)
    me = bpy.data.meshes.new(name + TAG); me.from_pydata(V, [], F); me.update(); me.shade_smooth()
    me.materials.append(M_MARSH)
    ob = bpy.data.objects.new(name + TAG, me); lod_coll.objects.link(ob)
    return ob, len(V)

lod_info = [make_clump("LOD0_Sawgrass", 18, 3, 1.0, 101),     # ~144 verts, close to camera
            make_clump("LOD1_Sawgrass", 9, 2, 1.7, 102),      # ~54 verts, mid distance
            make_clump("LOD2_Sawgrass", 4, 1, 3.5, 103)]      # ~16 verts, far
print("Grass LOD vertex counts:", [n for _, n in lod_info])


def _sock(sockets, typ):
    return [s_ for s_ in sockets if s_.enabled and s_.type == typ]


def _math(N, L, op, a, b=None):
    m_ = N.new("ShaderNodeMath"); m_.operation = op
    for i, v in enumerate((a, b)):
        if v is None:
            continue
        if isinstance(v, (int, float)):
            m_.inputs[i].default_value = v
        else:
            L.new(v, m_.inputs[i])
    return m_.outputs[0]


def marsh_ring_group():
    name = "MarshRingLOD" + TAG
    if name in bpy.data.node_groups:
        bpy.data.node_groups.remove(bpy.data.node_groups[name])
    ng = bpy.data.node_groups.new(name, 'GeometryNodeTree')
    I = ng.interface
    I.new_socket("Geometry", in_out='INPUT', socket_type='NodeSocketGeometry')
    for nm, st in (("Density", 'NodeSocketFloat'), ("Center", 'NodeSocketVector'),
                   ("Inner Radius", 'NodeSocketFloat'), ("Outer Radius", 'NodeSocketFloat'),
                   ("Camera", 'NodeSocketObject'), ("LOD Collection", 'NodeSocketCollection'),
                   ("LOD1 Distance", 'NodeSocketFloat'), ("LOD2 Distance", 'NodeSocketFloat'),
                   ("Viewport Density", 'NodeSocketFloat')):
        I.new_socket(nm, in_out='INPUT', socket_type=st)
    I.new_socket("Geometry", in_out='OUTPUT', socket_type='NodeSocketGeometry')
    N, L = ng.nodes, ng.links
    gi = N.new("NodeGroupInput"); go = N.new("NodeGroupOutput")
    pos = N.new("GeometryNodeInputPosition")
    # radial fade (in at the dome edge, out at the outer rim) is baked on the ring mesh as "grass_mask"
    na = N.new("GeometryNodeInputNamedAttribute"); na.data_type = 'FLOAT'
    na.inputs["Name"].default_value = "grass_mask"
    mask_out = [o for o in na.outputs if o.enabled and o.type == 'VALUE'][0]
    # open-water sloughs through the grass
    nz = N.new("ShaderNodeTexNoise"); nz.inputs["Scale"].default_value = 0.06
    nz.inputs["Detail"].default_value = 4
    L.new(pos.outputs[0], nz.inputs["Vector"])
    sl = N.new("ShaderNodeMapRange"); L.new(nz.outputs["Fac"], sl.inputs["Value"])
    sl.inputs["From Min"].default_value = 0.38; sl.inputs["From Max"].default_value = 0.48
    # viewport LOD: thin the grass while working in the viewport, full density at render
    isvp = N.new("GeometryNodeIsViewport")
    vpf = _math(N, L, 'MULTIPLY_ADD', isvp.outputs[0], _math(N, L, 'SUBTRACT', gi.outputs["Viewport Density"], 1.0))
    vpf.node.inputs[2].default_value = 1.0
    dens = _math(N, L, 'MULTIPLY', gi.outputs["Density"], mask_out)
    dens = _math(N, L, 'MULTIPLY', dens, sl.outputs[0])
    dens = _math(N, L, 'MULTIPLY', dens, vpf)
    dist = N.new("GeometryNodeDistributePointsOnFaces"); dist.distribute_method = 'RANDOM'
    L.new(gi.outputs["Geometry"], dist.inputs["Mesh"]); L.new(dens, dist.inputs["Density"])
    # LOD index from distance to the active camera
    cam_i = N.new("GeometryNodeObjectInfo"); cam_i.transform_space = 'ORIGINAL'
    L.new(gi.outputs["Camera"], cam_i.inputs["Object"])
    cd = N.new("ShaderNodeVectorMath"); cd.operation = 'DISTANCE'
    L.new(pos.outputs[0], cd.inputs[0]); L.new(cam_i.outputs["Location"], cd.inputs[1])
    c1 = _math(N, L, 'GREATER_THAN', cd.outputs["Value"], gi.outputs["LOD1 Distance"])
    c2 = _math(N, L, 'GREATER_THAN', cd.outputs["Value"], gi.outputs["LOD2 Distance"])
    lod_idx = _math(N, L, 'ADD', c1, c2)
    ci = N.new("GeometryNodeCollectionInfo"); ci.transform_space = 'ORIGINAL'
    L.new(gi.outputs["LOD Collection"], ci.inputs["Collection"])
    ci.inputs["Separate Children"].default_value = True
    ci.inputs["Reset Children"].default_value = True
    rot = N.new("FunctionNodeRandomValue"); rot.data_type = 'FLOAT_VECTOR'
    vin = _sock(rot.inputs, 'VECTOR')
    vin[0].default_value = (-0.12, -0.12, 0.0); vin[1].default_value = (0.12, 0.12, 6.283)
    scl = N.new("FunctionNodeRandomValue"); scl.data_type = 'FLOAT'
    fv = _sock(scl.inputs, 'VALUE'); fv[0].default_value = 0.6; fv[1].default_value = 1.4
    iop = N.new("GeometryNodeInstanceOnPoints")
    L.new(dist.outputs["Points"], iop.inputs["Points"])
    L.new(ci.outputs[0], iop.inputs["Instance"])
    iop.inputs["Pick Instance"].default_value = True
    L.new(lod_idx, iop.inputs["Instance Index"])
    L.new(_sock(rot.outputs, 'VECTOR')[0], iop.inputs["Rotation"])
    L.new(_sock(scl.outputs, 'VALUE')[0], iop.inputs["Scale"])
    L.new(iop.outputs["Instances"], go.inputs["Geometry"])
    return ng


NG_RING = marsh_ring_group()

# annulus mesh around the cypress dome (grass only grows here)
R_in = P["dome_radius"]; R_out = P["dome_radius"] + P["marsh_width"]
segs = 96; rV, rF, rMask = [], [], []
radii = [R_in - 3 + i for i in range(int(R_out - (R_in - 3)) + 1)]
for rad in radii:
    fade_in = min(max((rad - (R_in - 3)) / 4.0, 0.0), 1.0)
    fade_out = min(max((R_out - rad) / 6.0, 0.0), 1.0)
    for k in range(segs):
        a = 2 * math.pi * k / segs
        rV.append((DOME_C[0] + rad * math.cos(a), DOME_C[1] + rad * math.sin(a), 0))
        rMask.append(fade_in * fade_out)
for i in range(len(radii) - 1):
    for k in range(segs):
        a0 = i * segs + k; a1 = i * segs + (k + 1) % segs
        rF.append((a0, a1, a1 + segs, a0 + segs))
ring = mesh_obj("MarshRing", rV, rF, smooth=False)
_att = ring.data.attributes.new("grass_mask", 'FLOAT', 'POINT')
_att.data.foreach_set("value", rMask)
md = ring.modifiers.new("MarshRingLOD", 'NODES'); md.node_group = NG_RING
vals = {"Density": P["marsh_density"], "Center": (DOME_C[0], DOME_C[1], 0.0),
        "Inner Radius": R_in, "Outer Radius": R_out, "Camera": None,   # filled after camera is created
        "LOD Collection": lod_coll, "LOD1 Distance": P["lod1_distance"],
        "LOD2 Distance": P["lod2_distance"], "Viewport Density": P["viewport_density"]}


def set_ring_input(md_, key, val):
    for item in md_.node_group.interface.items_tree:
        if getattr(item, "in_out", None) == 'INPUT' and item.name == key:
            if hasattr(md_, "properties"):
                getattr(md_.properties.inputs, item.identifier).value = val
            else:
                md_[item.identifier] = val


for k_, v_ in vals.items():
    if v_ is not None:
        set_ring_input(md, k_, v_)
print("Marsh ring built: radius", R_in, "->", R_out)


print("Realism pass:", len(islands), "hummocks")

# ---------------------------------------------------------------- floating sticks & vines
sV, sF = [], []
for _ in range(P["n_sticks"]):
    x, y = in_dome(0.9)
    a = random.uniform(0, 6.3); Ls = random.uniform(0.8, 4.0)
    pitch = random.choice([0, 0, 0, random.uniform(0.15, 0.6)])
    d = Vector((math.cos(a), math.sin(a), pitch))
    pts = branch_pts(Vector((x, y, 0.02)), d, Ls, droop=0.0, steps=4)
    tube(sV, sF, pts, random.uniform(0.02, 0.05), 0.01, seg=6)
mesh_obj("FloatingSticks", sV, sF, M_STICK)

vV, vF = [], []
bald_trunks = [t for t in trunks if t[2] == "bald" and abs(t[0][0]) > 5][:30] or [t for t in trunks if t[2] == "bald"]
for _ in range(P["n_vines"]):
    pos, p, _k = random.choice(bald_trunks)
    top = Vector((pos[0], pos[1], random.uniform(8, p["h"] * 0.8)))
    a = random.uniform(0, 6.3); dist = random.uniform(1.5, 6)
    bot = Vector((pos[0] + dist * math.cos(a), pos[1] + dist * math.sin(a), random.uniform(-0.1, 1.5)))
    pts = []
    for i in range(13):
        t = i / 12; c = top.lerp(bot, t)
        c.z -= math.sin(t * math.pi) * random.uniform(0.3, 0.8)
        c.x += 0.15 * math.sin(t * 9); c.y += 0.15 * math.cos(t * 7)
        pts.append(c)
    tube(vV, vF, pts, 0.018, 0.012, seg=5)
mesh_obj("HangingVines", vV, vF, M_VINE)

# ---------------------------------------------------------------- water with duckweed rafts
_w = 3000   # open water beyond the marsh ring, out to the horizon
mesh_obj("SwampWater", [(-_w, -_w, 0), (_w, -_w, 0), (_w, _w, 0), (-_w, _w, 0)],
         [(0, 1, 2, 3)], M_DUCK, smooth=False)

# ---------------------------------------------------------------- world (blue sky), sun, camera
world = bpy.data.worlds.get("World" + TAG) or bpy.data.worlds.new("World" + TAG)
scene.world = world
world.use_nodes = True
wn = world.node_tree; wn.nodes.clear()
wo = wn.nodes.new("ShaderNodeOutputWorld")
bg = wn.nodes.new("ShaderNodeBackground"); bg.inputs["Strength"].default_value = 1.3
W, WL = wn.nodes, wn.links
tc = W.new("ShaderNodeTexCoord"); sep = W.new("ShaderNodeSeparateXYZ")
WL.new(tc.outputs["Generated"], sep.inputs[0])
ramp = W.new("ShaderNodeValToRGB")
ramp.color_ramp.elements[0].position = 0.0; ramp.color_ramp.elements[0].color = (*HAZE, 1)   # hazy horizon
ramp.color_ramp.elements[1].position = 0.4; ramp.color_ramp.elements[1].color = (0.13, 0.30, 0.75, 1)
WL.new(sep.outputs["Z"], ramp.inputs["Fac"])
# flat cloud layer: project view direction onto a plane (x/z, y/z)
zc = W.new("ShaderNodeMath"); zc.operation = 'MAXIMUM'; zc.inputs[1].default_value = 0.02
WL.new(sep.outputs["Z"], zc.inputs[0])
dx = W.new("ShaderNodeMath"); dx.operation = 'DIVIDE'; WL.new(sep.outputs["X"], dx.inputs[0]); WL.new(zc.outputs[0], dx.inputs[1])
dy = W.new("ShaderNodeMath"); dy.operation = 'DIVIDE'; WL.new(sep.outputs["Y"], dy.inputs[0]); WL.new(zc.outputs[0], dy.inputs[1])
cv = W.new("ShaderNodeCombineXYZ"); WL.new(dx.outputs[0], cv.inputs[0]); WL.new(dy.outputs[0], cv.inputs[1])
cn = W.new("ShaderNodeTexNoise"); cn.inputs["Scale"].default_value = 0.45
cn.inputs["Detail"].default_value = 10; cn.inputs["Roughness"].default_value = 0.6
WL.new(cv.outputs[0], cn.inputs["Vector"])
cr = W.new("ShaderNodeMapRange"); cr.inputs["From Min"].default_value = 0.5; cr.inputs["From Max"].default_value = 0.68
WL.new(cn.outputs["Fac"], cr.inputs["Value"])
fade = W.new("ShaderNodeMapRange"); fade.inputs["From Min"].default_value = 0.01; fade.inputs["From Max"].default_value = 0.12
WL.new(sep.outputs["Z"], fade.inputs["Value"])
cm = W.new("ShaderNodeMath"); cm.operation = 'MULTIPLY'
WL.new(cr.outputs[0], cm.inputs[0]); WL.new(fade.outputs[0], cm.inputs[1])
cmix = W.new("ShaderNodeMix"); cmix.data_type = 'RGBA'
WL.new(cm.outputs[0], cmix.inputs[0]); WL.new(ramp.outputs["Color"], cmix.inputs[6])
cmix.inputs[7].default_value = (0.95, 0.95, 0.93, 1)     # cumulus white
WL.new(cmix.outputs[2], bg.inputs["Color"])
WL.new(bg.outputs[0], wo.inputs[0])

sun = bpy.data.objects.new("Sun" + TAG, bpy.data.lights.new("Sun" + TAG, 'SUN'))
coll.objects.link(sun)
sun.data.energy = 3.2
sun.data.color = (1.0, 0.95, 0.86)
sun.data.angle = math.radians(4)
sun.rotation_euler = (math.radians(40), math.radians(-15), math.radians(160))

cam = bpy.data.objects.new("Camera" + TAG, bpy.data.cameras.new("Camera" + TAG))
coll.objects.link(cam)
scene.camera = cam
cam.location = (DOME_C[0], DOME_C[1] - P["pond_radius"] * 0.85, 1.8)   # standing at the pond edge
cam.rotation_euler = (math.radians(90), 0, 0)
cam.data.lens = 20
cam.data.clip_end = 10000
set_ring_input(md, "Camera", cam)        # grass LOD is measured from this camera

scene.render.engine = 'BLENDER_EEVEE'
scene.render.resolution_x, scene.render.resolution_y = 1920, 1080
ee = scene.eevee
for attr, val in (("use_raytracing", True), ("use_shadows", True), ("taa_render_samples", 64)):
    if hasattr(ee, attr):
        setattr(ee, attr, val)
scene.view_settings.view_transform = 'AgX'
scene.view_settings.exposure = 0.0
looks = [i.identifier for i in scene.view_settings.bl_rna.properties['look'].enum_items]
scene.view_settings.look = 'AgX - Medium High Contrast' if 'AgX - Medium High Contrast' in looks else 'None'

print("Hybrid swamp built:", len(trunks), "trunks")

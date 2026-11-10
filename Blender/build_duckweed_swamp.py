"""
Bald cypress slough with duckweed, for Blender 5.x
Builds a separate scene "DuckweedSwamp" (your pond-cypress scene is left untouched):
duckweed-covered water, big buttressed brown cypress trunks, dense cypress knees,
leafy green canopy and background forest, floating sticks, hanging vines,
soft overcast light and a camera looking down across the water.

Run inside Blender (Scripting tab > Open > Run Script). Re-running rebuilds the scene.
"""
import bpy, math, random
from mathutils import Vector, Matrix, Euler

P = dict(
    seed=11,
    n_trunks=120,            # total trunks incl. background
    area_x=(-18, 18),
    area_y=(3, 45),
    knees_loose=180,         # knees scattered across the water
    knees_per_trunk=(3, 12),
    leaves_per_cluster=260,
    n_sticks=40,
    n_vines=30,
)
random.seed(P["seed"])
SCENE_NAME = "DuckweedSwamp"
TAG = "_dw"

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


def mesh_obj(name, verts, faces, mat=None, smooth=True):
    me = bpy.data.meshes.new(name + TAG)
    me.from_pydata(verts, [], faces)
    me.update()
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
    gap_n = N.new("ShaderNodeTexNoise"); gap_n.inputs["Scale"].default_value = 0.6
    gap_n.inputs["Detail"].default_value = 8
    L.new(tc.outputs["Object"], gap_n.inputs["Vector"])
    gap = N.new("ShaderNodeValToRGB")
    gap.color_ramp.elements[0].position = 0.70; gap.color_ramp.elements[0].color = (0, 0, 0, 1)
    gap.color_ramp.elements[1].position = 0.76; gap.color_ramp.elements[1].color = (1, 1, 1, 1)
    L.new(gap_n.outputs["Fac"], gap.inputs["Fac"])
    water = N.new("ShaderNodeBsdfPrincipled")
    water.inputs["Base Color"].default_value = (0.01, 0.015, 0.005, 1)
    water.inputs["Roughness"].default_value = 0.05
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
M_LEAF = mat_simple("CypressFoliage", (0.12, 0.30, 0.05), 0.5, translucent=0.35,
                    var=((0.08, 0.22, 0.03), (0.28, 0.45, 0.08)), var_scale=0.3)
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


def leaf_cluster(V, F, center, radius, n, flat=0.6):
    """Cloud of small leaf cards - reads as feathery cypress foliage."""
    for _ in range(n):
        o = Vector((random.gauss(0, 1), random.gauss(0, 1), random.gauss(0, flat))).normalized()
        c = center + o * radius * random.random() ** 0.5
        rot = Euler((random.uniform(0, 6.3), random.uniform(0, 6.3), random.uniform(0, 6.3))).to_matrix()
        s = random.uniform(0.12, 0.28); w = s * 0.35
        b = len(V)
        for q in ((-w, 0, 0), (w, 0, 0), (w * 0.4, s, 0), (-w * 0.4, s, 0)):
            V.append(tuple(c + rot @ Vector(q)))
        F.append((b, b + 1, b + 2, b + 3))


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


# ---------------------------------------------------------------- trunks + canopy
tV, tF, bV, bF, lV, lF, kV, kF = [], [], [], [], [], [], [], []
trunks = []
hero = [((4.2, 6.0), 0.36, 0.95), ((-0.8, 11.0), 0.30, 0.75), ((-6.5, 14.0), 0.26, 0.55),
        ((8.5, 15.0), 0.28, 0.6), ((1.6, 9.0), 0.10, 0.12), ((-3.5, 20.0), 0.24, 0.5)]
spots = list(hero)
tries = 0
while len(spots) < P["n_trunks"] and tries < 30000:
    tries += 1
    x = random.uniform(*P["area_x"]); y = random.uniform(*P["area_y"])
    x *= 0.5 + y / 22
    if y < 9 and abs(x) < 6:          # keep the foreground fairly open like the photo
        continue
    if any((x - s[0][0]) ** 2 + (y - s[0][1]) ** 2 < 2.2 ** 2 for s in spots):
        continue
    r = random.uniform(0.06, 0.18)
    spots.append(((x, y), r, r * random.uniform(0.6, 1.6)))

for (pos, r, flare) in spots:
    p = dict(h=random.uniform(16, 24), r=r, flare=flare, flare_h=random.uniform(1.2, 2.6),
             k=random.choice([3, 4, 5, 6]), ph=random.uniform(0, 6.3),
             lean=(random.gauss(0, 0.025), random.gauss(0, 0.025)))
    build_trunk(tV, tF, pos, p)
    trunks.append((pos, p))
    # upper branches with foliage clusters at the ends
    for _ in range(random.randint(6, 11)):
        z = random.uniform(p["h"] * 0.3, p["h"] * 0.95)
        ox, oy = trunk_offset(z, p)
        a = random.uniform(0, 6.3)
        d = Vector((math.cos(a), math.sin(a), random.uniform(0.3, 1.0)))
        L = random.uniform(1.5, 4.0)
        pts = branch_pts(Vector((pos[0] + ox, pos[1] + oy, z)), d, L)
        tube(bV, bF, pts, r * 0.3 + 0.02, 0.01)
        leaf_cluster(lV, lF, pts[-1] + Vector((0, 0, 0.3)), random.uniform(1.0, 2.0), P["leaves_per_cluster"])
        leaf_cluster(lV, lF, pts[len(pts) // 2] + Vector((0, 0, 0.4)), random.uniform(0.7, 1.3), P["leaves_per_cluster"] // 2)
    leaf_cluster(lV, lF, Vector((pos[0], pos[1], p["h"])), 2.0, P["leaves_per_cluster"])
    # knees around each trunk
    for _ in range(random.randint(*P["knees_per_trunk"])):
        a = random.uniform(0, 6.3); dd = random.uniform(1.0, 4.0)
        knee(kV, kF, pos[0] + dd * math.cos(a), pos[1] + dd * math.sin(a),
             random.uniform(0.2, 1.0), random.uniform(0.06, 0.14))

# loose knees across the open water (clustered)
for _ in range(P["knees_loose"] // 4):
    cx = random.uniform(-10, 10); cy = random.uniform(-1, 30)
    for _ in range(random.randint(1, 7)):
        kx = cx + random.gauss(0, 0.5); ky = cy + random.gauss(0, 0.5)
        knee(kV, kF, kx, ky, random.uniform(0.15, 1.1) * random.choice([1, 1, 1, 1.4]),
             random.uniform(0.05, 0.13))

# background understory: low foliage wall
for _ in range(260):
    x = random.uniform(-35, 35); y = random.uniform(26, 55)
    leaf_cluster(lV, lF, Vector((x, y, random.uniform(0.8, 9.0))), random.uniform(1.5, 3.0), 260, flat=0.9)
# small leafy plants poking out of the duckweed
for _ in range(25):
    x = random.uniform(-12, 12); y = random.uniform(2, 25)
    leaf_cluster(lV, lF, Vector((x, y, 0.5)), 0.5, 60, flat=1.0)

mesh_obj("BaldCypressTrunks", tV, tF, M_BARK)
mesh_obj("Branches", bV, bF, M_BARK)
mesh_obj("Foliage", lV, lF, M_LEAF, smooth=False)
mesh_obj("CypressKnees", kV, kF, M_KNEE)

# ---------------------------------------------------------------- floating sticks & vines
sV, sF = [], []
for _ in range(P["n_sticks"]):
    x = random.uniform(-10, 10); y = random.uniform(0, 30)
    a = random.uniform(0, 6.3); L = random.uniform(0.8, 4.0)
    pitch = random.choice([0, 0, 0, random.uniform(0.15, 0.6)])     # some poke up out of the duckweed
    d = Vector((math.cos(a), math.sin(a), pitch))
    pts = branch_pts(Vector((x, y, 0.02)), d, L, droop=0.0, steps=4)
    tube(sV, sF, pts, random.uniform(0.02, 0.05), 0.01, seg=6)
    if random.random() < 0.4:   # side twig
        tube(sV, sF, branch_pts(pts[2], Vector((-d.y, d.x, 0.3)), L * 0.3, 0, 3), 0.015, 0.005)
mesh_obj("FloatingSticks", sV, sF, M_STICK)

vV, vF = [], []
for _ in range(P["n_vines"]):
    pos, p = random.choice(trunks[:40])
    top = Vector((pos[0], pos[1], random.uniform(8, p["h"] * 0.8)))
    a = random.uniform(0, 6.3); dist = random.uniform(1.5, 6)
    bot = Vector((pos[0] + dist * math.cos(a), pos[1] + dist * math.sin(a), random.uniform(-0.1, 1.5)))
    pts = []
    for i in range(13):
        t = i / 12
        c = top.lerp(bot, t)
        c.z -= math.sin(t * math.pi) * random.uniform(0.3, 0.8)
        c.x += 0.15 * math.sin(t * 9); c.y += 0.15 * math.cos(t * 7)
        pts.append(c)
    tube(vV, vF, pts, 0.018, 0.012, seg=5)
mesh_obj("HangingVines", vV, vF, M_VINE)

# ---------------------------------------------------------------- duckweed water
s = 150
wat = mesh_obj("DuckweedWater", [(-s/2, -20, 0), (s/2, -20, 0), (s/2, s - 20, 0), (-s/2, s - 20, 0)],
               [(0, 1, 2, 3)], M_DUCK, smooth=False)

# ---------------------------------------------------------------- world, light, camera
world = bpy.data.worlds.get("World" + TAG) or bpy.data.worlds.new("World" + TAG)
scene.world = world
world.use_nodes = True
wn = world.node_tree; wn.nodes.clear()
wo = wn.nodes.new("ShaderNodeOutputWorld")
bg = wn.nodes.new("ShaderNodeBackground"); bg.inputs["Strength"].default_value = 1.0
tc = wn.nodes.new("ShaderNodeTexCoord"); sep = wn.nodes.new("ShaderNodeSeparateXYZ")
ramp = wn.nodes.new("ShaderNodeValToRGB")
ramp.color_ramp.elements[0].color = (0.55, 0.62, 0.50, 1)   # green-tinted horizon bounce
ramp.color_ramp.elements[1].position = 0.6
ramp.color_ramp.elements[1].color = (0.85, 0.90, 0.92, 1)   # bright overcast
wn.links.new(tc.outputs["Generated"], sep.inputs[0])
wn.links.new(sep.outputs["Z"], ramp.inputs["Fac"])
wn.links.new(ramp.outputs["Color"], bg.inputs["Color"])
wn.links.new(bg.outputs[0], wo.inputs[0])

sun = bpy.data.objects.new("Sun" + TAG, bpy.data.lights.new("Sun" + TAG, 'SUN'))
coll.objects.link(sun)
sun.data.energy = 1.2
sun.data.color = (1.0, 0.97, 0.9)
sun.data.angle = math.radians(15)          # soft, hazy shadows
sun.rotation_euler = (math.radians(30), math.radians(10), math.radians(200))

cam = bpy.data.objects.new("Camera" + TAG, bpy.data.cameras.new("Camera" + TAG))
coll.objects.link(cam)
scene.camera = cam
cam.location = (0.0, -6.0, 3.0)
cam.rotation_euler = (math.radians(83), 0, 0)
cam.data.lens = 24
cam.data.clip_end = 300

scene.render.engine = 'BLENDER_EEVEE'
scene.render.resolution_x, scene.render.resolution_y = 1920, 1080
ee = scene.eevee
for attr, val in (("use_raytracing", True), ("use_shadows", True), ("taa_render_samples", 64)):
    if hasattr(ee, attr):
        setattr(ee, attr, val)
scene.view_settings.view_transform = 'AgX'
looks = [i.identifier for i in scene.view_settings.bl_rna.properties['look'].enum_items]
scene.view_settings.look = 'AgX - Base Contrast' if 'AgX - Base Contrast' in looks else 'None'
# soft distance haze
scene.world.mist_settings.start = 10
scene.world.mist_settings.depth = 50

print("Duckweed swamp built:", len(trunks), "trunks")

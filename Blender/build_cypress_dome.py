"""
Pond Cypress Dome generator for Blender 5.x
Builds: tea-colored reflective water with algae, buttressed pond cypress trunks,
cypress knees, Tillandsia (air plant) rosettes on trunks, sawgrass tufts,
thin upper branches, background shrubs, sky world, sun, and camera.

Run inside Blender (Scripting tab > Open > Run Script). Re-running rebuilds the scene.
Tweak the PARAMS block to change density/look.
"""
import bpy, math, random
from mathutils import Vector, Matrix, Euler

PARAMS = dict(
    seed=7,
    n_trunks=170,          # total trunks (foreground + background)
    area_x=(-16, 16),
    area_y=(1.5, 38),
    airplants_per_m=2.4,   # rosettes per metre of trunk height
    n_knees=60,
    n_grass_clumps=14,
)
random.seed(PARAMS["seed"])

# ---------------------------------------------------------------- cleanup
COLL_NAME = "CypressDome"
if COLL_NAME in bpy.data.collections:
    c = bpy.data.collections[COLL_NAME]
    for o in list(c.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    bpy.data.collections.remove(c)
for n in ("Cube",):
    if n in bpy.data.objects:
        bpy.data.objects.remove(bpy.data.objects[n], do_unlink=True)
for m in list(bpy.data.meshes):
    if m.users == 0:
        bpy.data.meshes.remove(m)

coll = bpy.data.collections.new(COLL_NAME)
bpy.context.scene.collection.children.link(coll)


def mesh_obj(name, verts, faces, mat=None, smooth=True):
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [], faces)
    me.update()
    if smooth:
        me.shade_smooth()
    ob = bpy.data.objects.new(name, me)
    coll.objects.link(ob)
    if mat:
        me.materials.append(mat)
    return ob

# ---------------------------------------------------------------- materials
def new_mat(name):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    out.location = (600, 0)
    return m, nt, out


def mat_bark():
    m, nt, out = new_mat("CypressBark")
    N, L = nt.nodes, nt.links
    bsdf = N.new("ShaderNodeBsdfPrincipled"); bsdf.location = (300, 0)
    tc = N.new("ShaderNodeTexCoord"); tc.location = (-1200, 0)
    mp = N.new("ShaderNodeMapping"); mp.location = (-1000, 0)
    mp.inputs["Scale"].default_value = (5, 5, 0.9)  # vertical streaks
    noise = N.new("ShaderNodeTexNoise"); noise.location = (-800, 100)
    noise.inputs["Scale"].default_value = 3.0
    noise.inputs["Detail"].default_value = 10
    noise.inputs["Roughness"].default_value = 0.65
    wave = N.new("ShaderNodeTexWave"); wave.location = (-800, -150)
    wave.wave_type = 'BANDS'; wave.bands_direction = 'X'
    wave.inputs["Scale"].default_value = 2.0
    wave.inputs["Distortion"].default_value = 8
    wave.inputs["Detail"].default_value = 6
    L.new(tc.outputs["Object"], mp.inputs["Vector"])
    L.new(mp.outputs["Vector"], noise.inputs["Vector"])
    L.new(mp.outputs["Vector"], wave.inputs["Vector"])
    mixn = N.new("ShaderNodeMix"); mixn.data_type = 'FLOAT'; mixn.location = (-550, 0)
    mixn.inputs[0].default_value = 0.1
    L.new(noise.outputs["Fac"], mixn.inputs[2]); L.new(wave.outputs["Fac"], mixn.inputs[3])
    ramp = N.new("ShaderNodeValToRGB"); ramp.location = (-350, 150)
    ramp.color_ramp.elements[0].position = 0.3
    ramp.color_ramp.elements[0].color = (0.07, 0.055, 0.04, 1)   # brown fissures
    ramp.color_ramp.elements[1].position = 0.62
    ramp.color_ramp.elements[1].color = (0.48, 0.47, 0.44, 1)   # pale grey bark
    L.new(mixn.outputs[0], ramp.inputs["Fac"])
    # water-stain band near the base (world Z)
    geo = N.new("ShaderNodeNewGeometry"); geo.location = (-800, -450)
    sep = N.new("ShaderNodeSeparateXYZ"); sep.location = (-600, -450)
    L.new(geo.outputs["Position"], sep.inputs[0])
    mr = N.new("ShaderNodeMapRange"); mr.location = (-400, -450)
    mr.inputs["From Min"].default_value = 0.6
    mr.inputs["From Max"].default_value = 2.2
    L.new(sep.outputs["Z"], mr.inputs["Value"])
    stain = N.new("ShaderNodeMix"); stain.data_type = 'RGBA'; stain.location = (-100, 100)
    stain.inputs[7].default_value = (0.20, 0.16, 0.11, 1)  # A: dark wet brown
    L.new(mr.outputs[0], stain.inputs[0])
    L.new(ramp.outputs["Color"], stain.inputs[7])
    # A=stain colour, B=bark; factor 0 at base -> stain
    stain.inputs[6].default_value = (0.06, 0.045, 0.03, 1)
    L.new(ramp.outputs["Color"], stain.inputs[7])
    L.new(stain.outputs[2], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.85
    bump = N.new("ShaderNodeBump"); bump.location = (100, -250)
    bump.inputs["Strength"].default_value = 0.6
    L.new(mixn.outputs[0], bump.inputs["Height"])
    L.new(bump.outputs["Normal"], bsdf.inputs["Normal"])
    L.new(bsdf.outputs[0], out.inputs[0])
    return m


def mat_simple(name, color, rough=0.6, sss=0.0, translucent=0.0, var=None):
    m, nt, out = new_mat(name)
    N, L = nt.nodes, nt.links
    bsdf = N.new("ShaderNodeBsdfPrincipled")
    bsdf.inputs["Base Color"].default_value = (*color, 1)
    bsdf.inputs["Roughness"].default_value = rough
    if var:
        # per-object/per-island colour variation using noise on object coords
        tc = N.new("ShaderNodeTexCoord"); tc.location = (-700, 0)
        nz = N.new("ShaderNodeTexNoise"); nz.location = (-500, 0)
        nz.inputs["Scale"].default_value = 1.5
        L.new(tc.outputs["Object"], nz.inputs["Vector"])
        rp = N.new("ShaderNodeValToRGB"); rp.location = (-300, 0)
        rp.color_ramp.elements[0].color = (*var[0], 1)
        rp.color_ramp.elements[1].color = (*var[1], 1)
        L.new(nz.outputs["Fac"], rp.inputs["Fac"])
        L.new(rp.outputs["Color"], bsdf.inputs["Base Color"])
    if translucent > 0:
        tr = N.new("ShaderNodeBsdfTranslucent")
        tr.inputs["Color"].default_value = (*color, 1)
        mx = N.new("ShaderNodeMixShader")
        mx.inputs[0].default_value = translucent
        L.new(bsdf.outputs[0], mx.inputs[1]); L.new(tr.outputs[0], mx.inputs[2])
        L.new(mx.outputs[0], out.inputs[0])
    else:
        L.new(bsdf.outputs[0], out.inputs[0])
    return m


def mat_water():
    m, nt, out = new_mat("SwampWater")
    N, L = nt.nodes, nt.links
    tc = N.new("ShaderNodeTexCoord"); tc.location = (-1200, 0)
    # glassy reflective surface
    water = N.new("ShaderNodeBsdfPrincipled"); water.location = (0, 200)
    water.inputs["Base Color"].default_value = (0.012, 0.012, 0.006, 1)
    water.inputs["Roughness"].default_value = 0.02
    water.inputs["IOR"].default_value = 1.33
    # submerged algae / periphyton mats seen through the water
    n1 = N.new("ShaderNodeTexNoise"); n1.location = (-900, -200)
    n1.inputs["Scale"].default_value = 0.9
    n1.inputs["Detail"].default_value = 12
    n1.inputs["Roughness"].default_value = 0.7
    L.new(tc.outputs["Object"], n1.inputs["Vector"])
    v = N.new("ShaderNodeTexVoronoi"); v.location = (-900, -450)
    v.inputs["Scale"].default_value = 18
    L.new(tc.outputs["Object"], v.inputs["Vector"])
    mx = N.new("ShaderNodeMath"); mx.operation = 'MULTIPLY'; mx.location = (-650, -300)
    L.new(n1.outputs["Fac"], mx.inputs[0]); L.new(v.outputs["Distance"], mx.inputs[1])
    mask = N.new("ShaderNodeValToRGB"); mask.location = (-450, -300)
    mask.color_ramp.elements[0].position = 0.08
    mask.color_ramp.elements[1].position = 0.28
    L.new(n1.outputs["Fac"], mask.inputs["Fac"])
    algae_col = N.new("ShaderNodeValToRGB"); algae_col.location = (-450, -600)
    algae_col.color_ramp.elements[0].color = (0.025, 0.02, 0.005, 1)
    algae_col.color_ramp.elements[1].color = (0.09, 0.075, 0.018, 1)
    L.new(v.outputs["Distance"], algae_col.inputs["Fac"])
    algae = N.new("ShaderNodeBsdfPrincipled"); algae.location = (0, -300)
    algae.inputs["Roughness"].default_value = 0.08   # still wet/specular on top
    algae.inputs["IOR"].default_value = 1.33
    L.new(algae_col.outputs["Color"], algae.inputs["Base Color"])
    fac = N.new("ShaderNodeMath"); fac.operation = 'MULTIPLY'; fac.location = (-200, -150)
    fac.inputs[1].default_value = 0.7
    L.new(mask.outputs["Color"], fac.inputs[0])
    mix = N.new("ShaderNodeMixShader"); mix.location = (300, 0)
    L.new(fac.outputs[0], mix.inputs[0])
    L.new(water.outputs[0], mix.inputs[1]); L.new(algae.outputs[0], mix.inputs[2])
    # very subtle ripples
    rn = N.new("ShaderNodeTexNoise"); rn.location = (-400, 350)
    rn.inputs["Scale"].default_value = 25
    L.new(tc.outputs["Object"], rn.inputs["Vector"])
    bump = N.new("ShaderNodeBump"); bump.location = (-200, 350)
    bump.inputs["Strength"].default_value = 0.02
    L.new(rn.outputs["Fac"], bump.inputs["Height"])
    L.new(bump.outputs["Normal"], water.inputs["Normal"])
    L.new(mix.outputs[0], out.inputs[0])
    return m


M_BARK = mat_bark()
M_WATER = mat_water()
M_TILL = mat_simple("Tillandsia", (0.22, 0.24, 0.16), 0.5, translucent=0.2,
                    var=((0.20, 0.24, 0.15), (0.33, 0.31, 0.22)))
M_GRASS = mat_simple("Sawgrass", (0.15, 0.25, 0.06), 0.5, translucent=0.3,
                     var=((0.12, 0.24, 0.05), (0.32, 0.33, 0.12)))
M_SHRUB = mat_simple("Shrub", (0.05, 0.12, 0.02), 0.5, translucent=0.2,
                     var=((0.03, 0.09, 0.015), (0.10, 0.20, 0.03)))
M_KNEE = M_BARK

# ---------------------------------------------------------------- trunk geometry
def trunk_radius(z, p, ang):
    """Radius of a trunk at height z (water line z=0)."""
    t = max(z, 0) / p["h"]
    r = p["r"] * (1 - 0.65 * t ** 0.8) + 0.004
    flare = p["flare"] * math.exp(-max(z, -0.3) / p["flare_h"])
    flute = 1 + 0.22 * math.exp(-max(z, 0) / (p["flare_h"] * 1.2)) * (
        math.sin(p["k"] * ang + p["ph"]) * 0.6 + math.sin((p["k"] + 2) * ang + 1.7 * p["ph"]) * 0.4)
    return (r + flare) * flute


def trunk_offset(z, p):
    z = max(z, 0.0)
    lx = p["lean"][0] * (z / p["h"]) ** 1.5 * p["h"] + 0.06 * math.sin(z * 0.7 + p["ph"])
    ly = p["lean"][1] * (z / p["h"]) ** 1.5 * p["h"] + 0.06 * math.cos(z * 0.5 + p["ph"])
    return lx, ly


def build_trunk(verts, faces, pos, p, seg=14, rings=34):
    base = len(verts)
    z0 = -0.6
    for i in range(rings + 1):
        t = i / rings
        z = z0 + (p["h"] - z0) * t ** 1.7
        ox, oy = trunk_offset(z, p)
        for j in range(seg):
            a = 2 * math.pi * j / seg
            r = trunk_radius(z, p, a)
            verts.append((pos[0] + ox + r * math.cos(a), pos[1] + oy + r * math.sin(a), z))
    for i in range(rings):
        for j in range(seg):
            a = base + i * seg + j
            b = base + i * seg + (j + 1) % seg
            faces.append((a, b, b + seg, a + seg))
    top = len(verts)
    ox, oy = trunk_offset(p["h"], p)
    verts.append((pos[0] + ox, pos[1] + oy, p["h"] + 0.05))
    for j in range(seg):
        faces.append((base + rings * seg + j, base + rings * seg + (j + 1) % seg, top))


def add_branch(verts, faces, start, direction, length, r0, seg=5, rings=5):
    base = len(verts)
    d = direction.normalized()
    up = Vector((0, 0, 1)) if abs(d.z) < 0.95 else Vector((1, 0, 0))
    u = d.cross(up).normalized(); w = d.cross(u).normalized()
    for i in range(rings + 1):
        t = i / rings
        c = start + d * length * t + Vector((0, 0, -0.15 * length * t * t))
        r = r0 * (1 - 0.85 * t) + 0.003
        for j in range(seg):
            a = 2 * math.pi * j / seg
            verts.append(tuple(c + (u * math.cos(a) + w * math.sin(a)) * r))
    for i in range(rings):
        for j in range(seg):
            a = base + i * seg + j; b = base + i * seg + (j + 1) % seg
            faces.append((a, b, b + seg, a + seg))


# ---------------------------------------------------------------- air plant rosette
def rosette_template(n_leaves, length, seed):
    rnd = random.Random(seed)
    V, F = [], []
    for k in range(n_leaves):
        az = 2 * math.pi * k / n_leaves + rnd.uniform(-0.2, 0.2)
        el = rnd.uniform(0.25, 1.35)                  # elevation from horizontal
        L = length * rnd.uniform(0.55, 1.0)
        droop = rnd.uniform(0.2, 0.9)
        steps = 5
        b = len(V)
        for s in range(steps + 1):
            t = s / steps
            e = el - droop * t * t                      # curve downward toward tip
            rr = L * t
            cx = rr * math.cos(e) * math.cos(az)
            cy = rr * math.cos(e) * math.sin(az)
            cz = rr * math.sin(e) * 0.9 + 0.02
            wdt = 0.016 * (1 - t) + 0.0008
            px, py = -math.sin(az) * wdt, math.cos(az) * wdt
            V.append((cx + px, cy + py, cz)); V.append((cx - px, cy - py, cz))
        for s in range(steps):
            a = b + 2 * s
            F.append((a, a + 1, a + 3, a + 2))
    return V, F


ROSETTES = [rosette_template(random.randint(30, 46), random.uniform(0.28, 0.45), i) for i in range(6)]


def place_template(V, F, tpl, mat4):
    b = len(V)
    tv, tf = tpl
    for v in tv:
        V.append(tuple(mat4 @ Vector(v)))
    for f in tf:
        F.append(tuple(i + b for i in f))


# ---------------------------------------------------------------- scatter trunks
trunk_V, trunk_F = [], []
branch_V, branch_F = [], []
air_V, air_F = [], []
knee_V, knee_F = [], []
trunks = []

# hero trunks near camera, then random fill
hero = [((0.4, 3.2), 0.22, 0.45), ((-3.2, 4.5), 0.15, 0.28), ((3.6, 5.0), 0.14, 0.25),
        ((5.8, 3.8), 0.17, 0.35), ((-6.0, 3.5), 0.14, 0.25), ((-1.4, 7.5), 0.12, 0.18)]
spots = [(h[0], h[1], h[2]) for h in hero]
attempts = 0
while len(spots) < PARAMS["n_trunks"] and attempts < 20000:
    attempts += 1
    x = random.uniform(*PARAMS["area_x"]); y = random.uniform(*PARAMS["area_y"])
    # widen with distance so the background stays filled in frame
    x *= 0.6 + y / 25
    if any((x - s[0][0]) ** 2 + (y - s[0][1]) ** 2 < 0.9 ** 2 for s in spots):
        continue
    r = random.uniform(0.05, 0.13)
    spots.append(((x, y), r, r * random.uniform(0.6, 1.8)))

for (pos, r, flare) in spots:
    p = dict(h=random.uniform(13, 19), r=r, flare=flare, flare_h=random.uniform(0.6, 1.3),
             k=random.choice([3, 4, 5]), ph=random.uniform(0, 6.28),
             lean=(random.gauss(0, 0.03), random.gauss(0, 0.03)))
    build_trunk(trunk_V, trunk_F, pos, p)
    trunks.append((pos, p))

    # thin upper branches
    for _ in range(random.randint(3, 7)):
        z = random.uniform(p["h"] * 0.55, p["h"] * 0.95)
        ox, oy = trunk_offset(z, p)
        a = random.uniform(0, 6.28)
        d = Vector((math.cos(a), math.sin(a), random.uniform(0.6, 1.6)))
        add_branch(branch_V, branch_F, Vector((pos[0] + ox, pos[1] + oy, z)), d,
                   random.uniform(1.0, 3.0), r * 0.25)

    # air plants on the trunk
    n_air = int(p["h"] * PARAMS["airplants_per_m"] * random.uniform(0.6, 1.3))
    for _ in range(n_air):
        z = random.uniform(1.4, p["h"] - 0.3) ** 1.0
        a = random.uniform(0, 6.28)
        rr = trunk_radius(z, p, a)
        ox, oy = trunk_offset(z, p)
        c = Vector((pos[0] + ox + rr * math.cos(a), pos[1] + oy + rr * math.sin(a), z))
        s = random.uniform(0.75, 1.4)
        tilt = Euler((random.uniform(-0.25, 0.25), random.uniform(0.15, 0.55), a), 'XYZ')
        mat4 = Matrix.Translation(c) @ tilt.to_matrix().to_4x4() @ Matrix.Scale(s, 4)
        place_template(air_V, air_F, random.choice(ROSETTES), mat4)

    # cypress knees around the base
    if random.random() < 0.55:
        for _ in range(random.randint(1, 5)):
            a = random.uniform(0, 6.28); d = random.uniform(0.6, 1.8)
            kx, ky = pos[0] + d * math.cos(a), pos[1] + d * math.sin(a)
            kh = random.uniform(0.1, 0.4); kr = random.uniform(0.04, 0.08)
            b = len(knee_V); seg = 8
            for i, (zz, rf) in enumerate([(-0.3, 1.3), (0.0, 1.0), (kh * 0.7, 0.8), (kh, 0.45)]):
                for j in range(seg):
                    aa = 2 * math.pi * j / seg
                    jitter = 1 + 0.15 * math.sin(3 * aa + i)
                    knee_V.append((kx + kr * rf * jitter * math.cos(aa), ky + kr * rf * jitter * math.sin(aa), zz))
            for i in range(3):
                for j in range(seg):
                    q = b + i * seg + j; w = b + i * seg + (j + 1) % seg
                    knee_F.append((q, w, w + seg, q + seg))
            top = len(knee_V); knee_V.append((kx, ky, kh + 0.03))
            for j in range(seg):
                knee_F.append((b + 3 * seg + j, b + 3 * seg + (j + 1) % seg, top))

mesh_obj("CypressTrunks", trunk_V, trunk_F, M_BARK)
mesh_obj("CypressBranches", branch_V, branch_F, M_BARK)
mesh_obj("AirPlants", air_V, air_F, M_TILL)
mesh_obj("CypressKnees", knee_V, knee_F, M_KNEE)

# ---------------------------------------------------------------- sawgrass tufts
gV, gF = [], []
def grass_clump(cx, cy, n, height, spread):
    for _ in range(n):
        a = random.uniform(0, 6.28); rr = random.uniform(0, spread)
        bx, by = cx + rr * math.cos(a), cy + rr * math.sin(a)
        h = height * random.uniform(0.5, 1.1)
        lean_a = random.uniform(0, 6.28); lean = random.uniform(0.1, 0.6) * h
        w = random.uniform(0.008, 0.016)
        b = len(gV); steps = 4
        for s in range(steps + 1):
            t = s / steps
            x = bx + math.cos(lean_a) * lean * t * t
            y = by + math.sin(lean_a) * lean * t * t
            z = -0.05 + h * t
            ww = w * (1 - t) + 0.0005
            gV.append((x - math.sin(lean_a) * ww, y + math.cos(lean_a) * ww, z))
            gV.append((x + math.sin(lean_a) * ww, y - math.cos(lean_a) * ww, z))
        for s in range(steps):
            q = b + 2 * s
            gF.append((q, q + 1, q + 3, q + 2))

grass_clump(0.9, 2.6, 380, 0.9, 0.9)     # the tuft at the hero trunk
for _ in range(PARAMS["n_grass_clumps"]):
    pos, p = random.choice(trunks[:40])
    grass_clump(pos[0] + random.uniform(-1, 1), pos[1] + random.uniform(-1, 1),
                random.randint(60, 180), random.uniform(0.5, 1.0), 0.5)
# edge vegetation on the right
for _ in range(10):
    grass_clump(random.uniform(8, 14), random.uniform(4, 14), 150, 1.2, 0.8)
mesh_obj("Sawgrass", gV, gF, M_GRASS)

# ---------------------------------------------------------------- background shrubs
# clusters of small lumpy spheres = leafy understory (wax myrtle / pond apple feel)
bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=4, radius=1.0)
_base = bpy.context.active_object
shrub_mesh = _base.data; shrub_mesh.name = "ShrubBlob"
bpy.data.objects.remove(_base, do_unlink=True)
shrub_mesh.materials.append(M_SHRUB)
shrub_mesh.shade_smooth()
tex = bpy.data.textures.get("ShrubNoise") or bpy.data.textures.new("ShrubNoise", 'VORONOI')
tex.noise_scale = 0.12
for i in range(14):
    side = random.choice([1, 1, -1])
    cx = side * random.uniform(8, 15); cy = random.uniform(5, 20)
    for k in range(random.randint(6, 12)):
        sh = bpy.data.objects.new(f"Shrub_{i:02d}_{k:02d}", shrub_mesh)
        coll.objects.link(sh)
        rad = random.uniform(0.3, 0.7)
        sh.location = (cx + random.uniform(-1.2, 1.2), cy + random.uniform(-1, 1), random.uniform(0.2, 2.2))
        sh.scale = (rad * random.uniform(1, 1.4), rad * random.uniform(1, 1.4), rad)
        sh.rotation_euler = (random.uniform(0, 6), random.uniform(0, 6), random.uniform(0, 6))
        d = sh.modifiers.new("Displace", 'DISPLACE'); d.texture = tex; d.strength = 0.25
        d.texture_coords = 'GLOBAL'

# ---------------------------------------------------------------- water
bpy.ops.mesh.primitive_plane_add(size=120, location=(0, 20, 0))
water = bpy.context.active_object
water.name = "SwampWater"
for c in water.users_collection:
    c.objects.unlink(water)
coll.objects.link(water)
water.data.materials.append(M_WATER)

# ---------------------------------------------------------------- world / sky
scene = bpy.context.scene
world = scene.world or bpy.data.worlds.new("World")
scene.world = world
world.use_nodes = True
wn = world.node_tree; wn.nodes.clear()
wo = wn.nodes.new("ShaderNodeOutputWorld")
bg = wn.nodes.new("ShaderNodeBackground"); bg.inputs["Strength"].default_value = 0.9
tc = wn.nodes.new("ShaderNodeTexCoord")
sep = wn.nodes.new("ShaderNodeSeparateXYZ")
ramp = wn.nodes.new("ShaderNodeValToRGB")
ramp.color_ramp.elements[0].position = 0.0
ramp.color_ramp.elements[0].color = (0.70, 0.80, 0.93, 1)   # hazy horizon
ramp.color_ramp.elements[1].position = 0.6
ramp.color_ramp.elements[1].color = (0.12, 0.32, 0.80, 1)   # blue zenith
wn.links.new(tc.outputs["Generated"], sep.inputs[0])
wn.links.new(sep.outputs["Z"], ramp.inputs["Fac"])
wn.links.new(ramp.outputs["Color"], bg.inputs["Color"])
wn.links.new(bg.outputs[0], wo.inputs[0])

# sun
sun = bpy.data.objects.get("Light")
if sun is None:
    sun = bpy.data.objects.new("Light", bpy.data.lights.new("Light", 'SUN'))
    scene.collection.objects.link(sun)
sun.data.type = 'SUN'
sun.data.energy = 3.0
sun.data.color = (1.0, 0.95, 0.86)
sun.data.angle = math.radians(2)
sun.rotation_euler = (math.radians(45), math.radians(-20), math.radians(150))

# camera
cam = bpy.data.objects.get("Camera")
if cam is None:
    cam = bpy.data.objects.new("Camera", bpy.data.cameras.new("Camera"))
    scene.collection.objects.link(cam)
scene.camera = cam
cam.location = (0.2, -4.0, 2.2)
cam.rotation_euler = (math.radians(86), 0, 0)
cam.data.lens = 26
cam.data.clip_end = 300

# render settings
scene.render.resolution_x, scene.render.resolution_y = 1920, 1280
if scene.render.engine.startswith("BLENDER_EEVEE"):
    ee = scene.eevee
    for attr, val in (("use_raytracing", True), ("use_shadows", True), ("taa_render_samples", 64)):
        if hasattr(ee, attr):
            setattr(ee, attr, val)
scene.view_settings.view_transform = 'AgX'
scene.view_settings.look = 'AgX - Medium High Contrast' if 'AgX - Medium High Contrast' in [
    i.identifier for i in scene.view_settings.bl_rna.properties['look'].enum_items] else 'None'

print("Cypress dome built:", len(trunks), "trunks")

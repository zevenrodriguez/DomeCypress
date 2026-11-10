"""
Lush layer for the cypress dome (Blender 5.x, scene "HybridSwamp").
Adds, as alpha-textured cards so it stays real-time friendly in Unity:
  - Cypress_CanopyExtra  : denser canopy (extra spray cards around the existing foliage)
  - Cypress_SpanishMoss  : grey Spanish moss hanging from the upper branches
  - Cypress_Shrubs       : pond apple / coco plum understory shrubs on the hummocks
  - Cypress_FernCards    : sword/Boston fern clumps on the hummocks and trunk bases, big leather ferns at the waterline
  - Cypress_KneesExtra   : extra knobby cypress knees around the trunks
  - Cypress_YoungTrunks / Cypress_YoungBranches / Cypress_YoungFoliage : small and medium cypress
  - Cypress_Pickerelweed : pickerelweed with violet flower spikes in the shallows
  - Cypress_FloatingLitter / Cypress_SunkenLitter : textured fallen leaves on and just under the water
Then exports them to the Unity project as Assets/Cypress/Models/CypressLush.fbx (+ understory atlas).
Re-running rebuilds the layer. Run build_hybrid_swamp.py first.
"""
import bpy, math, os, random, shutil
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree

L = dict(
    seed=31,
    canopy_extra=1.6,        # extra canopy cards, as a fraction of the existing leaf cards
    branch_sprays=38000,     # sprays hanging all along the branches (not only at the tips)
    trunk_sprigs=9000,       # short leafy sprigs sprouting from the upper trunks
    moss_strands=2600,       # Spanish moss curtains
    shrubs=260,
    fern_clumps=1600,
    trunk_base_ferns=260,    # fern clumps nestled against cypress trunk bases
    leather_ferns=260,
    extra_knees=650,         # additional cypress knees, clustered around trunks and existing knees
    young_small=16,          # young cypress 2-4.5 m (conical, branches to the ground)
    young_medium=11,         # medium cypress 6-10 m
    pickerel_clumps=260,
    floating_leaves=7000,    # fallen leaves / cypress sprigs floating on the water
    submerged_leaves=2500,   # waterlogged leaves sinking just under the surface
    export=True,
)
globals().update(globals().get("OVERRIDES", {}))
random.seed(L["seed"])

SCENE, TAG = "HybridSwamp", "_hy"
BLEND_DIR = os.path.dirname(bpy.data.filepath)
def _unity_project(blend_dir):
    """The Unity project folder 'domecypress': next to the .blend, or one folder up
    (so the scripts work with the .blend in DomeCypress/ or in DomeCypress/Blender/)."""
    d = blend_dir
    for _ in range(3):
        if os.path.isdir(os.path.join(d, "domecypress", "Assets")):
            return os.path.join(d, "domecypress")
        d = os.path.dirname(d)
    return os.path.join(blend_dir, "domecypress")

UNITY_DIR = os.path.join(_unity_project(BLEND_DIR), "Assets", "Cypress")
ATLAS2 = os.path.join(BLEND_DIR, "textures", "swamp_understory_atlas.png")
ATLAS3 = os.path.join(BLEND_DIR, "textures", "floating_litter_atlas.png")
PONDBED = os.path.join(BLEND_DIR, "textures", "pondbed_litter.png")
DOME_C, DOME_R, POND_R = Vector((0.0, 8.0, 0.0)), 18.0, 6.0

scene = bpy.data.scenes[SCENE]
for w in bpy.context.window_manager.windows:
    w.scene = scene
CNAME = "LushLayer" + TAG
if CNAME in bpy.data.collections:
    c = bpy.data.collections[CNAME]
    for o in list(c.objects):
        me = o.data; bpy.data.objects.remove(o, do_unlink=True)
        if me and me.users == 0: bpy.data.meshes.remove(me)
    bpy.data.collections.remove(c)
coll = bpy.data.collections.new(CNAME)
scene.collection.children.link(coll)

# ---------------------------------------------------------------- helpers
gme = bpy.data.objects["PeatHummocks" + TAG].data
bvh = BVHTree.FromPolygons([v.co.copy() for v in gme.vertices], [tuple(p.vertices) for p in gme.polygons])
DOWN = Vector((0, 0, -1))


def ground_h(x, y):
    loc, *_ = bvh.ray_cast(Vector((x, y, 5.0)), DOWN)
    return loc.z if loc is not None else -0.45


def in_ring(r0, r1):
    a = random.uniform(0, 2 * math.pi); r = math.sqrt(random.uniform(r0 * r0, r1 * r1))
    return DOME_C.x + r * math.cos(a), DOME_C.y + r * math.sin(a)


CELL = {0: (0.0, 0.5), 1: (0.5, 0.5), 2: (0.0, 0.0), 3: (0.5, 0.0)}   # (u0, v0) of each 2x2 atlas cell


class Cards:
    def __init__(self):
        self.V, self.F, self.UV = [], [], []

    def card(self, base, up, side, h, w, cell, hang=False, flip=None):
        """Quad whose bottom edge centre is `base` (or top edge when hang=True), extending along `up`."""
        u0, v0 = CELL[cell]
        if flip is None:
            flip = random.random() < 0.5
        us = (u0 + 0.5, u0) if flip else (u0, u0 + 0.5)
        if hang:            # texture top edge sits at the attach point, card hangs down
            b = base - up * h
        else:
            b = base
        n = len(self.V)
        self.V += [tuple(b - side * w * 0.5), tuple(b + side * w * 0.5),
                   tuple(b + side * w * 0.5 + up * h), tuple(b - side * w * 0.5 + up * h)]
        self.F.append((n, n + 1, n + 2, n + 3))
        self.UV += [(us[0], v0), (us[1], v0), (us[1], v0 + 0.5), (us[0], v0 + 0.5)]

    def obj(self, name, mat):
        me = bpy.data.meshes.new(name)
        me.from_pydata(self.V, [], self.F); me.update()
        uvl = me.uv_layers.new(name="UVMap")
        uvl.data.foreach_set("uv", [c for uv in self.UV for c in uv])
        me.materials.append(mat)
        ob = bpy.data.objects.new(name, me); coll.objects.link(ob)
        return ob


def rand_dir_h():
    a = random.uniform(0, 2 * math.pi)
    return Vector((math.cos(a), math.sin(a), 0.0))


def tilted(dir_h, tilt):
    """Unit vector leaning `tilt` radians from vertical toward dir_h."""
    return (Vector((0, 0, 1)) * math.cos(tilt) + dir_h * math.sin(tilt)).normalized()


# ---------------------------------------------------------------- materials (Blender preview)
def card_material(name, img_path, translucent=0.35):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree; nt.nodes.clear(); N, Lk = nt.nodes, nt.links
    out = N.new("ShaderNodeOutputMaterial")
    img = bpy.data.images.load(img_path, check_existing=True)
    if not img.packed_file:
        img.pack()
    uv = N.new("ShaderNodeUVMap"); uv.uv_map = "UVMap"
    tex = N.new("ShaderNodeTexImage"); tex.image = img
    Lk.new(uv.outputs["UV"], tex.inputs["Vector"])
    bsdf = N.new("ShaderNodeBsdfPrincipled"); bsdf.inputs["Roughness"].default_value = 0.55
    Lk.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    tr = N.new("ShaderNodeBsdfTranslucent"); Lk.new(tex.outputs["Color"], tr.inputs["Color"])
    leaf = N.new("ShaderNodeMixShader"); leaf.inputs[0].default_value = translucent
    Lk.new(bsdf.outputs[0], leaf.inputs[1]); Lk.new(tr.outputs[0], leaf.inputs[2])
    clip = N.new("ShaderNodeMath"); clip.operation = 'GREATER_THAN'; clip.inputs[1].default_value = 0.5
    Lk.new(tex.outputs["Alpha"], clip.inputs[0])
    mx = N.new("ShaderNodeMixShader"); transp = N.new("ShaderNodeBsdfTransparent")
    Lk.new(clip.outputs[0], mx.inputs[0]); Lk.new(transp.outputs[0], mx.inputs[1]); Lk.new(leaf.outputs[0], mx.inputs[2])
    Lk.new(mx.outputs[0], out.inputs[0])
    m.use_backface_culling = False
    for attr, val in (("surface_render_method", 'DITHERED'), ("use_transparent_shadow", True)):
        try: setattr(m, attr, val)
        except Exception: pass
    return m


M_CANOPY = bpy.data.materials["CypressLeafCards" + TAG]
M_UNDER = card_material("SwampUnderstory" + TAG, ATLAS2)
M_LITTER = card_material("FloatingLitter" + TAG, ATLAS3, translucent=0.1)

# ---------------------------------------------------------------- 1. denser canopy + Spanish moss
fol = bpy.data.objects["Foliage" + TAG].data
centers = [p.center.copy() for p in fol.polygons]
canopy = Cards()
for c in random.choices(centers, k=int(len(centers) * L["canopy_extra"])):   # may exceed 1.0x
    o = Vector((random.gauss(0, 0.85), random.gauss(0, 0.85), random.gauss(0, 0.45)))   # wider, fuller crowns
    base = c + o
    up = (Vector((o.x, o.y, 0)).normalized() if o.xy.length > 1e-3 else rand_dir_h()) * 0.8 + Vector((0, 0, random.uniform(-0.9, 0.2)))
    up.normalize()
    side = up.cross(Vector((random.gauss(0, 1), random.gauss(0, 1), random.gauss(0, 1)))).normalized()
    h = random.uniform(0.6, 1.2)
    canopy.card(base, up, side, h, h * 0.5, random.choice((0, 1, 2, 2)))   # extra weight on the darker spray

# sprays all along the branches: cypress foliage hangs in drooping curtains from every twig
bme = bpy.data.objects["Branches" + TAG].data
bverts = [v.co.copy() for v in bme.vertices if v.co.z > 3.0]
for _ in range(L["branch_sprays"] if bverts else 0):
    b = random.choice(bverts) + Vector((random.gauss(0, 0.15), random.gauss(0, 0.15), random.gauss(0, 0.1)))
    d = rand_dir_h()
    up = (d * random.uniform(0.4, 0.9) + Vector((0, 0, random.uniform(-0.95, 0.1)))).normalized()
    side = up.cross(Vector((random.gauss(0, 1), random.gauss(0, 1), random.gauss(0, 1)))).normalized()
    h = random.uniform(0.5, 1.1)
    canopy.card(b, up, side, h, h * 0.5, random.choice((0, 1, 1, 2, 2)))

# epicormic sprigs on the upper trunks (pond cypress get the thread-like cell 3)
for nm, cells in (("BaldCypressTrunks" + TAG, (0, 1, 2)), ("PondCypressTrunks" + TAG, (3, 3, 1))):
    tme = bpy.data.objects[nm].data
    tv = [(v.co.copy(), v.normal.copy()) for v in tme.vertices if v.co.z > 5.0]
    for _ in range(L["trunk_sprigs"] // 2 if tv else 0):
        co, n = random.choice(tv)
        out = Vector((n.x, n.y, 0))
        out = out.normalized() if out.length > 1e-3 else rand_dir_h()
        up = (out * random.uniform(0.5, 0.9) + Vector((0, 0, random.uniform(-0.4, 0.5)))).normalized()
        side = up.cross(Vector((0, 0, 1))).normalized() if abs(up.z) < 0.95 else Vector((1, 0, 0))
        h = random.uniform(0.35, 0.8)
        canopy.card(co, up, side, h, h * 0.5, random.choice(cells))

canopy.obj("Cypress_CanopyExtra", M_CANOPY)

high = [c for c in centers if c.z > 6.0]
moss = Cards()
for c in random.sample(high, min(L["moss_strands"], len(high))):
    top = c + Vector((random.gauss(0, 0.3), random.gauss(0, 0.3), -0.2))
    h = random.uniform(1.2, 3.2); w = h * random.uniform(0.4, 0.6)
    d = rand_dir_h(); side = Vector((-d.y, d.x, 0))
    down = Vector((random.gauss(0, 0.08), random.gauss(0, 0.08), 1.0)).normalized()   # hang with a slight sway
    moss.card(top, down, side, h, w, 0, hang=True)
    moss.card(top, down, d, h * random.uniform(0.8, 1.0), w, 0, hang=True)          # crossed for volume
moss.obj("Cypress_SpanishMoss", M_UNDER)

# ---------------------------------------------------------------- 2. understory shrubs on the hummocks
shrubs = Cards(); placed = 0; tries = 0
while placed < L["shrubs"] and tries < L["shrubs"] * 40:
    tries += 1
    x, y = in_ring(POND_R + 0.5, DOME_R + 1.0)
    z = ground_h(x, y)
    if z < 0.02:
        continue
    scale = random.uniform(0.9, 2.6)
    for _ in range(random.randint(6, 11)):
        d = rand_dir_h()
        up = tilted(d, random.uniform(0.0, 0.55))
        side = Vector((-d.y, d.x, 0))
        b = Vector((x, y, z - 0.05)) + d * random.uniform(0, 0.25 * scale)
        h = scale * random.uniform(0.7, 1.0)
        shrubs.card(b, up, side, h, h * 0.7, 2)
    placed += 1
shrubs.obj("Cypress_Shrubs", M_UNDER)

# ---------------------------------------------------------------- 3. ferns
ferns = Cards()


def fern_clump(x, y, z, scale, fronds):
    for _ in range(fronds):
        d = rand_dir_h()
        up = tilted(d, random.uniform(0.45, 1.05))      # arching outward
        side = Vector((-d.y, d.x, 0))
        h = scale * random.uniform(0.75, 1.0)
        ferns.card(Vector((x, y, z - 0.03)), up, side, h, h * 0.55, 1)


placed = tries = 0
while placed < L["fern_clumps"] and tries < L["fern_clumps"] * 40:
    tries += 1
    x, y = in_ring(POND_R, DOME_R + 2.0)
    z = ground_h(x, y)
    if z < 0.0:
        continue
    fern_clump(x, y, z, random.uniform(0.5, 1.1), random.randint(7, 13)); placed += 1
placed = tries = 0
while placed < L["leather_ferns"] and tries < L["leather_ferns"] * 60:     # big leather ferns at the waterline
    tries += 1
    x, y = in_ring(POND_R - 0.5, DOME_R + 3.0)
    z = ground_h(x, y)
    if not (-0.15 < z < 0.08):
        continue
    fern_clump(x, y, max(z, 0.0), random.uniform(1.4, 2.3), random.randint(9, 15)); placed += 1
# ferns nestled against trunk bases (seeds: trunk vertices just above the water)
trunk_base = []
for nm in ("BaldCypressTrunks" + TAG, "PondCypressTrunks" + TAG):
    me = bpy.data.objects[nm].data
    trunk_base += [v.co.copy() for v in me.vertices if 0.05 < v.co.z < 0.35]
for _ in range(L["trunk_base_ferns"]):
    if not trunk_base:
        break
    b = random.choice(trunk_base)
    d = Vector((b.x - DOME_C.x, b.y - DOME_C.y, 0))
    jitter = rand_dir_h() * random.uniform(0.05, 0.35)
    x, y = b.x + jitter.x, b.y + jitter.y
    fern_clump(x, y, max(ground_h(x, y), 0.0), random.uniform(0.6, 1.3), random.randint(8, 14))
ferns.obj("Cypress_FernCards", M_UNDER)

# ---------------------------------------------------------------- 3b. more cypress knees
kV, kF = [], []


def knee(kx, ky, kz, kh, kr):
    """Knobby conical knee (same profile as build_hybrid_swamp.py), rising from ground height kz."""
    seg = 10; b = len(kV)
    prof = [(-0.3, 1.25), (0.0, 1.0), (kh * 0.35, 0.85), (kh * 0.7, 0.6), (kh * 0.92, 0.45), (kh, 0.32)]
    ph = random.uniform(0, 6.3); lean = (random.gauss(0, 0.05), random.gauss(0, 0.05))
    for i, (zz, rf) in enumerate(prof):
        for j in range(seg):
            a = 2 * math.pi * j / seg
            jit = 1 + 0.18 * math.sin(3 * a + ph + i) + 0.08 * math.sin(7 * a + i * 2)
            lx, ly = lean[0] * max(zz, 0), lean[1] * max(zz, 0)
            kV.append((kx + lx + kr * rf * jit * math.cos(a), ky + ly + kr * rf * jit * math.sin(a), kz + zz))
    for i in range(len(prof) - 1):
        for j in range(seg):
            q = b + i * seg + j; w = b + i * seg + (j + 1) % seg
            kF.append((q, w, w + seg, q + seg))
    top = len(kV); kV.append((kx + lean[0] * kh, ky + lean[1] * kh, kz + kh + kr * 0.15))
    k = len(prof) - 1
    for j in range(seg):
        kF.append((b + k * seg + j, b + k * seg + (j + 1) % seg, top))


old_knees = [v.co.copy() for v in bpy.data.objects["CypressKnees" + TAG].data.vertices if v.co.z > 0.1]
made = 0; tries = 0
while made < L["extra_knees"] and tries < L["extra_knees"] * 30:
    tries += 1
    if random.random() < 0.6 and trunk_base:          # around trunks: knees ring the tree out to ~4 m
        b = random.choice(trunk_base)
        a = random.uniform(0, 2 * math.pi); r = random.uniform(0.8, 4.0)
        x, y = b.x + r * math.cos(a), b.y + r * math.sin(a)
    elif old_knees:                                    # thicken the existing knee clusters
        b = random.choice(old_knees)
        x, y = b.x + random.gauss(0, 0.6), b.y + random.gauss(0, 0.6)
    else:
        continue
    if math.hypot(x - DOME_C.x, y - DOME_C.y) > DOME_R + 2.0:
        continue
    g = ground_h(x, y)
    base = min(g, 0.0)
    kh = random.uniform(0.25, 1.0) * random.choice((1, 1, 1, 1.35)) + max(-base, 0) * 0.6
    knee(x, y, base, kh, random.uniform(0.05, 0.13))
    # knees often come in tight groups of 2-4
    for _ in range(random.choice((0, 0, 1, 2, 3))):
        x2, y2 = x + random.gauss(0, 0.25), y + random.gauss(0, 0.25)
        knee(x2, y2, min(ground_h(x2, y2), 0.0), kh * random.uniform(0.5, 0.9), random.uniform(0.04, 0.1))
    made += 1
me = bpy.data.meshes.new("Cypress_KneesExtra"); me.from_pydata(kV, [], kF); me.update(); me.shade_smooth()
me.materials.append(bpy.data.materials["KneeBark" + TAG])
coll.objects.link(bpy.data.objects.new("Cypress_KneesExtra", me))

# ---------------------------------------------------------------- 3c. young (small and medium) cypress
# Juvenile cypress are narrow cones with branches nearly to the ground; medium trees start to lift
# their crown and show a small flared base. Each gets its own trunk, branches and spray cards.
yV, yF, ybV, ybF = [], [], [], []
yfol = Cards()


def tube(V, F, pts, r0, r1, seg=6):
    base = len(V); n = len(pts)
    for i, c in enumerate(pts):
        d = (pts[min(i + 1, n - 1)] - pts[max(i - 1, 0)]).normalized()
        ref = Vector((0, 0, 1)) if abs(d.z) < 0.95 else Vector((1, 0, 0))
        u = d.cross(ref).normalized(); w = d.cross(u).normalized()
        r = r0 + (r1 - r0) * i / (n - 1)
        for j in range(seg):
            a = 2 * math.pi * j / seg
            V.append(tuple(c + (u * math.cos(a) + w * math.sin(a)) * r))
    for i in range(n - 1):
        for j in range(seg):
            a = base + i * seg + j; b = base + i * seg + (j + 1) % seg
            F.append((a, b, b + seg, a + seg))
    top = len(V); V.append(tuple(pts[-1] + (pts[-1] - pts[-2]).normalized() * 0.02))
    for j in range(seg):
        F.append((base + (n - 1) * seg + j, base + (n - 1) * seg + (j + 1) % seg, top))


def young_tree(x, y, h, medium):
    g = min(ground_h(x, y), 0.0)
    r = h * (0.022 if medium else 0.018)
    lean = Vector((random.gauss(0, 0.03), random.gauss(0, 0.03), 0))
    # trunk with a modest flare at the base
    rings, seg = 16, 12
    b0 = len(yV)
    for i in range(rings + 1):
        t = i / rings
        z = g - 0.3 + (h + 0.3) * t
        flare = (1.0 + (1.6 if medium else 0.9) * math.exp(-max(z, 0) / (0.35 if medium else 0.2)))
        rr = r * (1 - 0.85 * t) * flare + 0.005
        c = Vector((x, y, z)) + lean * max(z - g, 0.0) ** 1.3
        for j in range(seg):
            a = 2 * math.pi * j / seg
            k = 1 + 0.12 * math.exp(-max(z, 0) / 0.4) * math.sin(4 * a + i)
            yV.append((c.x + rr * k * math.cos(a), c.y + rr * k * math.sin(a), z))
    for i in range(rings):
        for j in range(seg):
            q = b0 + i * seg + j; w = b0 + i * seg + (j + 1) % seg
            yF.append((q, w, w + seg, q + seg))
    tip = Vector((x, y, g + h)) + lean * h ** 1.3
    yV.append(tuple(tip + Vector((0, 0, 0.05))))
    for j in range(seg):
        yF.append((b0 + rings * seg + j, b0 + rings * seg + (j + 1) % seg, len(yV) - 1))
    # whorl-ish branches: long near the bottom of the crown, short at the top (conical)
    crown_start = 0.12 if not medium else 0.3
    nb = int(h * (5 if medium else 7))
    for k in range(nb):
        t = crown_start + (0.97 - crown_start) * k / nb + random.uniform(-0.02, 0.02)
        z = g + h * t
        c = Vector((x, y, z)) + lean * max(z - g, 0.0) ** 1.3
        reach = h * (0.32 if medium else 0.38) * (1 - (t - crown_start) / (1 - crown_start)) ** 0.9 + 0.15
        a = random.uniform(0, 2 * math.pi)
        d = Vector((math.cos(a), math.sin(a), random.uniform(-0.05, 0.35))).normalized()
        pts = [c + d * reach * s_ / 4 + Vector((0, 0, -0.12 * reach * (s_ / 4) ** 2)) for s_ in range(5)]
        tube(ybV, ybF, pts, max(r * 0.35, 0.012), 0.004, seg=5)
        # sprays along the branch, drooping
        for s_ in range(max(5, int(reach * 14))):          # dense sprays: young cypress are fully clothed
            pb = pts[0].lerp(pts[-1], random.uniform(0.15, 1.0))
            up = (d * random.uniform(0.3, 0.8) + Vector((0, 0, random.uniform(-0.9, 0.1)))).normalized()
            side = up.cross(Vector((random.gauss(0, 1), random.gauss(0, 1), random.gauss(0, 1)))).normalized()
            hh = random.uniform(0.45, 0.9) * (1.2 if medium else 1.0)
            yfol.card(pb, up, side, hh, hh * 0.5, random.choice((0, 1, 1, 2)))
    # leader tuft
    for _ in range(6):
        up = Vector((random.gauss(0, 0.4), random.gauss(0, 0.4), 1)).normalized()
        side = up.cross(Vector((1, 0, 0))).normalized()
        yfol.card(tip - Vector((0, 0, 0.4)), up, side, 0.5, 0.25, random.choice((0, 1)))


def free_spot(r0, r1, clearance):
    for _ in range(400):
        x, y = in_ring(r0, r1)
        if any((x - b.x) ** 2 + (y - b.y) ** 2 < clearance ** 2 for b in trunk_base[::40]):
            continue
        if any((x - t[0]) ** 2 + (y - t[1]) ** 2 < clearance ** 2 for t in young_spots):
            continue
        return x, y
    return None


young_spots = []
for medium, count, hr in ((True, L["young_medium"], (6.0, 10.0)), (False, L["young_small"], (2.0, 4.5))):
    for _ in range(count):
        # young trees favour the light: the pond edge and the outer rim of the dome
        spot = free_spot(POND_R + 0.3, POND_R + 3.5, 1.8) if random.random() < 0.5 else free_spot(DOME_R - 4.0, DOME_R + 1.5, 1.8)
        if spot is None:
            continue
        young_spots.append(spot)
        young_tree(spot[0], spot[1], random.uniform(*hr), medium)

for name, V, F, mat in (("Cypress_YoungTrunks", yV, yF, bpy.data.materials["PondCypressBarkReal" + TAG]),
                        ("Cypress_YoungBranches", ybV, ybF, bpy.data.materials["BaldCypressBarkReal" + TAG])):
    me = bpy.data.meshes.new(name); me.from_pydata(V, [], F); me.update(); me.shade_smooth()
    me.materials.append(mat)
    coll.objects.link(bpy.data.objects.new(name, me))
yfol.obj("Cypress_YoungFoliage", M_CANOPY)

# ---------------------------------------------------------------- 4. pickerelweed in the shallows
pick = Cards(); placed = tries = 0
while placed < L["pickerel_clumps"] and tries < L["pickerel_clumps"] * 60:
    tries += 1
    x, y = in_ring(POND_R - 1.5, DOME_R + 4.0)
    z = ground_h(x, y)
    if not (-0.4 < z < -0.05):
        continue
    for _ in range(random.randint(3, 5)):
        d = rand_dir_h()
        pick.card(Vector((x, y, -0.15)) + d * random.uniform(0, 0.25), tilted(d, random.uniform(0, 0.25)),
                  Vector((-d.y, d.x, 0)), random.uniform(0.7, 1.2), random.uniform(0.6, 0.9), 3)
    placed += 1
pick.obj("Cypress_Pickerelweed", M_UNDER)

# ---------------------------------------------------------------- 5. leaf litter on / in the water
LITTER_CELLS = (0, 0, 0, 1, 1, 2, 2, 3, 3)              # cypress sprigs most common, then broad, maple, rotting
LITTER_SIZE = {0: (0.10, 0.18), 1: (0.09, 0.15), 2: (0.08, 0.13), 3: (0.09, 0.15)}


def water_point():
    for _ in range(200):
        x, y = in_ring(0.0, DOME_R + 3.0)
        g = ground_h(x, y)
        if g < -0.06:
            return x, y, g
    return None


# leaves drift into clusters against hummocks, knees and trunks: cluster centres near the shorelines
centres = []
while len(centres) < 90:
    x, y = in_ring(0.0, DOME_R + 3.0)
    if -0.2 < ground_h(x, y) < -0.04:
        centres.append((x, y))


def litter_layer(cards, count, z_of):
    n = 0; tries = 0
    while n < count and tries < count * 30:
        tries += 1
        if random.random() < 0.5:
            cx, cy = random.choice(centres)
            x, y = cx + random.gauss(0, 0.7), cy + random.gauss(0, 0.7)
            g = ground_h(x, y)
            if g > -0.04:
                continue
        else:
            pt = water_point()
            if pt is None:
                continue
            x, y, g = pt
        z = z_of(g)
        if z is None:
            continue
        cell = random.choice(LITTER_CELLS)
        h = random.uniform(*LITTER_SIZE[cell])
        a = random.uniform(0, 2 * math.pi)
        up = Vector((math.cos(a), math.sin(a), random.uniform(-0.04, 0.04))).normalized()
        side = Vector((-math.sin(a), math.cos(a), random.uniform(-0.04, 0.04))).normalized()
        cards.card(Vector((x, y, z)) - up * h * 0.5, up, side, h, h * 0.9, cell)
        n += 1


floating = Cards()
litter_layer(floating, L["floating_leaves"], lambda g: 0.008 + random.uniform(0, 0.004))
floating.obj("Cypress_FloatingLitter", M_LITTER)
sunken = Cards()
litter_layer(sunken, L["submerged_leaves"],
             lambda g: (random.uniform(max(g + 0.02, -0.25), -0.03) if g + 0.02 < -0.03 else None))
sunken.obj("Cypress_SunkenLitter", M_LITTER)
# the old untextured diamonds are superseded
old = bpy.data.objects.get("FloatingLeaves" + TAG)
if old:
    old.hide_render = True; old.hide_viewport = True

stats = {o.name: len(o.data.polygons) for o in coll.objects}
stats["young_trees"] = len(young_spots)
stats["knee_groups_added"] = made

# ---------------------------------------------------------------- export to Unity
if L["export"] and os.path.isdir(UNITY_DIR):
    os.makedirs(os.path.join(UNITY_DIR, "Models"), exist_ok=True)
    os.makedirs(os.path.join(UNITY_DIR, "Textures"), exist_ok=True)
    for ob in bpy.context.view_layer.objects:
        ob.select_set(False)
    for ob in coll.objects:
        ob.select_set(True)
    bpy.context.view_layer.objects.active = coll.objects[0]
    bpy.ops.export_scene.fbx(
        filepath=os.path.join(UNITY_DIR, "Models", "CypressLush.fbx"), use_selection=True, object_types={'MESH'},
        apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
        bake_space_transform=True, use_mesh_modifiers=False, mesh_smooth_type='FACE', use_tspace=False,
        add_leaf_bones=False, bake_anim=False, path_mode='AUTO', embed_textures=False)
    for src in (ATLAS2, ATLAS3, PONDBED):
        if os.path.exists(src):
            shutil.copyfile(src, os.path.join(UNITY_DIR, "Textures", os.path.basename(src)))

result = {"cards": stats}
print(result)

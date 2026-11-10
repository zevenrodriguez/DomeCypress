"""
Exports where every cypress trunk is, so Unity can place ghost orchids on the trunk surface.
Writes domecypress/Assets/Cypress/tree_anchors.json (Unity coordinates, meters):
  { "trees": [ { "kind": "bald|pond|young", "flat": [x, y, z, r, x, y, z, r, ...] }, ... ] }
(Same format Unity's Tools > Cypress > Interaction > Rebuild Tree Anchors writes; either works.)
Each sample is the trunk centre at height y (above the water, y = 0) and its average radius there,
every 0.25 m from 0.5 m up to 8 m (or the tree top). Run in Blender after build_hybrid_swamp.py
and build_lush_layer.py (young trees come from the lush layer).
"""
import bpy, json, math, os
from collections import defaultdict

TAG = "_hy"
OUT = os.path.join(os.path.dirname(bpy.data.filepath), "domecypress", "Assets", "Cypress", "tree_anchors.json")
H0, H1, STEP, BAND = 0.5, 8.0, 0.25, 0.14


def to_unity(x, y, z):
    return [round(-x, 4), round(z, 4), round(-y, 4)]


def components(me):
    """Connected pieces of a mesh (each trunk is one piece) via union-find over polygon edges."""
    parent = list(range(len(me.vertices)))

    def find(a):
        while parent[a] != a:
            parent[a] = parent[parent[a]]
            a = parent[a]
        return a

    for p in me.polygons:
        vs = p.vertices
        r0 = find(vs[0])
        for v in vs[1:]:
            r = find(v)
            if r != r0:
                parent[r] = r0
    groups = defaultdict(list)
    for i in range(len(me.vertices)):
        groups[find(i)].append(i)
    return list(groups.values())


def sample_trunk(me, idx):
    co = [me.vertices[i].co for i in idx]
    top = max(c.z for c in co)
    out = []
    h = H0
    while h <= min(H1, top - 0.2):
        ring = [c for c in co if abs(c.z - h) < BAND]
        if len(ring) >= 6:
            cx = sum(c.x for c in ring) / len(ring)
            cy = sum(c.y for c in ring) / len(ring)
            r = sum(math.hypot(c.x - cx, c.y - cy) for c in ring) / len(ring)
            out.append(to_unity(cx, cy, h) + [round(r, 4)])
        h += STEP
    return out


trees = []
for name, kind in (("BaldCypressTrunks" + TAG, "bald"), ("PondCypressTrunks" + TAG, "pond"), ("Cypress_YoungTrunks", "young")):
    ob = bpy.data.objects.get(name)
    if ob is None:
        continue
    me = ob.data
    for idx in components(me):
        if len(idx) < 40:
            continue
        s = sample_trunk(me, idx)
        if len(s) >= 4:
            trees.append({"kind": kind, "flat": [v for smp in s for v in smp]})

os.makedirs(os.path.dirname(OUT), exist_ok=True)
with open(OUT, "w", encoding="utf-8") as f:
    json.dump({"trees": trees}, f, separators=(",", ":"))
result = {"trees": len(trees), "by_kind": {k: sum(1 for t in trees if t["kind"] == k) for k in ("bald", "pond", "young")}, "out": OUT}
print(result)

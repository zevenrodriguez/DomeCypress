"""
Leaf-litter textures for the cypress dome water.
  floating_litter_atlas.png  2x2 RGBA cards (transparent background):
      0 fallen bald-cypress sprig (rusty orange)   1 pond-apple / laurel leaf (brown, spotted)
      2 red maple leaf (red-orange, lobed)          3 decaying dark leaf with holes
  pondbed_litter.png         1024x1024 seamless (tileable) submerged leaf litter on black muck
Usage: python make_litter_textures.py <out_dir>
"""
import sys, os, math, random
from PIL import Image, ImageDraw, ImageFilter

SS = 2
CELL = 1024


def lerp(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))


def oval(base, ang, L, W, steps=18, power=0.8):
    ca, sa = math.cos(ang), math.sin(ang); px, py = -sa, ca
    A, B = [], []
    for i in range(steps + 1):
        t = i / steps
        w = W * math.sin(math.pi * t) ** power
        cx, cy = base[0] + ca * L * t, base[1] + sa * L * t
        A.append((cx + px * w, cy + py * w)); B.append((cx - px * w, cy - py * w))
    return A + B[::-1]


def veins(d, base, ang, L, W, col, width):
    ca, sa = math.cos(ang), math.sin(ang)
    tip = (base[0] + ca * L * 0.95, base[1] + sa * L * 0.95)
    d.line([base, tip], fill=col, width=width)
    for k in range(1, 8):
        t = k / 8
        p = (base[0] + ca * L * t, base[1] + sa * L * t)
        for s in (-1, 1):
            a = ang + s * 0.9
            q = (p[0] + math.cos(a) * W * 0.8 * math.sin(math.pi * t), p[1] + math.sin(a) * W * 0.8 * math.sin(math.pi * t))
            d.line([p, q], fill=col, width=max(1, width // 2))


def maple(d, c, size, ang, col, vein_col):
    """Five-lobed leaf drawn as a fan of pointed lobes."""
    pts = []
    lobes = [(-1.25, 0.65), (-0.6, 0.95), (0.0, 1.0), (0.6, 0.95), (1.25, 0.65)]
    for i, (la, ll) in enumerate(lobes):
        a = ang + la
        pts.append((c[0] + math.cos(a) * size * ll, c[1] + math.sin(a) * size * ll))
        if i < len(lobes) - 1:
            a2 = ang + (la + lobes[i + 1][0]) / 2
            pts.append((c[0] + math.cos(a2) * size * 0.45, c[1] + math.sin(a2) * size * 0.45))
    a = ang + math.pi
    pts += [(c[0] + math.cos(a + 0.5) * size * 0.25, c[1] + math.sin(a + 0.5) * size * 0.25),
            (c[0] + math.cos(a - 0.5) * size * 0.25, c[1] + math.sin(a - 0.5) * size * 0.25)]
    d.polygon(pts, fill=col + (255,))
    for la, ll in lobes:
        a = ang + la
        d.line([c, (c[0] + math.cos(a) * size * ll * 0.85, c[1] + math.sin(a) * size * ll * 0.85)], fill=vein_col + (255,), width=3 * SS)
    d.line([c, (c[0] + math.cos(ang + math.pi) * size * 0.6, c[1] + math.sin(ang + math.pi) * size * 0.6)], fill=vein_col + (255,), width=4 * SS)


def mottle(img, rnd, dark=0.55, spots=60, rmin=4, rmax=30):
    """Darken random blotches inside the alpha (rot spots, wet patches)."""
    S = img.size[0]
    over = Image.new("L", img.size, 0); d = ImageDraw.Draw(over)
    for _ in range(spots):
        x, y = rnd.uniform(0, S), rnd.uniform(0, S); r = rnd.uniform(rmin, rmax) * SS
        d.ellipse([x - r, y - r, x + r, y + r], fill=int(255 * rnd.uniform(0.3, 1.0)))
    over = over.filter(ImageFilter.GaussianBlur(6 * SS))
    rgb = img.convert("RGB"); darker = rgb.point(lambda v: int(v * dark))
    out = Image.composite(darker, rgb, over).convert("RGBA"); out.putalpha(img.getchannel("A"))
    return out


def cell_sprig(rnd, S):
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0)); d = ImageDraw.Draw(img)
    base = (S * 0.5, S * 0.95); ang = -math.pi / 2 + rnd.uniform(-0.2, 0.2)
    stem, p = [], base
    for i in range(40):
        a = ang + 0.25 * math.sin(i * 0.15)
        p = (p[0] + math.cos(a) * S * 0.022, p[1] + math.sin(a) * S * 0.022); stem.append((p, a))
    d.line([base] + [s[0] for s in stem], fill=(70, 40, 22, 255), width=5 * SS)
    for k in range(70):
        t = 0.04 + 0.94 * k / 70
        p, a = stem[int(t * 39)]
        side = 1 if k % 2 else -1
        L = S * 0.1 * (0.5 + 0.6 * math.sin(math.pi * min(t * 1.1, 1)))
        col = lerp((150, 70, 25), (205, 120, 45), rnd.random())
        if rnd.random() < 0.12:
            continue                                     # some needles already dropped
        d.polygon(oval(p, a + side * rnd.uniform(1.0, 1.4), L, S * 0.011), fill=col + (255,))
    return mottle(img, rnd, 0.6, 30, 3, 15)


def cell_broad(rnd, S):
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0)); d = ImageDraw.Draw(img)
    base = (S * 0.5, S * 0.93); ang = -math.pi / 2 + rnd.uniform(-0.15, 0.15)
    L, W = S * 0.86, S * 0.25
    col = (150, 105, 50)
    d.polygon(oval(base, ang, L, W, 30, 0.75), fill=col + (255,))
    d.line([base, (base[0], S * 0.99)], fill=(90, 60, 30, 255), width=6 * SS)       # petiole
    veins(d, base, ang, L, W, (190, 150, 90, 255), 5 * SS)
    img = mottle(img, rnd, 0.45, 80, 6, 40)
    return img


def cell_maple(rnd, S):
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0)); d = ImageDraw.Draw(img)
    c = (S * 0.5, S * 0.55)
    maple(d, c, S * 0.42, -math.pi / 2, (185, 60, 30), (215, 120, 60))
    d.line([c, (S * 0.5, S * 0.98)], fill=(120, 40, 20, 255), width=6 * SS)
    return mottle(img, rnd, 0.55, 50, 6, 30)


def cell_decay(rnd, S):
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0)); d = ImageDraw.Draw(img)
    base = (S * 0.5, S * 0.92)
    d.polygon(oval(base, -math.pi / 2 + 0.2, S * 0.8, S * 0.3, 30, 0.7), fill=(70, 52, 32, 255))
    veins(d, base, -math.pi / 2 + 0.2, S * 0.8, S * 0.3, (110, 85, 55, 255), 4 * SS)
    for _ in range(14):                                   # holes eaten through (skeletonised patches)
        x, y = rnd.uniform(S * 0.3, S * 0.7), rnd.uniform(S * 0.15, S * 0.85); r = rnd.uniform(8, 40) * SS
        d.ellipse([x - r, y - r, x + r * 1.3, y + r], fill=(0, 0, 0, 0))
    return mottle(img, rnd, 0.6, 40, 5, 25)


def finish(img):
    img = img.resize((CELL, CELL), Image.LANCZOS)
    a = img.getchannel("A")
    rgb = img.convert("RGB").filter(ImageFilter.MaxFilter(9))
    out = Image.composite(img.convert("RGB"), rgb, a).convert("RGBA"); out.putalpha(a)
    return out


def atlas(path):
    S = CELL * SS
    out = Image.new("RGBA", (2 * CELL, 2 * CELL), (0, 0, 0, 0))
    for i, fn in enumerate((cell_sprig, cell_broad, cell_maple, cell_decay)):
        out.paste(finish(fn(random.Random(200 + i), S)), ((i % 2) * CELL, (i // 2) * CELL))
    out.save(path); print("saved", path)


def pondbed(path, size=1024):
    """Seamless submerged litter: everything is drawn with wrap-around copies so the tile repeats cleanly."""
    rnd = random.Random(77)
    S = size * SS
    img = Image.new("RGB", (S, S), (20, 14, 9)); d = ImageDraw.Draw(img)

    def wrapped(draw_fn, x, y, r):
        for ox in (-S, 0, S):
            for oy in (-S, 0, S):
                if -r < x + ox < S + r and -r < y + oy < S + r:
                    draw_fn(x + ox, y + oy)

    # silt blotches
    for _ in range(400):
        x, y, r = rnd.uniform(0, S), rnd.uniform(0, S), rnd.uniform(20, 90) * SS
        col = lerp((14, 10, 7), (40, 30, 20), rnd.random())
        wrapped(lambda X, Y: d.ellipse([X - r, Y - r, X + r, Y + r], fill=col), x, y, r)
    img = img.filter(ImageFilter.GaussianBlur(12 * SS)); d = ImageDraw.Draw(img)
    # layered leaves, older (darker) underneath, fresher on top
    for layer in range(4):
        k = layer / 3
        for _ in range(260):
            x, y = rnd.uniform(0, S), rnd.uniform(0, S)
            L = rnd.uniform(40, 120) * SS; W = L * rnd.uniform(0.25, 0.4); a = rnd.uniform(0, 2 * math.pi)
            col = lerp(lerp((30, 20, 12), (60, 40, 22), rnd.random()), lerp((95, 60, 30), (130, 70, 35), rnd.random()), k * 0.8)
            bx, by = x - math.cos(a) * L / 2, y - math.sin(a) * L / 2
            if rnd.random() < 0.25:
                wrapped(lambda X, Y: maple(d, (X, Y), L * 0.5, a, col, lerp(col, (0, 0, 0), 0.3)), x, y, L)
            else:
                wrapped(lambda X, Y: d.polygon(oval((X - math.cos(a) * L / 2, Y - math.sin(a) * L / 2), a, L, W, 16, 0.75), fill=col), x, y, L)
                wrapped(lambda X, Y: d.line([(X - math.cos(a) * L / 2, Y - math.sin(a) * L / 2), (X + math.cos(a) * L / 2, Y + math.sin(a) * L / 2)],
                                            fill=lerp(col, (180, 140, 90), 0.25), width=2 * SS), x, y, L)
        # cypress needles and twigs between layers
        for _ in range(500):
            x, y = rnd.uniform(0, S), rnd.uniform(0, S); a = rnd.uniform(0, 2 * math.pi); L = rnd.uniform(10, 26) * SS
            col = lerp((70, 40, 18), (140, 80, 35), rnd.random() * (0.4 + 0.6 * k))
            wrapped(lambda X, Y: d.line([(X, Y), (X + math.cos(a) * L, Y + math.sin(a) * L)], fill=col, width=3 * SS), x, y, L)
        if layer < 3:   # settle a layer of silt over the older leaves
            veil = Image.new("RGB", (S, S), (22, 16, 10))
            img = Image.blend(img, veil, 0.35); d = ImageDraw.Draw(img)
    img = img.resize((size, size), Image.LANCZOS)
    img.save(path); print("saved", path)


if __name__ == "__main__":
    out = sys.argv[1] if len(sys.argv) > 1 else "."
    os.makedirs(out, exist_ok=True)
    atlas(os.path.join(out, "floating_litter_atlas.png"))
    pondbed(os.path.join(out, "pondbed_litter.png"))

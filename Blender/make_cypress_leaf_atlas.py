"""
Generates a 2x2 RGBA leaf-card atlas for cypress foliage (transparent background).
Cells: 0,1 = bald-cypress feathery sprays (light / mid green), 2 = darker shade spray,
3 = pond-cypress thread-like upright branchlets.  Run with: python make_cypress_leaf_atlas.py out.png
"""
import sys, math, random
from PIL import Image, ImageDraw, ImageFilter

SS = 2                      # supersampling for clean anti-aliased edges
CELL = 1024
ATLAS = 2 * CELL


def lerp(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(len(a)))


def needle(d, base, ang, length, width, col_base, col_tip):
    """Tapered linear leaf as a polygon with a lighter midrib."""
    ca, sa = math.cos(ang), math.sin(ang)
    px, py = -sa, ca
    pts_l, pts_r = [], []
    steps = 8
    for i in range(steps + 1):
        t = i / steps
        w = width * (math.sin(math.pi * min(t * 1.15, 1.0)) ** 0.6) * (1 - 0.25 * t)
        cx = base[0] + ca * length * t
        cy = base[1] + sa * length * t
        pts_l.append((cx + px * w, cy + py * w))
        pts_r.append((cx - px * w, cy - py * w))
    poly = pts_l + pts_r[::-1]
    d.polygon(poly, fill=lerp(col_base, col_tip, 0.5) + (255,))
    # midrib highlight
    d.line([base, (base[0] + ca * length * 0.9, base[1] + sa * length * 0.9)],
           fill=lerp(col_tip, (230, 240, 170), 0.35) + (255,), width=max(1, int(width * 0.35)))


def branchlet(d, start, ang, length, scale, palette, rnd, upright=False):
    """Stem with two ranks of alternating needles (bald cypress) or appressed threads (pond cypress)."""
    steps = 40
    curve = rnd.uniform(-0.25, 0.25)
    prev = start
    stem = []
    for i in range(steps + 1):
        t = i / steps
        a = ang + curve * t
        p = (prev[0] + math.cos(a) * length / steps, prev[1] + math.sin(a) * length / steps)
        stem.append((p, a, t)); prev = p
    d.line([start] + [s[0] for s in stem], fill=(92, 70, 40, 255), width=int(5 * scale))
    n = rnd.randint(34, 44) if not upright else rnd.randint(46, 60)   # dense, full sprays
    for k in range(n):
        t = 0.06 + 0.92 * k / n
        p, a, _ = stem[int(t * steps)]
        side = 1 if k % 2 == 0 else -1
        if upright:      # pond cypress: short awl-like leaves pressed along the twig
            spread = rnd.uniform(0.15, 0.35)
            L = scale * rnd.uniform(28, 45) * (1 - 0.4 * t)
            W = scale * rnd.uniform(3.0, 4.5)
        else:            # bald cypress: flat linear leaves spread in one plane
            spread = rnd.uniform(1.05, 1.35)
            L = scale * rnd.uniform(60, 85) * (0.55 + 0.6 * math.sin(math.pi * min(t * 1.1, 1)))
            W = scale * rnd.uniform(6, 8.5)
        c0 = palette[rnd.randrange(len(palette))]
        c1 = lerp(c0, (200, 215, 120), rnd.uniform(0.1, 0.35))
        needle(d, p, a + side * spread, L, W, lerp(c0, (20, 40, 10), 0.25), c1)
    return stem


def make_cell(kind, seed):
    rnd = random.Random(seed)
    S = CELL * SS
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    pal = {
        "light": [(96, 150, 40), (110, 162, 52), (82, 138, 34), (125, 170, 60)],
        "mid":   [(70, 120, 30), (60, 110, 28), (82, 130, 38), (95, 140, 45)],
        "dark":  [(44, 86, 22), (52, 96, 26), (38, 78, 20), (60, 104, 30)],
        "pond":  [(88, 128, 44), (100, 140, 52), (76, 116, 38), (110, 146, 60)],
    }[kind]
    base = (S * 0.5, S * 0.97)
    # main twig rising through the card, branchlets fanning off it
    main_len = S * 0.85
    main_ang = -math.pi / 2 + rnd.uniform(-0.1, 0.1)
    stem = []
    prev = base
    for i in range(30):
        a = main_ang + 0.15 * math.sin(i * 0.2 + seed)
        p = (prev[0] + math.cos(a) * main_len / 30, prev[1] + math.sin(a) * main_len / 30)
        stem.append((p, a)); prev = p
    d.line([base] + [s[0] for s in stem], fill=(85, 62, 36, 255), width=int(9 * SS))
    # back layer: shaded branchlets behind the main spray fill the gaps so the card reads as a full mass
    dark = [lerp(c, (10, 25, 8), 0.35) for c in pal]
    for k in range(9 if kind != "pond" else 14):
        t = 0.1 + 0.8 * k / (9 if kind != "pond" else 14)
        p, a = stem[int(t * 29)]
        side = 1 if k % 2 else -1
        ang = a + side * rnd.uniform(0.35, 0.75)
        L = S * (0.36 if kind != "pond" else 0.3) * (1.0 - 0.4 * t) * rnd.uniform(0.85, 1.1)
        branchlet(d, p, ang, L, SS * (0.9 - 0.2 * t), dark, rnd, upright=(kind == "pond"))
    n_br = 11 if kind != "pond" else 18
    for k in range(n_br):
        t = 0.08 + 0.85 * k / n_br
        p, a = stem[int(t * 29)]
        side = 1 if k % 2 == 0 else -1
        ang = a + side * rnd.uniform(0.55, 0.9)
        L = S * (0.42 if kind != "pond" else 0.36) * (1.0 - 0.45 * t) * rnd.uniform(0.85, 1.1)
        branchlet(d, p, ang, L, SS * (1.0 - 0.25 * t), pal, rnd, upright=(kind == "pond"))
    # terminal branchlet
    p, a = stem[-1]
    branchlet(d, p, a, S * 0.18, SS * 0.8, pal, rnd, upright=(kind == "pond"))
    img = img.resize((CELL, CELL), Image.LANCZOS)
    # bleed colour into transparent pixels so mip-maps don't get dark fringes
    rgb = img.convert("RGB").filter(ImageFilter.MaxFilter(9))
    a = img.getchannel("A")
    bled = Image.composite(img.convert("RGB"), rgb, a)
    out = bled.convert("RGBA"); out.putalpha(a)
    return out


def main(path):
    atlas = Image.new("RGBA", (ATLAS, ATLAS), (0, 0, 0, 0))
    for i, (kind, seed) in enumerate((("light", 11), ("mid", 22), ("dark", 33), ("pond", 44))):
        cell = make_cell(kind, seed)
        atlas.paste(cell, ((i % 2) * CELL, (i // 2) * CELL))
    atlas.save(path)
    print("saved", path)


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else "cypress_leaf_atlas.png")

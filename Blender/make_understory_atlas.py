"""
2x2 RGBA understory atlas for the cypress dome (transparent background):
  cell 0 (top-left)     Spanish moss - grey-green strands hanging from the top edge
  cell 1 (top-right)    fern frond (sword / Boston fern) rising from the bottom edge
  cell 2 (bottom-left)  broadleaf shrub spray (pond apple / coco plum) rising from the bottom edge
  cell 3 (bottom-right) pickerelweed - heart-shaped leaves on stalks with a violet flower spike
Usage: python make_understory_atlas.py out.png
"""
import sys, math, random
from PIL import Image, ImageDraw, ImageFilter

SS = 2
CELL = 1024


def lerp(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))


def leaf_poly(base, ang, length, width, shape="oval", steps=14):
    ca, sa = math.cos(ang), math.sin(ang)
    px, py = -sa, ca
    L, R = [], []
    for i in range(steps + 1):
        t = i / steps
        if shape == "oval":
            w = width * math.sin(math.pi * t) ** 0.8
        elif shape == "pinna":
            w = width * min(1.0, t * 4) * (1 - t) ** 0.6
        else:  # heart
            w = width * (math.sin(math.pi * (0.15 + 0.85 * t)) ** 0.7) * (1.25 - 0.5 * t)
        cx, cy = base[0] + ca * length * t, base[1] + sa * length * t
        L.append((cx + px * w, cy + py * w)); R.append((cx - px * w, cy - py * w))
    return L + R[::-1]


def draw_leaf(d, base, ang, length, width, col, shape="oval", vein=True):
    d.polygon(leaf_poly(base, ang, length, width, shape), fill=col + (255,))
    if vein:
        tip = (base[0] + math.cos(ang) * length * 0.92, base[1] + math.sin(ang) * length * 0.92)
        d.line([base, tip], fill=lerp(col, (220, 230, 170), 0.35) + (255,), width=max(1, int(width * 0.12)))


def curve(start, ang, length, bend, steps=30):
    pts, p = [start], start
    for i in range(steps):
        a = ang + bend * (i / steps)
        p = (p[0] + math.cos(a) * length / steps, p[1] + math.sin(a) * length / steps)
        pts.append(p)
    return pts


def spanish_moss(rnd, S):
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0)); d = ImageDraw.Draw(img)
    for _ in range(150):
        x = S * (0.5 + rnd.gauss(0, 0.17)); y = rnd.uniform(-0.02, 0.08) * S
        L = S * rnd.uniform(0.35, 0.95)
        pts = []
        phase, amp = rnd.uniform(0, 6.3), rnd.uniform(6, 20) * SS
        for i in range(60):
            t = i / 59
            pts.append((x + math.sin(t * 9 + phase) * amp * t + rnd.gauss(0, 1.2), y + L * t))
        g = rnd.uniform(0, 1)
        col = lerp((118, 128, 108), (176, 184, 160), g)
        d.line(pts, fill=col + (255,), width=int(rnd.uniform(1.5, 3.2) * SS))
        for _ in range(rnd.randint(2, 6)):          # side wisps
            i = rnd.randint(5, 55); p = pts[i]
            d.line(curve(p, math.pi / 2 + rnd.uniform(-0.8, 0.8), rnd.uniform(20, 70) * SS, rnd.uniform(-1, 1), 12),
                   fill=col + (255,), width=int(1.5 * SS))
    return img


def fern(rnd, S):
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0)); d = ImageDraw.Draw(img)
    base = (S * 0.5, S * 0.99)
    rach = curve(base, -math.pi / 2 + rnd.uniform(-0.1, 0.1), S * 0.95, rnd.uniform(-0.35, 0.35), 50)
    d.line(rach, fill=(70, 95, 35, 255), width=int(5 * SS))
    for k in range(48):
        t = 0.06 + 0.92 * k / 48
        p = rach[int(t * 50)]
        side = 1 if k % 2 == 0 else -1
        a = -math.pi / 2 + side * rnd.uniform(1.05, 1.3) - 0.25 * side * t
        L = S * 0.2 * math.sin(math.pi * min(t * 1.1, 1)) ** 0.7 + S * 0.015
        col = lerp((40, 92, 26), (92, 140, 48), rnd.random() * 0.6 + 0.2 * t)
        draw_leaf(d, p, a, L, L * 0.17, col, "pinna")
    return img


def shrub(rnd, S):
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0)); d = ImageDraw.Draw(img)
    base = (S * 0.5, S * 0.99)
    stem = curve(base, -math.pi / 2, S * 0.9, rnd.uniform(-0.3, 0.3), 40)
    d.line(stem, fill=(80, 64, 44, 255), width=int(8 * SS))
    branches = [stem]
    for k in range(5):
        t = 0.25 + 0.6 * k / 5
        p = stem[int(t * 40)]
        side = 1 if k % 2 == 0 else -1
        br = curve(p, -math.pi / 2 + side * rnd.uniform(0.6, 1.0), S * rnd.uniform(0.25, 0.4), -side * 0.5, 20)
        d.line(br, fill=(80, 64, 44, 255), width=int(5 * SS)); branches.append(br)
    for br in branches:
        for i in range(4, len(br), 3):
            p = br[i]
            a = rnd.uniform(0, 2 * math.pi)
            L = S * rnd.uniform(0.07, 0.12)
            col = lerp((24, 62, 18), (70, 112, 36), rnd.random())
            draw_leaf(d, p, a, L, L * 0.33, col, "oval")
    return img


def pickerel(rnd, S):
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0)); d = ImageDraw.Draw(img)
    for k in range(7):
        bx = S * (0.5 + rnd.gauss(0, 0.06))
        a = -math.pi / 2 + rnd.uniform(-0.35, 0.35)
        stalk = curve((bx, S * 0.99), a, S * rnd.uniform(0.45, 0.7), rnd.uniform(-0.3, 0.3), 25)
        d.line(stalk, fill=(70, 110, 40, 255), width=int(6 * SS))
        tip = stalk[-1]
        L = S * rnd.uniform(0.18, 0.26)
        col = lerp((44, 100, 30), (96, 150, 52), rnd.random())
        draw_leaf(d, tip, a + rnd.uniform(-0.4, 0.4), L, L * 0.38, col, "heart")
    # violet flower spike
    sp = curve((S * 0.52, S * 0.99), -math.pi / 2 + 0.05, S * 0.8, 0.0, 25)
    d.line(sp, fill=(70, 110, 40, 255), width=int(6 * SS))
    for i in range(140):
        t = rnd.uniform(0.75, 1.0)
        p = sp[int(t * 25)]
        r = rnd.uniform(5, 10) * SS
        c = lerp((96, 70, 170), (150, 120, 220), rnd.random())
        x, y = p[0] + rnd.gauss(0, 12 * SS), p[1] + rnd.gauss(0, 6 * SS)
        d.ellipse([x - r, y - r, x + r, y + r], fill=c + (255,))
    return img


def finish(img):
    img = img.resize((CELL, CELL), Image.LANCZOS)
    a = img.getchannel("A")
    rgb = img.convert("RGB").filter(ImageFilter.MaxFilter(9))     # colour bleed for clean mips
    out = Image.composite(img.convert("RGB"), rgb, a).convert("RGBA"); out.putalpha(a)
    return out


def main(path):
    S = CELL * SS
    atlas = Image.new("RGBA", (2 * CELL, 2 * CELL), (0, 0, 0, 0))
    for i, fn in enumerate((spanish_moss, fern, shrub, pickerel)):
        atlas.paste(finish(fn(random.Random(100 + i), S)), ((i % 2) * CELL, (i // 2) * CELL))
    atlas.save(path); print("saved", path)


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else "swamp_understory_atlas.png")

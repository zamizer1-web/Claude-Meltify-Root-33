"""Character effects for Root 33, set A: a figure knitting itself back up, a big round friend sitting on a guard stack,
and a harvest arc of gold light.

  asset.fx.verso-get-back-up  art/fx/verso-get-back-up.png  two rows of five 256px frames: safe (threads of light
                              stitch a generic ink silhouette back together, gold seams like mended pottery) and
                              finished (loose brushstrokes fly in and paint the same figure back on its feet).
  asset.fx.esquie-sit         art/fx/esquie-sit.png         five 256px frames: drop, squash, rebound, settle, rest,
                              with a comic dust ring.
  asset.fx.sciel-reaping      art/fx/sciel-reaping.png      six 320px frames of a gold arc sweeping round a clearing
                              and cutting a ring of grain stalks.

All original, drawn from scratch with Pillow in the shared painterly style (tools/art/style.py). The figures are
invented generic shapes, with no likeness of anyone. Every random choice comes from a seeded random.Random (noise
included), so the same code always paints the same pixels. Frames are drawn at 2-3x and reduced.
"""
import math
import random
import sys
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageOps

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from style import (BONE, CRIMSON, CRIMSON_DEEP, DUSK, GOLD, GOLD_CORE, GOLD_DEEP, INK, INK_SOFT, PAPER,  # noqa: E402
                   NIGHT, TEAL, TEAL_DEEP, brush_stroke, canvas, curve, glow, inner_edge, outline_of, solid)

SEED = 3360
LIGHT_TEAL = (176, 226, 222)
WOOD = (150, 104, 64)
WOOD_DEEP = (88, 58, 38)
WOOD_LIGHT = (196, 148, 96)
IRON = (128, 124, 138)
IRON_LIGHT = (220, 216, 228)
BLUSH = (226, 120, 120)
STRAW = (222, 178, 92)
STRAW_DEEP = (150, 100, 40)


# ----------------------------------------------------------------------------------------------------------- helpers

def comp(base, *layers):
    for layer in layers:
        base = Image.alpha_composite(base, layer)
    return base


def clip(img, mask):
    """Multiply an RGBA layer's alpha by an L mask."""
    out = img.copy()
    out.putalpha(ImageChops.multiply(img.getchannel("A"), mask))
    return out


def fade(img, f):
    out = img.copy()
    out.putalpha(img.getchannel("A").point(lambda v: int(v * f)))
    return out


def scale_l(mask, f):
    return mask.point(lambda v: max(0, min(255, int(v * f))))


def noise_l(size, rng, cell=1, blur=0.0, stretch=True):
    """Seeded value noise (Image.effect_noise draws from C rand(), which would make builds order-dependent)."""
    w, h = size
    sw, sh = max(1, -(-w // cell)), max(1, -(-h // cell))
    im = Image.frombytes("L", (sw, sh), rng.randbytes(sw * sh))
    if (sw, sh) != (w, h):
        im = im.resize((w, h), Image.BICUBIC)
    if blur:
        im = im.filter(ImageFilter.GaussianBlur(blur))
    return ImageOps.autocontrast(im) if stretch else im


def lerp(a, b, t):
    return a + (b - a) * t


def lerp_pt(a, b, t):
    return (a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t)


def lerp_rgb(a, b, t):
    return tuple(int(round(a[i] + (b[i] - a[i]) * t)) for i in range(3))


def clamp01(t):
    return max(0.0, min(1.0, t))


def smooth(t):
    t = clamp01(t)
    return t * t * (3 - 2 * t)


def ramp(stops, t):
    t = min(max(t, stops[0][0]), stops[-1][0])
    for (t0, c0), (t1, c1) in zip(stops, stops[1:]):
        if t <= t1:
            return lerp_rgb(c0, c1, (t - t0) / ((t1 - t0) or 1))
    return stops[-1][1]


def map_l(mask, stops):
    lut = [ramp(stops, v / 255) for v in range(256)]
    return Image.merge("RGB", [mask.point([lut[v][c] for v in range(256)]) for c in range(3)])


def linear_light(size, angle):
    w, h = size
    d = int(math.hypot(w, h)) + 4
    g = Image.linear_gradient("L").resize((d, d)).rotate(angle, resample=Image.BICUBIC)
    return g.crop(((d - w) // 2, (d - h) // 2, (d - w) // 2 + w, (d - h) // 2 + h))


def radial_light(size, center, radius, power=1.0, steps=40):
    """L image: 255 at `center`, falling to 0 at `radius`."""
    m = Image.new("L", size, 0)
    d = ImageDraw.Draw(m)
    cx, cy = center
    for k in range(steps):
        r = radius * (1 - k / steps)
        d.ellipse((cx - r, cy - r, cx + r, cy + r), fill=int(255 * ((k + 1) / steps) ** power))
    return m.filter(ImageFilter.GaussianBlur(max(1.0, radius / steps * 1.5)))


def gold_paint(size, mask, rng, light=None, angle=25, bands=1.5):
    """Gold leaf (deep to bright sheen, sparse bright specks), shaped by mask."""
    light = light if light is not None else linear_light(size, angle)
    sheen = map_l(light.point(lambda v: int(255 * (0.18 + 0.72 * abs(math.sin(v / 255 * math.pi * bands))))),
                  [(0, GOLD_DEEP), (0.55, GOLD), (0.88, (250, 220, 156)), (1, GOLD_CORE)])
    speck = noise_l(size, rng, stretch=False).point(lambda v: 255 if v > 247 else 0).filter(ImageFilter.GaussianBlur(0.6))
    sheen = Image.composite(Image.new("RGB", size, GOLD_CORE), sheen, speck.point(lambda v: v // 2))
    out = sheen.convert("RGBA")
    out.putalpha(mask)
    return out


def paper_fill(size, mask, rng, base=BONE, amount=0.18):
    w, h = size
    grain = noise_l(size, rng, cell=1, blur=1.0)
    fibre = noise_l((max(1, w // 6), h), rng, blur=1.5).resize(size, Image.BICUBIC)
    shade = ImageChops.add(grain, fibre, 2.0, 0)
    tex = ImageChops.multiply(Image.new("RGB", size, base), Image.merge("RGB", [shade.point(lambda v: 150 + v * 105 // 255)] * 3))
    out = Image.blend(Image.new("RGB", size, base), tex, amount).convert("RGBA")
    out.putalpha(mask)
    return out


def ellipse_mask(size, c, rx, ry, fill=255):
    m = Image.new("L", size, 0)
    ImageDraw.Draw(m).ellipse((c[0] - rx, c[1] - ry, c[0] + rx, c[1] + ry), fill=fill)
    return m


def rough_blob(size, c, rx, ry, rng, wobble=0.03, jitter=0.004, steps=200):
    """An ellipse whose rim wanders like a hand-cut brush edge."""
    harm = [(k, rng.uniform(0, 2 * math.pi), rng.uniform(0.35, 1.0) / k) for k in range(2, 7)]
    pts = []
    for i in range(steps):
        a = 2 * math.pi * i / steps
        f = 1 + wobble * sum(amp * math.sin(k * a + ph) for k, ph, amp in harm) + rng.uniform(-jitter, jitter)
        pts.append((c[0] + math.cos(a) * rx * f, c[1] + math.sin(a) * ry * f))
    m = Image.new("L", size, 0)
    ImageDraw.Draw(m).polygon(pts, fill=255)
    return m


def ring_mask(size, c, r_in, r_out):
    m = Image.new("L", size, 0)
    d = ImageDraw.Draw(m)
    d.ellipse((c[0] - r_out, c[1] - r_out, c[0] + r_out, c[1] + r_out), fill=255)
    if r_in > 0:
        d.ellipse((c[0] - r_in, c[1] - r_in, c[0] + r_in, c[1] + r_in), fill=0)
    return m


def grow(mask, px):
    if px <= 4:
        return mask.filter(ImageFilter.MaxFilter(2 * int(px) + 1))
    return mask.filter(ImageFilter.GaussianBlur(px / 1.5)).point(lambda v: 255 if v > 17 else 0)


def shrink(mask, px):
    if px <= 4:
        return mask.filter(ImageFilter.MinFilter(2 * int(px) + 1))
    return mask.filter(ImageFilter.GaussianBlur(px / 1.5)).point(lambda v: 255 if v > 238 else 0)


def union(*masks):
    out = masks[0]
    for m in masks[1:]:
        out = ImageChops.lighter(out, m)
    return out


def shift(mask, dx, dy):
    out = Image.new("L", mask.size, 0)
    out.paste(mask, (int(dx), int(dy)))
    return out


def star(size, c, r, alpha=255, thin=0.18):
    """A four-point sparkle (L mask)."""
    m = Image.new("L", size, 0)
    x, y = c
    t = r * thin
    ImageDraw.Draw(m).polygon([(x - r, y), (x - t, y - t), (x, y - r), (x + t, y - t), (x + r, y), (x + t, y + t), (x, y + r),
                               (x - t, y + t)], fill=alpha)
    return m


def poly_mask(size, pts, fill=255):
    m = Image.new("L", size, 0)
    ImageDraw.Draw(m).polygon(pts, fill=fill)
    return m


def line_mask(size, pts, width, fill=255, caps=True):
    m = Image.new("L", size, 0)
    d = ImageDraw.Draw(m)
    d.line(pts, fill=fill, width=max(1, int(round(width))), joint="curve")
    if caps:
        r = width / 2
        for x, y in (pts[0], pts[-1]):
            d.ellipse((x - r, y - r, x + r, y + r), fill=fill)
    return m


def taper_mask(size, pts, widths, fill=255, draw=None):
    """A line whose width changes point by point (round joints)."""
    m = Image.new("L", size, 0) if draw is None else None
    d = draw or ImageDraw.Draw(m)
    r = widths[0] / 2
    d.ellipse((pts[0][0] - r, pts[0][1] - r, pts[0][0] + r, pts[0][1] + r), fill=fill)
    for (a, b), w in zip(zip(pts, pts[1:]), widths[1:]):
        d.line([a, b], fill=fill, width=max(1, int(round(w))))
        r = w / 2
        d.ellipse((b[0] - r, b[1] - r, b[0] + r, b[1] + r), fill=fill)
    return m


def ink_edge(size, mask, px, rgb=INK):
    return solid(size, rgb, ImageChops.subtract(grow(mask, px), mask))


def edge_fade(size, border):
    """Opaque inside, fading to clear over `border` px at every edge, so frames never show a square."""
    w, h = size
    m = Image.new("L", size, 0)
    d = ImageDraw.Draw(m)
    steps = 16
    for k in range(steps + 1):
        inset = border * k / steps  # outermost (clear) first, each inset rectangle a little more opaque
        d.rectangle((inset, inset, w - 1 - inset, h - 1 - inset), fill=int(255 * k / steps))
    return m


def downsample(img, f):
    return img.resize((img.width // f, img.height // f), Image.LANCZOS)


def save(img, out_dir, rel):
    p = Path(out_dir) / rel
    p.parent.mkdir(parents=True, exist_ok=True)
    img.save(p, optimize=True)
    return p


def arc_pts(c, r, a0, a1, n=None, ry=None):
    """Points along an arc (degrees, 0 = east, clockwise on screen)."""
    n = n or max(8, int(abs(a1 - a0) / 2.5))
    ry = r if ry is None else ry
    return [(c[0] + math.cos(math.radians(a0 + (a1 - a0) * i / n)) * r, c[1] + math.sin(math.radians(a0 + (a1 - a0) * i / n)) * ry)
            for i in range(n + 1)]


def resample(pts, n=24):
    """Evenly spaced points along a polyline, so brush strokes swell and taper smoothly."""
    seg = [math.hypot(b[0] - a[0], b[1] - a[1]) for a, b in zip(pts, pts[1:])]
    total = sum(seg) or 1.0
    out, acc, j = [], 0.0, 0
    for i in range(n):
        d = total * i / (n - 1)
        while j < len(seg) - 1 and acc + seg[j] < d:
            acc += seg[j]
            j += 1
        t = clamp01((d - acc) / (seg[j] or 1.0))
        out.append(lerp_pt(pts[j], pts[j + 1], t))
    return out


def sparkles(size, pts, rgb=GOLD_CORE, glow_rgb=GOLD, glow_r=4):
    """Four-point sparkles at (x, y, r) with a soft glow."""
    m = Image.new("L", size, 0)
    for x, y, r in pts:
        m = ImageChops.lighter(m, star(size, (x, y), r))
    return comp(glow(m, glow_rgb, glow_r, 1.3), solid(size, rgb, m))


def strip(frames, cell):
    """Lay equal frames out left to right; returns (sheet, sprites) with sprites named by frame."""
    sheet = canvas(cell * len(frames), cell)
    sprites = []
    for i, (name, img) in enumerate(frames):
        sheet.alpha_composite(img, (i * cell, 0))
        sprites.append({"name": name, "box": [i * cell, 0, (i + 1) * cell, cell]})
    return sheet, sprites


# ------------------------------------------------------------------------------------------------ get back up (Verso)
# A generic figure rig, in 256px cell units: four key poses from down on hands and knees to standing with a fist raised.
JOINTS = ("hip", "neck", "head", "knee_f", "foot_f", "knee_b", "foot_b", "elbow_l", "hand_l", "elbow_r", "hand_r")
POSES = [
    # down: on hands and knees, head bowed
    dict(hip=(100, 178), neck=(146, 168), head=(163, 180), knee_f=(118, 206), foot_f=(92, 212), knee_b=(96, 207), foot_b=(70, 212),
         elbow_l=(146, 190), hand_l=(152, 211), elbow_r=(162, 190), hand_r=(172, 211)),
    # kneeling on one knee, a hand on the other
    dict(hip=(116, 166), neck=(130, 112), head=(135, 91), knee_f=(152, 160), foot_f=(156, 212), knee_b=(112, 208), foot_b=(82, 211),
         elbow_l=(150, 134), hand_l=(154, 156), elbow_r=(112, 140), hand_r=(106, 168)),
    # pushing up
    dict(hip=(124, 138), neck=(130, 84), head=(133, 62), knee_f=(150, 166), foot_f=(152, 212), knee_b=(114, 178), foot_b=(100, 212),
         elbow_l=(150, 110), hand_l=(152, 134), elbow_r=(110, 104), hand_r=(102, 128)),
    # standing, one fist raised
    dict(hip=(128, 134), neck=(128, 78), head=(128, 56), knee_f=(139, 173), foot_f=(146, 212), knee_b=(118, 173), foot_b=(108, 212),
         elbow_l=(110, 104), hand_l=(104, 130), elbow_r=(150, 64), hand_r=(156, 38)),
]
V_KEYS = [0.0, 0.85, 1.65, 2.45, 3.0]       # pose per frame
V_REVEAL = [0.1, 0.36, 0.62, 0.86, 1.0]     # how far the figure is put back together per frame


def pose_at(k):
    i = min(int(k), len(POSES) - 2)
    t = smooth(k - i)
    return {j: lerp_pt(POSES[i][j], POSES[i + 1][j], t) for j in JOINTS}


def _norm(dx, dy):
    ln = math.hypot(dx, dy) or 1.0
    return dx / ln, dy / ln


def figure(p, F):
    """Masks and geometry for the rig at scale F: limbs as (points, widths), torso polygon, head (c, r), coat polygon."""
    P = {j: (p[j][0] * F, p[j][1] * F) for j in JOINTS}
    dx, dy = _norm(P["hip"][0] - P["neck"][0], P["hip"][1] - P["neck"][1])
    px, py = -dy, dx                                   # points to the figure's back (screen left when upright)
    sh = (P["neck"][0] + dx * 6 * F, P["neck"][1] + dy * 6 * F)
    hp = P["hip"]

    def at(base, a, b):
        return (base[0] + px * a * F + dx * b * F, base[1] + py * a * F + dy * b * F)

    torso = [at(sh, 14, -2), at(sh, -14, -2), at(hp, -11, 2), at(hp, 11, 2)]
    coat = [at(hp, 12, -8), at(hp, 19, 26), at(hp, 4, 30), at(hp, -10, 26), at(hp, -12, -8)]
    sl, sr = at(sh, 8, 2), at(sh, -8, 2)
    hl, hr = at(hp, 5, 0), at(hp, -5, 0)
    limbs = [
        ([hl, P["knee_b"], P["foot_b"]], [15 * F, 13 * F, 10 * F]),
        ([hr, P["knee_f"], P["foot_f"]], [15 * F, 13 * F, 10 * F]),
        ([sl, P["elbow_l"], P["hand_l"]], [11 * F, 9 * F, 8 * F]),
        ([sr, P["elbow_r"], P["hand_r"]], [11 * F, 9 * F, 8 * F]),
        ([P["neck"], P["head"]], [9 * F, 9 * F]),
    ]
    feet = [(P["foot_b"], -1 if P["foot_b"][0] < P["knee_b"][0] - 6 * F else 1), (P["foot_f"], 1)]
    return dict(P=P, torso=torso, coat=coat, limbs=limbs, head=(P["head"], 13.5 * F), feet=feet, dir=(dx, dy), perp=(px, py),
                sh=sh)


def figure_mask(size, g, F):
    m = Image.new("L", size, 0)
    d = ImageDraw.Draw(m)
    d.polygon(g["torso"], fill=255)
    d.polygon(g["coat"], fill=255)
    for pts, ws in g["limbs"]:
        taper_mask(size, pts, ws, draw=d)
    (hx, hy), hr = g["head"]
    d.ellipse((hx - hr, hy - hr, hx + hr, hy + hr), fill=255)
    for (fx, fy), s in g["feet"]:
        d.ellipse((fx - 6 * F + s * 4 * F, fy - 4 * F, fx + 6 * F + s * 4 * F, fy + 3 * F), fill=255)
    for (x, y) in (g["P"]["hand_l"], g["P"]["hand_r"]):
        d.ellipse((x - 6 * F, y - 6 * F, x + 6 * F, y + 6 * F), fill=255)
    return m.filter(ImageFilter.GaussianBlur(0.6 * F)).point(lambda v: 255 if v > 110 else 0)


def seam_lines(g, F, rng_seed):
    """Zigzag mend lines across every limb, the torso and the head; they follow the pose because they are placed
    along each limb, and the same seed keeps their jags the same from frame to frame."""
    rng = random.Random(rng_seed)
    out = []

    def across(c, ux, uy, half, n=4):
        vx, vy = -uy, ux
        pts = []
        for i in range(n + 1):
            t = -1 + 2 * i / n
            j = rng.uniform(-2.2, 2.2) * F
            pts.append((c[0] + vx * half * t + ux * j, c[1] + vy * half * t + uy * j))
        return pts

    for pts, ws in g["limbs"][:4]:
        for (a, b), w, t in zip(zip(pts, pts[1:]), ws[1:], (0.55, 0.45)):
            ux, uy = _norm(b[0] - a[0], b[1] - a[1])
            out.append(across(lerp_pt(a, b, t), ux, uy, w * 0.85, 3))
    dx, dy = g["dir"]
    for t in (0.3, 0.72):
        c = lerp_pt(g["sh"], g["P"]["hip"], t)
        out.append(across(c, dx, dy, 15 * F, 5))
    # one long seam down the back of the torso
    a, b = g["sh"], g["P"]["hip"]
    px, py = g["perp"]
    spine = []
    for i in range(6):
        c = lerp_pt(a, b, 0.08 + 0.84 * i / 5)
        o = (3 if i % 2 else -3) * F + rng.uniform(-1, 1) * F
        spine.append((c[0] + px * o, c[1] + py * o))
    out.append(spine)
    (hx, hy), hr = g["head"]
    out.append([(hx - hr * 0.9, hy - hr * 0.2), (hx - hr * 0.3, hy + hr * 0.15), (hx + hr * 0.2, hy - hr * 0.25), (hx + hr * 0.9, hy + hr * 0.1)])
    return out


def get_back_up_safe(i, F=2):
    """Light knitting the figure back together: a ghost of light fills in from the feet up as an ink silhouette with
    glowing gold seams, while loose threads of light pull in from all around."""
    S = 256 * F
    size = (S, S)
    rng = random.Random(SEED + 10 + i)
    r = V_REVEAL[i]
    g = figure(pose_at(V_KEYS[i]), F)
    body = figure_mask(size, g, F)
    x0, y0, x1, y1 = body.getbbox()
    cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
    # The knit front climbs from the feet to the crown.
    front = y1 - (y1 - y0 + 16 * F) * r if i < 4 else -10 * F
    band = 7 * F
    col = Image.new("L", (1, S), 0)
    col.putdata([int(255 * clamp01((y - front) / band)) for y in range(S)])
    knit = col.resize(size)
    solid_part = ImageChops.multiply(body, knit)
    ghost = ImageChops.subtract(body, solid_part)

    img = canvas(*size)
    foot_y = max(g["P"]["foot_f"][1], g["P"]["foot_b"][1]) + 2 * F
    pool = ellipse_mask(size, (cx, foot_y), (46 + 30 * r) * F, (9 + 4 * r) * F).filter(ImageFilter.GaussianBlur(6 * F))
    img = comp(img, solid(size, GOLD, scale_l(pool, 0.35 + 0.45 * r)))
    if i == 4:
        # a burst of light behind the risen figure
        rays = Image.new("L", size, 0)
        rd = ImageDraw.Draw(rays)
        hc = (cx, cy - 10 * F)
        for k in range(14):
            a = math.radians(k * 360 / 14 + 6)
            ln = (92 if k % 2 else 70) * F
            rd.polygon([hc, (hc[0] + math.cos(a - 0.08) * ln, hc[1] + math.sin(a - 0.08) * ln),
                        (hc[0] + math.cos(a + 0.08) * ln, hc[1] + math.sin(a + 0.08) * ln)], fill=150)
        rays = ImageChops.multiply(rays.filter(ImageFilter.GaussianBlur(2.5 * F)), radial_light(size, hc, 104 * F, 0.8))
        img = comp(img, solid(size, GOLD, rays), glow(body, GOLD_CORE, 9 * F, 1.0))
    img = comp(img, glow(body, GOLD, (10 + 6 * r) * F, 0.45 + 0.55 * r))

    # ghost: a pale body of light, cross-hatched like a loom, with a dotted rim
    hatch = Image.new("L", size, 0)
    hd = ImageDraw.Draw(hatch)
    step = 7 * F
    for k in range(-S, 2 * S, step):
        hd.line([(k, 0), (k + S, S)], fill=200, width=max(1, F))
        hd.line([(k + S, 0), (k, S)], fill=150, width=max(1, F))
    img = comp(img, glow(ghost, GOLD_DEEP, 3 * F, 0.35), solid(size, GOLD, scale_l(ghost, 0.3)))
    img = comp(img, solid(size, GOLD_CORE, ImageChops.multiply(hatch, scale_l(ghost, 0.85))))
    dots = Image.new("L", size, 0)
    dd = ImageDraw.Draw(dots)
    for k in range(-S, 2 * S, 5 * F):
        dd.line([(k, 0), (k + S, S)], fill=255, width=3 * F)
    rim = ImageChops.multiply(outline_of(ghost.point(lambda v: 255 if v > 128 else 0), max(1, F)), dots)
    rim = ImageChops.multiply(rim, ImageChops.invert(grow(solid_part, 2 * F)))
    img = comp(img, solid(size, BONE, rim))

    # knit part: an ink silhouette with a bone rim
    img = comp(img, solid(size, INK, ImageChops.multiply(grow(solid_part, 1.5 * F), knit)), solid(size, INK, solid_part))
    lit = ImageChops.multiply(ImageChops.subtract(solid_part, shift(solid_part, 2 * F, 3 * F)), scale_l(solid_part, 1))
    img = comp(img, solid(size, GOLD, scale_l(lit, 0.8)))

    # seams of gold: blazing where knit, faint threads in the ghost
    seams = Image.new("L", size, 0)
    sd = ImageDraw.Draw(seams)
    for pts in seam_lines(g, F, SEED + 99):
        sd.line(pts, fill=255, width=int(2.4 * F), joint="curve")
    seams = ImageChops.multiply(seams, grow(body, F))
    hot = ImageChops.multiply(seams, knit)
    img = comp(img, glow(hot, GOLD, 3 * F, 1.6), solid(size, GOLD_CORE, hot), solid(size, GOLD_CORE, scale_l(ImageChops.subtract(seams, hot), 0.7)))

    # loose threads of light being drawn in, each ending on the body near the knit front
    nthreads = [16, 12, 9, 5, 0][i]
    threads = Image.new("L", size, 0)
    heads = []
    td = ImageDraw.Draw(threads)
    for k in range(nthreads):
        a = rng.uniform(0, 2 * math.pi)
        R = rng.uniform(78, 112) * F * (1 - 0.25 * r)
        start = (S / 2 + math.cos(a) * R, S * 0.52 + math.sin(a) * R * 0.85)
        ty = clamp01(rng.uniform(-0.2, 0.25) + (front - y0) / max(1, y1 - y0)) * (y1 - y0) + y0
        row = body.crop((0, int(ty), S, int(ty) + 1)).getbbox()
        tx = rng.uniform(row[0], row[2]) if row else cx
        ctrl = lerp_pt(start, (tx, ty), 0.5)
        ctrl = (ctrl[0] + rng.uniform(-30, 30) * F, ctrl[1] + rng.uniform(-30, 10) * F)
        pts = curve(start, ctrl, (tx, ty), 30)
        td.line(pts, fill=rng.randint(150, 230), width=max(1, int(1.3 * F)), joint="curve")
        heads.append((start[0], start[1], rng.uniform(3.5, 6) * F))
    threads = threads.filter(ImageFilter.GaussianBlur(0.4 * F))
    img = comp(img, glow(threads, GOLD_DEEP, 1.5 * F, 0.6), glow(threads, GOLD, 2 * F, 1.2), solid(size, GOLD_CORE, threads))
    if heads:
        img = comp(img, sparkles(size, heads, glow_r=3 * F))
    # sparks running along the knit front
    if i < 4:
        row = body.crop((0, int(front), S, int(front) + 1)).getbbox()
        if row:
            sp = [(rng.uniform(row[0], row[2]), front + rng.uniform(-2, 2) * F, rng.uniform(4, 7) * F) for _ in range(3)]
            img = comp(img, sparkles(size, sp, glow_r=4 * F))
    else:
        sp = [(cx + math.cos(a) * rr * F, cy - 10 * F + math.sin(a) * rr * F, s * F)
              for a, rr, s in ((-2.4, 70, 7), (-0.6, 76, 5), (0.5, 66, 6), (2.6, 62, 4.5), (-1.5, 88, 5.5))]
        img = comp(img, sparkles(size, sp, glow_r=4 * F))
    # motes of light
    motes = Image.new("L", size, 0)
    md = ImageDraw.Draw(motes)
    for _ in range([26, 20, 15, 10, 14][i]):
        a = rng.uniform(0, 2 * math.pi)
        R = rng.uniform(40, 112) * F
        x, y = S / 2 + math.cos(a) * R, S * 0.5 + math.sin(a) * R * 0.85
        rr = rng.uniform(1.2, 2.6) * F
        md.ellipse((x - rr, y - rr, x + rr, y + rr), fill=rng.randint(140, 255))
    img = comp(img, glow(motes, GOLD, 2 * F, 1.2), solid(size, GOLD_CORE, motes))
    # A soft dark backing under the light (the ghost, its seams, the loose threads and motes), so the cream and gold
    # threads still read on a light parchment board. It shows most in the early frames, where there is no ink body yet.
    lights = ImageChops.lighter(ImageChops.lighter(ghost, grow(threads.point(lambda v: 255 if v > 40 else 0), 2 * F)),
                                ImageChops.lighter(grow(motes, 2 * F), grow(seams, F)))
    backing = solid(size, NIGHT, scale_l(lights.filter(ImageFilter.GaussianBlur(4 * F)), 0.42))
    img = comp(backing, img)
    return downsample(clip(img, edge_fade(size, 14 * F)), F)


def figure_strokes(g, F, rng):
    """The figure as loose brushstrokes: (points, width, colour, alpha). Same rng seed -> same strokes every frame."""
    P = g["P"]
    out = []
    dx, dy = g["dir"]
    px, py = g["perp"]
    for pts, ws in g["limbs"][:4]:
        out.append((pts, ws[0] * 1.15, INK, 250))
        out.append(([lerp_pt(pts[0], pts[1], 0.3), pts[1], lerp_pt(pts[1], pts[2], 0.7)], ws[1] * 0.45, INK_SOFT, 230))
    for k, o in enumerate((-8, 0, 8)):
        a = (g["sh"][0] + px * o * F, g["sh"][1] + py * o * F)
        b = (P["hip"][0] + px * o * 0.8 * F + dx * 6 * F, P["hip"][1] + py * o * 0.8 * F + dy * 6 * F)
        out.append(([a, lerp_pt(a, b, 0.5), b], 14 * F, INK if k != 1 else INK_SOFT, 250))
    # coat tail
    c = g["coat"]
    out.append(([lerp_pt(c[0], c[4], 0.5), lerp_pt(c[1], c[3], 0.5), c[2]], 22 * F, INK, 245))
    out.append(([c[0], c[1]], 8 * F, INK, 245))
    # head as a round dab
    (hx, hy), hr = g["head"]
    out.append((arc_pts((hx, hy), hr * 0.45, 200, 540, n=24), hr * 1.25, INK, 250))
    # crimson sash at the waist and a scarf streaming from the neck
    w0 = lerp_pt(g["sh"], P["hip"], 0.78)
    out.append(([(w0[0] + px * 15 * F, w0[1] + py * 15 * F), w0, (w0[0] - px * 15 * F, w0[1] - py * 15 * F)], 8 * F, CRIMSON, 250))
    n0 = P["neck"]
    out.append(([(n0[0] - px * 7 * F, n0[1] - py * 7 * F), (n0[0] + px * 12 * F, n0[1] + py * 12 * F + 4 * F),
                 (n0[0] + px * 30 * F - dx * 2 * F, n0[1] + py * 30 * F + 12 * F)], 9 * F, CRIMSON, 245))
    # gold accents
    for pts, ws in g["limbs"][:2]:
        out.append(([lerp_pt(pts[0], pts[1], 0.2), lerp_pt(pts[0], pts[1], 0.85)], 3 * F, GOLD, 220))
    out.append(([lerp_pt(g["sh"], P["hip"], 0.05), lerp_pt(g["sh"], P["hip"], 0.6)], 3 * F, GOLD, 220))
    return out


def get_back_up_finished(i, F=2):
    """The painted figure reassembling: loose strokes fly in from all around, trailing smears, and land in place."""
    S = 256 * F
    size = (S, S)
    rng = random.Random(SEED + 30 + i)
    r = [0.0, 0.34, 0.62, 0.86, 1.0][i]
    g = figure(pose_at(V_KEYS[i]), F)
    strokes = figure_strokes(g, F, rng)
    srng = random.Random(SEED + 31)               # the same scatter for every frame
    plan = []
    for _ in strokes:
        a = srng.uniform(0, 2 * math.pi)
        plan.append(dict(v=(math.cos(a) * srng.uniform(50, 95) * F, math.sin(a) * srng.uniform(40, 80) * F),
                         rot=srng.uniform(-80, 80), delay=srng.uniform(0.0, 0.4), seed=srng.randrange(1 << 30)))

    def place(pts, pl, s):
        cxs = sum(x for x, _ in pts) / len(pts)
        cys = sum(y for _, y in pts) / len(pts)
        e = (1 - s) ** 1.4
        a = math.radians(pl["rot"] * e)
        ca, sa = math.cos(a), math.sin(a)
        tx, ty = cxs + pl["v"][0] * e, cys + pl["v"][1] * e
        # keep scattered strokes inside the cell
        tx = min(max(tx, 40 * F), S - 40 * F)
        ty = min(max(ty, 40 * F), S - 30 * F)
        k = 1 - 0.25 * e
        return [(tx + ((x - cxs) * ca - (y - cys) * sa) * k, ty + ((x - cxs) * sa + (y - cys) * ca) * k) for x, y in pts]

    body = canvas(*size)
    trails = canvas(*size)
    flecks = Image.new("L", size, 0)
    fd = ImageDraw.Draw(flecks)
    red_flecks = Image.new("L", size, 0)
    rfd = ImageDraw.Draw(red_flecks)
    for (pts, w, rgb, al), pl in zip(strokes, plan):
        pts = resample(pts) if len(pts) < 12 else pts
        s = smooth((r - pl["delay"]) / (1 - pl["delay"])) if r < 1 else 1.0
        if r == 0:
            s = 0.0
        q = place(pts, pl, s)
        body = comp(body, brush_stroke(size, q, w, rgb, al, seed=pl["seed"], bristles=8))
        if s < 0.98:
            for k, back in enumerate((0.1, 0.2)):
                qb = place(pts, pl, max(0.0, s - back))
                trails = comp(trails, brush_stroke(size, qb, w * 0.8, rgb if rgb != INK else INK_SOFT, int(70 - 25 * k),
                                                   seed=pl["seed"] + k + 1, bristles=5))
            # flung droplets behind the moving stroke
            cxs = sum(x for x, _ in q) / len(q)
            cys = sum(y for _, y in q) / len(q)
            for _ in range(1):
                d = rng.uniform(10, 26) * F
                x, y = cxs + pl["v"][0] / max(1.0, math.hypot(*pl["v"])) * d + rng.uniform(-6, 6) * F, cys + pl["v"][1] / max(1.0, math.hypot(*pl["v"])) * d + rng.uniform(-6, 6) * F
                rr = rng.uniform(1.2, 2.4) * F
                (rfd if rgb == CRIMSON else fd).ellipse((x - rr, y - rr, x + rr, y + rr), fill=230)
    a = body.getchannel("A")
    img = canvas(*size)
    if i == 4:
        bb = a.getbbox()
        hc = ((bb[0] + bb[2]) / 2, (bb[1] + bb[3]) / 2)
        img = comp(img, solid(size, GOLD, scale_l(radial_light(size, hc, 100 * F, 1.3), 0.8)))
        # a splash ring of paint at the feet
        foot_y = max(g["P"]["foot_f"][1], g["P"]["foot_b"][1])
        for k in range(26):
            ang = rng.uniform(0, 2 * math.pi)
            rr = rng.uniform(46, 78) * F
            x, y = hc[0] + math.cos(ang) * rr, foot_y - 2 * F + math.sin(ang) * rr * 0.28
            sz = rng.uniform(1.6, 4.2) * F
            (rfd if k % 3 == 0 else fd).ellipse((x - sz, y - sz * 0.7, x + sz, y + sz * 0.7), fill=240)
    img = comp(img, glow(a, GOLD, (7 + 5 * r) * F, 0.5 + 0.5 * r), trails)
    img = comp(img, solid(size, BONE, scale_l(ImageChops.subtract(grow(a.point(lambda v: 255 if v > 90 else 0), F), a), 0.6)))
    img = comp(img, body)
    img = comp(img, glow(flecks, GOLD, 1.5 * F, 0.8), solid(size, GOLD, flecks), solid(size, CRIMSON, red_flecks))
    if i == 4:
        bb = a.getbbox()
        sp = [(bb[0] - 8 * F, bb[1] + 30 * F, 6 * F), (bb[2] + 10 * F, bb[1] + 14 * F, 7 * F), (bb[2] + 2 * F, bb[3] - 40 * F, 4.5 * F)]
        img = comp(img, sparkles(size, sp, glow_r=4 * F))
    return downsample(clip(img, edge_fade(size, 14 * F)), F)


def get_back_up():
    CELL = 256
    sheet = canvas(CELL * 5, CELL * 2)
    sprites = []
    for row, (mode, fn) in enumerate((("safe", get_back_up_safe), ("finished", get_back_up_finished))):
        for i in range(5):
            sheet.alpha_composite(fn(i), (i * CELL, row * CELL))
            sprites.append({"name": f"{mode}-{i + 1}", "box": [i * CELL, row * CELL, (i + 1) * CELL, (row + 1) * CELL]})
    return sheet, sprites


# ----------------------------------------------------------------------------------------------------- sit (Esquie)
E_FRAMES = [
    # name, body centre y, sx, sy, guard squash, dust ring (rx, ry, alpha), face
    ("drop", 76, 0.9, 1.12, 1.0, None, "wheee"),
    ("squash", 160, 1.3, 0.74, 0.42, (70, 12, 1.0), "squeeze"),
    ("rebound", 130, 0.95, 1.08, 0.55, (80, 17, 1.0), "grin"),
    ("settle", 148, 1.07, 0.94, 0.48, (92, 21, 0.8), "happy"),
    ("rest", 145, 1.0, 0.99, 0.48, (100, 24, 0.4), "content"),
]
E_R = 54


def guard(size, x, base, q, F, rng, flail=0.0, look=0.0):
    """A generic wooden guard pawn with a little iron helmet, squashed to q of its height."""
    img = canvas(*size)
    w = 1 / math.sqrt(q)
    H = 38 * q
    bw, tw = 12 * w, 8 * w
    body = poly_mask(size, [((x - bw) * F, base * F), ((x - tw) * F, (base - H * 0.62) * F), ((x + tw) * F, (base - H * 0.62) * F),
                            ((x + bw) * F, base * F)])
    hr_x, hr_y = 9 * w, 9 * q
    hc = (x * F, (base - H * 0.62 - hr_y * 0.8) * F)
    head = ellipse_mask(size, hc, hr_x * F, hr_y * F)
    arms = Image.new("L", size, 0)
    if flail:
        for s in (-1, 1):
            a0 = ((x + s * tw * 0.9) * F, (base - H * 0.45) * F)
            a1 = ((x + s * (tw + 9)) * F, (base - H * 0.45 - 7 * flail) * F)
            arms = union(arms, line_mask(size, [a0, a1], 3.4 * F))
    shape = union(body, head, arms).filter(ImageFilter.GaussianBlur(0.5 * F)).point(lambda v: 255 if v > 120 else 0)
    img = comp(img, ink_edge(size, shape, 2 * F))
    grad = linear_light(size, 200)
    wood = map_l(grad, [(0, WOOD_DEEP), (0.5, WOOD), (1, WOOD_LIGHT)]).convert("RGBA")
    wood.putalpha(shape)
    img = comp(img, wood)
    hl = ImageChops.multiply(shape, shift(ImageChops.invert(shape), 2 * F, 0))
    img = comp(img, solid(size, WOOD_LIGHT, scale_l(hl, 0.8)))
    helm = ImageChops.multiply(ellipse_mask(size, (hc[0], hc[1] - hr_y * 0.25 * F), hr_x * 1.12 * F, hr_y * 0.9 * F),
                               poly_mask(size, [(0, 0), (size[0], 0), (size[0], hc[1] + hr_y * 0.05 * F), (0, hc[1] + hr_y * 0.05 * F)]))
    brim = ellipse_mask(size, (hc[0], hc[1]), hr_x * 1.3 * F, max(1.5, 2.2 * q) * F)
    helm = union(helm, brim)
    img = comp(img, ink_edge(size, helm, 1.5 * F), solid(size, IRON, helm))
    img = comp(img, solid(size, IRON_LIGHT, ImageChops.multiply(helm, shift(ImageChops.invert(helm), 1.5 * F, 1.5 * F))))
    if q > 0.6:
        ey = hc[1] + hr_y * 0.35 * F
        eye = Image.new("L", size, 0)
        ed = ImageDraw.Draw(eye)
        for s in (-1, 1):
            ex = hc[0] + s * 3.4 * F
            ed.ellipse((ex - 1.4 * F, ey - 1.9 * F + look * F, ex + 1.4 * F, ey + 1.9 * F + look * F), fill=255)
        img = comp(img, solid(size, INK, eye))
    return img


def puff(size, c, r, F, rng):
    """One comic dust puff: a few overlapping round lobes."""
    m = Image.new("L", size, 0)
    d = ImageDraw.Draw(m)
    for k in range(4):
        a = rng.uniform(0, 2 * math.pi)
        o = r * 0.45
        rr = r * rng.uniform(0.6, 0.85)
        x, y = c[0] + math.cos(a) * o, c[1] + math.sin(a) * o * 0.7
        d.ellipse((x - rr, y - rr, x + rr, y + rr), fill=255)
    d.ellipse((c[0] - r * 0.7, c[1] - r * 0.6, c[0] + r * 0.7, c[1] + r * 0.6), fill=255)
    return m


def dust_ring(size, c, rx, ry, alpha, F, rng, half):
    """Puffs around an ellipse at the base; half='back' or 'front' so the body can sit between them."""
    img = canvas(*size)
    n = 13
    lobes = Image.new("L", size, 0)
    for k in range(n):
        a = 2 * math.pi * (k + 0.5 * (rng.random() - 0.5)) / n
        if (math.sin(a) < 0) != (half == "back"):
            continue
        if half == "front" and abs(math.cos(a)) < 0.55:
            continue
        x, y = c[0] + math.cos(a) * rx * F, c[1] + math.sin(a) * ry * F
        r = (9 + 7 * abs(math.cos(a))) * F * (0.8 + rx / 220)
        lobes = union(lobes, puff(size, (x, y - r * 0.3), r, F, rng))
    if not lobes.getbbox():
        return img
    lobes = lobes.filter(ImageFilter.GaussianBlur(0.6 * F)).point(lambda v: 255 if v > 120 else 0)
    img = comp(img, ink_edge(size, lobes, 2 * F))
    img = comp(img, paper_fill(size, lobes, rng, base=(236, 224, 200), amount=0.25))
    shade = ImageChops.multiply(lobes, shift(lobes, -3 * F, -4 * F))
    img = comp(img, solid(size, (200, 182, 156), scale_l(ImageChops.subtract(lobes, shade), 0.9)))
    return fade(img, alpha)


def big_friend(size, c, sx, sy, face, F, rng):
    """The big round friendly shape: a bone-white ball with stubby nubs, a gold rim light and a simple face."""
    R = E_R * F
    rx, ry = R * sx, R * sy
    img = canvas(*size)
    body = rough_blob(size, c, rx, ry, random.Random(SEED + 61), wobble=0.012, jitter=0.0015)
    # stubby feet poking forward and little arm nubs
    feet = Image.new("L", size, 0)
    fd = ImageDraw.Draw(feet)
    for s in (-1, 1):
        fx, fy = c[0] + s * rx * 0.42, c[1] + ry * 0.9
        fd.ellipse((fx - 11 * F, fy - 7 * F, fx + 11 * F, fy + 7 * F), fill=255)
    arms = Image.new("L", size, 0)
    ad = ImageDraw.Draw(arms)
    up = {"wheee": -26, "squeeze": -4, "grin": -14, "happy": 4, "content": 8}[face]
    for s in (-1, 1):
        ax, ay = c[0] + s * rx * 0.96, c[1] + up * F * 0.5
        ad.ellipse((ax - 10 * F, ay - 7 * F + up * F * 0.25, ax + 10 * F, ay + 7 * F + up * F * 0.25), fill=255)
    shape = union(body, feet, arms)
    img = comp(img, solid(size, (0, 0, 0), scale_l(shift(shape, 0, 3 * F).filter(ImageFilter.GaussianBlur(4 * F)), 0.35)))
    img = comp(img, ink_edge(size, shape, 2.5 * F))
    for part, base in ((arms, (232, 222, 204)), (feet, (226, 214, 194))):
        img = comp(img, paper_fill(size, part, rng, base=base, amount=0.2))
    img = comp(img, solid(size, INK, scale_l(ImageChops.subtract(grow(body, 2 * F), body), 0.9)))
    img = comp(img, paper_fill(size, body, rng, base=BONE, amount=0.22))
    # soft shading to the lower right, warm bounce light at the bottom, a gold rim to the upper left
    sh = ImageChops.multiply(body, radial_light(size, (c[0] + rx * 0.5, c[1] + ry * 0.55), max(rx, ry) * 1.1, 1.2))
    img = comp(img, solid(size, (196, 170, 150), scale_l(sh, 0.55)))
    rim = ImageChops.multiply(inner_edge(body, int(4 * F)), shift(ImageChops.invert(body), 5 * F, 5 * F).filter(ImageFilter.GaussianBlur(2 * F)))
    img = comp(img, gold_paint(size, rim, rng, angle=40))
    hi = ellipse_mask(size, (c[0] - rx * 0.38, c[1] - ry * 0.46), rx * 0.26, ry * 0.15).filter(ImageFilter.GaussianBlur(4 * F))
    img = comp(img, solid(size, (255, 252, 244), scale_l(ImageChops.multiply(hi, body), 0.9)))
    # little sit marks: a seam where the body meets the feet
    for s in (-1, 1):
        fx, fy = c[0] + s * rx * 0.42, c[1] + ry * 0.9
        toe = Image.new("L", size, 0)
        ImageDraw.Draw(toe).arc((fx - 8 * F, fy - 5 * F, fx + 8 * F, fy + 6 * F), 200, 340, fill=255, width=int(1.6 * F))
        img = comp(img, solid(size, INK_SOFT, scale_l(toe, 0.7)))
    # face
    fc = (c[0] + rx * 0.06, c[1] - ry * 0.08)
    ink = Image.new("L", size, 0)
    d = ImageDraw.Draw(ink)
    ex = 15 * F * sx
    ey = fc[1] - 6 * F * sy
    lw = int(2.6 * F)
    if face == "wheee":
        for s in (-1, 1):
            d.ellipse((fc[0] + s * ex - 3.6 * F, ey - 6 * F, fc[0] + s * ex + 3.6 * F, ey + 6 * F), fill=255)
        d.ellipse((fc[0] - 5 * F, fc[1] + 9 * F, fc[0] + 5 * F, fc[1] + 19 * F), fill=255)
    elif face == "squeeze":
        for s in (-1, 1):
            x = fc[0] + s * ex
            d.line([(x - s * 5 * F, ey - 4 * F), (x + s * 3 * F, ey), (x - s * 5 * F, ey + 4 * F)], fill=255, width=lw, joint="curve")
        d.line([(fc[0] - 9 * F, fc[1] + 11 * F), (fc[0] - 3 * F, fc[1] + 8 * F), (fc[0] + 3 * F, fc[1] + 11 * F), (fc[0] + 9 * F, fc[1] + 8 * F)],
               fill=255, width=lw, joint="curve")
    elif face == "grin":
        for s in (-1, 1):
            d.ellipse((fc[0] + s * ex - 3.6 * F, ey - 5 * F, fc[0] + s * ex + 3.6 * F, ey + 5 * F), fill=255)
        d.chord((fc[0] - 12 * F, fc[1] - 2 * F, fc[0] + 12 * F, fc[1] + 18 * F), 10, 170, fill=255)
    else:
        for s in (-1, 1):
            x = fc[0] + s * ex
            d.arc((x - 6 * F, ey - 4 * F, x + 6 * F, ey + 6 * F), 195, 345, fill=255, width=lw)
        d.arc((fc[0] - 10 * F, fc[1] + 0 * F, fc[0] + 10 * F, fc[1] + 14 * F), 20, 160, fill=255, width=lw)
    if face == "grin":
        tongue = ImageChops.multiply(ellipse_mask(size, (fc[0], fc[1] + 15 * F), 6 * F, 4 * F), ink)
    img = comp(img, solid(size, INK, ink.filter(ImageFilter.GaussianBlur(0.4 * F))))
    if face == "grin":
        img = comp(img, solid(size, CRIMSON, tongue))
    if face in ("happy", "content", "grin", "squeeze"):
        blush = Image.new("L", size, 0)
        bd = ImageDraw.Draw(blush)
        for s in (-1, 1):
            x = fc[0] + s * (ex + 9 * F)
            bd.ellipse((x - 7 * F, fc[1] + 1 * F, x + 7 * F, fc[1] + 8 * F), fill=170)
        img = comp(img, solid(size, BLUSH, ImageChops.multiply(blush.filter(ImageFilter.GaussianBlur(2 * F)), body)))
    return img


def esquie_frame(i, F=3):
    S = 256 * F
    size = (S, S)
    rng = random.Random(SEED + 50 + i)
    name, cy, sx, sy, q, dust, face = E_FRAMES[i]
    base = 216
    c = (128 * F, (cy + 2) * F)
    img = canvas(*size)
    # ground shadow grows as the body comes down
    shw = 60 + (1 - abs(cy - 150) / 90) * 30
    img = comp(img, solid(size, (0, 0, 0), scale_l(ellipse_mask(size, (128 * F, (base + 1) * F), shw * F, 9 * F).filter(ImageFilter.GaussianBlur(4 * F)), 0.4)))
    ring_c = (128 * F, (base - 4) * F)
    if dust:
        img = comp(img, dust_ring(size, ring_c, dust[0], dust[1], dust[2], F, random.Random(SEED + 70 + i), "back"))
    if i == 1:
        # a comic impact burst behind the squash
        burst = []
        for k in range(24):
            a = 2 * math.pi * k / 24
            rr = (112 if k % 2 == 0 else 84) * F
            burst.append((128 * F + math.cos(a) * rr * 1.05, (base - 40) * F + math.sin(a) * rr * 0.55))
        bm = poly_mask(size, burst)
        img = comp(img, ink_edge(size, bm, 2 * F), gold_paint(size, bm, rng, angle=60, bands=1.0))
        img = comp(img, solid(size, GOLD_CORE, scale_l(ImageChops.multiply(bm, radial_light(size, (128 * F, (base - 40) * F), 80 * F, 0.8)), 0.7)))
    flail = {0: 0.0, 1: 1.0, 2: -0.6, 3: 0.8, 4: 0.5}[i]
    for k, x in enumerate((98, 128, 158)):
        img = comp(img, guard(size, x, base, q, F, rng, flail=flail * (1 if k != 1 else 0), look=-1.4 if i == 0 else 0.0))
    img = comp(img, big_friend(size, c, sx, sy, face, F, rng))
    if dust:
        img = comp(img, dust_ring(size, ring_c, dust[0], dust[1], dust[2], F, random.Random(SEED + 70 + i), "front"))
    marks = Image.new("L", size, 0)
    md = ImageDraw.Draw(marks)
    if i == 0:
        # speed lines above and alarm marks over the guards
        for x, y0, y1 in ((86, 4, 30), (104, 0, 18), (152, 0, 18), (170, 4, 30)):
            md.line([(x * F, y0 * F), (x * F, y1 * F)], fill=255, width=int(3 * F))
        for x in (98, 158):
            md.line([(x * F, 150 * F), (x * F, 161 * F)], fill=255, width=int(3.2 * F))
            md.ellipse(((x - 1.8) * F, 164.5 * F, (x + 1.8) * F, 168 * F), fill=255)
    if i == 1:
        for a in (-160, -140, -40, -20):
            ar = math.radians(a)
            p0 = (128 * F + math.cos(ar) * 90 * F, 168 * F + math.sin(ar) * 50 * F)
            p1 = (128 * F + math.cos(ar) * 112 * F, 168 * F + math.sin(ar) * 62 * F)
            md.line([p0, p1], fill=255, width=int(3 * F))
    if i == 2:
        # little pebbles thrown up
        for x, y, r in ((52, 170, 3.4), (66, 150, 2.6), (196, 160, 3.0), (210, 178, 2.4), (40, 190, 2.2), (220, 196, 2.8)):
            md.ellipse(((x - r) * F, (y - r) * F, (x + r) * F, (y + r) * F), fill=255)
    img = comp(img, solid(size, INK, marks))
    if i >= 3:
        # dizzy stars over the squashed guards, and a happy sparkle
        st = [(70 + (6 if i == 4 else 0), 186, 6.5), (186 - (6 if i == 4 else 0), 182, 7.5)]
        sm = Image.new("L", size, 0)
        for x, y, r in st:
            sm = ImageChops.lighter(sm, star(size, (x * F, y * F), r * F, thin=0.3))
        img = comp(img, ink_edge(size, sm, 1.5 * F), gold_paint(size, sm, rng, bands=0.8))
        if i == 4:
            heart = Image.new("L", size, 0)
            hd = ImageDraw.Draw(heart)
            hx, hy, hs = 186 * F, 72 * F, 6 * F
            hd.ellipse((hx - hs * 1.05, hy - hs * 0.8, hx + hs * 0.05, hy + hs * 0.25), fill=255)
            hd.ellipse((hx - hs * 0.05, hy - hs * 0.8, hx + hs * 1.05, hy + hs * 0.25), fill=255)
            hd.polygon([(hx - hs * 1.0, hy - hs * 0.1), (hx + hs * 1.0, hy - hs * 0.1), (hx, hy + hs * 1.2)], fill=255)
            img = comp(img, ink_edge(size, heart, 1.5 * F), solid(size, CRIMSON, heart))
            img = comp(img, sparkles(size, [(74 * F, 84 * F, 5 * F), (200 * F, 100 * F, 3.5 * F)], glow_r=3 * F))
    return name, downsample(clip(img, edge_fade(size, 6 * F)), F)


def esquie_sit():
    return strip([esquie_frame(i) for i in range(len(E_FRAMES))], 256)


# --------------------------------------------------------------------------------------------------- reaping (Sciel)
# Six frames of a gold arc sweeping clockwise round a clearing; the head angle and trail length per frame.
R_FRAMES = [("glint", -150, 34, 0.0), ("sweep-1", -70, 110, 0.0), ("sweep-2", 20, 200, 0.0), ("sweep-3", 120, 300, 0.0),
            ("harvest", 210, 360, 0.0), ("fade", 210, 360, 1.0)]
R_START = -184


def stalk_layout(rng):
    """A ring of grain stalks standing in the arc's path: (angle, radius, height, lean)."""
    out = []
    n = 15
    for k in range(n):
        a = R_START + 18 + k * (348 / n) + rng.uniform(-6, 6)
        out.append((a, rng.uniform(98, 112), rng.uniform(30, 40), rng.uniform(-0.15, 0.15)))
    return out


def oval(c, a, b, ang, n=18):
    """Points of an ellipse with semi-axes a (along ang, radians) and b."""
    ca, sa = math.cos(ang), math.sin(ang)
    return [(c[0] + math.cos(t) * a * ca - math.sin(t) * b * sa, c[1] + math.cos(t) * a * sa + math.sin(t) * b * ca)
            for t in [2 * math.pi * k / n for k in range(n)]]


def stalk(size, base, height, lean, F, rng, cut=0.0, fly=None, keep=None):
    """A grain stalk: stem, two blades and a braided ear. cut > 0 draws only a tuft of stubble; fly=(dx, dy, rot,
    alpha) draws the cut-off top flying free instead (kept inside the box `keep`). The reaping frames leave no
    stubble, so the clearing is clean once the arc has passed."""
    img = canvas(*size)
    bx, by = base
    top = (bx + lean * height * F, by - height * F)
    stem = [lerp_pt(base, top, t / 10) for t in range(11)]
    stem = [(x + math.sin(t * 0.9) * 0.6 * F, y) for t, (x, y) in enumerate(stem)]
    cut_t = 0.3
    if fly is None:
        if cut:
            m = Image.new("L", size, 0)
            for ang, ln in ((-118, 6.5), (-92, 8.5), (-66, 6.0)):
                ar = math.radians(ang + lean * 40)
                m = union(m, line_mask(size, [base, (bx + math.cos(ar) * ln * F, by + math.sin(ar) * ln * F)], 1.8 * F))
        else:
            m = union(line_mask(size, stem, 2.2 * F), ear_mask(size, top, lean, F))
            for sgn in (-1, 1):
                p0 = lerp_pt(base, top, 0.18)
                leaf = curve(p0, (p0[0] + sgn * 6 * F, p0[1] - 6 * F), (p0[0] + sgn * 11 * F, p0[1] - 4 * F), 10)
                m = union(m, line_mask(size, leaf, 1.6 * F))
        img = comp(img, ink_edge(size, m, 1.5 * F), gold_paint(size, m, rng, angle=80, bands=1.0))
        return img
    dx, dy, rot, alpha = fly
    T = int(height * 1.6 * F)
    tsize = (T * 2, T * 2)
    pivot = lerp_pt(base, top, cut_t)
    off = (T - pivot[0], T - pivot[1])
    tb = (bx + off[0], by + off[1])
    tt = (top[0] + off[0], top[1] + off[1])
    m = union(line_mask(tsize, [lerp_pt(tb, tt, cut_t), tt], 2.2 * F), ear_mask(tsize, tt, lean, F))
    tile = comp(canvas(*tsize), ink_edge(tsize, m, 1.5 * F), gold_paint(tsize, m, rng, angle=80, bands=1.0))
    tile = tile.rotate(rot, resample=Image.BICUBIC, center=(T, T))
    x, y = pivot[0] + dx, pivot[1] + dy
    if keep:
        x, y = min(max(x, keep[0]), keep[2]), min(max(y, keep[1]), keep[3])
    layer = canvas(*size)
    layer.alpha_composite(tile, (int(x - T), int(y - T)))
    return fade(layer, alpha)


def ear_mask(size, top, lean, F):
    """A braided grain ear: kernels set in a chevron up the stem's last stretch, with whiskery awns."""
    m = Image.new("L", size, 0)
    d = ImageDraw.Draw(m)
    tx, ty = top
    ux, uy = _norm(lean, -1)
    vx, vy = -uy, ux
    ang = math.atan2(uy, ux)
    for k in range(4):
        back = (4 + k * 4.4) * F
        x, y = tx - ux * back, ty - uy * back
        for sgn in (-1, 1):
            kc = (x + vx * sgn * 2.6 * F, y + vy * sgn * 2.6 * F)
            d.polygon(oval(kc, 3.6 * F, 1.9 * F, ang + sgn * 0.6), fill=255)
    d.polygon(oval((tx, ty), 3.4 * F, 1.8 * F, ang), fill=255)
    for sgn in (-1, 0, 1):
        a2 = ang + sgn * 0.35
        d.line([(tx, ty), (tx + math.cos(a2) * 10 * F, ty + math.sin(a2) * 10 * F)], fill=255, width=max(1, int(0.9 * F)))
    return m


def reaping_frame(i, F=2):
    S = 320 * F
    size = (S, S)
    C = (S / 2, S / 2 + 6 * F)
    name, head, trail, dissolve = R_FRAMES[i]
    rng = random.Random(SEED + 80 + i)
    R = 104 * F
    tail = head - trail
    img = canvas(*size)

    def conic(fn):
        m = Image.new("L", size, 0)
        d = ImageDraw.Draw(m)
        rr = S
        for a in range(int(tail) - 2, int(head) + 3):
            v = fn(a)
            if v > 0:
                d.pieslice((C[0] - rr, C[1] - rr, C[0] + rr, C[1] + rr), a, a + 1.6, fill=max(0, min(255, int(v))))
        return m

    def tmap(a):
        return clamp01((a - tail) / max(1, trail))

    if trail > 0:
        fall = (lambda a: 255 * tmap(a) ** 1.6) if dissolve == 0 else (lambda a: 150 * (0.5 + 0.5 * math.sin(a * 0.11)) * (1 - 0.6 * tmap(a)))
        cone = conic(fall)
        w_out, w_in = (16 + 8 * min(1, trail / 200)) * F, (12 + 4 * min(1, trail / 200)) * F
        band = ImageChops.multiply(ring_mask(size, C, R - w_in, R + w_out).filter(ImageFilter.GaussianBlur(3 * F)), cone)
        if dissolve:
            band = ImageChops.multiply(band, noise_l(size, rng, cell=6 * F, blur=2 * F).point(lambda v: max(0, (v - 110) * 2)))
        img = comp(img, solid(size, GOLD_DEEP, scale_l(band.filter(ImageFilter.GaussianBlur(6 * F)), 0.6)))
        img = comp(img, glow(band, GOLD, 8 * F, 1.0))
        # the trail as brushed streaks of gold light
        streaks = canvas(*size)
        srng = random.Random(SEED + 81)
        for k in range(9):
            rr = R + srng.uniform(-w_in * 0.8, w_out * 0.8)
            a0 = tail + srng.uniform(0, 0.25) * trail
            a1 = head - srng.uniform(0, 0.12) * trail
            if a1 - a0 < 6:
                continue
            rgb = GOLD_CORE if k % 3 == 0 else (GOLD if k % 3 == 1 else (250, 222, 160))
            streaks = comp(streaks, brush_stroke(size, arc_pts(C, rr, a0, a1), srng.uniform(3, 7) * F, rgb, 230,
                                                 seed=srng.randrange(1 << 30), bristles=5))
        streaks = clip(streaks, cone)
        if dissolve:
            streaks = fade(streaks, 0.5)
        img = comp(img, glow(streaks.getchannel("A"), GOLD, 3 * F, 0.8), streaks)
    if dissolve == 0:
        # the leading blade: a crescent of white-gold light lying along the path, its point running ahead, swung on a
        # faint shaft of light from the middle of the clearing
        ha = math.radians(head)
        n = 24
        outer, inner = [], []
        for k in range(n + 1):
            t = k / n
            aa = ha - 0.12 + t * 0.52
            ro = R + (3 + 21 * (1 - t) ** 0.8) * F
            ri = R - (5 - 6 * t) * F
            outer.append((C[0] + math.cos(aa) * ro, C[1] + math.sin(aa) * ro))
            inner.append((C[0] + math.cos(aa) * ri, C[1] + math.sin(aa) * ri))
        tip = outer[-1]
        blade = poly_mask(size, outer + inner[::-1]).filter(ImageFilter.GaussianBlur(F)).point(lambda v: 255 if v > 120 else 0)
        hb = ha - 0.09
        shaft = line_mask(size, [(C[0] + math.cos(hb) * R * 0.42, C[1] + math.sin(hb) * R * 0.42),
                                 (C[0] + math.cos(hb) * (R + 6 * F), C[1] + math.sin(hb) * (R + 6 * F))], 3.2 * F)
        shaft = ImageChops.multiply(shaft, ImageChops.invert(radial_light(size, C, R * 0.95, 0.7)))
        img = comp(img, glow(shaft, GOLD, 3 * F, 0.9), solid(size, GOLD_CORE, scale_l(shaft, 0.75)))
        scale = 0.6 if i == 0 else 1.0
        img = comp(img, glow(blade, GOLD, 14 * F * scale, 1.4), glow(blade, GOLD_CORE, 5 * F, 1.2))
        img = comp(img, ink_edge(size, blade, int(1.5 * F), GOLD_DEEP), gold_paint(size, blade, rng, angle=head + 90, bands=0.8))
        edge = ImageChops.multiply(blade, ImageChops.invert(ring_mask(size, C, 0, R - 1 * F)))
        img = comp(img, solid(size, GOLD_CORE, scale_l(ImageChops.subtract(blade, shrink(blade, 3 * F)), 0.0)),
                   solid(size, GOLD_CORE, scale_l(ImageChops.subtract(edge, ring_mask(size, C, 0, R + 3 * F)), 0.95)))
        sp = [(tip[0], tip[1], (18 if i == 0 else 13) * F)]
        img = comp(img, sparkles(size, sp, glow_r=6 * F))

    # the grain: stalks stand until the blade passes, then their tops fly off outward
    lrng = random.Random(SEED + 90)
    lay = stalk_layout(lrng)
    for k, (a, rr, h, lean) in enumerate(lay):
        ar = math.radians(a)
        base = (C[0] + math.cos(ar) * rr * F * 0.92, C[1] + math.sin(ar) * rr * F * 0.92 + h * F * 0.45)
        passed = a < head - 4 if trail > 0 else False
        krng = random.Random(SEED + 200 + k)
        if not passed:
            img = comp(img, stalk(size, base, h, lean, F, krng))
            continue
        since = min(1.0, (head - a) / 150) if dissolve == 0 else 1.0
        out = 16 + 34 * since + 14 * dissolve
        tang = ar + math.pi / 2
        dx = math.cos(ar) * out * F * 0.7 + math.cos(tang) * out * F * 0.5
        dy = math.sin(ar) * out * F * 0.7 + math.sin(tang) * out * F * 0.5 - 10 * F * since
        rot = -(80 + 220 * since + 120 * dissolve) * (1 if k % 2 else 0.8)
        alpha = 1.0 - 0.5 * since if dissolve == 0 else 0.3
        keep = (34 * F, 34 * F, S - 34 * F, S - 34 * F)
        img = comp(img, stalk(size, base, h, lean, F, krng, fly=(dx, dy, rot, alpha), keep=keep))
    # loose grains and motes of light thrown off the arc
    grains = Image.new("L", size, 0)
    gd = ImageDraw.Draw(grains)
    count = [4, 14, 24, 32, 40, 30][i]
    for _ in range(count):
        a = rng.uniform(tail, head) if trail > 0 else head + rng.uniform(-20, 20)
        rr = R + rng.uniform(-20, 40 + 20 * dissolve) * F
        ar = math.radians(a)
        x, y = C[0] + math.cos(ar) * rr, C[1] + math.sin(ar) * rr
        if not (16 * F < x < S - 16 * F and 16 * F < y < S - 16 * F):
            continue
        L = rng.uniform(2.2, 3.6) * F
        t = ar + math.pi / 2 + rng.uniform(-0.6, 0.6)
        gd.line([(x - math.cos(t) * L, y - math.sin(t) * L), (x + math.cos(t) * L, y + math.sin(t) * L)], fill=rng.randint(170, 255),
                width=int(2.6 * F))
    img = comp(img, glow(grains, GOLD, 2 * F, 1.0), solid(size, (250, 222, 150), grains))
    if dissolve:
        sp = [(C[0] + math.cos(math.radians(a)) * R * f, C[1] + math.sin(math.radians(a)) * R * f, s * F)
              for a, f, s in ((-120, 1.05, 6), (-20, 0.95, 4.5), (70, 1.12, 5.5), (160, 1.0, 4), (240, 1.08, 5))]
        img = comp(img, sparkles(size, sp, glow_r=3 * F))
    return name, downsample(clip(img, edge_fade(size, 10 * F)), F)


def sciel_reaping():
    return strip([reaping_frame(i) for i in range(len(R_FRAMES))], 320)


# ----------------------------------------------------------------------------------------------------------- entry

def make(out_dir, fonts):
    out = []

    def emit(asset, rel, img, sprites=()):
        save(img, out_dir, rel)
        out.append({"asset": asset, "path": f"art/{rel}", "size": [img.width, img.height], "sprites": list(sprites)})

    sheet, sprites = get_back_up()
    emit("asset.fx.verso-get-back-up", "fx/verso-get-back-up.png", sheet, sprites)
    sheet, sprites = esquie_sit()
    emit("asset.fx.esquie-sit", "fx/esquie-sit.png", sheet, sprites)
    sheet, sprites = sciel_reaping()
    emit("asset.fx.sciel-reaping", "fx/sciel-reaping.png", sheet, sprites)
    return out

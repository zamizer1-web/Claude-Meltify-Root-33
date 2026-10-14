"""Map overlays and effects for Root 33: the Monolith plate and ring, the sacred river, routes, petals and tints.

All original, drawn with Pillow from the shared style kit (tools/art/style.py). Every random choice comes from a
seeded random.Random (noise included), so the same code always paints the same pixels.
"""
import math
import random
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from style import *  # noqa: E402,F401,F403
from style import (BONE, CRIMSON, CRIMSON_DEEP, DUSK, GOLD, GOLD_CORE, GOLD_DEEP, INK, INK_SOFT, NIGHT, PAPER,  # noqa: E402
                   TEAL, TEAL_DEEP, brush_stroke, canvas, glow, inner_edge, outline_of, pip, rgba, solid)
from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageOps  # noqa: E402

SEED = 3333
LIGHT_TEAL = (176, 226, 222)
VIOLET = (98, 66, 150)
AMBER = (236, 160, 72)


# ----------------------------------------------------------------------------------------------------------- helpers

def comp(base, *layers):
    for layer in layers:
        base = Image.alpha_composite(base, layer)
    return base


def fade(img, f):
    out = img.copy()
    out.putalpha(img.getchannel("A").point(lambda v: int(v * f)))
    return out


def clip(img, mask):
    """Multiply an RGBA layer's alpha by an L mask."""
    out = img.copy()
    out.putalpha(ImageChops.multiply(img.getchannel("A"), mask))
    return out


def scale_l(mask, f):
    return mask.point(lambda v: min(255, int(v * f)))


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


def lerp_rgb(a, b, t):
    return tuple(int(round(a[i] + (b[i] - a[i]) * t)) for i in range(3))


def ramp(stops, t):
    """Piecewise-linear colour ramp; stops = [(t, rgb), ...] sorted by t."""
    t = min(max(t, stops[0][0]), stops[-1][0])
    for (t0, c0), (t1, c1) in zip(stops, stops[1:]):
        if t <= t1:
            return lerp_rgb(c0, c1, (t - t0) / ((t1 - t0) or 1))
    return stops[-1][1]


def map_l(mask, stops):
    """Colour an L image through a ramp (returns RGB)."""
    lut = [ramp(stops, v / 255) for v in range(256)]
    return Image.merge("RGB", [mask.point([lut[v][c] for v in range(256)]) for c in range(3)])


def linear_light(size, angle):
    """A 0..255 linear gradient across `size`, rotated by `angle` degrees, with no empty corners."""
    w, h = size
    d = int(math.hypot(w, h)) + 4
    g = Image.linear_gradient("L").resize((d, d)).rotate(angle, resample=Image.BICUBIC)
    return g.crop(((d - w) // 2, (d - h) // 2, (d - w) // 2 + w, (d - h) // 2 + h))


def conic_light(size, center, fn, step=2):
    """An L image whose value depends on the angle around `center` (degrees, 0 = east, clockwise on screen)."""
    m = Image.new("L", size, 0)
    d = ImageDraw.Draw(m)
    r = math.hypot(*size) + 4
    cx, cy = center
    for a in range(0, 360, step):
        d.pieslice((cx - r, cy - r, cx + r, cy + r), a, a + step + 0.6, fill=max(0, min(255, int(fn(a + step / 2)))))
    return m.filter(ImageFilter.GaussianBlur(1.5))


def gold_paint(size, mask, rng, light=None, angle=25, bands=1.5):
    """Gold leaf (deep to bright sheen, sparse bright specks), shaped by mask. Seeded version of style.gold_leaf."""
    light = light if light is not None else linear_light(size, angle)
    sheen = map_l(light.point(lambda v: int(255 * (0.18 + 0.72 * abs(math.sin(v / 255 * math.pi * bands))))),
                  [(0, GOLD_DEEP), (0.55, GOLD), (0.88, (250, 220, 156)), (1, GOLD_CORE)])
    speck = noise_l(size, rng, stretch=False).point(lambda v: 255 if v > 247 else 0).filter(ImageFilter.GaussianBlur(0.6))
    sheen = Image.composite(Image.new("RGB", size, GOLD_CORE), sheen, speck.point(lambda v: v // 2))
    out = sheen.convert("RGBA")
    out.putalpha(mask)
    return out


def paper_fill(size, mask, rng, base=BONE, amount=0.18):
    """Bone paper with soft seeded fibres, shaped by mask."""
    w, h = size
    grain = noise_l(size, rng, cell=1, blur=1.0)
    fibre = noise_l((max(1, w // 6), h), rng, blur=1.5).resize(size, Image.BICUBIC)
    shade = ImageChops.add(grain, fibre, 2.0, 0)
    tex = ImageChops.multiply(Image.new("RGB", size, base), Image.merge("RGB", [shade.point(lambda v: 150 + v * 105 // 255)] * 3))
    out = Image.blend(Image.new("RGB", size, base), tex, amount).convert("RGBA")
    out.putalpha(mask)
    return out


def rough_disc(size, c, r, rng, wobble=0.035, jitter=0.004, steps=220, sy=1.0):
    """A disc whose rim wanders like a hand-cut brush edge."""
    harm = [(k, rng.uniform(0, 2 * math.pi), rng.uniform(0.35, 1.0) / k) for k in range(2, 8)]
    pts = []
    for i in range(steps):
        a = 2 * math.pi * i / steps
        f = 1 + wobble * sum(amp * math.sin(k * a + ph) for k, ph, amp in harm) + rng.uniform(-jitter, jitter)
        pts.append((c[0] + math.cos(a) * r * f, c[1] + math.sin(a) * r * f * sy))
    m = Image.new("L", size, 0)
    ImageDraw.Draw(m).polygon(pts, fill=255)
    return m


def gem(size, x, y, ang, r, glow_r=0):
    """A faceted gold diamond pointing along `ang` (radians): lit half, shaded half, ink rim."""
    ax, ay = math.cos(ang), math.sin(ang)
    px, py = math.cos(ang + math.pi / 2), math.sin(ang + math.pi / 2)
    tip, tail = (x + ax * r * 1.35, y + ay * r * 1.35), (x - ax * r * 1.35, y - ay * r * 1.35)
    s1, s2 = (x + px * r * 0.72, y + py * r * 0.72), (x - px * r * 0.72, y - py * r * 0.72)
    whole, lit = Image.new("L", size, 0), Image.new("L", size, 0)
    ImageDraw.Draw(whole).polygon([tip, s1, tail, s2], fill=255)
    # light comes from the upper left: light whichever half faces it
    lit_side = s1 if (s1[0] - x) + (s1[1] - y) < (s2[0] - x) + (s2[1] - y) else s2
    ImageDraw.Draw(lit).polygon([tip, lit_side, tail], fill=255)
    out = canvas(*size)
    if glow_r:
        out = comp(out, glow(whole, GOLD, glow_r, 0.8))
    rim = max(2, int(r * 0.16))
    out = comp(out, solid(size, INK, ImageChops.subtract(grow(whole, rim), whole)), solid(size, GOLD_DEEP, whole),
               solid(size, (246, 214, 140), lit))
    spark = Image.new("L", size, 0)
    ImageDraw.Draw(spark).line([tip, (x, y)], fill=200, width=max(1, int(r * 0.12)))
    return comp(out, solid(size, GOLD_CORE, clip(solid(size, GOLD_CORE, spark), lit).getchannel("A")))


def ring_mask(size, c, r_in, r_out):
    m = Image.new("L", size, 0)
    d = ImageDraw.Draw(m)
    d.ellipse((c[0] - r_out, c[1] - r_out, c[0] + r_out, c[1] + r_out), fill=255)
    d.ellipse((c[0] - r_in, c[1] - r_in, c[0] + r_in, c[1] + r_in), fill=0)
    return m


def arc_pts(c, r, a0, a1, n=None, wob=0.0, rng=None):
    """Points along an arc (degrees), optionally with a slow radial wobble."""
    n = n or max(8, int(abs(a1 - a0) / 2.5))
    ph = rng.uniform(0, 6.3) if rng else 0.0
    out = []
    for i in range(n + 1):
        t = i / n
        a = math.radians(a0 + (a1 - a0) * t)
        rr = r * (1 + wob * math.sin(t * math.pi * 2.2 + ph))
        out.append((c[0] + math.cos(a) * rr, c[1] + math.sin(a) * rr))
    return out


def painted_arc(size, c, r, a0, a1, width, rgb, rng, alpha=255, passes=2, taper=True, bristles=9, wob=0.006):
    """Overlapping bristle strokes along an arc."""
    layer = canvas(*size)
    for _ in range(passes):
        rr = r + rng.uniform(-width * 0.1, width * 0.1)
        j = rng.uniform(-1.5, 1.5)
        pts = arc_pts(c, rr, a0 + j, a1 + j * 0.7, wob=wob, rng=rng)
        layer = comp(layer, brush_stroke(size, pts, width * rng.uniform(0.88, 1.04), rgb, alpha,
                                         seed=rng.randrange(1 << 30), taper=taper, bristles=bristles))
    return layer


def bristle_grain(size, c, r0, r1, rng, colours, count, alpha=(35, 85)):
    """Thin circular bristle marks between radii r0 and r1, to break up flat paint."""
    layer = canvas(*size)
    for _ in range(count):
        m = Image.new("L", size, 0)
        r = rng.uniform(r0, r1)
        a0 = rng.uniform(0, 360)
        pts = arc_pts(c, r, a0, a0 + rng.uniform(15, 70), wob=0.004, rng=rng)
        ImageDraw.Draw(m).line(pts, fill=rng.randint(*alpha), width=max(1, int(rng.uniform(1.0, 3.0) * size[0] / 1024)), joint="curve")
        layer = comp(layer, solid(size, rng.choice(colours), m.filter(ImageFilter.GaussianBlur(0.6))))
    return layer


def star(size, c, r, rgb, alpha=255, thin=0.18):
    """A four-point sparkle."""
    m = Image.new("L", size, 0)
    d = ImageDraw.Draw(m)
    x, y = c
    t = r * thin
    d.polygon([(x - r, y), (x - t, y - t), (x, y - r), (x + t, y - t), (x + r, y), (x + t, y + t), (x, y + r), (x - t, y + t)],
              fill=alpha)
    return m


def smear(size, p0, angle, length, w0, rng, alpha=230, curl=0.0, bristles=12, steps=90, narrow=0.45):
    """A palette-knife drag: loaded at p0, bristles run dry at different lengths. Returns an L mask."""
    layer = Image.new("L", size, 0)
    d = ImageDraw.Draw(layer)
    path, a = [], angle
    x, y = p0
    for _ in range(steps + 1):
        path.append((x, y))
        x += math.cos(a) * length / steps
        y += math.sin(a) * length / steps
        a += curl / steps
    normals = []
    for i in range(steps + 1):
        j0, j1 = max(0, i - 1), min(steps, i + 1)
        dx, dy = path[j1][0] - path[j0][0], path[j1][1] - path[j0][1]
        ln = math.hypot(dx, dy) or 1
        normals.append((-dy / ln, dx / ln))
    for b in range(bristles):
        off = (b / max(1, bristles - 1) - 0.5) * w0
        end = rng.uniform(0.4, 1.0)
        bw = max(1, int(w0 / bristles * rng.uniform(1.3, 2.4)))
        base = alpha * rng.uniform(0.65, 1.0)
        last = max(2, int(steps * end))
        for i in range(last):
            t0, t1 = i / steps, (i + 1) / steps
            (nx0, ny0), (nx1, ny1) = normals[i], normals[i + 1]
            pa = (path[i][0] + nx0 * off * (1 - narrow * t0), path[i][1] + ny0 * off * (1 - narrow * t0))
            pb = (path[i + 1][0] + nx1 * off * (1 - narrow * t1), path[i + 1][1] + ny1 * off * (1 - narrow * t1))
            av = base * (1 - (i / last) ** 2.2)
            d.line([pa, pb], fill=int(av), width=bw)
    return layer.filter(ImageFilter.GaussianBlur(1.0))


def grow(mask, px):
    """Dilate a mask by px (a blur-and-threshold for wide amounts; rank filters get slow past a few px)."""
    if px <= 4:
        return mask.filter(ImageFilter.MaxFilter(2 * int(px) + 1))
    return mask.filter(ImageFilter.GaussianBlur(px / 1.5)).point(lambda v: 255 if v > 17 else 0)


def shrink(mask, px):
    if px <= 4:
        return mask.filter(ImageFilter.MinFilter(2 * int(px) + 1))
    return mask.filter(ImageFilter.GaussianBlur(px / 1.5)).point(lambda v: 255 if v > 238 else 0)


def ring_out(mask, px):
    return ImageChops.subtract(grow(mask, px), mask)


def ring_in(mask, px):
    return ImageChops.subtract(mask, shrink(mask, px))


def edge_keep(size, inner, outer):
    """A radial mask: opaque inside `inner`, fading to clear at `outer` (px from the centre), so glows never touch the
    image edge."""
    m = Image.new("L", size, 0)
    d = ImageDraw.Draw(m)
    cx, cy = size[0] / 2, size[1] / 2
    steps = 32
    for k in range(steps + 1):
        r = outer - (outer - inner) * k / steps
        d.ellipse((cx - r, cy - r, cx + r, cy + r), fill=int(255 * k / steps))
    return m.filter(ImageFilter.GaussianBlur(1))


def downsample(img, f):
    return img.resize((img.width // f, img.height // f), Image.LANCZOS)


def save(img, out_dir, rel):
    p = Path(out_dir) / rel
    p.parent.mkdir(parents=True, exist_ok=True)
    img.save(p, optimize=True)
    return p


# ----------------------------------------------------------------------------------------------------------- pieces

def monolith_plate(rng):
    """A crimson painted medallion with a gold rim; its ink centre is left plain for the runtime number."""
    S, F = 512, 2
    size, C = (S * F, S * F), (S * F // 2, S * F // 2)
    img = canvas(*size)
    outer = rough_disc(size, C, 226 * F, rng, wobble=0.03)
    img = comp(img, glow(outer, (0, 0, 0), 14 * F, 0.75))
    img = comp(img, solid(size, CRIMSON_DEEP, outer))

    # Crimson body laid in with wide circular strokes, darker and brighter reds alternating.
    strokes = canvas(*size)
    for i in range(18):
        r = rng.uniform(170, 214) * F
        a0 = rng.uniform(0, 360)
        col = (CRIMSON, (150, 18, 40), (196, 40, 58))[i % 3]
        strokes = comp(strokes, painted_arc(size, C, r, a0, a0 + rng.uniform(80, 220), rng.uniform(16, 30) * F, col, rng,
                                            alpha=rng.randint(170, 240), passes=1))
    img = comp(img, clip(strokes, outer))
    img = comp(img, clip(bristle_grain(size, C, 166 * F, 222 * F, rng, [CRIMSON_DEEP, (210, 58, 72), (96, 8, 24)], 90), outer))
    # Dry-brush bone light on the upper left, a little gold on the lower right: short, broken, few bristles.
    for i in range(6):
        r = rng.uniform(178, 212) * F
        a0 = rng.uniform(186, 240)
        img = comp(img, clip(painted_arc(size, C, r, a0, a0 + rng.uniform(10, 26), rng.uniform(7, 11) * F, BONE, rng,
                                         alpha=rng.randint(60, 105), passes=1, bristles=4, wob=0.0), outer))
    for i in range(4):
        r = rng.uniform(180, 210) * F
        a0 = rng.uniform(15, 70)
        img = comp(img, clip(painted_arc(size, C, r, a0, a0 + rng.uniform(16, 32), rng.uniform(6, 9) * F, GOLD, rng,
                                         alpha=rng.randint(90, 140), passes=1, bristles=4, wob=0.0), outer))

    # Outer rim: an ink edge and a broken gold brush ring just inside it.
    img = comp(img, solid(size, INK, ring_out(outer, 3 * F)))
    rim = canvas(*size)
    a = rng.uniform(0, 360)
    while a < 360 + 0:
        span = rng.uniform(60, 120)
        rim = comp(rim, painted_arc(size, C, 215 * F, a, a + span, 8 * F, GOLD, rng, alpha=255, passes=2, bristles=6))
        a += span + rng.uniform(5, 14)
    light = conic_light(size, C, lambda deg: 128 + 127 * math.cos(math.radians(deg - 225)))
    img = comp(img, clip(gold_paint(size, rim.getchannel("A"), rng, light=light, bands=1.0), outer))

    # Three gold studs (one per Ring clearing) on the crimson band.
    for k in range(3):
        ang = math.radians(-90 + 120 * k)
        img = comp(img, gem(size, C[0] + math.cos(ang) * 189 * F, C[1] + math.sin(ang) * 189 * F, ang, 13 * F, glow_r=6 * F))

    # The plain ink centre, sunk into the plate behind a gold collar.
    cseed = rng.randrange(1 << 30)
    centre = rough_disc(size, C, 150 * F, random.Random(cseed), wobble=0.018)
    collar_out = rough_disc(size, C, 160 * F, random.Random(cseed), wobble=0.018)
    collar = ImageChops.subtract(collar_out, centre)
    img = comp(img, glow(collar_out, (0, 0, 0), 9 * F, 0.9))
    img = comp(img, solid(size, INK, ring_out(collar_out, 2 * F)))
    img = comp(img, gold_paint(size, collar, rng, light=light, bands=1.2))
    img = comp(img, solid(size, INK, ring_in(collar, 1.5 * F).point(lambda v: v * 3 // 5)))
    img = comp(img, solid(size, INK, centre))
    lift = Image.new("L", size, 0)
    ImageDraw.Draw(lift).ellipse((C[0] - 110 * F, C[1] - 120 * F, C[0] + 90 * F, C[1] + 80 * F), fill=130)
    img = comp(img, clip(solid(size, INK_SOFT, lift.filter(ImageFilter.GaussianBlur(50 * F))), centre))
    # A thin gold rule inside the centre and an inner shadow under the collar.
    inset = rough_disc(size, C, 137 * F, random.Random(cseed), wobble=0.018)
    img = comp(img, solid(size, GOLD_DEEP, scale_l(ring_in(inset, 1 * F), 0.75)))
    img = comp(img, clip(solid(size, (0, 0, 0), ring_in(centre, 10 * F).filter(ImageFilter.GaussianBlur(6 * F))), centre))
    return downsample(clip(img, edge_keep(size, 238 * F, 254 * F)), F)


def sacred_river(rng):
    """A horizontally tileable ribbon of chroma: deep teal edges, a pale core, and teal-to-gold shimmer along it."""
    W, H, F = 1024, 128, 2
    w, h = W * F, H * F
    WW = 3 * w
    size = (WW, h)

    def harmonics(n, amp):
        return [(k, amp * rng.uniform(0.5, 1.0) / k ** 0.6, rng.uniform(0, 2 * math.pi)) for k in n]

    hc, hw, hcol = harmonics((1, 2, 3, 5), 4.0 * F), harmonics((1, 2, 4, 7), 3.2 * F), harmonics((2, 3), 1.0)

    def per(x, harm):
        return sum(a * math.sin(2 * math.pi * k * x / w + ph) for k, a, ph in harm)

    def yc(x):
        return h / 2 + per(x, hc)

    def half(x):
        return 28 * F + per(x, hw)

    band = Image.new("L", size, 0)
    core = Image.new("L", size, 0)
    col = Image.new("RGB", size)
    bd, cd, kd = ImageDraw.Draw(band), ImageDraw.Draw(core), ImageDraw.Draw(col)
    stops = [(0.0, (74, 158, 168)), (0.38, LIGHT_TEAL), (0.58, (238, 226, 176)), (0.8, GOLD), (1.0, (232, 176, 84))]
    for x in range(WW):
        c, hh = yc(x), half(x)
        bd.line([(x, c - hh), (x, c + hh)], fill=255)
        cd.line([(x, c - hh * 0.32), (x, c + hh * 0.22)], fill=255)
        t = 0.5 + 0.5 * per(x, hcol) / max(1e-6, sum(abs(a) for _, a, _ in hcol))
        kd.line([(x, 0), (x, h)], fill=ramp(stops, t))
    band = band.filter(ImageFilter.GaussianBlur(2.5 * F))

    img = canvas(*size)
    img = comp(img, glow(band, TEAL, 12 * F, 0.75))
    body = col.convert("RGBA")
    body.putalpha(scale_l(band, 0.9))
    img = comp(img, body)
    img = comp(img, solid(size, TEAL_DEEP, scale_l(ring_in(band.point(lambda v: 255 if v > 110 else 0), 7 * F).filter(ImageFilter.GaussianBlur(4 * F)), 0.8)))
    img = comp(img, solid(size, GOLD_CORE, scale_l(core.filter(ImageFilter.GaussianBlur(7 * F)), 0.3)))

    # Flowing streaks, laid once per period and repeated three times so the tile wraps.
    streaks = canvas(*size)
    for i in range(34):
        x0 = rng.uniform(0, w)
        length = rng.uniform(140, 380) * F
        dy = rng.uniform(-0.75, 0.75)
        wave = rng.uniform(1.0, 3.0) * F
        ph = rng.uniform(0, 6.3)
        colr = rng.choice([BONE, GOLD_CORE, LIGHT_TEAL, GOLD, BONE, (124, 196, 200)])
        width = rng.uniform(2.5, 7) * F
        alpha = rng.randint(90, 200)
        seed = rng.randrange(1 << 30)
        for k in range(3):
            pts = []
            for j in range(41):
                x = x0 + length * j / 40
                pts.append((x + k * w, yc(x) + dy * half(x) + wave * math.sin(j / 40 * 6.28 + ph)))
            streaks = comp(streaks, brush_stroke(size, pts, width, colr, alpha, seed=seed, bristles=4))
    img = comp(img, clip(streaks, band))

    # Shimmer: little four-point sparkles riding the current.
    spark = Image.new("L", size, 0)
    for i in range(16):
        x = rng.uniform(0, w)
        y = yc(x) + rng.uniform(-0.6, 0.6) * half(x)
        r = rng.uniform(4, 9) * F
        a = rng.randint(170, 255)
        for k in range(3):
            spark = ImageChops.lighter(spark, star(size, (x + k * w, y), r, GOLD_CORE, a))
    img = comp(img, glow(spark, GOLD, 4 * F, 1.3), solid(size, GOLD_CORE, spark))

    # Broken bone highlights along the upper bank.
    bank = canvas(*size)
    x = rng.uniform(0, 30) * F
    while x < w:
        ln = rng.uniform(40, 120) * F
        seed = rng.randrange(1 << 30)
        for k in range(3):
            pts = [(xx + k * w, yc(xx) - half(xx) * 0.82) for xx in [x + ln * j / 16 for j in range(17)]]
            bank = comp(bank, brush_stroke(size, pts, 2.5 * F, BONE, 150, seed=seed, bristles=3))
        x += ln + rng.uniform(30, 110) * F
    img = comp(img, bank)

    tile = img.crop((w, 0, 2 * w, h))
    # Make sure the top and bottom rows fade fully out.
    edge = Image.linear_gradient("L").resize((1, h)).point(lambda v: 0)
    ed = ImageDraw.Draw(edge)
    for y in range(h):
        ed.point((0, y), fill=int(255 * min(1.0, min(y, h - 1 - y) / (8 * F))))
    tile = clip(tile, edge.resize((w, h)))
    return downsample(tile, F)


def final_battlefield(rng):
    """A red-gold ring of brush and flame-tongues that marks Lumiere's clearing from Act III."""
    S, F = 512, 2
    size, C = (S * F, S * F), (S * F // 2, S * F // 2)
    R = 188 * F
    img = canvas(*size)
    ring = ring_mask(size, C, R - 16 * F, R + 16 * F)
    img = comp(img, glow(ring, CRIMSON, 26 * F, 1.0), glow(ring, CRIMSON_DEEP, 8 * F, 0.9))

    # Flame-tongues licking outward: alternating long and short, crimson with gold cores.
    tongues, cores = Image.new("L", size, 0), Image.new("L", size, 0)
    td, cd = ImageDraw.Draw(tongues), ImageDraw.Draw(cores)
    n = 24
    for i in range(n):
        a = math.radians(i * 360 / n + rng.uniform(-2, 2) - 90)
        long_ = i % 2 == 0
        reach = (rng.uniform(38, 46) if long_ else rng.uniform(20, 28)) * F
        half = math.radians(5.2 if long_ else 4.0)
        bend = math.radians(rng.uniform(4, 9))
        base = R + 6 * F
        p1 = (C[0] + math.cos(a - half) * base, C[1] + math.sin(a - half) * base)
        p2 = (C[0] + math.cos(a + half) * base, C[1] + math.sin(a + half) * base)
        tip = (C[0] + math.cos(a + bend) * (base + reach), C[1] + math.sin(a + bend) * (base + reach))
        mid = (C[0] + math.cos(a + bend * 0.2) * (base + reach * 0.55), C[1] + math.sin(a + bend * 0.2) * (base + reach * 0.55))
        td.polygon([p1, mid, tip, p2], fill=255)
        cp1 = (C[0] + math.cos(a - half * 0.35) * base, C[1] + math.sin(a - half * 0.35) * base)
        cp2 = (C[0] + math.cos(a + half * 0.35) * base, C[1] + math.sin(a + half * 0.35) * base)
        ctip = (C[0] + math.cos(a + bend * 0.7) * (base + reach * 0.7), C[1] + math.sin(a + bend * 0.7) * (base + reach * 0.7))
        cd.polygon([cp1, ctip, cp2], fill=255)
    tongues = tongues.filter(ImageFilter.GaussianBlur(1.2 * F))
    img = comp(img, glow(tongues, GOLD, 6 * F, 0.6), solid(size, CRIMSON, tongues), solid(size, INK, scale_l(outline_of(tongues.point(lambda v: 255 if v > 128 else 0), 2 * F), 0.8)))
    img = comp(img, solid(size, GOLD, cores.filter(ImageFilter.GaussianBlur(1.5 * F))))

    # The band itself: crimson underpaint, ink edges, then gold brushed on top with a few gaps.
    img = comp(img, painted_arc(size, C, R, 0, 361, 34 * F, CRIMSON_DEEP, rng, passes=2, taper=False))
    img = comp(img, painted_arc(size, C, R, 0, 361, 28 * F, CRIMSON, rng, passes=2, taper=False))
    band = ring_mask(size, C, R - 17 * F, R + 17 * F)
    img = comp(img, clip(bristle_grain(size, C, R - 16 * F, R + 16 * F, rng, [CRIMSON_DEEP, (214, 60, 72)], 70), band))
    img = comp(img, solid(size, INK, ring_out(band, 3 * F)))
    gold = canvas(*size)
    a = rng.uniform(0, 40)
    while a < 360:
        span = rng.uniform(70, 130)
        gold = comp(gold, painted_arc(size, C, R + rng.uniform(-2, 2) * F, a, min(a + span, 360 + 8), 14 * F, GOLD, rng, passes=2, bristles=7))
        a += span + rng.uniform(4, 10)
    light = conic_light(size, C, lambda deg: 128 + 127 * math.cos(math.radians(2 * (deg - 210))))
    img = comp(img, glow(gold.getchannel("A"), GOLD, 5 * F, 0.5), gold_paint(size, gold.getchannel("A"), rng, light=light, bands=1.0))
    # A thin bone inner rule and a soft gold light falling inward onto the clearing.
    img = comp(img, solid(size, BONE, scale_l(outline_of(ring_mask(size, C, 0, R - 26 * F), 2 * F), 0.7)))
    inner = ring_mask(size, C, R - 60 * F, R - 22 * F).filter(ImageFilter.GaussianBlur(18 * F))
    img = comp(img, solid(size, GOLD, scale_l(inner, 0.28)))
    return downsample(clip(img, edge_keep(size, 242 * F, 255 * F)), F)


def petal_sprite(length, rgb, shade, rng, rot, blush=None, ss=4):
    """One shaded, slightly curled petal, cropped to its bounds."""
    L = length * ss
    Wd = L * 0.62
    T = int(L * 1.7)
    size = (T, T)
    cx, cy = T / 2, T / 2
    bend = rng.uniform(-0.18, 0.18) * Wd
    pts = []
    for i in range(72):
        a = i / 72 * 2 * math.pi
        r = 1 - 0.38 * (1 + math.cos(a)) / 2
        y = math.cos(a) * L * 0.5
        x = math.sin(a) * Wd * 0.5 * r + bend * (y / (L * 0.5)) ** 2
        # a soft notch at the broad end
        if math.cos(a) < -0.96:
            y += L * 0.05 * (1 - abs(math.sin(a)) / 0.28)
        pts.append((cx + x, cy - y))
    m = Image.new("L", size, 0)
    ImageDraw.Draw(m).polygon(pts, fill=255)
    m = m.filter(ImageFilter.GaussianBlur(ss * 0.4))
    grad = Image.linear_gradient("L").resize(size)  # 0 at the broad top, 255 at the narrow base
    body = map_l(grad, [(0, lerp_rgb(rgb, BONE, 0.12)), (0.45, rgb), (1, shade)]).convert("RGBA")
    body.putalpha(m)
    tile = body
    if blush:
        bl = Image.new("L", size, 0)
        ImageDraw.Draw(bl).ellipse((cx - Wd * 0.25, cy + L * 0.12, cx + Wd * 0.25, cy + L * 0.55), fill=150)
        tile = comp(tile, clip(solid(size, blush, bl.filter(ImageFilter.GaussianBlur(L * 0.08))), m))
    hi = Image.new("L", size, 0)
    side = rng.choice([-1, 1])
    ImageDraw.Draw(hi).ellipse((cx + side * Wd * 0.05 - Wd * 0.18, cy - L * 0.42, cx + side * Wd * 0.05 + Wd * 0.14, cy + L * 0.05), fill=95)
    tile = comp(tile, clip(solid(size, BONE, hi.filter(ImageFilter.GaussianBlur(L * 0.06))), m))
    vein = Image.new("L", size, 0)
    ImageDraw.Draw(vein).line([(cx + bend * 0.1, cy + L * 0.42), (cx + bend * 0.4, cy), (cx + bend * 0.2, cy - L * 0.3)], fill=120,
                              width=max(1, int(ss * max(1.0, length / 40))), joint="curve")
    tile = comp(tile, clip(solid(size, shade, vein.filter(ImageFilter.GaussianBlur(ss * 0.6))), m))
    rim = inner_edge(m.point(lambda v: 255 if v > 128 else 0), max(1, int(ss * max(1.0, length / 48))))
    tile = comp(tile, solid(size, lerp_rgb(shade, INK, 0.35), scale_l(rim.filter(ImageFilter.GaussianBlur(ss * 0.4)), 0.75)))
    tile = tile.rotate(rot, resample=Image.BICUBIC, expand=True)
    tile = downsample(tile, ss)
    return tile.crop(tile.getbbox())


def petals(rng):
    """Eight petal particles, 64x128 cells: six crimson, two bone-white, sizes varied."""
    W, H, cw = 512, 128, 64
    sheet = canvas(W, H)
    specs = [(104, "crimson"), (44, "crimson"), (78, "crimson"), (34, "bone"), (90, "crimson"), (56, "crimson"), (70, "bone"), (28, "crimson")]
    sprites = []
    reds = [(CRIMSON, CRIMSON_DEEP), ((196, 36, 54), (120, 14, 32)), ((164, 20, 44), (98, 10, 28))]
    n_red = n_bone = 0
    for i, (length, kind) in enumerate(specs):
        if kind == "bone":
            rgb, shade, blush = BONE, (196, 180, 160), CRIMSON
            n_bone += 1
            name = f"bone-{n_bone}"
        else:
            rgb, shade = reds[i % 3]
            blush = None
            n_red += 1
            name = f"crimson-{n_red}"
        rot = rng.uniform(-24, 24) if length > 70 else rng.uniform(-70, 70)
        tile = petal_sprite(length, rgb, shade, rng, rot, blush)
        fit = min(1.0, (cw - 8) / tile.width, (H - 8) / tile.height)
        if fit < 1.0:
            tile = tile.resize((max(1, int(tile.width * fit)), max(1, int(tile.height * fit))), Image.LANCZOS)
        x0 = i * cw
        sheet.alpha_composite(tile, (x0 + (cw - tile.width) // 2, (H - tile.height) // 2))
        sprites.append({"name": name, "box": [x0, 0, x0 + cw, H]})
    return sheet, sprites


def route_dots(rng):
    """A tileable dotted route: four painted gold dots on ink dabs, joined by a faint ink thread."""
    W, H, F = 256, 32, 4
    size = (W * F, H * F)
    img = canvas(*size)
    cy = H * F / 2
    thread = Image.new("L", size, 0)
    ph = rng.uniform(0, 6.3)
    pts = [(x, cy + 1.2 * F * math.sin(2 * math.pi * 2 * x / (W * F) + ph)) for x in range(-8, W * F + 9, 4)]
    ImageDraw.Draw(thread).line(pts, fill=150, width=int(2.4 * F), joint="curve")
    img = comp(img, solid(size, INK, thread.filter(ImageFilter.GaussianBlur(0.6 * F))))
    light = linear_light(size, 30)
    for k in range(4):
        x = (32 + 64 * k) * F
        y = cy + 1.2 * F * math.sin(2 * math.pi * 2 * x / (W * F) + ph) + rng.uniform(-0.6, 0.6) * F
        dab = rough_disc(size, (x, y), rng.uniform(10.5, 11.5) * F, rng, wobble=0.10, sy=rng.uniform(0.86, 0.95))
        img = comp(img, glow(dab, INK, 1.5 * F, 0.55), solid(size, INK, scale_l(dab, 0.92)))
        r = rng.uniform(6.8, 7.6) * F
        dot = rough_disc(size, (x + rng.uniform(-0.5, 0.5) * F, y - 0.4 * F), r, rng, wobble=0.08)
        img = comp(img, gold_paint(size, dot, rng, light=light, bands=0.9))
        sh = Image.new("L", size, 0)
        ImageDraw.Draw(sh).ellipse((x - r * 0.6, y - r * 0.1, x + r * 1.1, y + r * 1.2), fill=170)
        img = comp(img, clip(solid(size, GOLD_DEEP, sh.filter(ImageFilter.GaussianBlur(2 * F))), dot))
        hi = Image.new("L", size, 0)
        ImageDraw.Draw(hi).ellipse((x - r * 0.62, y - r * 0.7, x - r * 0.05, y - r * 0.18), fill=230)
        img = comp(img, clip(solid(size, GOLD_CORE, hi.filter(ImageFilter.GaussianBlur(0.8 * F))), dot))
    return downsample(img, F)


def smeared_canvas(rng):
    """A burst of paint dragged outward from a dark heart: the Paintress's grief, smeared across the canvas."""
    S, F = 512, 2
    size, C = (S * F, S * F), (S * F // 2, S * F // 2)
    img = canvas(*size)
    heart = rough_disc(size, C, 96 * F, rng, wobble=0.08)
    img = comp(img, solid(size, NIGHT, heart.filter(ImageFilter.GaussianBlur(10 * F))))

    def drag(count, rmin, rmax, lmin, lmax, wmin, wmax, palette, amin, amax, curl):
        layer = canvas(*size)
        for i in range(count):
            a = rng.uniform(0, 2 * math.pi)
            r0 = rng.uniform(rmin, rmax) * F
            p0 = (C[0] + math.cos(a) * r0, C[1] + math.sin(a) * r0)
            m = smear(size, p0, a + rng.uniform(-0.25, 0.25), rng.uniform(lmin, lmax) * F, rng.uniform(wmin, wmax) * F, rng,
                      alpha=rng.randint(amin, amax), curl=curl * rng.uniform(0.6, 1.3), bristles=rng.randint(10, 16))
            layer = comp(layer, solid(size, palette[i % len(palette)], m))
        return layer

    img = comp(img, drag(12, 10, 50, 120, 170, 50, 76, [(58, 34, 66), INK, (84, 44, 82)], 200, 240, 0.7))
    img = comp(img, drag(16, 20, 70, 130, 190, 46, 72, [CRIMSON_DEEP, CRIMSON, INK, CRIMSON_DEEP], 210, 250, 0.7))
    img = comp(img, drag(14, 50, 90, 100, 160, 22, 38, [CRIMSON, (196, 40, 58), CRIMSON_DEEP, BONE], 190, 240, 0.8))
    img = comp(img, drag(8, 70, 110, 70, 120, 8, 14, [BONE, GOLD, BONE], 170, 230, 0.9))

    # Splatter thrown off the drag, elongated along its flight.
    spl = canvas(*size)
    for i in range(70):
        a = rng.uniform(0, 2 * math.pi)
        r = rng.uniform(150, 238) * F
        s = rng.uniform(2, 8) * F * (1.25 - r / (240 * F))
        x, y = C[0] + math.cos(a) * r, C[1] + math.sin(a) * r
        drop = Image.new("L", (int(s * 6) + 4, int(s * 2.2) + 4), 0)
        ImageDraw.Draw(drop).ellipse((2, 2, 2 + s * 2.8, 2 + s * 2), fill=255)
        ImageDraw.Draw(drop).ellipse((2 + s * 2.6, 2 + s * 0.6, 2 + s * 3.8, 2 + s * 1.4), fill=255)
        drop = drop.rotate(-math.degrees(a), resample=Image.BICUBIC, expand=True)
        colr = rng.choice([CRIMSON, CRIMSON, CRIMSON_DEEP, INK, BONE, GOLD])
        tile = solid(drop.size, colr, drop)
        spl.alpha_composite(tile, (int(x - drop.width / 2), int(y - drop.height / 2)))
    img = comp(img, spl)

    # The heart: a crimson pool with a stirred bone-and-gold swirl.
    pool = rough_disc(size, C, 74 * F, rng, wobble=0.07)
    img = comp(img, solid(size, CRIMSON_DEEP, pool), solid(size, INK, scale_l(outline_of(pool, 2 * F), 0.6)))
    for r, col, wd, span in [(50, CRIMSON, 22, 250), (30, (196, 40, 58), 14, 200)]:
        a0 = rng.uniform(0, 360)
        img = comp(img, clip(painted_arc(size, C, r * F, a0, a0 + span, wd * F, col, rng, alpha=220, passes=1, bristles=7, wob=0.08), pool))
    # One stirred bone spiral, one fleck of gold.
    spiral = [(C[0] + math.cos(t) * (10 + 8 * t) * F, C[1] + math.sin(t) * (10 + 8 * t) * F) for t in [1.2 + i / 30 * 4.6 for i in range(31)]]
    img = comp(img, clip(brush_stroke(size, spiral, 6 * F, BONE, 170, seed=rng.randrange(1 << 30), bristles=4), pool))
    a0 = rng.uniform(0, 360)
    img = comp(img, clip(painted_arc(size, C, 62 * F, a0, a0 + 70, 5 * F, GOLD, rng, alpha=230, passes=1, bristles=4), pool))
    return downsample(img, F)


def dusk_tint(rng):
    """A full-board dusk wash: violet above, amber below, nearly clear in the middle and deeper at the edges."""
    S = 1024
    size = (S, S)
    vert = Image.linear_gradient("L").resize(size, Image.BICUBIC).filter(ImageFilter.GaussianBlur(10))
    rgb = map_l(vert, [(0, VIOLET), (0.42, (150, 86, 132)), (0.62, DUSK), (1.0, AMBER)])
    vig = Image.radial_gradient("L").resize(size, Image.BICUBIC).filter(ImageFilter.GaussianBlur(10))  # 0 centre, ~181 mid-edge, 255 corners
    clouds = noise_l((S, S), rng, cell=96, blur=60)
    alpha = vig.point(lambda v: int(18 + 132 * (v / 255) ** 1.9))
    alpha = ImageChops.add(alpha, clouds.point(lambda v: int(v * 22 / 255)), 1.0, -11)
    img = rgb.convert("RGBA")
    img.putalpha(alpha)
    # A few huge, very soft brush bands: violet cloud above, warm haze low down.
    bands = canvas(S, S)
    for i in range(5):
        top = i < 3
        y = rng.uniform(80, 300) if top else rng.uniform(720, 940)
        pts = [(x, y + 30 * math.sin(x / S * math.pi * rng.uniform(1, 2.5) + rng.uniform(0, 6))) for x in range(-60, S + 61, 32)]
        bands = comp(bands, brush_stroke(size, pts, rng.uniform(70, 130), VIOLET if top else AMBER, rng.randint(24, 40),
                                         seed=rng.randrange(1 << 30), bristles=9))
    img = comp(img, bands.filter(ImageFilter.GaussianBlur(30)))
    return img


def journal_glow(rng):
    """Sheet: a gold glow for a forest (0,0,512,512) and a small relic tag (512,0,640,192)."""
    W, H = 640, 512
    sheet = canvas(W, H)

    # Glow: overlapping canopy-shaped lumps of light, motes and drifting leaves.
    G = 512
    size, C = (G, G), (G // 2, G // 2 + 10)
    lumps = Image.new("L", size, 0)
    ld = ImageDraw.Draw(lumps)
    for i in range(9):
        a = rng.uniform(0, 2 * math.pi)
        d = rng.uniform(0, 70)
        r = rng.uniform(55, 95)
        x, y = C[0] + math.cos(a) * d, C[1] + math.sin(a) * d * 0.8
        ld.ellipse((x - r, y - r * 0.85, x + r, y + r * 0.85), fill=255)
    g = canvas(*size)
    g = comp(g, glow(lumps, GOLD_DEEP, 58, 0.7), glow(lumps, GOLD, 34, 0.7),
             glow(shrink(lumps, 20), GOLD_CORE, 30, 0.55))
    # Light shafts falling through the canopy.
    shafts = Image.new("L", size, 0)
    sd = ImageDraw.Draw(shafts)
    for i in range(7):
        x = C[0] + rng.uniform(-120, 120)
        wdt = rng.uniform(8, 20)
        tilt = rng.uniform(18, 34)
        sd.polygon([(x - wdt + tilt, 70), (x + wdt + tilt, 70), (x + wdt * 1.6 - tilt, 440), (x - wdt * 1.6 - tilt, 440)], fill=rng.randint(50, 90))
    falloff = Image.radial_gradient("L").resize(size).point(lambda v: max(0, 255 - int(v * 1.25)))
    along = Image.linear_gradient("L").resize(size).point(lambda v: int(255 * math.sin(math.pi * min(1.0, max(0.0, (v - 40) / 190))) ** 1.5))
    shafts = ImageChops.multiply(ImageChops.multiply(shafts.filter(ImageFilter.GaussianBlur(14)), falloff), along)
    g = comp(g, solid(size, GOLD_CORE, shafts))
    # Little gold leaves drifting in the light.
    for i in range(13):
        a = rng.uniform(0, 2 * math.pi)
        d = rng.uniform(40, 175)
        x, y = C[0] + math.cos(a) * d, C[1] + math.sin(a) * d * 0.85
        ln = rng.uniform(12, 22)
        leaf = Image.new("L", (int(ln * 3), int(ln * 3)), 0)
        lc = ln * 1.5
        ImageDraw.Draw(leaf).polygon([(lc - ln / 2, lc), (lc - ln * 0.1, lc - ln * 0.24), (lc + ln / 2, lc), (lc - ln * 0.1, lc + ln * 0.24)], fill=255)
        ImageDraw.Draw(leaf).line([(lc - ln / 2 - 3, lc), (lc + ln / 2, lc)], fill=0, width=1)
        leaf = leaf.rotate(rng.uniform(0, 360), resample=Image.BICUBIC).filter(ImageFilter.GaussianBlur(0.5))
        layer = Image.new("L", size, 0)
        layer.paste(leaf, (int(x - lc), int(y - lc)))
        vis = 1.0 - min(1.0, d / 220)
        g = comp(g, glow(layer, GOLD, 4, 0.7 * vis), gold_paint(size, scale_l(layer, 0.5 + 0.5 * vis), rng, angle=rng.uniform(0, 360)))
    motes = Image.new("L", size, 0)
    md = ImageDraw.Draw(motes)
    for i in range(46):
        a = rng.uniform(0, 2 * math.pi)
        d = rng.uniform(10, 200) ** 1.0
        x, y = C[0] + math.cos(a) * d, C[1] + math.sin(a) * d * 0.85
        r = rng.uniform(1.2, 3.4)
        md.ellipse((x - r, y - r, x + r, y + r), fill=int(255 * (1 - d / 240)))
    g = comp(g, glow(motes, GOLD, 5, 1.6), solid(size, GOLD_CORE, motes))
    edge = Image.radial_gradient("L").resize(size).point(lambda v: 255 if v < 170 else max(0, int(255 * (250 - v) / 80)))
    g = clip(g, edge)
    sheet.alpha_composite(g, (0, 0))

    # Relic tag, drawn at 4x and reduced: a bone luggage tag on a gold cord, an open journal and a crimson ribbon.
    F = 4
    tw, th = 128, 192
    size = (tw * F, th * F)
    t = canvas(*size)
    x0, x1, y0, y1, ch = 20 * F, 108 * F, 38 * F, 182 * F, 22 * F
    jitter = lambda v: v + rng.uniform(-0.8, 0.8) * F  # noqa: E731
    outline_pts = [(x0 + ch, y0), (x1 - ch, y0), (x1, y0 + ch), (x1, y1), (x0, y1), (x0, y0 + ch)]
    body = Image.new("L", size, 0)
    pts = []
    for (ax, ay), (bx, by) in zip(outline_pts, outline_pts[1:] + outline_pts[:1]):
        for j in range(12):
            u = j / 12
            pts.append((jitter(ax + (bx - ax) * u), jitter(ay + (by - ay) * u)))
    ImageDraw.Draw(body).polygon(pts, fill=255)
    t = comp(t, glow(body, (0, 0, 0), 5 * F, 0.6))
    t = comp(t, paper_fill(size, body, rng, amount=0.22))
    edge_shade = ring_in(body, 9 * F).filter(ImageFilter.GaussianBlur(5 * F))
    t = comp(t, clip(solid(size, PAPER, scale_l(edge_shade, 0.8)), body))
    t = comp(t, solid(size, INK, ring_out(body, 3.2 * F)))
    inset = shrink(body, 7 * F)
    t = comp(t, solid(size, GOLD_DEEP, scale_l(inner_edge(inset, int(1.2 * F)), 0.9)))
    # Grommet and cord.
    hx, hy = 64 * F, 52 * F
    cord = canvas(*size)
    ry, rx = 22 * F, 10 * F
    cpts = [(hx + math.cos(math.radians(a)) * rx * (1 + 0.15 * math.sin(math.radians(a))), hy - ry + math.sin(math.radians(a)) * ry)
            for a in range(100, 441, 10)]
    cord = comp(cord, brush_stroke(size, cpts, 6.5 * F, INK, 255, seed=rng.randrange(1 << 30), taper=False, bristles=4))
    cord = comp(cord, brush_stroke(size, cpts, 3.6 * F, GOLD, 255, seed=rng.randrange(1 << 30), taper=False, bristles=3))
    grom = ring_mask(size, (hx, hy), 5 * F, 10 * F)
    hole = ring_mask(size, (hx, hy), 0, 5 * F)
    t = comp(t, solid(size, INK, outline_of(grom, int(1.6 * F))), gold_paint(size, grom, rng, angle=-30))
    t.putalpha(ImageChops.subtract(t.getchannel("A"), hole))
    t = comp(t, clip(cord, ImageChops.invert(ring_mask(size, (hx, hy), 0, 9 * F))))
    # Open journal.
    bx, by = 64 * F, 112 * F
    bwid, bh = 34 * F, 30 * F
    book = canvas(*size)
    cover = Image.new("L", size, 0)
    ImageDraw.Draw(cover).polygon([(bx - bwid - 4 * F, by - bh / 2 + 4 * F), (bx, by - bh / 2 + 9 * F), (bx + bwid + 4 * F, by - bh / 2 + 4 * F),
                                   (bx + bwid + 4 * F, by + bh / 2 + 5 * F), (bx, by + bh / 2 + 10 * F), (bx - bwid - 4 * F, by + bh / 2 + 5 * F)], fill=255)
    book = comp(book, solid(size, CRIMSON_DEEP, cover), solid(size, INK, outline_of(cover, 2 * F)))
    for sgn in (-1, 1):
        page = Image.new("L", size, 0)
        top = [(bx + sgn * bwid * u, by - bh / 2 + 6 * F * (1 - u) ** 0.5 * 0.0 - 5 * F * math.sin(math.pi * u * 0.9)) for u in [j / 14 for j in range(15)]]
        bot = [(bx + sgn * bwid * u, by + bh / 2 - 4 * F * math.sin(math.pi * u * 0.9)) for u in [j / 14 for j in range(15)]][::-1]
        top[0] = (bx, by - bh / 2 + 5 * F)
        bot[-1] = (bx, by + bh / 2 + 5 * F)
        ImageDraw.Draw(page).polygon(top + bot, fill=255)
        book = comp(book, paper_fill(size, page, rng, base=GOLD_CORE, amount=0.12))
        book = comp(book, solid(size, INK, outline_of(page, int(1.6 * F))))
        lines = Image.new("L", size, 0)
        ldr = ImageDraw.Draw(lines)
        for k in range(4):
            yy = by - bh / 2 + 8 * F + k * 5.5 * F
            ldr.line([(bx + sgn * 6 * F, yy), (bx + sgn * (bwid - 7 * F - rng.uniform(0, 6) * F), yy - 2 * F)], fill=150, width=int(1.4 * F))
        book = comp(book, clip(solid(size, INK_SOFT, lines), page))
    gut = Image.new("L", size, 0)
    ImageDraw.Draw(gut).line([(bx, by - bh / 2 + 5 * F), (bx, by + bh / 2 + 5 * F)], fill=255, width=int(2.2 * F))
    book = comp(book, solid(size, INK, gut))
    ribbon = Image.new("L", size, 0)
    rx = bx + 6 * F
    ImageDraw.Draw(ribbon).polygon([(rx - 3.5 * F, by + 4 * F), (rx + 3.5 * F, by + 4 * F), (rx + 4 * F, by + 44 * F), (rx, by + 38 * F), (rx - 4 * F, by + 44 * F)], fill=255)
    t = comp(t, solid(size, INK, outline_of(ribbon, int(1.6 * F))), solid(size, CRIMSON, ribbon))
    t = comp(t, book)
    # A gold spark over the book, so the tag reads as a relic.
    sp = star(size, (bx, 72 * F), 9 * F, GOLD_CORE, 255, thin=0.16)
    t = comp(t, glow(sp, GOLD, 3 * F, 1.2), solid(size, INK, scale_l(outline_of(sp, F), 0.8)), solid(size, GOLD, sp))
    tag = downsample(t, F)
    sheet.alpha_composite(tag, (512, 0))
    sprites = [{"name": "forest-glow", "box": [0, 0, 512, 512]}, {"name": "relic-tag", "box": [512, 0, 640, 192]}]
    return sheet, sprites


def ring_barrier(rng):
    """3x3 sheet of 256px cells, all centred on the Monolith so they stack: per-segment held / not held, the base
    track, and two shimmer frames of the Barrier."""
    CELL, F = 256, 2
    S = CELL * F
    size, C = (S, S), (S // 2, S // 2)
    R = 88 * F
    spans = [(-90 + 120 * i - 52, -90 + 120 * i + 52) for i in range(3)]
    light = conic_light(size, C, lambda deg: 128 + 127 * math.cos(math.radians(deg - 230)))
    # Everything fades out before the cell edge, so stacked cells never show a square.
    keep = Image.new("L", size, 0)
    for k in range(24):
        r = (127 - k * 0.4) * F
        ImageDraw.Draw(keep).ellipse((C[0] - r, C[1] - r, C[0] + r, C[1] + r), fill=min(255, (k + 1) * 11))

    def mid(i, r):
        a = math.radians(-90 + 120 * i)
        return (C[0] + math.cos(a) * r, C[1] + math.sin(a) * r)

    def base():
        img = canvas(*size)
        track = ring_mask(size, C, R - 5 * F, R + 5 * F)
        img = comp(img, glow(track, (0, 0, 0), 4 * F, 0.5), solid(size, INK, scale_l(track, 0.75)))
        img = comp(img, solid(size, GOLD_DEEP, scale_l(outline_of(ring_mask(size, C, 0, R + 5 * F), F), 0.8)))
        img = comp(img, solid(size, GOLD_DEEP, scale_l(inner_edge(ring_mask(size, C, 0, R - 5 * F), F), 0.8)))
        for k in range(3):
            a = math.radians(-30 + 120 * k)
            img = comp(img, gem(size, C[0] + math.cos(a) * R, C[1] + math.sin(a) * R, a, 9 * F, glow_r=3 * F))
        return img

    def held(i):
        a0, a1 = spans[i]
        img = canvas(*size)
        band = canvas(*size)
        band = comp(band, painted_arc(size, C, R, a0, a1, 30 * F, CRIMSON_DEEP, rng, passes=2, bristles=10))
        band = comp(band, painted_arc(size, C, R, a0 + 1, a1 - 1, 24 * F, CRIMSON, rng, passes=2, bristles=9))
        img = comp(img, glow(band.getchannel("A"), GOLD, 7 * F, 0.9), glow(band.getchannel("A"), (0, 0, 0), 3 * F, 0.6))
        img = comp(img, solid(size, INK, scale_l(outline_of(band.getchannel("A").point(lambda v: 255 if v > 90 else 0), 2 * F), 0.9)))
        img = comp(img, band)
        img = comp(img, clip(bristle_grain(size, C, R - 13 * F, R + 13 * F, rng, [CRIMSON_DEEP, (214, 60, 72)], 26),
                             band.getchannel("A")))
        gold = painted_arc(size, C, R, a0 + 3, a1 - 3, 12 * F, GOLD, rng, passes=2, bristles=7)
        img = comp(img, gold_paint(size, gold.getchannel("A"), rng, light=light, bands=1.0))
        img = comp(img, painted_arc(size, C, R - 2 * F, a0 + 12, a0 + 40, 3 * F, (250, 224, 166), rng, alpha=170, passes=1, bristles=3))
        img = comp(img, pip(size, mid(i, R), 13 * F, 2, GOLD))
        return img

    def open_(i):
        a0, a1 = spans[i]
        img = canvas(*size)
        band = painted_arc(size, C, R, a0 + 2, a1 - 2, 22 * F, INK, rng, alpha=235, passes=2, bristles=9)
        img = comp(img, band)
        img = comp(img, clip(painted_arc(size, C, R, a0 + 4, a1 - 4, 15 * F, INK_SOFT, rng, alpha=220, passes=1, bristles=7), band.getchannel("A")))
        dash = Image.new("L", size, 0)
        dd = ImageDraw.Draw(dash)
        a = a0 + 8
        while a < a1 - 8:
            if abs(a - (a0 + a1) / 2) > 11:
                dd.line(arc_pts(C, R, a, a + 4, n=4), fill=190, width=int(2.4 * F))
            a += 9
        img = comp(img, solid(size, BONE, dash.filter(ImageFilter.GaussianBlur(0.5 * F))))
        img = comp(img, solid(size, GOLD_DEEP, scale_l(outline_of(band.getchannel("A").point(lambda v: 255 if v > 90 else 0), F), 0.55)))
        img = comp(img, pip(size, mid(i, R), 11 * F, 0, GOLD))
        return img

    def barrier(phase):
        img = canvas(*size)
        hw = 16 * F
        shell = ring_mask(size, C, R - hw, R + hw)
        img = comp(img, glow(shell, TEAL, 9 * F, 0.9), solid(size, TEAL_DEEP, scale_l(shell.filter(ImageFilter.GaussianBlur(3 * F)), 0.55)))
        img = comp(img, solid(size, LIGHT_TEAL, scale_l(ring_mask(size, C, R - 10 * F, R + 10 * F).filter(ImageFilter.GaussianBlur(5 * F)), 0.4)))
        # A woven lattice of light: two sets of slanted threads; their brightness ripples around the ring, and the two
        # frames ripple out of phase so alternating them shimmers.
        rng_b = random.Random(SEED + 77)
        lattice = Image.new("L", size, 0)
        ld = ImageDraw.Draw(lattice)
        n = 36
        for sgn in (1, -1):
            for k in range(n):
                a = k * 360 / n + (5 if sgn < 0 else 0)
                wave = 0.5 + 0.5 * math.cos(math.radians(3 * a) + phase * math.pi + (0 if sgn > 0 else 2.1))
                v = int(40 + 200 * wave ** 1.8)
                p0 = arc_pts(C, R - hw * 0.85, a, a, n=1)[0]
                p1 = arc_pts(C, R + hw * 0.85, a + sgn * 11, a + sgn * 11, n=1)[0]
                ld.line([p0, p1], fill=v, width=int(1.6 * F))
        lattice = ImageChops.multiply(lattice.filter(ImageFilter.GaussianBlur(0.6 * F)), shell.filter(ImageFilter.GaussianBlur(4 * F)))
        img = comp(img, glow(lattice, LIGHT_TEAL, 2 * F, 1.0), solid(size, BONE, scale_l(lattice, 0.9)))
        # Bright crests travelling the edges.
        crest = canvas(*size)
        for k in range(3):
            for edge_r, off in ((R + hw - 2 * F, 0), (R - hw + 2 * F, 60)):
                a0 = -90 + 120 * k + off + phase * 60 + rng_b.uniform(-6, 6)
                crest = comp(crest, painted_arc(size, C, edge_r, a0, a0 + 44, 4 * F, GOLD_CORE, rng_b, alpha=235, passes=1, bristles=3))
        img = comp(img, glow(crest.getchannel("A"), GOLD, 3 * F, 1.1), crest)
        img = comp(img, solid(size, BONE, scale_l(outline_of(ring_mask(size, C, 0, R + hw), F), 0.7)),
                   solid(size, BONE, scale_l(inner_edge(ring_mask(size, C, 0, R - hw), F), 0.7)))
        sp = Image.new("L", size, 0)
        for k in range(7):
            a = math.radians(rng_b.uniform(0, 360) + phase * 25)
            rr = R + rng_b.uniform(-18, 18) * F
            sp = ImageChops.lighter(sp, star(size, (C[0] + math.cos(a) * rr, C[1] + math.sin(a) * rr), rng_b.uniform(5, 8) * F, GOLD_CORE, 255))
        img = comp(img, glow(sp, GOLD, 3 * F, 1.2), solid(size, GOLD_CORE, sp))
        return img

    cells = [("segment-1-held", held(0)), ("segment-2-held", held(1)), ("segment-3-held", held(2)),
             ("segment-1-not-held", open_(0)), ("segment-2-not-held", open_(1)), ("segment-3-not-held", open_(2)),
             ("ring-base", base()), ("barrier-1", barrier(0)), ("barrier-2", barrier(1))]
    sheet = canvas(CELL * 3, CELL * 3)
    sprites = []
    for idx, (name, img) in enumerate(cells):
        x0, y0 = (idx % 3) * CELL, (idx // 3) * CELL
        sheet.alpha_composite(downsample(clip(img, keep), F), (x0, y0))
        sprites.append({"name": name, "box": [x0, y0, x0 + CELL, y0 + CELL]})
    return sheet, sprites


# ----------------------------------------------------------------------------------------------------------- entry

def make(out_dir, fonts):
    out = []

    def emit(asset, rel, img, sprites=()):
        save(img, out_dir, rel)
        out.append({"asset": asset, "path": f"art/{rel}", "size": [img.width, img.height], "sprites": list(sprites)})

    emit("asset.monolith-plate", "map/monolith-number.png", monolith_plate(random.Random(SEED + 1)))
    emit("asset.sacred-river", "map/sacred-river.png", sacred_river(random.Random(SEED + 2)))
    emit("asset.final-battlefield", "map/final-battlefield.png", final_battlefield(random.Random(SEED + 3)))
    sheet, sprites = petals(random.Random(SEED + 4))
    emit("asset.petals", "fx/petals.png", sheet, sprites)
    emit("asset.route-dots", "map/route-dots.png", route_dots(random.Random(SEED + 5)))
    emit("asset.smeared-canvas", "fx/smeared-canvas.png", smeared_canvas(random.Random(SEED + 6)))
    emit("asset.dusk-tint", "fx/dusk-tint.png", dusk_tint(random.Random(SEED + 7)))
    sheet, sprites = journal_glow(random.Random(SEED + 8))
    emit("asset.journal-glow", "fx/journal-glow.png", sheet, sprites)
    sheet, sprites = ring_barrier(random.Random(SEED + 9))
    emit("asset.ring-barrier", "map/ring-barrier.png", sheet, sprites)
    return out

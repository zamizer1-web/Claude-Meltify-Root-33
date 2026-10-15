"""Gauges for Root 33: Gustave's Arm (three charge pips and the Overcharge toggle), Esquie's four-slot rock ring, and
Sciel's Sun and Moon gauges with their two Twilight charge pips.

Everything is original and drawn from scratch with Pillow in the shared painterly style (tools/art/style.py): ink
outlines, bone paper, gold leaf, crimson and river teal, with brushy, slightly uneven edges. The Arm is an invented
brass-and-iron gauntlet plate, not any character's actual design. Every random choice comes from a seeded
random.Random (noise included), so the same code always paints the same pixels. Pieces are drawn at F x and reduced.

Each sheet also carries "anchor" sprites (named *-slot-*): boxes on the frame where the matching state sprite
(a pip, a step, a slot glow or a rock token) should be drawn, so overlays never hard-code positions.
"""
import math
import random
import sys
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageOps

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from style import (BONE, CRIMSON, DUSK, GOLD, GOLD_CORE, GOLD_DEEP, INK, INK_SOFT, NIGHT, SUIT,  # noqa: E402
                   TEAL, TEAL_DEEP, brush_stroke, canvas, curve, glow, solid)

SEED = 3360
F = 3  # supersampling factor: everything is painted at F x and reduced with Lanczos

LIGHT_TEAL = (176, 226, 222)
MOON_TEAL = (126, 204, 204)
SILVER = (204, 214, 220)
SILVER_DEEP = (104, 122, 134)
SILVER_HI = (236, 244, 246)
IRON = (92, 90, 106)
IRON_DEEP = (40, 38, 50)
IRON_HI = (150, 148, 164)
IRON_LIGHT = (212, 210, 222)
ARC_BLUE = (74, 150, 236)
ARC_DEEP = (24, 58, 132)
ARC_LIGHT = (178, 222, 255)
ARC_WHITE = (246, 251, 255)
WHITE = (255, 255, 255)


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
    cw, ch = (cell, cell) if isinstance(cell, (int, float)) else cell
    sw, sh = max(2, int(-(-w // cw))), max(2, int(-(-h // ch)))
    im = Image.frombytes("L", (sw, sh), rng.randbytes(sw * sh))
    if (sw, sh) != (w, h):
        im = im.resize((w, h), Image.BICUBIC)
    if blur:
        im = im.filter(ImageFilter.GaussianBlur(blur))
    return ImageOps.autocontrast(im) if stretch else im


def lerp_rgb(a, b, t):
    return tuple(int(round(a[i] + (b[i] - a[i]) * t)) for i in range(3))


def ramp(stops, t):
    t = min(max(t, stops[0][0]), stops[-1][0])
    for (t0, c0), (t1, c1) in zip(stops, stops[1:]):
        if t <= t1:
            return lerp_rgb(c0, c1, (t - t0) / ((t1 - t0) or 1))
    return stops[-1][1]


def map_l(mask, stops):
    """Colour an L image through a ramp (returns RGB)."""
    lut = [ramp(stops, v / 255) for v in range(256)]
    return Image.merge("RGB", [mask.point([lut[v][c] for v in range(256)]) for c in range(3)])


def paint(size, light, stops, mask):
    out = map_l(light, stops).convert("RGBA")
    out.putalpha(mask)
    return out


def linear_light(size, angle):
    w, h = size
    d = int(math.hypot(w, h)) + 4
    g = Image.linear_gradient("L").resize((d, d)).rotate(angle, resample=Image.BICUBIC)
    return g.crop(((d - w) // 2, (d - h) // 2, (d - w) // 2 + w, (d - h) // 2 + h))


def radial_light(size, center, radius, power=1.0, steps=48):
    """L image: 255 at `center`, falling to 0 at `radius`."""
    m = Image.new("L", size, 0)
    d = ImageDraw.Draw(m)
    cx, cy = center
    for k in range(steps):
        r = radius * (1 - k / steps)
        d.ellipse((cx - r, cy - r, cx + r, cy + r), fill=int(255 * ((k + 1) / steps) ** power))
    return m.filter(ImageFilter.GaussianBlur(max(1.0, radius / steps * 1.5)))


def cyl_light(size, y0, y1, hi=0.3, power=0.8):
    """Horizontal-cylinder light: brightest at `hi` of the way from y0 to y1, dark at both ends."""
    w, h = size
    col = Image.new("L", (1, h))
    for y in range(h):
        t = min(max((y - y0) / max(1, y1 - y0), 0.0), 1.0)
        v = 1 - (abs(t - hi) / (1 - hi if t > hi else hi))
        col.putpixel((0, y), int(255 * max(0.0, v) ** power))
    return col.resize((w, h))


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


def silver_paint(size, mask, rng, light=None, angle=25, bands=1.5, tint=MOON_TEAL):
    """Silver with a cool teal sheen, the Moon's counterpart to gold leaf."""
    light = light if light is not None else linear_light(size, angle)
    sheen = map_l(light.point(lambda v: int(255 * (0.18 + 0.72 * abs(math.sin(v / 255 * math.pi * bands))))),
                  [(0, SILVER_DEEP), (0.45, lerp_rgb(SILVER, tint, 0.35)), (0.8, SILVER), (1, SILVER_HI)])
    speck = noise_l(size, rng, stretch=False).point(lambda v: 255 if v > 247 else 0).filter(ImageFilter.GaussianBlur(0.6))
    sheen = Image.composite(Image.new("RGB", size, WHITE), sheen, speck.point(lambda v: v // 2))
    out = sheen.convert("RGBA")
    out.putalpha(mask)
    return out


def rough_disc(size, c, r, rng, wobble=0.02, jitter=0.003, steps=220, sy=1.0):
    harm = [(k, rng.uniform(0, 2 * math.pi), rng.uniform(0.35, 1.0) / k) for k in range(2, 8)]
    pts = []
    for i in range(steps):
        a = 2 * math.pi * i / steps
        f = 1 + wobble * sum(amp * math.sin(k * a + ph) for k, ph, amp in harm) + rng.uniform(-jitter, jitter)
        pts.append((c[0] + math.cos(a) * r * f, c[1] + math.sin(a) * r * f * sy))
    m = Image.new("L", size, 0)
    ImageDraw.Draw(m).polygon(pts, fill=255)
    return m


def rough_poly(pts, rng, wobble, seg):
    """Subdivide a closed polygon and nudge each point with a smoothed random walk, like a brush-cut edge."""
    out = []
    n = len(pts)
    for i in range(n):
        (x0, y0), (x1, y1) = pts[i], pts[(i + 1) % n]
        k = max(1, int(math.hypot(x1 - x0, y1 - y0) / seg))
        for j in range(k):
            t = j / k
            out.append([x0 + (x1 - x0) * t, y0 + (y1 - y0) * t])
    walk, v = [], 0.0
    for _ in out:
        v = v * 0.6 + rng.uniform(-1, 1)
        walk.append(v)
    for p, a, b in zip(out, walk, walk[3:] + walk[:3]):
        p[0] += a * wobble
        p[1] += b * wobble
    return [tuple(p) for p in out]


def rough_rrect(size, box, radius, rng, wobble):
    """A rounded rectangle with a softly wobbling, brush-cut edge."""
    x0, y0, x1, y1 = box
    pts = []
    for cx, cy, a0 in ((x1 - radius, y0 + radius, -90), (x1 - radius, y1 - radius, 0), (x0 + radius, y1 - radius, 90),
                       (x0 + radius, y0 + radius, 180)):
        for k in range(10):
            a = math.radians(a0 + 90 * k / 9)
            pts.append((cx + math.cos(a) * radius, cy + math.sin(a) * radius))
    return smooth(poly_mask(size, rough_poly(pts, rng, wobble, 4 * F)), 0.8 * F)


def circle(size, c, r):
    m = Image.new("L", size, 0)
    ImageDraw.Draw(m).ellipse((c[0] - r, c[1] - r, c[0] + r, c[1] + r), fill=255)
    return m


def ring_mask(size, c, r_in, r_out):
    return ImageChops.subtract(circle(size, c, r_out), circle(size, c, r_in))


def grow(mask, px):
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


def union(*masks):
    out = masks[0]
    for m in masks[1:]:
        out = ImageChops.lighter(out, m)
    return out


def shift(mask, dx, dy):
    """Move a mask without wrapping."""
    out = Image.new(mask.mode, mask.size, 0)
    out.paste(mask, (int(dx), int(dy)))
    return out


def smooth(mask, r):
    return mask.filter(ImageFilter.GaussianBlur(r)).point(lambda v: 255 if v > 127 else 0).filter(ImageFilter.GaussianBlur(0.7))


def star(size, c, r, alpha=255, thin=0.18):
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


def taper_mask(size, pts, w0, w1, fill=255):
    """A line whose width runs from w0 to w1 (round joints)."""
    m = Image.new("L", size, 0)
    d = ImageDraw.Draw(m)
    n = len(pts) - 1
    for i, (a, b) in enumerate(zip(pts, pts[1:])):
        w = w0 + (w1 - w0) * (i + 1) / max(1, n)
        d.line([a, b], fill=fill, width=max(1, int(round(w))))
        r = w / 2
        d.ellipse((b[0] - r, b[1] - r, b[0] + r, b[1] + r), fill=fill)
    return m


def desaturate(img, amount, dim=1.0):
    a = img.getchannel("A")
    rgb = img.convert("RGB")
    grey = ImageOps.grayscale(rgb).convert("RGB")
    out = Image.blend(rgb, grey, amount)
    if dim != 1.0:
        out = out.point(lambda v: int(v * dim))
    out = out.convert("RGBA")
    out.putalpha(a)
    return out


def ink_edge(size, mask, px, rgb=INK, heavy=0.0):
    """An ink outline around a mask, weighted toward the lower right like a pen pressed on the shadow side."""
    g = grow(mask, px)
    if heavy:
        g = union(g, grow(shift(mask, heavy * 0.5, heavy), px))
    return solid(size, rgb, ImageChops.subtract(g, mask))


def drop_shadow(mask, dy, r, strength=0.7):
    return glow(shift(mask, 0, dy), (0, 0, 0), r, strength)


def dark_body(size, rng, a=INK, b=(46, 40, 62), wash=None, wash_from=0.55):
    """A painted ink body: colour a clouded toward b, faint horizontal brush streaks, an optional low colour wash."""
    w, h = size
    clouds = noise_l(size, rng, cell=18 * F, blur=5 * F)
    streaks = noise_l(size, rng, cell=(70 * F, 2 * F), blur=1.2 * F)
    n = ImageOps.autocontrast(ImageChops.add(clouds, streaks, 2.0, 0)).point(lambda v: int(v * 0.6))
    out = Image.composite(Image.new("RGB", size, b), Image.new("RGB", size, a), n)
    if wash:
        g = Image.linear_gradient("L").resize(size).point(lambda v: int(max(0, v / 255 - wash_from) / (1 - wash_from) * 120))
        out = Image.composite(Image.new("RGB", size, wash), out, g)
    return out.convert("RGBA")


def edge_fade(size, border):
    """Opaque inside, fading to clear over `border` px at every edge (so glows never end in a hard line)."""
    w, h = size
    m = Image.new("L", size, 0)
    d = ImageDraw.Draw(m)
    steps = 16
    for k in range(steps + 1):  # outermost (clear) first, each inset rectangle a little more opaque
        inset = border * k / steps
        d.rectangle((inset, inset, w - 1 - inset, h - 1 - inset), fill=int(255 * k / steps))
    return m


def soften_edges(img, border):
    img.putalpha(ImageChops.multiply(img.getchannel("A"), edge_fade(img.size, border)))
    return img


def bolt_path(p0, p1, rng, depth=4, jag=0.22):
    """A jagged lightning path from p0 to p1 by midpoint displacement."""
    pts = [p0, p1]
    for d in range(depth):
        nxt = [pts[0]]
        for a, b in zip(pts, pts[1:]):
            mx, my = (a[0] + b[0]) / 2, (a[1] + b[1]) / 2
            ln = math.hypot(b[0] - a[0], b[1] - a[1])
            nx, ny = -(b[1] - a[1]) / (ln or 1), (b[0] - a[0]) / (ln or 1)
            off = rng.uniform(-jag, jag) * ln
            nxt += [(mx + nx * off, my + ny * off), b]
        pts = nxt
    return pts


def bolt_layer(size, p0, p1, rng, width, core=ARC_WHITE, halo=ARC_BLUE, depth=4, jag=0.22, glow_r=None):
    pts = bolt_path(p0, p1, rng, depth, jag)
    m = taper_mask(size, pts, width, width * 0.25)
    gr = glow_r if glow_r is not None else width * 2.2
    return comp(canvas(*size), glow(m, halo, gr, 1.6), solid(size, core, m))


def downsample(img, f=F):
    return img.resize((img.width // f, img.height // f), Image.LANCZOS)


def save(img, out_dir, rel):
    p = Path(out_dir) / rel
    p.parent.mkdir(parents=True, exist_ok=True)
    img.save(p, optimize=True)
    return p


def lightning_glyph(c, s):
    """A plain lightning-bolt glyph polygon (unit s) centred on c."""
    pts = [(3, -19), (-11, 2), (-2, 2), (-6, 19), (11, -4), (2, -4), (7, -19)]
    return [(c[0] + x * s, c[1] + y * s) for x, y in pts]


# ---------------------------------------------------------------------------------------------------- Gustave's Arm

ARM_PANEL = (416, 152)
ARM_MED = (68, 72)          # gear medallion centre (the "elbow")
ARM_SOCKETS = [(170, 72), (250, 72), (330, 72)]
ARM_PIP = 96                # pip cell
TOGGLE = (208, 80)


def arm_panel(rng):
    W, H = ARM_PANEL
    size = (W * F, H * F)

    def P(x, y):
        return (x * F, y * F)

    def top(x):
        return 22 + (x - 100) * 12 / 272

    def bot(x):
        return 122 - (x - 100) * 12 / 272

    body_pts = [P(100, 22), P(236, 28), P(372, 34), P(404, 54), P(404, 90), P(372, 110), P(236, 116), P(100, 122)]
    body = smooth(poly_mask(size, rough_poly(body_pts, rng, 0.9 * F, 5 * F)), 1.2 * F)
    med = rough_disc(size, P(*ARM_MED), 60 * F, rng, wobble=0.012)
    whole = union(body, med)
    img = comp(canvas(*size), drop_shadow(whole, 5 * F, 7 * F, 0.75))

    # The plate: brushed iron with a rounded, forearm-like light.
    light = cyl_light(size, 22 * F, 122 * F, hi=0.28)
    streak = noise_l(size, rng, cell=(90 * F, 1.5 * F), blur=0.8 * F)
    light = ImageChops.add(scale_l(light, 0.85), scale_l(streak, 0.18))
    img = comp(img, paint(size, light, [(0, IRON_DEEP), (0.45, IRON), (0.78, IRON_HI), (1, IRON_LIGHT)], body))
    # Armour segments: faint seams where the plates overlap, each shaded on its left.
    for sx in (132, 372):
        seam = line_mask(size, [P(sx, top(sx) + 2), P(sx, bot(sx) - 2)], 1.6 * F)
        img = comp(img, clip(solid(size, INK, seam), body))
        sh = poly_mask(size, [P(sx, 0), P(sx + 10, 0), P(sx + 10, H), P(sx, H)]).filter(ImageFilter.GaussianBlur(4 * F))
        img = comp(img, clip(solid(size, IRON_DEEP, scale_l(sh, 0.5)), body))
    img = comp(img, gold_paint(size, ring_in(body, 3.2 * F), rng, angle=-20, bands=2.2))

    # The conduit: a groove running from the gear through every socket, with a dim blue vein in it.
    ch = line_mask(size, [P(112, 72), P(366, 72)], 12 * F)
    img = comp(img, solid(size, INK, ch))
    img = comp(img, solid(size, IRON_LIGHT, scale_l(ImageChops.subtract(shift(ch, 0, 2 * F), ch), 0.45)))
    vein = line_mask(size, [P(114, 72), P(364, 72)], 3 * F)
    img = comp(img, solid(size, ARC_DEEP, scale_l(vein, 0.9)))
    cap = circle(size, P(368, 72), 8 * F)
    img = comp(img, ink_edge(size, cap, 1.6 * F), gold_paint(size, cap, rng, angle=-35, bands=1.0))

    # Raised gold straps between the sockets, riveted top and bottom.
    for bx in (210, 290):
        strap = poly_mask(size, [P(bx - 5, top(bx) - 1), P(bx + 5, top(bx + 5) - 1), P(bx + 5, bot(bx + 5) + 1), P(bx - 5, bot(bx) + 1)])
        strap = ImageChops.multiply(strap, grow(body, 1))
        img = comp(img, solid(size, (0, 0, 0), scale_l(shift(strap, 2 * F, 0).filter(ImageFilter.GaussianBlur(2 * F)), 0.5)))
        img = comp(img, gold_paint(size, strap, rng, light=cyl_light(size, 22 * F, 122 * F, 0.3), bands=1.0))
        img = comp(img, solid(size, INK, ring_out(strap, 1.4 * F)))
    for rx, ry in ((210, top(210) + 9), (210, bot(210) - 9), (290, top(290) + 9), (290, bot(290) - 9),
                   (146, top(146) + 9), (146, bot(146) - 9), (356, top(356) + 9), (356, bot(356) - 9)):
        rv = circle(size, P(rx, ry), 3.6 * F)
        img = comp(img, solid(size, INK, ring_out(rv, 1.2 * F)), paint(size, radial_light(size, P(rx - 1.2, ry - 1.4), 5 * F),
                                                                      [(0, GOLD_DEEP), (0.6, GOLD), (1, GOLD_CORE)], rv))

    # Wrist cuff: three vents.
    for vy in (60, 72, 84):
        vent = line_mask(size, [P(382, vy), P(396, vy)], 3.4 * F)
        img = comp(img, solid(size, IRON_LIGHT, scale_l(ImageChops.subtract(shift(vent, 0, 1.4 * F), vent), 0.6)), solid(size, INK, vent))

    # Sockets: gold bezels round dark glass wells.
    for sx, sy in ARM_SOCKETS:
        c = P(sx, sy)
        bez = ring_mask(size, c, 26 * F, 33 * F)
        img = comp(img, solid(size, (0, 0, 0), scale_l(circle(size, (c[0] + 1.5 * F, c[1] + 2.5 * F), 34 * F).filter(ImageFilter.GaussianBlur(2.5 * F)), 0.6)))
        img = comp(img, gold_paint(size, bez, rng, light=radial_light(size, (c[0] - 12 * F, c[1] - 14 * F), 50 * F), bands=1.2))
        well = circle(size, c, 26 * F)
        img = comp(img, paint(size, radial_light(size, (c[0] + 6 * F, c[1] + 8 * F), 34 * F), [(0, (10, 10, 18)), (1, (34, 42, 66))], well))
        img = comp(img, clip(solid(size, INK, scale_l(ImageChops.subtract(well, shift(well, 4 * F, 5 * F)).filter(ImageFilter.GaussianBlur(2 * F)), 0.8)), well))
        img = comp(img, solid(size, INK, ring_out(circle(size, c, 33 * F), 1.6 * F)), solid(size, INK, ring_in(well, 1.4 * F)))

    # The elbow: an iron medallion with a gold cog round a dark lens.
    mc = P(*ARM_MED)
    img = comp(img, paint(size, radial_light(size, (mc[0] - 22 * F, mc[1] - 26 * F), 92 * F),
                          [(0, IRON_DEEP), (0.4, IRON), (0.85, IRON_HI), (1, IRON_LIGHT)], med))
    img = comp(img, gold_paint(size, ring_in(med, 4 * F), rng, angle=-30, bands=1.6))
    teeth, pts = 12, []
    for i in range(teeth * 4):
        a = 2 * math.pi * i / (teeth * 4) + 0.12
        r = 46 if (i % 4) in (0, 1) else 37
        pts.append((mc[0] + math.cos(a) * r * F, mc[1] + math.sin(a) * r * F))
    cog = ImageChops.subtract(smooth(poly_mask(size, pts), 0.8 * F), circle(size, mc, 27 * F))
    img = comp(img, solid(size, (0, 0, 0), scale_l(shift(cog, 1.5 * F, 2.5 * F).filter(ImageFilter.GaussianBlur(2 * F)), 0.55)))
    img = comp(img, gold_paint(size, cog, rng, light=radial_light(size, (mc[0] - 20 * F, mc[1] - 22 * F), 80 * F), bands=1.3))
    for k in range(6):
        a = 2 * math.pi * k / 6 + 0.3
        hole = circle(size, (mc[0] + math.cos(a) * 32 * F, mc[1] + math.sin(a) * 32 * F), 2.6 * F)
        img = comp(img, solid(size, INK, hole))
    img = comp(img, solid(size, INK, ring_out(cog, 1.5 * F)))
    hub = circle(size, mc, 26 * F)
    img = comp(img, paint(size, radial_light(size, (mc[0] - 9 * F, mc[1] - 10 * F), 40 * F), [(0, IRON_DEEP), (0.6, IRON), (1, IRON_HI)], hub))
    lens = circle(size, mc, 13 * F)
    img = comp(img, gold_paint(size, ring_mask(size, mc, 13 * F, 17 * F), rng, angle=-40, bands=1.0),
               paint(size, radial_light(size, (mc[0] - 3 * F, mc[1] - 4 * F), 18 * F), [(0, INK), (0.7, ARC_DEEP), (1, (60, 96, 160))], lens))
    hi = Image.new("L", size, 0)
    ImageDraw.Draw(hi).ellipse((mc[0] - 9 * F, mc[1] - 10 * F, mc[0] - 2 * F, mc[1] - 5 * F), fill=170)
    img = comp(img, solid(size, BONE, hi.filter(ImageFilter.GaussianBlur(1.2 * F))))
    img = comp(img, solid(size, INK, ring_out(circle(size, mc, 17 * F), 1.4 * F)))

    img = comp(img, ink_edge(size, whole, 2.6 * F, heavy=1.5 * F))
    return downsample(soften_edges(img, 3 * F))


def arm_pip(state, rng):
    """A charge pip: 0 empty dark lens, 1 lit electric blue, 2 blazing white with arcs of lightning."""
    S = ARM_PIP
    size = (S * F, S * F)
    c = (S * F / 2, S * F / 2)
    r, rl = 26 * F, 20 * F
    disc, lens = circle(size, c, r), circle(size, c, rl)
    img = canvas(*size)
    if state == 1:
        img = comp(img, glow(disc, ARC_BLUE, 7 * F, 1.1))
    if state == 2:
        img = comp(img, glow(disc, ARC_BLUE, 15 * F, 1.5), glow(disc, ARC_LIGHT, 8 * F, 1.7), glow(disc, ARC_WHITE, 4 * F, 1.4))
        rays = Image.new("L", size, 0)
        rd = ImageDraw.Draw(rays)
        for k in range(8):
            a = math.radians(k * 45 + 22.5)
            ln = r + (17 if k % 2 == 0 else 9) * F
            sp = 0.13 if k % 2 == 0 else 0.2
            rd.polygon([(c[0] + math.cos(a - sp) * r * 0.7, c[1] + math.sin(a - sp) * r * 0.7), (c[0] + math.cos(a) * ln, c[1] + math.sin(a) * ln),
                        (c[0] + math.cos(a + sp) * r * 0.7, c[1] + math.sin(a + sp) * r * 0.7)], fill=255)
        img = comp(img, glow(rays, ARC_LIGHT, 2 * F, 1.2), solid(size, ARC_WHITE, rays.filter(ImageFilter.GaussianBlur(0.6 * F))))
        for k in range(5):
            a = math.radians(k * 72 + rng.uniform(-14, 14) + 4)
            p0 = (c[0] + math.cos(a) * r * 0.95, c[1] + math.sin(a) * r * 0.95)
            p1 = (c[0] + math.cos(a + 0.25) * r * 1.62, c[1] + math.sin(a + 0.25) * r * 1.62)
            img = comp(img, bolt_layer(size, p0, p1, rng, 2.4 * F, depth=3, jag=0.3, glow_r=2.5 * F))
    img = comp(img, solid(size, (0, 0, 0), scale_l(shift(disc, 0, 2 * F).filter(ImageFilter.GaussianBlur(2 * F)), 0.6)))
    img = comp(img, solid(size, INK, disc))
    bez = ring_mask(size, c, rl + 1 * F, r - 1.6 * F)
    if state == 0:
        img = comp(img, solid(size, GOLD_DEEP, bez), clip(gold_paint(size, bez, rng, angle=-40, bands=1.0), scale_l(bez, 0.45)))
        img = comp(img, paint(size, radial_light(size, (c[0] + 5 * F, c[1] + 6 * F), 28 * F), [(0, (8, 8, 14)), (1, (36, 44, 70))], lens))
        glyph = poly_mask(size, lightning_glyph(c, 0.62 * F))
        img = comp(img, solid(size, (90, 104, 136), scale_l(ImageChops.subtract(shift(glyph, 1 * F, 1 * F), glyph), 0.6)),
                   solid(size, (22, 26, 40), glyph))
        hi = Image.new("L", size, 0)
        ImageDraw.Draw(hi).arc((c[0] - rl * 0.78, c[1] - rl * 0.78, c[0] + rl * 0.78, c[1] + rl * 0.78), 200, 260, fill=150, width=int(2.2 * F))
        img = comp(img, solid(size, BONE, hi.filter(ImageFilter.GaussianBlur(0.8 * F))))
    else:
        img = comp(img, gold_paint(size, bez, rng, light=radial_light(size, (c[0] - 10 * F, c[1] - 12 * F), 44 * F), bands=1.2))
        if state == 1:
            stops = [(0, ARC_DEEP), (0.45, ARC_BLUE), (0.85, ARC_LIGHT), (1, (226, 242, 255))]
            lc = (c[0] - 4 * F, c[1] - 5 * F)
        else:
            stops = [(0, ARC_BLUE), (0.3, ARC_LIGHT), (0.62, ARC_WHITE), (1, WHITE)]
            lc = c
        img = comp(img, paint(size, radial_light(size, lc, rl * 1.25, power=0.8), stops, lens))
        glyph = poly_mask(size, lightning_glyph(c, 0.62 * F))
        if state == 1:
            img = comp(img, glow(glyph, ARC_WHITE, 2 * F, 1.2), solid(size, (236, 246, 255), glyph))
        else:
            img = comp(img, glow(glyph, ARC_BLUE, 2.5 * F, 1.0), solid(size, (120, 186, 250), scale_l(ring_out(glyph, 1.2 * F), 0.8)),
                       solid(size, WHITE, glyph))
        hi = Image.new("L", size, 0)
        ImageDraw.Draw(hi).ellipse((c[0] - rl * 0.7, c[1] - rl * 0.78, c[0] - rl * 0.05, c[1] - rl * 0.3), fill=170)
        img = comp(img, clip(solid(size, WHITE, hi.filter(ImageFilter.GaussianBlur(1.6 * F))), lens))
    img = comp(img, solid(size, INK, ring_in(lens, 1.0 * F)), solid(size, INK, ring_out(disc, 1.2 * F)))
    img.putalpha(ImageChops.multiply(img.getchannel("A"), edge_fade(size, 8 * F)))
    return downsample(img)


def overcharge_toggle(armed, rng):
    """The Overcharge switch: a bolt badge and a slider. Off: dark, knob left. Armed: lit blue-white, knob right, crackling."""
    W, H = TOGGLE
    size = (W * F, H * F)

    def P(x, y):
        return (x * F, y * F)

    plate = rough_rrect(size, (P(14, 14)[0], P(14, 14)[1], P(200, 66)[0], P(200, 66)[1]), 26 * F, rng, 0.8 * F)
    badge = rough_disc(size, P(38, 40), 30 * F, rng, wobble=0.01)
    whole = union(plate, badge)
    img = canvas(*size)
    if armed:
        img = comp(img, glow(whole, ARC_BLUE, 8 * F, 1.3), glow(whole, ARC_LIGHT, 3.5 * F, 1.0))
    img = comp(img, drop_shadow(whole, 3 * F, 4 * F, 0.7))
    light = cyl_light(size, 14 * F, 66 * F, hi=0.3)
    img = comp(img, paint(size, light, [(0, IRON_DEEP), (0.5, IRON), (0.85, IRON_HI), (1, IRON_LIGHT)], plate))
    img = comp(img, gold_paint(size, ring_in(plate, 2.6 * F), rng, angle=-15, bands=2.0))
    # Slider track and knob.
    track = rough_rrect(size, (P(84, 29)[0], P(84, 29)[1], P(188, 51)[0], P(188, 51)[1]), 11 * F, rng, 0.3 * F)
    if armed:
        tl = linear_light(size, 0)
        img = comp(img, paint(size, tl, [(0, ARC_DEEP), (0.45, ARC_BLUE), (0.8, ARC_LIGHT), (1, ARC_WHITE)], track))
        for k in range(2):
            img = comp(img, clip(bolt_layer(size, P(88, 40 + (k - 0.5) * 6), P(160, 40 - (k - 0.5) * 6), rng, 1.6 * F, depth=4, jag=0.18,
                                            glow_r=1.5 * F), track))
    else:
        img = comp(img, paint(size, cyl_light(size, 29 * F, 51 * F, hi=0.85), [(0, (10, 10, 16)), (1, (40, 40, 54))], track))
    img = comp(img, clip(solid(size, INK, scale_l(ImageChops.subtract(track, shift(track, 0, 3 * F)).filter(ImageFilter.GaussianBlur(1.2 * F)), 0.9)), track))
    img = comp(img, solid(size, INK, ring_out(track, 1.4 * F)))
    kx = 174 if armed else 98
    knob = circle(size, P(kx, 40), 15 * F)
    img = comp(img, solid(size, (0, 0, 0), scale_l(shift(knob, 1 * F, 3 * F).filter(ImageFilter.GaussianBlur(2 * F)), 0.7)))
    if armed:
        img = comp(img, glow(knob, ARC_LIGHT, 5 * F, 1.4))
        img = comp(img, paint(size, radial_light(size, P(kx - 3, 36), 18 * F), [(0, ARC_LIGHT), (0.6, ARC_WHITE), (1, WHITE)], knob))
    else:
        img = comp(img, paint(size, radial_light(size, P(kx - 5, 34), 24 * F), [(0, IRON_DEEP), (0.5, IRON), (0.85, IRON_HI), (1, IRON_LIGHT)], knob))
    img = comp(img, gold_paint(size, ring_in(knob, 2.4 * F), rng, angle=-30, bands=1.0), solid(size, INK, ring_out(knob, 1.3 * F)))
    for gx in (-4, 0, 4):
        grip = line_mask(size, [P(kx + gx, 34), P(kx + gx, 46)], 1.2 * F)
        img = comp(img, solid(size, (90, 110, 150) if armed else IRON_DEEP, scale_l(grip, 0.8)))
    # The bolt badge.
    bc = P(38, 40)
    inner = circle(size, bc, 24 * F)
    if armed:
        img = comp(img, paint(size, radial_light(size, bc, 30 * F, power=0.8), [(0, ARC_DEEP), (0.55, (40, 90, 180)), (1, (110, 170, 240))], badge))
    else:
        img = comp(img, paint(size, radial_light(size, (bc[0] - 8 * F, bc[1] - 9 * F), 44 * F), [(0, IRON_DEEP), (0.6, IRON), (1, IRON_HI)], badge))
    img = comp(img, gold_paint(size, ring_in(badge, 3.4 * F), rng, angle=-35, bands=1.4))
    img = comp(img, solid(size, INK, scale_l(ring_out(inner, 1.0 * F), 0.8)))
    glyph = poly_mask(size, lightning_glyph(bc, 0.95 * F))
    if armed:
        img = comp(img, glow(glyph, ARC_BLUE, 5 * F, 1.6), glow(glyph, ARC_LIGHT, 2 * F, 1.3), solid(size, WHITE, glyph))
        for a in (-150, -30, 120):
            ar = math.radians(a)
            p0 = (bc[0] + math.cos(ar) * 26 * F, bc[1] + math.sin(ar) * 26 * F)
            p1 = (bc[0] + math.cos(ar + 0.2) * 37 * F, bc[1] + math.sin(ar + 0.2) * 37 * F)
            img = comp(img, bolt_layer(size, p0, p1, rng, 2.0 * F, depth=3, jag=0.3, glow_r=2 * F))
        sp = Image.new("L", size, 0)
        for x, y, r in ((196, 18, 6), (120, 12, 4.5), (190, 64, 4)):
            sp = ImageChops.lighter(sp, star(size, P(x, y), r * F))
        img = comp(img, glow(sp, ARC_BLUE, 2 * F, 1.4), solid(size, WHITE, sp))
    else:
        img = comp(img, solid(size, IRON_HI, scale_l(ImageChops.subtract(shift(glyph, 1.2 * F, 1.2 * F), glyph), 0.7)),
                   solid(size, (34, 34, 46), glyph))
    img = comp(img, ink_edge(size, whole, 2.2 * F, heavy=1.2 * F))
    img.putalpha(ImageChops.multiply(img.getchannel("A"), edge_fade(size, 4 * F)))
    return downsample(img)


def arm_sheet(rng):
    W, H = 512, 352
    sheet = canvas(W, H)
    sprites = []
    sheet.alpha_composite(arm_panel(rng), (0, 0))
    sprites.append(("panel", (0, 0) + ARM_PANEL))
    for i, (sx, sy) in enumerate(ARM_SOCKETS):
        h = ARM_PIP // 2
        sprites.append((f"pip-slot-{i + 1}", (sx - h, sy - h, sx + h, sy + h)))
    for i, name in enumerate(("pip-empty", "pip-blue", "pip-white")):
        x, y = i * (ARM_PIP + 4), 160
        sheet.alpha_composite(arm_pip(i, rng), (x, y))
        sprites.append((name, (x, y, x + ARM_PIP, y + ARM_PIP)))
    off = overcharge_toggle(False, rng)
    for i, (name, img) in enumerate((("overcharge-off", off), ("overcharge-armed", overcharge_toggle(True, rng)),
                                     ("overcharge-locked", fade(desaturate(off, 0.85, dim=0.75), 0.7)))):
        x, y = (0, 264) if i == 0 else ((216, 264) if i == 1 else (304, 168))
        sheet.alpha_composite(img, (x, y))
        sprites.append((name, (x, y, x + TOGGLE[0], y + TOGGLE[1])))
    return sheet, sprites


# -------------------------------------------------------------------------------------------------- Esquie's rock ring

RING = 352
RING_C = RING / 2
RING_R_OUT, RING_R_IN = 156, 58
SLOT_POS_R = 110
SLOT_OUT, SLOT_WELL = 42, 35
SLOT_CELL = 128
# name, angle on the ring, body colour, shade colour, mark (as on the rock tokens in art/tokens/rocks.png)
RING_ROCKS = [
    ("florrie", -90, (92, 152, 222), (36, 76, 150), "wave"),
    ("dorrie", 0, (244, 142, 128), (176, 70, 78), "coral"),
    ("soarrie", 90, (252, 240, 208), (206, 164, 94), "wing"),
    ("urrie", 180, (58, 76, 164), (22, 28, 84), "spiral"),
]
SAND = [(0, (104, 74, 68)), (0.28, (172, 132, 112)), (0.58, (222, 194, 164)), (0.84, (244, 230, 208)), (1, (255, 250, 238))]


def torus_light(n, c, r0, r1, L=(-0.5, -0.62, 0.6)):
    """Diffuse light on a plump ring (a torus seen from above), computed at n x n."""
    ln = math.sqrt(sum(v * v for v in L))
    lx, ly, lz = (v / ln for v in L)
    rm, hw = (r0 + r1) / 2, (r1 - r0) / 2
    im = Image.new("L", (n, n), 0)
    px = im.load()
    for y in range(n):
        dy = y + 0.5 - c
        for x in range(n):
            dx = x + 0.5 - c
            r = math.hypot(dx, dy) or 1e-6
            t = (r - rm) / hw
            if abs(t) > 1.08:
                continue
            t = max(-0.98, min(0.98, t))
            nz = math.sqrt(1 - t * t)
            d = (t * dx / r) * lx + (t * dy / r) * ly + nz * lz
            px[x, y] = int(255 * max(0.0, min(1.0, 0.18 + 0.82 * d)))
    return im


def mark_mask(size, kind, cx, cy, u):
    """A simple engraved version of each rock's mark (unit u)."""
    m = Image.new("L", size, 0)
    d = ImageDraw.Draw(m)
    sw = 0.24 * u

    def Q(x, y):
        return (cx + x * u, cy + y * u)

    def stroke(pts, w=sw):
        d.line(pts, fill=255, width=max(1, int(round(w))), joint="curve")
        for x, y in (pts[0], pts[-1]):
            d.ellipse((x - w / 2, y - w / 2, x + w / 2, y + w / 2), fill=255)

    if kind == "wave":
        for yo in (-0.32, 0.32):
            stroke([Q(-0.85 + 1.7 * i / 40, yo + 0.18 * math.sin(i / 40 * 2 * math.pi * 1.25 + 0.2)) for i in range(41)])
    elif kind == "coral":
        for path in [[(0.04, 0.85), (0.0, 0.4), (-0.06, -0.1), (-0.1, -0.7)], [(0.0, 0.42), (-0.42, 0.12), (-0.58, -0.4)],
                     [(-0.04, 0.06), (0.42, -0.18), (0.48, -0.68)], [(0.42, -0.18), (0.8, -0.32)]]:
            stroke([Q(x, y) for x, y in path])
    elif kind == "spiral":
        stroke([Q((0.08 + 0.8 * t) * math.cos(t * 2.1 * 2 * math.pi + 0.8), (0.08 + 0.8 * t) * math.sin(t * 2.1 * 2 * math.pi + 0.8) * 0.94)
                for t in [i / 120 for i in range(121)]], sw * 0.9)
    elif kind == "wing":
        for sgn in (1, -1):
            pts = curve(Q(sgn * 0.1, 0.3), Q(sgn * 0.22, -0.8), Q(sgn * 0.95, -0.62), 20)
            pts += curve(Q(sgn * 0.95, -0.62), Q(sgn * 0.9, 0.1), Q(sgn * 0.1, 0.3), 20)[1:]
            d.polygon(pts, fill=255)
        for sgn in (1, -1):
            for e0, e1 in (((0.3, 0.12), (0.62, -0.44)), ((0.2, 0.0), (0.42, -0.5))):
                d.line([Q(sgn * e0[0], e0[1]), Q(sgn * e1[0], e1[1])], fill=0, width=max(1, int(0.07 * u)))
    return m


def slot_well(size, c, rng, F_=F):
    """The recessed cup of one slot: gold bezel, sunken sand-coloured well with an inner shadow."""
    out = canvas(*size)
    disc = circle(size, c, SLOT_OUT * F_)
    well = circle(size, c, SLOT_WELL * F_)
    out = comp(out, solid(size, (0, 0, 0), scale_l(shift(disc, 1 * F_, 2 * F_).filter(ImageFilter.GaussianBlur(2.5 * F_)), 0.45)))
    out = comp(out, gold_paint(size, ImageChops.subtract(disc, well), rng,
                               light=radial_light(size, (c[0] - 16 * F_, c[1] - 18 * F_), 66 * F_), bands=1.25))
    lt = radial_light(size, (c[0] + 12 * F_, c[1] + 14 * F_), 52 * F_, power=0.9)
    out = comp(out, paint(size, lt, [(0, (92, 66, 60)), (0.5, (150, 116, 98)), (1, (204, 176, 148))], well))
    inner_sh = ImageChops.subtract(well, shift(well, 5 * F_, 6 * F_)).filter(ImageFilter.GaussianBlur(3 * F_))
    out = comp(out, clip(solid(size, (58, 38, 40), scale_l(inner_sh, 0.85)), well))
    rim_hi = ImageChops.subtract(well, shift(well, -2 * F_, -2.5 * F_)).filter(ImageFilter.GaussianBlur(1 * F_))
    out = comp(out, clip(solid(size, (246, 232, 206), scale_l(rim_hi, 0.6)), well))
    out = comp(out, solid(size, INK, ring_in(well, 1.2 * F_)), solid(size, INK, ring_out(disc, 1.4 * F_)))
    return out, well


def rock_ring(rng):
    size = (RING * F, RING * F)
    C = (RING_C * F, RING_C * F)
    outer = rough_disc(size, C, RING_R_OUT * F, rng, wobble=0.012, jitter=0.002)
    hole = rough_disc(size, C, RING_R_IN * F, rng, wobble=0.01, jitter=0.002)
    body = ImageChops.subtract(outer, hole)
    img = comp(canvas(*size), drop_shadow(outer, 4 * F, 6 * F, 0.7))

    # A plump, warm sand-and-bone ring lit from the upper left.
    tl = torus_light(RING, RING_C, RING_R_IN, RING_R_OUT).resize(size, Image.BICUBIC).filter(ImageFilter.GaussianBlur(F))
    img = comp(img, paint(size, tl, SAND, body))
    dabs = canvas(*size)
    for i in range(40):
        r = rng.uniform(RING_R_IN + 10, RING_R_OUT - 8) * F
        a0 = rng.uniform(0, 360)
        span = rng.uniform(14, 34)
        pts = [(C[0] + math.cos(math.radians(a)) * r, C[1] + math.sin(math.radians(a)) * r) for a in [a0 + span * k / 12 for k in range(13)]]
        lit = math.cos(math.radians(a0 + span / 2 - 225)) * ((r / F - RING_R_IN) / (RING_R_OUT - RING_R_IN) - 0.5) * -1
        col = (252, 240, 220) if lit > 0.02 else (150, 108, 96)
        dabs = comp(dabs, brush_stroke(size, pts, rng.uniform(4, 8) * F, col, rng.randint(40, 90), seed=rng.randrange(1 << 30), bristles=4))
    img = comp(img, clip(dabs, body))
    specks = Image.new("L", size, 0)
    for i in range(70):
        a = rng.uniform(0, 2 * math.pi)
        rr = rng.uniform(RING_R_IN + 6, RING_R_OUT - 6) * F
        x, y = C[0] + math.cos(a) * rr, C[1] + math.sin(a) * rr
        s = rng.uniform(0.7, 1.6) * F
        ImageDraw.Draw(specks).ellipse((x - s, y - s, x + s, y + s), fill=rng.randint(60, 130))
    img = comp(img, clip(solid(size, (120, 84, 74), specks), body))
    # Gold leaf on both rims.
    img = comp(img, gold_paint(size, ring_in(outer, 4.5 * F), rng, angle=-30, bands=2.5))
    img = comp(img, gold_paint(size, ImageChops.multiply(ring_out(hole, 4 * F), outer), rng, angle=-30, bands=2.0))
    hair = ring_in(shrink(outer, 9 * F), 1)
    img = comp(img, solid(size, GOLD_DEEP, scale_l(hair, 0.7)))

    # Little gold sprigs between the slots.
    for a in (-45, 45, 135, 225):
        ar = math.radians(a)
        sprig = Image.new("L", size, 0)
        stem = [(C[0] + math.cos(ar + t) * SLOT_POS_R * F, C[1] + math.sin(ar + t) * SLOT_POS_R * F) for t in [k / 20 * 0.36 - 0.18 for k in range(21)]]
        sprig = union(sprig, taper_mask(size, stem, 1.2 * F, 3.6 * F))
        d = ImageDraw.Draw(sprig)
        for side, t0 in ((1, -0.08), (-1, 0.06), (1, 0.12)):
            bx, by = C[0] + math.cos(ar + t0) * SLOT_POS_R * F, C[1] + math.sin(ar + t0) * SLOT_POS_R * F
            rad = (math.cos(ar + t0), math.sin(ar + t0))
            tan = (-rad[1], rad[0])
            tip = (bx + (rad[0] * side * 12 + tan[0] * 4) * F, by + (rad[1] * side * 12 + tan[1] * 4) * F)
            mid = ((bx + tip[0]) / 2, (by + tip[1]) / 2)
            nx, ny = tan
            d.polygon(curve((bx, by), (mid[0] + nx * 4 * F, mid[1] + ny * 4 * F), tip, 10) +
                      curve(tip, (mid[0] - nx * 4 * F, mid[1] - ny * 4 * F), (bx, by), 10)[1:], fill=255)
        berry = circle(size, (C[0] + math.cos(ar + 0.2) * SLOT_POS_R * F, C[1] + math.sin(ar + 0.2) * SLOT_POS_R * F), 3.2 * F)
        img = comp(img, solid(size, (0, 0, 0), scale_l(shift(sprig, 1 * F, 2 * F).filter(ImageFilter.GaussianBlur(1.5 * F)), 0.45)))
        img = comp(img, solid(size, INK, ring_out(sprig, 1.1 * F)), gold_paint(size, sprig, rng, angle=-30, bands=1.0))
        img = comp(img, solid(size, INK, ring_out(berry, 1.0 * F)), solid(size, CRIMSON, berry))
        img = comp(img, solid(size, (255, 210, 210), scale_l(circle(size, (C[0] + math.cos(ar + 0.2) * SLOT_POS_R * F - F, C[1] + math.sin(ar + 0.2) * SLOT_POS_R * F - F), 1.1 * F), 0.8)))

    # The pool at the centre: river teal with ripples and a gold spark, the place the rocks come home to.
    pool = shrink(hole, 1)
    img = comp(img, paint(size, radial_light(size, (C[0] - 16 * F, C[1] - 18 * F), 86 * F, power=0.9),
                          [(0, TEAL_DEEP), (0.45, TEAL), (0.85, LIGHT_TEAL), (1, (226, 246, 242))], pool))
    rip = Image.new("L", size, 0)
    rd = ImageDraw.Draw(rip)
    for rr, a0, a1 in ((22, 200, 330), (22, 20, 140), (38, 190, 300), (38, 10, 110), (50, 215, 280)):
        rd.arc((C[0] - rr * F, C[1] - rr * 0.82 * F, C[0] + rr * F, C[1] + rr * 0.82 * F), a0, a1, fill=200, width=int(2.4 * F))
    img = comp(img, clip(solid(size, (236, 250, 248), rip.filter(ImageFilter.GaussianBlur(0.6 * F))), pool))
    inner_sh = ImageChops.subtract(pool, shift(pool, 5 * F, 7 * F)).filter(ImageFilter.GaussianBlur(4 * F))
    img = comp(img, clip(solid(size, (14, 48, 60), scale_l(inner_sh, 0.8)), pool))
    sp = union(star(size, C, 15 * F, thin=0.16), star(size, (C[0] + 20 * F, C[1] - 18 * F), 5 * F))
    img = comp(img, glow(sp, GOLD, 3 * F, 1.4), solid(size, GOLD_CORE, sp))
    img = comp(img, solid(size, INK, ring_out(pool, 2 * F)))

    # The four slots, each with its rock's mark faintly carved in the well.
    for name, ang, body_rgb, shade_rgb, kind in RING_ROCKS:
        a = math.radians(ang)
        c = (C[0] + math.cos(a) * SLOT_POS_R * F, C[1] + math.sin(a) * SLOT_POS_R * F)
        well_img, well = slot_well(size, c, rng)
        img = comp(img, well_img)
        mk = ImageChops.multiply(mark_mask(size, kind, c[0], c[1], 0.52 * SLOT_WELL * F), shrink(well, 2 * F))
        tint = lerp_rgb(shade_rgb, (110, 80, 72), 0.35)
        img = comp(img, solid(size, (246, 230, 204), scale_l(ImageChops.subtract(shift(mk, 1.2 * F, 1.5 * F), mk), 0.55)),
                   solid(size, tint, scale_l(mk, 0.78)))
    img = comp(img, ink_edge(size, outer, 2.8 * F, heavy=1.6 * F))
    return downsample(soften_edges(img, 3 * F))


def slot_sprite(spec, rng):
    """One slot on its own: spec None is the empty cup; a rock spec lights it up in that rock's colour."""
    S = SLOT_CELL
    size = (S * F, S * F)
    c = (S * F / 2, S * F / 2)
    img = canvas(*size)
    disc = circle(size, c, SLOT_OUT * F)
    if spec is None:
        well_img, well = slot_well(size, c, rng)
        img = comp(img, well_img)
    else:
        name, _, body_rgb, shade_rgb, kind = spec
        img = comp(img, glow(disc, GOLD, 13 * F, 1.25), glow(disc, lerp_rgb(body_rgb, GOLD_CORE, 0.2), 7 * F, 1.5))
        well = circle(size, c, SLOT_WELL * F)
        img = comp(img, solid(size, (0, 0, 0), scale_l(shift(disc, 1 * F, 2 * F).filter(ImageFilter.GaussianBlur(2.5 * F)), 0.4)))
        img = comp(img, gold_paint(size, ImageChops.subtract(disc, well), rng,
                                   light=radial_light(size, (c[0] - 12 * F, c[1] - 14 * F), 80 * F, power=0.6), bands=1.0))
        img = comp(img, paint(size, radial_light(size, c, SLOT_WELL * F * 1.15, power=0.8),
                              [(0, lerp_rgb(shade_rgb, INK, 0.1)), (0.35, body_rgb), (0.7, lerp_rgb(body_rgb, GOLD_CORE, 0.55)), (1, GOLD_CORE)], well))
        rays = Image.new("L", size, 0)
        rd = ImageDraw.Draw(rays)
        for k in range(12):
            a = math.radians(k * 30 + 15)
            r0, r1 = SLOT_WELL * 0.3 * F, SLOT_WELL * (0.95 if k % 2 else 0.75) * F
            sp = 0.09
            rd.polygon([(c[0] + math.cos(a - sp) * r0, c[1] + math.sin(a - sp) * r0), (c[0] + math.cos(a) * r1, c[1] + math.sin(a) * r1),
                        (c[0] + math.cos(a + sp) * r0, c[1] + math.sin(a + sp) * r0)], fill=110)
        img = comp(img, clip(solid(size, GOLD_CORE, rays.filter(ImageFilter.GaussianBlur(F))), well))
        img = comp(img, solid(size, GOLD_CORE, scale_l(ring_mask(size, c, (SLOT_OUT - 2.2) * F, (SLOT_OUT - 0.8) * F), 0.85)))
        img = comp(img, solid(size, INK, scale_l(ring_in(well, 1.0 * F), 0.7)), solid(size, INK, ring_out(disc, 1.4 * F)))
        sp = Image.new("L", size, 0)
        for dx, dy, r in ((0.78, -0.8, 9), (-0.92, -0.42, 6), (0.86, 0.62, 5.5), (-0.6, 0.86, 4.5)):
            sp = ImageChops.lighter(sp, star(size, (c[0] + dx * SLOT_OUT * F, c[1] + dy * SLOT_OUT * F), r * F * rng.uniform(0.9, 1.1)))
        img = comp(img, glow(sp, GOLD, 2.5 * F, 1.5), solid(size, GOLD_CORE, sp))
    img.putalpha(ImageChops.multiply(img.getchannel("A"), edge_fade(size, 5 * F)))
    return downsample(img)


def ring_sheet(rng):
    W, H = RING + 2 * SLOT_CELL, 3 * SLOT_CELL
    sheet = canvas(W, H)
    sprites = []
    sheet.alpha_composite(rock_ring(rng), (0, 0))
    sprites.append(("ring", (0, 0, RING, RING)))
    h = SLOT_CELL // 2
    for name, ang, *_ in RING_ROCKS:
        a = math.radians(ang)
        cx, cy = round(RING_C + math.cos(a) * SLOT_POS_R), round(RING_C + math.sin(a) * SLOT_POS_R)
        sprites.append((f"slot-{name}", (cx - h, cy - h, cx + h, cy + h)))
    for i, spec in enumerate(RING_ROCKS):
        x, y = RING + (i % 2) * SLOT_CELL, (i // 2) * SLOT_CELL
        sheet.alpha_composite(slot_sprite(spec, rng), (x, y))
        sprites.append((f"slot-filled-{spec[0]}", (x, y, x + SLOT_CELL, y + SLOT_CELL)))
    x, y = RING, 2 * SLOT_CELL
    sheet.alpha_composite(slot_sprite(None, rng), (x, y))
    sprites.append(("slot-empty", (x, y, x + SLOT_CELL, y + SLOT_CELL)))
    return sheet, sprites


# --------------------------------------------------------------------------------------------- Sciel's Sun and Moon

GAUGE = (416, 136)
MEDAL = (62, 64)
SUN_STEPS = [(146 + 54 * i, 56) for i in range(5)]
SUN_GAUGE = (416, 168)  # taller than the Moon's, for a row of 40 px suit sockets under the steps
SUN_MEDAL = (62, 80)
SUIT_SOCKETS = [("fox", 200, 118), ("rabbit", 254, 118), ("mouse", 308, 118)]
MOON_STEPS = [(166 + 96 * i, 64) for i in range(3)]
STEP_CELL = (56, 72)
SUIT_CELL = 48
TWI = (192, 96)
TWI_SOCKETS = [("sun", 58, 48), ("moon", 134, 48)]
CHARGE_CELL = 72


def lozenge(c, hw, hh):
    return [(c[0], c[1] - hh), (c[0] + hw, c[1]), (c[0], c[1] + hh), (c[0] - hw, c[1])]


def droplet(size, c, s):
    """A water droplet pointing up (unit s, about 40 x 52 at s=1)."""
    cx, cy = c
    m = circle(size, (cx, cy + 8 * s), 17 * s)
    pts = curve((cx - 16.2 * s, cy + 3 * s), (cx - 8 * s, cy - 12 * s), (cx, cy - 26 * s), 16) + \
        curve((cx, cy - 26 * s), (cx + 8 * s, cy - 12 * s), (cx + 16.2 * s, cy + 3 * s), 16)[1:] + [(cx, cy + 10 * s)]
    return union(m, poly_mask(size, pts))


def sun_emblem(size, c, r, rng, lit=True):
    """A painted sun: wavy rays round a gold disc (r is the ray tips' radius)."""
    rays = Image.new("L", size, 0)
    d = ImageDraw.Draw(rays)
    for k in range(12):
        a = 2 * math.pi * k / 12 - math.pi / 2
        ln = r if k % 2 == 0 else r * 0.82
        sp = 0.17
        base = r * 0.5
        p0 = (c[0] + math.cos(a - sp) * base, c[1] + math.sin(a - sp) * base)
        p1 = (c[0] + math.cos(a + sp) * base, c[1] + math.sin(a + sp) * base)
        tip = (c[0] + math.cos(a + 0.06) * ln, c[1] + math.sin(a + 0.06) * ln)
        mid_o = (c[0] + math.cos(a + 0.14) * (base + ln) / 2, c[1] + math.sin(a + 0.14) * (base + ln) / 2)
        d.polygon(curve(p0, mid_o, tip, 8) + [p1], fill=255)
    disc = circle(size, c, r * 0.56)
    m = smooth(union(rays, disc), 0.4 * F)
    out = canvas(*size)
    out = comp(out, solid(size, INK, ring_out(m, 1.4 * F)))
    if lit:
        out = comp(out, gold_paint(size, rays, rng, light=radial_light(size, (c[0] - r * 0.3, c[1] - r * 0.35), r * 2.2), bands=1.0))
        out = comp(out, paint(size, radial_light(size, (c[0] - r * 0.18, c[1] - r * 0.22), r * 0.75, power=0.8),
                              [(0, GOLD_DEEP), (0.45, GOLD), (0.8, (252, 226, 160)), (1, GOLD_CORE)], disc))
        out = comp(out, solid(size, GOLD_DEEP, scale_l(ring_mask(size, c, r * 0.34, r * 0.34 + 1.2 * F), 0.8)))
    else:
        out = comp(out, solid(size, (52, 46, 56), rays), solid(size, (40, 36, 46), disc))
        out = comp(out, solid(size, (96, 84, 80), scale_l(ImageChops.subtract(shift(m, 1 * F, 1 * F), m), 0.7)))
    out = comp(out, solid(size, INK, scale_l(ring_out(disc, 0.8 * F), 0.8)))
    return out


def crescent_mask(size, c, r):
    return ImageChops.subtract(circle(size, c, r), circle(size, (c[0] + r * 0.46, c[1] - r * 0.26), r * 0.86))


def gauge_panel(size, box, rng, trim, wash):
    m = rough_rrect(size, box, 26 * F, rng, 0.8 * F)
    img = comp(canvas(*size), drop_shadow(m, 4 * F, 6 * F, 0.7))
    body = dark_body(size, rng, wash=wash)
    body.putalpha(scale_l(m, 245 / 255))
    img = comp(img, body)
    edge_dark = ring_in(m, 14 * F).filter(ImageFilter.GaussianBlur(8 * F))
    img = comp(img, clip(solid(size, (6, 5, 9), scale_l(edge_dark, 0.55)), m))
    border = ring_in(m, 3.4 * F)
    img = comp(img, gold_paint(size, border, rng, angle=-20, bands=2.4) if trim == "gold" else silver_paint(size, border, rng, angle=-20, bands=2.4))
    hair = ring_in(shrink(m, 9 * F), 1)
    img = comp(img, solid(size, GOLD if trim == "gold" else SILVER, scale_l(hair, 0.55)))
    img = comp(img, ink_edge(size, m, 2.2 * F, heavy=1.4 * F))
    return img


def medallion(size, c, rng, trim):
    r = 54 * F
    disc = rough_disc(size, c, r, rng, wobble=0.012)
    out = comp(canvas(*size), drop_shadow(disc, 3 * F, 5 * F, 0.7))
    if trim == "gold":
        stops = [(0, (20, 14, 18)), (0.6, (70, 30, 36)), (1, (120, 50, 50))]
    else:
        stops = [(0, (12, 14, 26)), (0.6, NIGHT), (1, (62, 70, 104))]
    out = comp(out, paint(size, radial_light(size, (c[0] - 16 * F, c[1] - 18 * F), 80 * F), stops, disc))
    rim = ring_in(disc, 5 * F)
    out = comp(out, gold_paint(size, rim, rng, angle=-35, bands=1.6) if trim == "gold" else silver_paint(size, rim, rng, angle=-35, bands=1.6))
    out = comp(out, solid(size, GOLD if trim == "gold" else SILVER, scale_l(ring_in(shrink(disc, 9 * F), 1), 0.5)))
    return out, disc


def step_socket(size, c, kind, rng):
    """An empty step in the frame: a dark recessed lozenge (sun) or droplet (moon) with a thin bezel."""
    if kind == "sun":
        m = smooth(poly_mask(size, lozenge(c, 17 * F, 24 * F)), 0.6 * F)
    else:
        m = smooth(droplet(size, c, F), 0.4 * F)
    bez = grow(m, 2.4 * F)
    out = canvas(*size)
    out = comp(out, gold_paint(size, bez, rng, angle=-35, bands=1.0) if kind == "sun" else silver_paint(size, bez, rng, angle=-35, bands=1.0))
    out = comp(out, paint(size, radial_light(size, (c[0] + 5 * F, c[1] + 7 * F), 30 * F), [(0, (8, 7, 12)), (1, (40, 36, 52) if kind == "sun" else (30, 40, 58))], m))
    out = comp(out, clip(solid(size, INK, scale_l(ImageChops.subtract(m, shift(m, 3 * F, 4 * F)).filter(ImageFilter.GaussianBlur(1.6 * F)), 0.9)), m))
    out = comp(out, solid(size, INK, ring_out(bez, 1.2 * F)), solid(size, INK, scale_l(ring_in(m, 0.8 * F), 0.8)))
    return out


def step_lit(kind, rng):
    W, H = STEP_CELL
    size = (W * F, H * F)
    c = (W * F / 2, H * F / 2)
    if kind == "sun":
        m = smooth(poly_mask(size, lozenge(c, 17 * F, 24 * F)), 0.6 * F)
        hue, deep, core = GOLD, GOLD_DEEP, GOLD_CORE
        stops = [(0, GOLD_DEEP), (0.4, GOLD), (0.75, (252, 226, 160)), (1, GOLD_CORE)]
    else:
        m = smooth(droplet(size, c, F), 0.4 * F)
        hue, deep, core = MOON_TEAL, TEAL_DEEP, SILVER_HI
        stops = [(0, TEAL_DEEP), (0.38, MOON_TEAL), (0.72, SILVER), (1, WHITE)]
    bez = grow(m, 2.4 * F)
    img = comp(canvas(*size), glow(bez, hue, 6 * F, 1.2), glow(bez, core, 2.5 * F, 0.9))
    img = comp(img, gold_paint(size, bez, rng, angle=-35, bands=1.0) if kind == "sun" else silver_paint(size, bez, rng, angle=-35, bands=1.0))
    lc = (c[0] - 4 * F, c[1] - 7 * F) if kind == "sun" else (c[0] - 4 * F, c[1] + 2 * F)
    img = comp(img, paint(size, radial_light(size, lc, 30 * F, power=0.75), stops, m))
    if kind == "sun":
        facet = poly_mask(size, [(c[0], c[1] - 24 * F), (c[0] - 17 * F, c[1]), (c[0], c[1])])
        img = comp(img, clip(solid(size, GOLD_CORE, scale_l(facet, 0.45)), m))
        img = comp(img, clip(solid(size, deep, scale_l(poly_mask(size, [(c[0], c[1] + 24 * F), (c[0] + 17 * F, c[1]), (c[0], c[1])]), 0.35)), m))
    else:
        hi = Image.new("L", size, 0)
        ImageDraw.Draw(hi).ellipse((c[0] - 9 * F, c[1] - 2 * F, c[0] - 3 * F, c[1] + 12 * F), fill=200)
        img = comp(img, clip(solid(size, WHITE, hi.filter(ImageFilter.GaussianBlur(1.4 * F))), m))
        rip = Image.new("L", size, 0)
        ImageDraw.Draw(rip).arc((c[0] - 9 * F, c[1] + 8 * F, c[0] + 9 * F, c[1] + 18 * F), 20, 160, fill=160, width=int(1.6 * F))
        img = comp(img, clip(solid(size, (226, 248, 248), rip), m))
    sp = star(size, (c[0] + 9 * F, c[1] - 14 * F), 6 * F)
    img = comp(img, glow(sp, hue, 1.5 * F, 1.2), solid(size, WHITE, sp))
    img = comp(img, solid(size, INK, ring_out(bez, 1.2 * F)), solid(size, INK, scale_l(ring_in(m, 0.8 * F), 0.6)))
    img.putalpha(ImageChops.multiply(img.getchannel("A"), edge_fade(size, 3 * F)))
    return downsample(img)


def step_empty(kind, rng):
    W, H = STEP_CELL
    size = (W * F, H * F)
    return downsample(step_socket(size, (W * F / 2, H * F / 2), kind, rng))


def suit_glyph(size, c, suit, u):
    """An original, plain suit glyph (unit u, inside a disc of radius about 16.5u), so suits read by shape as well as
    colour: fox = a head with tall pointed ears and a pointed chin, rabbit = a round head with two long upright ears,
    mouse = a narrow head with a pointed snout, wide-set ears and whiskers."""
    x, y = c
    m = Image.new("L", size, 0)
    d = ImageDraw.Draw(m)

    def ell(cx, cy, rx, ry, rot=0.0):
        a = math.radians(rot)
        return [(x + (cx + math.cos(t) * rx * math.cos(a) - math.sin(t) * ry * math.sin(a)) * u,
                 y + (cy + math.cos(t) * rx * math.sin(a) + math.sin(t) * ry * math.cos(a)) * u)
                for t in [k / 36 * 2 * math.pi for k in range(36)]]
    if suit == "fox":
        d.polygon([(x + px * u, y + py * u) for px, py in
                   ((-11, -12), (-4.5, -5.5), (4.5, -5.5), (11, -12), (10.5, -1), (0, 11.5), (-10.5, -1))], fill=255)
    elif suit == "rabbit":
        d.polygon(ell(0, 5, 7.5, 6.5), fill=255)
        d.polygon(ell(-3.8, -6, 2.8, 7.8, -12), fill=255)
        d.polygon(ell(3.8, -6, 2.8, 7.8, 12), fill=255)
    else:
        # A narrow head tapering to a pointed snout, ears set wide and low, and whiskers.
        d.polygon(ell(0, -1.5, 7.2, 5.6), fill=255)
        d.polygon([(x + px * u, y + py * u) for px, py in ((-7, -0.5), (7, -0.5), (3.2, 7), (0, 12), (-3.2, 7))], fill=255)
        d.polygon(ell(-8.6, -5.5, 4.4, 3.8, -25), fill=255)
        d.polygon(ell(8.6, -5.5, 4.4, 3.8, 25), fill=255)
        for sgn in (-1, 1):
            for wy in (4.5, 8.0):
                d.line([(x + sgn * 2.5 * u, y + 6.5 * u), (x + sgn * 11.5 * u, y + wy * u)], fill=255, width=max(1, int(0.8 * u)))
    return m.filter(ImageFilter.GaussianBlur(0.4 * u))


def suit_socket(size, c, rng, suit=None):
    m = circle(size, c, 16.5 * F)
    bez = grow(m, 2.5 * F)
    out = comp(canvas(*size), gold_paint(size, bez, rng, angle=-35, bands=1.0))
    out = comp(out, paint(size, radial_light(size, (c[0] + 3 * F, c[1] + 4 * F), 20 * F), [(0, (8, 7, 12)), (1, (44, 40, 54))], m))
    if suit:
        # The empty socket shows its suit faintly, so a missing suit is clear without colour.
        out = comp(out, solid(size, BONE, scale_l(suit_glyph(size, c, suit, F), 0.22)))
    return comp(out, solid(size, INK, ring_out(bez, 1.0 * F)))


def suit_gem(suit, rng):
    S = SUIT_CELL
    size = (S * F, S * F)
    c = (S * F / 2, S * F / 2)
    rgb = SUIT[suit]
    m = circle(size, c, 16.5 * F)
    bez = grow(m, 2.5 * F)
    img = comp(canvas(*size), glow(bez, rgb, 2.2 * F, 1.2))
    img = comp(img, gold_paint(size, bez, rng, angle=-35, bands=1.0))
    img = comp(img, paint(size, radial_light(size, (c[0] - 4 * F, c[1] - 5 * F), 22 * F, power=0.8),
                          [(0, lerp_rgb(rgb, INK, 0.45)), (0.55, rgb), (1, lerp_rgb(rgb, BONE, 0.6))], m))
    hi = Image.new("L", size, 0)
    r = 13.5 * F
    ImageDraw.Draw(hi).arc((c[0] - r, c[1] - r, c[0] + r, c[1] + r), 195, 250, fill=210, width=int(1.8 * F))
    img = comp(img, solid(size, WHITE, hi.filter(ImageFilter.GaussianBlur(0.6 * F))))
    glyph = suit_glyph(size, c, suit, F)
    img = comp(img, solid(size, BONE, scale_l(ring_out(glyph, 1.0 * F), 0.6)), solid(size, INK, scale_l(glyph, 0.92)))
    img = comp(img, solid(size, INK, ring_out(bez, 1.0 * F)))
    keep = circle(size, c, 22.5 * F).filter(ImageFilter.GaussianBlur(0.8 * F))
    img.putalpha(ImageChops.multiply(img.getchannel("A"), keep))
    return downsample(img)


def sun_frame(rng):
    W, H = SUN_GAUGE
    size = (W * F, H * F)

    def P(x, y):
        return (x * F, y * F)

    img = gauge_panel(size, (36 * F, 14 * F, 404 * F, 146 * F), rng, "gold", wash=(96, 30, 34))
    for sx, sy in SUN_STEPS:
        img = comp(img, step_socket(size, P(sx, sy), "sun", rng))
    # Short gold ticks between the steps, like rays joining them.
    for (x0, _), (x1, _) in zip(SUN_STEPS, SUN_STEPS[1:]):
        xm = (x0 + x1) / 2
        tick = taper_mask(size, [P(xm, 46), P(xm, 56), P(xm, 66)], 1.0 * F, 2.6 * F)
        img = comp(img, solid(size, GOLD, scale_l(tick, 0.55)))
    # Suit sockets, joined by a short rule.
    rule = line_mask(size, [P(170, 118), P(338, 118)], 1.4 * F)
    img = comp(img, solid(size, GOLD_DEEP, scale_l(rule, 0.8)))
    for suit, x, y in SUIT_SOCKETS:
        img = comp(img, suit_socket(size, P(x, y), rng, suit))
    med, disc = medallion(size, P(*SUN_MEDAL), rng, "gold")
    img = comp(img, med, sun_emblem(size, P(*SUN_MEDAL), 40 * F, rng))
    img = comp(img, ink_edge(size, disc, 2.4 * F, heavy=1.2 * F))
    return downsample(soften_edges(img, 3 * F))


def moon_frame(rng):
    W, H = GAUGE
    size = (W * F, H * F)

    def P(x, y):
        return (x * F, y * F)

    img = gauge_panel(size, (36 * F, 14 * F, 404 * F, 114 * F), rng, "silver", wash=(26, 52, 70))
    # Faint water lines along the bottom: the Aquafarms' channels.
    wv = Image.new("L", size, 0)
    wd = ImageDraw.Draw(wv)
    for yo, ph in ((99, 0.0), (105, 1.4)):
        wd.line([P(126 + i * 2.5, yo + 2.2 * math.sin(i * 0.25 + ph)) for i in range(105)], fill=255, width=int(1.6 * F), joint="curve")
    img = comp(img, solid(size, MOON_TEAL, scale_l(wv, 0.42)))
    for (x0, _), (x1, _) in zip(MOON_STEPS, MOON_STEPS[1:]):
        for k in range(3):
            dot = circle(size, P(x0 + (x1 - x0) * (k + 1) / 4, 64), 1.8 * F)
            img = comp(img, solid(size, SILVER, scale_l(dot, 0.6)))
    for sx, sy in MOON_STEPS:
        img = comp(img, step_socket(size, P(sx, sy), "moon", rng))
    med, disc = medallion(size, P(*MEDAL), rng, "silver")
    img = comp(img, med)
    mc = P(MEDAL[0] - 2, MEDAL[1] - 4)
    cres = smooth(crescent_mask(size, mc, 30 * F), 0.5 * F)
    img = comp(img, glow(cres, MOON_TEAL, 4 * F, 0.8), solid(size, INK, ring_out(cres, 1.4 * F)))
    img = comp(img, silver_paint(size, cres, rng, light=radial_light(size, (mc[0] - 14 * F, mc[1] - 6 * F), 50 * F), bands=1.0))
    sp = union(star(size, P(MEDAL[0] + 18, MEDAL[1] - 20), 5 * F), star(size, P(MEDAL[0] + 26, MEDAL[1] + 2), 3.5 * F),
               star(size, P(MEDAL[0] + 6, MEDAL[1] - 34), 3 * F))
    img = comp(img, solid(size, SILVER_HI, sp))
    rip = Image.new("L", size, 0)
    rd = ImageDraw.Draw(rip)
    for rr, yy in ((22, 98), (14, 103)):
        rd.arc((P(MEDAL[0] - rr, yy - 6)[0], P(0, yy - 6)[1], P(MEDAL[0] + rr, yy + 6)[0], P(0, yy + 6)[1]), 200, 340, fill=255, width=int(2 * F))
    img = comp(img, clip(solid(size, LIGHT_TEAL, scale_l(rip, 0.8)), disc))
    img = comp(img, ink_edge(size, disc, 2.4 * F, heavy=1.2 * F))
    return downsample(soften_edges(img, 3 * F))


def twilight_frame(rng):
    W, H = TWI
    size = (W * F, H * F)

    def P(x, y):
        return (x * F, y * F)

    m = rough_rrect(size, (6 * F, 12 * F, 186 * F, 84 * F), 30 * F, rng, 0.7 * F)
    img = comp(canvas(*size), drop_shadow(m, 3 * F, 5 * F, 0.7))
    hl = linear_light(size, 0)
    body = paint(size, hl, [(0, (96, 44, 46)), (0.45, (74, 42, 70)), (1, (30, 32, 62))], scale_l(m, 245 / 255))
    clouds = noise_l(size, rng, cell=14 * F, blur=4 * F).point(lambda v: int(v * 0.35))
    body = comp(body, clip(solid(size, (14, 10, 20), clouds), m))
    img = comp(img, body)
    border = ring_in(m, 3 * F)
    img = comp(img, Image.composite(silver_paint(size, border, rng, angle=-20, bands=2), gold_paint(size, border, rng, angle=-20, bands=2), hl))
    img = comp(img, ink_edge(size, m, 2 * F, heavy=1.2 * F))
    img = soften_edges(img, 2 * F)
    # A small horizon between the two sockets: half a sun sinking under a line.
    hc = P(96, 50)
    half = ImageChops.multiply(circle(size, hc, 8 * F), poly_mask(size, [(0, 0), (W * F, 0), (W * F, hc[1]), (0, hc[1])]))
    img = comp(img, glow(half, DUSK, 3 * F, 0.8), solid(size, INK, ring_out(half, 1 * F)), gold_paint(size, half, rng, angle=-30, bands=1.0))
    hz = line_mask(size, [P(84, 51.5), P(108, 51.5)], 1.6 * F)
    img = comp(img, solid(size, SILVER, hz))
    for kind, x, y in TWI_SOCKETS:
        c = P(x, y)
        well = circle(size, c, 21 * F)
        bez = ring_mask(size, c, 21 * F, 25 * F)
        img = comp(img, gold_paint(size, bez, rng, angle=-35, bands=1.0) if kind == "sun" else silver_paint(size, bez, rng, angle=-35, bands=1.0))
        img = comp(img, paint(size, radial_light(size, (c[0] + 5 * F, c[1] + 6 * F), 30 * F), [(0, (8, 7, 12)), (1, (40, 38, 56))], well))
        img = comp(img, solid(size, INK, ring_in(well, 1 * F)), solid(size, INK, ring_out(circle(size, c, 25 * F), 1.2 * F)))
    return downsample(img)


def charge_pip(kind, lit, rng):
    S = CHARGE_CELL
    size = (S * F, S * F)
    c = (S * F / 2, S * F / 2)
    r = 22 * F
    disc = circle(size, c, r)
    img = canvas(*size)
    hue = GOLD if kind == "sun" else MOON_TEAL
    if lit:
        img = comp(img, glow(disc, hue, 7 * F, 1.3), glow(disc, GOLD_CORE if kind == "sun" else SILVER_HI, 3 * F, 0.9))
    img = comp(img, solid(size, INK, disc))
    lens = circle(size, c, r - 2.5 * F)
    if kind == "sun":
        if lit:
            img = comp(img, paint(size, radial_light(size, c, r, power=0.7), [(0, (150, 52, 40)), (0.5, DUSK), (1, (250, 176, 120))], lens))
            img = comp(img, sun_emblem(size, c, 17 * F, rng, lit=True))
        else:
            img = comp(img, paint(size, radial_light(size, (c[0] + 4 * F, c[1] + 5 * F), 28 * F), [(0, (10, 8, 12)), (1, (40, 34, 42))], lens))
            img = comp(img, sun_emblem(size, c, 17 * F, rng, lit=False))
    else:
        cc = (c[0] - 1.5 * F, c[1])
        cres = smooth(crescent_mask(size, cc, 14 * F), 0.4 * F)
        if lit:
            img = comp(img, paint(size, radial_light(size, c, r, power=0.7), [(0, (14, 22, 46)), (0.6, NIGHT), (1, (72, 92, 130))], lens))
            img = comp(img, glow(cres, MOON_TEAL, 3 * F, 1.2), solid(size, INK, ring_out(cres, 1.2 * F)))
            img = comp(img, silver_paint(size, cres, rng, light=radial_light(size, (cc[0] - 8 * F, cc[1] - 4 * F), 26 * F), bands=1.0))
            sp = union(star(size, (c[0] + 9 * F, c[1] - 8 * F), 4 * F), star(size, (c[0] + 11 * F, c[1] + 7 * F), 2.6 * F))
            img = comp(img, solid(size, SILVER_HI, sp))
        else:
            img = comp(img, paint(size, radial_light(size, (c[0] + 4 * F, c[1] + 5 * F), 28 * F), [(0, (8, 9, 14)), (1, (34, 38, 50))], lens))
            img = comp(img, solid(size, (82, 92, 104), scale_l(ImageChops.subtract(shift(cres, 1 * F, 1 * F), cres), 0.7)),
                       solid(size, (36, 40, 52), cres))
    rim = ring_mask(size, c, r - 2.5 * F, r)
    if lit:
        img = comp(img, gold_paint(size, rim, rng, angle=-35, bands=1.0) if kind == "sun" else silver_paint(size, rim, rng, angle=-35, bands=1.0))
    else:
        img = comp(img, solid(size, GOLD_DEEP if kind == "sun" else SILVER_DEEP, scale_l(rim, 0.75)))
    if lit:
        hi = Image.new("L", size, 0)
        ImageDraw.Draw(hi).arc((c[0] - r * 0.8, c[1] - r * 0.8, c[0] + r * 0.8, c[1] + r * 0.8), 200, 255, fill=170, width=int(2 * F))
        img = comp(img, solid(size, WHITE, hi.filter(ImageFilter.GaussianBlur(0.6 * F))))
    img = comp(img, solid(size, INK, ring_out(disc, 1.2 * F)))
    img.putalpha(ImageChops.multiply(img.getchannel("A"), edge_fade(size, 5 * F)))
    return downsample(img)


def twilight_armed(rng):
    """A halo laid over the Twilight frame when both charges are held: dusk rings round each pip and an arc joining them."""
    W, H = TWI
    size = (W * F, H * F)

    def P(x, y):
        return (x * F, y * F)

    img = canvas(*size)
    (_, sx, sy), (_, mx, my) = TWI_SOCKETS
    arc_pts = curve(P(sx + 8, sy - 24), P(96, 2), P(mx - 8, my - 24), 40)
    arc = taper_mask(size, arc_pts[:21], 1.0 * F, 3.4 * F)
    arc = union(arc, taper_mask(size, arc_pts[20:], 3.4 * F, 1.0 * F))
    arc2 = taper_mask(size, curve(P(sx + 8, sy + 24), P(96, 98), P(mx - 8, my + 24), 30), 2.0 * F, 2.0 * F)
    for kind, x, y in TWI_SOCKETS:
        ring = ring_mask(size, P(x, y), 25.5 * F, 29 * F)
        col = DUSK if kind == "sun" else MOON_TEAL
        img = comp(img, glow(ring, col, 5 * F, 1.5), glow(ring, CRIMSON if kind == "sun" else TEAL, 9 * F, 0.6))
        img = comp(img, solid(size, GOLD_CORE if kind == "sun" else SILVER_HI, ring))
    hl = linear_light(size, 0)
    tw = Image.composite(Image.new("RGBA", size, MOON_TEAL + (255,)), Image.new("RGBA", size, DUSK + (255,)), hl)
    both = union(arc, scale_l(arc2, 0.55))
    img = comp(img, glow(both, (236, 150, 140), 4 * F, 1.2), clip(tw, both))
    img = comp(img, solid(size, WHITE, scale_l(shrink(arc, 1), 0.7)))
    sp = Image.new("L", size, 0)
    for x, y, r in ((96, 13, 6), (66, 18, 3.5), (128, 16, 3.5), (96, 90, 4), (20, 26, 4), (172, 70, 4)):
        sp = ImageChops.lighter(sp, star(size, P(x, y), r * F))
    img = comp(img, glow(sp, GOLD, 2 * F, 1.4), solid(size, WHITE, sp))
    img.putalpha(ImageChops.multiply(img.getchannel("A"), edge_fade(size, 3 * F)))
    return downsample(img)


def sun_moon_sheet(rng):
    W, H = 512, 496
    MOON_Y, TWI_Y, CHARGE_Y = SUN_GAUGE[1] + 4, SUN_GAUGE[1] + 4 + GAUGE[1] + 4, SUN_GAUGE[1] + 4 + GAUGE[1] + 4 + 108
    sheet = canvas(W, H)
    sprites = []

    def put(name, img, x, y):
        sheet.alpha_composite(img, (x, y))
        sprites.append((name, (x, y, x + img.width, y + img.height)))

    def anchor(name, ox, oy, cx, cy, w, h):
        sprites.append((name, (ox + cx - w // 2, oy + cy - h // 2, ox + cx + w // 2, oy + cy + h // 2)))

    put("sun-frame", sun_frame(rng), 0, 0)
    for i, (x, y) in enumerate(SUN_STEPS):
        anchor(f"sun-slot-{i + 1}", 0, 0, x, y, *STEP_CELL)
    for suit, x, y in SUIT_SOCKETS:
        anchor(f"suit-slot-{suit}", 0, 0, x, y, SUIT_CELL, SUIT_CELL)
    put("moon-frame", moon_frame(rng), 0, MOON_Y)
    for i, (x, y) in enumerate(MOON_STEPS):
        anchor(f"moon-slot-{i + 1}", 0, MOON_Y, x, y, *STEP_CELL)
    put("sun-step-empty", step_empty("sun", rng), 424, 0)
    put("sun-step-lit", step_lit("sun", rng), 424, 76)
    put("moon-step-empty", step_empty("moon", rng), 424, 152)
    put("moon-step-lit", step_lit("moon", rng), 424, 228)
    for i, suit in enumerate(("fox", "rabbit", "mouse")):
        put(f"suit-{suit}", suit_gem(suit, rng), 432, TWI_Y + i * (SUIT_CELL + 4))
    put("twilight-frame", twilight_frame(rng), 0, TWI_Y)
    for kind, x, y in TWI_SOCKETS:
        anchor(f"charge-slot-{kind}", 0, TWI_Y, x, y, CHARGE_CELL, CHARGE_CELL)
    put("twilight-armed", twilight_armed(rng), 200, TWI_Y)
    for i, (kind, lit) in enumerate((("sun", False), ("sun", True), ("moon", False), ("moon", True))):
        put(f"charge-{kind}-{'lit' if lit else 'off'}", charge_pip(kind, lit, rng), i * (CHARGE_CELL + 6), CHARGE_Y)
    return sheet, sprites


# -------------------------------------------------------------------------------------------------------------- make

def make(out_dir, fonts):
    out = []

    def emit(asset, rel, built):
        img, sprites = built
        save(img, out_dir, rel)
        out.append({"asset": asset, "path": f"art/{rel}", "size": [img.width, img.height],
                    "sprites": [{"name": n, "box": [int(v) for v in b]} for n, b in sprites]})

    emit("asset.arm-gauge", "gauges/gustaves-arm.png", arm_sheet(random.Random(SEED + 1)))
    emit("asset.rock-ring", "gauges/rock-ring.png", ring_sheet(random.Random(SEED + 2)))
    emit("asset.sun-moon-gauge", "gauges/sun-moon.png", sun_moon_sheet(random.Random(SEED + 3)))
    return out

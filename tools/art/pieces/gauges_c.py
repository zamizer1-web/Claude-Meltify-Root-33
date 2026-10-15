"""Gauges for Root 33, set C: Lune's thesis tracker and Stains, and Monoco's five-slot Bestial Wheel.

- art/gauges/thesis.png: a dark gilt strip holding four specimen plates (Journal, Route Chart, Chroma Sample and the
  larger gold Parents' Journal plate), plus each plate on its own, empty (a dashed ghost of the relic) and filled.
- art/gauges/stains.png: Fire, Ice and Lightning paint daubs with element marks, lit and dim, and the pre-battle
  toggle, off and armed.
- art/gauges/bestial-wheel.png: the wheel with five empty slots (clockwise from the top: Caster, Agile, Balanced,
  Heavy, Almighty) and a faction-badge socket beneath each slot toward the hub, plus every slot empty, filled and
  (Almighty) locked, the socket empty and lit, and a pointer.

Sprites named slot-<name> and badge-<name> are placement boxes inside the tracker or wheel sprite: paste the matching
128 px tile (or 64 px socket) there to change a slot's state.

Everything is original and drawn from scratch with Pillow in the shared painterly style (tools/art/style.py): ink
outlines, bone paper, gold leaf, crimson and river teal. The slot marks are invented generic glyphs (sparkle, feather,
scales, weight, sun). Every random choice comes from a seeded random.Random (noise included), so the same code always
paints the same pixels. Pieces are drawn at 4x and reduced, so edges stay soft at in-game size.
"""
import math
import random
import sys
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageOps

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from style import (BONE, CRIMSON, CRIMSON_DEEP, DUSK, GOLD, GOLD_CORE, GOLD_DEEP, INK, INK_SOFT,  # noqa: E402
                   TEAL, TEAL_DEEP, brush_stroke, canvas, curve, glow, solid)

SEED = 3373
F = 4
TS = 128
LIGHT_TEAL = (176, 226, 222)
FROST = (238, 250, 248)
VIOLET = (98, 66, 150)
VIOLET_DEEP = (48, 32, 84)
LILAC = (176, 152, 220)
EMBER = (230, 108, 44)
AMBER = (242, 168, 72)
IRON = (124, 120, 134)
IRON_LIGHT = (214, 210, 222)
WOOD = (128, 88, 56)
WOOD_DEEP = (74, 50, 36)
LEATHER = (32, 82, 94)


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
    w, h = size
    d = int(math.hypot(w, h)) + 4
    g = Image.linear_gradient("L").resize((d, d)).rotate(angle, resample=Image.BICUBIC)
    return g.crop(((d - w) // 2, (d - h) // 2, (d - w) // 2 + w, (d - h) // 2 + h))


def band_light(size, y0, y1, horizontal=False):
    """L ramp: 0 before y0, 255 after y1 (rows, or columns when horizontal)."""
    w, h = size
    n = (w if horizontal else h)
    vals = [int(255 * min(1, max(0, (i - y0) / ((y1 - y0) or 1)))) for i in range(n)]
    line = Image.new("L", (n, 1) if horizontal else (1, n))
    line.putdata(vals)
    return line.resize((w, h))


def radial_light(size, center, radius, power=1.0, steps=48):
    """L image: 255 at `center`, falling to 0 at `radius`."""
    m = Image.new("L", size, 0)
    d = ImageDraw.Draw(m)
    cx, cy = center
    for k in range(steps):
        r = radius * (1 - k / steps)
        d.ellipse((cx - r, cy - r, cx + r, cy + r), fill=int(255 * ((k + 1) / steps) ** power))
    return m.filter(ImageFilter.GaussianBlur(max(1.0, radius / steps * 1.5)))


def conic_light(size, center, fn, step=3):
    m = Image.new("L", size, 0)
    d = ImageDraw.Draw(m)
    r = math.hypot(*size) + 4
    cx, cy = center
    for a in range(0, 360, step):
        d.pieslice((cx - r, cy - r, cx + r, cy + r), a, a + step + 0.6, fill=max(0, min(255, int(fn(a + step / 2)))))
    return m.filter(ImageFilter.GaussianBlur(2))


def top_left_light(size, c):
    return conic_light(size, c, lambda deg: 128 + 127 * math.cos(math.radians(deg - 225)))


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


def streaks(size, rng, angle=0, cell=40):
    """Brush-streak noise running along `angle` degrees."""
    w, h = size
    d = int(math.hypot(w, h)) + 4
    n = noise_l((max(1, d // cell), d), rng, blur=0).resize((d, d), Image.BICUBIC)
    if angle:
        n = n.rotate(angle, resample=Image.BICUBIC)
    return n.crop(((d - w) // 2, (d - h) // 2, (d - w) // 2 + w, (d - h) // 2 + h))


def paint_fill(size, mask, rng, stops, light, depth=0.2, angle=-20):
    """Painted colour: a ramp through `light`, with bristle streaks and grain."""
    base = map_l(light, stops)
    grain = noise_l(size, rng, cell=2, blur=1.0)
    shade = ImageChops.add(grain, streaks(size, rng, angle), 2.0, 0)
    mult = shade.point(lambda v: int(255 * (1 - depth) + depth * v))
    out = ImageChops.multiply(base, Image.merge("RGB", [mult] * 3)).convert("RGBA")
    out.putalpha(mask)
    return out


def rough_disc(size, c, r, rng, wobble=0.035, jitter=0.004, steps=220, sy=1.0):
    harm = [(k, rng.uniform(0, 2 * math.pi), rng.uniform(0.35, 1.0) / k) for k in range(2, 8)]
    pts = []
    for i in range(steps):
        a = 2 * math.pi * i / steps
        f = 1 + wobble * sum(amp * math.sin(k * a + ph) for k, ph, amp in harm) + rng.uniform(-jitter, jitter)
        pts.append((c[0] + math.cos(a) * r * f, c[1] + math.sin(a) * r * f * sy))
    m = Image.new("L", size, 0)
    ImageDraw.Draw(m).polygon(pts, fill=255)
    return m


def disc(size, c, r, fill=255):
    m = Image.new("L", size, 0)
    ImageDraw.Draw(m).ellipse((c[0] - r, c[1] - r, c[0] + r, c[1] + r), fill=fill)
    return m


def ring_mask(size, c, r_in, r_out):
    m = Image.new("L", size, 0)
    d = ImageDraw.Draw(m)
    d.ellipse((c[0] - r_out, c[1] - r_out, c[0] + r_out, c[1] + r_out), fill=255)
    if r_in > 0:
        d.ellipse((c[0] - r_in, c[1] - r_in, c[0] + r_in, c[1] + r_in), fill=0)
    return m


def rr(size, p0, p1, r, fill=255):
    m = Image.new("L", size, 0)
    ImageDraw.Draw(m).rounded_rectangle((p0[0], p0[1], p1[0], p1[1]), radius=r, fill=fill)
    return m


def grow(mask, px):
    if px < 1:
        return mask
    if px <= 4:
        return mask.filter(ImageFilter.MaxFilter(2 * int(px) + 1))
    return mask.filter(ImageFilter.GaussianBlur(px / 1.5)).point(lambda v: 255 if v > 17 else 0)


def shrink(mask, px):
    if px < 1:
        return mask
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


def inter(a, b):
    return ImageChops.multiply(a, b)


def cut(a, b):
    return ImageChops.subtract(a, b)


def shift(mask, dx, dy):
    """Move a mask without wrapping."""
    out = Image.new("L", mask.size, 0)
    out.paste(mask, (int(dx), int(dy)))
    return out


def blur(mask, r):
    return mask.filter(ImageFilter.GaussianBlur(r))


def star(size, c, r, alpha=255, thin=0.18, rot=0.0):
    x, y = c
    t = r * thin
    pts = [(-r, 0), (-t, -t), (0, -r), (t, -t), (r, 0), (t, t), (0, r), (-t, t)]
    ca, sa = math.cos(math.radians(rot)), math.sin(math.radians(rot))
    return poly_mask(size, [(x + px * ca - py * sa, y + px * sa + py * ca) for px, py in pts], alpha)


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


def taper_mask(size, pts, widths, fill=255):
    """A line whose width changes point by point (round joints)."""
    m = Image.new("L", size, 0)
    d = ImageDraw.Draw(m)
    for (a, b), w in zip(zip(pts, pts[1:]), widths):
        d.line([a, b], fill=fill, width=max(1, int(round(w))))
        r = w / 2
        d.ellipse((b[0] - r, b[1] - r, b[0] + r, b[1] + r), fill=fill)
    return m


def teardrop(size, c, w, h, angle=0.0, m=1.2, steps=72):
    """A drop with its point at the top (before rotating by `angle` degrees, clockwise)."""
    ca, sa = math.cos(math.radians(angle)), math.sin(math.radians(angle))
    pts = []
    for i in range(steps):
        t = 2 * math.pi * i / steps
        x = w * math.sin(t) * abs(math.sin(t / 2)) ** m
        y = -h * math.cos(t)
        pts.append((c[0] + x * ca - y * sa, c[1] + x * sa + y * ca))
    return poly_mask(size, pts)


def brushy(mask, rng, amp=0.5, cell=20, soft=12):
    """Roughen a mask's edge like a brush-cut shape."""
    n = noise_l(mask.size, rng, cell=cell, blur=cell * 0.5)
    s = blur(mask, soft)
    n = n.point(lambda v: int(128 + (v - 128) * amp))
    return ImageChops.add(s, n, 1.0, -128).point(lambda v: 255 if v > 127 else 0)


def dashes(size, period, angle=45):
    w, h = size
    d = int(math.hypot(w, h)) + 4
    m = Image.new("L", (d, d), 0)
    dr = ImageDraw.Draw(m)
    for x in range(0, d, period):
        dr.rectangle((x, 0, x + period // 2, d), fill=255)
    m = m.rotate(angle)
    return m.crop(((d - w) // 2, (d - h) // 2, (d - w) // 2 + w, (d - h) // 2 + h))


def desaturate(img, amount, dim=1.0, tint=None):
    a = img.getchannel("A")
    rgb = img.convert("RGB")
    grey = ImageOps.grayscale(rgb).convert("RGB")
    if tint:
        grey = ImageChops.multiply(grey, Image.new("RGB", img.size, tint))
    out = Image.blend(rgb, grey, amount)
    if dim != 1.0:
        out = out.point(lambda v: int(v * dim))
    out = out.convert("RGBA")
    out.putalpha(a)
    return out


def ink_edge(size, mask, px, rgb=INK, heavy=0.0):
    """An ink outline around a mask, optionally weighted toward the bottom."""
    g = grow(mask, px)
    if heavy:
        g = union(g, grow(shift(mask, 0, heavy), px))
    return solid(size, rgb, ImageChops.subtract(g, mask))


def drop_shadow(size, mask, dy, r, a=0.45):
    return solid(size, (0, 0, 0), scale_l(blur(shift(mask, 0, dy), r), a))


def inner_shadow(size, mask, dx, dy, r, a=0.5, rgb=INK):
    """Shade just inside a mask's top-left edge, as if it were sunk."""
    edge = blur(cut(mask, shift(mask, dx, dy)), r)
    return clip(solid(size, rgb, scale_l(edge, a)), mask)


def sparkles(size, pts):
    m = Image.new("L", size, 0)
    for x, y, r in pts:
        m = ImageChops.lighter(m, star(size, (x, y), r))
    return m


def rotate_layer(img, deg, c):
    return img.rotate(-deg, resample=Image.BICUBIC, center=c)


def scale_layer(img, s, c):
    return img.transform(img.size, Image.AFFINE, (1 / s, 0, c[0] - c[0] / s, 0, 1 / s, c[1] - c[1] / s), resample=Image.BICUBIC)


def downsample(img, f=F):
    return img.resize((img.width // f, img.height // f), Image.LANCZOS)


def save(img, out_dir, rel):
    p = Path(out_dir) / rel
    p.parent.mkdir(parents=True, exist_ok=True)
    img.save(p, optimize=True)
    return p


def tile_space(S=TS):
    size = (S * F, S * F)
    C = (S * F / 2, S * F / 2)

    def P(x, y):
        return (C[0] + x * F, C[1] + y * F)
    return size, C, P


# ------------------------------------------------------------------------------------------------- thesis: relics

def item_journal(size, C, P, rng):
    """Lune's field journal: a teal leather notebook with a crimson strap and ribbon."""
    pages = rr(size, P(-19, -29), P(28, 33), 3 * F)
    cover = rr(size, P(-25, -34), P(23, 29), 4 * F)
    spine = inter(cover, rr(size, P(-26, -35), P(-16, 30), 0))
    strap = inter(cover, rr(size, P(12, -35), P(17, 30), 0))
    ribbon = poly_mask(size, [P(-9, 26), P(-2, 26), P(-2, 44), P(-5.5, 39.5), P(-9, 44)])
    sil = union(pages, cover, ribbon)
    img = canvas(*size)
    img = comp(img, paper_fill(size, pages, rng, base=(240, 230, 208), amount=0.25))
    lines = Image.new("L", size, 0)
    d = ImageDraw.Draw(lines)
    for k in range(4):
        x = 24.2 + k * 1.1
        d.line([P(x, -27), P(x, 31)], fill=150, width=max(1, int(0.45 * F)))
        y = 30.2 + k * 0.9
        d.line([P(-17, y), P(26.5, y)], fill=150, width=max(1, int(0.45 * F)))
    img = comp(img, solid(size, (176, 156, 120), inter(cut(lines, cover), pages)))
    img = comp(img, solid(size, (120, 100, 70), scale_l(inter(ring_out(cover, 1.5 * F), pages), 0.6)))
    leather = paint_fill(size, cover, rng, [(0, (16, 44, 54)), (0.55, LEATHER), (1, (74, 134, 140))],
                         linear_light(size, 35), depth=0.28, angle=-75)
    img = comp(img, leather, solid(size, INK, scale_l(spine, 0.42)))
    bands = union(line_mask(size, [P(-25, -26), P(-16, -26)], 2.2 * F, caps=False),
                  line_mask(size, [P(-25, 21), P(-16, 21)], 2.2 * F, caps=False))
    img = comp(img, gold_paint(size, inter(bands, cover), rng))
    em_c = P(-1, -4)
    emblem = union(star(size, em_c, 9 * F, thin=0.2), ring_mask(size, em_c, 12.2 * F, 13.8 * F))
    img = comp(img, solid(size, (8, 26, 32), scale_l(shift(emblem, 0.6 * F, 0.8 * F), 0.6)), gold_paint(size, emblem, rng, angle=60))
    img = comp(img, paint_fill(size, strap, rng, [(0, CRIMSON_DEEP), (1, CRIMSON)], linear_light(size, 0), depth=0.2),
               solid(size, INK, scale_l(ring_in(strap, 0.8 * F), 0.7)))
    img = comp(img, paint_fill(size, ribbon, rng, [(0, CRIMSON_DEEP), (1, CRIMSON)], linear_light(size, 90), depth=0.15))
    catch = inter(cut(cover, shift(cover, 1.2 * F, 1.6 * F)), cover)
    img = comp(img, solid(size, (190, 230, 226), scale_l(catch, 0.55)))
    img = comp(img, solid(size, INK, scale_l(ring_out(spine, 0.8 * F), 0.5)))
    img = comp(img, ink_edge(size, sil, 2 * F))
    return rotate_layer(img, -7, C), rotate_layer(sil, -7, C)


def item_chart(size, C, P, rng):
    """A folded route chart: river, hills, a dotted crimson route ending at a circled cross."""
    xs = [-35, -12, 11, 34]
    tops = [-23, -28, -23, -28]
    bots = [26, 22, 27, 22]
    panels = [poly_mask(size, [P(xs[k], tops[k]), P(xs[k + 1], tops[k + 1]), P(xs[k + 1], bots[k + 1]), P(xs[k], bots[k])])
              for k in range(3)]
    sil = union(*panels)
    img = canvas(*size)
    img = comp(img, paper_fill(size, sil, rng, base=(238, 224, 192), amount=0.3))
    img = comp(img, solid(size, (120, 92, 60), scale_l(panels[1], 0.2)), solid(size, (255, 250, 236), scale_l(panels[2], 0.18)))
    stain = blur(rough_disc(size, P(-20, 14), 9 * F, rng, wobble=0.2), 2 * F)
    img = comp(img, clip(solid(size, (176, 140, 92), scale_l(stain, 0.18)), sil))
    river = brush_stroke(size, curve(P(-36, -10), P(-6, 14), P(36, 8), 30), 4.2 * F, TEAL, 235, seed=SEED + 21, bristles=5)
    img = comp(img, clip(river, sil))
    hills = Image.new("L", size, 0)
    for hx, hy in ((-27, 2), (-20, 0), (20, 22)):
        hills = union(hills, line_mask(size, [P(hx - 4, hy + 3), P(hx, hy - 2), P(hx + 4, hy + 3)], 1.5 * F))
    img = comp(img, solid(size, INK_SOFT, scale_l(hills, 0.85)))
    route = curve(P(-25, 19), P(-4, -26), P(20, -11), 60)
    dots = Image.new("L", size, 0)
    dd = ImageDraw.Draw(dots)
    acc, last = 0.0, route[0]
    for p in route[:-6]:
        acc += math.hypot(p[0] - last[0], p[1] - last[1])
        last = p
        if acc >= 5.5 * F:
            acc = 0.0
            dd.ellipse((p[0] - 1.6 * F, p[1] - 1.6 * F, p[0] + 1.6 * F, p[1] + 1.6 * F), fill=255)
    dd.ellipse((P(-25, 19)[0] - 2.4 * F, P(-25, 19)[1] - 2.4 * F, P(-25, 19)[0] + 2.4 * F, P(-25, 19)[1] + 2.4 * F), fill=255)
    img = comp(img, solid(size, CRIMSON, dots))
    xm = union(line_mask(size, [P(18, -16), P(26, -8)], 2.4 * F), line_mask(size, [P(26, -16), P(18, -8)], 2.4 * F))
    img = comp(img, solid(size, CRIMSON_DEEP, xm), solid(size, INK, scale_l(ring_mask(size, P(22, -12), 7.6 * F, 9 * F), 0.85)))
    img = comp(img, solid(size, INK, star(size, P(-26, -15), 5.5 * F, thin=0.22)))
    folds = union(line_mask(size, [P(-12, -28), P(-12, 22)], 0.9 * F, caps=False), line_mask(size, [P(11, -23), P(11, 27)], 0.9 * F, caps=False))
    img = comp(img, solid(size, (90, 70, 50), scale_l(inter(folds, sil), 0.6)))
    img = comp(img, ink_edge(size, sil, 2 * F))
    return rotate_layer(img, 5, C), rotate_layer(sil, 5, C)


def item_vial(size, C, P, rng):
    """A stoppered phial of swirling colour: crimson, gold and teal."""
    body = rr(size, P(-12, -16), P(12, 32), 11.5 * F)
    neck = rr(size, P(-6, -26), P(6, -12), 1 * F)
    lip = rr(size, P(-9, -29), P(9, -24), 2 * F)
    cork = rr(size, P(-6, -39), P(6, -27), 2.4 * F)
    glass = union(body, neck, lip)
    sil = union(glass, cork)
    surf = Image.new("L", size, 0)
    ImageDraw.Draw(surf).ellipse((*P(-14, -6), *P(14, 0)), fill=255)
    below = union(surf, rr(size, P(-14, -3), P(14, 40), 0))
    liquid = inter(shrink(body, 1.2 * F), below)
    img = canvas(*size)
    img = comp(img, solid(size, LIGHT_TEAL, scale_l(glass, 0.32)))
    hue = band_light(size, P(0, -4)[1], P(0, 31)[1])
    img = comp(img, paint_fill(size, liquid, rng, [(0, TEAL), (0.35, GOLD), (0.62, DUSK), (1, CRIMSON)], hue, depth=0.12, angle=60))
    swirl = canvas(*size)
    swirl = comp(swirl, brush_stroke(size, curve(P(-9, 26), P(-14, 6), P(4, 4), 24) + curve(P(4, 4), P(14, 0), P(4, -3), 10),
                                     2.4 * F, GOLD_CORE, 170, seed=SEED + 31, bristles=4),
                 brush_stroke(size, curve(P(8, 28), P(12, 14), P(-4, 14), 24), 2.4 * F, CRIMSON_DEEP, 120, seed=SEED + 32, bristles=4))
    img = comp(img, clip(swirl, liquid))
    img = comp(img, solid(size, LIGHT_TEAL, scale_l(inter(cut(below, shift(below, 0, 1.4 * F)), shrink(body, 1.2 * F)), 0.9)))
    img = comp(img, gold_paint(size, lip, rng, angle=80))
    img = comp(img, paint_fill(size, cork, rng, [(0, WOOD_DEEP), (0.6, WOOD), (1, (184, 136, 92))], linear_light(size, 70), depth=0.3, angle=0))
    shine = union(line_mask(size, [P(-7, -10), P(-7.5, 24)], 2.6 * F), disc(size, P(6, 25), 1.5 * F))
    img = comp(img, solid(size, (255, 255, 250), scale_l(inter(shine, body), 0.75)))
    img = comp(img, solid(size, INK, scale_l(union(ring_out(lip, 0.8 * F), ring_out(cork, 0.8 * F)), 0.8)))
    img = comp(img, ink_edge(size, sil, 2 * F))
    return scale_layer(rotate_layer(img, 18, C), 1.16, C), scale_layer(rotate_layer(sil, 18, C), 1.16, C)


def item_parents(size, C, P, rng):
    """The Parents' Journal: a crimson tome bound in gold, with a medallion, clasp and teal ribbon."""
    pages = rr(size, P(-26, -33), P(35, 42), 3 * F)
    cover = rr(size, P(-33, -40), P(29, 36), 4.5 * F)
    clasp = rr(size, P(22, -7), P(38, 7), 2.5 * F)
    ribbon = poly_mask(size, [P(-13, 33), P(-5, 33), P(-5, 50), P(-9, 45), P(-13, 50)])
    sil = union(pages, cover, clasp, ribbon)
    img = canvas(*size)
    img = comp(img, paper_fill(size, pages, rng, base=(244, 232, 206), amount=0.25))
    lines = Image.new("L", size, 0)
    d = ImageDraw.Draw(lines)
    for k in range(5):
        x = 30.2 + k * 1.0
        d.line([P(x, -31), P(x, 40)], fill=150, width=max(1, int(0.45 * F)))
        y = 37.2 + k * 0.9
        d.line([P(-24, y), P(33.5, y)], fill=150, width=max(1, int(0.45 * F)))
    img = comp(img, solid(size, (176, 150, 110), inter(cut(lines, cover), pages)))
    img = comp(img, paint_fill(size, cover, rng, [(0, (64, 6, 20)), (0.55, CRIMSON_DEEP), (1, (176, 40, 56))],
                               linear_light(size, 35), depth=0.26, angle=-75))
    spine = inter(cover, rr(size, P(-34, -41), P(-23, 37), 0))
    img = comp(img, solid(size, INK, scale_l(spine, 0.35)))
    gold = union(ring_in(shrink(cover, 4 * F), 1.8 * F),
                 *[line_mask(size, [P(-33, y), P(-23, y)], 1.8 * F, caps=False) for y in (-31, -13, 9, 27)])
    for sx, sy in ((-1, -1), (1, -1), (-1, 1), (1, 1)):
        x0 = -33 if sx < 0 else 29
        y0 = -40 if sy < 0 else 36
        gold = union(gold, inter(poly_mask(size, [P(x0, y0), P(x0 - sx * 13, y0), P(x0, y0 - sy * 13)]), cover))
    mc = P(-1, -2)
    medal = union(ring_mask(size, mc, 13 * F, 16 * F), star(size, mc, 12 * F, thin=0.22), star(size, mc, 8.5 * F, thin=0.25, rot=45))
    gold = union(gold, medal)
    light = top_left_light(size, C)
    img = comp(img, solid(size, (30, 0, 8), scale_l(shift(gold, 0.6 * F, 0.9 * F), 0.7)), gold_paint(size, gold, rng, light=light, bands=1.2))
    img = comp(img, solid(size, CRIMSON, disc(size, mc, 2.6 * F)), solid(size, GOLD_CORE, disc(size, (mc[0] - 0.7 * F, mc[1] - 0.7 * F), 1 * F)))
    img = comp(img, gold_paint(size, clasp, rng, angle=80), solid(size, INK, scale_l(ring_in(clasp, 0.7 * F), 0.6)))
    img = comp(img, solid(size, CRIMSON, disc(size, P(31, 0), 3 * F)), solid(size, (250, 170, 180), disc(size, P(30.2, -0.8), 1.1 * F)))
    img = comp(img, paint_fill(size, ribbon, rng, [(0, TEAL_DEEP), (1, TEAL)], linear_light(size, 90), depth=0.15))
    catch = inter(cut(cover, shift(cover, 1.2 * F, 1.6 * F)), cover)
    img = comp(img, solid(size, (250, 200, 190), scale_l(catch, 0.45)))
    img = comp(img, ink_edge(size, sil, 2.2 * F))
    return rotate_layer(img, -4, C), rotate_layer(sil, -4, C)


THESIS = [("journal", item_journal, 72), ("route-chart", item_chart, 196), ("chroma-sample", item_vial, 320),
          ("parents-journal", item_parents, 494)]
TH_Y = 80


def thesis_tile(idx, filled):
    """One specimen plate (bone, or gold for the Parents' Journal), empty or holding its relic."""
    name, item_fn, _ = THESIS[idx]
    gold = name == "parents-journal"
    size, C, P = tile_space()
    prng = random.Random(SEED + 100 + idx)
    half, rh = (56, 44) if gold else (47, 38)
    outer = brushy(rr(size, P(-half, -half), P(half, half), 9 * F), prng, amp=0.45, cell=5 * F, soft=3 * F)
    recess = brushy(rr(size, P(-rh, -rh), P(rh, rh), 6 * F), prng, amp=0.25, cell=6 * F, soft=2 * F)
    img = comp(canvas(*size), drop_shadow(size, outer, 2.5 * F, 3 * F), ink_edge(size, outer, 2.2 * F))
    if gold:
        img = comp(img, gold_paint(size, outer, prng, light=top_left_light(size, C), bands=1.0))
        rule = cut(grow(recess, 4.5 * F), grow(recess, 3.5 * F))
        img = comp(img, solid(size, GOLD_DEEP, scale_l(rule, 0.9)))
        for sx, sy in ((-1, -1), (1, -1), (-1, 1), (1, 1)):
            gx, gy = P(sx * 50, sy * 50)
            gem = poly_mask(size, [(gx, gy - 4 * F), (gx + 4 * F, gy), (gx, gy + 4 * F), (gx - 4 * F, gy)])
            img = comp(img, ink_edge(size, gem, 1 * F), solid(size, CRIMSON, gem),
                       solid(size, (250, 160, 170), scale_l(star(size, (gx - 1 * F, gy - 1 * F), 1.6 * F), 0.9)))
    else:
        img = comp(img, paper_fill(size, outer, prng, base=BONE, amount=0.22))
        img = comp(img, clip(solid(size, (150, 120, 84), scale_l(blur(ring_in(outer, 4 * F), 3 * F), 0.35)), outer))
        rule = cut(grow(recess, 3.5 * F), grow(recess, 2.7 * F))
        img = comp(img, solid(size, GOLD_DEEP, scale_l(rule, 0.75)))
        for sx, sy in ((-1, -1), (1, -1), (-1, 1), (1, 1)):
            img = comp(img, solid(size, TEAL_DEEP, disc(size, P(sx * 43, sy * 43), 1.6 * F)))
    rng = random.Random(SEED + 150 + idx)
    if gold:
        stops = [(0, (14, 10, 20)), (1, (58, 44, 70))] if not filled else [(0, (40, 26, 30)), (0.6, (120, 70, 40)), (1, (236, 170, 90))]
        img = comp(img, paint_fill(size, recess, rng, stops, radial_light(size, C, rh * 1.4 * F, power=0.8), depth=0.25, angle=-30))
    else:
        base = (206, 192, 166) if not filled else (246, 236, 214)
        img = comp(img, paper_fill(size, recess, rng, base=base, amount=0.24))
        if filled:
            img = comp(img, clip(solid(size, GOLD, scale_l(radial_light(size, C, rh * 1.2 * F), 0.45)), recess))
    img = comp(img, inner_shadow(size, recess, 1.6 * F, 2.4 * F, 2.2 * F, a=0.6 if not filled else 0.4))
    img = comp(img, solid(size, INK, scale_l(ring_out(recess, 1 * F), 0.85)))
    layer, sil = item_fn(size, C, P, random.Random(SEED + 170 + idx))
    if filled:
        if gold:
            rays = Image.new("L", size, 0)
            rd = ImageDraw.Draw(rays)
            for k in range(16):
                a = math.radians(k * 22.5 + 6)
                w = 0.09 if k % 2 else 0.05
                rd.polygon([C, (C[0] + math.cos(a - w) * 80 * F, C[1] + math.sin(a - w) * 80 * F),
                            (C[0] + math.cos(a + w) * 80 * F, C[1] + math.sin(a + w) * 80 * F)], fill=110)
            rays = inter(blur(rays, 1.5 * F), radial_light(size, C, 60 * F, power=0.6))
            img = comp(img, clip(solid(size, GOLD_CORE, rays), recess))
        img = comp(img, clip(glow(sil, GOLD_CORE if gold else GOLD, 5 * F, 1.3), recess))
        img = comp(img, layer)
        spk = [(P(31, -33)[0], P(31, -33)[1], 6 * F), (P(-33, 30)[0], P(-33, 30)[1], 3.5 * F)] if not gold else \
            [(P(36, -37)[0], P(36, -37)[1], 7 * F), (P(-38, 31)[0], P(-38, 31)[1], 5 * F), (P(-36, -30)[0], P(-36, -30)[1], 3.5 * F)]
        sp = sparkles(size, spk)
        img = comp(img, glow(sp, GOLD, 2 * F, 1.4), solid(size, GOLD_CORE, sp))
    else:
        ghost_rgb = GOLD if gold else INK_SOFT
        sketch = desaturate(layer, 1.0, dim=1.0 if gold else 0.8, tint=(250, 200, 120) if gold else (190, 170, 140))
        img = comp(img, fade(sketch, 0.3 if gold else 0.28))
        dash = inter(ring_out(sil, 1.5 * F), dashes(size, 4 * F))
        img = comp(img, solid(size, ghost_rgb, scale_l(dash, 0.85 if gold else 0.75)))
    return downsample(img)


def thesis_sheet():
    W, H = 576, 160
    size = (W * F, H * F)
    rng = random.Random(SEED + 10)
    band = brushy(rr(size, (6 * F, 18 * F), (570 * F, 142 * F), 16 * F), rng, amp=0.45, cell=6 * F, soft=4 * F)
    img = comp(canvas(*size), drop_shadow(size, band, 4 * F, 6 * F, 0.5), ink_edge(size, band, 2.5 * F))
    img = comp(img, paint_fill(size, band, rng, [(0, (46, 40, 56)), (1, (20, 16, 24))], linear_light(size, 0), depth=0.3, angle=2))
    img = comp(img, clip(solid(size, CRIMSON_DEEP, scale_l(band_light(size, 70 * F, 142 * F), 0.4)), band))
    img = comp(img, clip(solid(size, TEAL_DEEP, scale_l(radial_light(size, (40 * F, 50 * F), 160 * F), 0.35)), band))
    img = comp(img, gold_paint(size, ring_in(band, 4 * F), rng, angle=10, bands=2.2))
    img = comp(img, solid(size, GOLD, scale_l(ring_in(shrink(band, 9 * F), 1 * F), 0.5)))
    # a dotted gold thread running plate to plate, and a chevron leading into the gold plate
    dots = Image.new("L", size, 0)
    dd = ImageDraw.Draw(dots)
    for x in range(30, 440, 8):
        dd.ellipse(((x - 1.4) * F, (TH_Y - 1.4) * F, (x + 1.4) * F, (TH_Y + 1.4) * F), fill=255)
    img = comp(img, solid(size, GOLD, scale_l(dots, 0.7)))
    chev = Image.new("L", size, 0)
    for cx in (392, 403):
        chev = union(chev, line_mask(size, [((cx - 4) * F, (TH_Y - 8) * F), ((cx + 4) * F, TH_Y * F), ((cx - 4) * F, (TH_Y + 8) * F)], 2.6 * F))
    img = comp(img, solid(size, (0, 0, 0), scale_l(shift(chev, 0, 1 * F), 0.6)), gold_paint(size, chev, rng, angle=80))
    strip = downsample(img)
    sheet = canvas(TS * 4 + 64, H + TS * 2)
    sheet.alpha_composite(strip, (0, 0))
    sprites = [{"name": "tracker", "box": [0, 0, W, H]}]
    for idx, (name, _, cx) in enumerate(THESIS):
        empty = thesis_tile(idx, False)
        full = thesis_tile(idx, True)
        x0, y0 = cx - TS // 2, TH_Y - TS // 2
        sheet.alpha_composite(empty, (x0, y0))
        sprites.append({"name": f"slot-{name}", "box": [x0, y0, x0 + TS, y0 + TS]})
        sheet.alpha_composite(empty, (idx * TS, H))
        sheet.alpha_composite(full, (idx * TS, H + TS))
        sprites.append({"name": f"{name}-empty", "box": [idx * TS, H, idx * TS + TS, H + TS]})
        sprites.append({"name": f"{name}-filled", "box": [idx * TS, H + TS, idx * TS + TS, H + 2 * TS]})
    return sheet, sprites


# ------------------------------------------------------------------------------------------------------- stains

def mark_fire(size, P):
    outer = union(teardrop(size, P(0, 2), 15 * F, 22 * F), teardrop(size, P(-10, 9), 8 * F, 13 * F, angle=-28),
                  teardrop(size, P(10, 10), 7 * F, 12 * F, angle=26))
    core = teardrop(size, P(0, 10), 6.5 * F, 11 * F)
    return outer, core


def mark_ice(size, P):
    m = Image.new("L", size, 0)
    for k in range(3):
        a = math.radians(90 + 60 * k)
        dx, dy = math.cos(a), math.sin(a)
        m = union(m, line_mask(size, [P(-dx * 23, -dy * 23), P(dx * 23, dy * 23)], 4.4 * F))
        for s in (-1, 1):
            bx, by = s * dx * 13, s * dy * 13
            for t in (-1, 1):
                b = a + t * math.radians(50) + (0 if s > 0 else math.pi)
                m = union(m, line_mask(size, [P(bx, by), P(bx + math.cos(b) * 8, by + math.sin(b) * 8)], 3.2 * F))
    hexa = poly_mask(size, [P(7 * math.cos(math.radians(30 + 60 * k)), 7 * math.sin(math.radians(30 + 60 * k))) for k in range(6)])
    core = poly_mask(size, [P(3.6 * math.cos(math.radians(30 + 60 * k)), 3.6 * math.sin(math.radians(30 + 60 * k))) for k in range(6)])
    return union(m, hexa), core


def mark_bolt(size, P):
    pts = [(5, -27), (-12, 3), (-1, 3), (-7, 27), (13, -6), (2, -6), (11, -27)]
    outer = poly_mask(size, [P(x, y) for x, y in pts])
    core = line_mask(size, [P(6, -21), P(-4, -1)], 1.6 * F)
    return outer, core


STAINS = [
    ("fire", mark_fire, [(0, (78, 6, 22)), (0.45, CRIMSON), (0.8, EMBER), (1, AMBER)], EMBER, CRIMSON_DEEP,
     (GOLD_CORE, AMBER), 128),
    ("ice", mark_ice, [(0, (24, 70, 88)), (0.45, TEAL_DEEP), (0.78, TEAL), (1, LIGHT_TEAL)], LIGHT_TEAL, (18, 56, 72),
     (FROST, LIGHT_TEAL), 222),
    ("lightning", mark_bolt, [(0, (30, 18, 56)), (0.45, VIOLET_DEEP), (0.8, VIOLET), (1, LILAC)], LILAC, (24, 14, 46),
     (GOLD_CORE, GOLD), 300),
]


def stain_tile(idx, lit):
    name, mark_fn, stops, glow_rgb, pool_rgb, mark_rgb, tail_deg = STAINS[idx]
    size, C, P = tile_space()
    rng = random.Random(SEED + 300 + idx)
    blob = rough_disc(size, P(0, 1), 38 * F, rng, wobble=0.11, jitter=0.006)
    drops = []
    for k in range(3):
        a = math.radians(tail_deg + 70 + k * 85 + rng.uniform(-20, 20))
        dist = rng.uniform(44, 50)
        drops.append(rough_disc(size, P(math.cos(a) * dist, 1 + math.sin(a) * dist), rng.uniform(2.6, 4.8) * F, rng, wobble=0.1))
    ta = math.radians(tail_deg)
    tail_pts = [P(math.cos(ta + 0.08 * math.sin(t * 3)) * (26 + 26 * t), 1 + math.sin(ta + 0.08 * math.sin(t * 3)) * (26 + 26 * t))
                for t in [i / 12 for i in range(13)]]
    tail = taper_mask(size, tail_pts, [(16 - 13 * i / 12) * F for i in range(12)])
    shape = brushy(union(blob, tail, *drops), rng, amp=0.55, cell=4 * F, soft=2.5 * F)
    img = canvas(*size)
    if lit:
        img = comp(img, glow(shape, glow_rgb, 4.5 * F, 1.0))
    img = comp(img, ink_edge(size, shape, 1.6 * F))
    light = radial_light(size, P(-12, -14), 62 * F, power=0.9)
    img = comp(img, paint_fill(size, shape, rng, stops, light, depth=0.3, angle=-25))
    strokes = canvas(*size)
    for k in range(6):
        y = -26 + k * 10 + rng.uniform(-2, 2)
        pts = curve(P(-40, y + 8), P(0, y - 6 + rng.uniform(-3, 3)), P(40, y + 2), 20)
        rgb = stops[-1][1] if k % 2 else stops[1][1]
        strokes = comp(strokes, brush_stroke(size, pts, 5 * F, rgb, 70, seed=SEED + 330 + idx * 10 + k, bristles=6))
    img = comp(img, clip(strokes, shape))
    img = comp(img, clip(solid(size, pool_rgb, scale_l(blur(ring_in(shape, 4 * F), 2.5 * F), 0.7)), shape))
    outer, core = mark_fn(size, P)
    img = comp(img, solid(size, INK, scale_l(blur(shift(outer, 1 * F, 2 * F), 1.5 * F), 0.55)), ink_edge(size, outer, 1.6 * F))
    if lit:
        img = comp(img, glow(outer, mark_rgb[0], 4 * F, 0.9))
    img = comp(img, solid(size, mark_rgb[0], outer), solid(size, mark_rgb[1], inter(core, outer)))
    if lit:
        spk = [(P(30, -30)[0], P(30, -30)[1], 6.5 * F), (P(-34, -22)[0], P(-34, -22)[1], 4 * F), (P(22, 36)[0], P(22, 36)[1], 3.2 * F)]
        sp = sparkles(size, spk)
        img = comp(img, glow(sp, glow_rgb, 2 * F, 1.4), solid(size, GOLD_CORE, sp))
        out = downsample(img)
    else:
        out = fade(desaturate(downsample(img), 0.8, dim=0.72, tint=(214, 210, 226)), 0.62)
    return out


def toggle(armed):
    """The pre-battle switch: a gilt track with a paint knob. Off: knob left, dull. Armed: knob right, lit, track painted."""
    W, H = 192, 96
    size = (W * F, H * F)

    def P(x, y):
        return (x * F, y * F)
    rng = random.Random(SEED + 400)
    track = brushy(rr(size, P(14, 26), P(178, 70), 22 * F), rng, amp=0.35, cell=5 * F, soft=2.5 * F)
    groove = shrink(track, 7 * F)
    img = comp(canvas(*size), drop_shadow(size, track, 3 * F, 4 * F, 0.5), ink_edge(size, track, 2.4 * F))
    img = comp(img, gold_paint(size, track, rng, light=linear_light(size, 20), bands=1.6))
    img = comp(img, solid(size, INK, scale_l(ring_out(groove, 1 * F), 0.9)))
    img = comp(img, paint_fill(size, groove, rng, [(0, (14, 12, 20)), (1, (52, 46, 62))], linear_light(size, 180), depth=0.3, angle=0))
    img = comp(img, inner_shadow(size, groove, 0, 3 * F, 2 * F, a=0.7))
    krng = random.Random(SEED + 410)
    if armed:
        streak = taper_mask(size, [P(30 + i * 10, 48 + 1.5 * math.sin(i * 1.3)) for i in range(13)],
                            [(9 + 3 * math.sin(i * 0.7)) * F for i in range(12)])
        streak = inter(brushy(streak, krng, amp=0.5, cell=3 * F, soft=1.5 * F), groove)
        hue = band_light(size, P(30, 0)[0], P(150, 0)[0], horizontal=True)
        img = comp(img, clip(glow(streak, GOLD, 4 * F, 1.0), groove))
        img = comp(img, paint_fill(size, streak, krng, [(0, VIOLET), (0.33, TEAL), (0.66, GOLD), (1, CRIMSON)], hue, depth=0.2, angle=0))
        kc = P(148, 48)
    else:
        dots = Image.new("L", size, 0)
        dd = ImageDraw.Draw(dots)
        for x in range(76, 168, 9):
            dd.ellipse((*P(x - 1.3, 48 - 1.3), *P(x + 1.3, 48 + 1.3)), fill=255)
        img = comp(img, solid(size, (110, 104, 122), scale_l(dots, 0.8)))
        kc = P(44, 48)
    knob = rough_disc(size, kc, 25 * F, krng, wobble=0.02, jitter=0.002)
    face = disc(size, kc, 17 * F)
    if armed:
        img = comp(img, glow(knob, AMBER, 10 * F, 1.3), glow(knob, GOLD_CORE, 4 * F, 0.8))
        img = comp(img, drop_shadow(size, knob, 2 * F, 2.5 * F, 0.6), ink_edge(size, knob, 2 * F))
        img = comp(img, gold_paint(size, knob, krng, light=top_left_light(size, kc), bands=1.0))
        img = comp(img, solid(size, INK, scale_l(ring_out(face, 1 * F), 0.9)))
        img = comp(img, paint_fill(size, face, krng, [(0, CRIMSON_DEEP), (0.7, CRIMSON), (1, (236, 96, 96))],
                                   radial_light(size, (kc[0] - 5 * F, kc[1] - 6 * F), 22 * F), depth=0.2))
        mark = union(star(size, kc, 12 * F, thin=0.2), star(size, kc, 6.5 * F, thin=0.25, rot=45))
        img = comp(img, glow(mark, GOLD_CORE, 3 * F, 1.2), solid(size, GOLD_CORE, mark))
        sp = sparkles(size, [(*P(176, 24), 5 * F), (*P(122, 22), 3.2 * F), (*P(172, 74), 3 * F)])
        img = comp(img, glow(sp, GOLD, 1.5 * F, 1.4), solid(size, GOLD_CORE, sp))
    else:
        img = comp(img, drop_shadow(size, knob, 2 * F, 2.5 * F, 0.5), ink_edge(size, knob, 2 * F))
        img = comp(img, paper_fill(size, knob, krng, base=(176, 170, 164), amount=0.3))
        img = comp(img, clip(solid(size, (236, 232, 226), scale_l(radial_light(size, (kc[0] - 8 * F, kc[1] - 9 * F), 20 * F), 0.6)), knob))
        img = comp(img, inner_shadow(size, face, 1 * F, 1.6 * F, 1.5 * F, a=0.6))
        img = comp(img, solid(size, INK_SOFT, scale_l(ring_mask(size, kc, 17 * F, 18.2 * F), 0.8)))
        img = comp(img, solid(size, INK_SOFT, scale_l(ring_mask(size, kc, 6.5 * F, 9 * F), 0.85)))
    return downsample(img)


def stains_sheet():
    sheet = canvas(TS * 3, TS * 2 + 96)
    sprites = []
    for idx, spec in enumerate(STAINS):
        for row, lit in enumerate((True, False)):
            x0, y0 = idx * TS, row * TS
            sheet.alpha_composite(stain_tile(idx, lit), (x0, y0))
            sprites.append({"name": f"{spec[0]}-{'lit' if lit else 'dim'}", "box": [x0, y0, x0 + TS, y0 + TS]})
    for k, armed in enumerate((False, True)):
        x0, y0 = k * 192, TS * 2
        sheet.alpha_composite(toggle(armed), (x0, y0))
        sprites.append({"name": f"toggle-{'armed' if armed else 'off'}", "box": [x0, y0, x0 + 192, y0 + 96]})
    return sheet, sprites


# ------------------------------------------------------------------------------------------------- bestial wheel

def glyph_caster(size, P):
    return union(star(size, P(-3, 3), 19 * F, thin=0.2), star(size, P(13, -13), 7 * F, thin=0.24),
                 star(size, P(-14, -14), 4.5 * F, thin=0.26))


def glyph_agile(size, P):
    rot = math.radians(40)

    def R(x, y):
        return P(x * math.cos(rot) - y * math.sin(rot), x * math.sin(rot) + y * math.cos(rot))
    left, right = [], []
    n = 24
    for i in range(n + 1):
        t = i / n
        y = 20 - 44 * t
        w = 10.5 * math.sin(math.pi * t) ** 0.65 * (1 - 0.2 * t)
        left.append(R(-w, y))
        right.append(R(w * 0.78, y))
    vane = poly_mask(size, left + right[::-1])
    for t, side in ((0.42, -1), (0.62, -1), (0.5, 1)):
        y = 20 - 44 * t
        vane = cut(vane, poly_mask(size, [R(side * 13, y + 1), R(side * 2.5, y - 4.5), R(side * 13, y - 6)]))
    vane = cut(vane, line_mask(size, [R(0, 16), R(0, -21)], 1.5 * F))
    quill = line_mask(size, [R(0, 30), R(0, 17)], 2.6 * F)
    return union(vane, quill)


def glyph_balanced(size, P):
    m = union(line_mask(size, [P(0, -16), P(0, 18)], 3.6 * F), line_mask(size, [P(-20, -11), P(20, -11)], 3.4 * F),
              disc(size, P(0, -18), 3.6 * F), rr(size, P(-11, 16), P(11, 21), 2 * F))
    for sx in (-1, 1):
        m = union(m, line_mask(size, [P(sx * 20, -11), P(sx * 20 - 6, 5)], 1.4 * F), line_mask(size, [P(sx * 20, -11), P(sx * 20 + 6, 5)], 1.4 * F))
        pan = Image.new("L", size, 0)
        ImageDraw.Draw(pan).chord((*P(sx * 20 - 9, -2), *P(sx * 20 + 9, 11)), 0, 180, fill=255)
        m = union(m, pan)
    return m


def glyph_heavy(size, P):
    """An anvil."""
    pts = [(-11, -14), (21, -14), (21, -4), (11, -3), (7, 8), (16, 13), (16, 20), (-14, 20), (-14, 13), (-5, 8), (-8, -3),
           (-15, -5), (-26, -10)]
    m = poly_mask(size, [P(x, y) for x, y in pts])
    face = rr(size, P(-9, -14), P(21, -10.5), 0)
    return m, face


def glyph_almighty(size, P):
    m = disc(size, P(0, 0), 9.5 * F)
    for k in range(8):
        a = math.radians(k * 45 - 90)
        r0, r1, w = 12.5, (23 if k % 2 == 0 else 19), 4.2
        b0 = (math.cos(a) * r0, math.sin(a) * r0)
        tip = (math.cos(a) * r1, math.sin(a) * r1)
        nx, ny = -math.sin(a) * w, math.cos(a) * w
        m = union(m, poly_mask(size, [P(b0[0] + nx, b0[1] + ny), P(*tip), P(b0[0] - nx, b0[1] - ny)]))
    return m


# name, glyph, filled paint ramp, glyph colour, glow colour
WHEEL = [
    ("caster", glyph_caster, [(0, (26, 16, 48)), (0.55, VIOLET), (1, LILAC)], GOLD_CORE, LILAC),
    ("agile", glyph_agile, [(0, (14, 52, 62)), (0.55, TEAL), (1, LIGHT_TEAL)], BONE, LIGHT_TEAL),
    ("balanced", glyph_balanced, [(0, (150, 132, 104)), (0.6, (226, 214, 190)), (1, (252, 248, 236))], INK, BONE),
    ("heavy", glyph_heavy, [(0, (70, 6, 20)), (0.55, CRIMSON), (1, (230, 90, 90))], BONE, (236, 96, 96)),
    ("almighty", glyph_almighty, [(0, GOLD_DEEP), (0.6, GOLD), (1, GOLD_CORE)], INK, GOLD_CORE),
]
WC = (256, 256)
R_WHEEL, R_FIELD, R_SLOT, R_BADGE, R_HUB = 240, 220, 166, 95, 38
SLOT_R, SLOT_IN, SOCK_R = 47, 39, 21


def slot_angle(k):
    return 72 * k


def at_angle(c, r, deg):
    a = math.radians(deg)
    return (c[0] + math.sin(a) * r, c[1] - math.cos(a) * r)


def slot_center(k):
    x, y = at_angle(WC, R_SLOT, slot_angle(k))
    return int(round(x)), int(round(y))


def badge_center(k):
    x, y = at_angle(WC, R_BADGE, slot_angle(k))
    return int(round(x)), int(round(y))


def glyph_mask(k, size, P):
    g = WHEEL[k][1](size, P)
    return g if isinstance(g, tuple) else (g, None)


def wheel_tile(k, state):
    """A slot as a 128 px tile: 'empty' (dark socket, engraved mark), 'filled' (painted, lit mark) or 'locked'."""
    name, _, stops, glyph_rgb, glow_rgb = WHEEL[k]
    size, C, P = tile_space()
    grng = random.Random(SEED + 500 + k)
    rim = rough_disc(size, C, SLOT_R * F, grng, wobble=0.012, jitter=0.002)
    hole = rough_disc(size, C, SLOT_IN * F, grng, wobble=0.01, jitter=0.002)
    rng = random.Random(SEED + 520 + k * 3 + ("empty", "filled", "locked").index(state))
    img = canvas(*size)
    if state == "filled":
        img = comp(img, glow(rim, glow_rgb, 4 * F, 1.3))
    img = comp(img, drop_shadow(size, rim, 2 * F, 2.5 * F, 0.55), ink_edge(size, rim, 2.2 * F))
    light = top_left_light(size, C)
    ring = cut(rim, hole)
    if state == "locked":
        img = comp(img, desaturate(gold_paint(size, ring, grng, light=light, bands=1.0), 0.8, dim=0.75, tint=(220, 216, 236)))
    else:
        img = comp(img, gold_paint(size, ring, grng, light=light, bands=1.0 if state == "filled" else 0.7))
        if state == "empty":
            img = comp(img, clip(solid(size, INK, scale_l(ring, 0.25)), ring))
    for j in range(5):
        a = slot_angle(k) + 36 + j * 72
        bx, by = at_angle(C, (SLOT_IN + SLOT_R) / 2 * F, a)
        img = comp(img, solid(size, GOLD_DEEP if state != "locked" else IRON, disc(size, (bx, by), 1.4 * F)))
    img = comp(img, solid(size, INK, scale_l(ring_out(hole, 1.2 * F), 0.95)))
    g, extra = glyph_mask(k, size, P)
    if state == "filled":
        img = comp(img, paint_fill(size, hole, rng, stops, radial_light(size, P(-10, -12), 52 * F, power=0.8), depth=0.25, angle=-30))
        img = comp(img, clip(solid(size, stops[0][1], scale_l(blur(ring_in(hole, 4 * F), 3 * F), 0.6)), hole))
        img = comp(img, solid(size, INK, scale_l(blur(shift(g, 0.8 * F, 1.6 * F), 1.2 * F), 0.5)))
        img = comp(img, ink_edge(size, g, 1.3 * F, rgb=INK if glyph_rgb != INK else lerp_rgb(stops[0][1], INK, 0.3)))
        if glyph_rgb != INK:
            img = comp(img, glow(g, glyph_rgb, 3 * F, 0.7))
        img = comp(img, solid(size, glyph_rgb, g))
        if extra is not None:
            img = comp(img, solid(size, stops[1][1], inter(extra, g)))
        sp = sparkles(size, [(*P(30, -34), 6 * F), (*P(-37, 26), 3.5 * F)])
        img = comp(img, glow(sp, glow_rgb, 2 * F, 1.4), solid(size, GOLD_CORE, sp))
    else:
        img = comp(img, paint_fill(size, hole, rng, [(0, (12, 10, 18)), (1, (54, 46, 66))],
                                   radial_light(size, P(4, 6), 46 * F, power=0.9), depth=0.3, angle=-30))
        img = comp(img, inner_shadow(size, hole, 1.6 * F, 2.6 * F, 2.4 * F, a=0.8))
        tint = lerp_rgb(stops[1][1], (120, 112, 134), 0.55) if state == "empty" else (92, 88, 104)
        img = comp(img, solid(size, (6, 4, 10), scale_l(shift(g, -0.8 * F, -1.0 * F), 0.8)),
                   solid(size, tint, scale_l(g, 0.75 if state == "empty" else 0.45)))
        if state == "locked":
            body = rr(size, P(-13, -2), P(13, 19), 3 * F)
            shackle = cut(ring_mask(size, P(0, -4), 6.5 * F, 10.5 * F), rr(size, P(-14, -4), P(14, 20), 0))
            shackle = union(shackle, rr(size, P(-10.5, -5), P(-6.5, 0), 0), rr(size, P(6.5, -5), P(10.5, 0), 0))
            lock = union(body, shackle)
            img = comp(img, drop_shadow(size, lock, 1.5 * F, 2 * F, 0.7), ink_edge(size, lock, 1.6 * F))
            img = comp(img, paint_fill(size, lock, rng, [(0, (70, 66, 80)), (0.6, IRON), (1, IRON_LIGHT)],
                                       linear_light(size, 30), depth=0.2, angle=0))
            key = union(disc(size, P(0, 6), 2.6 * F), poly_mask(size, [P(-1.6, 7), P(1.6, 7), P(2.4, 13), P(-2.4, 13)]))
            img = comp(img, solid(size, INK, key))
            img = comp(img, solid(size, (255, 255, 255), scale_l(inter(cut(body, shift(body, 0, 1.2 * F)), body), 0.5)))
    return downsample(img)


def socket_tile(lit):
    size, C, P = tile_space(64)
    rng = random.Random(SEED + 600)
    rim = rough_disc(size, C, SOCK_R * F, rng, wobble=0.015, jitter=0.002)
    hole = disc(size, C, (SOCK_R - 5) * F)
    img = canvas(*size)
    if lit:
        img = comp(img, glow(rim, GOLD, 5 * F, 1.3))
    img = comp(img, drop_shadow(size, rim, 1.5 * F, 2 * F, 0.5), ink_edge(size, rim, 1.8 * F))
    ring = cut(rim, hole)
    img = comp(img, gold_paint(size, ring, rng, light=top_left_light(size, C), bands=1.0 if lit else 0.6))
    if not lit:
        img = comp(img, solid(size, INK, scale_l(ring, 0.3)))
    img = comp(img, solid(size, INK, scale_l(ring_out(hole, 1 * F), 0.95)))
    stops = [(0, (12, 10, 18)), (1, (54, 46, 66))] if not lit else [(0, (60, 40, 30)), (1, (150, 104, 56))]
    img = comp(img, paint_fill(size, hole, rng, stops, radial_light(size, C, 16 * F), depth=0.3))
    img = comp(img, inner_shadow(size, hole, 1 * F, 1.8 * F, 1.6 * F, a=0.8))
    img = comp(img, solid(size, GOLD_DEEP if not lit else GOLD, scale_l(ring_mask(size, C, 9 * F, 10 * F), 0.5 if not lit else 0.8)))
    return downsample(img)


def pointer_tile():
    size, C, P = tile_space(64)
    rng = random.Random(SEED + 650)
    arrow = poly_mask(size, [P(-16, -22), P(16, -22), P(9, -8), P(0, 24), P(-9, -8)])
    cap = rr(size, P(-20, -28), P(20, -18), 4 * F)
    shape = union(arrow, cap)
    img = comp(canvas(*size), drop_shadow(size, shape, 2 * F, 2.5 * F, 0.55), ink_edge(size, shape, 2 * F))
    img = comp(img, gold_paint(size, shape, rng, light=linear_light(size, 70), bands=1.2))
    spine = line_mask(size, [P(0, -16), P(0, 18)], 1 * F)
    img = comp(img, solid(size, GOLD_DEEP, scale_l(spine, 0.8)))
    gem = poly_mask(size, [P(0, -16), P(6, -10), P(0, -3), P(-6, -10)])
    img = comp(img, ink_edge(size, gem, 0.8 * F), solid(size, CRIMSON, gem), solid(size, (250, 170, 180), star(size, P(-1.4, -11), 2.2 * F)))
    return downsample(img)


def wheel_base():
    S = 512
    size = (S * F, S * F)
    C = (WC[0] * F, WC[1] * F)
    rng = random.Random(SEED + 700)
    outer = rough_disc(size, C, R_WHEEL * F, rng, wobble=0.006, jitter=0.0012)
    field = rough_disc(size, C, R_FIELD * F, rng, wobble=0.005, jitter=0.0012)
    img = comp(canvas(*size), drop_shadow(size, outer, 5 * F, 8 * F, 0.55), ink_edge(size, outer, 3 * F))
    light = top_left_light(size, C)
    img = comp(img, gold_paint(size, outer, rng, light=light, bands=1.0))
    img = comp(img, solid(size, GOLD_DEEP, scale_l(ring_mask(size, C, (R_WHEEL - 6) * F, (R_WHEEL - 5) * F), 0.8)))
    # rim pattern: little engraved chevrons running clockwise
    chev = Image.new("L", size, 0)
    for j in range(60):
        a = j * 6 + 3
        if min((a - 36) % 72, 72 - (a - 36) % 72) < 5:
            continue
        p0 = at_angle(C, (R_FIELD + 6) * F, a - 1.4)
        p1 = at_angle(C, (R_FIELD + 10.5) * F, a + 1.0)
        p2 = at_angle(C, (R_WHEEL - 8) * F, a - 1.4)
        chev = union(chev, line_mask(size, [p0, p1, p2], 1.2 * F))
    img = comp(img, solid(size, GOLD_DEEP, scale_l(chev, 0.75)))
    img = comp(img, ink_edge(size, field, 2.4 * F))
    # the field: night ink with a teal heart and each sector washed in its slot's colour
    img = comp(img, paint_fill(size, field, rng, [(0, (16, 12, 22)), (0.6, (36, 30, 50)), (1, (58, 54, 78))],
                               radial_light(size, C, R_FIELD * F, power=0.7), depth=0.3, angle=30))
    wash = Image.new("RGBA", size, (0, 0, 0, 0))
    for k, spec in enumerate(WHEEL):
        sect = Image.new("L", size, 0)
        a = slot_angle(k) - 90
        rr_ = R_FIELD * F + 20
        ImageDraw.Draw(sect).pieslice((C[0] - rr_, C[1] - rr_, C[0] + rr_, C[1] + rr_), a - 34, a + 34, fill=255)
        pool = scale_l(radial_light(size, at_angle(C, R_SLOT * F, slot_angle(k)), 130 * F, power=0.8), 0.5)
        wash = comp(wash, solid(size, spec[2][1][1], scale_l(blur(sect, 10 * F), 0.26)),
                    solid(size, spec[2][2][1], inter(blur(sect, 10 * F), pool)))
    img = comp(img, clip(wash, field))
    img = comp(img, clip(solid(size, TEAL_DEEP, scale_l(radial_light(size, C, 120 * F, power=1.2), 0.45)), field))
    swirl = canvas(*size)
    for j in range(9):
        r = (70 + j * 17 + rng.uniform(-4, 4)) * F
        a0 = rng.uniform(0, 360)
        span = rng.uniform(70, 150)
        pts = [at_angle(C, r + 3 * F * math.sin(t * 4), a0 + span * t) for t in [i / 40 for i in range(41)]]
        swirl = comp(swirl, brush_stroke(size, pts, rng.uniform(5, 9) * F, (150, 150, 186) if j % 2 else TEAL, 34,
                                         seed=SEED + 710 + j, bristles=7))
    img = comp(img, clip(swirl, field))
    img = comp(img, inner_shadow(size, field, 0, 4 * F, 5 * F, a=0.6))
    # the track through the slots, spokes between them, stems from each slot to its badge socket
    track = ring_mask(size, C, (R_SLOT - 1.2) * F, (R_SLOT + 1.2) * F)
    img = comp(img, solid(size, GOLD, scale_l(track, 0.55)))
    spokes = Image.new("L", size, 0)
    stems = Image.new("L", size, 0)
    for k in range(5):
        a = slot_angle(k) + 36
        pts = [at_angle(C, (R_HUB + 2 + (R_FIELD - R_HUB - 4) * t) * F, a + 2.5 * math.sin(t * math.pi)) for t in [i / 16 for i in range(17)]]
        spokes = union(spokes, taper_mask(size, pts, [(6.5 - 4 * i / 16) * F for i in range(16)]))
        stems = union(stems, line_mask(size, [at_angle(C, (R_BADGE + SOCK_R) * F, slot_angle(k)),
                                              at_angle(C, (R_SLOT - SLOT_R) * F, slot_angle(k))], 2.4 * F))
    for m in (spokes, stems):
        img = comp(img, solid(size, (0, 0, 0), scale_l(blur(shift(m, 0, 1.5 * F), 1.5 * F), 0.6)), ink_edge(size, m, 1 * F),
                   gold_paint(size, m, rng, light=light, bands=1.4))
    # crimson studs on the rim at each spoke
    for k in range(5):
        x, y = at_angle(C, (R_FIELD + R_WHEEL) / 2 * F, slot_angle(k) + 36)
        stud = disc(size, (x, y), 5 * F)
        img = comp(img, ink_edge(size, stud, 1.4 * F), solid(size, CRIMSON, stud),
                   solid(size, (240, 130, 140), disc(size, (x - 1.4 * F, y - 1.6 * F), 1.8 * F)))
    # the hub: a gold boss with five petals, each in its slot's colour, and a bone pearl
    hub = rough_disc(size, C, R_HUB * F, rng, wobble=0.01, jitter=0.002)
    img = comp(img, drop_shadow(size, hub, 2.5 * F, 3 * F, 0.6), ink_edge(size, hub, 2 * F), gold_paint(size, hub, rng, light=light, bands=1.0))
    img = comp(img, solid(size, GOLD_DEEP, scale_l(ring_mask(size, C, (R_HUB - 5) * F, (R_HUB - 4) * F), 0.8)))
    for k, spec in enumerate(WHEEL):
        petal = teardrop(size, at_angle(C, 19 * F, slot_angle(k)), 7 * F, 12 * F, angle=slot_angle(k))
        img = comp(img, ink_edge(size, petal, 1.2 * F),
                   paint_fill(size, petal, rng, spec[2], radial_light(size, at_angle(C, 24 * F, slot_angle(k)), 14 * F), depth=0.15))
    pearl = disc(size, C, 7 * F)
    img = comp(img, ink_edge(size, pearl, 1.2 * F), paper_fill(size, pearl, rng, base=BONE),
               solid(size, (255, 255, 255), disc(size, (C[0] - 2 * F, C[1] - 2.4 * F), 2.2 * F)))
    base = downsample(img)
    sprites = [{"name": "wheel", "box": [0, 0, S, S]}]
    sock = socket_tile(False)
    for k, spec in enumerate(WHEEL):
        sx, sy = slot_center(k)
        bx, by = badge_center(k)
        base.alpha_composite(sock, (bx - 32, by - 32))
        base.alpha_composite(wheel_tile(k, "empty"), (sx - TS // 2, sy - TS // 2))
        sprites.append({"name": f"slot-{spec[0]}", "box": [sx - TS // 2, sy - TS // 2, sx + TS // 2, sy + TS // 2]})
        sprites.append({"name": f"badge-{spec[0]}", "box": [bx - 32, by - 32, bx + 32, by + 32]})
    return base, sprites


def wheel_sheet():
    base, sprites = wheel_base()
    sheet = canvas(512, 512 + TS * 3)
    sheet.alpha_composite(base, (0, 0))
    cells = [(f"{spec[0]}-empty", wheel_tile(k, "empty")) for k, spec in enumerate(WHEEL)]
    cells += [(f"{spec[0]}-filled", wheel_tile(k, "filled")) for k, spec in enumerate(WHEEL)]
    cells.append(("almighty-locked", wheel_tile(4, "locked")))
    for i, (name, tile) in enumerate(cells):
        x0, y0 = (i % 4) * TS, 512 + (i // 4) * TS
        sheet.alpha_composite(tile, (x0, y0))
        sprites.append({"name": name, "box": [x0, y0, x0 + TS, y0 + TS]})
    x0, y0 = 3 * TS, 512 + 2 * TS
    for j, (name, tile) in enumerate((("socket-empty", socket_tile(False)), ("socket-lit", socket_tile(True)))):
        sheet.alpha_composite(tile, (x0 + j * 64, y0))
        sprites.append({"name": name, "box": [x0 + j * 64, y0, x0 + j * 64 + 64, y0 + 64]})
    sheet.alpha_composite(pointer_tile(), (x0, y0 + 64))
    sprites.append({"name": "pointer", "box": [x0, y0 + 64, x0 + 64, y0 + 128]})
    return sheet, sprites


# ----------------------------------------------------------------------------------------------------------- entry

def make(out_dir, fonts):
    out = []

    def emit(asset, rel, img, sprites=()):
        save(img, out_dir, rel)
        out.append({"asset": asset, "path": f"art/{rel}", "size": [img.width, img.height], "sprites": list(sprites)})

    sheet, sprites = thesis_sheet()
    emit("asset.thesis-tracker", "gauges/thesis.png", sheet, sprites)
    sheet, sprites = stains_sheet()
    emit("asset.stain-icons", "gauges/stains.png", sheet, sprites)
    sheet, sprites = wheel_sheet()
    emit("asset.wheel-frame", "gauges/bestial-wheel.png", sheet, sprites)
    return out

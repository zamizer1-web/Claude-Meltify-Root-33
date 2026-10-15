"""Tokens and badges for Root 33: the Expedition Flag, Esquie's four rocks, the Lampmaster and grumpy-turtle guard
badges, the Living Portrait easel frame and its veiled twin, the Monolith crack and the Nevron coverage mark.

Everything is original and drawn from scratch with Pillow in the shared painterly style (tools/art/style.py): ink
outlines, bone paper, gold leaf, crimson and river teal. Characters appear only as invented silhouettes and emblems.
Every random choice comes from a seeded random.Random (noise included), so the same code always paints the same pixels.
Pieces are drawn at 2-4x and reduced, so edges stay soft at token size.
"""
import math
import random
import sys
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageFont, ImageOps

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from style import (BONE, CRIMSON, CRIMSON_DEEP, DUSK, GOLD, GOLD_CORE, GOLD_DEEP, INK, INK_SOFT, PAPER,  # noqa: E402
                   TEAL, TEAL_DEEP, brush_stroke, canvas, curve, glow, inner_edge, solid)

SEED = 3340
LIGHT_TEAL = (176, 226, 222)
WOOD = (128, 88, 56)
WOOD_DEEP = (74, 50, 36)
WOOD_LIGHT = (176, 128, 82)
IRON = (124, 120, 134)
IRON_LIGHT = (224, 220, 230)
MAGENTA = (204, 46, 130)
FLAG_BLUE = (64, 122, 190)
FLAG_DEEP = (30, 62, 118)


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
    out = Image.new("L", mask.size, 0)
    out.paste(mask, (int(dx), int(dy)))
    return out


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


def taper_mask(size, pts, widths, fill=255):
    """A line whose width changes point by point (round joints)."""
    m = Image.new("L", size, 0)
    d = ImageDraw.Draw(m)
    for (a, b), w in zip(zip(pts, pts[1:]), widths):
        d.line([a, b], fill=fill, width=max(1, int(round(w))))
        r = w / 2
        d.ellipse((b[0] - r, b[1] - r, b[0] + r, b[1] + r), fill=fill)
    return m


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


def edge_fade(size, border):
    """Opaque inside, fading to clear over `border` px at every edge."""
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


# ----------------------------------------------------------------------------------------------------- expedition flag

def flag_sprite(lit, fonts):
    """A swallowtail flag on a wooden pole, painted 33. Lit: flying, glowing finial. Faded: drooping, greyed, torn."""
    S, F = 256, 4
    size = (S * F, S * F)
    rng = random.Random(SEED + 11)

    def P(x, y):
        return (x * F, y * F)

    img = canvas(*size)
    # --- the flag cloth, drawn flat, then shaken (lit) or let droop (faded) column by column
    x0, y0, y1 = 86, 36, 130
    L = 156 if lit else 132
    x1 = x0 + L
    notch = 30
    flat_pts = [P(x0, y0), P(x1, y0), P(x1 - notch, (y0 + y1) / 2), P(x1, y1), P(x0, y1)]
    cloth = poly_mask(size, flat_pts)
    if not lit:
        # a few torn bites along the fly and the lower edge
        tr = random.Random(SEED + 12)
        for (bx, by, s) in [(x1 - 8, y0 + 6, 9), (x0 + 70, y1, 8), (x0 + 104, y1, 6), (x0 + 96, y0, 6)]:
            pts = [P(bx - s, by), P(bx + tr.uniform(-2, 2), by + (s if by < (y0 + y1) / 2 else -s) * tr.uniform(0.9, 1.4)), P(bx + s, by)]
            cloth = ImageChops.subtract(cloth, poly_mask(size, pts))
    vert = linear_light(size, 0)
    body = map_l(vert, [(0, lerp_rgb(FLAG_BLUE, BONE, 0.18)), (0.18, FLAG_BLUE), (0.5, lerp_rgb(FLAG_BLUE, FLAG_DEEP, 0.55)),
                        (1, FLAG_DEEP)]).convert("RGBA")
    flag = clip(body, cloth)
    # horizontal brush grain across the cloth
    grain = canvas(*size)
    for i in range(22):
        yy = rng.uniform(y0 + 4, y1 - 4)
        xa = rng.uniform(x0, x0 + L * 0.5)
        pts = [P(xa + k * rng.uniform(9, 13), yy + rng.uniform(-0.6, 0.6)) for k in range(9)]
        col = rng.choice([lerp_rgb(FLAG_BLUE, BONE, 0.3), FLAG_DEEP, (82, 140, 204)])
        grain = comp(grain, brush_stroke(size, pts, rng.uniform(3, 6) * F, col, rng.randint(50, 110), seed=rng.randrange(1 << 30), bristles=4))
    flag = comp(flag, clip(grain, cloth))
    # crimson hoist band
    hoist = ImageChops.multiply(cloth, poly_mask(size, [P(x0 - 2, y0 - 2), P(x0 + 17, y0 - 2), P(x0 + 17, y1 + 2), P(x0 - 2, y1 + 2)]))
    flag = comp(flag, solid(size, CRIMSON, hoist), solid(size, CRIMSON_DEEP, scale_l(ring_in(hoist, 2 * F), 0.8)))
    # gold trim, a thin bone rule inside it
    flag = comp(flag, gold_paint(size, ring_in(cloth, 3.2 * F), rng, angle=-20, bands=2.0))
    inner = shrink(cloth, 8 * F)
    flag = comp(flag, solid(size, BONE, scale_l(inner_edge(inner, int(1.1 * F)), 0.5)))
    # "33", bone with an ink edge
    fnt = ImageFont.truetype(fonts["black"], 70 * F)
    tl = Image.new("L", size, 0)
    td = ImageDraw.Draw(tl)
    bb = td.textbbox((0, 0), "33", font=fnt)
    tcx, tcy = (x0 + 17 + x1 - notch * 0.7) / 2 * F, (y0 + y1) / 2 * F + 1 * F
    td.text((tcx - (bb[2] - bb[0]) / 2 - bb[0], tcy - (bb[3] - bb[1]) / 2 - bb[1]), "33", font=fnt, fill=255)
    tl = ImageChops.multiply(tl, cloth)
    flag = comp(flag, solid(size, INK, ImageChops.multiply(grow(tl, 3.2 * F), cloth)))
    flag = comp(flag, paper_fill(size, tl, rng, base=BONE, amount=0.25))
    flag = comp(flag, clip(solid(size, PAPER, scale_l(ring_in(tl, 2 * F).filter(ImageFilter.GaussianBlur(F)), 0.7)), tl))

    # Warp: shift every column by dy(x).
    def dy(xd):
        t = max(0.0, xd) / L
        if lit:
            return (7.5 * math.sin(2 * math.pi * t * 1.2 - 0.35) + 2.0) * t ** 0.75
        return 52 * t ** 1.7 + 2.5 * math.sin(2 * math.pi * t * 1.6) * t

    warped = canvas(*size)
    shade = Image.new("L", (size[0], 1), 0)
    light = Image.new("L", (size[0], 1), 0)
    for xs in range(x0 * F - 2 * F, (x1 + 3) * F):
        xd = xs / F - x0
        off = int(round(dy(xd) * F))
        warped.paste(flag.crop((xs, 0, xs + 1, size[1])), (xs, off))
        s = (dy(xd + 1) - dy(xd - 1)) / 2
        v = max(-1.0, min(1.0, s * (3.2 if lit else 1.6)))
        shade.putpixel((xs, 0), int(max(0.0, v) * 120))
        light.putpixel((xs, 0), int(max(0.0, -v) * 90))
    fa = warped.getchannel("A")
    warped = comp(warped, clip(solid(size, INK, shade.resize(size)), fa), clip(solid(size, BONE, light.resize(size)), fa))
    warped = comp(warped, ink_edge(size, fa.point(lambda v: 255 if v > 100 else 0), 2.6 * F))

    # --- ground: a small earth mound with two stones
    mound = rough_disc(size, P(82, 230), 34 * F, random.Random(SEED + 13), wobble=0.05, sy=0.28)
    img = comp(img, solid(size, INK, scale_l(mound.filter(ImageFilter.GaussianBlur(5 * F)), 0.5)))
    mlight = linear_light(size, 90)
    img = comp(img, clip(map_l(mlight, [(0, (112, 92, 74)), (0.5, (82, 64, 54)), (1, (56, 42, 40))]).convert("RGBA"), mound))
    img = comp(img, ink_edge(size, mound, 2.4 * F))

    # --- pole
    pole = poly_mask(size, [P(77, 232), P(83, 232), P(88, 22), P(83, 22)])
    stripe = poly_mask(size, [P(78.2, 230), P(80, 230), P(85, 24), P(83.6, 24)])
    img = comp(img, solid(size, WOOD, pole))
    img = comp(img, solid(size, WOOD_LIGHT, scale_l(stripe, 0.8)),
               solid(size, WOOD_DEEP, scale_l(poly_mask(size, [P(81.6, 232), P(83, 232), P(88, 22), P(86.6, 22)]), 0.9)))
    img = comp(img, ink_edge(size, pole, 2.4 * F))
    img = comp(img, warped)
    # gold ties holding the hoist, the finial
    for yy in (y0 + 6, y1 - 6):
        tie = line_mask(size, [P(79, yy - 2.5), P(91, yy + 2.5)], 5 * F)
        img = comp(img, ink_edge(size, tie, 1.8 * F), gold_paint(size, tie, rng, angle=-40))
    fin = union(poly_mask(size, [P(86, 2), P(80.5, 16), P(86, 23), P(91.5, 16)]), ring_mask(size, P(85.5, 22), 0, 5.2 * F))
    if lit:
        img = comp(img, glow(fin, GOLD, 9 * F, 1.4))
    img = comp(img, ink_edge(size, fin, 2.2 * F), gold_paint(size, fin, rng, angle=-30, bands=1.2))
    img = comp(img, solid(size, GOLD_CORE, scale_l(poly_mask(size, [P(86, 5), P(83, 15), P(86, 13)]), 0.9)))

    if lit:
        a = img.getchannel("A").point(lambda v: 255 if v > 128 else 0)
        halo = canvas(*size)
        halo = comp(halo, glow(a, GOLD, 10 * F, 0.7))
        sp = ImageChops.lighter(star(size, P(86, 12), 20 * F, 255, 0.12), star(size, P(86, 12), 9 * F, 255, 0.3).rotate(45, center=P(86, 12)))
        for (sx, sy_, r) in [(232, 30, 7), (206, 148, 5), (60, 60, 5), (150, 22, 4)]:
            sp = ImageChops.lighter(sp, star(size, P(sx, sy_), r * F, 230))
        img = comp(halo, img, glow(sp, GOLD, 3 * F, 1.4), solid(size, GOLD_CORE, sp))
    else:
        img = fade(desaturate(img, 0.72, dim=0.82, tint=(214, 220, 236)), 0.85)
    return downsample(img, F)


# ------------------------------------------------------------------------------------------------------- Esquie's rocks

ROCKS = [
    # name, body colour, shade colour, mark, mark colour
    ("florrie", (92, 152, 222), (36, 76, 150), "wave", BONE),
    ("dorrie", (244, 142, 128), (176, 70, 78), "coral", BONE),
    ("soarrie", (252, 240, 208), (206, 164, 94), "wing", GOLD),
    ("urrie", (58, 76, 164), (22, 28, 84), "spiral", LIGHT_TEAL),
]
ROCK_STATES = ("sparkle", "hidden", "guarded")


def mark_masks(size, kind, cx, cy, u):
    """The rock's mark as (fill mask, ink detail mask), unit u."""
    m = Image.new("L", size, 0)
    det = Image.new("L", size, 0)
    d = ImageDraw.Draw(m)
    sw = 0.28 * u

    def P(x, y):
        return (cx + x * u, cy + y * u)

    def stroke(pts, w=sw):
        d.line(pts, fill=255, width=int(round(w)), joint="curve")
        for x, y in (pts[0], pts[-1]):
            d.ellipse((x - w / 2, y - w / 2, x + w / 2, y + w / 2), fill=255)

    if kind == "wave":
        for yo, ph in ((-0.36, 0.2), (0.36, 0.2)):
            stroke([P(-0.9 + 1.8 * i / 48, yo + 0.2 * math.sin(i / 48 * 2 * math.pi * 1.25 + ph)) for i in range(49)])
    elif kind == "coral":
        for path in [[(0.04, 0.9), (0.0, 0.4), (-0.06, -0.1), (-0.1, -0.74)], [(0.0, 0.42), (-0.42, 0.12), (-0.6, -0.42)],
                     [(-0.42, 0.12), (-0.86, -0.02)], [(-0.04, 0.06), (0.42, -0.18), (0.5, -0.72)], [(0.42, -0.18), (0.84, -0.34)]]:
            stroke([P(x, y) for x, y in path], sw * 0.95)
        for x, y in [(-0.1, -0.74), (-0.6, -0.42), (-0.86, -0.02), (0.5, -0.72), (0.84, -0.34)]:
            r = sw * 0.72
            px_, py_ = P(x, y)
            d.ellipse((px_ - r, py_ - r, px_ + r, py_ + r), fill=255)
    elif kind == "spiral":
        pts = []
        for i in range(181):
            t = i / 180
            th = t * 2.2 * 2 * math.pi + 0.8
            r = 0.08 + 0.84 * t
            pts.append(P(r * math.cos(th), r * math.sin(th) * 0.94))
        stroke(pts, sw * 0.9)
    elif kind == "wing":
        dd = ImageDraw.Draw(det)
        for sgn in (1, -1):
            def W(x, y):
                return P(sgn * x, y)
            root, tip = (0.1, 0.3), (1.0, -0.66)
            out = curve(W(*root), W(0.24, -0.8), W(*tip), 30)
            notches = [tip, (0.74, -0.3), (0.5, -0.04), (0.28, 0.18), root]
            tips = [(1.02, -0.18), (0.8, 0.16), (0.56, 0.4), (0.3, 0.54)]
            for n0, n1, ft in zip(notches, notches[1:], tips):
                out += curve(W(*n0), W(*ft), W(*n1), 16)[1:]
            d.polygon(out, fill=255)
            for n, e in zip(notches[1:4], [(0.62, -0.62), (0.42, -0.46), (0.26, -0.26)]):
                dd.line([W(*n), W(*e)], fill=255, width=max(1, int(0.06 * u)))
        det = ImageChops.multiply(det, shrink(m, 0.07 * u))
    return m, det


def rock_parts(idx, F, S):
    name, body_rgb, shade_rgb, kind, mark_rgb = ROCKS[idx]
    size = (S * F, S * F)
    C = (S * F / 2, S * F / 2 + 6 * F)
    R = 52 * F
    rr = random.Random(SEED + 100 + idx)
    body = rough_disc(size, C, R, rr, wobble=0.06, jitter=0.004, sy=0.86)
    img = canvas(*size)
    sh = Image.new("L", size, 0)
    ImageDraw.Draw(sh).ellipse((C[0] - 0.92 * R, C[1] + 0.6 * R, C[0] + 1.06 * R, C[1] + 1.0 * R), fill=170)
    img = comp(img, solid(size, INK, sh.filter(ImageFilter.GaussianBlur(4 * F))))
    light = radial_light(size, (C[0] - 0.42 * R, C[1] - 0.48 * R), 2.0 * R, power=0.9)
    fill = map_l(light, [(0, lerp_rgb(shade_rgb, INK, 0.3)), (0.3, shade_rgb), (0.6, body_rgb), (0.84, lerp_rgb(body_rgb, BONE, 0.3)),
                         (1, lerp_rgb(body_rgb, BONE, 0.65))]).convert("RGBA")
    fill.putalpha(body)
    img = comp(img, fill)
    # short dabs that follow the stone's roundness: light on the upper left, shade on the lower right
    dabs = canvas(*size)
    for i in range(16):
        r = rr.uniform(0.35, 0.92) * R
        a0 = rr.uniform(0, 360)
        span = rr.uniform(25, 60)
        pts = [(C[0] + math.cos(math.radians(a)) * r, C[1] + math.sin(math.radians(a)) * r * 0.86) for a in [a0 + span * k / 12 for k in range(13)]]
        lit = math.cos(math.radians(a0 + span / 2 - 225))
        col = lerp_rgb(body_rgb, BONE, 0.5) if lit > 0.2 else lerp_rgb(shade_rgb, INK, 0.25)
        dabs = comp(dabs, brush_stroke(size, pts, rr.uniform(3, 6) * F, col, rr.randint(50, 110), seed=rr.randrange(1 << 30), bristles=4))
    img = comp(img, clip(dabs, body))
    speck_d, speck_l = Image.new("L", size, 0), Image.new("L", size, 0)
    for i in range(46):
        a = rr.uniform(0, 2 * math.pi)
        dist = rr.uniform(0, 0.85) ** 0.6 * R
        x, y = C[0] + math.cos(a) * dist, C[1] + math.sin(a) * dist * 0.86
        r = rr.uniform(0.6, 1.7) * F
        ImageDraw.Draw(speck_d if i % 3 else speck_l).ellipse((x - r, y - r, x + r, y + r), fill=rr.randint(80, 160))
    img = comp(img, clip(solid(size, lerp_rgb(shade_rgb, INK, 0.4), speck_d), body), clip(solid(size, BONE, speck_l), body))
    # lower-right occlusion, glossy upper-left highlight
    cres = ImageChops.subtract(body, shift(body, -0.16 * R, -0.2 * R)).filter(ImageFilter.GaussianBlur(6 * F))
    img = comp(img, clip(solid(size, lerp_rgb(shade_rgb, INK, 0.45), scale_l(cres, 0.6)), body))
    hi = Image.new("L", size, 0)
    ImageDraw.Draw(hi).ellipse((C[0] - 0.66 * R, C[1] - 0.66 * R, C[0] - 0.14 * R, C[1] - 0.36 * R), fill=150)
    img = comp(img, clip(solid(size, BONE, hi.filter(ImageFilter.GaussianBlur(4 * F))), body))
    spec = Image.new("L", size, 0)
    ImageDraw.Draw(spec).ellipse((C[0] - 0.5 * R, C[1] - 0.58 * R, C[0] - 0.36 * R, C[1] - 0.48 * R), fill=235)
    img = comp(img, clip(solid(size, GOLD_CORE, spec.filter(ImageFilter.GaussianBlur(F))), body))
    # Dorrie's break line
    if kind == "coral":
        pts = [(C[0] + math.cos(math.radians(-38)) * R * f + j * R, C[1] + math.sin(math.radians(-38)) * R * f * 0.86 + k * R)
               for f, j, k in [(1.05, 0, 0), (0.86, -0.04, 0.04), (0.76, 0.03, 0.06), (0.6, -0.02, 0.1)]]
        crack = taper_mask(size, pts, [2.6 * F, 2.0 * F, 1.4 * F])
        img = comp(img, clip(solid(size, BONE, scale_l(shift(crack, F, 1.5 * F), 0.6)), body), clip(solid(size, INK, crack), body))
    # the mark
    mk, det = mark_masks(size, kind, C[0], C[1] - 0.04 * R, 0.5 * R)
    img = comp(img, solid(size, INK, grow(mk, 2.4 * F)))
    if kind == "wing":
        img = comp(img, gold_paint(size, mk, rr, angle=-35, bands=1.1))
    else:
        img = comp(img, solid(size, mark_rgb, mk))
        img = comp(img, clip(solid(size, lerp_rgb(mark_rgb, INK, 0.35), scale_l(ImageChops.subtract(mk, shift(mk, -1.5 * F, -1.5 * F)), 0.6)), mk))
    img = comp(img, solid(size, INK, det))
    img = comp(img, ink_edge(size, body, 3 * F, heavy=1.5 * F))
    return img, body, C, R


def chain_link(size, x, y, th, a, b, thick, face, F):
    ca, sa = math.cos(th), math.sin(th)
    if face:
        def oval(aa, bb):
            return [(x + aa * math.cos(p) * ca - bb * math.sin(p) * sa, y + aa * math.cos(p) * sa + bb * math.sin(p) * ca)
                    for p in [i / 36 * 2 * math.pi for i in range(36)]]
        m = ImageChops.subtract(poly_mask(size, oval(a, b)), poly_mask(size, oval(a - thick, b - thick)))
    else:
        m = line_mask(size, [(x - (a - thick * 0.6) * ca, y - (a - thick * 0.6) * sa), (x + (a - thick * 0.6) * ca, y + (a - thick * 0.6) * sa)],
                      thick * 1.6)
    out = comp(canvas(*size), ink_edge(size, m, 1.6 * F), solid(size, IRON, m))
    hl = ImageChops.subtract(m, shift(m, 1.6 * F, 1.8 * F))
    return comp(out, solid(size, IRON_LIGHT, scale_l(hl, 0.85)))


def chain_layers(size, C, R, F):
    """A loop of chain round the rock (back half, front half) with a small padlock hanging at the front."""
    cx, cy = C[0], C[1] + 0.22 * R
    rx, ry, tilt = 1.1 * R, 0.32 * R, math.radians(-8)
    n = 18
    links = []
    for k in range(n):
        t = 2 * math.pi * k / n + 0.09
        ex, ey = rx * math.cos(t), ry * math.sin(t)
        x = cx + ex * math.cos(tilt) - ey * math.sin(tilt)
        y = cy + ex * math.sin(tilt) + ey * math.cos(tilt)
        tx, ty = -rx * math.sin(t), ry * math.cos(t)
        th = math.atan2(tx * math.sin(tilt) + ty * math.cos(tilt), tx * math.cos(tilt) - ty * math.sin(tilt))
        links.append((math.sin(t), x, y, th, k % 2 == 0))
    back, front = canvas(*size), canvas(*size)
    for depth, x, y, th, face in sorted(links):
        link = chain_link(size, x, y, th, 0.2 * R, 0.12 * R, 0.07 * R, face, F)
        if depth < 0:
            back = comp(back, desaturate(link, 0.3, dim=0.6))
        else:
            front = comp(front, link)
    # padlock below the frontmost link
    lx, ly = cx - ry * math.sin(tilt), cy + ry * math.cos(tilt)
    w, h = 0.42 * R, 0.34 * R
    by = ly + 0.3 * R
    shackle = ImageChops.subtract(ring_mask(size, (lx, by - h * 0.32), 0.1 * R, 0.17 * R),
                                  poly_mask(size, [(lx - R, by - h * 0.32), (lx + R, by - h * 0.32), (lx + R, by + R), (lx - R, by + R)]))
    front = comp(front, ink_edge(size, shackle, 1.6 * F), solid(size, IRON, shackle))
    lock = Image.new("L", size, 0)
    ImageDraw.Draw(lock).rounded_rectangle((lx - w / 2, by - h / 2, lx + w / 2, by + h / 2), radius=0.08 * R, fill=255)
    front = comp(front, ink_edge(size, lock, 1.8 * F), gold_paint(size, lock, random.Random(SEED + 7), angle=-35, bands=1.0))
    kh = Image.new("L", size, 0)
    kd = ImageDraw.Draw(kh)
    kd.ellipse((lx - 0.05 * R, by - 0.09 * R, lx + 0.05 * R, by + 0.01 * R), fill=255)
    kd.polygon([(lx - 0.03 * R, by - 0.02 * R), (lx + 0.03 * R, by - 0.02 * R), (lx + 0.045 * R, by + 0.1 * R), (lx - 0.045 * R, by + 0.1 * R)], fill=255)
    front = comp(front, solid(size, INK, kh))
    return back, front


def rock_sheet():
    S, F = 160, 4
    size = (S * F, S * F)
    sheet = canvas(S * 4, S * 3)
    sprites = []
    for idx, spec in enumerate(ROCKS):
        img, body, C, R = rock_parts(idx, F, S)
        rng = random.Random(SEED + 200 + idx)
        for row, state in enumerate(ROCK_STATES):
            if state == "sparkle":
                out = comp(canvas(*size), glow(body, GOLD, 11 * F, 1.0), glow(body, lerp_rgb(spec[1], GOLD_CORE, 0.4), 5 * F, 0.8), img)
                sp = Image.new("L", size, 0)
                for (dx, dy_, r) in [(0.98, -0.82, 12), (-1.02, -0.4, 8), (0.86, 0.56, 7), (-0.4, -0.5, 7)]:
                    sp = ImageChops.lighter(sp, star(size, (C[0] + dx * R, C[1] + dy_ * R), r * F * rng.uniform(0.9, 1.1)))
                motes = Image.new("L", size, 0)
                for i in range(7):
                    a = rng.uniform(0, 2 * math.pi)
                    dist = rng.uniform(1.12, 1.4) * R
                    x, y = C[0] + math.cos(a) * dist, C[1] + math.sin(a) * dist * 0.8
                    r = rng.uniform(1.2, 2.2) * F
                    ImageDraw.Draw(motes).ellipse((x - r, y - r, x + r, y + r), fill=230)
                sp = ImageChops.lighter(sp, motes)
                out = comp(out, glow(sp, GOLD, 3 * F, 1.5), solid(size, GOLD_CORE, sp))
            elif state == "hidden":
                out = fade(desaturate(img, 0.75, dim=0.6, tint=(200, 204, 222)), 0.55)
            else:
                back, front = chain_layers(size, C, R, F)
                out = comp(canvas(*size), back, img, front)
            cell = downsample(out, F)
            x0, y0 = idx * S, row * S
            sheet.alpha_composite(cell, (x0, y0))
            sprites.append({"name": f"{spec[0]}-{state}", "box": [x0, y0, x0 + S, y0 + S]})
    return sheet, sprites


# ------------------------------------------------------------------------------------------------------------- badges

def badge(kind):
    """A round gold-rimmed badge with an ink silhouette on a painted field."""
    S, F = 256, 4
    size = (S * F, S * F)
    C = (S * F / 2, S * F / 2)
    rng = random.Random(SEED + (300 if kind == "lampmaster" else 400))

    def P(x, y):
        return (C[0] + x * F, C[1] + y * F)

    outer = rough_disc(size, C, 118 * F, rng, wobble=0.008, jitter=0.0015)
    field = rough_disc(size, C, 97 * F, rng, wobble=0.006, jitter=0.0015)
    img = canvas(*size)
    img = comp(img, solid(size, (0, 0, 0), scale_l(shift(outer, 0, 3 * F).filter(ImageFilter.GaussianBlur(5 * F)), 0.5)))
    img = comp(img, ink_edge(size, outer, 3 * F))
    light = conic_light(size, C, lambda deg: 128 + 127 * math.cos(math.radians(deg - 225)))
    img = comp(img, gold_paint(size, outer, rng, light=light, bands=1.0))
    img = comp(img, solid(size, GOLD_DEEP, scale_l(ring_mask(size, C, 108 * F, 110 * F), 0.8)))
    img = comp(img, solid(size, GOLD_CORE, scale_l(ring_mask(size, (C[0] - 0.6 * F, C[1] - 0.8 * F), 110.5 * F, 111.5 * F), 0.5)))
    for k in range(4):
        a = math.radians(45 + 90 * k)
        x, y = C[0] + math.cos(a) * 109 * F, C[1] + math.sin(a) * 109 * F
        stud = ring_mask(size, (x, y), 0, 4.2 * F)
        img = comp(img, ink_edge(size, stud, 1.5 * F), solid(size, CRIMSON, stud),
                   solid(size, (236, 120, 130), scale_l(ring_mask(size, (x - 1.2 * F, y - 1.2 * F), 0, 1.6 * F), 0.9)))
    img = comp(img, ink_edge(size, field, 3 * F))

    if kind == "lampmaster":
        img = comp(img, paper_fill(size, field, rng, base=(240, 228, 204), amount=0.22))
        img = comp(img, clip(solid(size, DUSK, linear_light(size, 90).point(lambda v: int(max(0, v - 120) * 0.55))), field))
        lantern_c = P(0, -54)
        warm = radial_light(size, lantern_c, 130 * F, power=1.2)
        img = comp(img, clip(solid(size, (238, 166, 76), scale_l(warm, 0.9)), field))
        rays = Image.new("L", size, 0)
        rd = ImageDraw.Draw(rays)
        for k in range(10):
            a = math.radians(k * 36 + 8)
            rd.polygon([lantern_c, (lantern_c[0] + math.cos(a - 0.07) * 200 * F, lantern_c[1] + math.sin(a - 0.07) * 200 * F),
                        (lantern_c[0] + math.cos(a + 0.07) * 200 * F, lantern_c[1] + math.sin(a + 0.07) * 200 * F)], fill=60)
        img = comp(img, clip(solid(size, GOLD_CORE, ImageChops.multiply(rays.filter(ImageFilter.GaussianBlur(4 * F)), warm)), field))

        fig = Image.new("L", size, 0)
        d = ImageDraw.Draw(fig)
        robe = curve(P(-24, -22), P(-40, 18), P(-54, 104)) + [P(54, 104)] + curve(P(50, 104), P(38, 18), P(24, -22))
        d.polygon(robe, fill=255)
        d.polygon([P(-30, -16), P(-27, -42), P(-15, -27), P(-7, -31), P(7, -31), P(15, -27), P(27, -42), P(30, -16)], fill=255)
        d.rectangle((*P(-6, -34), *P(6, -18)), fill=255)
        # sleeve reaching to the staff, and the staff with a crook
        d.polygon([P(22, -18), P(36, -14), P(62, 18), P(56, 30), P(40, 22)], fill=255)
        fig = union(fig, line_mask(size, [P(63, -60), P(65, 104)], 4.5 * F))
        fig = union(fig, line_mask(size, curve(P(63, -60), P(63, -84), P(50, -82), 12) + curve(P(50, -82), P(42, -80), P(44, -70), 8), 4.2 * F))
        d = ImageDraw.Draw(fig)
        d.ellipse((*P(55, 18), *P(68, 32)), fill=255)
        # the lantern head: handle ring, cap, frame, base
        fig = union(fig, ring_mask(size, P(0, -94), 3.6 * F, 7.4 * F))
        d = ImageDraw.Draw(fig)
        d.polygon([P(-19, -74), P(0, -90), P(19, -74)], fill=255)
        d.rounded_rectangle((*P(-21, -77), *P(21, -70)), radius=2 * F, fill=255)
        d.rounded_rectangle((*P(-19, -38), *P(19, -31)), radius=2 * F, fill=255)
        d.rounded_rectangle((*P(-16, -72), *P(16, -35)), radius=4 * F, fill=255)
        glass = Image.new("L", size, 0)
        ImageDraw.Draw(glass).rounded_rectangle((*P(-12.5, -69), *P(12.5, -39)), radius=3 * F, fill=255)
        img = comp(img, glow(glass, (240, 150, 60), 26 * F, 1.5), glow(glass, GOLD, 12 * F, 1.4), glow(glass, GOLD_CORE, 6 * F, 1.2))
        fig = ImageChops.multiply(fig, field)
        img = comp(img, solid(size, INK, fig))
        # the lantern's halo spills over the hood and shoulders, so the head reads as a light at token size
        halo = ImageChops.multiply(glow(glass, GOLD, 16 * F, 2.2).getchannel("A"), field)
        img = comp(img, solid(size, (250, 206, 120), scale_l(halo, 0.75)))
        # lamplight catching the top edges of the shoulders and collar
        rim = ImageChops.multiply(ImageChops.subtract(fig, shift(fig, 0, 2.5 * F)), radial_light(size, lantern_c, 80 * F, power=0.8))
        img = comp(img, solid(size, GOLD, rim))
        folds = union(line_mask(size, [P(-14, 26), P(-24, 104)], 2 * F), line_mask(size, [P(12, 26), P(20, 104)], 2 * F))
        img = comp(img, clip(solid(size, INK_SOFT, scale_l(folds, 0.9)), field))
        gl = map_l(radial_light(size, P(0, -54), 18 * F, power=0.7), [(0, GOLD), (0.6, (252, 224, 150)), (1, GOLD_CORE)]).convert("RGBA")
        gl.putalpha(glass)
        img = comp(img, gl)
        flame = poly_mask(size, curve(P(0, -66), P(-9, -50), P(0, -45), 12) + curve(P(0, -45), P(9, -50), P(0, -66), 12))
        img = comp(img, solid(size, (255, 255, 246), flame.filter(ImageFilter.GaussianBlur(1.2 * F))))
        bars = union(line_mask(size, [P(-5, -70), P(-5, -37)], 2.4 * F, caps=False), line_mask(size, [P(5, -70), P(5, -37)], 2.4 * F, caps=False),
                     line_mask(size, [P(-13, -54), P(-9, -54)], 2.2 * F, caps=False), line_mask(size, [P(9, -54), P(13, -54)], 2.2 * F, caps=False))
        img = comp(img, solid(size, INK, ImageChops.multiply(bars, glass)))
    else:
        base = (206, 230, 220)
        img = comp(img, paper_fill(size, field, rng, base=base, amount=0.22))
        img = comp(img, clip(solid(size, LIGHT_TEAL, scale_l(radial_light(size, P(-30, -50), 100 * F), 0.4)), field))
        ground = canvas(*size)
        for k, yy in enumerate((60, 70, 80)):
            pts = [P(-96 + i * 12, yy + 2 * math.sin(i * 0.9 + k)) for i in range(17)]
            ground = comp(ground, brush_stroke(size, pts, (5 - k) * F, TEAL_DEEP, 110 - k * 25, seed=SEED + 40 + k, bristles=4))
        img = comp(img, clip(ground, field))
        ox, oy = -4, 2

        def Q(x, y):
            return P(x + ox, y + oy)

        fig = Image.new("L", size, 0)
        d = ImageDraw.Draw(fig)
        sc = (16, 16)
        shell = [Q(sc[0] + 62 * math.cos(math.radians(a)), sc[1] + 54 * math.sin(math.radians(a))) for a in range(180, 361, 4)]
        d.polygon(shell, fill=255)
        d.rounded_rectangle((*Q(-52, 10), *Q(84, 28)), radius=8 * F, fill=255)
        d.ellipse((*Q(-92, -18), *Q(-46, 22)), fill=255)
        d.polygon([Q(-60, -8), Q(-38, -2), Q(-38, 24), Q(-62, 18)], fill=255)
        for lx in (-36, 46):
            d.rounded_rectangle((*Q(lx - 2, 16), *Q(lx + 24, 44)), radius=10 * F, fill=255)
            d.ellipse((*Q(lx - 7, 36), *Q(lx + 28, 52)), fill=255)
        d.polygon([Q(82, 14), Q(104, 26), Q(82, 28)], fill=255)
        # grumble scribble over the head
        scrib = [Q(-78 + 10 * t / 6.28 + 5 * math.cos(t * 1.0 + 3.14), -40 + 5 * math.sin(t * 1.0 + 3.14)) for t in [i / 40 * 6.28 * 3 for i in range(41)]]
        fig = union(fig, line_mask(size, scrib, 2.6 * F))
        fig = ImageChops.multiply(fig, field)
        img = comp(img, solid(size, INK, fig))
        # carved lines in the field colour: shell rim, scutes, brow, lidded eye, frown
        carve = Image.new("L", size, 0)
        cd = ImageDraw.Draw(carve)
        cd.line([Q(-44, 9), Q(78, 9)], fill=255, width=int(3.2 * F))
        hexc = (sc[0], -14)
        hexv = [Q(hexc[0] + 20 * math.cos(math.radians(30 + 60 * k)), hexc[1] + 15 * math.sin(math.radians(30 + 60 * k))) for k in range(6)]
        cd.line(hexv + [hexv[0]], fill=255, width=int(3.2 * F), joint="curve")
        for k in (3, 4, 5, 0, 2):
            a = math.radians(30 + 60 * k)
            vx, vy = hexc[0] + 20 * math.cos(a), hexc[1] + 15 * math.sin(a)
            ex, ey = sc[0] + 70 * math.cos(a), max(-44, min(9, sc[1] + 64 * math.sin(a)))
            cd.line([Q(vx, vy), Q(ex, ey)], fill=255, width=int(3.2 * F))
        for lx in (-26, 62):
            cd.line([Q(lx, -28), Q(lx + (8 if lx < 0 else -8), 9)], fill=255, width=int(2.6 * F))
        for lx in (-36, 46):
            for tx in (lx + 4, lx + 12, lx + 20):
                cd.line([Q(tx, 46), Q(tx, 53)], fill=255, width=int(2.4 * F))
        cd.ellipse((*Q(-80, -9), *Q(-66, 5)), fill=255)
        cd.line([Q(-88, -9), Q(-60, -16)], fill=255, width=int(3.8 * F))
        cd.line(curve(Q(-91, 11), Q(-82, 6), Q(-72, 12), 10), fill=255, width=int(2.6 * F), joint="curve")
        lid = poly_mask(size, [Q(-84, -14), Q(-62, -18), Q(-62, -2), Q(-84, 1)])
        carve = ImageChops.subtract(carve, ImageChops.multiply(lid, ring_mask(size, Q(-73, -2), 0, 7 * F)))
        cd = ImageDraw.Draw(carve)
        cd.line([Q(-88, -9), Q(-60, -16)], fill=255, width=int(3.8 * F))
        carve = ImageChops.multiply(ImageChops.multiply(carve, fig), field)
        img = comp(img, paper_fill(size, carve, rng, base=base, amount=0.22))
        dot = ring_mask(size, Q(-74, 1), 0, 2.6 * F)
        img = comp(img, solid(size, INK, dot))
    img = comp(img, clip(solid(size, INK, scale_l(ring_in(field, 10 * F).filter(ImageFilter.GaussianBlur(5 * F)), 0.45)), field))
    return downsample(img, F)


# --------------------------------------------------------------------------------------------------- easel and frames

PW, PH, PF = 512, 720, 2
FRAME_BOX = (64, 44, 448, 524)
WINDOW_BOX = (108, 88, 404, 480)


def wood_piece(size, mask, rng, F, angle):
    """A painted wooden member: warm base, grain strokes along `angle`, ink edge."""
    out = solid(size, WOOD, mask)
    grain = canvas(*size)
    bb = mask.getbbox()
    if not bb:
        return out
    cx, cy = (bb[0] + bb[2]) / 2, (bb[1] + bb[3]) / 2
    length = math.hypot(bb[2] - bb[0], bb[3] - bb[1])
    ca, sa = math.cos(angle), math.sin(angle)
    for i in range(10):
        off = rng.uniform(-0.5, 0.5) * min(bb[2] - bb[0], bb[3] - bb[1]) + rng.uniform(-6, 6) * F
        pts = [(cx + ca * length * (t - 0.5) - sa * off, cy + sa * length * (t - 0.5) + ca * off) for t in [k / 10 for k in range(11)]]
        col = rng.choice([WOOD_DEEP, WOOD_LIGHT, WOOD_DEEP])
        grain = comp(grain, brush_stroke(size, pts, rng.uniform(1.5, 3.5) * F, col, rng.randint(60, 120), seed=rng.randrange(1 << 30), bristles=3))
    out = comp(out, clip(grain, mask))
    out = comp(out, solid(size, WOOD_LIGHT, scale_l(ImageChops.subtract(mask, shift(mask, 2 * F, 1 * F)), 0.7)))
    out = comp(out, solid(size, WOOD_DEEP, scale_l(ImageChops.subtract(mask, shift(mask, -2 * F, -1 * F)), 0.8)))
    return comp(out, ink_edge(size, mask, 2.2 * F))


def easel_back(rng):
    """The easel legs and mast, drawn behind the frame."""
    F = PF
    size = (PW * F, PH * F)

    def P(x, y):
        return (x * F, y * F)

    img = canvas(*size)
    floor = Image.new("L", size, 0)
    ImageDraw.Draw(floor).ellipse((*P(60, 696), *P(452, 716)), fill=140)
    img = comp(img, solid(size, INK, floor.filter(ImageFilter.GaussianBlur(6 * F))))
    mast = poly_mask(size, [P(249, 6), P(263, 6), P(266, 706), P(246, 706)])
    img = comp(img, desaturate(wood_piece(size, mast, rng, F, math.pi / 2), 0.2, dim=0.72))
    for top, bot in (((240, 14), (72, 710)), ((272, 14), (440, 710))):
        ang = math.atan2(bot[1] - top[1], bot[0] - top[0])
        nx, ny = -math.sin(ang) * 8, math.cos(ang) * 8
        leg = poly_mask(size, [P(top[0] - nx, top[1] - ny), P(top[0] + nx, top[1] + ny), P(bot[0] + nx * 1.2, bot[1] + ny * 1.2),
                               P(bot[0] - nx * 1.2, bot[1] - ny * 1.2)])
        img = comp(img, wood_piece(size, leg, rng, F, ang))
    cross = poly_mask(size, [P(120, 626), P(392, 626), P(396, 640), P(116, 640)])
    img = comp(img, wood_piece(size, cross, rng, F, 0))
    return img


def easel_front(rng):
    """The ledge the frame rests on and the clamp at its top."""
    F = PF
    size = (PW * F, PH * F)

    def P(x, y):
        return (x * F, y * F)

    img = canvas(*size)
    ledge = Image.new("L", size, 0)
    ImageDraw.Draw(ledge).rounded_rectangle((*P(44, 514), *P(468, 542)), radius=4 * F, fill=255)
    img = comp(img, solid(size, INK, scale_l(shift(ledge, 0, 4 * F).filter(ImageFilter.GaussianBlur(4 * F)), 0.5)))
    img = comp(img, wood_piece(size, ledge, rng, F, 0))
    lip = Image.new("L", size, 0)
    ImageDraw.Draw(lip).rectangle((*P(46, 516), *P(466, 520)), fill=255)
    img = comp(img, clip(solid(size, WOOD_LIGHT, scale_l(lip, 0.7)), ledge))
    clamp = Image.new("L", size, 0)
    ImageDraw.Draw(clamp).rounded_rectangle((*P(232, 26), *P(280, 52)), radius=3 * F, fill=255)
    img = comp(img, solid(size, INK, scale_l(shift(clamp, 0, 3 * F).filter(ImageFilter.GaussianBlur(3 * F)), 0.5)))
    img = comp(img, wood_piece(size, clamp, rng, F, 0))
    for x in (242, 270):
        peg = ring_mask(size, P(x, 39), 0, 3.4 * F)
        img = comp(img, ink_edge(size, peg, 1.2 * F), gold_paint(size, peg, rng, angle=-30))
    return img


def gilded_frame(rng):
    """The gilded frame with a clear window, drawn alone."""
    F = PF
    size = (PW * F, PH * F)

    def box_mask(box, inset, radius):
        x0, y0, x1, y1 = box
        m = Image.new("L", size, 0)
        ImageDraw.Draw(m).rounded_rectangle(((x0 + inset) * F, (y0 + inset) * F, (x1 - inset) * F, (y1 - inset) * F),
                                            radius=max(1, radius - inset) * F, fill=255)
        return m

    outer = box_mask(FRAME_BOX, 0, 12)
    win = box_mask(WINDOW_BOX, 0, 4)
    frame = ImageChops.subtract(outer, win)
    cx, cy = (FRAME_BOX[0] + FRAME_BOX[2]) / 2 * F, (FRAME_BOX[1] + FRAME_BOX[3]) / 2 * F
    hw, hh = (FRAME_BOX[2] - FRAME_BOX[0]) / 2 * F, (FRAME_BOX[3] - FRAME_BOX[1]) / 2 * F
    # which half of the frame faces the light (upper left)
    tl = Image.new("L", size, 0)
    ImageDraw.Draw(tl).polygon([(cx - hw - 40, cy + hh + 40), (cx - hw - 40, cy - hh - 40), (cx + hw + 40, cy - hh - 40)], fill=255)
    tl = tl.filter(ImageFilter.GaussianBlur(40 * F))
    br = ImageChops.invert(tl)
    img = canvas(*size)
    img = comp(img, solid(size, (0, 0, 0), ImageChops.multiply(scale_l(shift(outer, 4 * F, 6 * F).filter(ImageFilter.GaussianBlur(8 * F)), 0.6),
                                                          ImageChops.invert(win))))
    img = comp(img, ink_edge(size, outer, 2.6 * F))
    light = linear_light(size, -35)
    img = comp(img, gold_paint(size, frame, rng, light=light, bands=2.2))

    def band(i0, i1):
        return ImageChops.multiply(ImageChops.subtract(box_mask(FRAME_BOX, i0, 12), box_mask(FRAME_BOX, i1, 12)), frame)

    bevel = band(0, 7)
    img = comp(img, solid(size, GOLD_CORE, scale_l(ImageChops.multiply(bevel, tl), 0.55)),
               solid(size, GOLD_DEEP, scale_l(ImageChops.multiply(bevel, br), 0.7)))
    cove = band(7, 19)
    img = comp(img, solid(size, GOLD_DEEP, scale_l(cove, 0.55)), solid(size, INK, scale_l(ImageChops.multiply(cove, tl), 0.3)),
               solid(size, GOLD, scale_l(ImageChops.multiply(cove, br), 0.35)))
    groove = band(19, 27)
    img = comp(img, solid(size, (96, 66, 30), scale_l(groove, 0.85)))
    flat = band(27, 38)
    img = comp(img, solid(size, GOLD_CORE, scale_l(ImageChops.multiply(flat, tl), 0.25)))
    sight = band(38, 44)
    img = comp(img, solid(size, GOLD_DEEP, scale_l(sight, 0.7)), solid(size, INK, scale_l(ImageChops.multiply(sight, tl), 0.35)))
    for i in (7, 19, 27, 38):
        img = comp(img, solid(size, INK, scale_l(ImageChops.multiply(inner_edge(box_mask(FRAME_BOX, i, 12), int(0.8 * F)), frame), 0.45)))
    # a row of gold beads in the groove
    beads = Image.new("L", size, 0)
    bd = ImageDraw.Draw(beads)
    x0, y0, x1, y1 = [v + s for v, s in zip(FRAME_BOX, (23, 23, -23, -23))]
    per = 2 * ((x1 - x0) + (y1 - y0))
    n = int(per / 9.2)
    for k in range(n):
        t = k / n * per
        if t < x1 - x0:
            px_, py_ = x0 + t, y0
        elif t < (x1 - x0) + (y1 - y0):
            px_, py_ = x1, y0 + t - (x1 - x0)
        elif t < 2 * (x1 - x0) + (y1 - y0):
            px_, py_ = x1 - (t - (x1 - x0) - (y1 - y0)), y1
        else:
            px_, py_ = x0, y1 - (t - 2 * (x1 - x0) - (y1 - y0))
        r = 3.1
        bd.ellipse(((px_ - r) * F, (py_ - r) * F, (px_ + r) * F, (py_ + r) * F), fill=255)
    img = comp(img, solid(size, INK, scale_l(shift(beads, 0.8 * F, 1.2 * F), 0.6)), gold_paint(size, beads, rng, angle=-45, bands=0.9))
    bhl = ImageChops.subtract(beads, shift(beads, 1.4 * F, 1.4 * F))
    img = comp(img, solid(size, GOLD_CORE, scale_l(bhl, 0.9)))
    # corner rosettes
    for (ax, ay) in [(FRAME_BOX[0] + 14, FRAME_BOX[1] + 14), (FRAME_BOX[2] - 14, FRAME_BOX[1] + 14),
                     (FRAME_BOX[0] + 14, FRAME_BOX[3] - 14), (FRAME_BOX[2] - 14, FRAME_BOX[3] - 14)]:
        ros = Image.new("L", size, 0)
        rd = ImageDraw.Draw(ros)
        for k in range(6):
            a = math.radians(k * 60 + 15)
            px_, py_ = ax + math.cos(a) * 10, ay + math.sin(a) * 10
            rd.ellipse(((px_ - 7) * F, (py_ - 7) * F, (px_ + 7) * F, (py_ + 7) * F), fill=255)
        rd.ellipse(((ax - 9) * F, (ay - 9) * F, (ax + 9) * F, (ay + 9) * F), fill=255)
        img = comp(img, solid(size, (0, 0, 0), scale_l(shift(ros, 1.5 * F, 2 * F).filter(ImageFilter.GaussianBlur(2 * F)), 0.6)))
        img = comp(img, ink_edge(size, ros, 1.8 * F), gold_paint(size, ros, rng, light=linear_light(size, -45), bands=1.4))
        boss = ring_mask(size, (ax * F, ay * F), 0, 5.5 * F)
        img = comp(img, ink_edge(size, boss, 1.2 * F), solid(size, CRIMSON, boss),
                   solid(size, (236, 120, 130), ring_mask(size, ((ax - 1.6) * F, (ay - 1.6) * F), 0, 2 * F)))
    # a soft shadow falling just inside the window, so a picture sits back in the frame
    inner_sh = ImageChops.multiply(ring_in(win, 10 * F).filter(ImageFilter.GaussianBlur(4 * F)), win)
    img = comp(img, solid(size, INK, scale_l(inner_sh, 0.45)))
    img = comp(img, solid(size, INK, ImageChops.multiply(ring_out(win, 1.6 * F), frame)))
    return img, win


def portrait_frame():
    rng = random.Random(SEED + 500)
    back = easel_back(rng)
    frame, win = gilded_frame(rng)
    front = easel_front(rng)
    # the mast behind the window is cut away, so the picture window stays clear but for the frame's inner shadow
    return downsample(comp(clip(back, ImageChops.invert(win)), frame, front), PF)


def portrait_veiled():
    rng = random.Random(SEED + 500)
    back = easel_back(rng)
    frame, win = gilded_frame(rng)
    front = easel_front(rng)
    F = PF
    size = (PW * F, PH * F)

    def P(x, y):
        return (x * F, y * F)

    crng = random.Random(SEED + 600)
    # the cloth: over the frame's top, hanging lower at the corners, lifting in the middle
    top = curve(P(48, 82), P(50, 32), P(104, 30), 16) + [P(256, 26)] + curve(P(408, 30), P(462, 32), P(464, 82), 16)
    right = curve(P(464, 82), P(474, 320), P(482, 572), 24)
    hem = []
    for i in range(61):
        t = i / 60
        x = 482 - (482 - 30) * t
        base = 572 - 72 * math.sin(math.pi * t) ** 0.9
        hem.append(P(x, base + 7 * math.sin(t * math.pi * 7.0) * math.sin(math.pi * t)))
    left = curve(P(30, 572), P(40, 320), P(48, 82), 24)
    cloth = poly_mask(size, top + right[1:] + hem[1:] + left[1:])
    vert = linear_light(size, 90)
    body = map_l(vert, [(0, (204, 44, 62)), (0.3, CRIMSON), (0.8, (140, 16, 38)), (1, CRIMSON_DEEP)]).convert("RGBA")
    drape = clip(body, cloth)
    folds_d, folds_l = canvas(*size), canvas(*size)
    xs = [70, 112, 150, 196, 236, 280, 322, 364, 404, 442]
    for i, xt in enumerate(xs):
        xb = xt + (xt - 256) * 0.14 + crng.uniform(-8, 8)
        mid = ((xt + xb) / 2 + crng.uniform(-10, 10), 300)
        pts = curve(P(xt, 70), P(*mid), P(xb, 580), 30)
        if i % 2 == 0:
            folds_d = comp(folds_d, brush_stroke(size, pts, crng.uniform(16, 26) * F, CRIMSON_DEEP, crng.randint(150, 200), seed=crng.randrange(1 << 30), bristles=6))
        else:
            folds_l = comp(folds_l, brush_stroke(size, pts, crng.uniform(8, 13) * F, (222, 76, 92), crng.randint(110, 160), seed=crng.randrange(1 << 30), bristles=5))
    drape = comp(drape, clip(folds_d.filter(ImageFilter.GaussianBlur(6 * F)), cloth), clip(folds_l.filter(ImageFilter.GaussianBlur(4 * F)), cloth))
    # light rolling over the top edge and the two top corners
    roll = Image.new("L", size, 0)
    ImageDraw.Draw(roll).line(curve(P(60, 56), P(256, 40), P(452, 56), 30), fill=170, width=int(14 * F), joint="curve")
    for (x, y) in ((70, 58), (442, 58)):
        roll = ImageChops.lighter(roll, radial_light(size, P(x, y), 30 * F))
    drape = comp(drape, clip(solid(size, (236, 116, 124), roll.filter(ImageFilter.GaussianBlur(7 * F))), cloth))
    drape = comp(drape, clip(solid(size, INK, scale_l(ring_in(cloth, 30 * F).filter(ImageFilter.GaussianBlur(14 * F)), 0.35)), cloth))
    # a small embroidered gold emblem
    emb = union(ring_mask(size, P(256, 250), 22 * F, 25.5 * F), poly_mask(size, [P(256, 232), P(268, 250), P(256, 268), P(244, 250)]))
    drape = comp(drape, ink_edge(size, emb, 1.4 * F, rgb=CRIMSON_DEEP), gold_paint(size, emb, crng, angle=-30, bands=1.2))
    # gold braid along the hem, a fringe of tassels below it
    braid = line_mask(size, [(x, y - 7 * F) for x, y in hem], 5 * F)
    braid = ImageChops.multiply(braid, cloth)
    fringe = Image.new("L", size, 0)
    fd = ImageDraw.Draw(fringe)
    for (x, y) in hem[1:-1]:
        for dxx in (-3 * F, 0, 3 * F):
            fd.line([(x + dxx, y - 2 * F), (x + dxx + crng.uniform(-1, 1) * F, y + crng.uniform(9, 13) * F)], fill=255, width=int(2 * F))
    img = comp(back, frame, front)
    img = comp(img, solid(size, (0, 0, 0), scale_l(shift(cloth, 5 * F, 7 * F).filter(ImageFilter.GaussianBlur(9 * F)), 0.55)))
    img = comp(img, ink_edge(size, cloth, 2.6 * F), drape)
    img = comp(img, ink_edge(size, fringe, 1.2 * F), gold_paint(size, fringe, crng, angle=-60, bands=1.4))
    img = comp(img, ink_edge(size, braid, 1.2 * F), gold_paint(size, braid, crng, angle=-20, bands=2.5))
    # the mast's head and the clamp stay in front of the cloth
    cap = Image.new("L", size, 0)
    ImageDraw.Draw(cap).rectangle((*P(230, 0), *P(282, 54)), fill=255)
    img = comp(img, clip(comp(back, front), cap))
    return downsample(img, F)


# ---------------------------------------------------------------------------------------------------------- the crack

def crack_paths(rng):
    pts = [(48.0, 38.0), (212.0, 218.0)]
    amp = 22.0
    for level in range(6):
        out = [pts[0]]
        for a, b in zip(pts, pts[1:]):
            mx, my = (a[0] + b[0]) / 2, (a[1] + b[1]) / 2
            dx, dy = b[0] - a[0], b[1] - a[1]
            ln = math.hypot(dx, dy) or 1
            o = rng.uniform(-amp, amp)
            out += [(mx - dy / ln * o, my + dx / ln * o), b]
        pts = out
        amp *= 0.5
    branches = []
    for t, sgn, ln in [(0.2, 1, 34), (0.34, -1, 44), (0.5, 1, 52), (0.63, -1, 36), (0.78, 1, 30), (0.44, -1, 22)]:
        i = int(t * (len(pts) - 1))
        (ax, ay), (bx, by) = pts[i], pts[i + 1]
        base_a = math.atan2(by - ay, bx - ax) + sgn * math.radians(rng.uniform(38, 66))
        b = [pts[i]]
        x, y, a = pts[i][0], pts[i][1], base_a
        for k in range(8):
            a += rng.uniform(-0.45, 0.45)
            x += math.cos(a) * ln / 8
            y += math.sin(a) * ln / 8
            b.append((x, y))
        branches.append(b)
        if ln > 40:
            j = 4
            sub = [b[j]]
            x, y = b[j]
            a = base_a - sgn * 0.9
            for k in range(5):
                a += rng.uniform(-0.4, 0.4)
                x += math.cos(a) * 3.6
                y += math.sin(a) * 3.6
                sub.append((x, y))
            branches.append(sub)
    return pts, branches


def crack_sprite(opened):
    S, F = 256, 4
    size = (S * F, S * F)
    rng = random.Random(SEED + 700)
    main, branches = crack_paths(rng)
    M = [(x * F, y * F) for x, y in main]
    n = len(M) - 1
    img = canvas(*size)
    if not opened:
        # thick enough to read at token size: a dark seam with a pale lip on its lower side and a faint dust halo
        widths = [(1.6 + 4.6 * math.sin(math.pi * (i + 0.5) / n) ** 0.8) * F for i in range(n)]
        line = taper_mask(size, M, widths)
        bl = union(*[taper_mask(size, [(x * F, y * F) for x, y in b], [(2.8 - 2.0 * k / len(b)) * F for k in range(len(b) - 1)]) for b in branches])
        allc = union(line, bl)
        img = comp(img, solid(size, PAPER, scale_l(grow(allc, 5 * F).filter(ImageFilter.GaussianBlur(4 * F)), 0.35)))
        img = comp(img, solid(size, BONE, scale_l(shift(grow(allc, 1.2 * F), 1.6 * F, 2.0 * F), 0.85)))
        img = comp(img, solid(size, INK, allc))
        img = comp(img, solid(size, (70, 40, 54), scale_l(shrink(line, 1.4 * F), 0.8)))
        chips = Image.new("L", size, 0)
        for i in range(6):
            x, y = M[int(rng.uniform(0.3, 0.7) * n)]
            x += rng.uniform(-8, 8) * F
            y += rng.uniform(-8, 8) * F
            r = rng.uniform(0.6, 1.2) * F
            ImageDraw.Draw(chips).ellipse((x - r, y - r, x + r, y + r), fill=200)
        img = comp(img, solid(size, INK, chips))
    else:
        left, right = [], []
        for i, (x, y) in enumerate(M):
            j0, j1 = max(0, i - 1), min(n, i + 1)
            dx, dy = M[j1][0] - M[j0][0], M[j1][1] - M[j0][1]
            ln = math.hypot(dx, dy) or 1
            nx, ny = -dy / ln, dx / ln
            h = (1.0 + 10.0 * math.sin(math.pi * i / n) ** 0.75) * F
            left.append((x + nx * h * rng.uniform(0.7, 1.3), y + ny * h * rng.uniform(0.7, 1.3)))
            right.append((x - nx * h * rng.uniform(0.7, 1.3), y - ny * h * rng.uniform(0.7, 1.3)))
        gap = poly_mask(size, left + right[::-1])
        bl = union(*[taper_mask(size, [(x * F, y * F) for x, y in b], [(3.2 - 2.4 * k / len(b)) * F for k in range(len(b) - 1)]) for b in branches])
        allc = union(gap, bl)
        # soft beams of light thrown out of the crack
        beams = Image.new("L", size, 0)
        bd = ImageDraw.Draw(beams)
        for k in range(7):
            x, y = M[int((0.22 + 0.56 * k / 6) * n)]
            dx, dy = M[-1][0] - M[0][0], M[-1][1] - M[0][1]
            ln = math.hypot(dx, dy)
            nx, ny = -dy / ln, dx / ln
            sgn = 1 if k % 2 else -1
            L = rng.uniform(40, 70) * F
            wdt = rng.uniform(5, 9) * F
            bd.polygon([(x - dx / ln * wdt * 0.4, y - dy / ln * wdt * 0.4), (x + dx / ln * wdt * 0.4, y + dy / ln * wdt * 0.4),
                        (x + nx * sgn * L + dx / ln * wdt, y + ny * sgn * L + dy / ln * wdt),
                        (x + nx * sgn * L - dx / ln * wdt, y + ny * sgn * L - dy / ln * wdt)], fill=rng.randint(50, 90))
        img = comp(img, solid(size, GOLD, beams.filter(ImageFilter.GaussianBlur(6 * F))))
        img = comp(img, glow(allc, CRIMSON, 16 * F, 1.2), glow(allc, GOLD, 7 * F, 1.3))
        img = comp(img, solid(size, INK, ImageChops.subtract(grow(allc, 2.6 * F), allc)))
        img = comp(img, solid(size, CRIMSON, allc), solid(size, GOLD, shrink(allc, 2 * F)))
        core = taper_mask(size, M, [(0.6 + 4.2 * math.sin(math.pi * (i + 0.5) / n) ** 0.9) * F for i in range(n)])
        img = comp(img, solid(size, GOLD_CORE, core.filter(ImageFilter.GaussianBlur(1.2 * F))))
        hot = taper_mask(size, M, [(0.3 + 1.6 * math.sin(math.pi * (i + 0.5) / n) ** 1.2) * F for i in range(n)])
        img = comp(img, solid(size, (255, 255, 250), hot.filter(ImageFilter.GaussianBlur(0.8 * F))))
        bcore = union(*[taper_mask(size, [(x * F, y * F) for x, y in b], [(1.4 - 1.1 * k / len(b)) * F for k in range(len(b) - 1)]) for b in branches])
        img = comp(img, solid(size, GOLD_CORE, bcore))
        # stone flakes thrown off, and sparks
        flakes = canvas(*size)
        for i in range(9):
            x, y = M[int(rng.uniform(0.25, 0.75) * n)]
            a = rng.uniform(0, 2 * math.pi)
            dist = rng.uniform(14, 34) * F
            fx, fy = x + math.cos(a) * dist, y + math.sin(a) * dist
            s = rng.uniform(2.5, 4.5) * F
            rot = rng.uniform(0, 2 * math.pi)
            tri = poly_mask(size, [(fx + math.cos(rot + k * 2.2) * s * (1 if k else 1.5), fy + math.sin(rot + k * 2.2) * s * (1 if k else 1.5)) for k in range(3)])
            flakes = comp(flakes, ink_edge(size, tri, 1.0 * F), solid(size, rng.choice([PAPER, BONE, INK_SOFT]), tri))
        img = comp(img, flakes)
        sp = Image.new("L", size, 0)
        for i in range(6):
            x, y = M[int(rng.uniform(0.15, 0.85) * n)]
            sp = ImageChops.lighter(sp, star(size, (x + rng.uniform(-26, 26) * F, y + rng.uniform(-26, 26) * F), rng.uniform(4, 7) * F))
        img = comp(img, glow(sp, GOLD, 2 * F, 1.4), solid(size, GOLD_CORE, sp))
    img = clip(img, edge_fade(size, 10 * F))
    return downsample(img, F)


# ------------------------------------------------------------------------------------------------------- Nevron mark

def nevron_mark():
    """The Nevron coverage mark: a flat corner badge, never a piece. A spiked ink roundel holding the magenta eye on a
    bone face, with no body, arms or feet, so it cannot be counted as one more Nevron warrior. 64 px."""
    S, F = 64, 8
    size = (S * F, S * F)
    rng = random.Random(SEED + 800)

    def P(x, y):
        return (x * F, y * F)

    c = P(32, 32)
    ring = rough_disc(size, c, 21 * F, rng, wobble=0.03)
    spikes = Image.new("L", size, 0)
    sd = ImageDraw.Draw(spikes)
    for k in range(12):
        a = math.radians(k * 30 + 15 + rng.uniform(-4, 4))
        ln = 26 if k % 2 == 0 else 24
        sd.polygon([P(32 + math.cos(a - 0.17) * 19.5, 32 + math.sin(a - 0.17) * 19.5), P(32 + math.cos(a) * ln, 32 + math.sin(a) * ln),
                    P(32 + math.cos(a + 0.17) * 19.5, 32 + math.sin(a + 0.17) * 19.5)], fill=255)
    sil = union(ring, spikes)
    face = ring_mask(size, c, 0, 15 * F)
    img = canvas(*size)
    img = comp(img, solid(size, INK, scale_l(shift(sil, 1 * F, 1.5 * F).filter(ImageFilter.GaussianBlur(1.5 * F)), 0.5)))
    img = comp(img, solid(size, BONE, ImageChops.subtract(grow(sil, 1.6 * F), sil)))
    img = comp(img, solid(size, INK, ImageChops.subtract(grow(sil, 2.4 * F), grow(sil, 1.6 * F)).point(lambda v: v * 3 // 5)))
    img = comp(img, solid(size, INK, sil))
    img = comp(img, clip(solid(size, (92, 70, 104), scale_l(radial_light(size, P(24, 22), 22 * F), 0.5)), ring))
    img = comp(img, solid(size, MAGENTA, scale_l(ring_mask(size, c, 15 * F, 16.8 * F), 0.9)))
    img = comp(img, paper_fill(size, face, rng, base=BONE, amount=0.2))
    # the eye
    eye = poly_mask(size, curve(P(21, 32), P(32, 20.5), P(43, 32), 16) + curve(P(43, 32), P(32, 41), P(21, 32), 16))
    img = comp(img, clip(glow(eye, MAGENTA, 2.5 * F, 1.0), face))
    img = comp(img, solid(size, BONE, eye))
    iris = ImageChops.multiply(ring_mask(size, P(32.5, 31.5), 0, 5.2 * F), eye)
    img = comp(img, solid(size, MAGENTA, iris))
    pupil = Image.new("L", size, 0)
    ImageDraw.Draw(pupil).ellipse((*P(31.4, 27.2), *P(33.6, 35.8)), fill=255)
    img = comp(img, solid(size, INK, pupil), solid(size, (255, 250, 246), ring_mask(size, P(30.2, 29.4), 0, 1.2 * F)))
    img = comp(img, solid(size, INK, ImageChops.subtract(grow(eye, 1.0 * F), eye)))
    return downsample(img, F)


# ----------------------------------------------------------------------------------------------------------- entry

def make(out_dir, fonts):
    out = []

    def emit(asset, rel, img, sprites=()):
        save(img, out_dir, rel)
        out.append({"asset": asset, "path": f"art/{rel}", "size": [img.width, img.height], "sprites": list(sprites)})

    flags = canvas(512, 256)
    flags.alpha_composite(flag_sprite(True, fonts), (0, 0))
    flags.alpha_composite(flag_sprite(False, fonts), (256, 0))
    emit("asset.flag-token", "tokens/expedition-flag.png", flags,
         [{"name": "lit", "box": [0, 0, 256, 256]}, {"name": "faded", "box": [256, 0, 512, 256]}])
    sheet, sprites = rock_sheet()
    emit("asset.rock-tokens", "tokens/rocks.png", sheet, sprites)
    emit("asset.badge-lampmaster", "tokens/badge-lampmaster.png", badge("lampmaster"))
    emit("asset.badge-francois", "tokens/badge-francois.png", badge("francois"))
    emit("asset.portrait-frame", "tokens/portrait-frame.png", portrait_frame(),
         [{"name": "frame", "box": [0, 0, PW, PH]}, {"name": "window", "box": list(WINDOW_BOX)}])
    emit("asset.portrait-veiled", "tokens/portrait-veiled.png", portrait_veiled())
    cracks = canvas(512, 256)
    cracks.alpha_composite(crack_sprite(False), (0, 0))
    cracks.alpha_composite(crack_sprite(True), (256, 0))
    emit("asset.cell-crack", "tokens/cell-crack.png", cracks,
         [{"name": "closed", "box": [0, 0, 256, 256]}, {"name": "open", "box": [256, 0, 512, 256]}])
    emit("asset.nevron-mark", "tokens/nevron-mark.png", nevron_mark())
    return out

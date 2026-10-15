"""Clea's effects for Root 33: converted pieces melting into Nevrons, the Flying Manor's ring of floating rocks, and a
sheet of regional Nevron warrior glyphs.

All original and drawn from scratch with Pillow in the shared painterly style (tools/art/style.py): ink, bone paper,
gold leaf, crimson and river teal, leaning here on a violet ink for Clea. The Nevrons are invented ink creatures in the
family of art/tokens/nevron-mark.png (a spined ink body, one magenta eye, tendril arms, drippy feet); the regional
variants change only their crest, limbs and accent colour. The converted piece is a generic bone pawn, not any game's
figure. Every random choice comes from a seeded random.Random (noise included), so the same code always paints the same
pixels. Pieces are drawn at 2-4x and reduced, so edges stay soft at token size.

Sprites
  art/fx/clea-melt.png                  six 256 px frames, played left to right: piece, drip, slump, pool, rise, nevron.
                                        The ground line sits at y=200 in every frame, so frames register on a clearing.
  art/fx/flying-manor-float.png         512 px cells centred on the clearing (a clearing about 250 px across fits inside
                                        the ring): float-1..float-4 loop the ring of rocks bobbing, and underside is an
                                        optional earth rim to hang under the clearing's lower edge.
  art/tokens/nevron-region-sprites.png  128 px warrior glyphs: nevron (any clearing without a region), woodland-fox,
                                        woodland-rabbit and woodland-mouse (plain clearings by suit), then one per map
                                        role in sheets/map_roles.json (the sprite name is the role id without "role.").
"""
import math
import random
import sys
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageOps

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from style import (BONE, CRIMSON, CRIMSON_DEEP, DUSK, GOLD, GOLD_CORE, GOLD_DEEP, INK, INK_SOFT, PAPER, SUIT,  # noqa: E402
                   TEAL, TEAL_DEEP, brush_stroke, canvas, curve, glow, solid)

SEED = 3373
MAGENTA = (204, 46, 130)
MAGENTA_LIGHT = (255, 178, 218)
SHEEN = (92, 70, 104)
VIOLET = (110, 72, 168)
VIOLET_DEEP = (52, 34, 84)
VIOLET_LIGHT = (176, 140, 226)
LILAC = (222, 200, 246)
LIGHT_TEAL = (176, 226, 222)
STONE = (150, 140, 146)
STONE_LIGHT = (208, 200, 194)
STONE_DEEP = (86, 76, 92)
EARTH = (120, 84, 60)
EARTH_DEEP = (70, 48, 40)
MOSS = (96, 150, 120)
EMBER = (240, 150, 80)
ASH = (196, 188, 184)


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


def radial_light(size, center, radius, power=1.0, steps=40):
    """L image: 255 at `center`, falling to 0 at `radius`."""
    m = Image.new("L", size, 0)
    d = ImageDraw.Draw(m)
    cx, cy = center
    for k in range(steps):
        r = radius * (1 - k / steps)
        d.ellipse((cx - r, cy - r, cx + r, cy + r), fill=int(255 * ((k + 1) / steps) ** power))
    return m.filter(ImageFilter.GaussianBlur(max(1.0, radius / steps * 1.5)))


def rough_disc(size, c, r, rng, wobble=0.035, jitter=0.004, steps=200, sy=1.0):
    harm = [(k, rng.uniform(0, 2 * math.pi), rng.uniform(0.35, 1.0) / k) for k in range(2, 8)]
    pts = []
    for i in range(steps):
        a = 2 * math.pi * i / steps
        f = 1 + wobble * sum(amp * math.sin(k * a + ph) for k, ph, amp in harm) + rng.uniform(-jitter, jitter)
        pts.append((c[0] + math.cos(a) * r * f, c[1] + math.sin(a) * r * f * sy))
    m = Image.new("L", size, 0)
    ImageDraw.Draw(m).polygon(pts, fill=255)
    return m


def grow(mask, px):
    if px <= 4:
        return mask.filter(ImageFilter.MaxFilter(2 * max(0, int(px)) + 1))
    return mask.filter(ImageFilter.GaussianBlur(px / 1.5)).point(lambda v: 255 if v > 17 else 0)


def shrink(mask, px):
    if px <= 4:
        return mask.filter(ImageFilter.MinFilter(2 * max(0, int(px)) + 1))
    return mask.filter(ImageFilter.GaussianBlur(px / 1.5)).point(lambda v: 255 if v > 238 else 0)


def union(*masks):
    out = masks[0]
    for m in masks[1:]:
        out = ImageChops.lighter(out, m)
    return out


def inter(a, b):
    return ImageChops.multiply(a, b)


def minus(a, b):
    return ImageChops.subtract(a, b)


def shift(mask, dx, dy):
    """Move a mask (or RGBA layer) without wrapping."""
    out = Image.new(mask.mode, mask.size, 0)
    out.paste(mask, (int(round(dx)), int(round(dy))))
    return out


def taper_mask(size, pts, widths, fill=255):
    """A line whose width changes point by point (round joints)."""
    m = Image.new("L", size, 0)
    d = ImageDraw.Draw(m)
    r = widths[0] / 2
    d.ellipse((pts[0][0] - r, pts[0][1] - r, pts[0][0] + r, pts[0][1] + r), fill=fill)
    for (a, b), w in zip(zip(pts, pts[1:]), widths):
        d.line([a, b], fill=fill, width=max(1, int(round(w))))
        r = w / 2
        d.ellipse((b[0] - r, b[1] - r, b[0] + r, b[1] + r), fill=fill)
    return m


def downsample(img, f):
    return img.resize((img.width // f, img.height // f), Image.LANCZOS)


def save(img, out_dir, rel):
    p = Path(out_dir) / rel
    p.parent.mkdir(parents=True, exist_ok=True)
    img.save(p, optimize=True)
    return p


def paint(size, rgb, mask, a=1.0):
    return solid(size, rgb, scale_l(mask, a) if a != 1.0 else mask)


class Grid:
    """Draws in design units: (x, y) maps to (ox + x*k, oy + y*k) pixels on a canvas of `size`."""

    def __init__(self, size, k, ox=0.0, oy=0.0):
        self.size, self.k, self.ox, self.oy = size, k, ox, oy

    def p(self, x, y):
        return (self.ox + x * self.k, self.oy + y * self.k)

    def ps(self, pts):
        return [self.p(x, y) for x, y in pts]

    def r(self, v):
        return v * self.k

    def blank(self):
        return Image.new("L", self.size, 0)

    def disc(self, x, y, r, rng, wobble=0.06, sy=1.0):
        return rough_disc(self.size, self.p(x, y), self.r(r), rng, wobble=wobble, sy=sy)

    def ell(self, x, y, rx, ry, fill=255):
        m = self.blank()
        x0, y0 = self.p(x - rx, y - ry)
        x1, y1 = self.p(x + rx, y + ry)
        ImageDraw.Draw(m).ellipse((x0, y0, x1, y1), fill=fill)
        return m

    def poly(self, pts, fill=255):
        m = self.blank()
        ImageDraw.Draw(m).polygon(self.ps(pts), fill=fill)
        return m

    def line(self, pts, w, fill=255):
        m = self.blank()
        ImageDraw.Draw(m).line(self.ps(pts), fill=fill, width=max(1, int(round(self.r(w)))), joint="curve")
        return m

    def taper(self, pts, w0, w1):
        n = len(pts) - 1
        return taper_mask(self.size, self.ps(pts), [self.r(w0 + (w1 - w0) * i / max(1, n - 1)) for i in range(n)])

    def tendril(self, p0, p1, p2, w0, w1, n=14):
        return self.taper(curve(p0, p1, p2, n), w0, w1)

    def spines(self, cx, cy, base_r, specs, sy=0.94):
        """Triangular spines around a body: specs are (degrees, length[, half-width in radians])."""
        m = self.blank()
        d = ImageDraw.Draw(m)
        for spec in specs:
            deg, ln = spec[0], spec[1]
            hw = spec[2] if len(spec) > 2 else 0.2
            a = math.radians(deg)
            b0 = self.p(cx + math.cos(a - hw) * base_r, cy + math.sin(a - hw) * base_r * sy)
            b1 = self.p(cx + math.cos(a + hw) * base_r, cy + math.sin(a + hw) * base_r * sy)
            tip = self.p(cx + math.cos(a + 0.12) * (base_r + 4 + ln), cy + math.sin(a + 0.12) * (base_r + 4 + ln) * sy)
            d.polygon([b0, tip, b1], fill=255)
        return m

    def feet(self, cx, y0, y1, spread=14, w=8):
        """Three drippy legs ending in round toes."""
        legs = [self.taper([(cx - spread, y0), (cx - spread - 3, y1 - 8), (cx - spread - 5, y1 - 2)], w, w * 0.75),
                self.taper([(cx, y0 + 4), (cx, y1 - 4), (cx + 1, y1 + 2)], w, w * 0.75),
                self.taper([(cx + spread, y0), (cx + spread + 3, y1 - 8), (cx + spread + 5, y1 - 2)], w, w * 0.75)]
        toes = [self.ell(cx - spread - 5.5, y1 - 1, 4.5, 3), self.ell(cx + 1, y1 + 3, 4.5, 3),
                self.ell(cx + spread + 5.5, y1 - 1, 4.5, 3)]
        return union(*legs, *toes)

    def arms(self, cx, cy, reach=24, rise=-6, drop=10, w=7):
        """Two tendril arms with round hands."""
        l0, r0 = (cx - 24, cy + 6), (cx + 24, cy + 6)
        lt, rt = (cx - reach - 22, cy + drop + 8), (cx + reach + 22, cy + drop + 8)
        a = union(self.tendril(l0, (cx - reach - 16, cy + rise), lt, w, w * 0.4),
                  self.tendril(r0, (cx + reach + 16, cy + rise), rt, w, w * 0.4))
        return union(a, self.ell(lt[0] - 0.5, lt[1] + 1.5, 3.2, 3.6), self.ell(rt[0] + 0.5, rt[1] + 1.5, 3.2, 3.6))


def leaf_pts(base, angle_deg, length, width, n=10):
    """A lancet leaf from `base`, pointing along angle (design units)."""
    a = math.radians(angle_deg)
    ux, uy = math.cos(a), math.sin(a)
    nx, ny = -uy, ux
    left, right = [], []
    for i in range(n + 1):
        s = i / n
        wv = width * math.sin(math.pi * min(1.0, s * 1.1)) ** 0.8 * (1 - 0.25 * s)
        cx, cy = base[0] + ux * s * length, base[1] + uy * s * length
        left.append((cx + nx * wv, cy + ny * wv))
        right.append((cx - nx * wv, cy - ny * wv))
    return left + right[::-1]


def draw_eye(img, g, x, y, w, sil, lid=0.0, closed=False):
    """The family eye: a bone almond with a magenta iris and slit pupil. lid covers the top part; closed draws a slit."""
    size = g.size
    if closed:
        slit = g.taper(curve((x - w, y), (x, y + 0.5 * w), (x + w, y), 16), 0.2 * w, 0.2 * w)
        img = comp(img, clip(glow(slit, MAGENTA, g.r(0.4 * w), 2.0), sil), solid(size, MAGENTA, slit))
        return comp(img, solid(size, MAGENTA_LIGHT, shrink(slit, max(1, g.r(0.05 * w)))))
    eye = g.poly(curve((x - w, y), (x, y - 1.07 * w), (x + w, y), 16) + curve((x + w, y), (x, y + 0.8 * w), (x - w, y), 16))
    img = comp(img, clip(glow(eye, MAGENTA, g.r(0.33 * w), 1.2), sil))
    img = comp(img, solid(size, BONE, eye))
    ix, iy = x + 0.067 * w, y - 0.067 * w
    img = comp(img, solid(size, MAGENTA, inter(g.ell(ix, iy, 0.43 * w, 0.43 * w), eye)))
    img = comp(img, solid(size, INK, inter(g.ell(ix, iy, 0.095 * w, 0.37 * w), eye)))
    img = comp(img, solid(size, (255, 250, 246), inter(g.ell(x - 0.1 * w, y - 0.23 * w, 0.1 * w, 0.1 * w), eye)))
    if lid:
        cut = y - 0.55 * w + lid * 0.95 * w
        lid_m = inter(eye, g.poly([(x - w - 2, y - 1.3 * w), (x + w + 2, y - 1.3 * w), (x + w + 2, cut + 0.12 * w),
                                   (x, cut - 0.1 * w), (x - w - 2, cut + 0.12 * w)]))
        img = comp(img, solid(size, INK, lid_m))
        img = comp(img, solid(size, (150, 118, 160), inter(minus(grow(lid_m, max(1, g.r(0.06 * w))), lid_m), eye)))
    return comp(img, solid(size, INK, minus(grow(eye, max(1, g.r(0.08 * w))), eye)))


def finish(g, parts, halo=True, shadow=True):
    """Ink a Nevron: drop shadow, bone halo and thin ink rim (token style), ink body, sheen, accents, eyes."""
    size = g.size
    sil, body = parts["sil"], parts["body"]
    img = canvas(*size)
    for layer in parts.get("aura", ()):
        img = comp(img, layer)
    if shadow:
        img = comp(img, solid(size, INK, scale_l(shift(sil, g.r(1.5), g.r(2.5)).filter(ImageFilter.GaussianBlur(g.r(3))), 0.5)))
    if halo:
        ring_a, ring_b = grow(sil, g.r(2.6)), grow(sil, g.r(3.6))
        img = comp(img, solid(size, BONE, minus(ring_a, sil)))
        img = comp(img, solid(size, INK, scale_l(minus(ring_b, ring_a), 0.6)))
    img = comp(img, solid(size, INK, sil))
    sx, sy, sr = parts.get("sheen_at", (54, 52, 30))
    img = comp(img, clip(solid(size, parts.get("sheen", SHEEN), scale_l(radial_light(size, g.p(sx, sy), g.r(sr)),
                                                                          parts.get("sheen_a", 0.55))), body))
    for layer in parts.get("paint", ()):
        img = comp(img, layer)
    for e in parts.get("eyes", ()):
        x, y, w = e[:3]
        opts = e[3] if len(e) > 3 else {}
        img = draw_eye(img, g, x, y, w, sil, **opts)
    for layer in parts.get("over", ()):
        img = comp(img, layer)
    return img


# ------------------------------------------------------------------------------------------------ regional Nevrons

def v_nevron(g, rng):
    """The plain Nevron, as on the coverage mark: five spines, tendril arms, three drippy feet."""
    body = g.disc(64, 64, 27, rng, wobble=0.08, sy=0.94)
    sp = g.spines(64, 64, 23, [(-160, 7), (-130, 11), (-100, 15), (-70, 12), (-40, 8)])
    sil = union(body, sp, g.arms(64, 64), g.feet(64, 84, 108))
    drops = union(g.ell(20, 40, 3, 3), g.ell(108, 44, 2.4, 2.4))
    return {"sil": union(sil, drops), "body": body, "eyes": [(64, 62, 15)]}


def v_woodland(g, rng, suit):
    """Plain clearings: a crest of the suit's colour (fox leaves, rabbit grass with seed heads, mouse mushrooms)."""
    size = g.size
    tint = SUIT[suit]
    deep = lerp_rgb(tint, INK, 0.45)
    out = {"sheen": lerp_rgb(tint, INK, 0.35), "sheen_a": 0.5, "paint": []}
    if suit == "fox":
        body = g.disc(64, 68, 26, rng, wobble=0.07, sy=0.94)
        leaves = []
        for deg, ln in ((-152, 17), (-122, 24), (-90, 29), (-58, 24), (-28, 17)):
            a = math.radians(deg)
            base = (64 + math.cos(a) * 18, 68 + math.sin(a) * 17)
            leaves.append((base, deg, ln))
        crest = union(*[g.poly(leaf_pts(b, d, ln, 7.2)) for b, d, ln in leaves])
        hands = union(g.poly(leaf_pts((22, 84), 150, 11, 4)), g.poly(leaf_pts((106, 84), 30, 11, 4)))
        sil = union(body, crest, g.arms(64, 68, reach=22), hands, g.feet(64, 88, 109))
        inner = shrink(union(crest, hands), g.r(1.4))
        shade = map_l(linear_light(size, 90), [(0, lerp_rgb(tint, GOLD, 0.35)), (0.6, tint), (1, lerp_rgb(tint, INK, 0.2))]).convert("RGBA")
        out["paint"] += [clip(shade, inner)]
        ribs = union(*[g.line([b, (b[0] + math.cos(math.radians(d)) * ln * 0.82, b[1] + math.sin(math.radians(d)) * ln * 0.82)], 1.1)
                       for b, d, ln in leaves])
        out["paint"] += [solid(size, INK, inter(ribs, crest)),
                         solid(size, lerp_rgb(tint, GOLD_CORE, 0.5), scale_l(inter(shift(ribs, -g.r(1.2), 0), inner), 0.5))]
        out["eyes"] = [(64, 68, 14)]
    elif suit == "rabbit":
        body = g.disc(64, 72, 26, rng, wobble=0.07, sy=0.94)
        blades, seeds = [], []
        for bx, top, sway in ((46, 22, -10), (55, 14, -5), (64, 10, 2), (73, 14, 7), (82, 22, 11)):
            p0, p2 = (bx, 56), (bx + sway, top)
            blades.append(g.tendril(p0, (bx + sway * 0.1, (56 + top) / 2), p2, 5, 2))
            seeds.append((p2[0], p2[1] - 2.5, sway))
        seed_m = union(*[g.poly(leaf_pts((x - math.sin(math.radians(s * 2)) * 2, y + 4), -90 + s * 2, 9, 3.4)) for x, y, s in seeds])
        sil = union(body, *blades, seed_m, g.arms(64, 72, reach=22), g.feet(64, 92, 110))
        out["paint"] += [paint(size, tint, shrink(seed_m, g.r(1.2))),
                         paint(size, GOLD_CORE, inter(shrink(seed_m, g.r(1.2)), shift(shrink(seed_m, g.r(2.4)), -g.r(1), -g.r(1))), 0.7)]
        tufts = union(*[g.line([(x, 84), (x + d, 78)], 1.2) for x, d in ((48, -2), (52, 1), (76, -1), (80, 2))])
        out["paint"] += [paint(size, lerp_rgb(tint, INK, 0.2), inter(tufts, body), 0.8)]
        out["eyes"] = [(64, 72, 14)]
    else:
        body = g.disc(64, 74, 26, rng, wobble=0.07, sy=0.92)
        stem = union(g.taper([(60, 56), (59, 46), (58, 38)], 10, 8), g.taper([(84, 58), (87, 50), (89, 44)], 6, 5))
        cap1 = g.poly(curve((32, 40), (36, 10), (58, 12), 14) + curve((58, 12), (82, 12), (86, 38), 14) +
                      curve((86, 38), (60, 30), (32, 40), 14))
        cap2 = g.poly(curve((78, 46), (80, 30), (90, 30), 10) + curve((90, 30), (101, 30), (102, 45), 10) +
                      curve((102, 45), (90, 41), (78, 46), 10))
        sil = union(body, stem, cap1, cap2, g.arms(64, 74, reach=22), g.feet(64, 94, 110))
        caps = shrink(union(cap1, cap2), g.r(1.4))
        shade = map_l(linear_light(size, 120), [(0, lerp_rgb(tint, GOLD_CORE, 0.3)), (0.6, tint), (1, deep)]).convert("RGBA")
        spots = union(g.ell(48, 24, 4, 3), g.ell(62, 19, 3, 2.4), g.ell(72, 28, 3.2, 2.6), g.ell(42, 33, 2.4, 2),
                      g.ell(88, 36, 2.4, 2), g.ell(95, 39, 1.8, 1.6))
        gills = union(g.line(curve((36, 38), (60, 30), (82, 37), 12), 1.0), g.line(curve((81, 44), (90, 40), (99, 43), 8), 0.8))
        out["paint"] += [clip(shade, caps), paint(size, BONE, inter(spots, caps)), paint(size, INK, inter(gills, caps), 0.8)]
        out["eyes"] = [(64, 74, 14)]
    out["sil"], out["body"] = sil, body
    return out


def v_lumiere(g, rng):
    """Lumiere: a glowing lure on a stalk, like a little lamp carried over the water; teal wave marks on the belly."""
    size = g.size
    body = g.disc(62, 74, 26, rng, wobble=0.07, sy=0.94)
    stalk = g.tendril((66, 52), (70, 8), (98, 18), 5.5, 2.6, 18)
    bulb = g.disc(100, 27, 7.5, rng, wobble=0.04)
    sp = g.spines(62, 74, 23, [(-165, 6), (-140, 9), (-115, 11)])
    sil = union(body, stalk, bulb, sp, g.arms(62, 74, reach=21), g.feet(62, 93, 110))
    waves = union(g.line(curve((44, 88), (50, 82), (56, 88), 8) + curve((56, 88), (62, 94), (68, 88), 8), 1.6),
                  g.line(curve((62, 96), (68, 90), (74, 96), 8) + curve((74, 96), (80, 101), (84, 95), 8), 1.6))
    bulb_in = shrink(bulb, g.r(1.4))
    gold = map_l(radial_light(size, g.p(98, 24), g.r(9)), [(0, GOLD_DEEP), (0.5, GOLD), (1, GOLD_CORE)]).convert("RGBA")
    return {"sil": sil, "body": body, "sheen": TEAL_DEEP, "sheen_a": 0.7, "sheen_at": (52, 62, 30),
            "paint": [paint(size, LIGHT_TEAL, inter(waves, shrink(body, g.r(2))), 0.85)],
            "eyes": [(60, 72, 14)],
            "over": [glow(bulb, GOLD, g.r(7), 1.5), glow(bulb, GOLD_CORE, g.r(2.5), 1.2), solid(size, INK, minus(grow(bulb, g.r(1)), bulb)),
                     clip(gold, bulb_in), paint(size, (255, 255, 250), g.ell(97.5, 24.5, 2, 1.6))]}


def v_old_lumiere(g, rng):
    """Old Lumiere: a ragged, cloaked Nevron of the ruins: broken drooping spines, tattered hem, embers and ash."""
    size = g.size
    body = g.disc(64, 58, 25, rng, wobble=0.07, sy=0.95)
    skirt = g.poly([(40, 56), (34, 96), (36, 111), (44, 94), (50, 105), (57, 92), (64, 113), (71, 93), (78, 104),
                    (85, 92), (92, 109), (95, 95), (88, 56)])
    holes = union(g.ell(49, 86, 2.6, 3.4), g.ell(79, 82, 2.2, 2.8), g.ell(66, 100, 1.6, 2.2))
    broken = union(g.tendril((46, 40), (34, 22), (26, 34), 5, 1.6), g.tendril((63, 35), (62, 22), (72, 15), 5.5, 2.2),
                   g.tendril((82, 40), (96, 26), (101, 38), 5, 1.6))
    limp = union(g.tendril((42, 64), (28, 72), (25, 88), 6, 2.2), g.tendril((86, 64), (100, 72), (103, 88), 6, 2.2),
                 g.ell(25, 89, 2.8, 3.2), g.ell(103, 89, 2.8, 3.2))
    sil = minus(union(body, skirt, broken, limp), holes)
    trunk = union(body, skirt)
    embers = union(*[g.ell(x, y, 1.6, 1.6) for x, y in ((37, 109), (51, 103), (64, 111), (78, 102), (92, 107), (44, 93))])
    ash = g.blank()
    d = ImageDraw.Draw(ash)
    for x, y, r, a in ((16, 20, 2.4, 20), (110, 18, 2.0, 70), (12, 60, 1.8, 120), (116, 62, 2.2, 160), (20, 104, 1.6, 40),
                       (110, 112, 1.8, 100), (30, 8, 1.5, 10)):
        cx, cy = g.p(x, y)
        rr = g.r(r)
        ca, sa = math.cos(math.radians(a)), math.sin(math.radians(a))
        d.polygon([(cx + ca * rr, cy + sa * rr), (cx - sa * rr * 0.5, cy + ca * rr * 0.5), (cx - ca * rr, cy - sa * rr),
                   (cx + sa * rr * 0.5, cy - ca * rr * 0.5)], fill=255)
    tatters = union(*[g.line([(x, 66), (x + dx, 96)], 0.9) for x, dx in ((48, -4), (58, -1), (70, 1), (80, 4))])
    return {"sil": sil, "body": trunk, "sheen": (128, 78, 70), "sheen_a": 0.55, "sheen_at": (56, 50, 34),
            "paint": [paint(size, (96, 82, 90), inter(tatters, shrink(skirt, g.r(2))), 0.9),
                      glow(embers, EMBER, g.r(2.2), 1.6), paint(size, (255, 214, 160), embers)],
            "eyes": [(64, 58, 13, {"lid": 0.45})],
            "over": [paint(size, INK, minus(grow(ash, g.r(0.8)), ash), 0.8), paint(size, ASH, ash)]}


def v_monolith(g, rng):
    """The Monolith: an angular shard body split by glowing crimson cracks."""
    size = g.size
    body = g.poly([(64, 20), (76, 34), (90, 40), (95, 66), (85, 92), (64, 99), (43, 92), (33, 66), (38, 40), (52, 34)])
    shards = union(g.poly([(40, 44), (22, 24), (48, 37)]), g.poly([(88, 44), (106, 24), (80, 37)]),
                   g.poly([(52, 36), (50, 18), (60, 30)]), g.poly([(76, 36), (80, 16), (68, 30)]))
    legs = union(g.poly([(47, 90), (40, 113), (51, 113), (58, 95)]), g.poly([(70, 95), (77, 113), (88, 113), (81, 90)]))
    blades = union(g.poly([(36, 60), (14, 84), (20, 88), (42, 72)]), g.poly([(92, 60), (114, 84), (108, 88), (86, 72)]))
    sil = union(body, shards, legs, blades)
    facet = union(g.poly([(64, 20), (52, 34), (38, 40), (33, 66), (50, 62), (58, 44)]),
                  g.poly([(43, 92), (64, 99), (60, 84)]))
    cracks_pts = [[(64, 22), (61, 34), (66, 42), (63, 51)], [(62, 74), (58, 82), (63, 90), (60, 99)], [(61, 36), (52, 46), (46, 50)],
                  [(66, 44), (78, 50), (86, 48)], [(58, 82), (48, 86)]]
    cracks = union(*[g.line(p, 1.8) for p in cracks_pts])
    cores = union(*[g.line(p, 0.7) for p in cracks_pts])
    return {"sil": sil, "body": body, "sheen": CRIMSON_DEEP, "sheen_a": 0.75, "sheen_at": (56, 52, 34),
            "paint": [paint(size, (120, 96, 120), inter(facet, shrink(body, g.r(1.5))), 0.35),
                      clip(glow(cracks, CRIMSON, g.r(2.4), 1.4), body), paint(size, CRIMSON, inter(cracks, body)),
                      paint(size, GOLD_CORE, inter(cores, body), 0.9)],
            "eyes": [(64, 62, 13)]}


def v_visages(g, rng):
    """The Visages Axon's clearing: a wide Nevron that watches with many eyes."""
    body = g.disc(64, 70, 31, rng, wobble=0.06, sy=0.86)
    sp = g.spines(64, 70, 28, [(-172, 5), (-148, 6), (-124, 7), (-90, 7), (-56, 7), (-32, 6), (-8, 5)], sy=0.86)
    sil = union(body, sp, g.arms(64, 70, reach=24, drop=4), g.feet(64, 92, 110, spread=15))
    return {"sil": sil, "body": body, "sheen": (130, 64, 112), "sheen_a": 0.55,
            "eyes": [(64, 79, 12), (44, 60, 7.5), (84, 60, 7.5), (64, 51, 6)]}


def v_sirene(g, rng):
    """The Sirene Axon's clearing: a floating Nevron trailing long ribbon tendrils, with bubbles."""
    size = g.size
    head = g.disc(60, 50, 21, rng, wobble=0.07)
    tail = union(g.tendril((60, 62), (74, 88), (54, 106), 18, 4, 16), g.tendril((54, 106), (46, 112), (40, 104), 4, 2, 8))
    ribbons_pts = [((70, 34), (104, 18), (112, 56)), ((72, 42), (104, 40), (106, 78)), ((70, 52), (96, 60), (92, 96)),
                   ((48, 36), (22, 26), (18, 60))]
    ribbons = union(*[g.tendril(a, b, c, 8, 3.2, 18) for a, b, c in ribbons_pts])
    curls = union(g.ell(112, 59, 3.4, 3.4), g.ell(105, 81, 3.2, 3.2), g.ell(91, 99, 3.2, 3.2), g.ell(18, 63, 3.2, 3.2))
    fins = union(g.tendril((44, 58), (30, 66), (34, 80), 5, 2), g.tendril((76, 58), (82, 70), (78, 80), 4, 1.6))
    sil = union(head, tail, ribbons, curls, fins)
    stripes = union(*[g.line(curve(a, b, c, 18)[2:16], 1.8) for a, b, c in ribbons_pts])
    bubbles = g.blank()
    for x, y, r in ((26, 92, 4), (34, 104, 2.6), (100, 104, 3.2), (14, 82, 2)):
        bubbles = union(bubbles, minus(g.ell(x, y, r, r), g.ell(x, y, r - 1.3, r - 1.3)))
    return {"sil": sil, "body": union(head, tail), "sheen": VIOLET, "sheen_a": 0.65, "sheen_at": (54, 42, 28),
            "paint": [paint(size, LILAC, inter(stripes, ribbons), 0.75)],
            "eyes": [(60, 50, 12.5)],
            "over": [paint(size, INK, minus(grow(bubbles, g.r(0.7)), bubbles), 0.7), paint(size, LIGHT_TEAL, bubbles)]}


def v_reacher(g, rng):
    """The Reacher: a tall, narrow Nevron with very long arms raised, gold-tipped claws."""
    size = g.size
    body = g.disc(64, 72, 18, rng, wobble=0.06, sy=1.5)
    sp = g.spines(64, 72, 15, [(-128, 5), (-90, 9), (-52, 5)], sy=1.5)
    arms = union(g.tendril((54, 60), (28, 60), (24, 18), 6.5, 3.2, 18), g.tendril((74, 60), (100, 60), (104, 18), 6.5, 3.2, 18))
    claws, tips = [], []
    for hx in (24, 104):
        for deg in (-125, -90, -55):
            a = math.radians(deg)
            tip = (hx + math.cos(a) * 9, 18 + math.sin(a) * 9)
            claws.append(g.taper([(hx, 18), ((hx + tip[0]) / 2, (18 + tip[1]) / 2), tip], 3, 1.2))
            tips.append(g.ell(tip[0], tip[1], 1.9, 1.9))
    legs = union(g.taper([(57, 96), (54, 106), (52, 112)], 6.5, 5), g.taper([(71, 96), (74, 106), (76, 112)], 6.5, 5),
                 g.ell(50, 113, 4.5, 2.8), g.ell(78, 113, 4.5, 2.8))
    sil = union(body, sp, arms, *claws, legs)
    clawm = union(*claws, *tips)
    return {"sil": sil, "body": body, "sheen": (150, 112, 58), "sheen_a": 0.5, "sheen_at": (58, 56, 26),
            "paint": [glow(inter(clawm, sil), GOLD, g.r(2), 1.0), paint(size, GOLD, inter(shrink(clawm, g.r(0.6)), sil)),
                      paint(size, GOLD_CORE, union(*tips), 0.8)],
            "eyes": [(64, 64, 11.5)]}


def v_nest(g, rng):
    """Esquie's Nest: a low, round Nevron wearing a shell of stone plates and three pebbles; stubby legs."""
    size = g.size
    body = g.disc(64, 74, 31, rng, wobble=0.05, sy=0.8)
    pebbles = union(g.disc(48, 46, 6, rng, 0.08), g.disc(64, 41, 7, rng, 0.08), g.disc(80, 46, 5.5, rng, 0.08))
    legs = union(*[g.taper([(x0, 90), (x1, 104)], 8, 7) for x0, x1 in ((40, 36), (54, 52), (74, 76), (88, 92))],
                 *[g.ell(x, 106, 5, 3) for x in (35, 52, 76, 93)])
    sil = union(body, pebbles, legs)
    shell = inter(shrink(body, g.r(1.4)), g.poly(curve((28, 82), (64, 66), (100, 82), 16) + [(110, 10), (18, 10)]))
    plates = union(g.line(curve((36, 66), (64, 50), (92, 66), 16), 1.3), g.line([(64, 52), (64, 70)], 1.3),
                   g.line([(46, 59), (42, 77)], 1.3), g.line([(82, 59), (86, 77)], 1.3))
    stone = map_l(ImageChops.add(linear_light(size, 90), noise_l(size, rng, cell=4, blur=2), 2.0, -40),
                  [(0, STONE_DEEP), (0.45, STONE), (1, STONE_LIGHT)]).convert("RGBA")
    peb_in = shrink(pebbles, g.r(1.2))
    moss = union(g.ell(56, 56, 5, 2.2), g.ell(78, 62, 4, 1.8))
    return {"sil": sil, "body": body, "sheen": (120, 104, 96), "sheen_a": 0.5, "sheen_at": (60, 80, 22),
            "paint": [clip(stone, union(shell, peb_in)), paint(size, INK, inter(plates, shell), 0.9),
                      paint(size, MOSS, inter(moss, shell), 0.85),
                      paint(size, BONE, inter(minus(shell, shift(shell, 0, g.r(1.4))), shell), 0.6)],
            "eyes": [(64, 85, 10.5)]}


def v_manor(g, rng):
    """The Flying Manor: a winged Nevron hovering on violet light, its feet dripping away."""
    size = g.size
    body = g.disc(64, 58, 21, rng, wobble=0.07)
    sp = g.spines(64, 58, 18, [(-122, 6), (-90, 9), (-58, 6)])
    lw = [(46, 50), (36, 32), (20, 22), (6, 28), (10, 44), (17, 42), (21, 55), (29, 51), (35, 64), (46, 62)]
    wings = union(g.poly(lw), g.poly([(128 - x, y) for x, y in lw]))
    drips = union(g.taper([(56, 74), (55, 84), (54, 92)], 6, 3), g.taper([(64, 76), (64, 88), (64, 98)], 7, 3),
                  g.taper([(72, 74), (73, 82), (74, 89)], 6, 3), g.ell(54, 93, 2.6, 3), g.ell(64, 99, 3, 3.4), g.ell(74, 90, 2.4, 2.8))
    loose = union(g.ell(52, 103, 2.2, 2.6), g.ell(64, 110, 2.8, 3.2), g.ell(76, 100, 1.8, 2.2))
    sil = union(body, sp, wings, drips, loose)
    ribs_l = [[(46, 52), (8, 29)], [(46, 54), (17, 43)], [(46, 57), (29, 52)]]
    ribs = union(*[g.line(p, 1.0) for p in ribs_l], *[g.line([(128 - a, b) for a, b in p], 1.0) for p in ribs_l])
    wing_in = shrink(wings, g.r(1.4))
    wash = map_l(linear_light(size, 90), [(0, VIOLET_LIGHT), (1, VIOLET_DEEP)]).convert("RGBA")
    hover = g.ell(64, 112, 24, 5)
    return {"sil": sil, "body": body, "sheen": VIOLET, "sheen_a": 0.75, "sheen_at": (56, 48, 24),
            "aura": [glow(hover, VIOLET, g.r(5), 1.3)],
            "paint": [clip(wash, scale_l(wing_in, 0.55)), paint(size, LILAC, inter(ribs, wing_in), 0.8)],
            "eyes": [(64, 58, 12)]}


def v_cliffs(g, rng):
    """Stone Wave Cliffs: a crest of layered stone curling over like a breaking wave; strata on the body; block feet."""
    size = g.size
    body = g.disc(64, 75, 26, rng, wobble=0.06, sy=0.9)
    path = curve((44, 58), (36, 16), (74, 14), 16) + curve((74, 14), (102, 14), (98, 34), 10)[1:] + \
        curve((98, 34), (96, 42), (86, 40), 6)[1:]
    n = len(path) - 1
    crest = taper_mask(size, g.ps(path), [g.r(14 - 10 * (i / n) ** 1.4) for i in range(n)])
    feet = union(g.poly([(44, 92), (42, 110), (58, 110), (57, 94)]), g.poly([(71, 94), (70, 110), (86, 110), (84, 92)]))
    sil = union(body, crest, feet, g.arms(64, 75, reach=21, drop=6))
    crest_in = shrink(crest, g.r(1.4))
    bands = g.blank()
    d = ImageDraw.Draw(bands)
    for k in range(0, 12):
        y = 10 + k * 4.2
        d.line(g.ps([(20, y + 3), (64, y - 2), (110, y + 3)]), fill=255 if k % 2 else 110, width=int(g.r(2)))
    strata = union(g.line(curve((44, 86), (64, 80), (86, 86), 12), 1.3), g.line(curve((50, 93), (64, 88), (80, 93), 12), 1.1))
    return {"sil": sil, "body": body, "sheen": TEAL_DEEP, "sheen_a": 0.7, "sheen_at": (54, 66, 28),
            "paint": [paint(size, STONE_DEEP, crest_in), paint(size, (110, 168, 170), inter(bands, crest_in), 0.85),
                      paint(size, LIGHT_TEAL, inter(minus(crest_in, shift(crest_in, g.r(1.2), g.r(1.6))), crest_in), 0.8),
                      paint(size, (120, 170, 172), inter(strata, shrink(body, g.r(2))), 0.75)],
            "eyes": [(64, 74, 13)]}


def v_river(g, rng):
    """The Sacred River: a finned Nevron, ribbed dorsal fin and fan arms, standing in a ripple."""
    size = g.size
    body = g.disc(64, 66, 25, rng, wobble=0.06, sy=0.95)
    tips, ribs = [], []
    angles = [-156, -128, -100, -72, -44]
    for i, deg in enumerate(angles):
        a = math.radians(deg)
        ln = 15 + 6 * math.sin(math.pi * (i + 0.5) / len(angles))
        base = (64 + math.cos(a) * 18, 66 + math.sin(a) * 17)
        tip = (64 + math.cos(a) * (22 + ln), 66 + math.sin(a) * (21 + ln))
        tips.append(tip)
        ribs.append((base, tip))
    fin_pts = [(64 + math.cos(math.radians(-170)) * 20, 66 + math.sin(math.radians(-170)) * 19)]
    for i, tip in enumerate(tips):
        fin_pts.append(tip)
        if i < len(tips) - 1:
            a = math.radians((angles[i] + angles[i + 1]) / 2)
            fin_pts.append((64 + math.cos(a) * 29, 66 + math.sin(a) * 28))
    fin_pts.append((64 + math.cos(math.radians(-10)) * 20, 66 + math.sin(math.radians(-10)) * 19))
    fin = g.poly(fin_pts)
    rib_m = union(*[g.taper([b, t], 3.4, 1.6) for b, t in ribs])
    lf = [(42, 70), (24, 60), (16, 68), (18, 78), (24, 84), (42, 78)]
    fans = union(g.poly(lf), g.poly([(128 - x, y) for x, y in lf]))
    fan_ribs = union(*[g.line([(42, 74), p], 0.9) for p in ((20, 63), (16, 72), (22, 82))],
                     *[g.line([(86, 74), (128 - p[0], p[1])], 0.9) for p in ((20, 63), (16, 72), (22, 82))])
    sil = union(body, fin, rib_m, fans, g.feet(64, 86, 106))
    fin_in = shrink(union(fin, fans), g.r(1.3))
    wash = map_l(linear_light(size, 90), [(0, LIGHT_TEAL), (1, TEAL_DEEP)]).convert("RGBA")
    ripple = union(minus(g.ell(64, 110, 34, 6), g.ell(64, 110, 31.5, 4.6)), minus(g.ell(64, 110, 46, 9), g.ell(64, 110, 44, 7.8)))
    stripes = union(g.line(curve((46, 80), (55, 76), (64, 80), 8) + curve((64, 80), (73, 84), (82, 80), 8), 1.4))
    drops = union(g.ell(28, 28, 2.2, 3), g.ell(104, 36, 2, 2.8))
    return {"sil": sil, "body": body, "sheen": TEAL, "sheen_a": 0.6, "sheen_at": (54, 56, 28),
            "aura": [paint(size, TEAL, ripple, 0.8)],
            "paint": [clip(wash, scale_l(minus(fin_in, rib_m), 0.8)), paint(size, INK, inter(fan_ribs, fans), 0.9),
                      paint(size, LIGHT_TEAL, inter(stripes, shrink(body, g.r(2))), 0.8)],
            "eyes": [(64, 66, 14)],
            "over": [paint(size, INK, minus(grow(drops, g.r(0.8)), drops), 0.8), paint(size, LIGHT_TEAL, drops)]}


REGIONS = [
    ("nevron", v_nevron),
    ("woodland-fox", lambda g, rng: v_woodland(g, rng, "fox")),
    ("woodland-rabbit", lambda g, rng: v_woodland(g, rng, "rabbit")),
    ("woodland-mouse", lambda g, rng: v_woodland(g, rng, "mouse")),
    ("lumiere", v_lumiere),
    ("old-lumiere", v_old_lumiere),
    ("monolith", v_monolith),
    ("visages", v_visages),
    ("sirene", v_sirene),
    ("the-reacher", v_reacher),
    ("esquies-nest", v_nest),
    ("flying-manor", v_manor),
    ("stone-wave-cliffs", v_cliffs),
    ("sacred-river", v_river),
]


def region_sheet():
    cell, F, cols = 128, 4, 7
    rows = -(-len(REGIONS) // cols)
    sheet = canvas(cols * cell, rows * cell)
    sprites = []
    for i, (name, fn) in enumerate(REGIONS):
        g = Grid((cell * F, cell * F), F)
        img = downsample(finish(g, fn(g, random.Random(SEED + 100 + i))), F)
        x, y = (i % cols) * cell, (i // cols) * cell
        sheet.alpha_composite(img, (x, y))
        sprites.append({"name": name, "box": [x, y, x + cell, y + cell]})
    return sheet, sprites


# --------------------------------------------------------------------------------------------------------- melt

PAWN = [(0, 44), (3, 47), (10, 47), (14, 42), (18, 36), (34, 31), (54, 25), (72, 19), (80, 18), (82, 25), (88, 25),
        (91, 15), (95, 14)]
HEAD_C, HEAD_R, PAWN_TOP = 111, 22, 133


def pawn_outline(m, lean, ph, F, gx=128, gy=200):
    """The pawn's outline (256-space profile), sagging and spreading as it melts by m in 0..1."""
    right = [(hw, h) for h, hw in PAWN]
    a0 = math.pi - math.asin(14 / HEAD_R)
    for i in range(1, 15):
        a = a0 * (1 - i / 14)
        right.append((HEAD_R * math.sin(a), HEAD_C + HEAD_R * math.cos(a)))
    left = [(-x, h) for x, h in reversed(right)]
    out = []
    for side, pts in ((1, right), (-1, left)):
        for x, h in pts:
            t = min(1.0, max(0.0, h / PAWN_TOP))
            h2 = h * (1 - m * (0.5 + 0.45 * t))
            x2 = x * (1 + m * 1.1 * (1 - t) ** 2 + m * 0.25) + lean * m * t ** 1.5 * 40
            x2 += m * 5 * math.sin(h * 0.13 + ph + side)
            out.append(((gx + x2) * F, (gy - h2) * F))
    return out


def ink_fill(size, mask, rng, light_at, light_r, gloss=0.6):
    """Wet violet-black paint: ink with a violet sheen."""
    layer = solid(size, INK, mask)
    sheen = scale_l(radial_light(size, light_at, light_r), gloss)
    layer = comp(layer, clip(solid(size, VIOLET, sheen), mask))
    tex = noise_l(size, rng, cell=6, blur=3).point(lambda v: max(0, v - 150))
    return comp(layer, clip(solid(size, VIOLET_DEEP, tex), mask))


def puddle(size, F, rng, r, sy=0.28, gx=128, gy=200):
    m = rough_disc(size, (gx * F, gy * F), r * F, rng, wobble=0.09, sy=sy)
    lobes = Image.new("L", size, 0)
    d = ImageDraw.Draw(lobes)
    for _ in range(5):
        a = rng.uniform(0, 2 * math.pi)
        rr = r * rng.uniform(0.75, 0.98)
        cx, cy = (gx + math.cos(a) * rr) * F, (gy + math.sin(a) * rr * sy) * F
        lr = r * rng.uniform(0.12, 0.2) * F
        d.ellipse((cx - lr, cy - lr * 0.6, cx + lr, cy + lr * 0.6), fill=255)
    return union(m, lobes)


def gloss_lines(size, F, gx, gy, r, sy, rng, alpha=160):
    """Thin lilac highlights along a puddle's far edge."""
    m = Image.new("L", size, 0)
    d = ImageDraw.Draw(m)
    for a0, a1, k in ((200, 250, 0.72), (275, 320, 0.6), (215, 230, 0.45)):
        pts = [((gx + math.cos(math.radians(a)) * r * k) * F, (gy + math.sin(math.radians(a)) * r * k * sy) * F)
               for a in range(a0, a1 + 1, 3)]
        d.line(pts, fill=alpha, width=max(2, int(F * 1.6)), joint="curve")
    return m.filter(ImageFilter.GaussianBlur(F * 0.6))


def bone_pawn(size, F, sil, rng):
    paper = noise_l(size, rng, cell=2, blur=1.2)
    light = linear_light(size, 180)
    shade = ImageChops.add(scale_l(light, 0.75), scale_l(paper, 0.25))
    fill = map_l(shade, [(0, (176, 160, 150)), (0.45, PAPER), (0.8, BONE), (1, (252, 248, 240))]).convert("RGBA")
    return clip(fill, sil)


def melt_frame(stage, rng):
    """One frame of the melt: 0 piece, 1 drip, 2 slump, 3 pool, 4 rise, 5 nevron."""
    S, F = 256, 2
    size = (S * F, S * F)
    gx, gy = 128, 200
    img = canvas(*size)
    m_by = {0: 0.0, 1: 0.06, 2: 0.42, 3: 0.8}
    pool_r = {0: 0, 1: 30, 2: 62, 3: 104, 4: 100, 5: 82}[stage]
    shadow = Image.new("L", size, 0)
    ImageDraw.Draw(shadow).ellipse(((gx - 60) * F, (gy - 9) * F, (gx + 60) * F, (gy + 9) * F), fill=120)
    img = comp(img, solid(size, INK, shadow.filter(ImageFilter.GaussianBlur(5 * F))))
    pool = puddle(size, F, random.Random(SEED + 40 + stage), pool_r) if pool_r else Image.new("L", size, 0)
    if pool_r:
        img = comp(img, glow(pool, VIOLET, 7 * F, 0.9))
    if stage <= 3:
        m = m_by[stage]
        pts = pawn_outline(m, 1.0, 0.7, F, gx, gy)
        sil = Image.new("L", size, 0)
        ImageDraw.Draw(sil).polygon(pts, fill=255)
        sil = sil.filter(ImageFilter.GaussianBlur(F * 0.8)).point(lambda v: 255 if v > 128 else 0)
        top_y = min(p[1] for p in pts)
        bot_y = max(p[1] for p in pts)
        cover = {0: 0.0, 1: 0.36, 2: 0.78, 3: 1.05}[stage]
        level = top_y + (bot_y - top_y) * cover
        wav = Image.new("L", size, 0)
        wpts = [(0, 0), (size[0], 0)]
        for x in range(size[0], -1, -8):
            wpts.append((x, level + math.sin(x / (9 * F) + stage) * 3 * F))
        ImageDraw.Draw(wav).polygon(wpts, fill=255)
        ink = inter(sil, wav) if cover > 0 else Image.new("L", size, 0)
        drips = Image.new("L", size, 0)
        if stage in (1, 2):
            row = [x for x in range(size[0]) if sil.getpixel((x, min(size[1] - 1, int(level + 2 * F)))) > 0]
            if row:
                x_lo, x_hi = row[0], row[-1]
                n = 5 if stage == 1 else 6
                for k in range(n):
                    x = x_lo + (x_hi - x_lo) * (k + 0.5) / n + rng.uniform(-4, 4) * F
                    ln = rng.uniform(16, 44) * F if stage == 1 else rng.uniform(20, 60) * F
                    y0 = level - 4 * F
                    y1 = min(bot_y - 3 * F, level + ln)
                    d = taper_mask(size, [(x, y0), (x + rng.uniform(-1, 1) * F, (y0 + y1) / 2), (x, y1)], [7 * F, 5 * F])
                    d = union(d, Image.new("L", size, 0))
                    ImageDraw.Draw(d).ellipse((x - 4.2 * F, y1 - 3 * F, x + 4.2 * F, y1 + 5 * F), fill=255)
                    drips = union(drips, d)
                drips = inter(drips, grow(sil, 2 * F))
        if stage == 0:
            cap = inter(sil, Image.new("L", size, 0))
            capm = Image.new("L", size, 0)
            ImageDraw.Draw(capm).ellipse(((gx - 13) * F, (gy - 136) * F, (gx + 11) * F, (gy - 124) * F), fill=255)
            cap = inter(grow(capm, F), grow(sil, F))
            ink = union(ink, cap)
            run = taper_mask(size, [((gx + 6) * F, (gy - 126) * F), ((gx + 9) * F, (gy - 117) * F), ((gx + 10) * F, (gy - 112) * F)],
                             [5 * F, 3.6 * F])
            ImageDraw.Draw(run).ellipse(((gx + 7) * F, (gy - 115) * F, (gx + 13) * F, (gy - 108) * F), fill=255)
            ink = union(ink, inter(run, grow(sil, F)))
        ink = union(ink, drips)
        whole = union(sil, ink, pool)
        img = comp(img, solid(size, INK, minus(grow(whole, 2.4 * F), whole)))
        if pool_r:
            img = comp(img, ink_fill(size, pool, rng, ((gx - pool_r * 0.4) * F, (gy - 4) * F), pool_r * F, 0.5))
            img = comp(img, solid(size, LILAC, inter(gloss_lines(size, F, gx, gy, pool_r, 0.28, rng), pool)))
        img = comp(img, bone_pawn(size, F, sil, rng))
        edge = minus(sil, shrink(sil, F * 1.4))
        img = comp(img, solid(size, INK, edge))
        img = comp(img, glow(ink, VIOLET, 5 * F, 0.8))
        img = comp(img, ink_fill(size, ink, rng, ((gx - 14) * F, top_y + 18 * F), 60 * F, 0.65))
        spec = Image.new("L", size, 0)
        sd = ImageDraw.Draw(spec)
        if stage == 0:
            drop_c = (gx - 2, gy - 158)
            dm = Image.new("L", size, 0)
            ImageDraw.Draw(dm).polygon([((drop_c[0] + 4.5 * math.sin(a)) * F, (drop_c[1] - 6 * math.cos(a) * (1.9 if math.cos(a) > 0 else 1)) * F)
                                        for a in [i / 30 * 2 * math.pi for i in range(30)]], fill=255)
            img = comp(img, glow(dm, VIOLET, 4 * F, 1.0), solid(size, INK, grow(dm, F)), ink_fill(size, dm, rng, (drop_c[0] * F, (drop_c[1]) * F), 6 * F, 0.8))
            for k in range(3):
                sd.line([((gx - 6 + k * 5) * F, (gy - 176 + k * 2) * F), ((gx - 6 + k * 5) * F, (gy - 168 + k * 2) * F)], fill=120, width=F)
            img = comp(img, solid(size, VIOLET_LIGHT, spec))
            spec = Image.new("L", size, 0)
            sd = ImageDraw.Draw(spec)
            for x, y, r in ((-30, -150, 2.2), (26, -144, 1.8), (-22, -120, 1.4)):
                sd.ellipse(((gx + x - r) * F, (gy + y - r) * F, (gx + x + r) * F, (gy + y + r) * F), fill=200)
            img = comp(img, solid(size, VIOLET_LIGHT, spec))
        else:
            hx = gx + (40 * m * 0.85 if stage < 3 else 0)
            hy = top_y / F + (14 if stage < 3 else 8)
            sd.arc(((hx - 16) * F, (hy - 10) * F, (hx + 6) * F, (hy + 14) * F), 190, 250, fill=190, width=int(2.2 * F))
            img = comp(img, solid(size, LILAC, inter(spec.filter(ImageFilter.GaussianBlur(F * 0.5)), ink)))
        if stage == 3:
            flecks = Image.new("L", size, 0)
            fd = ImageDraw.Draw(flecks)
            for x, y, w, h, a in ((-62, 4, 9, 5, 20), (54, 6, 8, 4, -15), (-30, 12, 6, 3, 40), (74, -2, 5, 3, 10)):
                cx, cy = (gx + x) * F, (gy + y) * F
                ca, sa = math.cos(math.radians(a)), math.sin(math.radians(a))
                pts = [(cx + (px * ca - py * sa) * F, cy + (px * sa + py * ca) * F) for px, py in
                       ((-w, 0), (-w * 0.3, -h), (w, -h * 0.4), (w * 0.6, h * 0.6), (-w * 0.5, h))]
                fd.polygon(pts, fill=255)
            img = comp(img, solid(size, INK, grow(flecks, F * 1.2)), bone_pawn(size, F, flecks, rng))
    else:
        img = comp(img, solid(size, INK, minus(grow(pool, 2.4 * F), pool)))
        img = comp(img, ink_fill(size, pool, rng, ((gx - pool_r * 0.4) * F, (gy - 4) * F), pool_r * F, 0.5))
        img = comp(img, solid(size, LILAC, inter(gloss_lines(size, F, gx, gy, pool_r, 0.28, rng), pool)))
        if stage == 4:
            g = Grid(size, 1.55 * F, (gx - 64 * 1.55) * F, (gy - 4 - 96 * 1.55) * F)
            r = random.Random(SEED + 60)
            body = g.disc(64, 74, 28, r, wobble=0.1, sy=0.9)
            sp = g.spines(64, 74, 24, [(-150, 3), (-118, 6), (-90, 9), (-62, 6), (-30, 3)])
            stubs = union(g.tendril((40, 80), (30, 78), (28, 86), 7, 4), g.tendril((88, 80), (98, 78), (100, 86), 7, 4))
            cut = Image.new("L", size, 0)
            ImageDraw.Draw(cut).rectangle((0, 0, size[0], (gy - 2) * F), fill=255)
            blob = inter(union(body, sp, stubs), cut)
            whole = union(blob, pool)
            img = comp(img, glow(blob, VIOLET, 8 * F, 1.0))
            img = comp(img, solid(size, INK, minus(grow(whole, 2.4 * F), grow(pool, 2.4 * F))))
            img = comp(img, solid(size, INK, minus(grow(blob, 2.4 * F), whole)))
            img = comp(img, ink_fill(size, blob, rng, g.p(52, 60), g.r(36), 0.75))
            img = draw_eye(img, g, 64, 72, 15, blob, closed=True)
            rise = Image.new("L", size, 0)
            rd = ImageDraw.Draw(rise)
            for x, y, rr in ((-48, -40, 3.2), (44, -56, 2.6), (-28, -96, 2.2), (34, -104, 2.8), (6, -128, 2.0), (-60, -84, 1.8)):
                cx, cy = (gx + x) * F, (gy + y) * F
                rd.ellipse((cx - rr * F, cy - rr * 1.25 * F, cx + rr * F, cy + rr * 1.25 * F), fill=255)
            trails = Image.new("L", size, 0)
            td = ImageDraw.Draw(trails)
            for x, y, rr in ((-48, -40, 3.2), (44, -56, 2.6), (-28, -96, 2.2), (34, -104, 2.8)):
                td.line([((gx + x) * F, (gy + y + rr * 2) * F), ((gx + x) * F, (gy + y + rr * 2 + 14) * F)], fill=130, width=int(rr * F))
            img = comp(img, solid(size, VIOLET_LIGHT, trails.filter(ImageFilter.GaussianBlur(F))))
            img = comp(img, glow(rise, VIOLET, 3 * F, 1.2), solid(size, INK, rise))
            shard = Image.new("L", size, 0)
            ImageDraw.Draw(shard).polygon([((gx + 70) * F, (gy + 2) * F), ((gx + 78) * F, (gy - 4) * F), ((gx + 84) * F, (gy + 1) * F),
                                           ((gx + 78) * F, (gy + 5) * F)], fill=255)
            img = comp(img, solid(size, INK, grow(shard, F)), bone_pawn(size, F, shard, rng))
        else:
            g = Grid(size, 1.6 * F, (gx - 64 * 1.6) * F, (gy + 1 - 110 * 1.6) * F)
            parts = v_nevron(g, random.Random(SEED + 61))
            parts["aura"] = [glow(parts["sil"], VIOLET, 9 * F, 1.1)]
            parts["sheen"], parts["sheen_a"] = VIOLET, 0.7
            img = comp(img, finish(g, parts, halo=False, shadow=False))
            edge = minus(grow(parts["sil"], 2 * F), union(parts["sil"], pool))
            img = comp(img, solid(size, INK, scale_l(edge, 0.9)))
            sparks = Image.new("L", size, 0)
            sd = ImageDraw.Draw(sparks)
            for x, y, rr in ((-70, -60, 2.2), (72, -76, 1.8), (-58, -130, 1.6), (64, -132, 2.0)):
                cx, cy = (gx + x) * F, (gy + y) * F
                t = rr * 0.3
                sd.polygon([(cx - rr * 2 * F, cy), (cx - t * F, cy - t * F), (cx, cy - rr * 2 * F), (cx + t * F, cy - t * F),
                            (cx + rr * 2 * F, cy), (cx + t * F, cy + t * F), (cx, cy + rr * 2 * F), (cx - t * F, cy + t * F)], fill=255)
            img = comp(img, glow(sparks, MAGENTA, 3 * F, 1.0), solid(size, LILAC, sparks))
    return downsample(img, F)


def melt_sheet():
    names = ["piece", "drip", "slump", "pool", "rise", "nevron"]
    sheet = canvas(256 * len(names), 256)
    sprites = []
    for i, name in enumerate(names):
        sheet.alpha_composite(melt_frame(i, random.Random(SEED + 20 + i)), (i * 256, 0))
        sprites.append({"name": name, "box": [i * 256, 0, (i + 1) * 256, 256]})
    return sheet, sprites


# ------------------------------------------------------------------------------------------------- flying manor

def rock_tile(rs, rng, crystals=False, tuft=True):
    """One floating rock in a 3/4 view: lit top face, strata sides, a hanging point. Returns (tile, anchor)."""
    F = 2
    W = int(rs * 2.8 * F)
    H = int(rs * 3.2 * F)
    size = (W, H)
    cx, cy = W / 2, rs * 1.15 * F
    n = rng.randint(6, 8)
    angs = sorted(rng.uniform(0, 2 * math.pi) for _ in range(n))
    angs = [a + i * 0.0 for i, a in enumerate(angs)]
    base = [i / n * 2 * math.pi + rng.uniform(-0.25, 0.25) for i in range(n)]
    top_pts = [(cx + math.cos(a) * rs * F * rng.uniform(0.8, 1.05), cy + math.sin(a) * rs * F * 0.6 * rng.uniform(0.85, 1.1))
               for a in base]
    top = Image.new("L", size, 0)
    ImageDraw.Draw(top).polygon(top_pts, fill=255)
    depth = rs * rng.uniform(0.45, 0.6) * F
    apex = (cx + rng.uniform(-0.25, 0.25) * rs * F, cy + rs * F * rng.uniform(1.25, 1.55))
    xs = [p[0] for p in top_pts]
    lpt = top_pts[xs.index(min(xs))]
    rpt = top_pts[xs.index(max(xs))]
    hang = Image.new("L", size, 0)
    hd = ImageDraw.Draw(hang)
    hd.polygon([lpt, (lpt[0] + rs * 0.12 * F, lpt[1] + depth), (apex[0] - rs * 0.3 * F, apex[1] - rs * 0.4 * F), apex,
                (apex[0] + rs * 0.35 * F, apex[1] - rs * 0.5 * F), (rpt[0] - rs * 0.1 * F, rpt[1] + depth), rpt], fill=255)
    hd.polygon([(x, y + depth) for x, y in top_pts], fill=255)
    whole = union(top, hang)
    side = minus(whole, top)
    img = canvas(*size)
    img = comp(img, glow(shift(hang, 0, rs * 0.3 * F), VIOLET, rs * 0.35 * F, 0.55))
    img = comp(img, solid(size, INK, minus(grow(whole, 2.2 * F), whole)))
    side_col = map_l(ImageChops.add(linear_light(size, 270), noise_l(size, rng, cell=3, blur=1.2), 2.0, -50),
                     [(0, (44, 38, 52)), (0.5, STONE_DEEP), (1, (124, 112, 120))]).convert("RGBA")
    img = comp(img, clip(side_col, side))
    strata = Image.new("L", size, 0)
    sd = ImageDraw.Draw(strata)
    for k in range(1, 5):
        y = cy + depth * 0.2 + k * rs * 0.26 * F
        sd.line([(0, y + rng.uniform(-2, 2) * F), (W / 2, y + rng.uniform(1, 3) * F), (W, y + rng.uniform(-2, 2) * F)],
                fill=150, width=max(1, int(0.9 * F)))
    for _ in range(3):
        x = cx + rng.uniform(-0.6, 0.6) * rs * F
        sd.line([(x, cy + depth * 0.3), (x + rng.uniform(-3, 3) * F, cy + depth + rs * 0.4 * F)], fill=170, width=max(1, int(F)))
    img = comp(img, solid(size, (34, 28, 40), inter(strata, side)))
    top_col = map_l(ImageChops.add(linear_light(size, 200), noise_l(size, rng, cell=2, blur=1.0), 2.0, -30),
                    [(0, STONE), (0.55, STONE_LIGHT), (1, BONE)]).convert("RGBA")
    img = comp(img, clip(top_col, top))
    front = inter(minus(top, shift(top, 0, -1.6 * F)), top)
    img = comp(img, solid(size, INK, inter(minus(grow(top, 1.2 * F), top), side)))
    img = comp(img, solid(size, BONE, scale_l(inter(minus(top, shift(top, 0, 1.6 * F)), top), 0.7)))
    img = comp(img, solid(size, (70, 62, 74), scale_l(front, 0.35)))
    if tuft:
        g_m = Image.new("L", size, 0)
        gd = ImageDraw.Draw(g_m)
        bx, by = cx + rng.uniform(-0.5, 0.2) * rs * F, cy + rs * 0.3 * F
        for k in range(5):
            x = bx + k * 2.4 * F
            gd.line([(x, by), (x + rng.uniform(-2.5, 2.5) * F, by - rng.uniform(4, 8) * F)], fill=255, width=max(1, int(1.3 * F)))
        moss = Image.new("L", size, 0)
        ImageDraw.Draw(moss).ellipse((cx - rs * 0.5 * F, cy - rs * 0.25 * F, cx + rs * 0.2 * F, cy + rs * 0.12 * F), fill=255)
        moss = inter(moss.filter(ImageFilter.GaussianBlur(2 * F)), top)
        img = comp(img, solid(size, MOSS, scale_l(moss, 0.55)), solid(size, (60, 110, 86), g_m))
    if crystals:
        cm = Image.new("L", size, 0)
        cd = ImageDraw.Draw(cm)
        bx, by = cx + rs * 0.25 * F, cy + rs * 0.05 * F
        for dx, hgt, wd in ((-4, 14, 3.2), (1, 20, 3.8), (6, 12, 3.0)):
            x = bx + dx * F
            cd.polygon([(x - wd * F, by), (x - wd * 0.3 * F, by - hgt * F), (x + wd * 0.5 * F, by - hgt * 0.92 * F),
                        (x + wd * F, by)], fill=255)
        img = comp(img, glow(cm, VIOLET_LIGHT, 4 * F, 1.0), solid(size, INK, minus(grow(cm, F), cm)))
        cfill = map_l(linear_light(size, 160), [(0, VIOLET_DEEP), (0.5, VIOLET), (1, LILAC)]).convert("RGBA")
        img = comp(img, clip(cfill, cm))
    tile = downsample(img, F)
    return tile, (cx / F, cy / F)


def ring_rocks(rng):
    rocks = []
    n = 9
    for i in range(n):
        a = math.radians(i * 360 / n - 80 + rng.uniform(-9, 9))
        rs = rng.choice([22, 25, 28, 31, 34]) if i % 3 else 33
        rr = 178 + rng.uniform(-7, 7)
        tile, anchor = rock_tile(rs, random.Random(SEED + 300 + i), crystals=i in (2, 6), tuft=i % 2 == 0)
        rocks.append({"a": a, "r": rr, "rs": rs, "ph": rng.uniform(0, 2 * math.pi), "tile": tile, "anchor": anchor, "lift": 16})
    for i in range(8):
        a = math.radians((i + 0.5) * 360 / 8 - 60 + rng.uniform(-10, 10))
        rs = rng.uniform(7, 11)
        tile, anchor = rock_tile(rs, random.Random(SEED + 400 + i), tuft=False)
        rocks.append({"a": a, "r": 170 + rng.uniform(-14, 22), "rs": rs, "ph": rng.uniform(0, 2 * math.pi), "tile": tile,
                      "anchor": anchor, "lift": 24})
    return rocks


def float_frame(rocks, k, frames=4):
    S = 512
    F = 2
    size = (S * F, S * F)
    c = S / 2
    img = canvas(*size)
    mist = Image.new("L", size, 0)
    ImageDraw.Draw(mist).ellipse(((c - 196) * F, (c - 196) * F, (c + 196) * F, (c + 196) * F), fill=255)
    ImageDraw.Draw(mist).ellipse(((c - 152) * F, (c - 152) * F, (c + 152) * F, (c + 152) * F), fill=0)
    img = comp(img, solid(size, VIOLET, scale_l(mist.filter(ImageFilter.GaussianBlur(14 * F)), 0.3)))
    shadows = Image.new("L", size, 0)
    sd = ImageDraw.Draw(shadows)
    placed = []
    for rk in rocks:
        lift = rk["lift"] + 7 * math.sin(2 * math.pi * k / frames + rk["ph"])
        gx, gy = c + math.cos(rk["a"]) * rk["r"], c + math.sin(rk["a"]) * rk["r"]
        rs = rk["rs"]
        sc = max(0.45, 1.0 - lift / 70)
        sx, sy = gx + lift * 0.3, gy + rs * 0.55 + lift * 0.25
        sd.ellipse(((sx - rs * 0.95 * sc) * F, (sy - rs * 0.4 * sc) * F, (sx + rs * 0.95 * sc) * F, (sy + rs * 0.4 * sc) * F),
                   fill=int(150 * sc))
        placed.append((gy, gx, gy - lift, rk))
    img = comp(img, solid(size, INK, shadows.filter(ImageFilter.GaussianBlur(4 * F))))
    for j in range(3):
        a0 = 30 * k + 120 * j + 10
        for rad, wd, col, al, off in ((214, 6, BONE, 175, 0), (204, 3.2, VIOLET_LIGHT, 150, 14)):
            pts = [((c + math.cos(math.radians(a)) * rad) * F, (c + math.sin(math.radians(a)) * rad) * F)
                   for a in [a0 + off + t * 1.5 for t in range(34)]]
            img = comp(img, brush_stroke(size, pts, wd * F, col, al, seed=SEED + 500 + j, bristles=5))
    out = downsample(img, F)
    for _, gx, top_y, rk in sorted(placed, key=lambda t: t[0]):
        ax, ay = rk["anchor"]
        out.alpha_composite(rk["tile"], (int(round(gx - ax)), int(round(top_y - ay))))
    return out


def underside(rng):
    """An earth rim hanging below a floating clearing (radius 124 around the cell centre), roots and falling pebbles."""
    S, F = 512, 2
    size = (S * F, S * F)
    c, R = S / 2, 124
    disc = Image.new("L", size, 0)
    ImageDraw.Draw(disc).ellipse(((c - R) * F, (c - R) * F, (c + R) * F, (c + R) * F), fill=255)
    pts = [((c + R + 3) * F, (c - 6) * F)]
    steps = 22
    for i in range(steps + 1):
        t = i / steps
        x = c + (R + 3) * math.cos(math.pi * t)
        depth = 40 + 150 * math.sin(math.pi * t) ** 1.6
        y = c + depth + rng.uniform(-6, 6) * math.sin(math.pi * t)
        pts.append((x * F, y * F))
    pts.append(((c - R - 3) * F, (c - 6) * F))
    mass = Image.new("L", size, 0)
    ImageDraw.Draw(mass).polygon(pts, fill=255)
    cut = Image.new("L", size, 0)
    ImageDraw.Draw(cut).rectangle((0, (c - 2) * F, size[0], size[1]), fill=255)
    mass = inter(mass, cut)
    band = minus(mass, disc)
    roots = Image.new("L", size, 0)
    for _ in range(9):
        x = c + rng.uniform(-0.75, 0.75) * R
        t = (x - (c - R)) / (2 * R)
        y0 = c + 40 + 150 * math.sin(math.pi * t) ** 1.6 - 8
        ln = rng.uniform(22, 54)
        sway = rng.uniform(-14, 14)
        p = curve((x, y0), (x + sway * 0.2, y0 + ln * 0.6), (x + sway, y0 + ln), 12)
        roots = union(roots, taper_mask(size, [(a * F, b * F) for a, b in p], [(3.2 - 2.4 * i / 11) * F for i in range(12)]))
    roots = minus(roots, disc)
    whole = union(band, roots)
    img = canvas(*size)
    img = comp(img, glow(shift(mass, 0, 30 * F), VIOLET, 26 * F, 0.6))
    img = comp(img, solid(size, INK, minus(grow(whole, 2.4 * F), union(whole, disc))))
    earth = map_l(ImageChops.add(linear_light(size, 270), noise_l(size, rng, cell=3, blur=1.5), 2.0, -40),
                  [(0, EARTH_DEEP), (0.5, EARTH), (0.8, (150, 112, 82)), (1, STONE)]).convert("RGBA")
    img = comp(img, clip(earth, band))
    strata = Image.new("L", size, 0)
    sd = ImageDraw.Draw(strata)
    for kk in range(1, 9):
        rr = R + kk * 17
        box = ((c - rr * 1.02) * F, (c - rr * 0.55) * F, (c + rr * 1.02) * F, (c + rr * 0.55 + kk * 6) * F)
        sd.arc(box, 10, 170, fill=170, width=int(1.6 * F))
    for _ in range(14):
        x = c + rng.uniform(-0.9, 0.9) * R
        y = c + rng.uniform(30, 150)
        sd.line([(x * F, y * F), ((x + rng.uniform(-3, 3)) * F, (y + rng.uniform(8, 20)) * F)], fill=150, width=int(1.2 * F))
    img = comp(img, solid(size, EARTH_DEEP, inter(strata, band)))
    stones = Image.new("L", size, 0)
    std = ImageDraw.Draw(stones)
    for _ in range(12):
        x = c + rng.uniform(-0.8, 0.8) * R
        y = c + rng.uniform(40, 130)
        r = rng.uniform(3, 7)
        std.ellipse(((x - r) * F, (y - r * 0.7) * F, (x + r) * F, (y + r * 0.7) * F), fill=255)
    stones = inter(stones, shrink(band, 2 * F))
    img = comp(img, solid(size, INK, minus(grow(stones, F), stones)), solid(size, STONE_LIGHT, stones))
    rim = inter(minus(grow(disc, 3.5 * F), disc), band)
    img = comp(img, solid(size, MOSS, rim), solid(size, INK, inter(minus(grow(disc, 1.2 * F), disc), band)))
    img = comp(img, solid(size, (60, 46, 40), roots))
    peb = Image.new("L", size, 0)
    pd = ImageDraw.Draw(peb)
    for x, y, r in ((c - 40, c + 206, 4), (c + 30, c + 222, 3), (c - 4, c + 240, 2.4), (c + 64, c + 196, 2.6)):
        pd.polygon([((x - r) * F, y * F), (x * F, (y - r) * F), ((x + r) * F, (y - r * 0.2) * F), ((x + r * 0.3) * F, (y + r) * F)],
                   fill=255)
    img = comp(img, solid(size, INK, grow(peb, 1.4 * F)), solid(size, STONE, peb))
    return downsample(img, F)


def manor_sheet(rng):
    rocks = ring_rocks(rng)
    cell = 512
    names = ["float-1", "float-2", "float-3", "float-4", "underside"]
    sheet = canvas(cell * len(names), cell)
    sprites = []
    for k in range(4):
        sheet.alpha_composite(float_frame(rocks, k), (k * cell, 0))
    sheet.alpha_composite(underside(random.Random(SEED + 600)), (4 * cell, 0))
    for i, name in enumerate(names):
        sprites.append({"name": name, "box": [i * cell, 0, (i + 1) * cell, cell]})
    return sheet, sprites


# ----------------------------------------------------------------------------------------------------------- entry

def make(out_dir, fonts):
    out = []

    def emit(asset, rel, img, sprites=()):
        save(img, out_dir, rel)
        out.append({"asset": asset, "path": f"art/{rel}", "size": [img.width, img.height], "sprites": list(sprites)})

    sheet, sprites = melt_sheet()
    emit("asset.fx.clea-melt", "fx/clea-melt.png", sheet, sprites)
    sheet, sprites = manor_sheet(random.Random(SEED + 200))
    emit("asset.fx.flying-manor-float", "fx/flying-manor-float.png", sheet, sprites)
    sheet, sprites = region_sheet()
    emit("asset.nevron-region-sprites", "tokens/nevron-region-sprites.png", sheet, sprites)
    return out

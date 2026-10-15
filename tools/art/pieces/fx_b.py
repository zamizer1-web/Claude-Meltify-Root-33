"""Effects for Root 33's made-for-mod animations: Maelle's Aid (a keepsake handed over in a warm glow), Maelle's Disarm
(a rapier feint that flicks a card spinning away) and the White-Haired Man's Massacre (an ink-wipe swipe across a
clearing on the shore).

All original, drawn from scratch with Pillow in the shared painterly style (tools/art/style.py). The hand, blade, card
and keepsake are invented generic shapes; no character, logo, icon or interface from either game is copied. Every random
choice comes from a seeded random.Random (noise included), so the same code always paints the same pixels. Each piece is
a horizontal strip of frames listed as sprites, drawn at 2x and reduced so brush edges stay soft at overlay size.
"""
import math
import random
import sys
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageOps

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from style import (BONE, CRIMSON, CRIMSON_DEEP, GOLD, GOLD_CORE, GOLD_DEEP, INK, INK_SOFT, NIGHT, TEAL,  # noqa: E402
                   TEAL_DEEP, brush_stroke, canvas, curve, glow, inner_edge, solid)

SEED = 3350
F = 2  # supersampling factor
AMBER = (236, 160, 72)
EMBER = (255, 222, 166)
STEEL = (214, 216, 226)
STEEL_DEEP = (112, 114, 132)
LIGHT_TEAL = (176, 226, 222)
VIOLET_INK = (58, 34, 66)
SLEEVE = (66, 56, 70)


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
    lut = [ramp(stops, v / 255) for v in range(256)]
    return Image.merge("RGB", [mask.point([lut[v][c] for v in range(256)]) for c in range(3)])


def linear_light(size, angle):
    w, h = size
    d = int(math.hypot(w, h)) + 4
    g = Image.linear_gradient("L").resize((d, d)).rotate(angle, resample=Image.BICUBIC)
    return g.crop(((d - w) // 2, (d - h) // 2, (d - w) // 2 + w, (d - h) // 2 + h))


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


def rough_disc(size, c, r, rng, wobble=0.035, jitter=0.004, steps=220, sy=1.0, rot=0.0):
    """A disc (or ellipse, sy) whose rim wanders like a hand-cut brush edge, optionally rotated by rot radians."""
    harm = [(k, rng.uniform(0, 2 * math.pi), rng.uniform(0.35, 1.0) / k) for k in range(2, 8)]
    ca, sa = math.cos(rot), math.sin(rot)
    pts = []
    for i in range(steps):
        a = 2 * math.pi * i / steps
        f = 1 + wobble * sum(amp * math.sin(k * a + ph) for k, ph, amp in harm) + rng.uniform(-jitter, jitter)
        x, y = math.cos(a) * r * f, math.sin(a) * r * f * sy
        pts.append((c[0] + x * ca - y * sa, c[1] + x * sa + y * ca))
    m = Image.new("L", size, 0)
    ImageDraw.Draw(m).polygon(pts, fill=255)
    return m


def ring_mask(size, c, r_in, r_out, sy=1.0):
    m = Image.new("L", size, 0)
    d = ImageDraw.Draw(m)
    d.ellipse((c[0] - r_out, c[1] - r_out * sy, c[0] + r_out, c[1] + r_out * sy), fill=255)
    if r_in > 0:
        d.ellipse((c[0] - r_in, c[1] - r_in * sy, c[0] + r_in, c[1] + r_in * sy), fill=0)
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


def union(*masks):
    out = masks[0]
    for m in masks[1:]:
        out = ImageChops.lighter(out, m)
    return out


def shift(mask, dx, dy):
    out = Image.new(mask.mode, mask.size, 0)
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


def taper_mask(size, pts, widths, fill=255):
    """A line whose width changes point by point (round joints)."""
    m = Image.new("L", size, 0)
    d = ImageDraw.Draw(m)
    for (a, b), w in zip(zip(pts, pts[1:]), widths):
        d.line([a, b], fill=fill, width=max(1, int(round(w))))
        r = w / 2
        d.ellipse((b[0] - r, b[1] - r, b[0] + r, b[1] + r), fill=fill)
    return m


def smooth(pts, rounds=3, closed=True):
    """Chaikin corner cutting, for soft hand-drawn outlines."""
    for _ in range(rounds):
        out = []
        n = len(pts)
        rng_ = range(n) if closed else range(n - 1)
        for i in rng_:
            (x0, y0), (x1, y1) = pts[i], pts[(i + 1) % n]
            out += [(0.75 * x0 + 0.25 * x1, 0.75 * y0 + 0.25 * y1), (0.25 * x0 + 0.75 * x1, 0.25 * y0 + 0.75 * y1)]
        pts = out if closed else [pts[0]] + out + [pts[-1]]
    return pts


def ellipse_pts(c, rx, ry, ang, n=36):
    ca, sa = math.cos(ang), math.sin(ang)
    out = []
    for i in range(n):
        t = 2 * math.pi * i / n
        x, y = math.cos(t) * rx, math.sin(t) * ry
        out.append((c[0] + x * ca - y * sa, c[1] + x * sa + y * ca))
    return out


def soft_disc(size, c, r, blur, strength=1.0, sy=1.0):
    m = Image.new("L", size, 0)
    ImageDraw.Draw(m).ellipse((c[0] - r, c[1] - r * sy, c[0] + r, c[1] + r * sy), fill=255)
    m = m.filter(ImageFilter.GaussianBlur(blur))
    return scale_l(m, strength) if strength != 1.0 else m


def bloom(size, c, r, strength=1.0):
    """A warm three-layer glow: amber haze, gold body, near-white heart."""
    out = canvas(*size)
    out = comp(out, solid(size, AMBER, soft_disc(size, c, r * 0.9, r * 0.7, 0.55 * strength)))
    out = comp(out, solid(size, GOLD, soft_disc(size, c, r * 0.5, r * 0.4, 0.75 * strength)))
    out = comp(out, solid(size, GOLD_CORE, soft_disc(size, c, r * 0.2, r * 0.18, min(1.0, 1.0 * strength))))
    return out


def edge_fade(size, border):
    """Opaque inside, fading to clear over `border` px at every edge, so nothing in a frame touches its cell edge."""
    w, h = size
    m = Image.new("L", size, 0)
    d = ImageDraw.Draw(m)
    steps = 16
    for k in range(steps + 1):
        inset = border * (1 - k / steps)
        d.rectangle((inset, inset, w - 1 - inset, h - 1 - inset), fill=int(255 * k / steps))
    return m


def petal_tile(length, rgb, shade, rng, rot, ss=F):
    """One small shaded petal tile (RGBA, already at supersampled scale), cropped to its bounds."""
    L = length * ss
    Wd = L * 0.6
    T = int(L * 1.7) + 4
    size = (T, T)
    cx, cy = T / 2, T / 2
    bend = rng.uniform(-0.2, 0.2) * Wd
    pts = []
    for i in range(48):
        a = i / 48 * 2 * math.pi
        r = 1 - 0.38 * (1 + math.cos(a)) / 2
        y = math.cos(a) * L * 0.5
        x = math.sin(a) * Wd * 0.5 * r + bend * (y / (L * 0.5)) ** 2
        pts.append((cx + x, cy - y))
    m = poly_mask(size, pts).filter(ImageFilter.GaussianBlur(ss * 0.3))
    grad = Image.linear_gradient("L").resize(size)
    body = map_l(grad, [(0, lerp_rgb(rgb, BONE, 0.15)), (0.5, rgb), (1, shade)]).convert("RGBA")
    body.putalpha(m)
    vein = Image.new("L", size, 0)
    ImageDraw.Draw(vein).line([(cx, cy + L * 0.38), (cx + bend * 0.3, cy - L * 0.25)], fill=110, width=max(1, int(ss * 0.8)))
    body = comp(body, clip(solid(size, shade, vein), m))
    body = comp(body, solid(size, lerp_rgb(shade, INK, 0.4), scale_l(inner_edge(m.point(lambda v: 255 if v > 128 else 0), max(1, ss // 2)), 0.7)))
    body = body.rotate(rot, resample=Image.BICUBIC, expand=True)
    bb = body.getbbox()
    return body.crop(bb) if bb else body


def paste_center(img, tile, c):
    img.alpha_composite(tile, (int(round(c[0] - tile.width / 2)), int(round(c[1] - tile.height / 2))))
    return img


def downsample(img, f):
    return img.resize((img.width // f, img.height // f), Image.LANCZOS)


def save(img, out_dir, rel):
    p = Path(out_dir) / rel
    p.parent.mkdir(parents=True, exist_ok=True)
    img.save(p, optimize=True)
    return p


def strip(frames, names, extra=()):
    """Lay equal frames side by side; extra = [(name, tile, (x, y))] placed after them. Returns (sheet, sprites)."""
    fw, fh = frames[0].size
    W = fw * len(frames) + sum(t.width for _, t, _ in extra)
    sheet = canvas(W, fh)
    sprites = []
    for i, (fr, nm) in enumerate(zip(frames, names)):
        sheet.alpha_composite(fr, (i * fw, 0))
        sprites.append({"name": nm, "box": [i * fw, 0, (i + 1) * fw, fh]})
    x = fw * len(frames)
    for nm, tile, (dx, dy) in extra:
        sheet.alpha_composite(tile, (x + dx, dy))
        sprites.append({"name": nm, "box": [x + dx, dy, x + dx + tile.width, dy + tile.height]})
        x += tile.width
    return sheet, sprites


# ----------------------------------------------------------------------------------------------------- the keepsake

def locket(size, c, r, rng, tilt=0.0):
    """The keepsake: an oval gold locket with a bone enamel face, a painted crimson five-petal flower and a bail ring."""
    x, y = c
    sy = 1.18
    out = canvas(*size)
    body = rough_disc(size, c, r, rng, wobble=0.012, sy=sy, rot=tilt)
    face = rough_disc(size, c, r * 0.7, rng, wobble=0.018, sy=sy, rot=tilt)
    bx, by = x + math.sin(tilt) * r * (sy + 0.2), y - math.cos(tilt) * r * (sy + 0.2)
    bail = ring_mask(size, (bx, by), r * 0.13, r * 0.3)
    whole = union(body, bail)
    out = comp(out, solid(size, INK, ring_out(whole, max(2, int(r * 0.1)))))
    out = comp(out, gold_paint(size, whole, rng, angle=40, bands=1.1))
    # A deep bevel between rim and face, then the enamel.
    out = comp(out, solid(size, GOLD_DEEP, ring_out(face, max(1, int(r * 0.07)))))
    out = comp(out, paper_fill(size, face, rng, base=BONE, amount=0.22))
    # A tiny painted flower: five crimson petals, a gold heart.
    fl = Image.new("L", size, 0)
    d = ImageDraw.Draw(fl)
    pr = r * 0.2
    for k in range(5):
        a = -math.pi / 2 + tilt + k * 2 * math.pi / 5
        d.polygon(ellipse_pts((x + math.cos(a) * pr, y + math.sin(a) * pr), pr * 0.95, pr * 0.55, a), fill=255)
    out = comp(out, solid(size, CRIMSON, fl), solid(size, CRIMSON_DEEP, scale_l(inner_edge(fl, max(1, int(r * 0.04))), 0.8)))
    heart = Image.new("L", size, 0)
    ImageDraw.Draw(heart).ellipse((x - pr * 0.42, y - pr * 0.42, x + pr * 0.42, y + pr * 0.42), fill=255)
    out = comp(out, gold_paint(size, heart, rng, bands=0.8))
    # Two small teal leaves below the flower.
    lv = Image.new("L", size, 0)
    for s in (-1, 1):
        a = math.pi / 2 + tilt + s * 0.62
        ImageDraw.Draw(lv).polygon(ellipse_pts((x + math.cos(a) * pr * 2.0, y + math.sin(a) * pr * 2.0), pr * 0.62, pr * 0.3, a), fill=255)
    out = comp(out, solid(size, TEAL_DEEP, lv))
    # Glaze: a soft highlight on the upper left of the enamel and a bright tick on the rim.
    hi = Image.new("L", size, 0)
    ImageDraw.Draw(hi).ellipse((x - r * 0.55, y - r * 0.75, x - r * 0.05, y - r * 0.3), fill=150)
    out = comp(out, clip(solid(size, (255, 255, 255), hi.filter(ImageFilter.GaussianBlur(r * 0.12))), face))
    tick = Image.new("L", size, 0)
    ImageDraw.Draw(tick).arc((x - r * 0.88, y - r * 0.88 * sy, x + r * 0.88, y + r * 0.88 * sy), 200, 245, fill=230,
                             width=max(1, int(r * 0.09)))
    out = comp(out, solid(size, GOLD_CORE, tick.filter(ImageFilter.GaussianBlur(0.8))))
    return out, (bx, by)


def ribbon(size, start, pts, w, rng):
    """A crimson ribbon tail trailing from the bail along pts (narrowing, with a darker underside)."""
    path = [start] + pts
    path = smooth(path, rounds=3, closed=False)
    n = len(path)
    widths = [w * (1 - 0.65 * i / n) for i in range(n)]
    m = taper_mask(size, path, widths)
    under = taper_mask(size, [(px, py + w * 0.25) for px, py in path], [x * 0.7 for x in widths])
    out = comp(solid(size, CRIMSON_DEEP, ImageChops.subtract(under, m)), solid(size, CRIMSON, m))
    out = comp(out, solid(size, (214, 70, 84), scale_l(inner_edge(m, max(1, int(w * 0.18))), 0.55)))
    return comp(solid(size, INK, scale_l(ring_out(m, 2), 0.8)), out)


# Palm up, fingers toward the right with their tips curling up in three small bumps; a thick thumb on the near side.
HAND = [(-30, 270), (24, 240), (60, 226), (96, 216), (122, 208), (140, 200), (151, 191), (155, 182), (152, 175),
        (147, 174), (144, 177), (142, 182), (138, 179), (133, 178), (129, 181), (128, 186), (123, 184), (118, 183),
        (114, 186), (112, 190), (100, 194), (86, 195), (76, 192), (68, 186), (66, 177), (70, 166), (74, 159),
        (72, 153), (64, 151), (54, 157), (45, 168), (38, 182), (22, 190), (-30, 206)]
CREASES = [((112, 190), (127, 188), (134, 196)), ((128, 185), (140, 186), (146, 194))]


def hand_layer(size, dx, dy, rng, light=1.0):
    """An open palm turned up, reaching in from the lower left: an ink silhouette with a warm rim light."""
    pts = smooth([((x + dx) * F, (y + dy) * F) for x, y in HAND], rounds=3)
    m = poly_mask(size, pts).filter(ImageFilter.GaussianBlur(F * 0.6)).point(lambda v: 255 if v > 128 else 0)
    out = canvas(*size)
    out = comp(out, glow(m, (0, 0, 0), 6 * F, 0.45))
    shade = map_l(linear_light(size, 90), [(0, (78, 66, 80)), (1, INK)]).convert("RGBA")
    shade.putalpha(m)
    out = comp(out, shade)
    # Finger creases and the sleeve cuff.
    crease = Image.new("L", size, 0)
    d = ImageDraw.Draw(crease)
    for a, b, c in CREASES:
        d.line(curve(((a[0] + dx) * F, (a[1] + dy) * F), ((b[0] + dx) * F, (b[1] + dy) * F), ((c[0] + dx) * F, (c[1] + dy) * F), 16),
               fill=170, width=max(1, int(1.3 * F)), joint="curve")
    # the thumb's edge against the palm
    d.line(curve(((50 + dx) * F, (184 + dy) * F), ((60 + dx) * F, (178 + dy) * F), ((64 + dx) * F, (168 + dy) * F), 12),
           fill=150, width=max(1, int(1.2 * F)), joint="curve")
    out = comp(out, clip(solid(size, (120, 104, 116), crease.filter(ImageFilter.GaussianBlur(0.6))), m))
    cuff_poly = [((12 + dx) * F, (176 + dy) * F), ((34 + dx) * F, (174 + dy) * F), ((46 + dx) * F, (236 + dy) * F),
                 ((20 + dx) * F, (250 + dy) * F)]
    cuff = ImageChops.multiply(poly_mask(size, cuff_poly), m)
    out = comp(out, solid(size, SLEEVE, cuff))
    band = ImageChops.multiply(poly_mask(size, [((34 + dx) * F, (174 + dy) * F), ((40 + dx) * F, (173 + dy) * F),
                                                ((52 + dx) * F, (236 + dy) * F), ((46 + dx) * F, (238 + dy) * F)]), m)
    out = comp(out, gold_paint(size, band, rng, angle=80))
    # Warm rim light along the upper edge, as if lit by the keepsake.
    top = ImageChops.subtract(m, shift(m, 0, 4 * F))
    out = comp(out, solid(size, GOLD, scale_l(top.filter(ImageFilter.GaussianBlur(1.2 * F)), 0.85 * light)))
    out = comp(out, solid(size, INK, scale_l(ring_out(m, F), 0.9)))
    return out


def motes(size, rng, c, r0, r1, count, a0=0, a1=360, up=0.0, sizes=(1.5, 4.0), alpha=(140, 255)):
    """Gold dust: sparkles and dots scattered in a ring, optionally drifting upward."""
    out = canvas(*size)
    for i in range(count):
        a = math.radians(rng.uniform(a0, a1))
        r = rng.uniform(r0, r1)
        x, y = c[0] + math.cos(a) * r, c[1] + math.sin(a) * r - up * rng.uniform(0, 1)
        s = rng.uniform(*sizes) * F
        al = rng.randint(*alpha)
        if i % 3 == 0:
            m = star((size), (x, y), s * 2.0, al)
            out = comp(out, solid(size, GOLD, scale_l(m.filter(ImageFilter.GaussianBlur(s * 0.8)), 0.6)),
                       solid(size, GOLD_CORE, m))
        else:
            m = Image.new("L", size, 0)
            ImageDraw.Draw(m).ellipse((x - s * 0.5, y - s * 0.5, x + s * 0.5, y + s * 0.5), fill=al)
            out = comp(out, solid(size, GOLD, scale_l(m.filter(ImageFilter.GaussianBlur(s)), 0.9)),
                       solid(size, rng.choice([GOLD_CORE, EMBER, GOLD]), m.filter(ImageFilter.GaussianBlur(0.5))))
    return out


def soft_rays(size, c, r0, r1, count, rng, rgb=GOLD, alpha=110, width=10):
    """Long tapered spokes of light around c."""
    m = Image.new("L", size, 0)
    d = ImageDraw.Draw(m)
    a0 = rng.uniform(0, 360)
    for k in range(count):
        a = math.radians(a0 + k * 360 / count + rng.uniform(-8, 8))
        ln = rng.uniform(0.7, 1.0) * (r1 - r0)
        w = width * F * rng.uniform(0.6, 1.2)
        px, py = -math.sin(a), math.cos(a)
        b = (c[0] + math.cos(a) * r0, c[1] + math.sin(a) * r0)
        t = (c[0] + math.cos(a) * (r0 + ln), c[1] + math.sin(a) * (r0 + ln))
        d.polygon([(b[0] + px * w, b[1] + py * w), t, (b[0] - px * w, b[1] - py * w)], fill=int(alpha * rng.uniform(0.6, 1.0)))
    return solid(size, rgb, m.filter(ImageFilter.GaussianBlur(3 * F)))


def keepsake_frame(stage, rng):
    """One 256x256 frame of the keepsake being handed over. stage: offer, lift, pass, glow."""
    S = 256
    size = (S * F, S * F)
    img = canvas(*size)
    trail_from = (100 * F, 166 * F)
    if stage == "offer":
        c, r, glow_r, hand_a, tilt = (100, 166), 19, 58, 1.0, -0.08
    elif stage == "lift":
        c, r, glow_r, hand_a, tilt = (122, 120), 21, 84, 1.0, 0.16
    elif stage == "pass":
        c, r, glow_r, hand_a, tilt = (160, 96), 22, 104, 0.55, 0.3
    else:
        c, r, glow_r, hand_a, tilt = (132, 112), 25, 124, 0.0, 0.0
    C = (c[0] * F, c[1] * F)

    light = bloom(size, C, glow_r * F, 1.0 if stage != "offer" else 0.75)
    if stage == "glow":
        light = comp(light, soft_rays(size, C, 18 * F, 110 * F, 12, rng, GOLD, 130, 9))
        light = comp(light, soft_rays(size, C, 14 * F, 76 * F, 9, rng, GOLD_CORE, 100, 5))
        ring = ring_mask(size, C, 62 * F, 66 * F).filter(ImageFilter.GaussianBlur(2.5 * F))
        light = comp(light, solid(size, GOLD, scale_l(ring, 0.6)))
        ring2 = ring_mask(size, C, 86 * F, 88 * F).filter(ImageFilter.GaussianBlur(3 * F))
        light = comp(light, solid(size, EMBER, scale_l(ring2, 0.35)))
    # Light stays a round pool inside the cell, so it never shows the frame's square edge.
    keep = soft_disc(size, (size[0] / 2, size[1] / 2), size[0] * 0.4, size[0] * 0.06)
    img = comp(img, clip(light, keep))
    if stage in ("lift", "pass"):
        # A gold trail of dust curving from the palm up to the keepsake.
        mid = ((trail_from[0] + C[0]) / 2 - 22 * F, (trail_from[1] + C[1]) / 2 + 6 * F) if stage == "lift" else \
              ((trail_from[0] + C[0]) / 2 - 10 * F, (trail_from[1] + C[1]) / 2 + 26 * F)
        pts = curve(trail_from, mid, C, 30)
        img = comp(img, brush_stroke(size, pts, 9 * F, GOLD, 120, seed=rng.randrange(1 << 30), bristles=5))
        img = comp(img, brush_stroke(size, pts, 3 * F, GOLD_CORE, 170, seed=rng.randrange(1 << 30), bristles=3))
        dust = canvas(*size)
        for i in range(14):
            t = rng.uniform(0.05, 0.92)
            x, y = pts[int(t * 30)]
            x += rng.uniform(-9, 9) * F
            y += rng.uniform(-9, 9) * F
            dust = comp(dust, motes(size, rng, (x, y), 0, 1, 1, sizes=(1.2, 3.2)))
        img = comp(img, dust)
    if hand_a > 0:
        img = comp(img, fade(hand_layer(size, -6, 6, rng, light=1.0), hand_a))
    # Ribbon tail, trailing opposite the motion.
    _, bail = locket(size, C, r * F, random.Random(7), tilt)
    if stage == "offer":
        tail = [(bail[0] - 8 * F, bail[1] - 2 * F), (bail[0] - 18 * F, bail[1] + 12 * F), (bail[0] - 30 * F, bail[1] + 18 * F)]
    elif stage == "lift":
        tail = [(bail[0] - 14 * F, bail[1] + 6 * F), (bail[0] - 22 * F, bail[1] + 26 * F), (bail[0] - 40 * F, bail[1] + 38 * F)]
    elif stage == "pass":
        tail = [(bail[0] - 18 * F, bail[1] + 2 * F), (bail[0] - 34 * F, bail[1] + 18 * F), (bail[0] - 56 * F, bail[1] + 22 * F)]
    else:
        # Held up in the light: the ribbon falls loose to both sides.
        tail = [(bail[0] + 12 * F, bail[1] - 5 * F), (bail[0] + 24 * F, bail[1] + 8 * F), (bail[0] + 32 * F, bail[1] + 4 * F)]
        img = comp(img, ribbon(size, bail, [(bail[0] - 12 * F, bail[1] - 4 * F), (bail[0] - 24 * F, bail[1] + 10 * F),
                                            (bail[0] - 30 * F, bail[1] + 24 * F)], 5.5 * F, rng))
    img = comp(img, ribbon(size, bail, tail, 5.5 * F, rng))
    lk, _ = locket(size, C, r * F, random.Random(7), tilt)
    img = comp(img, lk)
    # Dust around the keepsake, more as the gift takes hold.
    count = {"offer": 7, "lift": 12, "pass": 16, "glow": 26}[stage]
    img = comp(img, motes(size, rng, C, r * F * 1.6, glow_r * F * 0.85, count, up=12 * F))
    if stage == "glow":
        # Crimson petals lifted on the warmth.
        for k in range(6):
            a = math.radians(-150 + k * 52 + rng.uniform(-10, 10))
            rr = rng.uniform(58, 92) * F
            rgb, shade = ((BONE, (196, 180, 160)) if k % 3 == 1 else (CRIMSON if k % 2 else (196, 36, 54), CRIMSON_DEEP))
            tile = petal_tile(rng.uniform(15, 19), rgb, shade, rng, rng.uniform(0, 360))
            paste_center(img, tile, (C[0] + math.cos(a) * rr, C[1] + math.sin(a) * rr))
    img = clip(img, edge_fade(size, 10 * F))
    return downsample(img, F)


def keepsake_icon(rng):
    """The keepsake alone, 128x128, for a marker or tooltip."""
    S = 128
    size = (S * F, S * F)
    C = (S * F / 2, S * F / 2 + 8 * F)
    img = comp(canvas(*size), bloom(size, C, 52 * F, 0.85))
    lk, bail = locket(size, C, 30 * F, random.Random(7), 0.0)
    tail = [(bail[0] + 12 * F, bail[1] - 6 * F), (bail[0] + 24 * F, bail[1] + 6 * F), (bail[0] + 34 * F, bail[1] + 2 * F)]
    img = comp(img, ribbon(size, bail, tail, 6 * F, rng))
    tail2 = [(bail[0] - 12 * F, bail[1] - 4 * F), (bail[0] - 26 * F, bail[1] + 10 * F), (bail[0] - 32 * F, bail[1] + 22 * F)]
    img = comp(img, ribbon(size, bail, tail2, 6 * F, rng))
    img = comp(img, lk)
    img = comp(img, motes(size, rng, C, 36 * F, 54 * F, 7, sizes=(1.4, 3.0)))
    img = clip(img, edge_fade(size, 6 * F))
    return downsample(img, F)


def maelle_keepsake():
    rng = random.Random(SEED + 1)
    names = ["offer", "lift", "pass", "glow"]
    frames = [keepsake_frame(n, rng) for n in names]
    icon = keepsake_icon(rng)
    return strip(frames, names, [("keepsake", icon, (0, 64))])


# ---------------------------------------------------------------------------------------------- the rapier and card

def rapier(size, guard, ang, blade_len, rng, alpha=1.0):
    """A generic swept-hilt rapier: steel blade, gold quillons and knuckle bow, ink grip, gold pommel."""
    gx, gy = guard
    ax, ay = math.cos(ang), math.sin(ang)
    px, py = -ay, ax

    def P(a, p):
        return (gx + ax * a * F + px * p * F, gy + ay * a * F + py * p * F)

    out = canvas(*size)
    blade = poly_mask(size, [P(2, 4.0), P(blade_len * 0.18, 3.4), P(blade_len, 0.6), P(blade_len, -0.6), P(blade_len * 0.18, -3.4), P(2, -4.0)])
    out = comp(out, solid(size, INK, ring_out(blade, max(1, int(1.2 * F)))))
    out = comp(out, solid(size, STEEL, blade))
    lower = poly_mask(size, [P(2, 0), P(blade_len, 0), P(blade_len, -0.4), P(2, -3.0)])
    out = comp(out, solid(size, STEEL_DEEP, ImageChops.multiply(lower, blade)))
    hi = Image.new("L", size, 0)
    ImageDraw.Draw(hi).line([P(6, 1.4), P(blade_len * 0.92, 0.2)], fill=230, width=max(1, int(0.9 * F)))
    out = comp(out, solid(size, (255, 255, 255), hi))
    # Grip and pommel.
    grip = poly_mask(size, [P(-4, 3.2), P(-34, 2.6), P(-34, -2.6), P(-4, -3.2)])
    gold = Image.new("L", size, 0)
    gd = ImageDraw.Draw(gold)
    # quillons, curving slightly toward the blade at the ends
    qu = curve(P(-1, 19), P(-4, 0), P(-1, -19), 20)
    gd.line(qu, fill=255, width=int(3.4 * F), joint="curve")
    for e in (qu[0], qu[-1]):
        gd.ellipse((e[0] - 3.2 * F, e[1] - 3.2 * F, e[0] + 3.2 * F, e[1] + 3.2 * F), fill=255)
    # knuckle bow from one quillon round to the pommel
    gd.line(curve(P(-1, 17), P(-22, 26), P(-36, 4), 24), fill=255, width=int(2.6 * F), joint="curve")
    # a small ring on the blade side of the guard
    for s in (1, -1):
        gd.polygon(ellipse_pts(P(5, 6.5 * s), 6.5 * F, 3.6 * F, ang + math.pi / 2), fill=255)
    pom = P(-39, 0)
    gd.ellipse((pom[0] - 5 * F, pom[1] - 5 * F, pom[0] + 5 * F, pom[1] + 5 * F), fill=255)
    holes = Image.new("L", size, 0)
    for s in (1, -1):
        ImageDraw.Draw(holes).polygon(ellipse_pts(P(5, 6.5 * s), 4.0 * F, 1.6 * F, ang + math.pi / 2), fill=255)
    gold = ImageChops.subtract(gold, holes)
    hilt = union(grip, gold)
    out = comp(out, solid(size, INK, ring_out(hilt, max(1, int(1.3 * F)))))
    out = comp(out, solid(size, INK_SOFT, grip))
    wire = Image.new("L", size, 0)
    for k in range(6):
        a = -8 - k * 4.4
        ImageDraw.Draw(wire).line([P(a, 2.8), P(a - 2.2, -2.8)], fill=200, width=max(1, int(0.9 * F)))
    out = comp(out, clip(solid(size, GOLD_DEEP, wire), grip))
    out = comp(out, gold_paint(size, gold, rng, angle=-30, bands=1.2))
    return fade(out, alpha) if alpha < 1.0 else out


def card_tile(rng, w=50, h=72):
    """A generic playing-card back: bone paper, ink edge, gold rule, a crimson lozenge with a gold star (supersampled)."""
    W, H = int(w * F), int(h * F)
    pad = 6 * F
    size = (W + 2 * pad, H + 2 * pad)
    box = (pad, pad, pad + W, pad + H)
    m = Image.new("L", size, 0)
    ImageDraw.Draw(m).rounded_rectangle(box, radius=int(w * 0.12 * F), fill=255)
    out = canvas(*size)
    out = comp(out, glow(m, (0, 0, 0), 3 * F, 0.5))
    out = comp(out, paper_fill(size, m, rng, base=BONE, amount=0.25))
    rule = Image.new("L", size, 0)
    ImageDraw.Draw(rule).rounded_rectangle((box[0] + 5 * F, box[1] + 5 * F, box[2] - 5 * F, box[3] - 5 * F), radius=int(w * 0.08 * F),
                                           outline=255, width=max(1, int(1.6 * F)))
    out = comp(out, gold_paint(size, rule, rng, bands=1.0))
    cx, cy = size[0] / 2, size[1] / 2
    lz = poly_mask(size, [(cx, cy - h * 0.3 * F), (cx + w * 0.3 * F, cy), (cx, cy + h * 0.3 * F), (cx - w * 0.3 * F, cy)])
    out = comp(out, solid(size, INK, ring_out(lz, max(1, int(1.2 * F)))), solid(size, CRIMSON, lz))
    out = comp(out, clip(solid(size, CRIMSON_DEEP, linear_light(size, 135).point(lambda v: int(v * 0.7))), lz))
    out = comp(out, solid(size, GOLD_CORE, star(size, (cx, cy), w * 0.16 * F, 255, 0.2)))
    out = comp(out, solid(size, GOLD, star(size, (cx, cy), w * 0.16 * F * 0.55, 255, 0.24)))
    # One gold pip in a single corner, so the spin reads.
    pip_m = Image.new("L", size, 0)
    ImageDraw.Draw(pip_m).ellipse((box[0] + 8 * F, box[1] + 8 * F, box[0] + 13 * F, box[1] + 13 * F), fill=255)
    out = comp(out, gold_paint(size, pip_m, rng))
    out = comp(out, solid(size, INK, ring_out(m, max(1, int(1.4 * F)))))
    return out


def card_at(img, tile, c, rot, flip=1.0, scale=1.0, alpha=1.0):
    t = tile
    sx = max(0.08, abs(flip)) * scale
    if sx != 1.0 or scale != 1.0:
        t = t.resize((max(2, int(t.width * sx)), max(2, int(t.height * scale))), Image.LANCZOS)
    t = t.rotate(rot, resample=Image.BICUBIC, expand=True)
    if alpha < 1.0:
        t = fade(t, alpha)
    return paste_center(img, t, c)


def crescent(size, c, r, a0, a1, w, rng, head_at_end=True):
    """A slash crescent along an arc (degrees, screen clockwise): thick at the leading end, a hair at the tail."""
    n = 40
    pts, widths = [], []
    for i in range(n + 1):
        t = i / n
        a = math.radians(a0 + (a1 - a0) * t)
        pts.append((c[0] + math.cos(a) * r, c[1] + math.sin(a) * r))
        k = t if head_at_end else 1 - t
        widths.append(max(1.0, w * (k ** 1.6) * (1 - 0.7 * max(0, k - 0.88) / 0.12)))
    return taper_mask(size, pts, widths[1:])


def slash_layer(size, c, r, a0, a1, w, rng, strength=1.0):
    m = crescent(size, c, r, a0, a1, w, rng)
    out = canvas(*size)
    out = comp(out, solid(size, GOLD, scale_l(m.filter(ImageFilter.GaussianBlur(w * 0.6)), 0.9 * strength)))
    out = comp(out, solid(size, CRIMSON, scale_l(shift(m, 0, w * 0.35).filter(ImageFilter.GaussianBlur(1.5)), 0.85 * strength)))
    out = comp(out, solid(size, EMBER, scale_l(m, strength)))
    core = crescent(size, c, r - w * 0.12, a0 + 4, a1, w * 0.45, rng)
    out = comp(out, solid(size, (255, 255, 255), scale_l(core, strength)))
    return out


def motion_arcs(size, c, r, spans, rng, rgb=INK, alpha=150, w=2.2):
    """Thin curved speed marks around a spinning object."""
    out = canvas(*size)
    for a0, a1 in spans:
        pts = [(c[0] + math.cos(math.radians(a0 + (a1 - a0) * i / 24)) * r, c[1] + math.sin(math.radians(a0 + (a1 - a0) * i / 24)) * r)
               for i in range(25)]
        out = comp(out, brush_stroke(size, pts, w * F * 2, rgb, alpha, seed=rng.randrange(1 << 30), bristles=3))
    return out


def disarm_frame(stage, card, rng):
    """One 256x256 frame of the feint. stage: feint, flick, spin, away."""
    S = 256
    size = (S * F, S * F)
    img = canvas(*size)

    def Pt(x, y):
        return (x * F, y * F)

    if stage == "feint":
        # The real blade tucked low under the card; a ghost of the fake high line it just showed.
        img = comp(img, fade(rapier(size, Pt(44, 210), math.radians(-48), 150, rng), 0.28))
        img = comp(img, motion_arcs(size, Pt(44, 210), 150 * F + 30 * F, [(-46, -26)], rng, GOLD, 170, 2.0))
        img = comp(img, motion_arcs(size, Pt(44, 210), 150 * F + 18 * F, [(-44, -28)], rng, INK, 110, 1.4))
        img = comp(img, rapier(size, Pt(44, 210), math.radians(-24), 150, rng))
        card_at(img, card, Pt(196, 112), -6)
    elif stage == "flick":
        img = comp(img, slash_layer(size, Pt(44, 210), 150 * F, -24, -62, 17 * F, rng))
        img = comp(img, rapier(size, Pt(48, 212), math.radians(-60), 150, rng))
        hit = Pt(150, 86)
        sp = canvas(*size)
        for k in range(7):
            a = math.radians(-120 + k * 26 + rng.uniform(-8, 8))
            d0, d1 = rng.uniform(6, 10) * F, rng.uniform(16, 30) * F
            m = Image.new("L", size, 0)
            ImageDraw.Draw(m).line([(hit[0] + math.cos(a) * d0, hit[1] + math.sin(a) * d0),
                                    (hit[0] + math.cos(a) * d1, hit[1] + math.sin(a) * d1)], fill=255, width=int(1.6 * F))
            sp = comp(sp, solid(size, GOLD_CORE, m))
        img = comp(img, solid(size, GOLD, scale_l(soft_disc(size, hit, 10 * F, 8 * F), 0.9)), sp,
                   solid(size, (255, 255, 255), star(size, hit, 12 * F, 255, 0.14)))
        card_at(img, card, Pt(186, 70), 28)
    elif stage == "spin":
        img = comp(img, fade(slash_layer(size, Pt(44, 210), 150 * F, -24, -62, 17 * F, rng), 0.35))
        img = comp(img, fade(rapier(size, Pt(46, 214), math.radians(-40), 150, rng), 0.9))
        cc = Pt(204, 54)
        img = comp(img, motion_arcs(size, cc, 46 * F, [(150, 250), (-20, 70)], rng, INK, 160, 2.2))
        img = comp(img, motion_arcs(size, cc, 52 * F, [(170, 230), (10, 50)], rng, GOLD, 200, 1.6))
        # dust shaken off the arc behind it
        for k in range(8):
            t = k / 8
            x, y = 150 + 50 * t + rng.uniform(-4, 4), 92 - 34 * t + rng.uniform(-6, 6)
            img = comp(img, motes(size, rng, Pt(x, y), 0, 1, 1, sizes=(1.2, 2.6)))
        card_at(img, card, cc, 145, flip=0.62)
    else:
        # The card is gone over the edge of the clearing; the blade comes up in a little salute.
        img = comp(img, fade(slash_layer(size, Pt(44, 210), 150 * F, -24, -62, 17 * F, rng), 0.12))
        img = comp(img, fade(rapier(size, Pt(58, 228), math.radians(-78), 132, rng), 0.8))
        trail = curve(Pt(150, 96), Pt(180, 26), Pt(222, 40), 30)
        img = comp(img, brush_stroke(size, trail, 5 * F, GOLD, 130, seed=rng.randrange(1 << 30), bristles=3))
        for k in range(9):
            x, y = trail[int(k / 9 * 28)]
            img = comp(img, motes(size, rng, (x, y), 0, 2 * F, 1, sizes=(1.2, 2.6)))
        cc = Pt(220, 46)
        img = comp(img, motion_arcs(size, cc, 34 * F, [(120, 220), (-60, 40)], rng, INK, 160, 2.0))
        card_at(img, card, cc, 250, flip=0.42, scale=0.8)
        img = comp(img, solid(size, GOLD_CORE, star(size, Pt(198, 20), 8 * F, 230, 0.16)))
    img = clip(img, edge_fade(size, 8 * F))
    return downsample(img, F)


def maelle_disarm():
    rng = random.Random(SEED + 2)
    card = card_tile(rng)
    names = ["feint", "flick", "spin", "away"]
    frames = [disarm_frame(n, card, rng) for n in names]
    return strip(frames, names)


# ------------------------------------------------------------------------------------------------------- the ink wipe

class Swipe:
    """One huge ink brush swipe across a clearing, painted like a single loaded stroke: a solid ink body with ragged
    dry-brush edges, streaks of bare paper inside it and a tail that runs dry into separate bristle marks. Bristles,
    splatter and petals are chosen once and then drawn up to any progress, so every frame shows the same stroke."""

    def __init__(self, rng, S):
        self.S = S
        self.size = (S * F, S * F)
        u = S / 384 * F
        self.u = u
        n = 200
        raw = curve((18 * u, 262 * u), (176 * u, 312 * u), (370 * u, 78 * u), n)
        self.path = []
        for i, (x, y) in enumerate(raw):
            j0, j1 = max(0, i - 1), min(n, i + 1)
            dx, dy = raw[j1][0] - raw[j0][0], raw[j1][1] - raw[j0][1]
            ln = math.hypot(dx, dy) or 1
            self.path.append((x, y, -dy / ln, dx / ln, dx / ln, dy / ln))
        self.n = n
        self.W = 104 * u
        self.body_end = 0.86
        self.full = [self.width(i / n) for i in range(n + 1)]
        self.harm = [[(k, rng.uniform(0, 6.3), rng.uniform(0.3, 1.0) / k) for k in range(2, 9)] for _ in range(2)]

        def bristles(count, lo, hi, start=(0.0, 0.0), end=(0.8, 1.0), gaps=3, bw=(0.025, 0.06)):
            out = []
            for _ in range(count):
                g = []
                for _ in range(rng.randint(0, gaps)):
                    g0 = rng.uniform(0.3, 0.98)
                    g.append((g0, g0 + rng.uniform(0.01, 0.07)))
                out.append((rng.uniform(lo, hi), rng.uniform(*start), rng.uniform(*end), rng.uniform(*bw), rng.uniform(0.75, 1.0), g))
            return out

        # Edge bristles leave the stroke's border ragged; tail bristles run on past the body and fan out dry.
        self.edges = bristles(22, -0.6, -0.4, gaps=5) + bristles(22, 0.4, 0.6, gaps=5)
        self.tail = bristles(30, -0.5, 0.5, start=(0.45, 0.7), end=(0.88, 1.0), gaps=4)
        self.sheen = bristles(5, -0.36, -0.14, start=(0.08, 0.3), end=(0.55, 0.85), gaps=4, bw=(0.012, 0.025))
        self.crimson = bristles(6, 0.3, 0.46, start=(0.05, 0.25), end=(0.6, 0.95), gaps=4, bw=(0.012, 0.028))
        # Flying white: streaks of bare paper inside the body, more of them as the brush runs dry.
        self.white = []
        for _ in range(34):
            t0 = rng.uniform(0.22, 0.8)
            self.white.append((rng.uniform(-0.4, 0.4), t0, t0 + rng.uniform(0.06, 0.24), rng.uniform(0.012, 0.03),
                               rng.uniform(0.7, 1.0), []))
        # Splatter: round drops (a few with a short tail) flung off the outer side and ahead of the brush.
        self.drops = []
        for _ in range(64):
            t = rng.uniform(0.06, 1.0)
            side = 1 if rng.random() < 0.7 else -1
            s = 1.0 + 6.0 * rng.random() ** 2.4
            self.drops.append((t, side, rng.uniform(0.02, 0.5) ** 1.3, s, rng.random() < 0.35 and s > 2.5,
                               rng.choice([INK, INK, INK, VIOLET_INK])))
        # Petals torn loose by the stroke: crimson and bone, never drops of red.
        self.petals = []
        for k in range(11):
            t = rng.uniform(0.2, 1.0)
            kind = k % 3 == 1
            self.petals.append((t, rng.choice([-1, 1, 1]), rng.uniform(0.15, 0.9), rng.uniform(15, 21), rng.uniform(0, 360),
                                (BONE, (196, 180, 160)) if kind else (rng.choice([CRIMSON, (196, 36, 54)]), CRIMSON_DEEP)))
        self.rng = rng

    def width(self, t):
        rise = 0.5 + 0.5 * min(1.0, t / 0.12) ** 0.7
        return self.W * rise * (1 - 0.32 * t ** 1.6)

    def body_width(self, t):
        w = self.width(t)
        if t > 0.64:
            w *= math.sqrt(max(0.0, 1 - ((t - 0.64) / (self.body_end - 0.64)) ** 2))
        return w

    def wob(self, side, t):
        return 1 + 0.1 * sum(a * math.sin(k * t * 2 * math.pi + ph) for k, ph, a in self.harm[side])

    def point(self, t):
        i = min(self.n, max(0, int(round(t * self.n))))
        return self.path[i]

    def lines(self, bristles, progress, alpha=255, scale=None):
        m = Image.new("L", self.size, 0)
        d = ImageDraw.Draw(m)
        n = self.n
        widths = scale or self.full
        for off, start, end, bw, al, gaps in bristles:
            last = min(end, progress)
            i0, i1 = int(start * n), int(last * n)
            span = max(1e-3, end - start)
            w_px = max(1, int(bw * self.W))
            for i in range(i0, i1):
                t = i / n
                if any(g0 <= t <= g1 for g0, g1 in gaps):
                    continue
                x0, y0, nx0, ny0, _, _ = self.path[i]
                x1, y1, nx1, ny1, _, _ = self.path[i + 1]
                w0, w1 = widths[i], widths[i + 1]
                k = (t - start) / span
                a = alpha * al * (1 - 0.6 * k ** 3)
                d.line([(x0 + nx0 * off * w0, y0 + ny0 * off * w0), (x1 + nx1 * off * w1, y1 + ny1 * off * w1)], fill=int(a),
                       width=w_px)
        return m

    def body_mask(self, progress):
        n = self.n
        last = int(min(progress, self.body_end) * n)
        upper, lower = [], []
        for i in range(0, last + 1):
            t = i / n
            x, y, nx, ny, _, _ = self.path[i]
            w = self.body_width(t) / 2
            upper.append((x - nx * w * self.wob(0, t), y - ny * w * self.wob(0, t)))
            lower.append((x + nx * w * self.wob(1, t), y + ny * w * self.wob(1, t)))
        m = poly_mask(self.size, upper + lower[::-1])
        # The brush landed hard: a blunt rounded start.
        x, y, nx, ny, tx, ty = self.path[2]
        m = union(m, rough_disc(self.size, (x + tx * 6 * self.u, y + ty * 6 * self.u), self.body_width(0.01) * 0.5, self.rng,
                                wobble=0.06, sy=1.0))
        if progress < self.body_end:
            # The loaded brush head at the front.
            x, y, nx, ny, tx, ty = self.point(progress)
            w = self.body_width(progress)
            m = union(m, rough_disc(self.size, (x, y), w * 0.5, self.rng, wobble=0.05, sy=0.62,
                                    rot=math.atan2(ty, tx) + math.pi / 2))
        m = union(m, self.lines(self.edges, progress), self.lines(self.tail, progress))
        m = ImageChops.subtract(m, self.lines(self.white, progress, 235, scale=[self.body_width(i / n) for i in range(n + 1)]))
        return m.filter(ImageFilter.GaussianBlur(0.7 * F))

    def splatter(self, progress, alpha=1.0):
        out = canvas(*self.size)
        for t, side, dist, s, tailed, colr in self.drops:
            if t > progress + 0.06:
                continue
            x, y, nx, ny, tx, ty = self.point(min(t, 1.0))
            r = self.full[min(self.n, int(t * self.n))] * 0.5 + dist * self.W
            fwd = (max(0.0, progress - t) * 26 + 4) * self.u
            x, y = x + nx * side * r + tx * fwd, y + ny * side * r + ty * fwd
            sz = s * self.u
            m = Image.new("L", self.size, 0)
            d = ImageDraw.Draw(m)
            d.ellipse((x - sz, y - sz, x + sz, y + sz), fill=255)
            if tailed:
                d.polygon([(x - ny * sz * 0.8, y + nx * sz * 0.8), (x - tx * sz * 4.5, y - ty * sz * 4.5), (x + ny * sz * 0.8, y - nx * sz * 0.8)],
                          fill=255)
            out = comp(out, solid(self.size, colr, scale_l(m.filter(ImageFilter.GaussianBlur(0.5 * F)), alpha)))
        return out

    def petals_layer(self, progress, drift=0.0, alpha=1.0):
        out = canvas(*self.size)
        prng = random.Random(41)
        for t, side, dist, ln, rot, (rgb, shade) in self.petals:
            if t > progress - 0.1:
                continue
            x, y, nx, ny, tx, ty = self.point(t)
            r = self.full[int(t * self.n)] * 0.5 + dist * self.W * 0.9
            fwd = (progress - t) * 60 * self.u + drift * 30 * self.u
            x, y = x + nx * side * r + tx * fwd, y + ny * side * r + ty * fwd + drift * 22 * self.u
            tile = petal_tile(ln, rgb, shade, prng, rot + progress * 140 + drift * 90)
            if alpha < 1.0:
                tile = fade(tile, alpha)
            paste_center(out, tile, (x, y))
        return out

    def stroke(self, progress, alpha=1.0):
        """The painted stroke up to `progress`: ink body, a dark crimson under-glow, a cool sheen and broken crimson lip."""
        size = self.size
        body = self.body_mask(progress)
        if alpha < 1.0:
            body = scale_l(body, alpha)
        out = canvas(*size)
        out = comp(out, glow(body, CRIMSON_DEEP, 10 * F, 0.55 * alpha))
        out = comp(out, solid(size, INK, body))
        out = comp(out, clip(solid(size, VIOLET_INK, self.lines(self.sheen, progress, int(150 * alpha)).filter(ImageFilter.GaussianBlur(0.8 * F))), body))
        out = comp(out, clip(solid(size, (150, 140, 160), self.lines(self.sheen[:2], progress, int(90 * alpha))), body))
        out = comp(out, clip(solid(size, CRIMSON, self.lines(self.crimson, progress, int(200 * alpha)).filter(ImageFilter.GaussianBlur(0.5 * F))), body))
        return out, body


def ripples(size, c, rng, rings=3, r0=10, sy=0.34, alpha=150):
    out = canvas(*size)
    for k in range(rings):
        r = (r0 + k * 11 + rng.uniform(-2, 2)) * F
        m = Image.new("L", size, 0)
        a0 = rng.uniform(0, 60)
        ImageDraw.Draw(m).arc((c[0] - r, c[1] - r * sy, c[0] + r, c[1] + r * sy), a0, a0 + 300, fill=int(alpha * (1 - k / (rings + 0.5))),
                              width=max(1, int(2.2 * F)))
        out = comp(out, solid(size, LIGHT_TEAL if k == 0 else TEAL, m.filter(ImageFilter.GaussianBlur(0.6 * F))))
    return out


def ink_wipe_frame(stage, sw):
    size = sw.size
    rng = sw.rng
    u = sw.u
    img = canvas(*size)
    if stage == "strike":
        p = 0.34
        stroke, body = sw.stroke(p)
        # A spray of fine ink thrown ahead of the landing brush.
        x, y, nx, ny, tx, ty = sw.point(p)
        spray = Image.new("L", size, 0)
        d = ImageDraw.Draw(spray)
        for _ in range(34):
            a = math.atan2(ty, tx) + rng.gauss(0, 0.3)
            r = sw.W * 0.38 + rng.uniform(0, 1) ** 1.5 * 46 * u
            s = rng.uniform(0.6, 2.0) * u
            px, py = x + math.cos(a) * r, y + math.sin(a) * r
            d.ellipse((px - s, py - s, px + s, py + s), fill=rng.randint(150, 255))
        img = comp(img, solid(size, INK, spray.filter(ImageFilter.GaussianBlur(0.4 * F))), sw.splatter(p), stroke)
        # Speed marks behind the brush.
        lines = canvas(*size)
        for k in range(5):
            t0 = rng.uniform(0.06, 0.22)
            x, y, nx, ny, tx, ty = sw.point(t0)
            off = rng.choice([-1, 1]) * rng.uniform(0.62, 0.85) * sw.W
            a = (x + nx * off, y + ny * off)
            b = (a[0] - tx * rng.uniform(30, 56) * u, a[1] - ty * rng.uniform(30, 56) * u)
            lines = comp(lines, brush_stroke(size, [b, ((a[0] + b[0]) / 2, (a[1] + b[1]) / 2), a], 3 * F, INK, 140,
                                             seed=rng.randrange(1 << 30), bristles=3))
        img = comp(lines, img)
    elif stage == "sweep":
        p = 0.68
        stroke, body = sw.stroke(p)
        img = comp(img, sw.splatter(p), stroke, sw.petals_layer(p))
    elif stage == "wipe":
        stroke, body = sw.stroke(1.0)
        img = comp(img, sw.splatter(1.0), stroke, sw.petals_layer(1.0))
    else:
        # Aftermath: the ink cracks into flakes that lift away, leaving a pale stain; petals settle on the shallows.
        stroke, body = sw.stroke(1.0)
        nrng = random.Random(SEED + 77)
        D = int(size[0] * 1.5)
        streaks = noise_l((max(1, D // (36 * F)), max(1, D // (3 * F))), nrng).resize((D, D), Image.BICUBIC)
        streaks = streaks.rotate(32, resample=Image.BICUBIC).crop(((D - size[0]) // 2, (D - size[1]) // 2,
                                                                 (D - size[0]) // 2 + size[0], (D - size[1]) // 2 + size[1]))
        # eaten more toward the far end of the stroke, where it lifts off first
        erode = ImageChops.add(streaks, linear_light(size, 210), 1.6, -40)
        keep = erode.point(lambda v: 255 if v < 150 else 0).filter(ImageFilter.GaussianBlur(0.5 * F))
        img = comp(img, solid(size, VIOLET_INK, scale_l(body, 0.22)))
        img = comp(img, fade(clip(stroke, keep), 0.85))
        flakes = canvas(*size)
        frng = random.Random(SEED + 78)
        for _ in range(90):
            t = frng.uniform(0.04, 0.95)
            x, y, nx, ny, tx, ty = sw.point(t)
            w = sw.body_width(t)
            off = frng.uniform(-0.5, 0.5) * w
            lift = frng.uniform(10, 50) * u * (0.5 + t)
            fx = x + nx * off + lift * 0.7
            fy = y + ny * off - lift * 0.7
            s = frng.uniform(2.0, 6.0) * u * (1.2 - 0.5 * t)
            pts = [(fx + math.cos(a) * s * frng.uniform(0.5, 1.2), fy + math.sin(a) * s * frng.uniform(0.5, 1.2))
                   for a in sorted(frng.uniform(0, 2 * math.pi) for _ in range(5))]
            colr = frng.choice([INK, INK, VIOLET_INK, INK_SOFT])
            flakes = comp(flakes, solid(size, colr, poly_mask(size, pts, int(255 * frng.uniform(0.5, 0.95)))))
        img = comp(img, flakes)
        prng = random.Random(SEED + 79)
        for (cx, cy), rr, rgb, shade in (((236, 312), 9, CRIMSON, CRIMSON_DEEP), ((306, 262), 8, BONE, (196, 180, 160)),
                                         ((168, 340), 7, (196, 36, 54), CRIMSON_DEEP)):
            img = comp(img, ripples(size, (cx * u, cy * u), rng, rings=3, r0=rr, alpha=230))
            paste_center(img, petal_tile(prng.uniform(15, 18), rgb, shade, prng, prng.uniform(60, 120)), (cx * u, cy * u - 2 * u))
        img = comp(img, sw.petals_layer(1.0, drift=0.6, alpha=0.95))
    img = clip(img, edge_fade(size, 10 * F))
    return downsample(img, F)


def whm_ink_wipe():
    sw = Swipe(random.Random(SEED + 3), 384)
    names = ["strike", "sweep", "wipe", "fade"]
    frames = [ink_wipe_frame(n, sw) for n in names]
    return strip(frames, names)


# ----------------------------------------------------------------------------------------------------------- entry

def make(out_dir, fonts):
    out = []

    def emit(asset, rel, img, sprites=()):
        save(img, out_dir, rel)
        out.append({"asset": asset, "path": f"art/{rel}", "size": [img.width, img.height], "sprites": list(sprites)})

    sheet, sprites = maelle_keepsake()
    emit("asset.fx.maelle-keepsake", "fx/maelle-keepsake.png", sheet, sprites)
    sheet, sprites = maelle_disarm()
    emit("asset.fx.maelle-disarm", "fx/maelle-disarm.png", sheet, sprites)
    sheet, sprites = whm_ink_wipe()
    emit("asset.fx.whm-ink-wipe", "fx/whm-ink-wipe.png", sheet, sprites)
    return out

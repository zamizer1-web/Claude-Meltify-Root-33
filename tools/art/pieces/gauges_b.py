"""Gauges, set B, for Root 33: Verso's Perfection rank letters, Maelle's family row of relationship frames and her
three stance chips.

  gauges/rank-letters.png   D, C, B, A and S in Cinzel Black, climbing from a scumbled ink-grey D on a faint smudge,
                            through a bone C on a river-teal swash and a gilt-rimmed B on a crimson swash, to a gold A
                            with a soft glow and a blazing white-gold S in a burst of brushed rays, sparks and petals.
  gauges/family-frames.png  A small arch-topped portrait frame hung from a nail, one per relationship step: indifferent
                            (dull, no pips lit), amiable (one), friendly (two, crimson gems), allied (three blazing pips,
                            a family star, laurel and a warm glow) and hostile (cracked frame and glass, crimson cast,
                            pips slashed). Also the sketch slot that fits the frame window (bone paper with an artist's
                            construction lines), a small crimson rosette marking a bot's chosen family, and "window",
                            the portrait opening of the first frame (the same offset inside every frame).
  gauges/stance-chips.png   Round gilt-rimmed chips: Offensive (crimson, a blade and slash), Defensive (river teal, a
                            heater shield) and Virtuose (gold, a faceted eight-point star with a halo).

Everything is original and drawn from scratch with Pillow in the shared painterly style (tools/art/style.py): ink
outlines, bone paper, gold leaf, crimson and river teal. No character is drawn: the sketch slot holds only generic
construction lines. Every random choice comes from a seeded random.Random (noise included), so the same code always
paints the same pixels. Pieces are drawn at 4x and reduced, so edges stay soft at in-game size.
"""
import math
import random
import sys
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageFont, ImageOps

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from style import (BONE, CRIMSON, CRIMSON_DEEP, GOLD, GOLD_CORE, GOLD_DEEP, INK, INK_SOFT, PAPER, TEAL,  # noqa: E402
                   TEAL_DEEP, brush_stroke, canvas, curve, glow, petal_tile, solid)

SEED = 3352
F = 4
LIGHT_TEAL = (176, 226, 222)
GOLD_PALE = (250, 222, 160)
IRON = (124, 120, 134)
IRON_LIGHT = (224, 220, 230)
STRING = (168, 136, 98)
CRIMSON_LIGHT = (232, 88, 98)


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


def inter(a, b):
    return ImageChops.multiply(a, b)


def noise_l(size, rng, cell=1, blur=0.0, stretch=True):
    w, h = size
    sw, sh = max(1, -(-w // cell)), max(1, -(-h // cell))
    im = Image.frombytes("L", (sw, sh), rng.randbytes(sw * sh))
    if (sw, sh) != (w, h):
        im = im.resize((w, h), Image.BICUBIC)
    if blur:
        im = im.filter(ImageFilter.GaussianBlur(blur))
    return ImageOps.autocontrast(im) if stretch else im


def streaks(size, rng, angle=0.0, along=40, across=2, blur=0.8):
    """Brush-streak noise: long along `angle` (degrees), fine across it."""
    w, h = size
    d = int(math.hypot(w, h)) + 4
    small = noise_l((max(2, d // along), max(2, d // across)), rng)
    n = small.resize((d, d), Image.BICUBIC).rotate(angle, resample=Image.BICUBIC)
    n = n.crop(((d - w) // 2, (d - h) // 2, (d - w) // 2 + w, (d - h) // 2 + h))
    if blur:
        n = n.filter(ImageFilter.GaussianBlur(blur))
    return ImageOps.autocontrast(n)


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


def gold_paint(size, mask, rng, light=None, angle=25, bands=1.5):
    """Gold leaf (deep to bright sheen, sparse bright specks), shaped by mask."""
    light = light if light is not None else linear_light(size, angle)
    sheen = map_l(light.point(lambda v: int(255 * (0.18 + 0.72 * abs(math.sin(v / 255 * math.pi * bands))))),
                  [(0, GOLD_DEEP), (0.55, GOLD), (0.88, GOLD_PALE), (1, GOLD_CORE)])
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


def disc(size, c, r, fill=255):
    m = Image.new("L", size, 0)
    ImageDraw.Draw(m).ellipse((c[0] - r, c[1] - r, c[0] + r, c[1] + r), fill=fill)
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


def roughen(mask, rng, amp=60, soften=3.0, cell=5):
    """Make a clean edge brush-cut: blur it, push it about with noise, cut it again."""
    n = noise_l(mask.size, rng, cell=max(1, int(cell)), blur=cell * 0.5).point(lambda v: 128 + (v - 128) * amp // 128)
    b = mask.filter(ImageFilter.GaussianBlur(soften))
    return ImageChops.add(b, n, 1.0, -128).point(lambda v: 255 if v >= 128 else 0)


def star(size, c, r, alpha=255, thin=0.18, rot=0.0):
    x, y = c
    t = r * thin
    pts = []
    for k in range(8):
        a = rot + k * math.pi / 4
        rr = r if k % 2 == 0 else t * 1.41
        pts.append((x + math.cos(a) * rr, y + math.sin(a) * rr))
    m = Image.new("L", size, 0)
    ImageDraw.Draw(m).polygon(pts, fill=alpha)
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


def drop_shadow(size, mask, dy, blur, strength=0.6):
    return solid(size, INK, scale_l(shift(mask, 0, dy).filter(ImageFilter.GaussianBlur(blur)), strength))


def top_lip(mask, px):
    """Pixels on a mask's upper edges (light catches them)."""
    return ImageChops.subtract(mask, shift(mask, 0, px))


def bottom_lip(mask, px):
    return ImageChops.subtract(mask, shift(mask, 0, -px))


def leaf_pts(c, length, width, ang, n=16):
    """A pointed leaf (lens) centred on c, its long axis along ang."""
    ax, ay = math.cos(ang), math.sin(ang)
    px, py = -ay, ax
    pts = []
    for i in range(n + 1):
        t = i / n
        s = (t - 0.5) * length
        w = math.sin(math.pi * t) * width / 2
        pts.append((c[0] + ax * s + px * w, c[1] + ay * s + py * w))
    for i in range(n - 1, 0, -1):
        t = i / n
        s = (t - 0.5) * length
        w = math.sin(math.pi * t) * width / 2
        pts.append((c[0] + ax * s - px * w, c[1] + ay * s - py * w))
    return pts


def resample(pts, spacing):
    """Points every `spacing` px along a polyline."""
    out = [pts[0]]
    carry = 0.0
    for (x0, y0), (x1, y1) in zip(pts, pts[1:]):
        seg = math.hypot(x1 - x0, y1 - y0)
        d = spacing - carry
        while d <= seg:
            t = d / seg
            out.append((x0 + (x1 - x0) * t, y0 + (y1 - y0) * t))
            d += spacing
        carry = (carry + seg) % spacing
    return out


def sparks(size, rng, center, r_in, r_out, count, r_min, r_max, rgb=GOLD_CORE, halo=GOLD, avoid=None):
    """Four-point glints scattered in a ring around center."""
    out = canvas(*size)
    for _ in range(count):
        for _try in range(12):
            a = rng.uniform(0, 2 * math.pi)
            rr = rng.uniform(r_in, r_out)
            x, y = center[0] + math.cos(a) * rr, center[1] + math.sin(a) * rr
            if avoid is None or avoid.getpixel((int(min(max(x, 0), size[0] - 1)), int(min(max(y, 0), size[1] - 1)))) == 0:
                break
        r = rng.uniform(r_min, r_max)
        m = star(size, (x, y), r, thin=0.14, rot=rng.uniform(-0.15, 0.15))
        out = comp(out, glow(m, halo, r * 0.45, 1.5), solid(size, rgb, m))
    return out


def scatter_petals(img, rng, center, r_in, r_out, count, px_min, px_max, rgbs=(CRIMSON, CRIMSON_DEEP)):
    w, h = img.size
    for _ in range(count):
        tile = petal_tile(rng.uniform(px_min, px_max), rng.choice(rgbs), rng)
        a = rng.uniform(0, 2 * math.pi)
        rr = rng.uniform(r_in, r_out)
        x = int(center[0] + math.cos(a) * rr - tile.width / 2)
        y = int(center[1] + math.sin(a) * rr * 0.9 - tile.height / 2)
        x = min(max(x, 0), w - tile.width)
        y = min(max(y, 0), h - tile.height)
        img.alpha_composite(tile, (x, y))
    return img


def downsample(img, f=F):
    return img.resize((img.width // f, img.height // f), Image.LANCZOS)


def save(img, out_dir, rel):
    p = Path(out_dir) / rel
    p.parent.mkdir(parents=True, exist_ok=True)
    img.save(p, optimize=True)
    return p


# ------------------------------------------------------------------------------------------------------ rank letters

RANKS = "DCBAS"
RS = 256  # one letter cell, final px


def fit_font(path, text, cap_h):
    probe = ImageFont.truetype(path, 200)
    b = probe.getbbox(text)
    return ImageFont.truetype(path, max(8, int(200 * cap_h / (b[3] - b[1]))))


def text_mask(size, text, fnt, center):
    m = Image.new("L", size, 0)
    d = ImageDraw.Draw(m)
    b = d.textbbox((0, 0), text, font=fnt)
    d.text((center[0] - (b[0] + b[2]) / 2, center[1] - (b[1] + b[3]) / 2), text, font=fnt, fill=255)
    return m


def smear(size, rng, pts, width, deep, lit, alpha=235):
    """A loaded-brush smear along a polyline: blunt head, bristle streaks, a dry split tail."""
    n = len(pts)
    left, right = [], []
    for i, (x, y) in enumerate(pts):
        (ax, ay), (bx, by) = pts[max(0, i - 1)], pts[min(n - 1, i + 1)]
        ln = math.hypot(bx - ax, by - ay) or 1
        nx, ny = -(by - ay) / ln, (bx - ax) / ln
        t = i / (n - 1)
        w = width / 2 * (0.42 + 0.58 * math.sin(math.pi * (0.16 + 0.84 * t))) * (1 + 0.07 * math.sin(t * 17 + width))
        left.append((x + nx * w, y + ny * w))
        right.append((x - nx * w, y - ny * w))
    band = poly_mask(size, left + right[::-1])
    band = roughen(band, rng, amp=90, soften=width * 0.03, cell=max(2, int(width * 0.03)))
    ang = math.degrees(math.atan2(pts[0][1] - pts[-1][1], pts[-1][0] - pts[0][0]))
    st = streaks(size, rng, angle=ang, along=30, across=max(2, int(width / 70)), blur=0.6)
    along = linear_light(size, 90 + ang)  # 0 at the head, 255 at the tail
    x0 = min(p[0] for p in pts)
    span = max(p[0] for p in pts) - x0
    k0 = 255 * (x0 / size[0] + 0.55 * span / size[0])
    dry = ImageChops.subtract(st, along.point(lambda v: int(max(0, v - k0) * 3.2)))
    dry = dry.point(lambda v: 255 if v > 40 else v * 6)
    body = inter(band, dry)
    out = solid(size, deep, scale_l(inter(body, st.point(lambda v: 150 + v * 105 // 255)), alpha / 255))
    hi = st.point(lambda v: 0 if v < 140 else min(255, (v - 140) * 3))
    return comp(out, solid(size, lit, scale_l(inter(body, hi), alpha / 255 * 0.85)))


def swash(size, rng, c, span, lift, width, rgb, alpha, rgb2=None):
    """A painted backdrop smear rising left to right behind a letter."""
    p0 = (c[0] - span, c[1] + lift)
    p1 = (c[0] + rng.uniform(-0.05, 0.05) * span, c[1] + rng.uniform(-0.1, 0.05) * span)
    p2 = (c[0] + span, c[1] - lift)
    return smear(size, rng, curve(p0, p1, p2, 60), width, rgb, rgb2 or rgb, alpha)


def ray_burst(size, rng, c, count, r0, r_long, r_short, w_min, w_max, rgb, alpha, core=None):
    out = canvas(*size)
    off = rng.uniform(0, math.pi)
    for k in range(count):
        a = off + 2 * math.pi * k / count + rng.uniform(-0.08, 0.08)
        r1 = rng.uniform(*r_long) if k % 2 == 0 else rng.uniform(*r_short)
        pts = [(c[0] + math.cos(a) * (r0 + (r1 - r0) * i / 14), c[1] + math.sin(a) * (r0 + (r1 - r0) * i / 14))
               for i in range(15)]
        w = rng.uniform(w_min, w_max) * (1.0 if k % 2 == 0 else 0.75)
        out = comp(out, brush_stroke(size, pts, w, rgb, alpha=alpha, seed=rng.randrange(1 << 30), bristles=7))
        if core:
            out = comp(out, brush_stroke(size, pts[:11], w * 0.35, core, alpha=alpha, seed=rng.randrange(1 << 30),
                                         bristles=3))
    return out


def rank_letter(idx, fonts):
    """One Perfection letter; idx 0..4 is D..S and sets how hot it burns."""
    rng = random.Random(SEED + 101 * idx)
    N = RS * F
    size = (N, N)
    c = (N / 2, N / 2)
    fnt = fit_font(fonts["black"], "D", 0.56 * N)
    m = text_mask(size, RANKS[idx], fnt, (c[0], c[1] + 2 * F))
    m = roughen(m, rng, amp=70 - idx * 8, soften=1.4 * F, cell=3 * F)
    vgrad = linear_light(size, 0)  # dark at the top, light at the bottom
    img = canvas(*size)

    # --- backdrop, rising with the rank
    if idx == 0:
        img = comp(img, swash(size, rng, c, 0.30 * N, 0.05 * N, 0.20 * N, (84, 78, 90), 110, rgb2=(124, 118, 130)))
    elif idx == 1:
        img = comp(img, swash(size, rng, c, 0.36 * N, 0.10 * N, 0.27 * N, TEAL_DEEP, 215, rgb2=TEAL))
    elif idx == 2:
        img = comp(img, swash(size, rng, c, 0.38 * N, 0.12 * N, 0.30 * N, CRIMSON_DEEP, 230, rgb2=CRIMSON))
        flick = curve((c[0] - 0.30 * N, c[1] + 0.20 * N), (c[0], c[1] + 0.10 * N), (c[0] + 0.36 * N, c[1] - 0.16 * N), 30)
        img = comp(img, brush_stroke(size, flick, 0.025 * N, GOLD, alpha=200, seed=rng.randrange(1 << 30), bristles=3))
    elif idx == 3:
        img = comp(img, ray_burst(size, rng, c, 10, 0.20 * N, (0.36 * N, 0.42 * N), (0.29 * N, 0.33 * N),
                                  0.035 * N, 0.05 * N, GOLD, 130))
        img = comp(img, swash(size, rng, c, 0.40 * N, 0.13 * N, 0.33 * N, CRIMSON_DEEP, 240, rgb2=CRIMSON))
    else:
        rays = ray_burst(size, rng, c, 18, 0.14 * N, (0.42 * N, 0.49 * N), (0.32 * N, 0.39 * N),
                         0.05 * N, 0.075 * N, GOLD, 245, core=GOLD_CORE)
        rays = clip(rays, radial_light(size, c, 0.5 * N, power=0.55))
        img = comp(img, glow(radial_light(size, c, 0.36 * N), GOLD, 6 * F, 0.9), rays)
        img = comp(img, swash(size, rng, c, 0.41 * N, 0.13 * N, 0.34 * N, CRIMSON_DEEP, 245, rgb2=CRIMSON))

    # --- glow and shadow
    outline = grow(m, 3.5 * F)
    if idx == 3:
        img = comp(img, glow(outline, GOLD, 9 * F, 1.3))
    if idx == 4:
        img = comp(img, glow(outline, GOLD, 16 * F, 2.2), glow(outline, GOLD_CORE, 6 * F, 1.4))
    img = comp(img, drop_shadow(size, outline, 4 * F, 3 * F, 0.55 if idx < 4 else 0.25))
    if idx >= 3:
        rim = ring_out(outline, (1.6 if idx == 3 else 2.4) * F)
        img = comp(img, gold_paint(size, rim, rng, angle=-30, bands=1.2))
    img = comp(img, ink_edge(size, m, 3.5 * F, heavy=1.5 * F))

    # --- the letter itself
    if idx == 0:
        st = streaks(size, rng, angle=8, along=30, across=3)
        fill = paint(size, ImageChops.add(st, vgrad, 2.0, 0), [(0, (74, 70, 80)), (0.55, (116, 110, 120)), (1, (156, 150, 158))], m)
        dry = streaks(size, rng, angle=8, along=18, across=2).point(lambda v: 255 if v > 70 else int(v * 2))
        img = comp(img, solid(size, INK_SOFT, m), clip(fill, dry))
    elif idx == 1:
        img = comp(img, paper_fill(size, m, rng, BONE, 0.3),
                   solid(size, PAPER, inter(m, vgrad.point(lambda v: int(v * 0.75)))),
                   solid(size, TEAL_DEEP, scale_l(bottom_lip(m, 3 * F), 0.55)))
    elif idx == 2:
        img = comp(img, paper_fill(size, m, rng, BONE, 0.24),
                   solid(size, PAPER, inter(m, vgrad.point(lambda v: int(v * 0.6)))),
                   gold_paint(size, ring_in(m, 3 * F), rng, angle=-30, bands=1.0),
                   solid(size, INK, scale_l(ring_in(shrink(m, 3 * F), 0.8 * F), 0.5)))
    elif idx == 3:
        img = comp(img, gold_paint(size, m, rng, angle=20, bands=1.4),
                   solid(size, GOLD_CORE, scale_l(top_lip(m, 3 * F), 0.85)),
                   solid(size, (128, 84, 30), scale_l(bottom_lip(m, 3 * F), 0.8)))
    else:
        hot = radial_light(size, (c[0], c[1] - 0.02 * N), 0.40 * N, power=0.8)
        sheen = linear_light(size, 20).point(lambda v: int(255 * abs(math.sin(v / 255 * math.pi * 1.3))))
        light = ImageChops.add(hot, sheen, 1.6, 0)
        img = comp(img, paint(size, light, [(0, GOLD_DEEP), (0.3, GOLD), (0.6, GOLD_PALE), (0.85, GOLD_CORE),
                                            (1, (255, 255, 248))], m),
                   solid(size, (255, 255, 250), scale_l(top_lip(m, 3 * F), 0.95)),
                   solid(size, (150, 98, 34), scale_l(bottom_lip(m, 2.5 * F), 0.7)),
                   solid(size, (255, 255, 250), scale_l(inter(m, shrink(m, 9 * F).filter(ImageFilter.GaussianBlur(6 * F))), 0.55)))

    # --- front sparkle
    if idx == 2:
        img = comp(img, sparks(size, rng, c, 0.30 * N, 0.40 * N, 2, 6 * F, 8 * F, avoid=outline))
    if idx == 3:
        img = scatter_petals(img, rng, c, 0.34 * N, 0.44 * N, 3, 10 * F, 14 * F)
        img = comp(img, sparks(size, rng, c, 0.30 * N, 0.44 * N, 4, 7 * F, 11 * F, avoid=outline))
    if idx == 4:
        img = scatter_petals(img, rng, c, 0.33 * N, 0.46 * N, 6, 10 * F, 16 * F, rgbs=(CRIMSON, CRIMSON_DEEP, CRIMSON_LIGHT))
        img = comp(img, sparks(size, rng, c, 0.26 * N, 0.46 * N, 8, 8 * F, 15 * F, avoid=outline))
        img = comp(img, sparks(size, rng, (c[0] + 0.12 * N, c[1] - 0.17 * N), 0, 1, 1, 16 * F, 16 * F))
    return downsample(img)


def rank_sheet(fonts):
    sheet = canvas(RS * 5, RS)
    sprites = []
    for i, letter in enumerate(RANKS):
        sheet.alpha_composite(rank_letter(i, fonts), (i * RS, 0))
        sprites.append({"name": letter, "box": [i * RS, 0, (i + 1) * RS, RS]})
    return sheet, sprites


# ----------------------------------------------------------------------------------------------------- family frames

FW, FH = 144, 184                       # one frame cell, final px
OUTER_BOX, OUTER_SPRING = (16, 20, 128, 150), 60     # arch-topped frame: (x0, top, x1, bottom), y where the arch springs
WIN_BOX, WIN_SPRING = (29, 34, 115, 137), 64        # the portrait window inside it
NAIL = (72, 7)
PIP_Y, PIP_XS, PIP_R = 165, (51, 72, 93), 8
FAMILY = (("indifferent", 0), ("amiable", 1), ("friendly", 2), ("allied", 3), ("hostile", -1))


def sc(pts, f=F, dx=0, dy=0):
    return [((x + dx) * f, (y + dy) * f) for x, y in pts]


def arch_mask(size, box, spring, f=F, dx=0, dy=0):
    x0, y0, x1, y1 = [(v + o) * f for v, o in zip(box, (dx, dy, dx, dy))]
    sy = (spring + dy) * f
    m = Image.new("L", size, 0)
    d = ImageDraw.Draw(m)
    d.rectangle((x0, sy, x1, y1), fill=255)
    d.ellipse((x0, y0, x1, 2 * sy - y0), fill=255)
    return m


def arch_path(box, spring, f=F):
    """The arch outline, clockwise from the bottom-left corner, in canvas px."""
    x0, y0, x1, y1 = [v * f for v in box]
    sy = spring * f
    cx, rx, ry = (x0 + x1) / 2, (x1 - x0) / 2, sy - y0
    pts = [(x0, y1 - (y1 - sy) * i / 40) for i in range(40)]
    pts += [(cx + rx * math.cos(math.pi + math.pi * i / 120), sy + ry * math.sin(math.pi + math.pi * i / 120)) for i in range(121)]
    pts += [(x1, sy + (y1 - sy) * i / 40) for i in range(1, 41)]
    pts += [(x1 - (x1 - x0) * i / 40, y1) for i in range(1, 41)]
    return pts


def pip_layer(size, c, r, state):
    """A relationship pip: 0 empty socket, 1 lit gold, 2 blazing."""
    out = canvas(*size)
    whole = disc(size, c, r)
    if state == 2:
        out = comp(out, glow(whole, GOLD, r * 1.1, 2.0))
    out = comp(out, solid(size, INK, disc(size, c, r * 1.16)))
    ring_light = radial_light(size, (c[0] - r * 0.4, c[1] - r * 0.5), r * 1.6)
    out = comp(out, paint(size, ring_light, [(0, (120, 84, 34)), (0.6, GOLD_DEEP), (1, GOLD)], whole))
    inner = disc(size, c, r * 0.7)
    out = comp(out, solid(size, INK, ring_out(inner, max(1, r * 0.08))))
    if state == 0:
        out = comp(out, solid(size, (44, 38, 48), inner), solid(size, INK, scale_l(top_lip(inner, r * 0.3), 0.8)))
    else:
        hot = radial_light(size, (c[0] - r * 0.2, c[1] - r * 0.25), r * 0.85)
        stops = [(0, GOLD_DEEP), (0.5, GOLD), (1, GOLD_PALE)] if state == 1 else [(0, GOLD), (0.4, GOLD_PALE), (1, GOLD_CORE)]
        hi = Image.new("L", size, 0)
        ImageDraw.Draw(hi).ellipse((c[0] - r * 0.42, c[1] - r * 0.48, c[0] + r * 0.02, c[1] - r * 0.12), fill=220)
        out = comp(out, paint(size, hot, stops, inner), solid(size, GOLD_CORE if state == 1 else (255, 255, 250), hi))
    return out


def gem_dot(size, c, r, rgb, deep, lit_rgb):
    out = comp(canvas(*size), solid(size, INK, disc(size, c, r * 1.25)), solid(size, deep, disc(size, c, r)))
    hi = Image.new("L", size, 0)
    ImageDraw.Draw(hi).ellipse((c[0] - r * 0.75, c[1] - r * 0.8, c[0] + r * 0.35, c[1] + r * 0.2), fill=255)
    out = comp(out, solid(size, rgb, inter(hi, disc(size, c, r))))
    spark = Image.new("L", size, 0)
    ImageDraw.Draw(spark).ellipse((c[0] - r * 0.5, c[1] - r * 0.55, c[0] - r * 0.1, c[1] - r * 0.2), fill=230)
    return comp(out, solid(size, lit_rgb, spark))


def stud(size, rng, c, r, dull, gem):
    """A small gilt rosette at a frame corner, optionally set with a crimson gem."""
    out = canvas(*size)
    petals = Image.new("L", size, 0)
    d = ImageDraw.Draw(petals)
    for k in range(6):
        a = k * math.pi / 3 + 0.3
        px, py = c[0] + math.cos(a) * r * 0.62, c[1] + math.sin(a) * r * 0.62
        d.ellipse((px - r * 0.48, py - r * 0.48, px + r * 0.48, py + r * 0.48), fill=255)
    body = union(petals, disc(size, c, r * 0.7))
    g = gold_paint(size, body, rng, angle=-35, bands=1.0)
    if dull:
        g = desaturate(g, 0.65, 0.8)
    out = comp(out, ink_edge(size, body, 0.9 * F), g)
    if gem:
        out = comp(out, gem_dot(size, c, r * 0.38, CRIMSON_LIGHT, CRIMSON_DEEP, (255, 220, 220)))
    else:
        out = comp(out, solid(size, INK, ring_out(disc(size, c, r * 0.3), 0.6 * F)),
                   solid(size, GOLD_PALE if not dull else (170, 160, 150), disc(size, c, r * 0.3)))
    return out


def laurel(size, rng, base, tip, bend, leaf_len, leaf_w, count, flip):
    """A gilt sprig: a curved stem with paired pointed leaves."""
    pts = curve(base, bend, tip, 40)
    stem = line_mask(size, pts, 1.6 * F)
    leaves = Image.new("L", size, 0)
    d = ImageDraw.Draw(leaves)
    for i in range(count):
        t = 0.18 + 0.78 * i / max(1, count - 1)
        k = int(t * (len(pts) - 1))
        (x0, y0), (x1, y1) = pts[max(0, k - 1)], pts[min(len(pts) - 1, k + 1)]
        ang = math.atan2(y1 - y0, x1 - x0)
        ll = leaf_len * (1.0 - 0.35 * t)
        for side in (-1, 1):
            a = ang + side * 0.75 * (1 if not flip else 1)
            cx, cy = pts[k][0] + math.cos(a) * ll * 0.5, pts[k][1] + math.sin(a) * ll * 0.5
            d.polygon(leaf_pts((cx, cy), ll, leaf_w * (1.0 - 0.3 * t), a), fill=255)
    tipl = leaf_pts((tip[0] + (tip[0] - bend[0]) * 0.08, tip[1] + (tip[1] - bend[1]) * 0.08), leaf_len * 0.7, leaf_w * 0.7,
                    math.atan2(tip[1] - bend[1], tip[0] - bend[0]))
    d.polygon(tipl, fill=255)
    body = union(stem, leaves)
    return comp(canvas(*size), ink_edge(size, body, 1.0 * F), gold_paint(size, body, rng, angle=60, bands=1.2))


def jag(pts, rng, depth=2, amt=0.22):
    """Midpoint displacement: make a polyline splinter."""
    for _ in range(depth):
        out = [pts[0]]
        for (x0, y0), (x1, y1) in zip(pts, pts[1:]):
            ln = math.hypot(x1 - x0, y1 - y0)
            k = rng.uniform(-amt, amt) * ln
            out += [((x0 + x1) / 2 - (y1 - y0) / (ln or 1) * k, (y0 + y1) / 2 + (x1 - x0) / (ln or 1) * k), (x1, y1)]
        pts = out
    return pts


def hostile_crack():
    main = [(109, 29), (101, 42), (105, 55), (93, 71), (97, 86), (81, 102), (85, 117), (68, 131), (61, 145), (54, 152)]
    branches = [[(93, 71), (80, 67), (71, 74), (62, 70)], [(81, 102), (95, 113), (102, 126)],
                [(97, 86), (108, 92)], [(68, 131), (54, 126), (44, 131)]]
    return main, branches


def family_frame(state_idx):
    name, lit = FAMILY[state_idx]
    hostile = lit < 0
    rng = random.Random(SEED + 401 + 53 * state_idx)
    size = (FW * F, FH * F)
    img = canvas(*size)

    outer = roughen(arch_mask(size, OUTER_BOX, OUTER_SPRING), rng, amp=46, soften=1.0 * F, cell=2 * F)
    window = arch_mask(size, WIN_BOX, WIN_SPRING)
    chunk = None
    if hostile:
        chunk = poly_mask(size, sc([(101, 24), (117, 29), (111, 37), (104, 33)]))
        outer = ImageChops.subtract(outer, chunk)
    moulding = ImageChops.subtract(outer, window)
    inner_band = inter(grow(window, 5 * F), moulding)
    outer_band = ImageChops.subtract(moulding, inner_band)

    # --- hanger: strings behind the frame up to a nail
    strings = canvas(*size)
    nail = (NAIL[0] * F, NAIL[1] * F)
    ends = [(44 * F, 38 * F), (100 * F, 38 * F)]
    for k, e in enumerate(ends):
        if hostile and k == 1:
            pts = curve(nail, (nail[0] + 6 * F, nail[1] + 4 * F), (nail[0] + 7 * F, nail[1] + 13 * F), 16)
            frayed = curve((e[0] - 4 * F, e[1] - 14 * F), (e[0] - 2 * F, e[1] - 8 * F), e, 10)
            for p in (pts, frayed):
                sm = line_mask(size, p, 1.5 * F)
                strings = comp(strings, ink_edge(size, sm, 0.8 * F), solid(size, STRING, sm))
            continue
        sm = line_mask(size, [nail, e], 1.5 * F)
        strings = comp(strings, ink_edge(size, sm, 0.8 * F), solid(size, STRING, sm))
    nail_m = disc(size, nail, 3 * F)
    hi = disc(size, (nail[0] - 0.9 * F, nail[1] - 0.9 * F), 1.2 * F)
    strings = comp(strings, drop_shadow(size, nail_m, 1.5 * F, 1.2 * F, 0.6), ink_edge(size, nail_m, 0.9 * F),
                   solid(size, IRON, nail_m), solid(size, IRON_LIGHT, hi))
    not_window = ImageOps.invert(window)
    img = comp(img, clip(strings, not_window))

    # --- halo and shadow (kept out of the portrait window)
    back = canvas(*size)
    if lit == 3:
        back = comp(back, glow(grow(outer, 2 * F), GOLD, 7 * F, 1.3))
    if hostile:
        back = comp(back, glow(grow(outer, 2 * F), CRIMSON, 5 * F, 0.8))
    back = comp(back, drop_shadow(size, outer, 3 * F, 2.5 * F, 0.55))
    img = comp(img, clip(back, not_window))

    # --- the moulding: bright outer band, darker sunken inner band, a bead row between
    mould = canvas(*size)
    mould = comp(mould, gold_paint(size, outer_band, rng, angle=18, bands=1.7 if lit == 3 else 1.4))
    sunk = paint(size, linear_light(size, 160), [(0, (118, 80, 32)), (0.6, GOLD_DEEP), (1, (206, 160, 82))], inner_band)
    mould = comp(mould, sunk)
    mould = comp(mould, solid(size, GOLD_CORE, scale_l(inter(top_lip(outer, 2 * F), outer_band), 0.7)))
    beads = Image.new("L", size, 0)
    bd = ImageDraw.Draw(beads)
    bx0, by0, bx1, by1 = WIN_BOX
    for x, y in resample(arch_path((bx0 - 2.6, by0 - 2.6, bx1 + 2.6, by1 + 2.6), WIN_SPRING), 4.6 * F)[:-1]:
        bd.ellipse((x - 1.25 * F, y - 1.25 * F, x + 1.25 * F, y + 1.25 * F), fill=255)
    mould = comp(mould, solid(size, (96, 64, 26), shift(beads, 0.4 * F, 0.5 * F)), solid(size, GOLD_PALE, beads))
    sep = inter(ring_out(grow(window, 5 * F), 0.8 * F), moulding)
    mould = comp(mould, solid(size, INK, scale_l(sep, 0.55)))
    if lit == 0:
        mould = desaturate(mould, 0.72, 0.78)
    elif lit == 1:
        mould = desaturate(mould, 0.2, 0.96)
    elif hostile:
        mould = desaturate(mould, 0.85, 0.8, tint=(236, 150, 140))
    img = comp(img, mould)

    # --- window: inner shadow, ink lip
    inner_shadow = inter(window, ImageOps.invert(window.filter(ImageFilter.GaussianBlur(5 * F))))
    img = comp(img, solid(size, INK, scale_l(inner_shadow, 1.5)))
    img = comp(img, solid(size, INK, inter(ring_out(window, 1.6 * F), moulding)))
    img = comp(img, ink_edge(size, outer, 2.4 * F, heavy=1.2 * F))

    # --- ornaments
    gem = lit >= 2
    for k, (sx, sy) in enumerate(((22.5, 143.5), (121.5, 143.5))):
        if hostile and k == 0:
            hole = disc(size, (sx * F, sy * F), 2.6 * F)
            img = comp(img, solid(size, INK, hole), solid(size, (70, 40, 40), disc(size, (sx * F, sy * F - 0.6 * F), 1.6 * F)))
            continue
        img = comp(img, stud(size, rng, (sx * F, sy * F), 5.2 * F, lit == 0 or hostile, gem))
    key = (72 * F, 21 * F)
    if lit == 3:
        sm = star(size, key, 11 * F, thin=0.22)
        img = comp(img, glow(sm, GOLD, 5 * F, 2.0), ink_edge(size, sm, 1.0 * F), solid(size, GOLD_CORE, sm),
                   solid(size, (255, 255, 250), disc(size, key, 2.2 * F)))
    else:
        dm = poly_mask(size, [(key[0], key[1] - 5.5 * F), (key[0] + 4.5 * F, key[1]), (key[0], key[1] + 5.5 * F),
                              (key[0] - 4.5 * F, key[1])])
        lit_half = poly_mask(size, [(key[0], key[1] - 5.5 * F), (key[0] - 4.5 * F, key[1]), (key[0], key[1] + 5.5 * F)])
        if lit == 2:
            body, face = CRIMSON_DEEP, CRIMSON_LIGHT
        elif lit == 0 or hostile:
            body, face = (96, 88, 90), (150, 142, 140)
        else:
            body, face = GOLD_DEEP, GOLD_PALE
        img = comp(img, ink_edge(size, dm, 1.0 * F), solid(size, body, dm), solid(size, face, inter(lit_half, dm)))

    # --- pip plaque
    plaque = roughen(poly_mask(size, sc([(38, 153), (106, 153), (110, 165), (106, 177), (38, 177), (34, 165)])), rng,
                     amp=30, soften=0.8 * F, cell=2 * F)
    img = comp(img, drop_shadow(size, plaque, 2 * F, 2 * F, 0.5), ink_edge(size, plaque, 2.0 * F, heavy=1.0 * F))
    pl = paint(size, linear_light(size, 0), [(0, (58, 48, 60)), (1, (26, 22, 30))], plaque)
    img = comp(img, pl, solid(size, GOLD_DEEP if not hostile else (140, 70, 60), scale_l(ring_in(plaque, 1.0 * F), 0.9)))
    if lit == 3:
        img = comp(img, laurel(size, rng, (37 * F, 171 * F), (10 * F, 148 * F), (15 * F, 172 * F), 10 * F, 5 * F, 3, False),
                   laurel(size, rng, (107 * F, 171 * F), (134 * F, 148 * F), (129 * F, 172 * F), 10 * F, 5 * F, 3, True))
    for k, x in enumerate(PIP_XS):
        state = 0 if hostile or k >= max(lit, 0) else (2 if lit == 3 else 1)
        img = comp(img, pip_layer(size, (x * F, PIP_Y * F), PIP_R * F, state))

    # --- hostile: the crack through frame and glass, a crimson cast, the pips struck out
    if hostile:
        img = comp(img, solid(size, CRIMSON_DEEP, scale_l(window, 0.16)))
        img = comp(img, solid(size, INK, ring_out(chunk, 1.4 * F)) if chunk else canvas(*size))
        main, branches = hostile_crack()
        lines = [(jag(main, rng), 2.6)] + [(jag(b, rng, 1), 1.6) for b in branches]
        gap = Image.new("L", size, 0)
        glint = Image.new("L", size, 0)
        for pts, w in lines:
            p = sc(pts)
            ws = [w * F * (1 - 0.55 * i / len(p)) for i in range(len(p))]
            gap = union(gap, taper_mask(size, p, ws))
            glint = union(glint, taper_mask(size, sc(pts, dx=0.9, dy=-0.6), [x * 0.45 for x in ws]))
        img = comp(img, solid(size, (255, 236, 226), scale_l(ImageChops.subtract(glint, gap), 0.75)),
                   solid(size, INK, gap))
        slash = brush_stroke(size, sc([(36, 173), (58, 166), (86, 161), (110, 155)]), 4.2 * F, CRIMSON, alpha=245,
                             seed=rng.randrange(1 << 30), bristles=6)
        img = comp(img, glow(slash.getchannel("A"), INK, 1.2 * F, 1.2), slash)
    return downsample(img)


def sketch_slot():
    """Bone paper cut to the frame window, with an artist's construction lines (no character)."""
    rng = random.Random(SEED + 611)
    x0, y0, x1, y1 = WIN_BOX
    w, h = x1 - x0, y1 - y0
    size = (w * F, h * F)
    m = arch_mask(size, WIN_BOX, WIN_SPRING, dx=-x0, dy=-y0)
    img = comp(canvas(*size), paper_fill(size, m, rng, BONE, 0.32))
    img = comp(img, solid(size, PAPER, inter(m, linear_light(size, 0).point(lambda v: int(v * 0.5)))))
    charcoal = Image.new("L", size, 0)
    cd = ImageDraw.Draw(charcoal)

    def sketch_line(pts, passes=2, width=1.0, alpha=150):
        for _ in range(passes):
            jit = [(x * F + rng.uniform(-0.7, 0.7) * F, y * F + rng.uniform(-0.7, 0.7) * F) for x, y in pts]
            cd.line(jit, fill=int(alpha * rng.uniform(0.7, 1.0)), width=max(1, int(width * F)), joint="curve")

    hx, hy, rx, ry = w / 2, 46, 15, 19
    oval = [(hx + rx * math.cos(a / 40 * 2 * math.pi), hy + ry * math.sin(a / 40 * 2 * math.pi)) for a in range(43)]
    sketch_line(oval, passes=3, width=0.9, alpha=140)
    sketch_line([(hx + 1.5, hy - ry - 5), (hx - 1, hy + ry + 4)], passes=1, width=0.6, alpha=110)
    sketch_line([(hx - rx - 3, hy + 2), (hx + rx + 3, hy + 1)], passes=1, width=0.6, alpha=110)
    sketch_line(curve((hx - 6, hy + ry - 2), (hx - 5, hy + ry + 8), (hx - 7, hy + ry + 13), 10), passes=2, width=0.8)
    sketch_line(curve((hx + 6, hy + ry - 2), (hx + 5, hy + ry + 8), (hx + 7, hy + ry + 13), 10), passes=2, width=0.8)
    sketch_line(curve((4, h - 2), (8, hy + ry + 12), (hx, hy + ry + 12), 24), passes=3, width=0.9)
    sketch_line(curve((hx, hy + ry + 12), (w - 8, hy + ry + 12), (w - 4, h - 2), 24), passes=3, width=0.9)
    for i in range(9):
        x = 6 + i * 2.6
        sketch_line([(x, h - 4), (x + 7, h - 15)], passes=1, width=0.5, alpha=90)
    for i in range(7):
        x = w - 22 + i * 2.4
        sketch_line([(x, 18 + i * 0.6), (x + 6, 10 + i * 0.6)], passes=1, width=0.5, alpha=80)
    charcoal = charcoal.filter(ImageFilter.GaussianBlur(0.5 * F))
    smudge = scale_l(inter(m, ImageOps.invert(m.filter(ImageFilter.GaussianBlur(6 * F)))), 0.9)
    img = comp(img, solid(size, INK_SOFT, inter(charcoal, m)), solid(size, INK_SOFT, smudge))
    img = comp(img, solid(size, (120, 108, 100), scale_l(ring_in(m, 1.0 * F), 0.6)))
    return downsample(img)


def chosen_mark():
    """A small crimson ribbon rosette: Maelle has picked this family."""
    rng = random.Random(SEED + 677)
    S = 56
    size = (S * F, S * F)
    c = (S / 2 * F, S / 2 * F - 4 * F)
    img = canvas(*size)
    tails = []
    for side in (-1, 1):
        x = c[0] + side * 5 * F
        tails.append(poly_mask(size, [(x - 4 * F, c[1]), (x + 4 * F, c[1]), (x + side * 9 * F + 5 * F, c[1] + 26 * F),
                                      (x + side * 9 * F, c[1] + 21 * F), (x + side * 9 * F - 5 * F, c[1] + 26 * F)]))
    tail = union(*tails)
    pleats = Image.new("L", size, 0)
    deep = Image.new("L", size, 0)
    dp, dd = ImageDraw.Draw(pleats), ImageDraw.Draw(deep)
    R = 15 * F
    for k in range(14):
        a0 = k * 2 * math.pi / 14
        a1 = a0 + 2 * math.pi / 14
        am = (a0 + a1) / 2
        pts = [c, (c[0] + math.cos(a0) * R * 0.92, c[1] + math.sin(a0) * R * 0.92),
               (c[0] + math.cos(am) * R * 1.08, c[1] + math.sin(am) * R * 1.08), (c[0] + math.cos(a1) * R * 0.92, c[1] + math.sin(a1) * R * 0.92)]
        dp.polygon(pts, fill=255)
        dd.polygon([c, pts[2], pts[3]], fill=255)
    body = union(tail, pleats)
    img = comp(img, drop_shadow(size, body, 2 * F, 2 * F, 0.5), ink_edge(size, body, 1.6 * F, heavy=0.8 * F),
               solid(size, CRIMSON, body), solid(size, CRIMSON_DEEP, inter(deep, pleats)),
               solid(size, CRIMSON_DEEP, inter(tail, linear_light(size, 0))))
    centre = disc(size, c, 6.5 * F)
    img = comp(img, ink_edge(size, centre, 1.2 * F), gold_paint(size, centre, rng, angle=-30, bands=1.0),
               solid(size, GOLD_CORE, disc(size, (c[0] - 2 * F, c[1] - 2 * F), 1.8 * F)))
    return downsample(img)


def family_sheet():
    W, H = FW * 5, FH + 120
    sheet = canvas(W, H)
    sprites = []
    for i, (name, _lit) in enumerate(FAMILY):
        sheet.alpha_composite(family_frame(i), (i * FW, 0))
        sprites.append({"name": name, "box": [i * FW, 0, (i + 1) * FW, FH]})
    sprites.append({"name": "window", "box": list(WIN_BOX)})
    sk = sketch_slot()
    sx, sy = 29, FH + 8
    sheet.alpha_composite(sk, (sx, sy))
    sprites.append({"name": "sketch-slot", "box": [sx, sy, sx + sk.width, sy + sk.height]})
    ch = chosen_mark()
    cx, cy = FW + 44, FH + 8
    sheet.alpha_composite(ch, (cx, cy))
    sprites.append({"name": "chosen", "box": [cx, cy, cx + ch.width, cy + ch.height]})
    return sheet, sprites


# ------------------------------------------------------------------------------------------------------ stance chips

CS = 160  # one chip cell, final px
STANCES = (("offensive", CRIMSON, CRIMSON_DEEP, CRIMSON_LIGHT),
           ("defensive", TEAL, TEAL_DEEP, LIGHT_TEAL),
           ("virtuose", GOLD, GOLD_DEEP, GOLD_CORE))


def blade_mark(size, c, R, rng):
    """A straight blade raised to the upper right, gilt guard, slash behind."""
    u = (math.cos(-math.pi / 4), math.sin(-math.pi / 4))
    p = (-u[1], u[0])

    def at(s, t=0.0):
        return (c[0] + u[0] * s * R + p[0] * t * R, c[1] + u[1] * s * R + p[1] * t * R)

    out = canvas(*size)
    arc = curve(at(-0.2, -0.85), at(0.15, 0.05), at(0.75, 0.55), 30)
    out = comp(out, brush_stroke(size, arc, 0.16 * R, CRIMSON_LIGHT, alpha=200, seed=rng.randrange(1 << 30), bristles=7),
               brush_stroke(size, arc[4:-6], 0.06 * R, BONE, alpha=200, seed=rng.randrange(1 << 30), bristles=3))
    blade = poly_mask(size, [at(-0.22, 0.085), at(0.62, 0.07), at(0.86, 0), at(0.62, -0.07), at(-0.22, -0.085)])
    lit = poly_mask(size, [at(-0.22, 0), at(0.86, 0), at(0.62, -0.07), at(-0.22, -0.085)])
    guard = line_mask(size, [at(-0.24, -0.3), at(-0.2, 0), at(-0.24, 0.3)], 0.1 * R)
    curls = union(disc(size, at(-0.26, -0.32), 0.07 * R), disc(size, at(-0.26, 0.32), 0.07 * R))
    grip = line_mask(size, [at(-0.28, 0), at(-0.52, 0)], 0.1 * R)
    pommel = disc(size, at(-0.6, 0), 0.085 * R)
    gilt = union(guard, curls, pommel)
    whole = union(blade, gilt, grip)
    out = comp(out, solid(size, INK, scale_l(shift(whole, 0.04 * R, 0.07 * R).filter(ImageFilter.GaussianBlur(0.04 * R)), 0.55)),
               ink_edge(size, whole, 0.05 * R), solid(size, PAPER, blade), solid(size, BONE, inter(lit, blade)),
               solid(size, INK, scale_l(line_mask(size, [at(-0.15, 0), at(0.6, 0)], 0.02 * R), 0.45)),
               solid(size, (255, 255, 250), scale_l(line_mask(size, [at(-0.1, -0.04), at(0.55, -0.04)], 0.018 * R), 0.8)),
               solid(size, CRIMSON_DEEP, grip),
               solid(size, INK, scale_l(inter(grip, streaks(size, rng, angle=-45 + 90, along=2, across=60, blur=0)
                                              .point(lambda v: 255 if v > 150 else 0)), 0.5)),
               gold_paint(size, gilt, rng, angle=-40, bands=1.0))
    return out


def shield_mark(size, c, R, rng):
    """A heater shield with a gilt boss and a raised rim."""
    def pt(x, y):
        return (c[0] + x * R, c[1] + y * R)

    outline = [pt(-0.55, -0.62), pt(0, -0.54), pt(0.55, -0.62), pt(0.56, -0.05)]
    outline += [pt(0.56 * (1 - t) ** 1.1 * math.cos(t * 0.6), -0.05 + 0.82 * math.sin(t * math.pi / 2)) for t in
                [i / 16 for i in range(1, 17)]]
    outline += [pt(-0.56 * (1 - t) ** 1.1 * math.cos(t * 0.6), -0.05 + 0.82 * math.sin(t * math.pi / 2)) for t in
                [i / 16 for i in range(16, 0, -1)]]
    outline += [pt(-0.56, -0.05)]
    sh = poly_mask(size, outline)
    out = canvas(*size)
    arcs = curve(pt(-0.85, -0.1), pt(0, -1.05), pt(0.85, -0.1), 30)
    out = comp(out, brush_stroke(size, arcs, 0.1 * R, LIGHT_TEAL, alpha=150, seed=rng.randrange(1 << 30), bristles=6))
    lit_half = poly_mask(size, [pt(-1, -1), pt(0, -1), pt(0, 1), pt(-1, 1)])
    field = shrink(sh, 0.11 * R)
    band = poly_mask(size, [pt(-0.09, -0.7), pt(0.09, -0.7), pt(0.09, 0.9), pt(-0.09, 0.9)])
    boss = disc(size, pt(0, -0.1), 0.15 * R)
    out = comp(out, solid(size, INK, scale_l(shift(sh, 0.04 * R, 0.07 * R).filter(ImageFilter.GaussianBlur(0.04 * R)), 0.55)),
               ink_edge(size, sh, 0.05 * R),
               solid(size, PAPER, sh), solid(size, BONE, inter(sh, lit_half)),
               solid(size, TEAL_DEEP, inter(field, ImageOps.invert(lit_half))), solid(size, TEAL, inter(field, lit_half)),
               solid(size, INK, scale_l(ring_out(field, 0.02 * R), 0.7)),
               solid(size, PAPER, inter(band, field)), solid(size, BONE, inter(inter(band, field), lit_half)),
               ink_edge(size, boss, 0.035 * R), gold_paint(size, boss, rng, angle=-40, bands=1.0),
               solid(size, GOLD_CORE, disc(size, pt(-0.05, -0.15), 0.05 * R)))
    return out


def star_mark(size, c, R, rng):
    """A faceted eight-point star with a white halo and a few glints."""
    pts = []
    for k in range(16):
        a = -math.pi / 2 + k * math.pi / 8
        rr = (0.82 if k % 4 == 0 else 0.46) * R if k % 2 == 0 else 0.17 * R
        pts.append((c[0] + math.cos(a) * rr, c[1] + math.sin(a) * rr))
    whole = poly_mask(size, pts)
    lit = Image.new("L", size, 0)
    d = ImageDraw.Draw(lit)
    for k in range(0, 16, 2):
        d.polygon([c, pts[k], pts[(k - 1) % 16]], fill=255)
    out = comp(canvas(*size), glow(whole, (255, 252, 236), 0.12 * R, 1.6),
               solid(size, INK, scale_l(shift(whole, 0.03 * R, 0.06 * R).filter(ImageFilter.GaussianBlur(0.04 * R)), 0.45)),
               ink_edge(size, whole, 0.045 * R), solid(size, GOLD_PALE, whole), solid(size, (255, 255, 248), inter(lit, whole)),
               solid(size, (255, 255, 255), disc(size, c, 0.08 * R)))
    for (dx, dy, r) in ((0.62, -0.6, 0.13), (-0.66, 0.55, 0.1), (0.6, 0.55, 0.08), (-0.6, -0.5, 0.07)):
        sm = star(size, (c[0] + dx * R, c[1] + dy * R), r * R, thin=0.14)
        out = comp(out, glow(sm, GOLD_CORE, 0.05 * R, 1.4), solid(size, (255, 255, 250), sm))
    return out


def stance_chip(idx):
    name, base, deep, bright = STANCES[idx]
    rng = random.Random(SEED + 701 + 37 * idx)
    N = CS * F
    size = (N, N)
    c = (N / 2, N / 2 - 3 * F)
    R = 60 * F
    body = rough_disc(size, c, R, rng, wobble=0.02, jitter=0.002)
    field = rough_disc(size, c, R * 0.8, rng, wobble=0.015, jitter=0.002)
    rim = ImageChops.subtract(body, field)
    img = canvas(*size)
    if idx == 2:
        img = comp(img, glow(body, GOLD, 9 * F, 1.5), glow(body, GOLD_CORE, 3 * F, 1.0))
    img = comp(img, drop_shadow(size, body, 5 * F, 4 * F, 0.6), ink_edge(size, body, 2.6 * F, heavy=1.6 * F))
    img = comp(img, gold_paint(size, rim, rng, angle=30, bands=1.3))
    img = comp(img, solid(size, GOLD_CORE, scale_l(inter(top_lip(body, 2 * F), rim), 0.7)))
    notches = Image.new("L", size, 0)
    nd = ImageDraw.Draw(notches)
    for k in range(8):
        a = k * math.pi / 4 + math.pi / 8
        x, y = c[0] + math.cos(a) * R * 0.9, c[1] + math.sin(a) * R * 0.9
        nd.ellipse((x - 2.2 * F, y - 2.2 * F, x + 2.2 * F, y + 2.2 * F), fill=255)
    img = comp(img, solid(size, (110, 72, 26), shift(notches, 0.5 * F, 0.6 * F)), solid(size, GOLD_PALE, notches))
    # painted field: lit from the upper left, streaked like a brushed enamel
    light = radial_light(size, (c[0] - 0.3 * R, c[1] - 0.35 * R), 1.4 * R, power=1.1)
    st = streaks(size, rng, angle=-30, along=24, across=3)
    mix = Image.blend(light, st, 0.22)
    shade = tuple(max(0, int(v * 0.6)) for v in deep)
    img = comp(img, paint(size, mix, [(0, shade), (0.4, deep), (0.78, base), (1, bright)], field))
    img = comp(img, solid(size, INK, scale_l(inter(field, top_lip(field, 6 * F).filter(ImageFilter.GaussianBlur(2 * F))), 0.6)),
               solid(size, INK, ring_out(field, 1.4 * F)))
    if idx == 0:
        img = comp(img, blade_mark(size, (c[0] + 0.03 * R, c[1] + 0.0 * R), R * 0.95, rng))
    elif idx == 1:
        img = comp(img, shield_mark(size, (c[0], c[1] + 0.06 * R), R * 0.86, rng))
    else:
        img = comp(img, star_mark(size, c, R * 0.8, rng))
    return downsample(img)


def stance_sheet():
    sheet = canvas(CS * len(STANCES), CS)
    sprites = []
    for i, (name, *_rest) in enumerate(STANCES):
        sheet.alpha_composite(stance_chip(i), (i * CS, 0))
        sprites.append({"name": name, "box": [i * CS, 0, (i + 1) * CS, CS]})
    return sheet, sprites


# ------------------------------------------------------------------------------------------------------------- make

def make(out_dir, fonts):
    out = []

    def emit(asset, rel, img, sprites=()):
        save(img, out_dir, rel)
        out.append({"asset": asset, "path": f"art/{rel}", "size": [img.width, img.height], "sprites": list(sprites)})

    sheet, sprites = rank_sheet(fonts)
    emit("asset.rank-letters", "gauges/rank-letters.png", sheet, sprites)
    sheet, sprites = family_sheet()
    emit("asset.family-frames", "gauges/family-frames.png", sheet, sprites)
    sheet, sprites = stance_sheet()
    emit("asset.stance-chips", "gauges/stance-chips.png", sheet, sprites)
    return out

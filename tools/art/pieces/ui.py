"""Interface frames for Root 33: portrait fallback, prompt/settings frame, character card and lock badge,
vignette letterbox, act/countdown banner, tracker frame and step pips, speech bubble, clearing label plate.

Everything is drawn from scratch with Pillow in the shared painterly style (tools/art/style.py): ink edges,
bone paper, gold leaf and crimson brushwork. No text is baked in; the mod draws it at runtime.

Reproducible: all randomness comes from seeded Python RNGs. Image.effect_noise is not used, because it draws
from C rand(), which is unseeded and shared, so its output depends on what ran earlier in the process.
"""
import math
import random
import sys
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageOps

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from style import *  # noqa: E402,F401,F403
from style import (BONE, CRIMSON, CRIMSON_DEEP, GOLD, GOLD_CORE, GOLD_DEEP, INK, INK_SOFT, NIGHT, PAPER,  # noqa: E402
                   brush_stroke, canvas, curve, glow, inner_edge, outline_of, petal_tile, rgba, rough_rect_mask,
                   solid)

SEED = 3303


# ---------------------------------------------------------------- seeded texture helpers

def comp(base, *layers):
    for layer in layers:
        base = Image.alpha_composite(base, layer)
    return base


def noise(size, seed, sigma=40.0):
    """Per-pixel noise centred on 128 with roughly the given spread (seeded, unlike Image.effect_noise)."""
    w, h = size
    im = Image.frombytes("L", (w, h), random.Random(seed).randbytes(w * h))
    k = sigma / 73.9
    return im.point(lambda v: max(0, min(255, int(round(128 + (v - 128) * k)))))


def soft_noise(size, seed, cell=(8, 8), blur=2.0):
    """Low-frequency noise (a coarse grid scaled up), stretched to the full 0-255 range."""
    w, h = size
    small = noise((max(2, w // cell[0]), max(2, h // cell[1])), seed, 73.9)
    return ImageOps.autocontrast(small.resize((w, h), Image.BICUBIC).filter(ImageFilter.GaussianBlur(blur)))


def scale_mask(mask, k):
    return mask.point(lambda v: max(0, min(255, int(v * k))))


def clip(layer, mask):
    """Multiply an RGBA layer's alpha by an L mask."""
    out = layer.copy()
    out.putalpha(ImageChops.multiply(layer.getchannel("A"), mask))
    return out


def shifted(mask, dx, dy):
    """Move a mask without wrapping (ImageChops.offset wraps the far edge round to the near one)."""
    out = Image.new(mask.mode, mask.size, 0)
    out.paste(mask, (dx, dy))
    return out


def pressure(size, seed, cell=30):
    """A near-binary map of where a pen presses harder, so ink lines swell crisply instead of haloing."""
    return soft_noise(size, seed, (cell, cell), 5).point(lambda v: max(0, min(255, (v - 140) * 12)))


def grain_fill(size, base, seed, depth=0.08, streak=None):
    """A flat colour with paper grain and soft fibres around it (opaque RGB)."""
    w, h = size
    fine = noise(size, seed, 60).filter(ImageFilter.GaussianBlur(1.0))
    fib = soft_noise(size, seed + 1, streak or (6, 1), 1.5)
    g = ImageChops.add(fine, fib, 2.0, 0)
    g = ImageOps.autocontrast(g, cutoff=1)
    up = g.point(lambda v: max(0, (v - 128) * 2))
    down = g.point(lambda v: max(0, (128 - v) * 2))
    lo = tuple(int(c * (1 - depth)) for c in base)
    hi = tuple(min(255, int(c + (255 - c) * depth * 1.5)) for c in base)
    out = Image.new("RGB", size, base)
    out = Image.composite(Image.new("RGB", size, hi), out, scale_mask(up, 0.6))
    out = Image.composite(Image.new("RGB", size, lo), out, scale_mask(down, 0.6))
    return out


def mottle(size, a, b, seed, cell=48, amount=0.55, streak_cell=(70, 3)):
    """Dark painted body: colour a, clouded toward b, with faint horizontal brush streaks."""
    clouds = soft_noise(size, seed, (cell, cell), cell / 5)
    streaks = soft_noise(size, seed + 7, streak_cell, 1.2)
    n = ImageChops.add(clouds, streaks, 2.0, 0)
    n = ImageOps.autocontrast(n).point(lambda v: int(v * amount))
    return Image.composite(Image.new("RGB", size, b), Image.new("RGB", size, a), n)


def leaf(size, mask, seed=0, bands=1.5):
    """Gold leaf (warm sheen with speckle) shaped by mask; a seeded version of style.gold_leaf."""
    w, h = size
    grad = Image.linear_gradient("L").resize((w, h)).rotate(25)

    def ch(c):
        def f(v):
            s = abs(math.sin(v / 255 * math.pi * bands))
            base = GOLD_DEEP[c] + (GOLD[c] - GOLD_DEEP[c]) * (0.35 + 0.65 * s)
            return int(base + (GOLD_CORE[c] - base) * 0.65 * s ** 8)
        return grad.point(f)
    sheen = Image.merge("RGB", [ch(0), ch(1), ch(2)])
    speck = noise(size, seed + 99, 50).point(lambda v: 255 if v > 200 else 0).filter(ImageFilter.GaussianBlur(0.6))
    sheen = Image.composite(Image.new("RGB", size, GOLD_CORE), sheen, speck.point(lambda v: v // 3))
    out = sheen.convert("RGBA")
    out.putalpha(mask)
    return out


def aa(mask, r=0.7):
    return mask.filter(ImageFilter.GaussianBlur(r))


def band(mask, outside, inside=0):
    """A ring straddling a mask's edge: `outside` px beyond it and `inside` px within it, with no seam."""
    grown = mask.filter(ImageFilter.MaxFilter(outside * 2 + 1)) if outside else mask
    shrunk = mask.filter(ImageFilter.MinFilter(inside * 2 + 1)) if inside else mask
    return ImageChops.subtract(grown, shrunk)


def smooth(mask, r):
    """Round off a hard mask's joins: blur and re-threshold, then a touch of anti-aliasing."""
    return aa(mask.filter(ImageFilter.GaussianBlur(r)).point(lambda v: 255 if v > 127 else 0))


def stroke_mask(size, pts, width, seed, bristles=5, taper=True):
    return brush_stroke(size, pts, width, (255, 255, 255), seed=seed, bristles=bristles, taper=taper).getchannel("A")


def taper_poly(draw, pts, width, fill=255, power=0.5):
    """A solid stroke along pts, swelling in the middle and tapering to points at both ends."""
    n = len(pts)
    top, bot = [], []
    for i, (x, y) in enumerate(pts):
        a = pts[min(i + 1, n - 1)]
        b = pts[max(i - 1, 0)]
        dx, dy = a[0] - b[0], a[1] - b[1]
        ln = math.hypot(dx, dy) or 1
        nx, ny = -dy / ln, dx / ln
        t = i / max(1, n - 1)
        hw = width / 2 * (math.sin(math.pi * t) ** power)
        top.append((x + nx * hw, y + ny * hw))
        bot.append((x - nx * hw, y - ny * hw))
    draw.polygon(top + bot[::-1], fill=fill)


def gold_line(size, pts, width, seed, glow_r=0.0):
    """A thin gold rule (leaf) with an optional faint glow, as an RGBA layer."""
    m = Image.new("L", size, 0)
    taper_poly(ImageDraw.Draw(m), pts, width, power=0.15)
    m = aa(m, 0.5)
    out = canvas(*size)
    if glow_r:
        out = comp(out, glow(m, GOLD, glow_r, 0.7))
    return comp(out, leaf(size, m, seed))


def diamond(cx, cy, rx, ry):
    return [(cx, cy - ry), (cx + rx, cy), (cx, cy + ry), (cx - rx, cy)]


def gem(size, cx, cy, rx, ry, seed, rgb=CRIMSON):
    """A small crimson diamond set in gold, with an ink edge."""
    d_out = Image.new("L", size, 0)
    ImageDraw.Draw(d_out).polygon(diamond(cx, cy, rx + 3, ry + 3), fill=255)
    d_in = Image.new("L", size, 0)
    ImageDraw.Draw(d_in).polygon(diamond(cx, cy, rx, ry), fill=255)
    out = comp(canvas(*size), solid(size, INK, aa(outline_of(d_out, 1))), leaf(size, aa(d_out), seed))
    out = comp(out, solid(size, rgb, aa(d_in)))
    hi = Image.new("L", size, 0)
    ImageDraw.Draw(hi).polygon([(cx, cy - ry * 0.8), (cx + rx * 0.45, cy - ry * 0.1), (cx, cy)], fill=150)
    return comp(out, solid(size, GOLD_CORE, aa(hi, 0.8)))


def flourish(size, cx, cy, s, sx, sy, seed, gemmed=True):
    """Original corner ornament: two tapering gold rails along the edges, an inward curl and two leaves.

    (cx, cy) is the corner; sx, sy (+1/-1) point into the panel."""
    rng = random.Random(seed)
    m = Image.new("L", size, 0)
    d = ImageDraw.Draw(m)

    def P(u, v):
        return (cx + sx * u * s, cy + sy * v * s)

    # Rails running along both edges from the corner.
    for horiz in (True, False):
        pts = [P(t, 0) if horiz else P(0, t) for t in range(6, 170, 8)]
        taper_poly(d, pts, 7 * s, power=0.6)
    # The curl: a stem from the corner winding into a spiral.
    c = 50
    spiral = [P(8, 8)]
    for i in range(46):
        t = i / 45
        a = math.radians(225 + 470 * t)
        r = 32 * (1 - 0.78 * t)
        spiral.append(P(c + math.cos(a) * r, c + math.sin(a) * r))
    taper_poly(d, spiral, 9 * s, power=0.35)
    # Leaves sprouting inward from each rail.
    for horiz in (True, False):
        for base, ang, ln in ((92, 38, 44), (130, 28, 30)):
            ang += rng.uniform(-4, 4)
            bu, bv = (base, 0) if horiz else (0, base)
            du, dv = (math.cos(math.radians(ang)), math.sin(math.radians(ang)))
            if not horiz:
                du, dv = dv, du
            tip = (bu + du * ln, bv + dv * ln)
            mid = ((bu + tip[0]) / 2, (bv + tip[1]) / 2)
            nx, ny = -dv, du
            w = ln * 0.26
            poly = [P(*p) for p in curve((bu, bv), (mid[0] + nx * w, mid[1] + ny * w), tip, 12)] + \
                   [P(*p) for p in curve(tip, (mid[0] - nx * w, mid[1] - ny * w), (bu, bv), 12)]
            d.polygon(poly, fill=255)
    m = aa(m, 0.6)
    out = comp(canvas(*size), solid(size, INK, scale_mask(shifted(m, 1, 2).filter(ImageFilter.GaussianBlur(1.6)), 0.75)))
    out = comp(out, leaf(size, m, seed, bands=2.5))
    if gemmed:
        gx, gy = P(18, 18)
        out = comp(out, gem(size, gx, gy, 7 * s + 2, 7 * s + 2, seed + 3))
    return out


def dark_panel(size, box, seed, border_w=6, fill_alpha=242, inset=20, wash=CRIMSON_DEEP, shadow=True):
    """An ink panel with a brush-cut edge, painted body, gold border and an inner gold hairline."""
    m = rough_rect_mask(size, box, wobble=2.2, seed=seed)
    m = aa(m, 0.6)
    img = canvas(*size)
    if shadow:
        img = comp(img, glow(shifted(m, 0, 6), (0, 0, 0), 12, 0.7))
    body = mottle(size, INK, (46, 40, 62), seed + 1).convert("RGBA")
    # A low crimson wash rising from the bottom edge, and darker edges toward the border.
    x0, y0, x1, y1 = box
    wash_m = Image.linear_gradient("L").resize((x1 - x0, y1 - y0))
    wash_m = wash_m.point(lambda v: int(max(0, v - 120) * 0.5))
    wm = Image.new("L", size, 0)
    wm.paste(wash_m, (x0, y0))
    body = Image.composite(Image.new("RGBA", size, rgba(wash)), body, wm)
    edge_dark = inner_edge(m, 36).filter(ImageFilter.GaussianBlur(22))
    body = Image.composite(Image.new("RGBA", size, rgba((8, 6, 10))), body, scale_mask(edge_dark, 0.7))
    body.putalpha(scale_mask(m, fill_alpha / 255))
    img = comp(img, body)
    img = comp(img, solid(size, INK, outline_of(m, 2)))
    img = comp(img, leaf(size, inner_edge(m, border_w), seed + 2))
    if inset:
        m2 = rough_rect_mask(size, (x0 + inset, y0 + inset, x1 - inset, y1 - inset), wobble=1.0, seed=seed + 3)
        img = comp(img, solid(size, GOLD, scale_mask(aa(inner_edge(m2, 1), 0.4), 0.7)))
    return img


def bone_plate(size, poly, seed, edge=3, inner=7, shadow=True):
    """A bone-paper plaque shaped by poly, with an ink edge and a gold hairline inside."""
    m = Image.new("L", size, 0)
    ImageDraw.Draw(m).polygon(poly, fill=255)
    m = smooth(m, 1.5)
    img = canvas(*size)
    if shadow:
        img = comp(img, glow(shifted(m, 0, 4), (0, 0, 0), 5, 0.6))
    fill = grain_fill(size, BONE, seed, 0.045).convert("RGBA")
    shade = Image.linear_gradient("L").resize(size).point(lambda v: int(max(0, v - 110) * 0.55))
    fill = Image.composite(Image.new("RGBA", size, rgba(PAPER)), fill, shade)
    fill.putalpha(m)
    img = comp(img, fill)
    m2 = m.filter(ImageFilter.MinFilter(inner * 2 + 1))
    img = comp(img, solid(size, GOLD_DEEP, aa(inner_edge(m2, 1), 0.4)))
    # Heavier ink on the lower right, like a pen pressed harder on the shadow side.
    weight = ImageChops.subtract(shifted(m, 1, 2), m)
    img = comp(img, solid(size, INK, aa(ImageChops.lighter(band(m, edge - 1, 1), weight), 0.5)))
    return img, m


def save(img, out_dir, rel):
    p = Path(out_dir) / rel
    p.parent.mkdir(parents=True, exist_ok=True)
    img.save(p, optimize=True)
    return p


def entry(asset, rel, img, sprites=()):
    return {"asset": asset, "path": "art/" + rel, "size": [img.width, img.height],
            "sprites": [{"name": n, "box": list(b)} for n, b in sprites]}


# ---------------------------------------------------------------- pieces

def portrait_fallback(seed):
    """A neutral head-and-shoulders bust: near-white bone greys with an ink edge, tinted per character at runtime."""
    S, EXTRA = 512, 40  # drawn taller, then cropped, so the cut-off at the bottom has no edge effects
    size = (S, S + EXTRA)
    rng = random.Random(seed)
    m = Image.new("L", size, 0)
    d = ImageDraw.Draw(m)
    cx, cy, rx, ry = 256, 172, 86, 110
    head = []
    for i in range(200):
        t = i / 200 * 2 * math.pi
        sx, cyy = math.sin(t), -math.cos(t)
        # Wider at the brow, tapering to a jaw and a rounded chin.
        narrow = 1 - 0.30 * max(0.0, cyy) ** 1.25 + 0.03 * max(0.0, -cyy)
        head.append((cx + rx * sx * narrow, cy + ry * cyy))
    d.polygon(head, fill=255)
    for side in (-1, 1):  # small ears
        ex = cx + side * (rx - 6)
        d.ellipse((ex - 12, cy + 2, ex + 12, cy + 50), fill=255)
    d.polygon([(214, 236), (298, 236), (306, 330), (206, 330)], fill=255)
    right = curve((300, 300), (326, 352), (408, 364), 30) + curve((408, 364), (474, 374), (488, S + EXTRA), 30)
    left = [(S - x, y) for x, y in right]
    d.polygon(right + left[::-1], fill=255)
    m = smooth(m, 5)

    img = canvas(*size)
    base = grain_fill(size, (240, 237, 232), seed + 1, 0.035).convert("RGBA")
    # Light from the upper left: a matte shadow side on the right, a cast shadow under the jaw, and a soft rim.
    side = Image.linear_gradient("L").rotate(90).resize(size).point(lambda v: int(max(0, 255 - v - 120) * 1.1))
    jaw = Image.new("L", size, 0)
    ImageDraw.Draw(jaw).polygon([(x + 4, y + 16) for x, y in head if y > cy + 40], fill=255)
    jaw = jaw.filter(ImageFilter.GaussianBlur(5))
    head_m = Image.new("L", size, 0)
    ImageDraw.Draw(head_m).polygon(head, fill=255)
    jaw = ImageChops.subtract(jaw, head_m)
    rim = inner_edge(m, 18).filter(ImageFilter.GaussianBlur(10))
    shade = ImageChops.lighter(ImageChops.lighter(scale_mask(side, 0.75), scale_mask(jaw, 0.8)), scale_mask(rim, 0.45))
    base = Image.composite(Image.new("RGBA", size, rgba((200, 196, 192))), base, shade)
    # Painted strokes following the form: arcs around the skull, sweeps across the shoulders.
    strokes = canvas(*size)
    for i in range(22):
        if i < 9:
            a0 = rng.uniform(160, 320)
            r = rng.uniform(38, 78)
            pts = [(cx + math.cos(math.radians(a0 + k * 7)) * r, cy - 6 + math.sin(math.radians(a0 + k * 7)) * r * 1.2)
                   for k in range(10)]
        else:
            y = rng.uniform(388, 500)
            x = rng.uniform(40, 340)
            pts = curve((x, y + 12), (x + 70, y - 12), (x + 160, y + rng.uniform(-4, 16)), 16)
        col = (222, 218, 212) if rng.random() < (0.75 if i < 9 else 0.5) else (250, 249, 246)
        strokes = comp(strokes, brush_stroke(size, pts, rng.uniform(14, 26), col, alpha=int(rng.uniform(70, 120)),
                                             seed=seed + 10 + i, bristles=6))
    base = comp(base, strokes)
    # A faint neckline so the bust reads as a person, not a mannequin.
    collar = Image.new("L", size, 0)
    taper_poly(ImageDraw.Draw(collar), curve((196, 342), (256, 404), (316, 342), 24), 5, power=0.5)
    base = comp(base, solid(size, (150, 146, 144), scale_mask(aa(collar, 0.8), 0.6)))
    base.putalpha(m)
    img = comp(img, base)
    # Ink edge with brush pressure: 5 px everywhere, swelling to 9 px in places and on the shadow side.
    # The swell steps 5 -> 7 -> 9 px over a wider and a narrower pressure zone, so it tapers instead of bulging.
    pn = soft_noise(size, seed + 4, (30, 30), 5)
    wide = pn.point(lambda v: max(0, min(255, (v - 125) * 12)))
    narrow = pn.point(lambda v: max(0, min(255, (v - 160) * 12)))
    weight = ImageChops.subtract(shifted(m, 3, 2), m)
    ring = ImageChops.lighter(band(m, 5, 1), ImageChops.multiply(band(m, 7), wide))
    ring = ImageChops.lighter(ImageChops.lighter(ring, ImageChops.multiply(band(m, 9), narrow)), weight)
    img = comp(img, solid(size, INK, aa(ring, 0.6)))
    return img.crop((0, 0, S, S))


def prompt_frame(seed):
    W, H = 1200, 700
    box = (26, 26, W - 26, H - 26)
    img = dark_panel((W, H), box, seed, border_w=6, fill_alpha=244, inset=22)
    c = 22 + 26
    for (x, y, sx, sy) in ((c, c, 1, 1), (W - c, c, -1, 1), (c, H - c, 1, -1), (W - c, H - c, -1, -1)):
        img = comp(img, flourish((W, H), x, y, 1.0, sx, sy, seed + x + y))
    return img


def lock_badge(seed):
    S, c = 200, 100
    img = canvas(S, S)
    disc = Image.new("L", (S, S), 0)
    ImageDraw.Draw(disc).ellipse((c - 86, c - 86, c + 86, c + 86), fill=255)
    disc = aa(disc)
    inner = Image.new("L", (S, S), 0)
    ImageDraw.Draw(inner).ellipse((c - 72, c - 72, c + 72, c + 72), fill=255)
    inner = aa(inner)
    img = comp(img, glow(shifted(disc, 0, 5), (0, 0, 0), 7, 0.8))
    img = comp(img, solid((S, S), INK, disc))
    img = comp(img, leaf((S, S), ImageChops.subtract(disc.filter(ImageFilter.MinFilter(5)), inner.filter(ImageFilter.MaxFilter(5))), seed, 1.0))
    rad = Image.radial_gradient("L").resize((S, S))
    red = Image.composite(Image.new("RGBA", (S, S), rgba(CRIMSON_DEEP)), Image.new("RGBA", (S, S), rgba((206, 40, 62))),
                          rad.point(lambda v: min(255, int(v * 1.4))))
    red.putalpha(inner)
    img = comp(img, red)
    # The padlock: bone with an ink edge.
    lock = Image.new("L", (S, S), 0)
    ld = ImageDraw.Draw(lock)
    ld.ellipse((c - 30, 44, c + 30, 104), fill=255)
    ld.ellipse((c - 17, 57, c + 17, 91), fill=0)
    ld.rectangle((c - 17, 74, c + 17, 100), fill=0)
    ld.rounded_rectangle((c - 40, 90, c + 40, 148), radius=10, fill=255)
    lock = aa(lock)
    img = comp(img, solid((S, S), INK, aa(outline_of(lock, 4))))
    body = grain_fill((S, S), BONE, seed + 1, 0.06).convert("RGBA")
    sh = Image.new("L", (S, S), 0)
    ImageDraw.Draw(sh).rectangle((c + 10, 40, S, S), fill=110)
    body = Image.composite(Image.new("RGBA", (S, S), rgba(PAPER)), body, sh.filter(ImageFilter.GaussianBlur(6)))
    body.putalpha(lock)
    img = comp(img, body)
    key = Image.new("L", (S, S), 0)
    kd = ImageDraw.Draw(key)
    kd.ellipse((c - 9, 104, c + 9, 122), fill=255)
    kd.polygon([(c - 5, 116), (c + 5, 116), (c + 8, 136), (c - 8, 136)], fill=255)
    img = comp(img, solid((S, S), INK, aa(key)))
    return img


def character_card(seed):
    CW, CH, B, GAP = 600, 900, 200, 20
    size = (CW, CH)
    card = dark_panel(size, (12, 12, CW - 12, CH - 12), seed, border_w=6, fill_alpha=250, inset=0)
    # Portrait window: an arched opening over the top ~60% of the card.
    x0, y0, x1, y1, arch = 46, 46, CW - 46, 530, 92
    pts = [(x0, y1), (x0, y0 + arch)]
    for i in range(1, 60):
        a = math.pi * i / 60
        pts.append(((x0 + x1) / 2 - (x1 - x0) / 2 * math.cos(a), y0 + arch - arch * math.sin(a)))
    pts += [(x1, y0 + arch), (x1, y1)]
    win = Image.new("L", size, 0)
    ImageDraw.Draw(win).polygon(pts, fill=255)
    win = aa(win)
    # Corner ornaments in the spandrels beside the arch and at the foot of the card.
    for (x, y, sx, sy, s) in ((30, 30, 1, 1, 0.62), (CW - 30, 30, -1, 1, 0.62), (30, CH - 30, 1, -1, 0.5), (CW - 30, CH - 30, -1, -1, 0.5)):
        card = comp(card, flourish(size, x, y, s, sx, sy, seed + x + y, gemmed=True))
    a = card.getchannel("A")
    card.putalpha(ImageChops.subtract(a, win))
    # The window's rim: ink, then gold, then a soft shadow falling onto the portrait.
    card = comp(card, solid(size, INK, aa(outline_of(win, 9), 0.5)))
    card = comp(card, leaf(size, aa(ImageChops.subtract(outline_of(win, 6), outline_of(win, 2)), 0.5), seed + 5))
    card = comp(card, solid(size, (8, 6, 10), scale_mask(ImageChops.multiply(inner_edge(win, 30).filter(ImageFilter.GaussianBlur(12)), win), 0.45)))
    # Name plate: a crimson swallowtail ribbon behind a bone plaque that overlaps the window's foot.
    ry0, ry1, rx = 514, 560, 30
    rib = Image.new("L", size, 0)
    ImageDraw.Draw(rib).polygon([(rx, ry0), (CW - rx, ry0), (CW - rx - 16, (ry0 + ry1) / 2), (CW - rx, ry1),
                                 (rx, ry1), (rx + 16, (ry0 + ry1) / 2)], fill=255)
    rib = aa(rib)
    paint = Image.new("RGBA", size, rgba(CRIMSON))
    streaks = soft_noise(size, seed + 21, (40, 2), 1.0)
    paint = Image.composite(Image.new("RGBA", size, rgba(CRIMSON_DEEP)), paint, streaks.point(lambda v: max(0, (110 - v) * 2)))
    fold = Image.new("L", size, 0)  # darker where the ribbon turns behind the plaque
    for fx in (78, CW - 78):
        ImageDraw.Draw(fold).rectangle((fx - 14, ry0, fx + 14, ry1), fill=200)
    paint = Image.composite(Image.new("RGBA", size, rgba((70, 8, 20))), paint, fold.filter(ImageFilter.GaussianBlur(8)))
    paint.putalpha(rib)
    card = comp(card, glow(shifted(rib, 0, 4), (0, 0, 0), 4, 0.6), paint, solid(size, INK, aa(band(rib, 2), 0.5)))
    px0, py0, px1, py1, cut = 70, 498, CW - 70, 574, 26
    plate, _ = bone_plate(size, [(px0 + cut, py0), (px1 - cut, py0), (px1, (py0 + py1) / 2), (px1 - cut, py1),
                                 (px0 + cut, py1), (px0, (py0 + py1) / 2)], seed + 8)
    card = comp(card, plate)
    card = comp(card, gem(size, px0 + 22, (py0 + py1) / 2, 6, 8, seed + 9), gem(size, px1 - 22, (py0 + py1) / 2, 6, 8, seed + 10))
    # Engine / DLC line: a slim crimson-deep band between two gold rules.
    strip = Image.new("L", size, 0)
    taper_poly(ImageDraw.Draw(strip), [(x, 612) for x in range(64, CW - 63, 8)], 40, power=0.12)
    strip = aa(strip)
    card = comp(card, solid(size, CRIMSON_DEEP, scale_mask(strip, 0.55)))
    for y in (590, 634):
        card = comp(card, gold_line(size, [(x, y) for x in range(72, CW - 71, 8)], 2.6, seed + y))
    # Summary area: a slightly lifted inset with a gold hairline and gem corners.
    sbox = (62, 654, CW - 62, 822)
    sm = aa(rough_rect_mask(size, sbox, wobble=1.0, seed=seed + 11), 0.5)
    card = comp(card, solid(size, (46, 40, 58), scale_mask(sm, 0.55)))
    card = comp(card, solid(size, GOLD, scale_mask(aa(inner_edge(sm, 1), 0.4), 0.75)))
    for (gx, gy) in ((sbox[0], sbox[1]), (sbox[2], sbox[1]), (sbox[0], sbox[3]), (sbox[2], sbox[3])):
        card = comp(card, gem(size, gx, gy, 4, 4, seed + gx + gy, rgb=GOLD_DEEP))
    sheet = canvas(CW + GAP + B, CH)
    sheet.alpha_composite(card, (0, 0))
    sheet.alpha_composite(lock_badge(seed + 40), (CW + GAP, 0))
    return sheet, [("card", (0, 0, CW, CH)), ("lock-badge", (CW + GAP, 0, CW + GAP + B, B))]


def letterbox_bar(W, BH, edge_y, seed):
    """One letterbox bar (top orientation): solid ink down to edge_y, then a dry-brushed edge and a gold rule."""
    rng = random.Random(seed)
    size = (W, BH)
    ph1, ph2 = rng.uniform(0, 6.28), rng.uniform(0, 6.28)

    def wave(x):  # the hand-painted line of the bar's edge
        return edge_y + 8 * math.sin(x / 260 + ph1) + 3 * math.sin(x / 83 + ph2)
    m = Image.new("L", size, 0)
    d = ImageDraw.Draw(m)
    d.polygon([(0, 0), (W, 0)] + [(x, wave(x) - 6) for x in range(W, -1, -12)], fill=255)
    # Loaded strokes along the edge: long, tapered, overlapping, a few overshooting into the picture.
    for _ in range(150):
        ln = rng.uniform(180, 640)
        x = rng.uniform(-300, W)
        off = rng.choice((rng.uniform(-6, 4), rng.uniform(-6, 4), rng.uniform(4, 22)))
        w = rng.uniform(8, 16) if off < 4 else rng.uniform(5, 12)
        tilt = rng.uniform(-2.5, 2.5)
        pts = [(x + ln * t / 14, wave(x + ln * t / 14) + off + tilt * t / 14) for t in range(15)]
        if off >= 4 and rng.random() < 0.6:  # dry stroke: breaks into dashes
            k = 0
            while k < 14:
                run = rng.randint(2, 5)
                taper_poly(d, pts[k:k + run + 1], w * rng.uniform(0.6, 1.0), power=0.4)
                k += run + rng.randint(1, 2)
        else:
            taper_poly(d, pts, w, power=0.45)
    # Fine bristle hairlines trailing below the edge.
    for _ in range(70):
        ln = rng.uniform(80, 360)
        x = rng.uniform(-100, W)
        off = rng.uniform(8, 26)
        pts = [(x + ln * t / 10, wave(x + ln * t / 10) + off + rng.uniform(-0.4, 0.4)) for t in range(11)]
        taper_poly(d, pts, rng.uniform(1.5, 3.2), power=0.3)
    m = aa(m, 0.6)
    body = mottle(size, INK, (40, 34, 52), seed + 2, cell=90, amount=0.4, streak_cell=(90, 2)).convert("RGBA")
    body.putalpha(m)
    img = comp(canvas(W, BH), glow(shifted(m, 0, 5), (0, 0, 0), 8, 0.45), body)
    # The thin gold rule, following the edge's line and fading out toward the screen edges, with a gem at its centre.
    ry = edge_y - 22
    rule = Image.new("L", size, 0)
    taper_poly(ImageDraw.Draw(rule), [(x, wave(x) - edge_y + ry) for x in range(0, W + 1, 12)], 3.2, power=0.08)
    fade = Image.linear_gradient("L").rotate(90).resize((W, BH)).point(lambda v: int(255 * min(1, (1 - abs(v - 127.5) / 127.5) * 3.2)))
    rule = ImageChops.multiply(aa(rule, 0.5), fade)
    img = comp(img, glow(rule, GOLD, 4, 0.6), leaf(size, rule, seed + 3, bands=3))
    img = comp(img, gem(size, W / 2, wave(W / 2) - edge_y + ry, 9, 6, seed + 4))
    return img


def vignette_frame(seed):
    W, H, BH = 1920, 1080, 200
    img = canvas(W, H)
    top = letterbox_bar(W, BH, 118, seed)
    bottom = ImageOps.flip(letterbox_bar(W, BH, 118, seed + 50))
    img.alpha_composite(top, (0, 0))
    img.alpha_composite(bottom, (0, H - BH))
    return img


def banner_frame(seed):
    W, H = 1400, 260
    size = (W, H)
    rng = random.Random(seed)

    L0, R0 = 60, W - 60

    def cy(x):  # the stroke's centre line, with a gentle flick up at the right end
        flick = -10 * max(0.0, (x - (R0 - 240)) / 240) ** 2
        return 130 + 6 * math.sin(x / W * math.pi * 1.6 + 0.5) + flick

    def half(x):  # the stroke swells a little toward the middle
        return 72 * (0.9 + 0.1 * math.sin(math.pi * min(max(x / W, 0), 1)))
    m = Image.new("L", size, 0)
    d = ImageDraw.Draw(m)
    nb = 80
    for b in range(nb):
        f = b / (nb - 1) * 2 - 1
        edge = abs(f) ** 2
        # Left: a loaded, rounded start. Right: the brush runs dry and splits into bristles.
        xs = L0 + 58 * (1 - math.sqrt(max(0.0, 1 - f * f))) + rng.uniform(0, 7)
        xe = R0 - edge * rng.uniform(20, 150) - rng.expovariate(1 / 22)
        xs_i, xe_i = int(xs), int(xe)
        pts = [(x, cy(x) + f * half(x) + rng.uniform(-0.4, 0.4)) for x in range(xs_i, xe_i, 20)] + [(xe, cy(xe) + f * half(xe))]
        d.line(pts, fill=255, width=int(rng.uniform(3, 6)), joint="curve")
        if rng.random() < 0.35:  # a dry dash carried past the end
            gap, ln = rng.uniform(8, 26), rng.uniform(24, 90)
            dash = [(xe + gap + ln * t / 6, cy(xe + gap + ln * t / 6) + f * half(xe)) for t in range(7)]
            taper_poly(d, dash, rng.uniform(2, 4.5), power=0.35)
    m = aa(m, 1.0).point(lambda v: min(255, int(v * 1.4)))
    img = canvas(W, H)
    img = comp(img, glow(shifted(m, 0, 8), (0, 0, 0), 12, 0.45))
    # Crimson body: lighter along the top, deeper along the bottom, with horizontal brush streaks.
    streaks = soft_noise(size, seed + 1, (60, 2), 1.0)
    body = Image.new("RGBA", size, rgba(CRIMSON))
    light = streaks.point(lambda v: max(0, (v - 150) * 2))
    dark = streaks.point(lambda v: max(0, (100 - v) * 2))
    body = Image.composite(Image.new("RGBA", size, rgba((214, 52, 74))), body, scale_mask(light, 0.7))
    body = Image.composite(Image.new("RGBA", size, rgba(CRIMSON_DEEP)), body, scale_mask(dark, 0.7))
    vert = Image.linear_gradient("L").resize(size)
    body = Image.composite(Image.new("RGBA", size, rgba(CRIMSON_DEEP)), body, vert.point(lambda v: int(max(0, v - 140) * 1.3)))
    body = Image.composite(Image.new("RGBA", size, rgba((226, 74, 92))), body, vert.point(lambda v: int(max(0, 70 - v) * 1.6)))
    body.putalpha(m)
    img = comp(img, body)
    # Gold edges along the top and bottom of the stroke.
    for k, sgn in enumerate((-1, 1)):
        pts = [(x, cy(x) + sgn * (half(x) - 7) + rng.uniform(-0.4, 0.4)) for x in range(124, W - 219, 10)]
        e = Image.new("L", size, 0)
        taper_poly(ImageDraw.Draw(e), pts, 6.5, power=0.22)
        e = aa(e, 0.6)
        img = comp(img, solid(size, INK, scale_mask(shifted(e, 0, 2).filter(ImageFilter.GaussianBlur(1.2)), 0.7)))
        img = comp(img, leaf(size, e, seed + 5 + k, bands=3))
        pts2 = [(x, cy(x) + sgn * (half(x) - 20)) for x in range(190, W - 279, 10)]
        e2 = Image.new("L", size, 0)
        taper_poly(ImageDraw.Draw(e2), pts2, 2, power=0.12)
        img = comp(img, solid(size, GOLD, scale_mask(aa(e2, 0.4), 0.55)))
    # A few petals shaken off the dry end, drifting right as in the title art.
    prng = random.Random(seed + 9)
    for i in range(7):
        x = R0 - 30 + prng.uniform(0, 80) + i * 6
        y = prng.uniform(52, H - 52)
        pt = petal_tile(prng.uniform(12, 21), CRIMSON if i != 3 else BONE, prng)
        if x + pt.width / 2 < W - 2:
            img.alpha_composite(pt, (int(x - pt.width / 2), int(y - pt.height / 2)))
    return img


def make_pip(state, seed, S=96, r=24):
    """A step pip in its own cell: 0 empty socket, 1 lit gold, 2 blazing with a crimson-gold aura."""
    size = (S, S)
    c = S / 2
    img = canvas(S, S)
    disc = Image.new("L", size, 0)
    ImageDraw.Draw(disc).ellipse((c - r, c - r, c + r, c + r), fill=255)
    disc = aa(disc)
    inner = Image.new("L", size, 0)
    ri = r * 0.7
    ImageDraw.Draw(inner).ellipse((c - ri, c - ri, c + ri, c + ri), fill=255)
    inner = aa(inner)
    if state == 2:
        img = comp(img, glow(disc, CRIMSON, 13, 1.5), glow(disc, GOLD, 7, 1.6))
        rays = Image.new("L", size, 0)
        rd = ImageDraw.Draw(rays)
        for k in range(8):
            a = math.radians(k * 45 + 22.5)
            ln = r + (18 if k % 2 == 0 else 10)
            sp = 0.16 if k % 2 == 0 else 0.22
            rd.polygon([(c + math.cos(a - sp) * r * 0.6, c + math.sin(a - sp) * r * 0.6), (c + math.cos(a) * ln, c + math.sin(a) * ln),
                        (c + math.cos(a + sp) * r * 0.6, c + math.sin(a + sp) * r * 0.6)], fill=255)
        img = comp(img, solid(size, GOLD_CORE, aa(rays, 0.8)))
    img = comp(img, solid(size, (0, 0, 0), scale_mask(shifted(disc, 0, 2).filter(ImageFilter.GaussianBlur(2)), 0.6)))
    img = comp(img, solid(size, INK, disc))
    if state == 0:
        img = comp(img, solid(size, (14, 11, 16), inner))
        shade = Image.new("L", size, 0)
        ImageDraw.Draw(shade).ellipse((c - ri, c - ri + 4, c + ri, c + ri + 4), fill=255)
        img = comp(img, solid(size, INK_SOFT, ImageChops.multiply(inner, shade.filter(ImageFilter.GaussianBlur(2)))))
    else:
        rad = Image.radial_gradient("L").resize((int(ri * 2) + 2, int(ri * 2) + 2))
        g = Image.new("L", size, 255)
        g.paste(rad, (int(c - ri) - 1, int(c - ri) - 1))
        inner_col = GOLD_CORE if state == 2 else (250, 214, 136)
        outer_col = GOLD if state == 2 else GOLD_DEEP
        fill = Image.composite(Image.new("RGBA", size, rgba(outer_col)), Image.new("RGBA", size, rgba(inner_col)), g)
        fill.putalpha(inner)
        img = comp(img, fill)
        hi = Image.new("L", size, 0)
        ImageDraw.Draw(hi).ellipse((c - ri * 0.55, c - ri * 0.7, c + ri * 0.05, c - ri * 0.15), fill=210)
        img = comp(img, solid(size, (255, 252, 240), hi.filter(ImageFilter.GaussianBlur(1.5))))
    rim = ImageChops.subtract(disc, disc.filter(ImageFilter.MinFilter(5)))
    img = comp(img, leaf(size, rim, seed, 1.0) if state else solid(size, GOLD_DEEP, rim))
    # Fade anything near the cell edge so the glow never ends in a hard line.
    fall = Image.radial_gradient("L").resize(size).point(lambda v: 255 if v < 150 else max(0, int(255 - (v - 150) * 2.6)))
    img.putalpha(ImageChops.multiply(img.getchannel("A"), fall))
    return img


def tracker_frame(seed):
    PW, PH, S, GAP = 640, 440, 96, 16
    size = (PW, PH)
    panel = dark_panel(size, (12, 12, PW - 12, PH - 12), seed, border_w=4, fill_alpha=232, inset=14)
    for (x, y, sx, sy) in ((26, 26, 1, 1), (PW - 26, 26, -1, 1), (26, PH - 26, 1, -1), (PW - 26, PH - 26, -1, -1)):
        panel = comp(panel, flourish(size, x, y, 0.44, sx, sy, seed + x + y))
    # Header rule under a title row.
    panel = comp(panel, gold_line(size, [(x, 78) for x in range(90, PW - 89, 8)], 2.4, seed + 6, glow_r=2))
    sheet = canvas(PW + GAP + S, PH)
    sheet.alpha_composite(panel, (0, 0))
    sprites = [("panel", (0, 0, PW, PH))]
    for i, name in enumerate(("pip-empty", "pip-lit", "pip-blazing")):
        x, y = PW + GAP, 20 + i * (S + 20)
        sheet.alpha_composite(make_pip(i, seed + 30 + i, S), (x, y))
        sprites.append((name, (x, y, x + S, y + S)))
    return sheet, sprites


def bubble(tail, seed, CW=520, CH=330):
    """A hand-inked bone speech bubble with its tail at the lower left or lower right."""
    size = (CW, CH)
    flip = tail == "right"

    def X(x):
        return CW - x if flip else x
    m = Image.new("L", size, 0)
    d = ImageDraw.Draw(m)
    d.rounded_rectangle((24, 20, CW - 24, 238), radius=74, fill=255)
    # A swooping tail leaving the lower corner (kept inside the corner's 9-slice cell).
    tail_pts = curve((X(100), 228), (X(100), 288), (X(38), 316), 18) + curve((X(38), 316), (X(130), 286), (X(172), 228), 18)
    d.polygon(tail_pts, fill=255)
    # A slight hand-made wobble on the edge.
    wob = soft_noise(size, seed, (30, 30), 4).point(lambda v: int((v - 128) * 0.3 + 128))
    m = ImageChops.add(m.filter(ImageFilter.GaussianBlur(3)), wob, 1.0, -128)
    m = smooth(m, 1.0)
    img = canvas(CW, CH)
    img = comp(img, solid(size, INK, scale_mask(shifted(m, 5, 7).filter(ImageFilter.GaussianBlur(5)), 0.4)))
    fill = grain_fill(size, BONE, seed + 1, 0.035).convert("RGBA")
    shade = Image.linear_gradient("L").resize(size).point(lambda v: int(max(0, v - 120) * 0.5))
    fill = Image.composite(Image.new("RGBA", size, rgba(PAPER)), fill, shade)
    fill.putalpha(m)
    img = comp(img, fill)
    # Hand-inked edge: a steady line, swelling with pen pressure and heavier on the lower right.
    press = pressure(size, seed + 2, 40)
    weight = ImageChops.subtract(shifted(m, 2, 3), m)
    ring = ImageChops.lighter(ImageChops.lighter(band(m, 2, 2), ImageChops.multiply(band(m, 4), press)), weight)
    img = comp(img, solid(size, INK, aa(ring, 0.6)))
    return img


def speech_bubble(seed):
    CW, CH = 520, 330
    sheet = canvas(CW * 2, CH)
    sheet.alpha_composite(bubble("left", seed), (0, 0))
    sheet.alpha_composite(bubble("right", seed + 1), (CW, 0))
    return sheet, [("tail-left", (0, 0, CW, CH)), ("tail-right", (CW, 0, CW * 2, CH))]


def label_plate(seed):
    W, H = 420, 110
    x0, y0, x1, y1, cut = 18, 22, W - 18, 88, 24
    cy = (y0 + y1) / 2
    # Pointed, gently bowed ends; the long middle is straight so the plate 9-slices cleanly.
    left_end = curve((x0 + cut, y1), (x0 + cut * 0.45, y1 - 8), (x0, cy), 10) + curve((x0, cy), (x0 + cut * 0.45, y0 + 8), (x0 + cut, y0), 10)
    right_end = [(W - x, y) for x, y in reversed(left_end)]
    img, _ = bone_plate((W, H), right_end + left_end, seed, edge=3, inner=7)
    img = comp(img, gem((W, H), x0 + 20, cy, 5, 7, seed + 1), gem((W, H), x1 - 20, cy, 5, 7, seed + 2))
    return img


# ---------------------------------------------------------------- module entry

def make(out_dir, fonts):
    out = []
    img = portrait_fallback(SEED + 100)
    save(img, out_dir, "portraits/silhouette.png")
    out.append(entry("asset.portrait-fallback", "portraits/silhouette.png", img))

    img = prompt_frame(SEED + 200)
    save(img, out_dir, "ui/prompt-frame.png")
    out.append(entry("asset.prompt-frame", "ui/prompt-frame.png", img))

    img, sprites = character_card(SEED + 300)
    save(img, out_dir, "ui/character-card.png")
    out.append(entry("asset.card-frame", "ui/character-card.png", img, sprites))

    img = vignette_frame(SEED + 400)
    save(img, out_dir, "ui/vignette-frame.png")
    out.append(entry("asset.vignette-frame", "ui/vignette-frame.png", img))

    img = banner_frame(SEED + 500)
    save(img, out_dir, "ui/banner-frame.png")
    out.append(entry("asset.banner-frame", "ui/banner-frame.png", img))

    img, sprites = tracker_frame(SEED + 600)
    save(img, out_dir, "ui/tracker-frame.png")
    out.append(entry("asset.tracker-frame", "ui/tracker-frame.png", img, sprites))

    img, sprites = speech_bubble(SEED + 700)
    save(img, out_dir, "ui/speech-bubble.png")
    out.append(entry("asset.bubble-frame", "ui/speech-bubble.png", img, sprites))

    img = label_plate(SEED + 800)
    save(img, out_dir, "ui/label-plate.png")
    out.append(entry("asset.label-plate", "ui/label-plate.png", img))
    return out

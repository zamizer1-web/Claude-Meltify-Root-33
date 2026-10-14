"""Shared painterly style for Root 33's interface art.

Every piece is original and drawn with Pillow from these helpers, so the set stays consistent:
ink outlines, bone paper, gold leaf, crimson petals and a river teal, with brushy, slightly
uneven edges that sit between Root's storybook look and Clair Obscur's painted one.
"""
import math
import random

from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageFont

INK = (24, 20, 26)
INK_SOFT = (52, 44, 54)
BONE = (242, 236, 224)
PAPER = (226, 214, 190)
GOLD = (240, 196, 110)
GOLD_DEEP = (176, 128, 52)
GOLD_CORE = (255, 246, 222)
CRIMSON = (178, 22, 46)
CRIMSON_DEEP = (112, 12, 30)
TEAL = (98, 170, 176)
TEAL_DEEP = (40, 98, 112)
NIGHT = (34, 30, 52)
DUSK = (196, 108, 92)
# Root's suit colours, for anything that marks a suit.
SUIT = {"fox": (196, 62, 44), "rabbit": (232, 196, 70), "mouse": (232, 136, 62), "bird": (86, 160, 168)}


def canvas(w, h):
    return Image.new("RGBA", (w, h), (0, 0, 0, 0))


def font(path, size):
    return ImageFont.truetype(path, size)


def rgba(rgb, a=255):
    return tuple(rgb) + (a,)


def solid(size, rgb, mask):
    """A flat colour layer shaped by an L mask."""
    layer = Image.new("RGBA", size, rgba(rgb, 0))
    layer.putalpha(mask)
    return layer


def glow(mask, rgb, radius, strength=1.0):
    m = mask.filter(ImageFilter.GaussianBlur(radius))
    if strength != 1.0:
        m = m.point(lambda v: min(255, int(v * strength)))
    return solid(mask.size, rgb, m)


def paper_texture(size, base=BONE, amount=0.22, seed=0):
    """Bone paper with soft fibres; returns RGBA (opaque)."""
    w, h = size
    noise = Image.effect_noise((w, h), 40).filter(ImageFilter.GaussianBlur(1.2))
    fibres = Image.effect_noise((max(1, w // 6), h), 70).resize((w, h), Image.BICUBIC).filter(ImageFilter.GaussianBlur(2))
    shade = ImageChops.add(noise, fibres, 2.0, 128)
    tex = ImageChops.multiply(Image.new("RGB", size, base), Image.merge("RGB", [shade] * 3))
    return Image.blend(Image.new("RGB", size, base), tex, amount).convert("RGBA")


def rough_rect_mask(size, box, radius=18, wobble=3.0, seed=0, steps=220):
    """A rounded rectangle whose edge wobbles like a brush-cut panel."""
    rng = random.Random(seed)
    x0, y0, x1, y1 = box
    pts = []
    per = 2 * ((x1 - x0) + (y1 - y0))
    for i in range(steps):
        t = i / steps * per
        if t < x1 - x0:
            px, py = x0 + t, y0
        elif t < (x1 - x0) + (y1 - y0):
            px, py = x1, y0 + (t - (x1 - x0))
        elif t < 2 * (x1 - x0) + (y1 - y0):
            px, py = x1 - (t - (x1 - x0) - (y1 - y0)), y1
        else:
            px, py = x0, y1 - (t - 2 * (x1 - x0) - (y1 - y0))
        pts.append((px + rng.uniform(-wobble, wobble), py + rng.uniform(-wobble, wobble)))
    m = Image.new("L", size, 0)
    ImageDraw.Draw(m).polygon(pts, fill=255)
    if radius:
        m = m.filter(ImageFilter.GaussianBlur(radius / 6)).point(lambda v: 255 if v > 128 else 0)
    return m


def outline_of(mask, width):
    """The ring of pixels around a mask's edge, `width` px thick."""
    grown = mask.filter(ImageFilter.MaxFilter(width * 2 + 1))
    return ImageChops.subtract(grown, mask)


def inner_edge(mask, width):
    shrunk = mask.filter(ImageFilter.MinFilter(width * 2 + 1))
    return ImageChops.subtract(mask, shrunk)


def brush_stroke(size, points, width, rgb, alpha=255, seed=0, taper=True, bristles=7):
    """A painted stroke along a polyline: several bristle lines with jitter, tapered at both ends."""
    rng = random.Random(seed)
    layer = Image.new("L", size, 0)
    d = ImageDraw.Draw(layer)
    n = len(points)
    for b in range(bristles):
        off = (b / max(1, bristles - 1) - 0.5) * width * 0.8
        pts = []
        for i, (x, y) in enumerate(points):
            if i < n - 1:
                dx, dy = points[i + 1][0] - x, points[i + 1][1] - y
            else:
                dx, dy = x - points[i - 1][0], y - points[i - 1][1]
            ln = math.hypot(dx, dy) or 1
            nx, ny = -dy / ln, dx / ln
            t = i / max(1, n - 1)
            scale = math.sin(math.pi * t) ** 0.35 if taper else 1.0
            jit = rng.uniform(-width * 0.06, width * 0.06)
            pts.append((x + nx * (off * scale + jit), y + ny * (off * scale + jit)))
        w = max(1, int(width / bristles * rng.uniform(1.1, 1.9)))
        d.line(pts, fill=int(alpha * rng.uniform(0.7, 1.0)), width=w, joint="curve")
    layer = layer.filter(ImageFilter.GaussianBlur(0.8))
    return solid(size, rgb, layer)


def curve(p0, p1, p2, steps=40):
    """Quadratic Bezier points."""
    out = []
    for i in range(steps + 1):
        t = i / steps
        x = (1 - t) ** 2 * p0[0] + 2 * (1 - t) * t * p1[0] + t ** 2 * p2[0]
        y = (1 - t) ** 2 * p0[1] + 2 * (1 - t) * t * p1[1] + t ** 2 * p2[1]
        out.append((x, y))
    return out


def gold_leaf(size, mask, seed=0):
    """Gold fill with a warm sheen and speckle, shaped by mask."""
    w, h = size
    grad = Image.linear_gradient("L").resize((w, h)).rotate(25, expand=False)
    sheen = Image.merge("RGB", [grad.point(lambda v: int(GOLD_DEEP[c] + (GOLD_CORE[c] - GOLD_DEEP[c]) * (0.25 + 0.75 * abs(math.sin(v / 255 * math.pi * 1.5))))) for c in range(3)])
    speck = Image.effect_noise((w, h), 50).point(lambda v: 255 if v > 200 else 0).filter(ImageFilter.GaussianBlur(0.6))
    sheen = Image.composite(Image.new("RGB", (w, h), GOLD_CORE), sheen, speck.point(lambda v: v // 3))
    out = sheen.convert("RGBA")
    out.putalpha(mask)
    return out


def framed_panel(size, box, fill_rgb=INK, fill_alpha=230, border=GOLD, border_w=5, seed=0, double=True):
    """A dark brush-cut panel with a gold border (and a thin inner rule)."""
    img = canvas(*size)
    m = rough_rect_mask(size, box, wobble=2.5, seed=seed)
    img = Image.alpha_composite(img, glow(m, (0, 0, 0), 14, 0.6))
    body = solid(size, fill_rgb, m.point(lambda v: v * fill_alpha // 255))
    img = Image.alpha_composite(img, body)
    ring = inner_edge(m, border_w)
    img = Image.alpha_composite(img, gold_leaf(size, ring, seed))
    if double:
        x0, y0, x1, y1 = box
        inset = border_w * 3
        m2 = rough_rect_mask(size, (x0 + inset, y0 + inset, x1 - inset, y1 - inset), wobble=1.2, seed=seed + 1)
        img = Image.alpha_composite(img, solid(size, GOLD, inner_edge(m2, 1).point(lambda v: v * 3 // 5)))
    return img


def corner_flourish(img, cx, cy, scale, angle_deg, seed=0):
    """A small gold leaf-and-curl ornament at a panel corner."""
    size = img.size
    pts = [(cx + math.cos(math.radians(angle_deg + a)) * r * scale, cy + math.sin(math.radians(angle_deg + a)) * r * scale)
           for a, r in ((0, 0), (14, 34), (28, 62), (44, 84), (64, 94), (90, 88))]
    img = Image.alpha_composite(img, brush_stroke(size, pts, 11 * scale, GOLD_DEEP, seed=seed, bristles=5))
    img = Image.alpha_composite(img, brush_stroke(size, pts, 7 * scale, GOLD, seed=seed + 1, bristles=4))
    leaf = Image.new("L", size, 0)
    lx, ly = pts[3]
    ImageDraw.Draw(leaf).ellipse((lx - 9 * scale, ly - 5 * scale, lx + 9 * scale, ly + 5 * scale), fill=255)
    return Image.alpha_composite(img, gold_leaf(size, leaf, seed))


def pip(size, center, r, state, rgb_on=GOLD, seed=0):
    """A round pip: state 0 empty socket, 1 lit, 2 blazing."""
    img = canvas(*size)
    cx, cy = center
    m = Image.new("L", size, 0)
    ImageDraw.Draw(m).ellipse((cx - r, cy - r, cx + r, cy + r), fill=255)
    if state >= 2:
        img = Image.alpha_composite(img, glow(m, rgb_on, r * 0.9, 1.6))
    img = Image.alpha_composite(img, solid(size, INK, m))
    inner = Image.new("L", size, 0)
    ri = r * 0.72
    ImageDraw.Draw(inner).ellipse((cx - ri, cy - ri, cx + ri, cy + ri), fill=255)
    if state == 0:
        img = Image.alpha_composite(img, solid(size, INK_SOFT, inner))
    else:
        core = GOLD_CORE if state == 2 else rgb_on
        img = Image.alpha_composite(img, solid(size, rgb_on, inner))
        hi = Image.new("L", size, 0)
        ImageDraw.Draw(hi).ellipse((cx - ri * 0.55, cy - ri * 0.65, cx + ri * 0.1, cy - ri * 0.05), fill=200)
        img = Image.alpha_composite(img, solid(size, core, hi.filter(ImageFilter.GaussianBlur(r * 0.08))))
    img = Image.alpha_composite(img, solid(size, GOLD_DEEP, outline_of(m, 2)))
    return img


def petal_tile(size_px, rgb, rng):
    """One teardrop petal (RGBA tile), randomly rotated."""
    w, h = int(size_px * 0.62) + 2, int(size_px) + 2
    tile = Image.new("RGBA", (w * 2, h * 2), (0, 0, 0, 0))
    d = ImageDraw.Draw(tile)
    pts = []
    for i in range(40):
        a = i / 40 * 2 * math.pi
        r = 1 - 0.35 * (1 + math.cos(a)) / 2
        pts.append((w + math.sin(a) * w * 0.5 * r, h + math.cos(a) * h * 0.5))
    d.polygon(pts, fill=rgba(rgb))
    vein = tuple(max(0, c - 40) for c in rgb)
    d.line([(w, h - h * 0.4), (w, h + h * 0.35)], fill=rgba(vein, 140), width=max(1, int(size_px / 18)))
    return tile.rotate(rng.uniform(0, 360), resample=Image.BICUBIC, expand=True)


def text_center(img, xy, text, fnt, fill=INK, stroke=0, stroke_fill=None):
    d = ImageDraw.Draw(img)
    bbox = d.textbbox((0, 0), text, font=fnt, stroke_width=stroke)
    x = xy[0] - (bbox[2] - bbox[0]) / 2 - bbox[0]
    y = xy[1] - (bbox[3] - bbox[1]) / 2 - bbox[1]
    d.text((x, y), text, font=fnt, fill=fill, stroke_width=stroke, stroke_fill=stroke_fill)
    return img


def contact_sheet(paths, out, bg=(60, 56, 64), cell=320, cols=4, label_font=None):
    """Lay finished pieces out on a dark checker for review."""
    rows = (len(paths) + cols - 1) // cols
    sheet = Image.new("RGB", (cols * cell, rows * (cell + 24)), bg)
    d = ImageDraw.Draw(sheet)
    for i, p in enumerate(paths):
        im = Image.open(p).convert("RGBA")
        im.thumbnail((cell - 16, cell - 16))
        x, y = (i % cols) * cell, (i // cols) * (cell + 24)
        checker = Image.new("RGB", (cell, cell), (90, 86, 94))
        cd = ImageDraw.Draw(checker)
        for cx in range(0, cell, 16):
            for cy in range(0, cell, 16):
                if (cx // 16 + cy // 16) % 2:
                    cd.rectangle((cx, cy, cx + 15, cy + 15), fill=(72, 68, 78))
        checker.paste(im, ((cell - im.width) // 2, (cell - im.height) // 2), im)
        sheet.paste(checker, (x, y))
        label = "/".join(str(p).replace("\\", "/").split("/")[-2:])
        d.text((x + 6, y + cell + 4), label, fill=(230, 226, 220), font=label_font)
    sheet.save(out)

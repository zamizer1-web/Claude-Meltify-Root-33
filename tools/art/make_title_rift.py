#!/usr/bin/env python3
"""Make asset.title-rift: an original "33" tearing out of a glowing rift, with petals drifting off.

Composited onto Root's own logo (read from the player's install) at runtime, so nothing
of Root's or Clair Obscur's art is copied. Uses the Cinzel font (SIL Open Font License 1.1);
rendering text into an image is allowed by the OFL.

Usage: python3 -I tools/art/make_title_rift.py --font path/to/cinzel-latin-900-normal.woff2 [--out art/title-rift.png]
Deterministic: the same seed gives the same image.
"""
import argparse
import math
import random
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageFont

W, H = 1600, 900
GOLD_CORE = (255, 246, 222)
GOLD = (240, 196, 110)
CRIMSON = (178, 22, 46)
INK = (24, 20, 26)
BONE = (242, 236, 224)


def lerp(a, b, t):
    return a + (b - a) * t


def rift_polygon(rng, x0, y0, x1, y1, max_half_width, steps=48):
    """A jagged tear along the line (x0,y0)->(x1,y1), widest in the middle."""
    dx, dy = x1 - x0, y1 - y0
    length = math.hypot(dx, dy)
    nx, ny = -dy / length, dx / length
    left, right, spine = [], [], []
    drift = 0.0
    for i in range(steps + 1):
        t = i / steps
        drift += rng.uniform(-9, 9)
        drift *= 0.85
        cx, cy = x0 + dx * t + nx * drift, y0 + dy * t + ny * drift
        spine.append((cx, cy))
        width = max_half_width * (math.sin(math.pi * t) ** 0.8) * rng.uniform(0.55, 1.15)
        jag_l = width * rng.uniform(0.6, 1.0)
        jag_r = width * rng.uniform(0.6, 1.0)
        left.append((cx + nx * jag_l, cy + ny * jag_l))
        right.append((cx - nx * jag_r, cy - ny * jag_r))
    return left + right[::-1], spine


def crack_branches(rng, spine, count, reach):
    """Fine jagged cracks leaving the tear; each is a list of (points, alpha) segments that fade out."""
    lines = []
    for _ in range(count):
        sx, sy = spine[rng.randrange(6, len(spine) - 6)]
        ang = rng.choice([0, math.pi]) + rng.uniform(-0.7, 0.7)
        steps = rng.randint(6, 12)
        pts = [(sx, sy)]
        for _ in range(steps):
            ang += rng.uniform(-0.5, 0.5)
            step = rng.uniform(reach * 0.04, reach * 0.11)
            sx, sy = sx + math.cos(ang) * step, sy + math.sin(ang) * step
            pts.append((sx, sy))
        lines.append(pts)
    return lines


def petal(size, color, rng):
    """One teardrop petal on its own small transparent tile."""
    w, h = int(size * 0.62) + 2, int(size) + 2
    tile = Image.new("RGBA", (w * 2, h * 2), (0, 0, 0, 0))
    d = ImageDraw.Draw(tile)
    pts = []
    for i in range(40):
        a = i / 40 * 2 * math.pi
        r = 1 - 0.35 * (1 + math.cos(a)) / 2  # pointed at one end
        pts.append((w + math.sin(a) * w * 0.5 * r, h + math.cos(a) * h * 0.5))
    d.polygon(pts, fill=color + (255,))
    vein = tuple(max(0, c - 40) for c in color)
    d.line([(w, h - h * 0.4), (w, h + h * 0.35)], fill=vein + (140,), width=max(1, int(size / 18)))
    tile = tile.rotate(rng.uniform(0, 360), resample=Image.BICUBIC, expand=True)
    return tile


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--font", required=True)
    ap.add_argument("--out", default="art/title-rift.png")
    ap.add_argument("--preview", default="")
    ap.add_argument("--seed", type=int, default=33)
    args = ap.parse_args()
    rng = random.Random(args.seed)

    canvas = Image.new("RGBA", (W, H), (0, 0, 0, 0))

    # 1. The rift: a jagged diagonal tear with light pouring out.
    poly, spine = rift_polygon(rng, W * 0.60, -40, W * 0.40, H + 40, 120)
    rift = Image.new("L", (W, H), 0)
    ImageDraw.Draw(rift).polygon(poly, fill=255)
    halo = Image.new("RGBA", (W, H), CRIMSON + (0,))
    halo.putalpha(rift.filter(ImageFilter.GaussianBlur(70)).point(lambda v: int(v * 0.85)))
    glow = Image.new("RGBA", (W, H), GOLD + (0,))
    glow.putalpha(rift.filter(ImageFilter.GaussianBlur(26)))
    core = Image.new("RGBA", (W, H), GOLD_CORE + (0,))
    core.putalpha(rift.filter(ImageFilter.GaussianBlur(4)))
    for layer in (halo, glow, core):
        canvas = Image.alpha_composite(canvas, layer)

    # Fine cracks branching off the tear, fading as they go.
    cracks = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    cd = ImageDraw.Draw(cracks)
    for pts in crack_branches(rng, spine, 22, 300):
        n = len(pts) - 1
        for i in range(n):
            a = int(230 * (1 - i / n) ** 1.4)
            cd.line([pts[i], pts[i + 1]], fill=GOLD_CORE + (a,), width=2 if i < n / 3 else 1)
    canvas = Image.alpha_composite(canvas, cracks.filter(ImageFilter.GaussianBlur(2.5)))
    canvas = Image.alpha_composite(canvas, cracks)

    # Light rays pouring out of the tear.
    rays = Image.new("L", (W, H), 0)
    rd = ImageDraw.Draw(rays)
    cx, cy = spine[len(spine) // 2]
    for _ in range(28):
        a = rng.uniform(0, 2 * math.pi)
        spread = rng.uniform(0.015, 0.05)
        far = 900
        rd.polygon([(cx, cy), (cx + math.cos(a - spread) * far, cy + math.sin(a - spread) * far),
                    (cx + math.cos(a + spread) * far, cy + math.sin(a + spread) * far)], fill=int(rng.uniform(18, 46)))
    rays = ImageChops.multiply(rays.filter(ImageFilter.GaussianBlur(14)),
                               Image.radial_gradient("L").point(lambda v: max(0, 255 - int(v * 1.6))).resize((W * 2, H * 2)).crop((int(W - cx), int(H - cy), int(2 * W - cx), int(2 * H - cy))))
    ray_layer = Image.new("RGBA", (W, H), GOLD + (0,))
    ray_layer.putalpha(rays)
    canvas = Image.alpha_composite(canvas, ray_layer)

    # Petals behind the numerals (drawn now; a crisper front layer comes last).
    def petal_layer(count, front):
        layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        for _ in range(count):
            t = rng.random()
            sx, sy = spine[int(t * (len(spine) - 1))]
            ang = rng.uniform(-0.9, 0.5)
            dist = rng.uniform(40, 720) * (1 if rng.random() < 0.78 else -0.7)
            px, py = sx + math.cos(ang) * dist, sy + math.sin(ang) * dist * 0.6
            size = rng.uniform(12, 38) * (1.0 if abs(dist) < 350 else 0.75) * (1.15 if front else 0.85)
            color = CRIMSON if rng.random() < 0.82 else BONE
            pt = petal(size, color, rng)
            alpha = 1.0 if front else rng.uniform(0.45, 0.85) * (1 - min(abs(dist) / 900, 0.6))
            pt.putalpha(pt.getchannel("A").point(lambda v, a=alpha: int(v * a)))
            if not front and rng.random() < 0.4:
                pt = pt.filter(ImageFilter.GaussianBlur(2.2))
            if 0 <= px < W and 0 <= py < H:
                layer.alpha_composite(pt, (int(px - pt.width / 2), int(py - pt.height / 2)))
        return layer

    canvas = Image.alpha_composite(canvas, petal_layer(70, front=False))

    # 2. The numerals: bone white with an ink outline, lit gold where they cross the rift.
    font = ImageFont.truetype(args.font, 560)
    text = "33"
    bbox = font.getbbox(text, stroke_width=14)
    tx = (W - (bbox[2] - bbox[0])) // 2 - bbox[0]
    ty = (H - (bbox[3] - bbox[1])) // 2 - bbox[1] + 10
    shape = Image.new("L", (W, H), 0)
    ImageDraw.Draw(shape).text((tx, ty), text, font=font, fill=255)
    outline = Image.new("L", (W, H), 0)
    ImageDraw.Draw(outline).text((tx, ty), text, font=font, fill=255, stroke_width=14, stroke_fill=255)
    # The tear runs through the numerals: cut a narrow jagged gap along the spine.
    gap = Image.new("L", (W, H), 0)
    gd = ImageDraw.Draw(gap)
    for i in range(len(spine) - 1):
        gd.line([spine[i], spine[i + 1]], fill=255, width=int(rng.uniform(14, 26)))
    gap = gap.filter(ImageFilter.MaxFilter(3))
    shape = ImageChops.subtract(shape, gap)
    outline = ImageChops.subtract(outline, gap.filter(ImageFilter.MinFilter(5)))

    shadow = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    shadow.putalpha(outline.filter(ImageFilter.GaussianBlur(18)).point(lambda v: int(v * 0.55)))
    canvas = Image.alpha_composite(canvas, shadow)
    ink = Image.new("RGBA", (W, H), INK + (0,))
    ink.putalpha(outline)
    canvas = Image.alpha_composite(canvas, ink)

    # Painted fill: bone with streaks, warmer where the light from the rift hits it.
    noise = Image.effect_noise((W, H), 38).filter(ImageFilter.GaussianBlur(1.5))
    streaks = Image.effect_noise((W // 8, H), 60).resize((W, H), Image.BICUBIC).filter(ImageFilter.GaussianBlur(3))
    light = rift.filter(ImageFilter.GaussianBlur(120))
    fill = Image.new("RGBA", (W, H), BONE + (255,))
    warm = Image.new("RGBA", (W, H), GOLD_CORE + (255,))
    fill = Image.composite(warm, fill, light)
    tex = ImageChops.multiply(fill.convert("RGB"), Image.merge("RGB", [ImageChops.add(noise, streaks, 2.0, 140)] * 3)).convert("RGBA")
    tex = Image.blend(fill, tex, 0.35)
    tex.putalpha(shape)
    canvas = Image.alpha_composite(canvas, tex)

    # Where the numerals cross the tear, their edges burn gold.
    edge = ImageChops.subtract(outline, shape.filter(ImageFilter.MinFilter(9)))
    burn = ImageChops.multiply(edge, rift.filter(ImageFilter.GaussianBlur(40)).point(lambda v: min(255, v * 3)))
    burn_layer = Image.new("RGBA", (W, H), GOLD + (0,))
    burn_layer.putalpha(burn.filter(ImageFilter.GaussianBlur(2)))
    canvas = Image.alpha_composite(canvas, burn_layer)

    # Light blazing through the gap in the numerals.
    seam = ImageChops.multiply(gap, outline.filter(ImageFilter.MaxFilter(15)))
    seam_glow = Image.new("RGBA", (W, H), GOLD + (0,))
    seam_glow.putalpha(seam.filter(ImageFilter.GaussianBlur(10)))
    seam_core = Image.new("RGBA", (W, H), GOLD_CORE + (0,))
    seam_core.putalpha(seam.filter(ImageFilter.GaussianBlur(2)))
    canvas = Image.alpha_composite(canvas, seam_glow)
    canvas = Image.alpha_composite(canvas, seam_core)

    # Shards breaking off the numerals toward the right, as if torn through.
    shards = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    trails = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    sd, td = ImageDraw.Draw(shards), ImageDraw.Draw(trails)
    for _ in range(18):
        cx = rng.uniform(W * 0.62, W * 0.86)
        cy = rng.uniform(H * 0.25, H * 0.78)
        r = rng.uniform(5, 16) * (1.4 if cx < W * 0.7 else 1.0)
        angs = sorted(rng.uniform(0, 2 * math.pi) for _ in range(rng.randint(4, 6)))
        pts = [(cx + math.cos(a) * r * rng.uniform(0.45, 1.25), cy + math.sin(a) * r * rng.uniform(0.45, 1.25)) for a in angs]
        sd.polygon(pts, fill=BONE + (255,))
        sd.line(pts + [pts[0]], fill=INK + (200,), width=2)
        sd.line([pts[0], pts[1]], fill=GOLD + (220,), width=2)  # lit edge facing the rift
        td.line([(cx - r * 4.5, cy + r * 0.6), (cx - r, cy)], fill=GOLD + (90,), width=max(1, int(r / 3)))
    canvas = Image.alpha_composite(canvas, trails.filter(ImageFilter.GaussianBlur(4)))
    canvas = Image.alpha_composite(canvas, shards)

    # 3. A few crisp petals in front, kept off the numerals so they never read as stains.
    front = petal_layer(26, front=True)
    keep_off = outline.filter(ImageFilter.MaxFilter(21))
    front.putalpha(ImageChops.subtract(front.getchannel("A"), keep_off))
    canvas = Image.alpha_composite(canvas, front)

    out = Path(args.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    canvas.save(out, optimize=True)
    print(f"wrote {out} ({W}x{H})")

    if args.preview:
        bg = Image.new("RGBA", (W, H), (14, 12, 18, 255))
        Image.alpha_composite(bg, canvas).convert("RGB").save(args.preview, quality=92)
        print(f"wrote preview {args.preview}")


if __name__ == "__main__":
    main()

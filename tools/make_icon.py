"""Draws TouchScreenEdgePanel's app icon (app.ico, 16-256 px) — an ultra-wide touch panel on a stand,
showing the three widgets: Discord (left), live scores (middle), app grid (right).

    python -I tools/make_icon.py app.ico preview.png
"""
import sys
from PIL import Image, ImageDraw, ImageFilter

GREEN = (34, 197, 94)
PURPLE = (139, 147, 255)
S = 1024  # master canvas


def lerp(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(len(a)))


def background(size):
    """Rounded dark tile with the same green / purple ambient glow as the panel."""
    bg = Image.new("RGBA", (size, size))
    d = ImageDraw.Draw(bg)
    for y in range(size):
        d.line([(0, y), (size, y)], fill=lerp((30, 33, 40, 255), (10, 11, 14, 255), y / size))
    glow = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    g = ImageDraw.Draw(glow)
    g.ellipse([-0.25 * size, 0.55 * size, 0.6 * size, 1.35 * size], fill=GREEN + (95,))
    g.ellipse([0.45 * size, -0.4 * size, 1.3 * size, 0.4 * size], fill=PURPLE + (80,))
    glow = glow.filter(ImageFilter.GaussianBlur(size * 0.12))
    bg = Image.alpha_composite(bg, glow)

    mask = Image.new("L", (size, size), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, size - 1, size - 1], radius=int(size * 0.22), fill=255)
    out = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    out.paste(bg, (0, 0), mask)
    ImageDraw.Draw(out).rounded_rectangle([2, 2, size - 3, size - 3], radius=int(size * 0.22),
                                          outline=(255, 255, 255, 34), width=max(1, size // 128))
    return out


def detailed():
    im = background(S)
    d = ImageDraw.Draw(im)

    # ultra-wide screen (≈ 2560×720 → 3.5 : 1, a bit taller so it reads at small sizes)
    x0, y0, x1, y1 = 64, 318, 960, 622

    # stand
    d.polygon([(392, y1 - 10), (632, y1 - 10), (700, 760), (324, 760)], fill=(34, 36, 42))
    d.rounded_rectangle([300, 744, 724, 780], radius=16, fill=(48, 50, 57))
    d.rounded_rectangle([x0 - 4, y0 + 10, x1 + 4, y1 + 16], radius=58, fill=(0, 0, 0, 120))  # shadow
    d.rounded_rectangle([x0, y0, x1, y1], radius=54, fill=(12, 13, 16), outline=(58, 61, 68), width=12)

    pad = 30
    top, bot = y0 + pad, y1 - pad
    panel = (24, 26, 31)
    edge = (48, 51, 58)

    # left widget: voice (mic button + speaking ring)
    gapw = 22
    pw = (x1 - x0 - 2 * pad - 2 * gapw) // 3          # three equal widgets
    lx0, lx1 = x0 + pad, x0 + pad + pw
    d.rounded_rectangle([lx0, top, lx1, bot], radius=26, fill=panel, outline=edge, width=4)
    cx, cy, r = (lx0 + lx1) // 2, (top + bot) // 2, 70
    d.ellipse([cx - r - 16, cy - r - 16, cx + r + 16, cy + r + 16], outline=GREEN, width=12)
    d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=PURPLE)
    d.rounded_rectangle([cx - 15, cy - 38, cx + 15, cy + 12], radius=15, fill=(255, 255, 255))   # mic
    d.arc([cx - 30, cy - 22, cx + 30, cy + 30], 20, 160, fill=(255, 255, 255), width=8)
    d.line([cx, cy + 30, cx, cy + 42], fill=(255, 255, 255), width=8)

    # middle widget: live scores (rows, one live in green)
    mx0, mx1 = lx1 + gapw, lx1 + gapw + pw
    d.rounded_rectangle([mx0, top, mx1, bot], radius=26, fill=panel, outline=edge, width=4)
    rows = 3
    rh = (bot - top - 40) / rows
    for i in range(rows):
        ry = top + 20 + i * rh + rh / 2
        live = i == 0
        col = GREEN if live else (150, 155, 165)
        d.ellipse([mx0 + 24, ry - 10, mx0 + 44, ry + 10], fill=GREEN if live else (90, 94, 102))
        mid = (mx0 + mx1) / 2
        d.rounded_rectangle([mx0 + 58, ry - 10, mid - 26, ry + 10], radius=10, fill=col)
        d.rounded_rectangle([mid - 15, ry - 15, mid + 15, ry + 15], radius=7, fill=(240, 242, 245))
        d.rounded_rectangle([mid + 26, ry - 10, mx1 - 24, ry + 10], radius=10, fill=col)

    # right widget: app tiles 3×2
    rx0, rx1 = mx1 + gapw, x1 - pad
    d.rounded_rectangle([rx0, top, rx1, bot], radius=26, fill=panel, outline=edge, width=4)
    colors = [GREEN, (240, 242, 245), PURPLE, (251, 146, 60), (56, 189, 248), GREEN]
    cols, rws, gap = 3, 2, 14
    tw = (rx1 - rx0 - 36 - gap * (cols - 1)) / cols
    th = (bot - top - 36 - gap * (rws - 1)) / rws
    t = min(tw, th)
    gx = rx0 + (rx1 - rx0 - (t * cols + gap * (cols - 1))) / 2
    gy = top + (bot - top - (t * rws + gap * (rws - 1))) / 2
    for i, c in enumerate(colors):
        cxx = gx + (i % cols) * (t + gap)
        cyy = gy + (i // cols) * (t + gap)
        d.rounded_rectangle([cxx, cyy, cxx + t, cyy + t], radius=int(t * 0.28), fill=c)
    return im


def simple(size):
    """16-32 px: just the wide screen with three coloured blocks."""
    k = 8
    big = background(size * k)
    d = ImageDraw.Draw(big)
    n = size * k
    x0, y0, x1, y1 = n * 0.08, n * 0.30, n * 0.92, n * 0.66
    d.rounded_rectangle([x0, y0, x1, y1], radius=n * 0.07, fill=(12, 13, 16), outline=(70, 74, 82), width=int(n * 0.035))
    p = n * 0.07
    w = (x1 - x0 - 2 * p - 2 * n * 0.035) / 3
    for i, c in enumerate([PURPLE, (230, 232, 236), GREEN]):
        bx = x0 + p + i * (w + n * 0.035)
        d.rounded_rectangle([bx, y0 + p, bx + w, y1 - p], radius=n * 0.03, fill=c)
    d.polygon([(n * 0.40, y1), (n * 0.60, y1), (n * 0.66, n * 0.80), (n * 0.34, n * 0.80)], fill=(60, 63, 70))
    return big.resize((size, size), Image.LANCZOS)


def main(ico_path, preview_path):
    master = detailed()
    frames = {}
    for sz in (256, 128, 64):
        frames[sz] = master.resize((sz, sz), Image.LANCZOS)
    for sz in (48, 40, 32, 24, 20, 16):
        frames[sz] = simple(sz)
    order = [256, 128, 64, 48, 40, 32, 24, 20, 16]
    frames[256].save(ico_path, format="ICO", sizes=[(s, s) for s in order],
                     append_images=[frames[s] for s in order[1:]])

    # preview strip on light and dark backgrounds
    strip = Image.new("RGBA", (40 + sum(order) + 20 * len(order), 2 * 300), (0, 0, 0, 0))
    for row, bgc in enumerate([(32, 33, 36, 255), (236, 237, 240, 255)]):
        ImageDraw.Draw(strip).rectangle([0, row * 300, strip.width, row * 300 + 299], fill=bgc)
        x = 20
        for s in order:
            strip.alpha_composite(frames[s], (x, row * 300 + (300 - s) // 2))
            x += s + 20
    strip.save(preview_path)
    print("ok", ico_path)


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])

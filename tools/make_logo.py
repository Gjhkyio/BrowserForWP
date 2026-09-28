#!/usr/bin/env python3
"""BrowserForWP logo / asset generator.

Renders the project logo and writes every image asset Windows Phone 8.1
expects, at both scale-100 and scale-240.

Design
------
A rounded "tile" carrying an indigo -> cyan gradient, a white wireframe globe,
and a green accent dot that reads as the "secure transport" marker. The mark is
deliberately geometric so it stays legible down to 44x44 (the task-switcher
size) where detail is lost.

Why pure stdlib
---------------
No Pillow, no cairo, no ImageMagick dependency: this must run on any machine
that has Python 3, including the Windows box where the app is actually built.
PNGs are encoded by hand with ``zlib`` + ``struct``.

Rendering
---------
The mark is a *continuous* function of normalised coordinates, so it is
supersampled 3x3 per output pixel and box-filtered. That yields genuinely
anti-aliased edges at every size without any drawing library.

Usage
-----
    python3 tools/make_logo.py            # write into BrowserForWP/Assets
    python3 tools/make_logo.py --out DIR  # write somewhere else
"""

from __future__ import annotations

import argparse
import math
import os
import struct
import zlib

# ── Palette ────────────────────────────────────────────────────────────────
GRAD_TOP = (0.106, 0.165, 0.420)  # #1B2A6B  indigo
GRAD_BOT = (0.000, 0.639, 0.769)  # #00A3C4  cyan
ACCENT = (0.184, 0.816, 0.478)  # #2FD07A  secure-transport green
INK = (1.000, 1.000, 1.000)  # globe strokes

# ── Mark geometry, in normalised units where the tile spans [-1, 1] ────────
CORNER_RADIUS = 0.235
GLOBE_RADIUS = 0.575
GLOBE_STROKE = 0.075
LATITUDE_DEG = 25.0  # where the two latitude arcs sit
MERIDIAN_FLATTEN = 0.30  # horizontal squash of the vertical meridian
ARC_TOLERANCE = 0.16  # ring thickness for the elliptical arcs
ACCENT_CENTER = (0.640, 0.640)
ACCENT_RADIUS = 0.185

SUPERSAMPLE = 3
MASTER_SIZE = 720  # rendered once, then downsampled for every asset

# ── Asset table: (file stem, width, height) ────────────────────────────────
# Windows Phone 8.1 WinRT asset contract. "scale-240" variants are what the
# .vbproj currently references; the unsuffixed ones are what the app manifest
# names, so both are produced.
ASSETS = [
    ("StoreLogo", 50, 50),
    ("StoreLogo.scale-240", 120, 120),
    ("Square71x71Logo", 71, 71),
    ("Square71x71Logo.scale-240", 170, 170),
    ("SmallLogo", 44, 44),
    ("SmallLogo.scale-240", 106, 106),
    ("Logo", 150, 150),
    ("Logo.scale-240", 360, 360),
]

# Composite assets: transparent canvas + centred mark at a given ratio.
COMPOSITES = [
    ("WideLogo", 310, 150, 0.72),
    ("WideLogo.scale-240", 744, 360, 0.72),
    ("SplashScreen", 480, 800, 0.58),
    ("SplashScreen.scale-240", 1152, 1920, 0.58),
]


# ── Small vector helpers ───────────────────────────────────────────────────
def _lerp(a: float, b: float, t: float) -> float:
    return a + (b - a) * t


def _lerp3(a, b, t):
    return (_lerp(a[0], b[0], t), _lerp(a[1], b[1], t), _lerp(a[2], b[2], t))


def _clamp01(x: float) -> float:
    return 0.0 if x < 0.0 else (1.0 if x > 1.0 else x)


# ── Signed-distance / coverage tests ───────────────────────────────────────
def inside_rounded_square(u: float, v: float, cr: float) -> bool:
    """True if (u, v) is inside a square spanning [-1, 1] with corner radius cr."""
    ax, ay = abs(u), abs(v)
    if ax > 1.0 or ay > 1.0:
        return False
    # Inside the straight edges?
    if ax <= 1.0 - cr or ay <= 1.0 - cr:
        return True
    # Otherwise we are in a corner: measure from the corner arc centre.
    dx, dy = ax - (1.0 - cr), ay - (1.0 - cr)
    return dx * dx + dy * dy <= cr * cr


def _ellipse_ring(u: float, v: float, rx: float, ry: float, cy: float, tol: float) -> bool:
    """True if (u, v) lands on the outline of an axis-aligned ellipse."""
    if rx <= 0.0 or ry <= 0.0:
        return False
    e = (u / rx) ** 2 + ((v - cy) / ry) ** 2
    return abs(e - 1.0) <= tol


def sample(u: float, v: float):
    """Return the RGBA colour of the mark at normalised (u, v). v grows downward."""
    if not inside_rounded_square(u, v, CORNER_RADIUS):
        return (0.0, 0.0, 0.0, 0.0)

    # Background: eased vertical gradient with a soft top-left highlight.
    t = _clamp01((v + 1.0) / 2.0)
    t = t * t * 0.55 + t * 0.45
    r, g, b = _lerp3(GRAD_TOP, GRAD_BOT, t)
    halo = max(0.0, 1.0 - math.hypot(u + 0.42, v + 0.45) / 1.35) * 0.20
    r, g, b = _clamp01(r + halo), _clamp01(g + halo), _clamp01(b + halo)

    dist = math.hypot(u, v)

    # Accent dot (secure transport). Drawn beneath the globe so the globe
    # outline wins where the two overlap.
    if math.hypot(u - ACCENT_CENTER[0], v - ACCENT_CENTER[1]) <= ACCENT_RADIUS:
        r, g, b = ACCENT

    # Globe: outer ring.
    if abs(dist - GLOBE_RADIUS) <= GLOBE_STROKE / 2.0:
        r, g, b = INK
    else:
        # Two latitude arcs.
        lat = math.radians(LATITUDE_DEG)
        cy = GLOBE_RADIUS * math.sin(lat)
        rx = GLOBE_RADIUS * math.cos(lat)
        ry = rx * MERIDIAN_FLATTEN
        if _ellipse_ring(u, v, rx, ry, cy, ARC_TOLERANCE) or _ellipse_ring(
            u, v, rx, ry, -cy, ARC_TOLERANCE
        ):
            if dist <= GLOBE_RADIUS + 0.01:
                r, g, b = INK
        # Single vertical meridian.
        elif _ellipse_ring(u, v, GLOBE_RADIUS * MERIDIAN_FLATTEN, GLOBE_RADIUS, 0.0, ARC_TOLERANCE):
            r, g, b = INK

    return (r, g, b, 1.0)


def render_master(size: int = MASTER_SIZE, ss: int = SUPERSAMPLE):
    """Supersampled render of the mark -> flat list of floats [r,g,b,a, ...]."""
    buf = [0.0] * (size * size * 4)
    inv = 2.0 / (size * ss)
    n = ss * ss
    for py in range(size):
        row = py * size * 4
        for px in range(size):
            ar = ag = ab = aa = 0.0
            for sy in range(ss):
                v = -1.0 + (py * ss + sy + 0.5) * inv
                for sx in range(ss):
                    u = -1.0 + (px * ss + sx + 0.5) * inv
                    cr, cg, cb, ca = sample(u, v)
                    ar += cr * ca
                    ag += cg * ca
                    ab += cb * ca
                    aa += ca
            idx = row + px * 4
            buf[idx] = ar / n
            buf[idx + 1] = ag / n
            buf[idx + 2] = ab / n
            buf[idx + 3] = aa / n
    return buf


def resize(src, sw: int, sh: int, dw: int, dh: int):
    """Area-average resample of a straight-alpha RGBA float buffer."""
    if sw == dw and sh == dh:
        return list(src)
    out = [0.0] * (dw * dh * 4)
    xr = sw / dw
    yr = sh / dh
    for dy in range(dh):
        y0 = dy * yr
        y1 = y0 + yr
        iy0, iy1 = int(y0), min(sh, int(math.ceil(y1)))
        for dx in range(dw):
            x0 = dx * xr
            x1 = x0 + xr
            ix0, ix1 = int(x0), min(sw, int(math.ceil(x1)))
            ar = ag = ab = aa = wsum = 0.0
            for sy in range(iy0, iy1):
                wy = min(sy + 1, y1) - max(sy, y0)
                if wy <= 0:
                    continue
                for sx in range(ix0, ix1):
                    wx = min(sx + 1, x1) - max(sx, x0)
                    if wx <= 0:
                        continue
                    w = wy * wx
                    si = (sy * sw + sx) * 4
                    ar += src[si] * w
                    ag += src[si + 1] * w
                    ab += src[si + 2] * w
                    aa += src[si + 3] * w
                    wsum += w
            oi = (dy * dw + dx) * 4
            if wsum > 0:
                out[oi] = ar / wsum
                out[oi + 1] = ag / wsum
                out[oi + 2] = ab / wsum
                out[oi + 3] = aa / wsum
    return out


def compose(mark, mark_size: int, width: int, height: int, ratio: float):
    """Centre the mark on a transparent canvas, sized to `ratio` of the short edge."""
    target = max(1, int(min(width, height) * ratio))
    scaled = resize(mark, mark_size, mark_size, target, target)
    out = [0.0] * (width * height * 4)
    ox = (width - target) // 2
    oy = (height - target) // 2
    for y in range(target):
        dy = oy + y
        if dy < 0 or dy >= height:
            continue
        srow = y * target * 4
        drow = dy * width * 4
        for x in range(target):
            dx = ox + x
            if dx < 0 or dx >= width:
                continue
            sa = scaled[srow + x * 4 + 3]
            si = srow + x * 4
            di = drow + dx * 4
            if sa >= 1.0:
                out[di] = scaled[si]
                out[di + 1] = scaled[si + 1]
                out[di + 2] = scaled[si + 2]
                out[di + 3] = sa
            elif sa > 0.0:
                da = out[di + 3]
                oa = sa + da * (1.0 - sa)
                out[di] = (scaled[si] * sa) / oa
                out[di + 1] = (scaled[si + 1] * sa) / oa
                out[di + 2] = (scaled[si + 2] * sa) / oa
                out[di + 3] = oa
    return out


# ── PNG encoding ───────────────────────────────────────────────────────────
def _chunk(tag: bytes, data: bytes) -> bytes:
    return (
        struct.pack(">I", len(data))
        + tag
        + data
        + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)
    )


def write_png(path: str, width: int, height: int, buf) -> None:
    """Write an 8-bit RGBA PNG from a straight-alpha float buffer."""
    raw = bytearray()
    for y in range(height):
        raw.append(0)  # filter type: None
        base = y * width * 4
        for x in range(width):
            i = base + x * 4
            a = _clamp01(buf[i + 3])
            if a <= 0.0:
                raw += b"\x00\x00\x00\x00"
                continue
            # Undo the average-of-premultiplied accumulation by un-premultiplying.
            r = _clamp01(buf[i] / a) if a > 0 else 0.0
            g = _clamp01(buf[i + 1] / a) if a > 0 else 0.0
            b = _clamp01(buf[i + 2] / a) if a > 0 else 0.0
            raw += bytes(
                (
                    int(round(r * 255)),
                    int(round(g * 255)),
                    int(round(b * 255)),
                    int(round(a * 255)),
                )
            )
    png = b"\x89PNG\r\n\x1a\n"
    png += _chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0))
    png += _chunk(b"IDAT", zlib.compress(bytes(raw), 9))
    png += _chunk(b"IEND", b"")
    os.makedirs(os.path.dirname(path) or ".", exist_ok=True)
    with open(path, "wb") as fh:
        fh.write(png)


def main() -> None:
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    ap = argparse.ArgumentParser(description="Regenerate BrowserForWP image assets.")
    ap.add_argument(
        "--out",
        default=os.path.join(root, "BrowserForWP", "Assets"),
        help="output directory (default: BrowserForWP/Assets)",
    )
    args = ap.parse_args()

    print(f"rendering master {MASTER_SIZE}x{MASTER_SIZE} at {SUPERSAMPLE}x{SUPERSAMPLE} ...")
    mark = render_master()

    print("writing assets")
    for stem, w, h in ASSETS:
        buf = resize(mark, MASTER_SIZE, MASTER_SIZE, w, h)
        write_png(os.path.join(args.out, stem + ".png"), w, h, buf)
        print(f"  {stem + '.png':<34} {w}x{h}")

    for stem, w, h, ratio in COMPOSITES:
        buf = compose(mark, MASTER_SIZE, w, h, ratio)
        write_png(os.path.join(args.out, stem + ".png"), w, h, buf)
        print(f"  {stem + '.png':<34} {w}x{h}")

    print("done.")


if __name__ == "__main__":
    main()

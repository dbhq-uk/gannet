#!/usr/bin/env python3
"""Generate the gannet mark, wordmark and lockups.

THE MARK is a northern gannet's head in profile, white, cut out of a navy
tile: the crown, the long dagger bill, the gape, and the throat running out
of the bottom edge. The one colour is the eye, in gold. Chosen on 21 Oct 2026
from eight drafted directions (05, "negative space"), put on navy rather than
ink (Dan: "05 on navy blue").

THE WORDMARK is lowercase, upright and geometric: every bowl a true circle,
every stem straight, one stroke weight throughout. Chosen on 21 Oct 2026 from
a second round of drafts (g07, without its gold dot; Dan: "7 without the
yellow dot"), after a monospace wordmark, ten typefaces and a round of
italic scripts were turned down. The draft was a raster; this file rebuilds
it from its measured proportions, so there is no font to install and nothing
to trace again.

Edit this generator and re-run it. Do not hand-edit what it emits.

    python3 assets/_gen/logo.py
"""

import math
import shutil
import subprocess
from pathlib import Path

OUT = Path(__file__).resolve().parent.parent

NAVY = "#102A4C"
WHITE = "#FFFFFF"
GOLD = "#F2B632"
PAPER = "#F7F5F0"
INK = "#0B0E14"

# ---------------------------------------------------------------------------
# The mark, on a 512 grid. Everything is clipped to the tile, so the neck can
# run out of the bottom edge cleanly.

TILE_R = 112

HEAD = (
    "M0 262"
    " C26 160 112 96 214 96"           # crown
    " C268 96 306 118 340 146"         # forehead into the bill
    " C386 182 430 214 470 246"        # upper edge of the bill to the tip
    " C414 242 344 236 292 240"        # underside of the upper mandible
    " C240 240 204 272 204 318"        # gape corner, into the chin
    " C204 372 258 420 286 512"        # throat to the bottom edge
    " L0 512"                          # back of the neck, straight down
    " Z"
)

JAW = ("M254 300 C262 288 276 282 292 280 C360 272 412 264 454 256"
       " C410 272 352 290 300 302 C282 306 266 306 254 300 Z")

# The navy teardrop round the eye points down the bill, where the real bird's
# bare black facial skin is.
EYE_RING = ("M200 172 C200 150 216 136 236 136 C258 136 286 152 312 170"
            " C286 186 258 204 236 204 C216 204 200 192 200 172 Z")
EYE = (236, 170, 19)


# THE NAVY BAND. The head sits inside an inner frame, inset PAD on every side,
# so the white never runs to the tile's edge. Added 22 Oct 2026 (Dan: "can we
# put a bit of blue padding around the icon"), after the favicon on a dark tab
# bar read as a white blob running off the edge: there the navy tile all but
# disappears, and only a band of it round the head keeps the shape. 48 was
# chosen over 72 at 16px: at 72 the head is too small to read in a tab.
PAD = 48


def mark_body(tile=NAVY, uid="t"):
    cx, cy, r = EYE
    s = (512 - 2 * PAD) / 512
    inner_r = TILE_R - PAD
    # The frame's corners are concentric with the tile's, so where the neck
    # meets the bottom-left corner it rounds with the navy curve outside it.
    # The back of the neck runs straight down to that corner rather than
    # sweeping into a curved notch (Dan, 22 Oct 2026: "that can be straighten
    # but round the corner so matches the blue curve").
    a, b = PAD, 512 - PAD
    frame = (f"M{a} {a + inner_r} A{inner_r} {inner_r} 0 0 1 {a + inner_r} {a} H{b - inner_r} "
             f"A{inner_r} {inner_r} 0 0 1 {b} {a + inner_r} V{b - inner_r} "
             f"A{inner_r} {inner_r} 0 0 1 {b - inner_r} {b} H{a + inner_r} "
             f"A{inner_r} {inner_r} 0 0 1 {a} {b - inner_r} Z")
    return (
        f'<defs><clipPath id="{uid}"><path d="{frame}"/></clipPath></defs>'
        f'<rect width="512" height="512" rx="{TILE_R}" fill="{tile}"/>'
        f'<g clip-path="url(#{uid})"><g transform="translate({PAD} {PAD}) scale({s:.4f})">'
        f'<path d="{HEAD}" fill="{WHITE}"/>'
        f'<path d="{JAW}" fill="{WHITE}"/>'
        f'<path d="{EYE_RING}" fill="{tile}"/>'
        f'<circle cx="{cx}" cy="{cy}" r="{r}" fill="{GOLD}"/>'
        f'</g></g>'
    )


def mark(size=512):
    return (
        f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512" '
        f'width="{size}" height="{size}" role="img" aria-label="gannet">\n'
        f'  {mark_body()}\n</svg>\n'
    )


def maskable(size=512):
    """A full-bleed square for Android's maskable icon: the launcher crops it
    to its own shape, so the ground is square and the tile is drawn inside the
    80% safe zone."""
    return (
        f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512" width="{size}" height="{size}">\n'
        f'  <rect width="512" height="512" fill="{NAVY}"/>\n'
        f'  <g transform="translate(51.2 51.2) scale(0.8)">{mark_body(uid="m")}</g>\n'
        f'</svg>\n'
    )


# ---------------------------------------------------------------------------
# The wordmark, in units of the x-height. Measured from the chosen draft: a
# 33 px stroke on a 170 px x-height, the t rising 23% above it, the g's hook
# dropping just over half an x-height below the baseline.

X = 100.0
S = 19.5
R = (X - S) / 2
H = S / 2
GAP = 9
ASC = 23
HOOK = 10            # how far below the baseline the g's hook turns


def wordmark_strokes():
    out, x = [], 0.0
    # g: a bowl, a stem on the right, and a U-shaped hook below the baseline
    out += [f'<circle cx="{x + X/2}" cy="{X/2}" r="{R}"/>',
            f'<path d="M{x + X - H} 0 V{X + HOOK} A{R} {R} 0 0 1 {x + H} {X + HOOK}"/>']
    x += X + GAP
    # a: a bowl and a full-height stem
    out += [f'<circle cx="{x + X/2}" cy="{X/2}" r="{R}"/>',
            f'<path d="M{x + X - H} 0 V{X}"/>']
    x += X + GAP
    # n, n: a left stem, an arch, a right stem
    for _ in range(2):
        left, right = x + H, x + X - H
        out.append(f'<path d="M{left} 0 V{X} M{left} {X/2} A{R} {R} 0 0 1 {right} {X/2} V{X}"/>')
        x += X + GAP
    # e: a full ring with its lower-right wedge cut away by a mask, and the
    # bar through its centre. A stroked arc cannot end flush with the bar -
    # its square end steps out past the curve - so the opening is cut, not
    # drawn: the ring stays whole down to the bar's lower edge and stops on
    # a line from the centre at 38 degrees. The bar is clipped to the ring's
    # outer circle, so its right end follows the curve instead of a corner.
    # Nothing inside the mask may inherit the group's stroke, or it leaks.
    cx = x + X/2
    t38 = math.tan(math.radians(38))
    far = X
    wedge = (f"{cx},{X/2 + H} {cx + far},{X/2 + H} {cx + far},{X/2 + far * t38:.2f} "
             f"{cx},{X/2}")
    out += [f'<mask id="e-open" maskUnits="userSpaceOnUse" x="{cx - X}" y="{-X}" width="{3 * X}" height="{3 * X}">'
            f'<rect x="{cx - X}" y="{-X}" width="{3 * X}" height="{3 * X}" fill="#fff" stroke="none"/>'
            f'<polygon points="{wedge}" fill="#000" stroke="none"/></mask>',
            f'<clipPath id="e-bar"><circle cx="{cx}" cy="{X/2}" r="{R + H}"/></clipPath>',
            f'<circle cx="{cx}" cy="{X/2}" r="{R}" mask="url(#e-open)"/>',
            f'<path d="M{cx - R} {X/2} H{cx + R + H}" clip-path="url(#e-bar)"/>']
    x += X + GAP
    # t: a stem from above the x-height, a crossbar, a foot turning right
    sx, fr = x + H + 14, 26
    end = sx + fr + 16
    out += [f'<path d="M{sx} {-ASC} V{X - H - fr} A{fr} {fr} 0 0 0 {sx + fr} {X - H} H{end}"/>',
            f'<path d="M{x - 2} {H} H{end}"/>']
    return out, end


WORD_W = wordmark_strokes()[1]
WORD_TOP = -ASC
WORD_BOTTOM = X + HOOK + R + H


def wordmark_body(colour):
    strokes, _ = wordmark_strokes()
    return (f'<g fill="none" stroke="{colour}" stroke-width="{S}" stroke-linecap="butt">'
            + "".join(strokes) + "</g>")


def wordmark(colour=NAVY, pad=4):
    w, h = WORD_W + 2 * pad, WORD_BOTTOM - WORD_TOP + 2 * pad
    return (
        f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="{-pad} {WORD_TOP - pad} {w:.1f} {h:.1f}" '
        f'width="{w * 2:.0f}" height="{h * 2:.0f}" role="img" aria-label="gannet">\n'
        f'  {wordmark_body(colour)}\n</svg>\n'
    )


# ---------------------------------------------------------------------------
# The lockup: the tile, then the word, the word's x-height centred on the
# tile and sized to 34% of it, which is where the pair balanced by eye: at
# 44% the g's hook hung below the tile.

def lockup(word=NAVY, ground=None, pad=0):
    tile = 512
    scale = tile * 0.34 / X
    gap = tile * 0.16
    wx = tile + gap
    wy = tile / 2 - (X / 2) * scale
    w = wx + WORD_W * scale + pad
    h = tile
    bg = f'<rect x="{-pad}" y="{-pad}" width="{w + pad:.1f}" height="{h + 2 * pad}" fill="{ground}"/>' if ground else ""
    return (
        f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="{-pad} {-pad} {w + pad:.1f} {h + 2 * pad}" '
        f'width="{(w + pad) / 2:.0f}" height="{(h + 2 * pad) / 2:.0f}" role="img" aria-label="gannet">\n'
        f'  {bg}{mark_body(uid="lt")}\n'
        f'  <g transform="translate({wx:.1f} {wy:.1f}) scale({scale:.4f})">{wordmark_body(word)}</g>\n'
        f'</svg>\n'
    )


# ---------------------------------------------------------------------------
# Cards: the Open Graph image every link preview shows, and the GitHub social
# preview. Parchment ground and charcoal type, as the site; the lockup in navy.
# The line is a description of what gannet is, not a claim that it runs.

CANVAS = "#F2F0EB"
CHARCOAL = "#292827"
CARD_LINE = "Dives deep, cites everything"  # Dan, 22 Oct 2026


def card(width, height):
    tile = height * 0.30
    scale = tile / 512
    x0 = width * 0.08
    y0 = height * 0.24
    word_scale = tile * 0.34 / X
    wx = x0 + tile * 1.16
    wy = y0 + tile / 2 - (X / 2) * word_scale
    text_y = y0 + tile + height * 0.17
    return (
        f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {width} {height}" width="{width}" height="{height}">\n'
        f'  <rect width="{width}" height="{height}" fill="{CANVAS}"/>\n'
        f'  <g transform="translate({x0:.1f} {y0:.1f}) scale({scale:.4f})">{mark_body(uid="c")}</g>\n'
        f'  <g transform="translate({wx:.1f} {wy:.1f}) scale({word_scale:.4f})">{wordmark_body(NAVY)}</g>\n'
        f'  <text x="{x0:.1f}" y="{text_y:.1f}" font-family="Geist, Inter, sans-serif" font-weight="460" '
        f'font-size="{height * 0.072:.1f}" letter-spacing="-1" fill="{CHARCOAL}">{CARD_LINE}</text>\n'
        f'</svg>\n'
    )


def main():
    files = {
        "logo.svg": mark(),
        "logo-maskable.svg": maskable(),
        "wordmark.svg": wordmark(NAVY),
        "wordmark-white.svg": wordmark(WHITE),
        "lockup.svg": lockup(NAVY),
        "lockup-white.svg": lockup(WHITE),
        "banner-light.svg": lockup(NAVY, PAPER, pad=48),
        "banner-dark.svg": lockup(WHITE, INK, pad=48),
        "og.svg": card(1200, 630),
        "social.svg": card(1280, 640),
    }
    for name, text in files.items():
        (OUT / name).write_text(text)
        print(f"wrote assets/{name}")

    # The raster set every surface asks for. rsvg-convert renders the SVGs
    # above exactly; nothing raster is drawn by hand.
    if not shutil.which("rsvg-convert"):
        print("rsvg-convert not found: SVGs written, PNGs skipped")
        return
    png = OUT / "png"
    png.mkdir(exist_ok=True)
    for name, src, size in [
        ("favicon-16.png", "logo.svg", 16), ("favicon-32.png", "logo.svg", 32),
        ("favicon-48.png", "logo.svg", 48), ("apple-touch-icon.png", "logo.svg", 180),
        ("icon-192.png", "logo.svg", 192), ("icon-512.png", "logo.svg", 512),
        ("icon-512-maskable.png", "logo-maskable.svg", 512),
        ("avatar-1024.png", "logo.svg", 1024), ("mark-240.png", "logo.svg", 240),
    ]:
        subprocess.run(["rsvg-convert", "-w", str(size), "-h", str(size),
                        str(OUT / src), "-o", str(png / name)], check=True)
        print(f"wrote assets/png/{name}")
    for name, src, width in [("lockup-1200.png", "lockup.svg", 1200), ("lockup-white-1200.png", "lockup-white.svg", 1200),
                             ("og-image.png", "og.svg", 1200), ("social.png", "social.svg", 1280)]:
        subprocess.run(["rsvg-convert", "-w", str(width), str(OUT / src), "-o", str(png / name)], check=True)
        print(f"wrote assets/png/{name}")


if __name__ == "__main__":
    main()

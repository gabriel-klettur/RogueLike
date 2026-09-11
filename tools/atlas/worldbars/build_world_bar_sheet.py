#!/usr/bin/env python3
"""Reduce the hand-drawn world-bar art to the game's texel grid.

The source is drawn at roughly thirty times the size the readout is rendered at: an icon is
180x178 px and the game draws it at 10, where one texel is one screen pixel at the snapped
camera. Nothing about that is wrong with the art -- it is drawn to be seen at HUD size too --
but it means the reduction has to be deliberate.

WHAT THE COLOUR SHEET PROVIDES, AND WHAT IT DELIBERATELY DOES NOT
-----------------------------------------------------------------
Measured on `staging/ui/world_bars_colour_src.png`: eight framed icons, four pip corners, a pip
core, and TWO BAR FRAMES THAT ARE 100% HOLLOW -- zero opaque texels across their whole central
band. The greyscale sheet it replaces carried a frame, a plate and a fill per row; this one
carries only the frames, because the interior is no longer a sprite. The empty part of the bar
shows the world straight through and the filled part is drawn by a shader.

So this script paints TWELVE of the layout's seventeen cells and says so in the manifest. The
importer assigns only what the manifest lists, which is the difference between "not painted, use
the generated piece" and "painted transparent", two states a sheet alone cannot tell apart.

THE ICONS KEEP THEIR FRAMES, which reverses the greyscale pipeline's decision and is a
consequence of colour rather than a change of mind. Cropping to the bare glyph bought resolution
when every glyph was white: it took the readable size from 12 texels to 8. These glyphs carry
their own hue, and measured at 8, 10, 12 and 14 the colour does most of the identifying work --
at 10 all eight read with their gold frames intact, and the only genuinely confusable pair is
Poison against Root, both green, separated by silhouette density. The frame is also part of this
art's identity in a way the greyscale one's was not.

Usage:
    python tools/atlas/worldbars/build_world_bar_sheet.py [--preview]

Writes  unity/Valkur/Assets/_Project/Art/WorldBars/world_bars.png
        unity/Valkur/Assets/_Project/Art/WorldBars/world_bars_build.json
Then run  Valkur > UI > Import World Bar Skin  in Unity.
"""

from __future__ import annotations

import argparse
import json
import os
import sys

try:
    from PIL import Image
except ImportError:  # pragma: no cover
    sys.exit("Pillow is required: python -m pip install Pillow")


REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
SRC = os.path.join(REPO, "staging", "ui", "world_bars_colour_src.png")
OUT_DIR = os.path.join(REPO, "unity", "Valkur", "Assets", "_Project", "Art", "WorldBars")
OUT = os.path.join(OUT_DIR, "world_bars.png")
MANIFEST = os.path.join(OUT_DIR, "world_bars_build.json")

# ── The source, as measured by connected components ──────────────────────────
# Recorded rather than re-detected on every run so a re-export that MOVES a piece fails here
# instead of silently slicing a neighbour.

ICONS = [  # StatusEffectKind order: the drawn row is indexed by the enum's integer value
    ("burn",        29, 151, 179, 177),
    ("poison",     227, 151, 181, 177),
    ("stun",       425, 151, 180, 178),
    ("freeze",     624, 151, 180, 178),
    ("slow",       823, 151, 181, 177),
    ("root",      1022, 151, 180, 177),
    ("vulnerable",1220, 151, 181, 178),
    ("marked",    1420, 151, 177, 178),
]

# The pip's ring is drawn as FOUR corner pieces; the game wants one square sprite, so they are
# composited over their common bounding box -- the hole between them IS the ring's interior.
PIP_CORNERS = [(61, 371, 81, 78), (151, 370, 80, 79), (61, 454, 81, 78), (151, 454, 80, 78)]
PIP_CORE = (275, 405, 98, 97)

FRAME_HEALTH   = (49, 565, 725, 117)
FRAME_RESOURCE = (807, 574, 531, 108)

# ── Target geometry, mirroring WorldBarStyle ─────────────────────────────────
# These must match the asset: the importer slices by WorldBarSheetLayout, which is built from
# the style's numbers, so a disagreement here puts the art one cell over.

HEALTH_ROW   = 6   # texels, frame included
RESOURCE_ROW = 4
PIP          = 6
ICON         = 10  # measured: 8 identifies by colour alone, 10 by colour AND silhouette
GUTTER       = 1
STRETCH_W    = 8   # nominal width of a 9-sliced piece; only its border columns survive
FRAME_BORDER = 3   # must match WorldBarSheetLayout's stretchBorder
FLAT_BORDER  = 2   # must match its flatBorder
SOLID_SIZE   = 4


def _resize_rgba(src: Image.Image, w: int, h: int) -> Image.Image:
    """Area downscale, then flatten the alpha the art exported just short of opaque.

    The source ships at alpha 252-253 rather than 255. Left alone, every frame in the game is
    faintly see-through, which reads as a rendering bug rather than as a style -- and here it
    would be invisible against the deliberately transparent interior beside it.
    """
    small = src.resize((w, h), Image.BOX)
    px = small.load()
    for yy in range(h):
        for xx in range(w):
            r, g, b, a = px[xx, yy]
            if a >= 200:
                a = 255
            elif a < 24:
                a = 0
            px[xx, yy] = (r, g, b, a)
    return small


def build_icon(img: Image.Image, box, size: int) -> Image.Image:
    """A framed icon, whole. No glyph extraction: this art's gold frame is part of the icon."""
    _, x, y, w, h = box
    return _resize_rgba(img.crop((x, y, x + w, y + h)), size, size)


def build_sliced(img: Image.Image, box, width: int, height: int, border: int) -> Image.Image:
    """A 9-sliced bar piece, COMPOSED from its own end caps rather than squashed into `width`.

    A sliced sprite keeps `border` columns at each end and stretches what is between them, so
    those columns have to be the art's END CAP at the same scale the HEIGHT was reduced by.
    Resizing the whole 725-pixel bar into eight columns instead -- the obvious one-liner, and
    what the first version of this script did -- reduces the width by 90x while the height goes
    down by 19x, so the ornate cap arrives as two thirds of one column and the drawn bar has no
    cap at all. Scale comes from the HEIGHT; only the middle is sampled from the centre.
    """
    x, y, w, h = box
    crop = img.crop((x, y, x + w, y + h))
    scale = height / float(h)
    sw = max(width, int(round(w * scale)))
    scaled = _resize_rgba(crop, sw, height)

    mid = max(1, width - 2 * border)
    out = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    out.paste(scaled.crop((0, 0, border, height)), (0, 0))
    cx = (sw - mid) // 2
    out.paste(scaled.crop((cx, 0, cx + mid, height)), (border, 0))
    out.paste(scaled.crop((sw - border, 0, sw, height)), (width - border, 0))
    return out


def composite_pip_ring(img: Image.Image, size: int) -> Image.Image:
    x0 = min(c[0] for c in PIP_CORNERS)
    y0 = min(c[1] for c in PIP_CORNERS)
    x1 = max(c[0] + c[2] for c in PIP_CORNERS)
    y1 = max(c[1] + c[3] for c in PIP_CORNERS)
    return _resize_rgba(img.crop((x0, y0, x1, y1)), size, size)


def next_pot(v: int) -> int:
    p = 1
    while p < v:
        p <<= 1
    return p


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--preview", action="store_true",
                    help="also write a magnified preview next to the sheet")
    args = ap.parse_args()

    if not os.path.exists(SRC):
        sys.exit(f"[world_bars] Source not found: {SRC}\n"
                 "The hand-drawn sheet lives in staging/, outside Assets/, because Unity imports\n"
                 "everything under Assets whether or not anything references it.")

    img = Image.open(SRC).convert("RGBA")

    inner_h = HEALTH_ROW - 2
    inner_r = RESOURCE_ROW - 2

    # What this sheet paints. The five it does NOT paint - both plates, both fills and the plain
    # square - are absent on purpose: the bar's interior is transparent and its filled part is
    # drawn by a shader, so a painted plate would be exactly the thing the design removes.
    painted = {
        "frame_health":   build_sliced(img, FRAME_HEALTH,   STRETCH_W, HEALTH_ROW,   FRAME_BORDER),
        "frame_resource": build_sliced(img, FRAME_RESOURCE, STRETCH_W, RESOURCE_ROW, FRAME_BORDER),
        "pip_frame":      composite_pip_ring(img, PIP),
        "pip_core":       _resize_rgba(img.crop((PIP_CORE[0], PIP_CORE[1],
                                                 PIP_CORE[0] + PIP_CORE[2],
                                                 PIP_CORE[1] + PIP_CORE[3])), PIP - 2, PIP - 2),
    }
    for i, box in enumerate(ICONS):
        painted[f"icon_{i}"] = build_icon(img, box, ICON)

    # Cell sizes for every piece the layout knows about, painted or not, so the sheet keeps the
    # rects the importer slices by. Unpainted cells stay transparent and are left out of the
    # manifest.
    cells = [
        ("frame_health",   STRETCH_W,  HEALTH_ROW),
        ("frame_resource", STRETCH_W,  RESOURCE_ROW),
        ("plate_health",   STRETCH_W,  inner_h),
        ("plate_resource", STRETCH_W,  inner_r),
        ("fill_health",    STRETCH_W,  inner_h),
        ("fill_resource",  STRETCH_W,  inner_r),
        ("solid",          SOLID_SIZE, SOLID_SIZE),
        ("__shelf__",      0, 0),
        ("pip_frame",      PIP,        PIP),
        ("pip_core",       PIP - 2,    PIP - 2),
        ("__shelf__",      0, 0),
    ] + [(f"icon_{i}", ICON, ICON) for i in range(len(ICONS))]

    placed, width, shelf_y, x, shelf_h = [], 0, 0, 0, 0
    for name, cw, ch in cells:
        if name == "__shelf__":
            width = max(width, x - GUTTER)
            x, shelf_y, shelf_h = 0, shelf_y + shelf_h + GUTTER, 0
            continue
        placed.append((name, x, shelf_y, cw, ch))
        x += cw + GUTTER
        shelf_h = max(shelf_h, ch)
    width = max(width, x - GUTTER)
    height = shelf_y + shelf_h

    sheet_w, sheet_h = next_pot(max(32, width)), next_pot(max(32, height))
    sheet = Image.new("RGBA", (sheet_w, sheet_h), (255, 255, 255, 0))
    for name, x, y, cw, ch in placed:
        piece = painted.get(name)
        if piece is None:
            continue
        # Unity counts texture rows from the BOTTOM and PIL from the top. Getting this backwards
        # is invisible in the file and puts every piece on the wrong shelf in game.
        sheet.paste(piece, (x, sheet_h - y - ch), piece)

    os.makedirs(OUT_DIR, exist_ok=True)
    sheet.save(OUT)

    manifest = {
        "source": os.path.relpath(SRC, REPO).replace("\\", "/"),
        "sheet": [sheet_w, sheet_h],
        "healthRowTexels": HEALTH_ROW,
        "resourceRowTexels": RESOURCE_ROW,
        "pipTexels": PIP,
        "iconTexels": ICON,
        # The importer assigns ONLY these. A cell absent here is "not painted, keep the generated
        # piece"; a cell painted transparent would be indistinguishable from it in the PNG alone.
        "painted": sorted(painted.keys()),
        "pieces": {name: [x, sheet_h - y - ch, cw, ch] for name, x, y, cw, ch in placed},
    }
    with open(MANIFEST, "w", encoding="utf-8") as f:
        json.dump(manifest, f, indent=2)

    print(f"[world_bars] {OUT}")
    print(f"[world_bars] {sheet_w}x{sheet_h}, {len(painted)} painted of {len(placed)} cells")
    print(f"[world_bars] not painted (generated fallback): "
          f"{', '.join(n for n, _, _, _, _ in placed if n not in painted)}")
    print(f"[world_bars] health row {HEALTH_ROW}, resource {RESOURCE_ROW}, pip {PIP}, icon {ICON}")
    print("[world_bars] Now run Valkur > UI > Import World Bar Skin.")

    if args.preview:
        scale = 10
        prev = Image.new("RGBA", (sheet_w * scale, sheet_h * scale), (26, 26, 30, 255))
        prev.alpha_composite(sheet.resize((sheet_w * scale, sheet_h * scale), Image.NEAREST))
        prev.save(os.path.join(OUT_DIR, "world_bars_preview.png"))
        print("[world_bars] preview written")


if __name__ == "__main__":
    main()

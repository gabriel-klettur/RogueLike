#!/usr/bin/env python3
"""Build a numbered contact sheet from a directory of sprite crops.

Used to review a slicing pass: every crop is drawn isolated, at a readable size,
labelled with the index it carries in the slice manifest and its native pixel
size.  That index is the key everything downstream (names, categories, sizes) is
written against.

    python make_contact_sheet.py --crops <dir> --out <png> [--cell 220] [--cols 6]
"""

from __future__ import annotations

import argparse
import os

from PIL import Image, ImageDraw, ImageFont

CHECKER_A = (58, 58, 64)
CHECKER_B = (44, 44, 50)
LABEL_H = 26


def _font(size: int) -> ImageFont.ImageFont:
    for path in (r"C:\Windows\Fonts\arialbd.ttf", r"C:\Windows\Fonts\arial.ttf"):
        if os.path.exists(path):
            return ImageFont.truetype(path, size)
    return ImageFont.load_default()


def _checker(size: int, square: int = 12) -> Image.Image:
    """Neutral checkerboard so transparent edges and dark art both read."""
    tile = Image.new("RGB", (size, size), CHECKER_A)
    d = ImageDraw.Draw(tile)
    for y in range(0, size, square):
        for x in range(0, size, square):
            if ((x // square) + (y // square)) % 2:
                d.rectangle([x, y, x + square - 1, y + square - 1], fill=CHECKER_B)
    return tile


def build(crops_dir: str, out_path: str, cell: int, cols: int) -> None:
    files = sorted(f for f in os.listdir(crops_dir) if f.lower().endswith(".png"))
    if not files:
        raise SystemExit(f"no PNG crops in {crops_dir}")

    rows = (len(files) + cols - 1) // cols
    cell_h = cell + LABEL_H
    sheet = Image.new("RGB", (cols * cell, rows * cell_h), (24, 24, 28))
    checker = _checker(cell)
    draw = ImageDraw.Draw(sheet)
    font = _font(15)

    for i, name in enumerate(files):
        cx, cy = (i % cols) * cell, (i // cols) * cell_h
        sheet.paste(checker, (cx, cy))

        img = Image.open(os.path.join(crops_dir, name)).convert("RGBA")
        w, h = img.size
        scale = min((cell - 12) / w, (cell - 12) / h)
        fit = img.resize((max(1, int(w * scale)), max(1, int(h * scale))), Image.LANCZOS)
        sheet.paste(fit, (cx + (cell - fit.width) // 2, cy + (cell - fit.height) // 2), fit)

        idx = os.path.splitext(name)[0].rsplit("_", 1)[-1].lstrip("0") or "0"
        draw.rectangle([cx, cy + cell, cx + cell - 1, cy + cell_h - 1], fill=(16, 16, 20))
        draw.text((cx + 6, cy + cell + 5), f"#{idx}", fill=(255, 214, 0), font=font)
        draw.text((cx + 54, cy + cell + 5), f"{w}x{h}px", fill=(150, 200, 255), font=font)
        draw.rectangle([cx, cy, cx + cell - 1, cy + cell_h - 1], outline=(90, 90, 100))

    sheet.save(out_path)
    print(f"{out_path}: {len(files)} crops, {cols}x{rows}")


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--crops", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--cell", type=int, default=220)
    ap.add_argument("--cols", type=int, default=6)
    args = ap.parse_args()
    build(args.crops, args.out, args.cell, args.cols)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

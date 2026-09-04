#!/usr/bin/env python3
"""Cut Gatita's 3x3 LISTENING sheet into one aligned PNG per pose.

    python tools/atlas/wave6/build_gatita_listening_faces.py [--dry-run]

These are the faces she pulls while the PLAYER is typing — attentive, head
tilted, ears up, some with the little attention marks beside them. They are a
second axis, not nine more emotions: what she looks like doing the listening,
paired one-to-one with what she would look like saying something.

WHY THIS IS A SEPARATE SCRIPT FROM build_gatita_faces.py. That one measures a
sheet whose three columns are separated by clean vertical gaps across the whole
image. This sheet's attention marks BRIDGE the gap between the middle and right
cells on every row, so a whole-image column projection finds two bands instead of
three. The columns here are therefore measured PER ROW, which is also why the
bands below are not a single COL_BANDS list.

ALIGNMENT IS AGAINST THE TALKING FACES, NOT AGAINST THIS SHEET. The listening
portrait swaps into the very same rect as the talking one, so aligning these nine
to each other would still let the head jump the moment she stops listening and
starts answering. Every pose is correlated against the shipped
`gatita_face_neutral.png` and pasted onto that exact 370x395 canvas.

The correlation uses the HEAD ONLY. The attention marks are drawn outside the
silhouette and are present in five of the nine, so a whole-cell alpha mask would
drag those five sideways by the weight of their own marks — measured, up to 40 px.
The head is isolated as the largest connected alpha component, which is the same
reason build_knight_frames.py takes its ground line off the largest component
rather than off the lowest pixel.
"""

from __future__ import annotations

import argparse
import os
import sys

import numpy as np
from PIL import Image
from scipy import ndimage, signal

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
FACIAL_DIR = os.path.join(
    REPO_ROOT, "unity", "Valkur", "Assets", "_Project", "Art", "NPC", "neutral",
    "vendors", "cheff", "gatita_chanchita", "facial")

SHEET = os.path.join(REPO_ROOT, "staging", "npc",
                     "gatita_facil_listening_expressions.png")

# The talking face every pose is aligned to, and whose canvas they all adopt.
REFERENCE = os.path.join(FACIAL_DIR, "gatita_face_neutral.png")

PREFIX = "gatita_listening"

# Measured off the sheet's own alpha, row bands first and then the columns WITHIN
# each row — see the module docstring for why the columns cannot be global.
ROW_BANDS = [(57, 429), (439, 822), (833, 1211)]
COL_BANDS_PER_ROW = [
    [(48, 378), (457, 854), (867, 1231)],
    [(27, 435), (458, 787), (837, 1226)],
    [(23, 437), (450, 793), (833, 1231)],
]

# Row-major, matching the bands above, and matching FacialExpression 1:1 so every
# listening pose has a talking face it belongs to. Read off the art:
#   neutral   square on, calm wide eyes, closed mouth
#   laugh     tongue out, attention marks, brows up
#   thinking  eyes slid to the side, one brow lowered
#   playful   head tilted hard, tongue out, marks
#   happy     centred, soft eyes, gentle smile
#   worry     brows raised inward, tongue out, uneasy
#   wink      one eye shut, tongue out, marks both sides
#   sad       looking away, brows drawn, mouth small
#   angry     tilted, marks, brow down
NAMES = [
    "neutral", "laugh", "thinking",
    "playful", "happy", "worry",
    "wink", "sad", "angry",
]

ALPHA_CUTOFF = 60
SEARCH = 60          # +/- pixels the correlation may shift a pose by


def head_mask(cell: np.ndarray) -> np.ndarray:
    """
    The largest connected alpha blob — the head, without the attention marks.

    The marks are separate blobs drawn clear of the silhouette, so "largest
    component" separates them exactly. Aligning on the whole cell instead moves
    the five marked poses by the weight of their own marks.
    """
    alpha = cell[:, :, 3] > ALPHA_CUTOFF
    labels, count = ndimage.label(alpha)
    if count == 0:
        return alpha
    sizes = ndimage.sum(alpha, labels, range(1, count + 1))
    return labels == (int(np.argmax(sizes)) + 1)


def best_offset(mask: np.ndarray, reference: np.ndarray) -> tuple[int, int]:
    """The (dx, dy) putting `mask` on top of `reference`, by cross-correlation."""
    a = mask.astype(np.float32) - mask.mean()
    b = reference.astype(np.float32) - reference.mean()
    corr = signal.fftconvolve(b, a[::-1, ::-1], mode="same")

    cy, cx = np.unravel_index(int(np.argmax(corr)), corr.shape)
    dy = cy - reference.shape[0] // 2
    dx = cx - reference.shape[1] // 2
    return int(np.clip(dx, -SEARCH, SEARCH)), int(np.clip(dy, -SEARCH, SEARCH))


def normalize_head(cell: np.ndarray, target_head_h: int) -> np.ndarray:
    """
    The cell scaled so its HEAD is the same height as the talking faces' head.

    THE ARTIST DREW THIS SHEET AT ITS OWN ZOOM, exactly as every other generated
    sheet in this project was. Measured raw, the nine listening heads run 91% to
    108% of the shipped talking head, so pasting them at source scale makes the
    portrait breathe every time she starts and stops listening. `TARGET_BODY_PX`
    in build_player_frames.py exists for the same reason and is measured the same
    way — off a body dimension, never off the bounding box, because the bounding
    box moves with the pose.

    HEIGHT rather than width, and the head alone rather than the whole cell.
    Width is what a tilt changes — a head turned hard sideways is legitimately
    wider and must be allowed to be — while its crown-to-chin height is the thing
    that should read as constant. Including the attention marks would size the
    pose by its punctuation: they sit clear of the silhouette in five of the nine,
    so those five would come out systematically smaller than the four without.
    """
    solid = cell[:, :, 3] > ALPHA_CUTOFF
    labels, count = ndimage.label(solid)
    if count == 0:
        return cell

    sizes = ndimage.sum(solid, labels, range(1, count + 1))
    head = labels == (int(np.argmax(sizes)) + 1)
    ys, _ = np.nonzero(head)
    head_h = ys.max() - ys.min() + 1

    scale = target_head_h / head_h
    if abs(scale - 1.0) < 0.005:
        return cell

    size = (max(int(round(cell.shape[1] * scale)), 1),
            max(int(round(cell.shape[0] * scale)), 1))
    return np.array(Image.fromarray(cell).resize(size, Image.LANCZOS))


def compose(cell: np.ndarray, off_x: int, off_y: int, width: int, height: int) -> Image.Image:
    """
    The cell placed on the reference canvas, with any attention mark that would
    fall off the edge nudged inward instead of being cropped.

    THE HEAD SIZE IS THE THING THAT MUST NOT MOVE. The portrait rect is 132x141
    and the talking sprites are 370x395 — the same aspect to within half a
    percent — so `preserveAspect` fits them almost exactly. Widening the canvas to
    make room for the marks would therefore make Unity fit by WIDTH instead and
    render the head about 12% smaller, and scaling the pose down to fit costs the
    same 11% directly. Both trade a head that jumps for marks that fit, which is
    the worse bargain: the head is the character and the marks are punctuation.

    So the head keeps the reference scale and the reference position, and the
    marks — which are separate connected components drawn clear of the
    silhouette, at a distance that carries no meaning — are slid toward it by the
    few pixels they overhang. Measured on this sheet, five of the nine poses
    overhang and the largest correction is small enough to be invisible beside
    the pose's own tilt. Cropping them instead loses up to 2.9% of the drawing,
    and what it loses is exactly the part that says she is listening.
    """
    out = Image.new("RGBA", (width, height), (0, 0, 0, 0))

    # TWO THRESHOLDS, AND BOTH ARE NEEDED — measured, either one alone is wrong.
    # Labelling at ALPHA_CUTOFF separates the marks from the head cleanly, but
    # every pixel between 1 and the cutoff then belongs to no component and is
    # dropped: about 600 px per pose, and what it takes off is the anti-aliased
    # rim of every ear. Labelling at alpha > 0 instead keeps the rim and destroys
    # the separation, because that same faint rim BRIDGES the marks to the head —
    # the "head" blob then measured 392 px wide on a 398 px cell, i.e. the marks
    # were inside it and could not be nudged at all.
    #
    # So: label where the drawing is solid, then grow those labels outward over
    # the soft rim, assigning each faint pixel to the component it is nearest to.
    solid = cell[:, :, 3] > ALPHA_CUTOFF
    labels, count = ndimage.label(solid)
    if count == 0:
        return out

    nearest = ndimage.distance_transform_edt(
        labels == 0, return_distances=False, return_indices=True)
    labels = labels[tuple(nearest)]
    labels[cell[:, :, 3] == 0] = 0

    sizes = ndimage.sum(solid, ndimage.label(solid)[0], range(1, count + 1))
    head_label = int(np.argmax(sizes)) + 1

    for label in range(1, count + 1):
        piece = labels == label
        ys, xs = np.nonzero(piece)

        shift = 0
        if label != head_label:
            left = xs.min() + off_x
            right = xs.max() + off_x
            if left < 0:
                shift = -left
            elif right > width - 1:
                shift = (width - 1) - right

        rgba = np.zeros_like(cell)
        rgba[piece] = cell[piece]
        out.alpha_composite(Image.fromarray(rgba), (off_x + shift, off_y))

    return out


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()

    if not os.path.isfile(SHEET):
        print(f"ERROR: sheet not found at {SHEET}", file=sys.stderr)
        return 1
    if not os.path.isfile(REFERENCE):
        print(f"ERROR: reference face not found at {REFERENCE}", file=sys.stderr)
        return 1

    sheet = np.array(Image.open(SHEET).convert("RGBA"))
    ref_image = Image.open(REFERENCE).convert("RGBA")
    ref_w, ref_h = ref_image.size
    ref_mask = np.array(ref_image)[:, :, 3] > ALPHA_CUTOFF

    # The head height every pose is scaled to. Taken off the reference's own
    # silhouette rather than hardcoded, so re-cutting after any change to the
    # talking faces keeps the two sets in step.
    ref_ys, _ = np.nonzero(ref_mask)
    ref_head_h = int(ref_ys.max() - ref_ys.min() + 1)

    written = 0
    for index, name in enumerate(NAMES):
        y0, y1 = ROW_BANDS[index // 3]
        x0, x1 = COL_BANDS_PER_ROW[index // 3][index % 3]
        cell = normalize_head(sheet[y0:y1 + 1, x0:x1 + 1], ref_head_h)

        # Onto the reference canvas, head roughly centred, then correlated.
        head = head_mask(cell)
        ys, xs = np.nonzero(head)
        cy, cx = (ys.min() + ys.max()) // 2, (xs.min() + xs.max()) // 2

        canvas = Image.new("RGBA", (ref_w, ref_h), (0, 0, 0, 0))
        paste_x = ref_w // 2 - cx
        paste_y = ref_h // 2 - cy
        canvas.paste(Image.fromarray(cell), (paste_x, paste_y))

        placed = np.zeros((ref_h, ref_w), dtype=bool)
        hy, hx = np.nonzero(head)
        yy, xx = hy + paste_y, hx + paste_x
        keep = (yy >= 0) & (yy < ref_h) & (xx >= 0) & (xx < ref_w)
        placed[yy[keep], xx[keep]] = True

        dx, dy = best_offset(placed, ref_mask)
        aligned = compose(cell, paste_x + dx, paste_y + dy, ref_w, ref_h)

        got = np.array(aligned)[:, :, 3] > ALPHA_CUTOFF
        iou = (got & ref_mask).sum() / max((got | ref_mask).sum(), 1)
        print(f"  {PREFIX}_{name}.png  shift=({dx:+d},{dy:+d})  IoU vs neutral {iou:.3f}")

        if not args.dry_run:
            aligned.save(os.path.join(FACIAL_DIR, f"{PREFIX}_{name}.png"))
            written += 1

    print(f"{'DRY RUN — ' if args.dry_run else ''}{written} file(s) written to {FACIAL_DIR}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

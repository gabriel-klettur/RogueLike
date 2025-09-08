from __future__ import annotations
from typing import List, Tuple
import pygame


def image_to_grid_size(image: pygame.Surface | None, tile_size: int) -> Tuple[int, int]:
    """
    Compute (rows, cols) for a collision grid derived from an image size and tile size.
    Guarantees at least a 1x1 grid to allow editing even for very small images.
    Uses ceil-like division to cover partial edges.
    """
    if image is None:
        return 1, 1
    w = int(image.get_width())
    h = int(image.get_height())
    cols = max(1, (w + tile_size - 1) // tile_size)
    rows = max(1, (h + tile_size - 1) // tile_size)
    return rows, cols


def resample_collision_map(old_map: List[List[str]], new_rows: int, new_cols: int) -> List[List[str]]:
    """
    Resize a collision map to (new_rows x new_cols) using an area-pooling style nearest
    approach: mark a destination cell as '#' if any source cell in the corresponding
    source block is '#'. If the input map is empty, initializes a '.' map.
    Ensures at least 1x1 size when given non-positive sizes.
    """
    if new_rows <= 0 or new_cols <= 0:
        new_rows, new_cols = 1, 1
    old_rows = len(old_map)
    old_cols = len(old_map[0]) if old_rows > 0 else 0
    if old_rows == 0 or old_cols == 0:
        return [["." for _ in range(new_cols)] for _ in range(new_rows)]
    if new_rows == old_rows and new_cols == old_cols:
        # return a shallow copy to avoid accidental aliasing
        return [row[:] for row in old_map]

    res: List[List[str]] = [["." for _ in range(new_cols)] for _ in range(new_rows)]
    for r in range(new_rows):
        r0 = int((r * old_rows) / new_rows)
        r1 = int(((r + 1) * old_rows) / new_rows) - 1
        if r0 >= old_rows:
            r0 = old_rows - 1
        if r1 < r0:
            r1 = r0
        for c in range(new_cols):
            c0 = int((c * old_cols) / new_cols)
            c1 = int(((c + 1) * old_cols) / new_cols) - 1
            if c0 >= old_cols:
                c0 = old_cols - 1
            if c1 < c0:
                c1 = c0
            solid = False
            for sr in range(r0, r1 + 1):
                row = old_map[sr]
                for sc in range(c0, c1 + 1):
                    if row[sc] == "#":
                        solid = True
                        break
                if solid:
                    break
            res[r][c] = "#" if solid else "."
    return res

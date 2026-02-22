"""Normalize collision grids in buildings_collisions_by_image.json.

Usage (Windows PowerShell):
  python scripts/normalize_colliders_grids.py \
      --input data/buildings/buildings_collisions_by_image.json \
      --output data/buildings/buildings_collisions_by_image.json \
      --size 15 --fill . --backup

Behavior:
- For every image entry, if its collision grid is missing/invalid or has rows<=1 or cols<=1,
  it will be replaced by an N x N grid (default 15x15) filled with the given character ('.' or '#').
- For valid grids with rows>1 and cols>1, the script leaves them untouched unless --force is provided.
- It always rewrites width/height to match the collision array dimensions.
- If --backup is provided and output == input, a copy <input>.bak is written before modifying.

Notes:
- No image IO is performed; grid_ref_size is preserved if present.
- The script is idempotent (running multiple times is safe given the same flags).
"""
from __future__ import annotations

import argparse
import json
import os
import shutil
from typing import Any, Dict, List, Tuple

Grid = List[List[str]]


def load_json(path: str) -> Dict[str, Any]:
    if not os.path.exists(path):
        raise FileNotFoundError(f"Input JSON not found: {path}")
    with open(path, "r", encoding="utf-8") as f:
        data = json.load(f)
        if not isinstance(data, dict):
            raise ValueError("Root JSON must be an object/dict")
        return data


def save_json(path: str, data: Dict[str, Any]) -> None:
    os.makedirs(os.path.dirname(path) or ".", exist_ok=True)
    with open(path, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=4, ensure_ascii=False)


def is_valid_grid(grid: Any) -> Tuple[bool, int, int]:
    """Return (valid, rows, cols) for a potential grid structure."""
    if not isinstance(grid, list) or not grid:
        return False, 0, 0
    if not isinstance(grid[0], list):
        return False, 0, 0
    rows = len(grid)
    cols = len(grid[0]) if rows > 0 else 0
    if rows <= 0 or cols <= 0:
        return False, rows, cols
    # Ensure all rows have equal length and contain strings
    for r in grid:
        if not isinstance(r, list) or len(r) != cols:
            return False, rows, cols
        for ch in r:
            if not isinstance(ch, str) or len(ch) != 1:
                return False, rows, cols
    return True, rows, cols


def make_grid(size: int, fill: str) -> Grid:
    return [[fill for _ in range(size)] for _ in range(size)]


def normalize_entries(data: Dict[str, Any], size: int, fill: str, force: bool) -> Tuple[int, int]:
    """Normalize entries; return (changed_count, total_count)."""
    changed = 0
    total = 0
    for key, entry in list(data.items()):
        if not isinstance(entry, dict):
            continue
        total += 1
        grid = entry.get("collision")
        valid, rows, cols = is_valid_grid(grid)
        needs_replace = False
        if not valid:
            needs_replace = True
        else:
            if rows <= 1 or cols <= 1:
                needs_replace = True
            elif force:
                # Force-normalize every grid to NxN
                needs_replace = True

        if needs_replace:
            data[key] = {
                **entry,
                "collision": make_grid(size, fill),
                "width": size,
                "height": size,
            }
            changed += 1
        else:
            # Just ensure width/height match the actual grid
            entry["width"] = cols
            entry["height"] = rows
    return changed, total


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Normalize collision grids to an NxN default where needed.")
    parser.add_argument("--input", default=os.path.join("data", "buildings", "buildings_collisions_by_image.json"),
                        help="Path to input JSON (by image)")
    parser.add_argument("--output", default=os.path.join("data", "buildings", "buildings_collisions_by_image.json"),
                        help="Path to output JSON (can be same as input for in-place)")
    parser.add_argument("--size", type=int, default=15, help="Target grid size N (N x N)")
    parser.add_argument("--fill", choices=[".", "#"], default=".", help="Fill character for generated grids")
    parser.add_argument("--force", action="store_true", help="Replace even valid grids with NxN")
    parser.add_argument("--backup", action="store_true", help="If in-place, write <input>.bak before modifying")
    return parser.parse_args()


def main() -> None:
    args = parse_args()
    inp = os.path.abspath(args.input)
    out = os.path.abspath(args.output)

    data = load_json(inp)

    if args.backup and inp == out and os.path.exists(inp):
        bak = inp + ".bak"
        shutil.copy2(inp, bak)
        print(f"Backup written: {bak}")

    changed, total = normalize_entries(data, size=args.size, fill=args.fill, force=args.force)
    save_json(out, data)

    print(f"Normalized entries: {changed}/{total} -> size {args.size}x{args.size}, fill='{args.fill}', force={args.force}")
    if inp == out:
        print(f"Updated file in-place: {out}")
    else:
        print(f"Wrote output: {out}")


if __name__ == "__main__":
    main()

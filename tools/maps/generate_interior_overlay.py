#!/usr/bin/env python3
"""Generate a small building-interior overlay for StreamingAssets/Maps.

An interior is an ordinary overlay file: a JSON object whose only required key is
``layers``, mapping a TilemapLayer name to a row-major matrix of tile names, row 0 at the
TOP (the loader flips Y).  Nothing else in the schema is needed - the runtime exit trigger
is dropped on the arrival tile by ``WorldTransitionService``, so an interior carries no
components and needs no editor pass.

Written as a generator rather than by hand because the file is 100 % derived from four
numbers, and because "how was this room made" has to survive the first time somebody wants
a bigger one.  Tile names resolve as ``Resources/Tiles/<name>``; the bare names available at
that root are wall, floor, floor_1..floor_7, dungeon_floor and dungeon_tunnel.

Usage:
    python tools/maps/generate_interior_overlay.py                       # default room
    python tools/maps/generate_interior_overlay.py --width 20 --height 14 --name inn
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
MAPS_DIR = REPO_ROOT / "unity" / "Valkur" / "Assets" / "StreamingAssets" / "Maps"

# Interiors live in their own subfolder, NOT beside the zone overlays.  Everything
# directly under Maps/ is a 50x50 zone tile of the base world - WorldLoader composes
# them by offset, and RealShippedOverlayBoundsAndNamesTests asserts that size for every
# file it finds there.  An interior is a room of whatever size the room is, so putting
# one in that folder breaks an invariant that is worth keeping absolute.
INTERIORS_DIR = MAPS_DIR / "Interiors"

# The nine authored layers the loader accepts.  Only the ones we paint are emitted; an
# absent layer is simply not painted, which is what "leave alone" means here.
GROUND = "Ground"
COLLISION = "Collision"
FLOOR_DECALS = "FloorDecals"


def build_layers(width: int, height: int, floor_tile: str, wall_tile: str,
                 rug_tile: str) -> dict:
    """Room with a one-cell wall ring, a floor inside it, and a rug in the middle."""
    if width < 5 or height < 5:
        raise ValueError("A room smaller than 5x5 has no interior once it has walls.")

    ground = [[floor_tile for _ in range(width)] for _ in range(height)]

    # Collision holds the walls.  The loader gives any tile resolved for the Collision
    # layer a Grid collider, so painting the ring here is all the blocking that is needed.
    collision = [["" for _ in range(width)] for _ in range(height)]
    for x in range(width):
        collision[0][x] = wall_tile
        collision[height - 1][x] = wall_tile
    for y in range(height):
        collision[y][0] = wall_tile
        collision[y][width - 1] = wall_tile

    # A 2x2 rug so the room reads as a room rather than as a grey box, and so the arrival
    # tile has something under it that is visibly not the floor everywhere else.
    decals = [["" for _ in range(width)] for _ in range(height)]
    cx, cy = width // 2, height // 2
    for dy in (0, 1):
        for dx in (-1, 0):
            decals[cy + dy][cx + dx] = rug_tile

    return {GROUND: ground, COLLISION: collision, FLOOR_DECALS: decals}


def default_spawn(width: int, height: int) -> tuple[float, float]:
    """Centre of the room in world units.

    Row 0 of the matrix is the TOP and the loader flips Y, so a matrix ``height`` tall
    occupies Unity cells y in [0, height).  The centre of a cell is its integer corner plus
    a half, which is where the player has to land - the corner itself straddles four cells
    and reads as standing on a seam.
    """
    return (width / 2.0 + 0.5, height / 2.0 + 0.5)


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--name", default="house_interior_small",
                    help="output basename, without '.overlay.json'")
    ap.add_argument("--width", type=int, default=14)
    ap.add_argument("--height", type=int, default=10)
    ap.add_argument("--floor-tile", default="floor")
    ap.add_argument("--wall-tile", default="wall")
    ap.add_argument("--rug-tile", default="floor_5")
    args = ap.parse_args()

    layers = build_layers(args.width, args.height,
                          args.floor_tile, args.wall_tile, args.rug_tile)

    out = INTERIORS_DIR / f"{args.name}.overlay.json"
    INTERIORS_DIR.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps({"layers": layers}, indent=1), encoding="utf-8")

    sx, sy = default_spawn(args.width, args.height)
    print(f"Wrote {out}")
    print(f"  {args.width} x {args.height}, walls on the border.")
    print(f"  Suggested doorway spawn: {sx} {sy}")
    rel = out.relative_to(MAPS_DIR).as_posix()
    print(f"  Author a door with:  door {rel} {sx} {sy}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

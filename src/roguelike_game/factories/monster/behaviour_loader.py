from __future__ import annotations
import json
import math
from dataclasses import dataclass
from pathlib import Path
from typing import Dict, Any, List, Tuple, Optional

_DATA_DIR = Path(__file__).resolve().parents[4] / "data" / "entities" / "behaviour"
_CATALOG_PATH = _DATA_DIR / "patrols.json"

_catalog_cache: Optional[Dict[str, Any]] = None


def _load_catalog() -> Dict[str, Any]:
    global _catalog_cache
    if _catalog_cache is None:
        try:
            with open(_CATALOG_PATH, encoding="utf-8") as f:
                _catalog_cache = json.load(f)
        except FileNotFoundError:
            _catalog_cache = {"version": 1, "patrols": {}}
    return _catalog_cache


def _merge_params(defaults: Dict[str, Any], overrides: Optional[Dict[str, Any]]) -> Dict[str, Any]:
    merged = dict(defaults or {})
    if overrides:
        merged.update(overrides)
    return merged


def build_patrol_points(px: int, py: int, patrol_cfg: Optional[Dict[str, Any]], tile_size: int) -> List[Tuple[float, float]]:
    """
    Build a world-space waypoint list based on patrol configuration and catalog.
    patrol_cfg example: {"id": "circle", "params": {"radius_tiles": 5, "points": 16}}
    If patrol_cfg is None or unknown, falls back to a simple 2-point line along +X.
    """
    catalog = _load_catalog()
    patterns: Dict[str, Any] = catalog.get("patrols", {})

    # Default fallback: two points (current behavior)
    def default_line() -> List[Tuple[float, float]]:
        return [(px, py), (px + 5 * tile_size, py)]

    if not patrol_cfg or not isinstance(patrol_cfg, dict):
        return default_line()

    pid = patrol_cfg.get("id") or patrol_cfg.get("type")
    if not pid:
        return default_line()

    entry = patterns.get(str(pid))
    if not entry:
        return default_line()

    params = _merge_params(entry.get("default_params", {}), patrol_cfg.get("params"))
    # Normalize helper
    def tiles(v: float) -> float:
        return float(v) * float(tile_size)

    pid = str(pid).lower()

    if pid == "line":
        # axis: 'x'|'y'; length_tiles: int
        axis = str(params.get("axis", "x")).lower()
        length = float(params.get("length_tiles", 5))
        if axis == "y":
            return [(px, py), (px, py + tiles(length))]
        # default x
        return [(px, py), (px + tiles(length), py)]

    if pid == "ping_pong":
        # Like line but explicit; same as 2-point
        length = float(params.get("length_tiles", 5))
        axis = str(params.get("axis", "x")).lower()
        if axis == "y":
            return [(px, py), (px, py + tiles(length))]
        return [(px, py), (px + tiles(length), py)]

    if pid == "circle":
        radius = tiles(float(params.get("radius_tiles", 4)))
        points = int(params.get("points", 16))
        clockwise = bool(params.get("clockwise", True))
        out: List[Tuple[float, float]] = []
        rng = range(points - 1, -1, -1) if clockwise else range(points)
        for i in rng:
            th = (2.0 * math.pi * i) / points
            cx = px + radius * math.cos(th)
            cy = py + radius * math.sin(th)
            out.append((cx, cy))
        return out

    if pid == "square":
        width = tiles(float(params.get("width_tiles", 6)))
        height = tiles(float(params.get("height_tiles", 6)))
        per_edge = max(1, int(params.get("points_per_edge", 4)))
        # Build rectangle centered at (px,py)
        x0, y0 = px - width / 2.0, py - height / 2.0
        x1, y1 = px + width / 2.0, py + height / 2.0
        out: List[Tuple[float, float]] = []
        # top edge (x0->x1, y0)
        for i in range(per_edge):
            t = i / max(1, per_edge - 1)
            out.append((x0 + (x1 - x0) * t, y0))
        # right edge (x1, y0->y1)
        for i in range(1, per_edge):
            t = i / max(1, per_edge - 1)
            out.append((x1, y0 + (y1 - y0) * t))
        # bottom edge (x1->x0, y1)
        for i in range(1, per_edge):
            t = i / max(1, per_edge - 1)
            out.append((x1 - (x1 - x0) * t, y1))
        # left edge (x0, y1->y0)
        for i in range(1, per_edge - 1):
            t = i / max(1, per_edge - 1)
            out.append((x0, y1 - (y1 - y0) * t))
        return out

    if pid == "zigzag":
        # segments: number of corners horizontally; step_tiles: advance per segment; amplitude_tiles: vertical amplitude
        segments = max(1, int(params.get("segments", 6)))
        step = tiles(float(params.get("step_tiles", 3)))
        amp = tiles(float(params.get("amplitude_tiles", 2)))
        axis = str(params.get("axis", "x")).lower()
        out: List[Tuple[float, float]] = []
        if axis == "y":
            # vertical progression, zigzag horizontally
            for i in range(segments + 1):
                y = py + step * i
                x = px + (amp if i % 2 == 0 else -amp)
                out.append((x, y))
        else:
            # horizontal progression, zigzag vertically
            for i in range(segments + 1):
                x = px + step * i
                y = py + (amp if i % 2 == 0 else -amp)
                out.append((x, y))
        return out

    if pid == "figure_eight":
        # Two circles of radius r, centers separated by gap along X
        r = tiles(float(params.get("radius_tiles", 3)))
        pts = int(params.get("points_per_loop", 12))
        gap = tiles(float(params.get("gap_tiles", 2)))
        cx_left = px - (r + gap / 2.0)
        cx_right = px + (r + gap / 2.0)
        cy = py
        out: List[Tuple[float, float]] = []
        # left loop clockwise
        for i in range(pts):
            th = (2.0 * math.pi * (pts - 1 - i)) / pts
            out.append((cx_left + r * math.cos(th), cy + r * math.sin(th)))
        # right loop counter-clockwise
        for i in range(pts):
            th = (2.0 * math.pi * i) / pts
            out.append((cx_right + r * math.cos(th), cy + r * math.sin(th)))
        return out

    # Unknown pattern -> default
    return default_line()

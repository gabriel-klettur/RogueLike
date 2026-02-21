from __future__ import annotations

"""Spatial grid to cull lights outside the camera view efficiently."""

from dataclasses import dataclass, field
from typing import Dict, Iterable, List, Tuple

from .light_types import Light
from .culling import light_intersects_screen


@dataclass
class LightSpatialGrid:
    """Stores lights in a uniform grid to accelerate culling queries."""

    cell_size: int = 256
    rebuild_frequency: int = 30
    _grid: Dict[Tuple[int, int], List[Light]] = field(default_factory=dict, init=False)
    _dirty: bool = True
    _age: int = 0

    def mark_dirty(self) -> None:
        self._dirty = True

    def register_lights(self, lights: Iterable[Light]) -> None:
        """Rebuild the spatial buckets from the provided iterable of lights."""

        self._grid.clear()
        cs = self.cell_size
        for light in lights:
            try:
                cx = int(light.x) // cs
                cy = int(light.y) // cs
            except Exception:
                continue
            self._grid.setdefault((cx, cy), []).append(light)
        self._dirty = False
        self._age = 0

    def _ensure_fresh(self, lights: Iterable[Light]) -> None:
        if self._dirty or self._age >= self.rebuild_frequency:
            self.register_lights(lights)

    def collect_candidates(
        self,
        lights: Iterable[Light],
        camera,
        screen_size: Tuple[int, int],
        max_radius: float,
    ) -> List[Tuple[Light, int]]:
        """Return visible light candidates intersecting the camera view."""

        self._ensure_fresh(lights)
        self._age += 1

        width, height = screen_size
        zoom = float(getattr(camera, "zoom", 1.0))
        try:
            offset_x = float(getattr(camera, "offset_x", 0.0))
            offset_y = float(getattr(camera, "offset_y", 0.0))
        except Exception:
            offset_x = offset_y = 0.0

        view_w = width / zoom
        view_h = height / zoom
        padding = float(max_radius)
        wx0, wy0 = offset_x - padding, offset_y - padding
        wx1, wy1 = offset_x + view_w + padding, offset_y + view_h + padding

        cs = self.cell_size
        gx0, gy0 = int(wx0) // cs, int(wy0) // cs
        gx1, gy1 = int(wx1) // cs, int(wy1) // cs

        candidates: List[Tuple[Light, int]] = []
        visited_ids: set[int] = set()

        for gy in range(gy0, gy1 + 1):
            for gx in range(gx0, gx1 + 1):
                for light in self._grid.get((gx, gy), ()):  # type: ignore[arg-type]
                    object_id = id(light)
                    if object_id in visited_ids:
                        continue
                    visited_ids.add(object_id)
                    if not getattr(light, "enabled", True):
                        continue
                    screen_radius = int(light.radius * zoom)
                    if screen_radius <= 0:
                        continue
                    if not light_intersects_screen(
                        light.x,
                        light.y,
                        screen_radius,
                        camera,
                        screen_size,
                        zoom,
                    ):
                        continue
                    candidates.append((light, screen_radius))
        return candidates

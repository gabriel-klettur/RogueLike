from __future__ import annotations

"""Caching helpers for tinted radial gradients used by the lighting system."""

from collections import OrderedDict
from typing import Tuple

import pygame

from .gradients import get_radial_gradient


class TintedGradientCache:
    """Cache low-resolution tinted gradients to avoid repeated allocations."""

    def __init__(self, max_items: int = 64) -> None:
        self._store: "OrderedDict[Tuple[int, int, int, int, int, int], pygame.Surface]" = OrderedDict()
        self._max_items = max_items

    def clear(self) -> None:
        """Drop all cached surfaces."""

        self._store.clear()

    def fetch(
        self,
        radius_px: int,
        falloff: float,
        center_scale: float,
        color: Tuple[int, int, int],
        intensity: float,
    ) -> pygame.Surface:
        """Return a tinted gradient surface for the requested parameters.

        Parameters
        ----------
        radius_px:
            Radius expressed in pixels in the low-resolution buffer.
        falloff:
            Controls how fast the light fades to black.
        center_scale:
            Multiplier applied to the center pixel brightness.
        color:
            Base RGB color of the light.
        intensity:
            Normalized intensity in the range ``[0.0, 1.0]``.
        """

        radius_bucket = max(1, int(radius_px))
        falloff_bucket = int(round(falloff * 100))
        center_bucket = int(round(center_scale * 100))

        clamped_intensity = max(0.0, min(1.0, intensity))
        intensity_bucket = int(clamped_intensity * 15 + 0.5)

        r = min(255, int(color[0] * (intensity_bucket / 15.0)))
        g = min(255, int(color[1] * (intensity_bucket / 15.0)))
        b = min(255, int(color[2] * (intensity_bucket / 15.0)))

        key = (radius_bucket, falloff_bucket, center_bucket, r, g, b)
        cached = self._store.get(key)
        if cached is not None:
            return cached

        base = get_radial_gradient(radius_bucket, falloff, center_scale)
        tinted = base.copy()
        tinted.fill((r, g, b, 255), special_flags=pygame.BLEND_RGBA_MULT)

        self._store[key] = tinted
        if len(self._store) > self._max_items:
            self._store.popitem(last=False)
        return tinted

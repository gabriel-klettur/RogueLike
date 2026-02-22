"""Utilities to cache Pygame circle masks for projectile sampling."""
from __future__ import annotations

from typing import Dict, Tuple

import pygame


class CircleMaskCache:
    """Caches circle masks keyed by integer radius.

    Re-using masks avoids creating intermediate surfaces every frame.
    """

    def __init__(self) -> None:
        self._cache: Dict[int, pygame.mask.Mask] = {}

    def get(self, radius: float) -> Tuple[pygame.mask.Mask, int]:
        """Return a circle mask and its radius metadata.

        Args:
            radius: Requested radius in pixels.

        Returns:
            A tuple ``(mask, radius_int)`` where ``mask`` is a cached
            ``pygame.mask.Mask`` describing a filled circle and ``radius_int`` is
            the integer radius used to build the mask. Radii are rounded to the
            nearest positive integer.
        """
        r_int = max(1, int(round(radius)))
        mask = self._cache.get(r_int)
        if mask is None:
            surface = pygame.Surface((2 * r_int + 1, 2 * r_int + 1), pygame.SRCALPHA)
            pygame.draw.circle(surface, (255, 255, 255, 255), (r_int, r_int), r_int)
            mask = pygame.mask.from_surface(surface)
            self._cache[r_int] = mask
        return mask, r_int

    def clear(self) -> None:
        """Empty the cache (useful while testing)."""
        self._cache.clear()

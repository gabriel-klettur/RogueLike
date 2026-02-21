"""Surface scaling cache used across chunk generation paths."""
from __future__ import annotations

from typing import Dict, Tuple

import pygame

from roguelike_engine.map.view.constants import MAX_SURFACE_DIM


class SpriteScaler:
    """Cache sprite scaling results to avoid repeated transforms."""

    def __init__(self) -> None:
        self._scaled_cache: Dict[Tuple[int, float], pygame.Surface] = {}

    def clear(self) -> None:
        """Remove every cached surface."""
        self._scaled_cache.clear()

    def scaled(self, sprite: pygame.Surface, zoom: float) -> pygame.Surface:
        """Return a scaled copy clamped to ``MAX_SURFACE_DIM``."""
        key = (id(sprite), float(zoom))
        cached = self._scaled_cache.get(key)
        if cached is not None:
            return cached

        width, height = sprite.get_size()
        target_w = self._scale_dimension(width, zoom)
        target_h = self._scale_dimension(height, zoom)
        try:
            scaled = pygame.transform.scale(sprite, (target_w, target_h))
        except Exception:  # pragma: no cover - pygame defensive fallback
            scaled = sprite
        self._scaled_cache[key] = scaled
        return scaled

    @staticmethod
    def _scale_dimension(size: int, zoom: float) -> int:
        scaled = int(round(size * zoom))
        if scaled < 1:
            return 1
        if scaled > MAX_SURFACE_DIM:
            return MAX_SURFACE_DIM
        return scaled

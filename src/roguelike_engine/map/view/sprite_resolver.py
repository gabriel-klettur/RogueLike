"""Helpers to resolve tile sprites with caching and overlay rules."""
from __future__ import annotations

from typing import Dict, Optional, Tuple

import pygame

from roguelike_engine.config.config_tiles import OVERLAY_CODE_MAP
from roguelike_engine.map.model.layer import Layer
from roguelike_engine.tile.utils.loader import get_sprite_for_tile

SpriteKey = Tuple[str, Optional[str]]


class SpriteResolver:
    """Lazily resolve sprites respecting overlay fallback rules."""

    def __init__(self, overlay_only: bool) -> None:
        self._overlay_only = overlay_only
        self._cache: Dict[SpriteKey, Optional[pygame.Surface]] = {}
        self.valid_overlay_codes = set(OVERLAY_CODE_MAP)

    def clear(self) -> None:
        """Drop cached surfaces. Useful when invalidating the entire map cache."""
        self._cache.clear()

    def should_draw(self, layer: Layer, code: Optional[str]) -> bool:
        if not code:
            if self._overlay_only:
                return False
            return layer == Layer.Ground

        if self._overlay_only and layer == Layer.Ground and code not in self.valid_overlay_codes:
            return False

        return True

    def resolve(self, char: str, code: Optional[str], layer: Layer) -> Optional[pygame.Surface]:
        key = (char, code)
        if key not in self._cache:
            self._cache[key] = self._load_sprite(char, code, layer)
        return self._cache[key]

    def _load_sprite(
        self,
        char: str,
        code: Optional[str],
        layer: Layer,
    ) -> Optional[pygame.Surface]:
        if not self.should_draw(layer, code):
            return None
        return get_sprite_for_tile(char, code)

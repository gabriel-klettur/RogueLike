from __future__ import annotations

"""Utility for managing the low-resolution light buffer."""

from dataclasses import dataclass, field
from typing import Optional, Tuple

import pygame

from .lighting_config import LightingSettings
from .occlusion_tiles import build_occlusion_mask


@dataclass
class LightSurfaceBuffer:
    """Handle allocation and scaling of the low-resolution lightmap."""

    settings: LightingSettings
    _surface: Optional[pygame.Surface] = field(default=None, init=False)
    _size: Optional[Tuple[int, int]] = field(default=None, init=False)
    _scale: int = field(default=1, init=False)

    def ensure(self, screen_size: Tuple[int, int]) -> pygame.Surface:
        """Ensure the backing surface matches the requested screen size."""

        required_scale = max(1, self.settings.low_res_scale)
        width, height = screen_size
        lw = max(1, width // required_scale)
        lh = max(1, height // required_scale)

        if (
            self._surface is None
            or self._size != (lw, lh)
            or self._scale != required_scale
        ):
            self._surface = pygame.Surface((lw, lh), flags=pygame.SRCALPHA)
            self._size = (lw, lh)
            self._scale = required_scale
        return self._surface

    @property
    def scale(self) -> int:
        return self._scale

    @property
    def size(self) -> Optional[Tuple[int, int]]:
        return self._size

    def fill_black(self) -> None:
        if self._surface is not None:
            self._surface.fill((0, 0, 0, 255))

    def clear(self) -> None:
        self._surface = None
        self._size = None

    def scaled(self, screen_size: Tuple[int, int]) -> Optional[pygame.Surface]:
        if self._surface is None:
            return None
        scale = self._scale
        lw, lh = self._size or self._surface.get_size()
        target_w, target_h = screen_size
        if (lw * scale, lh * scale) == (target_w, target_h):
            if scale == 1:
                return self._surface
            return pygame.transform.scale(self._surface, (target_w, target_h))
        if self.settings.tier == "lights_high":
            return pygame.transform.smoothscale(self._surface, (target_w, target_h))
        return pygame.transform.scale(self._surface, (target_w, target_h))

    def apply_occlusion(self, screen_size: Tuple[int, int], camera, map_manager) -> None:
        if self._surface is None or map_manager is None:
            return
        try:
            occlusion = build_occlusion_mask(screen_size, camera, map_manager)
            lw, lh = self._surface.get_size()
            if occlusion.get_size() != (lw, lh):
                try:
                    occlusion = pygame.transform.smoothscale(occlusion, (lw, lh))
                except Exception:
                    occlusion = pygame.transform.scale(occlusion, (lw, lh))
            self._surface.blit(occlusion, (0, 0), special_flags=pygame.BLEND_RGBA_MULT)
        except Exception:
            return

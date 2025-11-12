from __future__ import annotations

"""Render point lights onto a low-resolution lightmap surface."""

from typing import Iterable, Tuple

import pygame

from .light_cache import TintedGradientCache
from .light_types import Light
from .lighting_config import LightingSettings
from .shadows_poly import build_visibility_mask_lowres


class LightRenderer:
    """Render visible lights with cached radial gradients."""

    def __init__(self, settings: LightingSettings) -> None:
        self.settings = settings
        self._cache = TintedGradientCache(max_items=64)
        self._compose_mode: str = "add"

    def set_compose_mode(self, mode: str) -> None:
        self._compose_mode = "max" if str(mode or "add").lower().startswith("max") else "add"

    def clear_cache(self) -> None:
        self._cache.clear()

    def draw(
        self,
        surface: pygame.Surface,
        candidates: Iterable[Tuple[Light, int]],
        camera,
        map_manager,
    ) -> None:
        scale = max(1, self.settings.low_res_scale)
        lw, lh = surface.get_size()
        hero_limit = 0
        shadow_rays = 0
        if map_manager is not None and self.settings.shadow_polygons_enabled():
            hero_limit = self.settings.shadow_hero_count()
            shadow_rays = self.settings.shadow_rays()
        heroes_used = 0

        for light, screen_radius in candidates:
            radius_lr = max(1, screen_radius // scale)
            if radius_lr <= 0:
                continue
            intensity = max(0.0, min(1.0, light.current_intensity()))
            tint = self._cache.fetch(
                radius_px=radius_lr,
                falloff=float(light.falloff),
                center_scale=float(getattr(light, "center_scale", 1.0)),
                color=light.color,
                intensity=intensity,
            )

            sx, sy = camera.apply((light.x, light.y))
            sx /= scale
            sy /= scale
            blit_pos = (int(sx - radius_lr), int(sy - radius_lr))

            tint_to_blit = tint
            if hero_limit > 0 and heroes_used < hero_limit:
                try:
                    visibility = build_visibility_mask_lowres(
                        lw,
                        lh,
                        scale,
                        camera,
                        map_manager,
                        (light.x, light.y),
                        light.radius,
                        rays=shadow_rays,
                    )
                    local_copy = tint.copy()
                    local_copy.blit(
                        visibility,
                        (-blit_pos[0], -blit_pos[1]),
                        special_flags=pygame.BLEND_RGBA_MULT,
                    )
                    tint_to_blit = local_copy
                    heroes_used += 1
                except Exception:
                    tint_to_blit = tint

            try:
                if self._compose_mode == "add":
                    surface.blit(tint_to_blit, blit_pos, special_flags=pygame.BLEND_RGBA_ADD)
                else:
                    surface.blit(tint_to_blit, blit_pos, special_flags=pygame.BLEND_RGBA_MAX)
            except Exception:
                continue

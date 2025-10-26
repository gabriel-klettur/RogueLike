from __future__ import annotations

from typing import List, Tuple, Optional
import pygame
import time

from .light_types import Light
from .gradients import get_radial_gradient
from .quality import (
    load_quality_config,
    get_low_res_scale,
    get_max_lights,
    get_max_radius,
    get_quality_tier,
)
from .culling import light_intersects_screen


class LightingManager:
    """Manages point lights, composes a low-res additive lightmap, and applies it to the screen.

    Design:
    - Keeps a low-resolution lightmap Surface for performance.
    - Culls lights outside the camera view.
    - Uses cached radial gradients and tints them per-light before additive blit.
    - Integrates with config/quality tiers (ambient vs. lights_low/high).
    """

    def __init__(self, config_path: str = "data/config/lighting.json") -> None:
        self.enabled: bool = True
        self._cfg = load_quality_config()
        self._tier = get_quality_tier(self._cfg)
        self._scale: int = get_low_res_scale(self._cfg)
        self._max_lights: int = get_max_lights(self._cfg)
        self._max_radius: int = get_max_radius(self._cfg)
        self._lr_surface: Optional[pygame.Surface] = None
        self._lr_size: Optional[Tuple[int, int]] = None
        self._last_scale: int = self._scale
        self._lights: List[Light] = []

    # ---- Public API --------------------------------------------------------
    def clear(self) -> None:
        self._lights.clear()

    def add(self, light: Light) -> None:
        if light.radius > self._max_radius:
            light.radius = self._max_radius
        self._lights.append(light)

    def remove_by_id(self, lid: str) -> None:
        self._lights = [l for l in self._lights if l.id != lid]

    def set_enabled(self, v: bool) -> None:
        self.enabled = bool(v)

    def clear_debug_lights(self) -> None:
        """Remove lights that are not synced from ECS.

        ECS-synced lights are tagged with id starting with 'ecs:'. Debug/runtime
        lights typically have id=None or some other tag; we keep only ECS ones.
        """
        kept: list[Light] = []
        for l in self._lights:
            try:
                if isinstance(l.id, str) and l.id.startswith("ecs:"):
                    kept.append(l)
            except Exception:
                # If id access fails, treat as debug and drop it
                continue
        self._lights = kept

    def set_quality(self, tier: str) -> None:
        self._tier = tier

    def should_render(self) -> bool:
        if not self.enabled:
            return False
        return self._tier in ("lights_low", "lights_high")

    def compose_lightmap(self, screen_size: Tuple[int, int], camera) -> Optional[pygame.Surface]:
        """Build and return the low-res lightmap Surface (black + additive lights).
        Returns None if disabled or no visible lights.
        """
        if not self.should_render():
            return None
        w, h = screen_size
        scale = max(1, int(self._scale))
        lw = max(1, w // scale)
        lh = max(1, h // scale)
        if self._lr_surface is None or self._lr_size != (lw, lh) or self._last_scale != scale:
            self._lr_surface = pygame.Surface((lw, lh), flags=pygame.SRCALPHA)
            self._lr_size = (lw, lh)
            self._last_scale = scale
        # Clear to black (no light)
        self._lr_surface.fill((0, 0, 0, 255))
        # Prepare screen-space values
        z = float(getattr(camera, "zoom", 1.0))
        # Compose lights with culling and limits
        visible = 0
        for lt in self._lights:
            if not lt.enabled:
                continue
            sr = int(lt.radius * z)
            if sr <= 0:
                continue
            if not light_intersects_screen(lt.x, lt.y, sr, camera, (w, h), z):
                continue
            # Enforce visible lights limit
            visible += 1
            if visible > self._max_lights:
                break
            # Build tinted gradient in low-res scale
            rr = max(1, sr // scale)
            base = get_radial_gradient(rr, lt.falloff)
            # Tint by light color and intensity
            tint = base.copy()
            ci = max(0.0, min(1.0, lt.current_intensity()))
            r = min(255, int(lt.color[0] * ci))
            g = min(255, int(lt.color[1] * ci))
            b = min(255, int(lt.color[2] * ci))
            tint.fill((r, g, b, 255), special_flags=pygame.BLEND_RGBA_MULT)
            # Compute top-left on low-res surface
            sx, sy = camera.apply((lt.x, lt.y))
            sx //= scale
            sy //= scale
            blit_pos = (int(sx - rr), int(sy - rr))
            try:
                self._lr_surface.blit(tint, blit_pos, special_flags=pygame.BLEND_RGBA_ADD)
            except Exception:
                # Be robust against out-of-bounds blits
                pass
        return self._lr_surface

    def get_scaled(self, screen_size: Tuple[int, int]) -> Optional[pygame.Surface]:
        if self._lr_surface is None:
            return None
        scale = self._last_scale or 1
        lw, lh = self._lr_size or self._lr_surface.get_size()
        w, h = screen_size
        if (lw * scale, lh * scale) == (w, h):
            return self._lr_surface if scale == 1 else pygame.transform.scale(self._lr_surface, (w, h))
        # Choose scaling method by tier
        if self._tier == "lights_high":
            return pygame.transform.smoothscale(self._lr_surface, (w, h))
        return pygame.transform.scale(self._lr_surface, (w, h))


_GLOBAL_LM: Optional[LightingManager] = None


def get_global_lighting() -> LightingManager:
    global _GLOBAL_LM
    if _GLOBAL_LM is None:
        _GLOBAL_LM = LightingManager()
    return _GLOBAL_LM

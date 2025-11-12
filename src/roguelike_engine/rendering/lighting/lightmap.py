from __future__ import annotations

import time
from typing import Optional, Tuple

import pygame

from .light_autoscaler import AutoScaleTrend, LightAutoscaler
from .light_grid import LightSpatialGrid
from .light_renderer import LightRenderer
from .light_surface import LightSurfaceBuffer
from .light_types import Light
from .lighting_config import LightingSettings
from .lighting_stagger import StaggerScheduler


class LightingManager:
    """Compose a low-resolution lightmap using point-light descriptors."""

    def __init__(self, config_path: str = "data/config/lighting.json") -> None:
        self.enabled: bool = True
        self.settings = LightingSettings(config_path=config_path)

        self._lights: list[Light] = []
        self._compose_mode: str = "add"

        self._autoscaler = LightAutoscaler(budget_ms=2.0)
        self._grid = LightSpatialGrid(cell_size=256)
        self._stagger = StaggerScheduler(interval_ms=3000)
        self._buffer = LightSurfaceBuffer(settings=self.settings)
        self._renderer = LightRenderer(self.settings)

    # ------------------------------------------------------------------
    # Public API
    def clear(self) -> None:
        self._lights.clear()
        self._grid.mark_dirty()
        self._stagger.reset()

    def add(self, light: Light) -> None:
        if light.radius > self.settings.max_radius:
            light.radius = self.settings.max_radius
        self._lights.append(light)
        self._grid.mark_dirty()
        if isinstance(getattr(light, "id", None), str) and light.id.startswith("persist:"):
            self._stagger.reset()

    def remove_by_id(self, lid: str) -> None:
        original = len(self._lights)
        self._lights = [light for light in self._lights if light.id != lid]
        if len(self._lights) != original:
            self._grid.mark_dirty()
            self._stagger.reset()

    def set_enabled(self, value: bool) -> None:
        self.enabled = bool(value)

    def clear_debug_lights(self) -> None:
        self._lights = [
            light
            for light in self._lights
            if isinstance(getattr(light, "id", None), str)
            and (light.id.startswith("ecs:") or light.id.startswith("persist:"))
        ]
        self._grid.mark_dirty()

    def set_quality(self, tier: str) -> None:
        self.settings.tier = tier

    def set_compose_mode(self, mode: str) -> None:
        self._compose_mode = "max" if str(mode or "add").lower().startswith("max") else "add"
        self._renderer.set_compose_mode(self._compose_mode)

    def set_tile_occlusion(self, enabled: bool) -> None:
        self.settings.set_tile_occlusion(enabled)

    def tile_occlusion_enabled(self) -> bool:
        return self.settings.tile_occlusion_enabled()

    def set_shadow_polygons(self, enabled: bool) -> None:
        self.settings.set_shadow_polygons(enabled)

    def shadow_polygons_enabled(self) -> bool:
        return self.settings.shadow_polygons_enabled()

    def get_shadow_hero_count(self) -> int:
        return self.settings.shadow_hero_count()

    def get_shadow_rays(self) -> int:
        return self.settings.shadow_rays()

    def set_low_res_scale(self, scale: int) -> None:
        old_scale = self.settings.low_res_scale
        new_scale = self.settings.set_low_res_scale(scale)
        if new_scale != old_scale:
            self._grid.mark_dirty()
            self._buffer.clear()
            self._renderer.clear_cache()

    def set_max_lights(self, count: int) -> None:
        self.settings.set_max_lights(count)

    def set_max_radius(self, radius: int) -> None:
        old_radius = self.settings.max_radius
        new_radius = self.settings.set_max_radius(radius)
        if new_radius != old_radius:
            self._grid.mark_dirty()
            self._renderer.clear_cache()

    def set_shadow_hero_count(self, value: int) -> None:
        self.settings.set_shadow_hero_count(value)

    def set_shadow_rays(self, value: int) -> None:
        self.settings.set_shadow_rays(value)

    def current_low_res_scale(self) -> int:
        return self.settings.low_res_scale

    def current_max_lights(self) -> int:
        return self.settings.max_lights

    def current_max_radius(self) -> int:
        return self.settings.max_radius

    def should_render(self) -> bool:
        return self.enabled and self.settings.should_render()

    # ------------------------------------------------------------------
    # Rendering pipeline
    def compose_lightmap(
        self,
        screen_size: Tuple[int, int],
        camera,
        map_manager=None,
    ) -> Optional[pygame.Surface]:
        if not self.should_render():
            return None

        self._load_persistent_lights()
        if not self._sync_daynight_state():
            return None

        surface = self._buffer.ensure(screen_size)
        self._buffer.fill_black()

        self._update_stagger_targets()
        self._stagger.tick()

        start = time.perf_counter()
        candidates = self._grid.collect_candidates(
            self._lights,
            camera,
            screen_size,
            max_radius=self.settings.max_radius,
        )
        if candidates:
            candidates.sort(key=lambda entry: entry[1], reverse=True)
            max_lights = self.settings.max_lights
            if len(candidates) > max_lights:
                candidates = candidates[:max_lights]
            self._renderer.draw(surface, candidates, camera, map_manager)

        trend = self._autoscaler.record((time.perf_counter() - start) * 1000.0)
        if trend is not None:
            self._handle_autoscale_feedback(trend)

        if map_manager and self.settings.tile_occlusion_enabled():
            self._buffer.apply_occlusion(screen_size, camera, map_manager)
        return surface

    def get_scaled(self, screen_size: Tuple[int, int]) -> Optional[pygame.Surface]:
        return self._buffer.scaled(screen_size)

    # ------------------------------------------------------------------
    # Internal helpers
    def _handle_autoscale_feedback(self, trend: AutoScaleTrend) -> None:
        if trend is AutoScaleTrend.TOO_SLOW:
            if self.settings.low_res_scale < 8:
                self.set_low_res_scale(self.settings.low_res_scale + 1)
            elif self.settings.max_lights > 4:
                self.set_max_lights(self.settings.max_lights - 2)
        elif trend is AutoScaleTrend.TOO_FAST:
            if self.settings.max_lights < 256:
                self.set_max_lights(self.settings.max_lights + 1)
            elif self.settings.low_res_scale > 1:
                self.set_low_res_scale(self.settings.low_res_scale - 1)

    def _load_persistent_lights(self) -> None:
        try:
            from .light_instances_loader import load_persistent_to_manager

            load_persistent_to_manager(self)
        except Exception:
            pass

    def _sync_daynight_state(self) -> bool:
        try:
            from .daynight import get_global_daynight

            daynight = get_global_daynight()
        except Exception:
            if self._stagger.needs_population():
                self._stagger.populate(self._lights, order_desc=False)
            return True

        if daynight.is_lights_disabled_now():
            self._disable_persistent_lights()
            self._stagger.reset()
            return False

        self._stagger.configure(int(daynight.get_lights_stagger_interval_ms()))
        if self._stagger.needs_population():
            order_desc = daynight.get_lights_stagger_order() == "desc"
            self._stagger.populate(self._lights, order_desc)
            # When night is already active we want persistent lights on immediately.
            self._stagger.force_enable_all()
        return True

    def _disable_persistent_lights(self) -> None:
        for light in self._lights:
            try:
                if isinstance(getattr(light, "id", None), str) and light.id.startswith("persist:"):
                    light.enabled = False
            except Exception:
                continue

    def _update_stagger_targets(self) -> None:
        if self._stagger.needs_population():
            self._stagger.populate(self._lights, order_desc=False)


_GLOBAL_LM: Optional[LightingManager] = None


def get_global_lighting() -> LightingManager:
    global _GLOBAL_LM
    if _GLOBAL_LM is None:
        _GLOBAL_LM = LightingManager()
    return _GLOBAL_LM

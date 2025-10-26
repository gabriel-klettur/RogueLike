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
from .occlusion_tiles import build_occlusion_mask
from .shadows_poly import build_visibility_mask_lowres


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

    def set_tile_occlusion(self, enabled: bool) -> None:
        try:
            self._cfg["tile_occlusion"] = bool(enabled)
        except Exception:
            pass

    def tile_occlusion_enabled(self) -> bool:
        try:
            return bool(self._cfg.get("tile_occlusion", False))
        except Exception:
            return False

    # --- Shadow polygons (hero lights) toggles --------------------------------
    def set_shadow_polygons(self, enabled: bool) -> None:
        try:
            self._cfg["shadow_polygons"] = bool(enabled)
        except Exception:
            pass

    def shadow_polygons_enabled(self) -> bool:
        try:
            return bool(self._cfg.get("shadow_polygons", False))
        except Exception:
            return False

    def get_shadow_hero_count(self) -> int:
        try:
            v = int(self._cfg.get("shadow_hero_count", 1))
            return max(0, min(2, v))
        except Exception:
            return 1

    def get_shadow_rays(self) -> int:
        try:
            v = int(self._cfg.get("shadow_rays", 64))
            return max(8, min(256, v))
        except Exception:
            return 64

    # --- Live tunables (quality/limits) --------------------------------------
    def set_low_res_scale(self, scale: int) -> None:
        try:
            s = max(1, int(scale))
            self._cfg["low_res_scale"] = s
            self._scale = s
            # Force reallocation next compose
            self._last_scale = 0
            self._lr_surface = None
            self._lr_size = None
        except Exception:
            pass

    def set_max_lights(self, n: int) -> None:
        try:
            v = max(0, int(n))
            self._cfg["max_lights_visible"] = v
            self._max_lights = v
        except Exception:
            pass

    def set_max_radius(self, r: int) -> None:
        try:
            v = max(16, int(r))
            self._cfg["max_radius"] = v
            self._max_radius = v
        except Exception:
            pass

    def set_shadow_hero_count(self, n: int) -> None:
        try:
            v = max(0, min(2, int(n)))
            self._cfg["shadow_hero_count"] = v
        except Exception:
            pass

    def set_shadow_rays(self, n: int) -> None:
        try:
            v = max(8, min(256, int(n)))
            self._cfg["shadow_rays"] = v
        except Exception:
            pass

    # --- Read current values --------------------------------------------------
    def current_low_res_scale(self) -> int:
        return int(self._scale)

    def current_max_lights(self) -> int:
        return int(self._max_lights)

    def current_max_radius(self) -> int:
        return int(self._max_radius)

    def should_render(self) -> bool:
        if not self.enabled:
            return False
        return self._tier in ("lights_low", "lights_high")

    def compose_lightmap(self, screen_size: Tuple[int, int], camera, map_manager=None) -> Optional[pygame.Surface]:
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
        heroes_used = 0
        shadow_on = bool(self._cfg.get("shadow_polygons", False))
        hero_limit = self.get_shadow_hero_count() if shadow_on else 0
        shadow_rays = self.get_shadow_rays() if shadow_on else 0
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
            # Optional per-light polygon shadow: mask the tinted gradient by visibility
            if hero_limit > 0 and heroes_used < hero_limit and map_manager is not None:
                try:
                    vis = build_visibility_mask_lowres(
                        lw, lh, scale, camera, map_manager, (lt.x, lt.y), lt.radius, rays=shadow_rays
                    )
                    # Crop visibility to the tint area and multiply
                    crop = pygame.Rect(max(0, blit_pos[0]), max(0, blit_pos[1]), base.get_width(), base.get_height())
                    # Adjust crop if partially outside
                    if crop.right > lw:
                        crop.width = max(0, lw - crop.left)
                    if crop.bottom > lh:
                        crop.height = max(0, lh - crop.top)
                    if crop.width > 0 and crop.height > 0:
                        sub = vis.subsurface(crop).copy()
                        # Compute offset into tint when blit_pos is negative
                        off_x = max(0, -blit_pos[0])
                        off_y = max(0, -blit_pos[1])
                        tint_cropped = tint.subsurface((off_x, off_y, crop.width, crop.height)).copy()
                        tint_cropped.blit(sub, (0, 0), special_flags=pygame.BLEND_RGBA_MULT)
                        # Replace tint with cropped masked version and adjust blit position
                        tint = tint_cropped
                        blit_pos = (crop.left, crop.top)
                        heroes_used += 1
                except Exception:
                    # If anything fails, fallback to unmasked
                    pass
            try:
                self._lr_surface.blit(tint, blit_pos, special_flags=pygame.BLEND_RGBA_ADD)
            except Exception:
                # Be robust against out-of-bounds blits
                pass
        # Optional: tile occlusion mask (attenuate light behind solid tiles)
        try:
            if bool(self._cfg.get("tile_occlusion", False)) and map_manager is not None:
                occl = build_occlusion_mask(screen_size, camera, map_manager)
                # Downscale occlusion to low-res
                scale = self._last_scale or 1
                lw, lh = self._lr_size or self._lr_surface.get_size()
                if occl.get_size() != (lw, lh):
                    try:
                        occl_lr = pygame.transform.smoothscale(occl, (lw, lh))
                    except Exception:
                        occl_lr = pygame.transform.scale(occl, (lw, lh))
                else:
                    occl_lr = occl
                # Multiply: black blocks light, white keeps light
                self._lr_surface.blit(occl_lr, (0, 0), special_flags=pygame.BLEND_RGBA_MULT)
        except Exception:
            # Be robust; occlusion is optional
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

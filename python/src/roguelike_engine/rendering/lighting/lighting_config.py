from __future__ import annotations

"""Utilities to centralize lighting quality configuration access."""

from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, Dict

from .quality import (
    get_low_res_scale,
    get_max_lights,
    get_max_radius,
    get_quality_tier,
    load_quality_config,
)


@dataclass
class LightingSettings:
    """Wrap lighting quality configuration with safe accessors.

    The configuration object is stored in ``raw`` so that other systems can
    inspect or serialize it if needed. Attribute setters clamp the provided
    values to stay within reasonable bounds and keep the original code's
    defensive behaviour.
    """

    config_path: str = "data/config/lighting.json"
    raw: Dict[str, Any] = field(init=False)
    _tier: str = field(init=False)
    _low_res_scale: int = field(init=False)
    _max_lights: int = field(init=False)
    _max_radius: int = field(init=False)

    def __post_init__(self) -> None:
        config_file = Path(self.config_path)
        self.raw = load_quality_config(config_file)
        self._tier = get_quality_tier(self.raw)
        self._low_res_scale = get_low_res_scale(self.raw)
        self._max_lights = get_max_lights(self.raw)
        self._max_radius = get_max_radius(self.raw)

    # ------------------------------------------------------------------
    # Tier and toggles
    @property
    def tier(self) -> str:
        return self._tier

    @tier.setter
    def tier(self, value: str) -> None:
        self._tier = value

    def should_render(self) -> bool:
        return self._tier in ("lights_low", "lights_high")

    # ------------------------------------------------------------------
    # Resolution and budget limits
    @property
    def low_res_scale(self) -> int:
        return self._low_res_scale

    def set_low_res_scale(self, scale: int) -> int:
        value = max(1, min(8, int(scale)))
        self.raw["low_res_scale"] = value
        self._low_res_scale = value
        return value

    @property
    def max_lights(self) -> int:
        return self._max_lights

    def set_max_lights(self, count: int) -> int:
        value = max(0, min(256, int(count)))
        self.raw["max_lights_visible"] = value
        self._max_lights = value
        return value

    @property
    def max_radius(self) -> int:
        return self._max_radius

    def set_max_radius(self, radius: int) -> int:
        value = max(16, min(2048, int(radius)))
        self.raw["max_radius"] = value
        self._max_radius = value
        return value

    # ------------------------------------------------------------------
    # Tile occlusion
    def tile_occlusion_enabled(self) -> bool:
        return bool(self.raw.get("tile_occlusion", False))

    def set_tile_occlusion(self, enabled: bool) -> None:
        self.raw["tile_occlusion"] = bool(enabled)

    # ------------------------------------------------------------------
    # Shadow polygons
    def shadow_polygons_enabled(self) -> bool:
        return bool(self.raw.get("shadow_polygons", False))

    def set_shadow_polygons(self, enabled: bool) -> None:
        self.raw["shadow_polygons"] = bool(enabled)

    def shadow_hero_count(self) -> int:
        value = int(self.raw.get("shadow_hero_count", 1))
        return max(0, min(2, value))

    def set_shadow_hero_count(self, value: int) -> None:
        clamped = max(0, min(2, int(value)))
        self.raw["shadow_hero_count"] = clamped

    def shadow_rays(self) -> int:
        value = int(self.raw.get("shadow_rays", 64))
        return max(8, min(256, value))

    def set_shadow_rays(self, value: int) -> None:
        clamped = max(8, min(256, int(value)))
        self.raw["shadow_rays"] = clamped

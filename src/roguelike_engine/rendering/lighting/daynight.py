from __future__ import annotations

import json
from pathlib import Path
from typing import List, Tuple, Optional
import pygame

_DEFAULT_CONFIG_PATH = Path("data/config/lighting.json")


def _clamp(x: float, a: float = 0.0, b: float = 1.0) -> float:
    return a if x < a else b if x > b else x


def _lerp(a: float, b: float, t: float) -> float:
    return a + (b - a) * t


def _lerp_color(a: Tuple[int, int, int], b: Tuple[int, int, int], t: float) -> Tuple[int, int, int]:
    return (
        int(_lerp(a[0], b[0], t)),
        int(_lerp(a[1], b[1], t)),
        int(_lerp(a[2], b[2], t)),
    )


class DayNightSystem:
    """Controls ambient light color/intensity across the day using keyframes.

    - Loads config from data/config/lighting.json
    - Builds a 1440-minute LUT for O(1) queries
    - Generates a cached overlay Surface to multiply with the screen
    """

    def __init__(self, config_path: Path | None = None) -> None:
        self.config_path = config_path or _DEFAULT_CONFIG_PATH
        self.enabled: bool = True
        self.ambient_only: bool = True
        self.time_scale_minutes_per_second: float = 120.0  # 1s real = 2m game
        self._keyframes: List[Tuple[int, float, Tuple[int, int, int]]] = []
        self._lut: List[Tuple[float, Tuple[int, int, int]]] = [(1.0, (255, 255, 255))] * 1440
        self._start_ticks: int = pygame.time.get_ticks()
        self._last_overlay_rebuild_ticks: int = 0
        self._overlay_cache: Optional[pygame.Surface] = None
        self._overlay_color_cache: Optional[Tuple[int, int, int]] = None
        self._overlay_size_cache: Optional[Tuple[int, int]] = None
        self._load_config()
        self._rebuild_lut()

    # ---- Public API ----
    def set_time_scale(self, minutes_per_second: float) -> None:
        self.time_scale_minutes_per_second = max(0.1, float(minutes_per_second))

    def set_keyframes(self, keyframes: List[Tuple[int, float, Tuple[int, int, int]]]) -> None:
        self._keyframes = sorted(keyframes, key=lambda k: k[0])
        self._rebuild_lut()

    def get_ambient_intensity(self) -> float:
        minute = self._current_minute()
        return float(self._lut[minute][0])

    def get_ambient_color(self) -> Tuple[int, int, int]:
        minute = self._current_minute()
        return self._lut[minute][1]

    def ambient_enabled(self) -> bool:
        return bool(self.enabled)

    def get_overlay_surface(self, size: Tuple[int, int]) -> pygame.Surface:
        """Return a cached screen-sized Surface filled with the current ambient tint.
        Uses BLEND_RGBA_MULT when applied to dim the scene.
        """
        w, h = size
        color = self._compute_overlay_color()
        if (
            self._overlay_cache is None
            or self._overlay_size_cache != (w, h)
            or self._overlay_color_cache != color
        ):
            surf = pygame.Surface((w, h), flags=pygame.SRCALPHA)
            # Full alpha to ensure multiplication takes effect
            surf.fill((color[0], color[1], color[2], 255))
            self._overlay_cache = surf
            self._overlay_size_cache = (w, h)
            self._overlay_color_cache = color
        return self._overlay_cache  # type: ignore[return-value]

    # ---- Internals ----
    def _load_config(self) -> None:
        try:
            if self.config_path.exists():
                data = json.loads(self.config_path.read_text(encoding="utf-8"))
            else:
                data = {}
        except Exception:
            data = {}
        self.enabled = bool(data.get("enabled", True))
        # In fase 1 usamos solo overlay ambiental aunque ambient_only sea False
        self.ambient_only = bool(data.get("ambient_only", True))
        self.time_scale_minutes_per_second = float(data.get("time_scale", 120.0) or 120.0)
        # Parse keyframes
        kf = []
        for rec in data.get("keyframes", []) or []:
            try:
                minute = int(rec.get("minute", 0))
                intensity = float(rec.get("intensity", 1.0))
                color = tuple(rec.get("color", [255, 255, 255]))  # type: ignore[assignment]
                if not (0 <= minute <= 1440):
                    continue
                kf.append((minute, _clamp(intensity, 0.0, 1.0), (int(color[0]), int(color[1]), int(color[2]))))
            except Exception:
                continue
        if not kf:
            # Fallback sensible
            kf = [
                (300, 0.30, (180, 140, 120)),
                (360, 0.55, (220, 180, 150)),
                (420, 0.80, (230, 230, 235)),
                (720, 1.00, (245, 245, 255)),
                (1140, 0.55, (220, 170, 140)),
                (1200, 0.30, (170, 140, 180)),
                (1440, 0.20, (150, 170, 220)),
            ]
        self._keyframes = sorted(kf, key=lambda k: k[0])

    def _rebuild_lut(self) -> None:
        # Build LUT for minutes 0..1439 inclusive (wrap at 1440)
        lut: List[Tuple[float, Tuple[int, int, int]]] = [(1.0, (255, 255, 255))] * 1440
        kf = self._keyframes
        if not kf:
            self._lut = lut
            return
        # Ensure wrap-around by adding (0) if needed
        if kf[0][0] != 0:
            kf = [(0, kf[0][1], kf[0][2])] + kf
        # Walk segments
        for i in range(len(kf) - 1):
            m0, i0, c0 = kf[i]
            m1, i1, c1 = kf[i + 1]
            span = max(1, m1 - m0)
            for s in range(span):
                t = s / float(span)
                # Smoothstep easing
                tt = t * t * (3 - 2 * t)
                ii = _lerp(i0, i1, tt)
                cc = _lerp_color(c0, c1, tt)
                minute = (m0 + s) % 1440
                lut[minute] = (ii, cc)
        # Fill remaining to 1440 using last keyframe
        last_m, last_i, last_c = kf[-1]
        for m in range(last_m, 1440):
            lut[m] = (last_i, last_c)
        self._lut = lut

    def _current_minute(self) -> int:
        # Minutes elapsed since start with time scale
        ticks = pygame.time.get_ticks() - self._start_ticks
        minutes = (ticks / 1000.0) * self.time_scale_minutes_per_second
        minute_of_day = int(minutes) % 1440
        return minute_of_day

    def _compute_overlay_color(self) -> Tuple[int, int, int]:
        # Recompute tint only every ~150 ms
        now = pygame.time.get_ticks()
        if now - self._last_overlay_rebuild_ticks < 150 and self._overlay_color_cache is not None:
            return self._overlay_color_cache
        intensity = self.get_ambient_intensity()
        color = self.get_ambient_color()
        # Convert intensity & color into a multiplicative tint
        # Scale color by intensity, but keep a minimum to avoid total black unless intended
        r = max(8, min(255, int(color[0] * intensity)))
        g = max(8, min(255, int(color[1] * intensity)))
        b = max(8, min(255, int(color[2] * intensity)))
        tint = (r, g, b)
        self._last_overlay_rebuild_ticks = now
        self._overlay_color_cache = tint
        return tint


_GLOBAL_DN: Optional[DayNightSystem] = None


def get_global_daynight() -> DayNightSystem:
    global _GLOBAL_DN
    if _GLOBAL_DN is None:
        _GLOBAL_DN = DayNightSystem(_DEFAULT_CONFIG_PATH)
    return _GLOBAL_DN

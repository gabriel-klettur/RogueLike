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
        # 0.4 min/s => 24h (1440 min) por 3600 s (1h real)
        self.time_scale_minutes_per_second: float = 0.4
        # Minuto del día al inicio (offset), 0..1439
        self._start_minute: int = 0
        # Piso mínimo de intensidad ambiental (nunca oscuridad total)
        self._min_intensity: float = 0.2
        self._keyframes: List[Tuple[int, float, Tuple[int, int, int]]] = []
        self._lut: List[Tuple[float, Tuple[int, int, int]]] = [(1.0, (255, 255, 255))] * 1440
        self._start_ticks: int = pygame.time.get_ticks()
        self._last_overlay_rebuild_ticks: int = 0
        self._overlay_cache: Optional[pygame.Surface] = None
        self._overlay_color_cache: Optional[Tuple[int, int, int]] = None
        self._overlay_size_cache: Optional[Tuple[int, int]] = None
        self._load_config()
        self._rebuild_lut()
        # Lights disable window defaults (09:00–19:00) unless overridden by config
        if not hasattr(self, "_lights_disable_start_minute"):
            self._lights_disable_start_minute: int = 540
        if not hasattr(self, "_lights_disable_end_minute"):
            self._lights_disable_end_minute: int = 1140
        # Stagger defaults unless overridden by config
        if not hasattr(self, "_lights_stagger_interval_ms"):
            self._lights_stagger_interval_ms: int = 3000
        if not hasattr(self, "_lights_stagger_order"):
            self._lights_stagger_order: str = "asc"

    # ---- Public API ----
    def set_time_scale(self, minutes_per_second: float) -> None:
        self.time_scale_minutes_per_second = max(0.1, float(minutes_per_second))

    def set_keyframes(self, keyframes: List[Tuple[int, float, Tuple[int, int, int]]]) -> None:
        self._keyframes = sorted(keyframes, key=lambda k: k[0])
        self._rebuild_lut()
        self._overlay_color_cache = None

    def get_keyframes(self) -> List[Tuple[int, float, Tuple[int, int, int]]]:
        return list(self._keyframes)

    def set_keyframe(self, minute: int, intensity: Optional[float] = None, color: Optional[Tuple[int, int, int]] = None) -> None:
        try:
            minute = int(minute) % 1440
        except Exception:
            minute = 0
        # Find existing keyframe
        idx = None
        for i, (m, _i, _c) in enumerate(self._keyframes):
            if m == minute:
                idx = i
                break
        if idx is None:
            # Create from current sampled values
            cur_i = self._lut[minute][0]
            cur_c = self._lut[minute][1]
            if intensity is not None:
                cur_i = float(intensity)
            if color is not None:
                cur_c = tuple(color)
            self._keyframes.append((minute, _clamp(cur_i, 0.0, 1.0), (int(cur_c[0]), int(cur_c[1]), int(cur_c[2]))))
        else:
            m, i0, c0 = self._keyframes[idx]
            if intensity is not None:
                i0 = _clamp(float(intensity), 0.0, 1.0)
            if color is not None:
                c0 = (int(color[0]), int(color[1]), int(color[2]))
            self._keyframes[idx] = (m, i0, c0)
        self._keyframes = sorted(self._keyframes, key=lambda k: k[0])
        self._rebuild_lut()
        self._overlay_color_cache = None

    def get_min_intensity(self) -> float:
        return float(self._min_intensity)

    def set_min_intensity(self, v: float) -> None:
        self._min_intensity = max(0.0, min(1.0, float(v)))
        self._overlay_color_cache = None

    def save_config(self) -> None:
        try:
            data = {
                "enabled": bool(self.enabled),
                "ambient_only": bool(self.ambient_only),
                "time_scale": float(self.time_scale_minutes_per_second),
                "start_minute": int(self._start_minute),
                # Persist lights schedule as HH:MM strings
                "lights_disable_start": f"{(self._lights_disable_start_minute//60)%24:02d}:{self._lights_disable_start_minute%60:02d}",
                "lights_disable_end": f"{(self._lights_disable_end_minute//60)%24:02d}:{self._lights_disable_end_minute%60:02d}",
                "lights_stagger_interval_ms": int(self._lights_stagger_interval_ms),
                "lights_stagger_order": str(self._lights_stagger_order),
                "min_intensity": float(self._min_intensity),
                "keyframes": [
                    {"minute": m, "intensity": float(i), "color": [int(c[0]), int(c[1]), int(c[2])]} for (m, i, c) in self._keyframes
                ],
            }
            self.config_path.parent.mkdir(parents=True, exist_ok=True)
            self.config_path.write_text(json.dumps(data, indent=2), encoding="utf-8")
        except Exception:
            pass

    def get_ambient_intensity(self) -> float:
        minute = self._current_minute()
        val = float(self._lut[minute][0])
        # Aplica piso mínimo configurable
        if val < self._min_intensity:
            val = self._min_intensity
        return val

    def get_ambient_color(self) -> Tuple[int, int, int]:
        minute = self._current_minute()
        return self._lut[minute][1]

    # ---- Public sampling helpers -------------------------------------------
    def get_intensity_at_minute(self, minute: int, apply_floor: bool = False) -> float:
        try:
            m = int(minute) % 1440
        except Exception:
            m = 0
        v = float(self._lut[m][0])
        if apply_floor and v < self._min_intensity:
            v = self._min_intensity
        return v

    def get_color_at_minute(self, minute: int) -> Tuple[int, int, int]:
        try:
            m = int(minute) % 1440
        except Exception:
            m = 0
        return self._lut[m][1]

    def common_minutes(self) -> Tuple[int, int, int, int, int, int]:
        """Return commonly edited minutes: 00:00, 05:00, 07:00, 12:00, 19:00, 21:00."""
        return (0, 300, 420, 720, 1140, 1260)

    def ambient_enabled(self) -> bool:
        return bool(self.enabled)

    def get_overlay_surface(self, size: Tuple[int, int]) -> pygame.Surface:
        """Return a cached screen-sized Surface filled with the current ambient tint.
        Uses BLEND_RGBA_MULT when applied to dim the scene.
        """
        w, h = size
        # Capture previous tint before computing a potentially new one to detect changes
        prev_color = self._overlay_color_cache
        color = self._compute_overlay_color()
        if (
            self._overlay_cache is None
            or self._overlay_size_cache != (w, h)
            or prev_color != color
        ):
            surf = pygame.Surface((w, h), flags=pygame.SRCALPHA)
            try:
                # Convert to display format for faster blits
                surf = surf.convert_alpha()
            except Exception:
                pass
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
        # Si el config anterior trae valores antiguos absurdos (e.g., 120.0), normaliza a 0.4 por defecto
        if self.time_scale_minutes_per_second > 10.0:
            self.time_scale_minutes_per_second = 0.4
        # Minuto inicial del día (por defecto: 10:00 = 600 para arrancar en día)
        try:
            self._start_minute = int(data.get("start_minute", 600)) % 1440
        except Exception:
            self._start_minute = 600
        # Piso mínimo de intensidad
        try:
            self._min_intensity = float(data.get("min_intensity", 0.2))
            self._min_intensity = max(0.0, min(1.0, self._min_intensity))
        except Exception:
            self._min_intensity = 0.2
        # Optional lights disable window: strings "HH:MM" or minute integers
        def _parse_hhmm(v) -> int:
            try:
                if isinstance(v, str):
                    parts = v.strip().split(":")
                    if len(parts) == 2:
                        h = max(0, min(23, int(parts[0])))
                        m = max(0, min(59, int(parts[1])))
                        return (h * 60 + m) % 1440
                if isinstance(v, (int, float)):
                    return int(v) % 1440
            except Exception:
                pass
            return 540  # default 09:00
        try:
            self._lights_disable_start_minute = _parse_hhmm(data.get("lights_disable_start", 540))
        except Exception:
            self._lights_disable_start_minute = 540
        try:
            self._lights_disable_end_minute = _parse_hhmm(data.get("lights_disable_end", 1140))
        except Exception:
            self._lights_disable_end_minute = 1140
        # Stagger settings (interval and order)
        try:
            iv = int(data.get("lights_stagger_interval_ms", 3000))
            # Clamp to reasonable bounds (0..10 min)
            self._lights_stagger_interval_ms = max(0, min(600000, iv))
        except Exception:
            self._lights_stagger_interval_ms = 3000
        try:
            ord_raw = str(data.get("lights_stagger_order", "asc") or "asc").strip().lower()
            self._lights_stagger_order = "desc" if ord_raw.startswith("desc") else "asc"
        except Exception:
            self._lights_stagger_order = "asc"
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
                # Night absolute until 05:00 (00:00 and 05:00)
                (0, 0.00, (150, 170, 220)),
                (300, 0.00, (150, 170, 220)),
                # Dawn 05:00 -> 07:00 ramps to full day with neutral color
                (420, 1.00, (255, 255, 255)),
                # Day 07:00 -> 19:00 no filter (pure white)
                (1140, 1.00, (255, 255, 255)),
                # Dusk 19:00 -> 21:00 ramps to absolute night
                (1260, 0.00, (120, 140, 200)),
                # Night 21:00 -> 24:00 remains absolute
                (1440, 0.00, (120, 140, 200)),
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
        minute_of_day = (int(minutes) + self._start_minute) % 1440
        return minute_of_day

    # ---- Public helpers for HUD ---------------------------------------------
    def get_minute_of_day(self) -> int:
        """Return current minute of day [0..1439]."""
        return self._current_minute()

    def set_minute_of_day(self, minute: int) -> None:
        """Set the simulated clock to a specific minute of day [0..1439].

        Adjusts the internal start offset to preserve monotonic tick-based time.
        """
        try:
            minute = int(minute) % 1440
        except Exception:
            minute = 0
        # Compute elapsed minutes since start
        ticks = pygame.time.get_ticks() - self._start_ticks
        elapsed = int((ticks / 1000.0) * self.time_scale_minutes_per_second) % 1440
        # Choose start offset so that current becomes desired minute
        self._start_minute = (minute - elapsed) % 1440
        # Invalidate overlay cache so changes reflect immediately
        self._overlay_color_cache = None

    def get_game_time_hms(self) -> Tuple[int, int, int]:
        """Return current game time as (hour, minute, second)."""
        ticks = pygame.time.get_ticks() - self._start_ticks
        game_minutes_f = (ticks / 1000.0) * self.time_scale_minutes_per_second + float(self._start_minute)
        total_seconds = int(game_minutes_f * 60.0) % (24 * 3600)
        h = (total_seconds // 3600) % 24
        m = (total_seconds % 3600) // 60
        s = total_seconds % 60
        return h, m, s

    def get_phase(self) -> str:
        """Return a textual phase based on keyframe/typical ranges: dawn/day/dusk/night."""
        m = self._current_minute()
        # Rangos exactos solicitados:
        # Dawn: 05:00–07:00 (300–420), Day: 07:00–19:00 (420–1140),
        # Dusk: 19:00–21:00 (1140–1260), Night: 21:00–05:00.
        if 300 <= m < 420:
            return "Dawn"
        if 420 <= m < 1140:
            return "Day"
        if 1140 <= m < 1260:
            return "Dusk"
        return "Night"

    # ---- Lights schedule helpers -------------------------------------------
    def get_lights_disable_window(self) -> Tuple[int, int]:
        """Return (start_minute, end_minute) in [0..1439]."""
        return int(self._lights_disable_start_minute) % 1440, int(self._lights_disable_end_minute) % 1440

    def set_lights_disable_window(self, start_minute: int, end_minute: int) -> None:
        self._lights_disable_start_minute = int(start_minute) % 1440
        self._lights_disable_end_minute = int(end_minute) % 1440
        # No overlay invalidation needed; affects only point lights schedule

    def is_lights_disabled_now(self) -> bool:
        """True if current minute falls inside the disable window (supports wrap)."""
        m = self._current_minute()
        a, b = self.get_lights_disable_window()
        if a == b:
            return False  # degenerate -> never disable
        if a < b:
            return a <= m < b
        # Wrapped window (e.g., 22:00..06:00)
        return m >= a or m < b

    def get_lights_stagger_interval_ms(self) -> int:
        return int(self._lights_stagger_interval_ms)

    def get_lights_stagger_order(self) -> str:
        return "desc" if str(self._lights_stagger_order).lower().startswith("desc") else "asc"

    def _compute_overlay_color(self) -> Tuple[int, int, int]:
        # Recompute tint only every ~150 ms
        now = pygame.time.get_ticks()
        if now - self._last_overlay_rebuild_ticks < 150 and self._overlay_color_cache is not None:
            return self._overlay_color_cache
        intensity = self.get_ambient_intensity()
        color = self.get_ambient_color()
        # Convert intensity & color into a multiplicative tint
        # Escalamos por intensidad; permitir 0 para noche absoluta
        r = max(0, min(255, int(color[0] * intensity)))
        g = max(0, min(255, int(color[1] * intensity)))
        b = max(0, min(255, int(color[2] * intensity)))
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

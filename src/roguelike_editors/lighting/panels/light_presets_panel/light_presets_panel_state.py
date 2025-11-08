from __future__ import annotations

import json
from pathlib import Path
import pygame
from typing import List, Tuple


class LightPresetsPanelState:
    """State for the Light Presets panel (UI-only state).

    Stores preset configuration and cached button hit-rects.
    Rendering reads/updates these, while the controller tests collisions.
    """

    def __init__(self) -> None:
        # Panel placement and cached UI rects
        self._panel_rect: pygame.Rect | None = None
        # Combo selector
        self.spawn_preset: str = "Torch"
        self.spawn_types: list[str] = ["Torch", "Lamp", "Magic", "Custom"]
        self.spawn_combo_open: bool = False
        self._combo_spawn_type: pygame.Rect | None = None
        self._combo_spawn_items: list[tuple] | None = None
        # Preset parameter values (defaults similar to Torch)
        self.spawn_radius: int = 160
        self.spawn_intensity: float = 1.0
        self.spawn_falloff: float = 2.0
        self.spawn_color: tuple[int, int, int] = (255, 200, 140)
        self.spawn_flicker_amp: float = 0.15
        self.spawn_flicker_speed: float = 2.5
        self.spawn_center_scale: float = 1.0
        self.spawn_single_shot: bool = False
        # Preset buttons
        self._btn_preset_torch: pygame.Rect | None = None
        self._btn_preset_lamp: pygame.Rect | None = None
        self._btn_preset_magic: pygame.Rect | None = None
        # Param steppers
        self._btn_sr_minus: pygame.Rect | None = None
        self._btn_sr_plus: pygame.Rect | None = None
        self._btn_si_minus: pygame.Rect | None = None
        self._btn_si_plus: pygame.Rect | None = None
        self._btn_sf_minus: pygame.Rect | None = None
        self._btn_sf_plus: pygame.Rect | None = None
        self._btn_fa_minus: pygame.Rect | None = None
        self._btn_fa_plus: pygame.Rect | None = None
        self._btn_fs_minus: pygame.Rect | None = None
        self._btn_fs_plus: pygame.Rect | None = None
        self._btn_cs_minus: pygame.Rect | None = None
        self._btn_cs_plus: pygame.Rect | None = None
        self._btn_single_shot: pygame.Rect | None = None
        # Color steppers
        self._btn_r_minus: pygame.Rect | None = None
        self._btn_r_plus: pygame.Rect | None = None
        self._btn_g_minus: pygame.Rect | None = None
        self._btn_g_plus: pygame.Rect | None = None
        self._btn_b_minus: pygame.Rect | None = None
        self._btn_b_plus: pygame.Rect | None = None

        # Local tooltips list for the panel
        self._tooltips: List[Tuple[pygame.Rect, str]] = []

        # Load presets from JSON (with robust defaults) and initialize from Torch
        self.presets: dict[str, dict] = self._load_presets()
        try:
            # Replace spawn types with those from config if present, keep Custom at the end
            names = list(self.presets.keys())
            if names:
                self.spawn_types = names + (["Custom"] if "Custom" not in names else [])
        except Exception:
            pass
        try:
            p = self.presets.get("Torch")
            if isinstance(p, dict):
                self.spawn_radius = int(p.get("radius", self.spawn_radius))
                self.spawn_intensity = float(p.get("intensity", self.spawn_intensity))
                self.spawn_falloff = float(p.get("falloff", self.spawn_falloff))
                c = p.get("color", self.spawn_color)
                if isinstance(c, (list, tuple)) and len(c) == 3:
                    self.spawn_color = (int(c[0]), int(c[1]), int(c[2]))
                self.spawn_flicker_amp = float(p.get("flicker_amp", self.spawn_flicker_amp))
                self.spawn_flicker_speed = float(p.get("flicker_speed", self.spawn_flicker_speed))
                self.spawn_center_scale = float(p.get("center_scale", self.spawn_center_scale))
        except Exception:
            pass

    def _load_presets(self) -> dict[str, dict]:
        defaults = {
            "Torch": {
                "radius": 160,
                "intensity": 1.0,
                "falloff": 2.0,
                "color": [255, 200, 140],
                "flicker_amp": 0.15,
                "flicker_speed": 0.75,
                "center_scale": 0.85,
            },
            "Lamp": {
                "radius": 120,
                "intensity": 0.9,
                "falloff": 2.2,
                "color": [255, 240, 200],
                "flicker_amp": 0.05,
                "flicker_speed": 1.2,
                "center_scale": 0.9,
            },
            "Magic": {
                "radius": 180,
                "intensity": 1.1,
                "falloff": 1.6,
                "color": [120, 200, 255],
                "flicker_amp": 0.20,
                "flicker_speed": 3.2,
                "center_scale": 1.0,
            },
        }

        # Find data/light/presets.json by walking up from this file
        try:
            here = Path(__file__).resolve()
            candidates = [p / "data" / "light" / "presets.json" for p in here.parents]
            cfg_path = next((c for c in candidates if c.exists()), None)
            if cfg_path is None:
                return defaults
            with cfg_path.open("r", encoding="utf-8") as f:
                data = json.load(f)
            raw = data.get("presets", {}) if isinstance(data, dict) else {}
            if not isinstance(raw, dict):
                return defaults
            merged = dict(defaults)
            for k, v in raw.items():
                if isinstance(v, dict):
                    merged[k] = v
            return merged
        except Exception:
            return defaults

    @property
    def panel_rect(self) -> pygame.Rect | None:
        """Expose the panel rect to external controllers for hit testing."""
        return self._panel_rect

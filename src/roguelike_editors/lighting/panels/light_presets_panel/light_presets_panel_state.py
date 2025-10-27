from __future__ import annotations

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

    @property
    def panel_rect(self) -> pygame.Rect | None:
        """Expose the panel rect to external controllers for hit testing."""
        return self._panel_rect


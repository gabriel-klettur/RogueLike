from __future__ import annotations

import pygame
from typing import List, Tuple


class DayTimePanelState:
    """State for the Daytime Tools panel (UI-only state).

    Stores button hit-rects and tooltip info. Rendering reads/updates these,
    while the controller tests collisions against them.
    """

    def __init__(self) -> None:
        # Panel placement and cached UI rects
        self._panel_rect: pygame.Rect | None = None
        self._btn_time_m5: pygame.Rect | None = None
        self._btn_time_p5: pygame.Rect | None = None
        self._btn_time_m30: pygame.Rect | None = None
        self._btn_time_p30: pygame.Rect | None = None
        self._btn_time_05: pygame.Rect | None = None
        self._btn_time_07: pygame.Rect | None = None
        self._btn_time_12: pygame.Rect | None = None
        self._btn_time_19: pygame.Rect | None = None
        self._btn_time_21: pygame.Rect | None = None
        self._btn_time_00: pygame.Rect | None = None
        self._btn_minI_minus: pygame.Rect | None = None
        self._btn_minI_plus: pygame.Rect | None = None
        self._btn_ts_minus: pygame.Rect | None = None
        self._btn_ts_plus: pygame.Rect | None = None
        self._btn_i_0000_minus: pygame.Rect | None = None
        self._btn_i_0000_plus: pygame.Rect | None = None
        self._btn_i_0500_minus: pygame.Rect | None = None
        self._btn_i_0500_plus: pygame.Rect | None = None
        self._btn_i_0700_minus: pygame.Rect | None = None
        self._btn_i_0700_plus: pygame.Rect | None = None
        self._btn_i_1200_minus: pygame.Rect | None = None
        self._btn_i_1200_plus: pygame.Rect | None = None
        self._btn_i_1900_minus: pygame.Rect | None = None
        self._btn_i_1900_plus: pygame.Rect | None = None
        self._btn_i_2100_minus: pygame.Rect | None = None
        self._btn_i_2100_plus: pygame.Rect | None = None
        self._btn_time_save: pygame.Rect | None = None

        # Local tooltips list for the panel
        self._tooltips: List[Tuple[pygame.Rect, str]] = []

    @property
    def panel_rect(self) -> pygame.Rect | None:
        """Exposes the panel rect to external controllers for hit testing."""
        return self._panel_rect


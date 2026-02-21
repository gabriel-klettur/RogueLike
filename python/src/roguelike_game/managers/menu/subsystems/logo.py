from __future__ import annotations

from typing import Optional, Tuple

import pygame


class LogoManager:
    """Loads, scales and positions the game logo above the menu panel."""

    def __init__(self) -> None:
        self.logo_path: Optional[str] = None
        self._logo_surface: Optional[pygame.Surface] = None
        self._logo_scaled: Optional[pygame.Surface] = None
        self._logo_scaled_screen_size: Optional[Tuple[int, int]] = None
        self._logo_max_w_ratio: float = 0.6
        self._logo_max_h_ratio: float = 0.22
        self._logo_gap_px: int = 16
        self._logo_initial_scale: float = 1.0
        self._logo_top_ratio: float = 0.08

    def set_logo(
        self,
        path: Optional[str],
        *,
        max_width_ratio: float = 0.6,
        max_height_ratio: float = 0.22,
        gap_px: int = 16,
        initial_scale: float = 1.0,
        top_ratio: float = 0.08,
    ) -> None:
        self.logo_path = path
        self._logo_surface = None
        self._logo_scaled = None
        self._logo_scaled_screen_size = None
        self._logo_max_w_ratio = max(0.1, min(1.0, float(max_width_ratio)))
        self._logo_max_h_ratio = max(0.1, min(1.0, float(max_height_ratio)))
        self._logo_gap_px = max(0, int(gap_px))
        self._logo_initial_scale = max(0.05, float(initial_scale))
        self._logo_top_ratio = max(0.0, min(0.9, float(top_ratio)))

    def _ensure_logo_loaded_and_scaled(self, screen: pygame.Surface) -> bool:
        if not self.logo_path:
            return False
        if self._logo_surface is None:
            try:
                surf = pygame.image.load(self.logo_path)
                try:
                    surf = surf.convert_alpha()
                except Exception:
                    surf = surf.convert()
                self._logo_surface = surf
            except Exception:
                self._logo_surface = None
                return False
        sw, sh = screen.get_size()
        if self._logo_scaled is None or self._logo_scaled_screen_size != (sw, sh):
            iw, ih = self._logo_surface.get_size()
            if iw <= 0 or ih <= 0:
                return False
            max_w = int(sw * self._logo_max_w_ratio)
            max_h = int(sh * self._logo_max_h_ratio)
            scale_base = min(max_w / iw, max_h / ih)
            scale = min(1.0, scale_base) * self._logo_initial_scale
            new_w = max(1, int(iw * scale))
            new_h = max(1, int(ih * scale))
            try:
                self._logo_scaled = pygame.transform.scale(self._logo_surface, (new_w, new_h))
            except Exception:
                self._logo_scaled = self._logo_surface
            self._logo_scaled_screen_size = (sw, sh)
        return True

    def compute_layout(self, screen: pygame.Surface):
        """Returns (surf, (x, y), bottom_y) or None if no logo."""
        if not self._ensure_logo_loaded_and_scaled(screen):
            return None
        sw, sh = screen.get_size()
        lw, lh = self._logo_scaled.get_size()
        x = (sw - lw) // 2
        y = max(8, int(sh * self._logo_top_ratio))
        surf = self._logo_scaled._surf if hasattr(self._logo_scaled, "_surf") else self._logo_scaled
        return surf, (x, y), y + lh

    @property
    def gap_px(self) -> int:
        return self._logo_gap_px

from __future__ import annotations

import json
import time
from pathlib import Path
from typing import Optional, Tuple

import pygame


class PressStartManager:
    """Controls the pre-menu overlay 'Press to start' with hot-reloadable config."""

    def __init__(self) -> None:
        self.active: bool = False
        self.text: str = "Pulsa para comenzar"
        self.blink_interval_s: float = 0.85
        self.last_toggle: float = time.time()
        self.visible: bool = True

        # Visual config
        self.font_scale: float = 1.5
        self.extra_offset_px: int = 28
        self.color: Tuple[int, int, int] = (255, 220, 0)
        self.shadow_color: Tuple[int, int, int] = (0, 0, 0)
        self.font_size: Optional[int] = None

        # Hot reload state
        self._intro_cfg_path: Optional[Path] = None
        self._intro_cfg_mtime: Optional[float] = None
        self._intro_cfg_last_check: float = 0.0

        self.load_intro_config()

    # ---------------- Config ----------------
    def load_intro_config(self) -> None:
        try:
            cfg_path = Path("data/config/intro.json")
            self._intro_cfg_path = cfg_path
            if not cfg_path.exists():
                self._intro_cfg_mtime = None
                return
            with cfg_path.open("r", encoding="utf-8") as f:
                cfg = json.load(f)
            press = cfg.get("press", {}) or {}
            if "text" in press:
                self.text = str(press.get("text"))
            if "font_scale" in press:
                try:
                    self.font_scale = max(0.1, float(press.get("font_scale")))
                except Exception:
                    pass
            if "font_size" in press:
                try:
                    val = int(press.get("font_size"))
                    self.font_size = max(8, val)
                except Exception:
                    pass
            if "extra_offset_px" in press:
                try:
                    self.extra_offset_px = int(press.get("extra_offset_px"))
                except Exception:
                    pass
            if "blink_interval_s" in press:
                try:
                    self.blink_interval_s = max(0.1, float(press.get("blink_interval_s")))
                except Exception:
                    pass
            if "color" in press:
                try:
                    col = tuple(press.get("color") or [])
                    if len(col) >= 3:
                        self.color = (int(col[0]), int(col[1]), int(col[2]))
                except Exception:
                    pass
            if "shadow_color" in press:
                try:
                    scol = tuple(press.get("shadow_color") or [])
                    if len(scol) >= 3:
                        self.shadow_color = (int(scol[0]), int(scol[1]), int(scol[2]))
                except Exception:
                    pass
            try:
                self._intro_cfg_mtime = cfg_path.stat().st_mtime
            except Exception:
                self._intro_cfg_mtime = None
        except Exception:
            # Silent failure: don't break the menu
            pass

    def maybe_reload_intro_config(self) -> None:
        try:
            now = time.time()
            if (now - self._intro_cfg_last_check) < 1.0:
                return
            self._intro_cfg_last_check = now
            p = self._intro_cfg_path
            if not p or not p.exists():
                return
            mtime = p.stat().st_mtime
            if self._intro_cfg_mtime != mtime:
                self.load_intro_config()
                self._intro_cfg_mtime = mtime
        except Exception:
            pass

    # ---------------- Control ----------------
    def enable(self, text: Optional[str] = None, blink_interval_s: float = 0.85) -> None:
        self.active = True
        if text:
            self.text = text
        self.blink_interval_s = max(0.1, float(blink_interval_s))
        self.last_toggle = time.time()
        self.visible = True

    def disable(self) -> None:
        self.active = False

    # ---------------- Draw ----------------
    def draw(self, screen: pygame.Surface, base_font, logo_layout) -> pygame.Rect:
        # Overlay
        sw, sh = screen.get_size()
        overlay = pygame.Surface((sw, sh), pygame.SRCALPHA)
        overlay.fill((0, 0, 0, 140))
        surface_to_blit = overlay._surf if hasattr(overlay, "_surf") else overlay
        screen.blit(surface_to_blit, (0, 0))
        # Logo if provided
        if logo_layout is not None:
            surf, pos, bottom = logo_layout
            screen.blit(surf, pos)
        # Blink toggle
        now = time.time()
        if (now - self.last_toggle) >= self.blink_interval_s:
            self.visible = not self.visible
            self.last_toggle = now
        if self.visible:
            # Hot reload config (if user edits intro.json live)
            self.maybe_reload_intro_config()
            # Font selection
            try:
                base_size = int(getattr(base_font, "size", 24))  # fallback path
                base_size = int(getattr(base_font, "font_size", base_size))
            except Exception:
                base_size = 24
            cfg_size = int(self.font_size or 0)
            if cfg_size > 0:
                press_size = max(8, cfg_size)
            else:
                press_size = max(8, int(base_size * float(self.font_scale)))
            try:
                press_font = pygame.font.Font(None, press_size)
            except Exception:
                press_font = base_font
            text_surf = press_font.render(self.text, True, self.color)
            shadow = press_font.render(self.text, True, self.shadow_color)
            tx = (sw - text_surf.get_width()) // 2
            if logo_layout is not None:
                _, _, bottom = logo_layout
                ty = bottom + max(12, 16) + int(self.extra_offset_px)
                ty = min(ty, sh - text_surf.get_height() - 24)
            else:
                ty = (sh - text_surf.get_height()) // 2 + int(self.extra_offset_px)
            screen.blit(shadow, (tx + 2, ty + 2))
            screen.blit(text_surf, (tx, ty))
        return pygame.Rect(0, 0, sw, sh)

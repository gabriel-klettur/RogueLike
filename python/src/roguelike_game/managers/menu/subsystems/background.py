from __future__ import annotations

import logging
import time
from pathlib import Path
from typing import List, Tuple, Optional

import pygame

logger = logging.getLogger(__name__)


class BackgroundManager:
    """Handles static background and carousel with scaling and intro flash.

    Responsibilities:
    - Single background image with cover/contain.
    - Carousel of multiple images with crossfade and slide.
    - Intro flash overlay and enabling the carousel after the flash.
    """

    def __init__(self) -> None:
        # Single background
        self.background_path: Optional[str] = None
        self._bg_surface: Optional[pygame.Surface] = None
        self._bg_scaled_cache: Optional[pygame.Surface] = None
        self._bg_scaled_size: Optional[Tuple[int, int]] = None
        self._bg_scaled_screen_size: Optional[Tuple[int, int]] = None
        self._bg_scaled_offset: Tuple[int, int] = (0, 0)
        self._bg_scale_mode: str = "cover"

        # Carousel
        self.backgrounds: List[str] = []
        self._bg_surfaces_list: List[pygame.Surface] = []
        self._bg_scaled_list: List[pygame.Surface] = []
        self._bg_offsets_list: List[Tuple[int, int]] = []
        self._bg_last_screen_size: Optional[Tuple[int, int]] = None
        self._bg_index: int = 0
        self._bg_prev_index: Optional[int] = None
        self._bg_last_switch_time: float = time.time()
        self._bg_transition_start: Optional[float] = None
        self._bg_interval_s: float = 2.0
        self._bg_transition_s: float = 0.6
        self._bg_slide_px: int = 24

        # Intro flash and cycle gate
        self._bg_cycle_enabled: bool = False
        self._intro_flash_done: bool = False
        self._intro_flash_start_time: Optional[float] = None

        # Configurable flash defaults (can be tweaked from MenuManager)
        self._startup_flash_enabled: bool = True
        self._startup_flash_trigger: str = "time"  # time|on_menu_show|on_carousel_start
        self._startup_flash_at_s: float = 6.0
        self._startup_enable_cycle_after_flash: bool = True
        self._startup_flash_duration_s: float = 0.25
        self._startup_flash_ease: str = "linear"
        self._startup_flash_color_rgba: Tuple[int, int, int, int] = (255, 255, 255, 255)

    # ---------------- Single background ----------------
    def set_background(self, path: Optional[str], *, scale_mode: Optional[str] = None) -> None:
        self.background_path = path
        if scale_mode in ("cover", "contain"):
            self._bg_scale_mode = scale_mode or self._bg_scale_mode
        self._bg_surface = None
        self._bg_scaled_cache = None
        self._bg_scaled_size = None
        self._bg_scaled_screen_size = None
        self._bg_scaled_offset = (0, 0)

    def _ensure_background_loaded(self) -> None:
        if self._bg_surface is None and self.background_path:
            try:
                surf = pygame.image.load(self.background_path)
                try:
                    surf = surf.convert_alpha()
                except Exception:
                    surf = surf.convert()
                self._bg_surface = surf
            except Exception as e:
                logger.warning("No se pudo cargar el fondo del menú: %s", e)
                self._bg_surface = None

    def _blit_background_if_any(self, screen: pygame.Surface) -> None:
        if not self.background_path:
            return
        self._ensure_background_loaded()
        if self._bg_surface is None:
            return
        sw, sh = screen.get_size()
        iw, ih = self._bg_surface.get_size()
        if (
            self._bg_scaled_cache is None
            or self._bg_scaled_screen_size != (sw, sh)
            or self._bg_scaled_size is None
        ):
            try:
                if iw == 0 or ih == 0:
                    return
                if self._bg_scale_mode == "contain":
                    scale = min(sw / iw, sh / ih)
                else:
                    scale = max(sw / iw, sh / ih)
                new_w = max(1, int(iw * scale))
                new_h = max(1, int(ih * scale))
                self._bg_scaled_cache = pygame.transform.scale(self._bg_surface, (new_w, new_h))
                self._bg_scaled_size = (new_w, new_h)
                self._bg_scaled_screen_size = (sw, sh)
                off_x = (sw - new_w) // 2
                off_y = (sh - new_h) // 2
                self._bg_scaled_offset = (off_x, off_y)
            except Exception:
                self._bg_scaled_cache = self._bg_surface
                self._bg_scaled_size = self._bg_surface.get_size()
                self._bg_scaled_screen_size = (sw, sh)
                off_x = (sw - self._bg_scaled_size[0]) // 2
                off_y = (sh - self._bg_scaled_size[1]) // 2
                self._bg_scaled_offset = (off_x, off_y)
        surface_to_blit = (
            self._bg_scaled_cache._surf if hasattr(self._bg_scaled_cache, "_surf") else self._bg_scaled_cache
        )
        screen.blit(surface_to_blit, self._bg_scaled_offset)

    # ---------------- Carousel ----------------
    def set_backgrounds(
        self,
        paths: List[str],
        interval_s: float = 2.0,
        transition_s: float = 0.6,
        slide_px: int = 24,
        scale_mode: str = "cover",
    ) -> None:
        self.backgrounds = [p for p in paths if p]
        if scale_mode in ("cover", "contain"):
            self._bg_scale_mode = scale_mode
        self._bg_interval_s = max(0.1, float(interval_s))
        self._bg_transition_s = max(0.0, float(transition_s))
        self._bg_slide_px = int(slide_px)
        self._bg_index = 0
        self._bg_prev_index = None
        self._bg_last_switch_time = time.time()
        self._bg_transition_start = None
        self._reset_backgrounds_cache()
        # Disable single background when carousel is configured
        if self.backgrounds:
            self.background_path = None
            self._bg_surface = None
            self._bg_scaled_cache = None
            self._bg_scaled_size = None
            self._bg_scaled_screen_size = None
            self._bg_scaled_offset = (0, 0)

    def _reset_backgrounds_cache(self) -> None:
        self._bg_surfaces_list = []
        self._bg_scaled_list = []
        self._bg_offsets_list = []
        self._bg_last_screen_size = None

    def _ensure_backgrounds_loaded_and_scaled(self, screen: pygame.Surface) -> bool:
        if not self.backgrounds:
            return False
        if not self._bg_surfaces_list:
            for p in self.backgrounds:
                try:
                    surf = pygame.image.load(p)
                    try:
                        surf = surf.convert_alpha()
                    except Exception:
                        surf = surf.convert()
                    self._bg_surfaces_list.append(surf)
                except Exception as e:
                    logger.warning("No se pudo cargar fondo '%s': %s", p, e)
                    ph = pygame.Surface(screen.get_size())
                    ph.fill((0, 0, 0))
                    self._bg_surfaces_list.append(ph)
        sw, sh = screen.get_size()
        if (not self._bg_scaled_list) or (self._bg_last_screen_size != (sw, sh)):
            self._bg_scaled_list = []
            self._bg_offsets_list = []
            for s in self._bg_surfaces_list:
                try:
                    iw, ih = s.get_size()
                    if iw == 0 or ih == 0:
                        iw, ih = 1, 1
                    if self._bg_scale_mode == "contain":
                        scale = min(sw / iw, sh / ih)
                    else:
                        scale = max(sw / iw, sh / ih)
                    new_w = max(1, int(iw * scale))
                    new_h = max(1, int(ih * scale))
                    scaled = pygame.transform.scale(s, (new_w, new_h))
                except Exception:
                    scaled = s
                    new_w, new_h = scaled.get_size()
                off_x = (sw - new_w) // 2
                off_y = (sh - new_h) // 2
                self._bg_scaled_list.append(scaled)
                self._bg_offsets_list.append((off_x, off_y))
            self._bg_last_screen_size = (sw, sh)
        return True

    def _update_background_cycle_state(self) -> None:
        if not self.backgrounds:
            return
        if not getattr(self, "_bg_cycle_enabled", False):
            return
        now = time.time()
        if self._bg_transition_start is not None:
            t = now - self._bg_transition_start
            if t >= self._bg_transition_s:
                self._bg_prev_index = None
                self._bg_transition_start = None
                self._bg_last_switch_time = now
            return
        if (now - self._bg_last_switch_time) >= self._bg_interval_s and len(self.backgrounds) > 1:
            self._bg_prev_index = self._bg_index
            self._bg_index = (self._bg_index + 1) % len(self.backgrounds)
            self._bg_transition_start = now

    def _blit_backgrounds(self, screen: pygame.Surface) -> None:
        if self.backgrounds:
            if not self._ensure_backgrounds_loaded_and_scaled(screen):
                return
            if not getattr(self, "_bg_cycle_enabled", False):
                idx = max(0, min(self._bg_index, len(self._bg_scaled_list) - 1))
                cur = self._bg_scaled_list[idx]
                cur_off = self._bg_offsets_list[idx] if len(self._bg_offsets_list) > idx else (0, 0)
                surf = cur._surf if hasattr(cur, "_surf") else cur
                screen.blit(surf, cur_off)
                return
            self._update_background_cycle_state()
            cur = self._bg_scaled_list[self._bg_index]
            cur_off = (
                self._bg_offsets_list[self._bg_index] if len(self._bg_offsets_list) > self._bg_index else (0, 0)
            )
            if self._bg_transition_start is None or self._bg_prev_index is None or self._bg_transition_s <= 0.0:
                surf = cur._surf if hasattr(cur, "_surf") else cur
                screen.blit(surf, cur_off)
                return
            prev = self._bg_scaled_list[self._bg_prev_index]
            prev_off = (
                self._bg_offsets_list[self._bg_prev_index] if len(self._bg_offsets_list) > self._bg_prev_index else (0, 0)
            )
            now = time.time()
            t = (now - self._bg_transition_start) / max(1e-6, self._bg_transition_s)
            t = max(0.0, min(1.0, t))
            alpha_prev = int(255 * (1.0 - t))
            alpha_next = int(255 * t)
            dx_prev = int(-self._bg_slide_px * t)
            dx_next = int(self._bg_slide_px * (1.0 - t))
            try:
                prev.set_alpha(alpha_prev)
                surf_prev = prev._surf if hasattr(prev, "_surf") else prev
                screen.blit(surf_prev, (prev_off[0] + dx_prev, prev_off[1]))
            finally:
                try:
                    prev.set_alpha(None)
                except Exception:
                    pass
            try:
                cur.set_alpha(alpha_next)
                surf_cur = cur._surf if hasattr(cur, "_surf") else cur
                screen.blit(surf_cur, (cur_off[0] + dx_next, cur_off[1]))
            finally:
                try:
                    cur.set_alpha(None)
                except Exception:
                    pass
            return
        self._blit_background_if_any(screen)

    # ---------------- Flash ----------------
    @staticmethod
    def _flash_alpha_factor(t: float, duration: float, ease: str) -> float:
        if duration <= 0:
            return 0.0
        u = max(0.0, min(1.0, t / max(1e-6, duration)))
        k = 1.0 - u
        e = (ease or "linear").lower()
        try:
            if e in ("linear",):
                return k
            if e in ("ease_out", "ease_out_quad", "quad_out"):
                return k * k
            if e in ("ease_in_out", "ease_in_out_sine", "sine_in_out"):
                import math

                return 1.0 - 0.5 * (1.0 - math.cos(math.pi * u))
        except Exception:
            pass
        return k

    def draw(self, screen: pygame.Surface, mode: str, game) -> None:
        """Draw backgrounds and flash for start/load_list modes."""
        if mode not in ("start", "load_list"):
            return
        # Enable/trigger flash/cycle according to configured trigger
        try:
            intro_t0 = getattr(game, "intro_music_started_at", None)
        except Exception:
            intro_t0 = None
        now = time.time()
        elapsed = (now - intro_t0) if intro_t0 else None
        try:
            flash_enabled = bool(getattr(self, "_startup_flash_enabled", True))
            trigger = str(getattr(self, "_startup_flash_trigger", "time"))
            if flash_enabled and not self._intro_flash_done:
                if trigger == "time":
                    flash_at = float(getattr(self, "_startup_flash_at_s", 6.0))
                    if (intro_t0 is not None) and (elapsed is not None) and (elapsed >= flash_at):
                        self._intro_flash_done = True
                        self._intro_flash_start_time = now
                        if bool(getattr(self, "_startup_enable_cycle_after_flash", True)):
                            self._bg_cycle_enabled = True
                        self._bg_last_switch_time = now
                elif trigger == "on_menu_show":
                    self._intro_flash_done = True
                    self._intro_flash_start_time = now
                    if bool(getattr(self, "_startup_enable_cycle_after_flash", True)):
                        self._bg_cycle_enabled = True
                    self._bg_last_switch_time = now
                elif trigger == "on_carousel_start":
                    if not getattr(self, "_bg_cycle_enabled", False):
                        if bool(getattr(self, "_startup_enable_cycle_after_flash", True)):
                            self._bg_cycle_enabled = True
                        self._bg_last_switch_time = now
                        self._intro_flash_done = True
                        self._intro_flash_start_time = now
        except Exception:
            pass
        # Draw backgrounds
        self._blit_backgrounds(screen)
        # Flash overlay
        if self._intro_flash_start_time is not None and bool(getattr(self, "_startup_flash_enabled", True)):
            dt = now - self._intro_flash_start_time
            flash_dur = float(getattr(self, "_startup_flash_duration_s", 0.25))
            if flash_dur <= 0:
                self._intro_flash_start_time = None
            elif 0.0 <= dt <= flash_dur:
                ease = str(getattr(self, "_startup_flash_ease", "linear"))
                fac = BackgroundManager._flash_alpha_factor(dt, flash_dur, ease)
                color_rgba = tuple(getattr(self, "_startup_flash_color_rgba", (255, 255, 255, 255)))
                base_rgb = (int(color_rgba[0]), int(color_rgba[1]), int(color_rgba[2]))
                alpha = int(255 * max(0.0, min(1.0, fac)))
                try:
                    sw, sh = screen.get_size()
                    flash = pygame.Surface((sw, sh), pygame.SRCALPHA)
                    flash.fill((base_rgb[0], base_rgb[1], base_rgb[2], alpha))
                    surface_to_blit = flash._surf if hasattr(flash, "_surf") else flash
                    screen.blit(surface_to_blit, (0, 0))
                except Exception:
                    pass
            elif dt > flash_dur:
                self._intro_flash_start_time = None

    # External helpers for MenuManager
    def on_menu_show(self) -> None:
        """Convenience to trigger on_menu_show flash mode."""
        if str(getattr(self, "_startup_flash_trigger", "time")) == "on_menu_show" and not self._intro_flash_done:
            now = time.time()
            self._intro_flash_done = True
            self._intro_flash_start_time = now
            if bool(getattr(self, "_startup_enable_cycle_after_flash", True)):
                self._bg_cycle_enabled = True
            self._bg_last_switch_time = now

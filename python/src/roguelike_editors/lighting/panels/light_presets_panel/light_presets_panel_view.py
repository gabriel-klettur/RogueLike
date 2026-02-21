from __future__ import annotations

import pygame
from typing import Tuple

from .light_presets_panel_state import LightPresetsPanelState


class LightPresetsPanelView:
    def __init__(self, state: LightPresetsPanelState, font: pygame.font.Font | None = None) -> None:
        self.state = state
        self.font = font or pygame.font.SysFont("consolas", 18)

    def render(self, screen: pygame.Surface, *, anchor_rect: pygame.Rect, row_h: int) -> None:
        st = self.state
        # Reset tooltips for this frame
        try:
            st._tooltips = []
        except Exception:
            pass

        # Preferred panel size and position: to the right of DayTime panel
        gap = 12
        rw = 300
        rrow = row_h
        # Estimate height based on content
        rp_h = rrow * 12 + 16
        sw, sh = screen.get_size()

        rx = anchor_rect.x + anchor_rect.w + gap
        ry = anchor_rect.y
        if rx + rw > sw - 8:
            rx = sw - rw - 8
        if rx < anchor_rect.x + 8:
            rx = anchor_rect.x
            ry = anchor_rect.y + anchor_rect.h + 8
            if ry + rp_h > sh - 8:
                ry = max(8, sh - rp_h - 8)

        # Background
        bg = pygame.Surface((rw, rp_h), pygame.SRCALPHA)
        bg.fill((20, 20, 28, 200))
        screen.blit(bg, (rx, ry))
        st._panel_rect = pygame.Rect(rx, ry, rw, rp_h)

        # Title
        self._draw_label(screen, rx + 8, ry + 6, "Light Presets", (220, 220, 235))

        # Content start
        by = ry + 6 + rrow

        # Spawn Type combo
        combo_h = rrow - 8
        combo_rect = pygame.Rect(rx + 12, by, (rw - 24), combo_h)
        cbg = pygame.Surface(combo_rect.size, pygame.SRCALPHA)
        cbg.fill((35, 35, 42, 230))
        screen.blit(cbg, combo_rect.topleft)
        val_text = f"Spawn Type: {st.spawn_preset}"
        vt = self.font.render(val_text, True, (230, 230, 240))
        screen.blit(vt, (combo_rect.x + 8, combo_rect.y + (combo_rect.height - vt.get_height()) // 2))
        ax = combo_rect.right - 16
        ay = combo_rect.y + combo_rect.height // 2
        tri = [(ax - 6, ay - 3), (ax + 6, ay - 3), (ax, ay + 4)] if not getattr(st, 'spawn_combo_open', False) else [(ax - 6, ay + 3), (ax + 6, ay + 3), (ax, ay - 4)]
        pygame.draw.polygon(screen, (200, 200, 210), tri)
        st._combo_spawn_type = combo_rect
        by += rrow

        # Dropdown items
        st._combo_spawn_items = []
        if getattr(st, 'spawn_combo_open', False):
            items = list(getattr(st, 'spawn_types', ["Torch", "Lamp", "Magic", "Custom"]))
            item_h = combo_h
            drop_h = item_h * len(items)
            drop_rect = pygame.Rect(combo_rect.x, combo_rect.bottom + 2, combo_rect.width, drop_h)
            dd = pygame.Surface(drop_rect.size, pygame.SRCALPHA)
            dd.fill((28, 28, 34, 245))
            screen.blit(dd, drop_rect.topleft)
            for idx, it in enumerate(items):
                ir = pygame.Rect(drop_rect.x, drop_rect.y + idx * item_h, drop_rect.width, item_h)
                try:
                    mx, my = pygame.mouse.get_pos()
                    if ir.collidepoint(mx, my):
                        pygame.draw.rect(screen, (60, 60, 80, 255), ir)
                except Exception:
                    pass
                it_s = self.font.render(it, True, (230, 230, 240))
                screen.blit(it_s, (ir.x + 10, ir.y + (item_h - it_s.get_height()) // 2))
                st._combo_spawn_items.append((ir, it))

        # Preset buttons
        pw = (rw - 24 - 16) // 3
        st._btn_preset_torch = self._draw_button(screen, rx + 12, by, pw, rrow - 8, "Torch", st.spawn_preset == "Torch")
        st._btn_preset_lamp = self._draw_button(screen, rx + 12 + pw + 8, by, pw, rrow - 8, "Lamp", st.spawn_preset == "Lamp")
        st._btn_preset_magic = self._draw_button(screen, rx + 12 + (pw + 8) * 2, by, pw, rrow - 8, "Magic", st.spawn_preset == "Magic")
        by += rrow

        # Param steppers
        def draw_stepper(label: str, val_text: str, minus_attr: str, plus_attr: str) -> None:
            nonlocal by
            bw = 36
            self._draw_label(screen, rx + 12, by - rrow // 2, label, (200, 200, 210))
            st.__dict__[minus_attr] = self._draw_button(screen, rx + 12, by, bw, rrow - 10, "-", False)
            vb = pygame.Surface((rw - 24 - (bw * 2) - 24, rrow - 10), pygame.SRCALPHA)
            vb.fill((35, 35, 42, 220))
            screen.blit(vb, (rx + 12 + bw + 6, by))
            vt = self.font.render(val_text, True, (230, 230, 240))
            screen.blit(vt, (rx + 12 + bw + 14, by + (rrow - 10 - vt.get_height()) // 2))
            st.__dict__[plus_attr] = self._draw_button(screen, rx + rw - 12 - bw, by, bw, rrow - 10, "+", False)
            by += rrow

        draw_stepper("Radius", str(st.spawn_radius), "_btn_sr_minus", "_btn_sr_plus")
        draw_stepper("Intensity", f"{st.spawn_intensity:.2f}", "_btn_si_minus", "_btn_si_plus")
        draw_stepper("Falloff", f"{st.spawn_falloff:.2f}", "_btn_sf_minus", "_btn_sf_plus")
        draw_stepper("Flicker Amp", f"{st.spawn_flicker_amp:.2f}", "_btn_fa_minus", "_btn_fa_plus")
        draw_stepper("Flicker Spd", f"{st.spawn_flicker_speed:.2f}", "_btn_fs_minus", "_btn_fs_plus")
        draw_stepper("Center Scale", f"{getattr(st, 'spawn_center_scale', 1.0):.2f}", "_btn_cs_minus", "_btn_cs_plus")

        # Single-shot toggle
        st._btn_single_shot = self._draw_button(
            screen, rx + 12, by, rw - 24, rrow - 8, f"Single-shot: {'ON' if st.spawn_single_shot else 'OFF'}", st.spawn_single_shot
        )
        by += rrow

        # Color steppers
        r, g, b = st.spawn_color

        def draw_color_stepper(name: str, val: int, minus_attr: str, plus_attr: str):
            nonlocal by
            bw = 36
            self._draw_label(screen, rx + 12, by - rrow // 2, name, (200, 200, 210))
            st.__dict__[minus_attr] = self._draw_button(screen, rx + 12, by, bw, rrow - 10, "-", False)
            vb = pygame.Surface((rw - 24 - (bw * 2) - 24, rrow - 10), pygame.SRCALPHA)
            vb.fill((35, 35, 42, 220))
            screen.blit(vb, (rx + 12 + bw + 6, by))
            vt = self.font.render(str(val), True, (230, 230, 240))
            screen.blit(vt, (rx + 12 + bw + 14, by + (rrow - 10 - vt.get_height()) // 2))
            st.__dict__[plus_attr] = self._draw_button(screen, rx + rw - 12 - bw, by, bw, rrow - 10, "+", False)
            by += rrow

        draw_color_stepper("R", int(r), "_btn_r_minus", "_btn_r_plus")
        draw_color_stepper("G", int(g), "_btn_g_minus", "_btn_g_plus")
        draw_color_stepper("B", int(b), "_btn_b_minus", "_btn_b_plus")

        # Swatch
        swatch = pygame.Surface((rw - 24, rrow - 8))
        swatch.fill((int(r), int(g), int(b)))
        screen.blit(swatch, (rx + 12, by))
        by += rrow

        # Debug controls moved from main panel
        spawn_label = "Spawn Debug Light (Click map)"
        st._btn_spawn_debug = self._draw_button(
            screen, rx + 12, by, rw - 24, rrow - 8, spawn_label, bool(getattr(st, 'spawn_mode', False))
        )
        by += rrow
        st._btn_clear_debug = self._draw_button(
            screen, rx + 12, by, rw - 24, rrow - 8, "Clear Debug Lights", False
        )

    def _draw_label(self, screen: pygame.Surface, x: int, y: int, text: str, color: Tuple[int, int, int]) -> pygame.Rect:
        surf = self.font.render(text, True, color)
        screen.blit(surf, (x, y))
        return pygame.Rect(x, y, surf.get_width(), surf.get_height())

    def _draw_button(self, screen: pygame.Surface, x: int, y: int, w: int, h: int, text: str, on: bool) -> pygame.Rect:
        rect = pygame.Rect(x, y, w, h)
        bg_on = (60, 120, 60, 220)
        bg_off = (120, 60, 60, 220)
        bg = pygame.Surface((w, h), pygame.SRCALPHA)
        bg.fill(bg_on if on else bg_off)
        screen.blit(bg, rect.topleft)
        pygame.draw.rect(screen, (30, 30, 30), rect, width=2)
        surf = self.font.render(text, True, (250, 250, 250))
        tx = x + 10
        ty = y + (h - surf.get_height()) // 2
        screen.blit(surf, (tx, ty))
        return rect


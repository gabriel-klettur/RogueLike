from __future__ import annotations

import pygame
from typing import Tuple

from .day_time_panel_state import DayTimePanelState


class DayTimePanelView:
    def __init__(self, state: DayTimePanelState, font: pygame.font.Font | None = None) -> None:
        self.state = state
        self.font = font or pygame.font.SysFont("consolas", 18)

    def render(self, screen: pygame.Surface, *, anchor_rect: pygame.Rect, row_h: int) -> None:
        st = self.state
        # Reset tooltips for this frame
        try:
            st._tooltips = []
        except Exception:
            pass
        # Query day/night system (guard on optional import)
        try:
            from roguelike_engine.rendering.lighting.daynight import get_global_daynight
            dn = get_global_daynight()
            gh, gm, gs = dn.get_game_time_hms()
            phase = dn.get_phase()
            intensity = float(dn.get_ambient_intensity())
        except Exception:
            dn = None; gh = gm = gs = 0; phase = "?"; intensity = 1.0

        # Preferred panel size
        gap = 12
        rw = 280
        rrow = row_h
        rp_h = rrow * 8 + 12
        sw, sh = screen.get_size()

        # Default position: to the right of the anchor panel
        rx = anchor_rect.x + anchor_rect.w + gap
        ry = anchor_rect.y
        # If it would overflow the screen width, clamp to fit; if still no space, place below
        if rx + rw > sw - 8:
            rx = sw - rw - 8
        if rx < anchor_rect.x + 8:  # not enough room at right, place below main panel
            rx = anchor_rect.x
            ry = anchor_rect.y + anchor_rect.h + 8
            if ry + rp_h > sh - 8:
                ry = max(8, sh - rp_h - 8)

        # Background
        rbg = pygame.Surface((rw, rp_h), pygame.SRCALPHA)
        rbg.fill((20, 20, 28, 200))
        screen.blit(rbg, (rx, ry))
        st._panel_rect = pygame.Rect(rx, ry, rw, rp_h)

        # Title
        self._draw_label(screen, rx + 8, ry + 6, "Daytime Tools", (220, 220, 235))

        # Content
        ty = ry + 6 + rrow
        # Status lines
        t1 = self.font.render(f"Time: {gh:02d}:{gm:02d}:{gs:02d}", True, (230, 230, 240))
        screen.blit(t1, (rx + 12, ty)); ty += rrow
        t2 = self.font.render(f"Phase: {phase}", True, (230, 230, 240))
        screen.blit(t2, (rx + 12, ty)); ty += rrow
        t3 = self.font.render(f"Intensity: {intensity:.2f}", True, (230, 230, 240))
        screen.blit(t3, (rx + 12, ty)); ty += rrow

        # Row of +/- 5m and +/- 30m
        bw = (rw - 24 - 12) // 4
        st._btn_time_m5  = self._draw_button(screen, rx + 12 + (bw + 4) * 0, ty, bw, rrow - 10, "-5m", False)
        st._btn_time_p5  = self._draw_button(screen, rx + 12 + (bw + 4) * 1, ty, bw, rrow - 10, "+5m", False)
        st._btn_time_m30 = self._draw_button(screen, rx + 12 + (bw + 4) * 2, ty, bw, rrow - 10, "-30m", False)
        st._btn_time_p30 = self._draw_button(screen, rx + 12 + (bw + 4) * 3, ty, bw, rrow - 10, "+30m", False)
        ty += rrow

        # Jumps: 05:00, 07:00, 12:00, 19:00, 21:00, 00:00
        jb = (rw - 24 - 8) // 3
        st._btn_time_05 = self._draw_button(screen, rx + 12 + (jb + 4) * 0, ty, jb, rrow - 10, "05:00", False)
        st._btn_time_07 = self._draw_button(screen, rx + 12 + (jb + 4) * 1, ty, jb, rrow - 10, "07:00", False)
        st._btn_time_12 = self._draw_button(screen, rx + 12 + (jb + 4) * 2, ty, jb, rrow - 10, "12:00", False)
        ty += rrow
        st._btn_time_19 = self._draw_button(screen, rx + 12 + (jb + 4) * 0, ty, jb, rrow - 10, "19:00", False)
        st._btn_time_21 = self._draw_button(screen, rx + 12 + (jb + 4) * 1, ty, jb, rrow - 10, "21:00", False)
        st._btn_time_00 = self._draw_button(screen, rx + 12 + (jb + 4) * 2, ty, jb, rrow - 10, "00:00", False)
        ty += rrow

        # Min intensity stepper
        try:
            min_i = float(dn.get_min_intensity()) if dn else 0.2
        except Exception:
            min_i = 0.2
        self._draw_label(screen, rx + 12, ty - rrow // 2, "Min Intensity", (200, 200, 210))
        bw = 36
        vwx = rx + 12 + bw + 6
        st._btn_minI_minus = self._draw_button(screen, rx + 12, ty, bw, rrow - 10, "-", False)
        vb = pygame.Surface((rw - 24 - (bw * 2) - 24, rrow - 10), pygame.SRCALPHA)
        vb.fill((35, 35, 42, 220))
        screen.blit(vb, (vwx, ty))
        vt = self.font.render(f"{min_i:.2f}", True, (230, 230, 240))
        screen.blit(vt, (vwx + 8, ty + (rrow - 10 - vt.get_height()) // 2))
        st._btn_minI_plus = self._draw_button(screen, rx + rw - 12 - bw, ty, bw, rrow - 10, "+", False)
        ty += rrow

        # Time Scale (min/s) stepper
        try:
            ts_val = float(dn.time_scale_minutes_per_second) if dn else 0.4
        except Exception:
            ts_val = 0.4
        self._draw_label(screen, rx + 12, ty - rrow // 2, "Time Scale (min/s)", (200, 200, 210))
        st._btn_ts_minus = self._draw_button(screen, rx + 100, ty, bw, rrow - 10, "-", False)
        vb = pygame.Surface((rw - 24 - 100 - (bw * 2) - 24, rrow - 10), pygame.SRCALPHA)
        vb.fill((35, 35, 42, 220))
        screen.blit(vb, (rx + 100 + bw + 6, ty))
        vt = self.font.render(f"{ts_val:.2f}", True, (230, 230, 240))
        screen.blit(vt, (rx + 100 + bw + 14, ty + (rrow - 10 - vt.get_height()) // 2))
        st._btn_ts_plus = self._draw_button(screen, rx + rw - 12 - bw, ty, bw, rrow - 10, "+", False)
        ty += rrow

        # Keyframe intensities
        self._draw_label(screen, rx + 12, ty - rrow // 2, "Keyframes (intensity)", (200, 200, 210))
        ty += rrow

        def draw_kf_row(lbl: str, val: float, minus_attr: str, plus_attr: str):
            nonlocal ty
            bw2 = 36
            self._draw_label(screen, rx + 12, ty - rrow // 2, lbl, (200, 200, 210))
            st.__dict__[minus_attr] = self._draw_button(screen, rx + 100, ty, bw2, rrow - 10, "-", False)
            vb2 = pygame.Surface((rw - 24 - 100 - (bw2 * 2) - 24, rrow - 10), pygame.SRCALPHA)
            vb2.fill((35, 35, 42, 220))
            screen.blit(vb2, (rx + 100 + bw2 + 6, ty))
            vt2 = self.font.render(f"{val:.2f}", True, (230, 230, 240))
            screen.blit(vt2, (rx + 100 + bw2 + 14, ty + (rrow - 10 - vt2.get_height()) // 2))
            st.__dict__[plus_attr] = self._draw_button(screen, rx + rw - 12 - bw2, ty, bw2, rrow - 10, "+", False)
            ty += rrow

        def gi(m):
            try:
                return float(dn.get_intensity_at_minute(m, apply_floor=False)) if dn else 0.0
            except Exception:
                return 0.0

        for label, minute, mi_attr, pl_attr in (
            ("00:00", 0, "_btn_i_0000_minus", "_btn_i_0000_plus"),
            ("05:00", 300, "_btn_i_0500_minus", "_btn_i_0500_plus"),
            ("07:00", 420, "_btn_i_0700_minus", "_btn_i_0700_plus"),
            ("12:00", 720, "_btn_i_1200_minus", "_btn_i_1200_plus"),
            ("19:00", 1140, "_btn_i_1900_minus", "_btn_i_1900_plus"),
            ("21:00", 1260, "_btn_i_2100_minus", "_btn_i_2100_plus"),
        ):
            draw_kf_row(label, gi(minute), mi_attr, pl_attr)

        # Save button
        st._btn_time_save = self._draw_button(screen, rx + 12, ty, rw - 24, rrow - 10, "Save lighting.json", False)

        # Tooltips (hover) - local to this panel
        def add_tip(rect: pygame.Rect | None, text: str):
            if isinstance(rect, pygame.Rect):
                st._tooltips.append((rect, text))

        # Populate tooltips
        add_tip(st._panel_rect, "Daytime Tools: Atajos para hora, fase e intensidad ambiental.")
        add_tip(st._btn_time_m5, "Retroceder 5 minutos.")
        add_tip(st._btn_time_p5, "Avanzar 5 minutos.")
        add_tip(st._btn_time_m30, "Retroceder 30 minutos.")
        add_tip(st._btn_time_p30, "Avanzar 30 minutos.")
        add_tip(st._btn_time_05, "Ir a 05:00 (Dawn).")
        add_tip(st._btn_time_07, "Ir a 07:00 (Day).")
        add_tip(st._btn_time_12, "Ir a 12:00 (Noon).")
        add_tip(st._btn_time_19, "Ir a 19:00 (Dusk start).")
        add_tip(st._btn_time_21, "Ir a 21:00 (Night).")
        add_tip(st._btn_time_00, "Ir a 00:00 (Midnight).")
        add_tip(st._btn_minI_minus, "Bajar intensidad mínima (piso de noche).")
        add_tip(st._btn_minI_plus, "Subir intensidad mínima (piso de noche).")
        add_tip(st._btn_ts_minus, "Disminuir la velocidad del día (minutos por segundo).")
        add_tip(st._btn_ts_plus, "Aumentar la velocidad del día (minutos por segundo).")
        for r in [
            st._btn_i_0000_minus, st._btn_i_0000_plus,
            st._btn_i_0500_minus, st._btn_i_0500_plus,
            st._btn_i_0700_minus, st._btn_i_0700_plus,
            st._btn_i_1200_minus, st._btn_i_1200_plus,
            st._btn_i_1900_minus, st._btn_i_1900_plus,
            st._btn_i_2100_minus, st._btn_i_2100_plus,
        ]:
            add_tip(r, "Ajustar intensidad del keyframe seleccionado.")
        add_tip(st._btn_time_save, "Guardar configuración en data/config/lighting.json")

        try:
            mx, my = pygame.mouse.get_pos()
            tip = None
            for r, txt in st._tooltips:
                if r.collidepoint(mx, my):
                    tip = txt
                    break
            if tip:
                pad = 6
                ts = self.font.render(tip, True, (15, 15, 20))
                tw, th = ts.get_width() + pad * 2, ts.get_height() + pad * 2
                box = pygame.Surface((tw, th), pygame.SRCALPHA)
                box.fill((245, 245, 250, 235))
                screen.blit(box, (mx + 12, my + 12))
                screen.blit(ts, (mx + 12 + pad, my + 12 + pad))
        except Exception:
            pass

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


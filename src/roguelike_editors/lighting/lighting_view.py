from __future__ import annotations

import pygame
import math
from typing import Tuple

from .lighting_state import LightingEditorState


class LightingEditorView:
    def __init__(self, state: LightingEditorState, font: pygame.font.Font | None = None) -> None:
        self.state = state
        self.font = font or pygame.font.SysFont("consolas", 18)

    def render(self, screen: pygame.Surface, *, ambient_on: bool, lights_on: bool, occlusion_on: bool, shadows_on: bool) -> None:
        st = self.state
        x = st.panel_x
        y = st.panel_y
        w = st.panel_w
        row = st.row_h
        # Panel background (viewport with scrolling)
        vp_h = row * 11 + 12
        bg = pygame.Surface((w, vp_h), pygame.SRCALPHA)
        bg.fill((20, 20, 28, 200))
        screen.blit(bg, (x, y))
        st._panel_rect = pygame.Rect(x, y, w, vp_h)
        # Title
        self._draw_label(screen, x + 8, y + 6, "Lighting Editor", (220, 220, 235))
        # Prepare scissor for scrollable content area
        old_clip = screen.get_clip()
        screen.set_clip(pygame.Rect(x, y, w, vp_h))
        st._tooltips = []
        # Buttons (scrollable content starts below title)
        so = int(getattr(st, 'scroll_offset', 0))
        list_y = y + 6 + row
        by = list_y - so
        st._btn_ambient = self._draw_button(screen, x + 8, by, w - 16, row - 6, f"Ambient: {'ON' if ambient_on else 'OFF'}", ambient_on)
        by += row
        st._btn_lights = self._draw_button(screen, x + 8, by, w - 16, row - 6, f"Point Lights: {'ON' if lights_on else 'OFF'}", lights_on)
        by += row
        # Spawn button: when active, blink in yellow
        spawn_label = "Spawn Debug Light (Click map)"
        if getattr(st, 'spawn_mode', False):
            t = pygame.time.get_ticks() * 0.012
            pulse = 0.5 + 0.5 * math.sin(t)
            base = 140 + int(80 * pulse)
            bg_col = (base, base, 40, 230)
            border_col = (255, 235, 80)
            st._btn_spawn = self._draw_button(screen, x + 8, by, w - 16, row - 6, spawn_label, True, bg_color=bg_col, border_color=border_col)
        else:
            st._btn_spawn = self._draw_button(screen, x + 8, by, w - 16, row - 6, spawn_label, False)
        by += row
        st._btn_clear = self._draw_button(screen, x + 8, by, w - 16, row - 6, "Clear Debug Lights", False)
        by += row
        st._btn_occlusion = self._draw_button(screen, x + 8, by, w - 16, row - 6, f"Tile Occlusion: {'ON' if occlusion_on else 'OFF'}", occlusion_on)
        by += row
        st._btn_shadows = self._draw_button(screen, x + 8, by, w - 16, row - 6, f"Shadows (stub): {'ON' if shadows_on else 'OFF'}", shadows_on)
        # --- Presets ---------------------------------------------------------
        by += row
        lab = self._draw_label(screen, x + 8, by - row // 2, "Presets", (200, 200, 210))
        try:
            st._tooltips.append((lab, "Presets: Configuraciones rápidas de color/radio/flicker."))
        except Exception:
            pass
        # Spawn Type combo (professional selector)
        combo_h = row - 8
        combo_rect = pygame.Rect(x + 8, by, (w - 16), combo_h)
        # Draw combo box background
        combo_bg = pygame.Surface(combo_rect.size, pygame.SRCALPHA)
        combo_bg.fill((35, 35, 42, 230))
        screen.blit(combo_bg, combo_rect.topleft)
        # Value text
        val_text = f"Spawn Type: {st.spawn_preset}"
        vt = self.font.render(val_text, True, (230, 230, 240))
        screen.blit(vt, (combo_rect.x + 8, combo_rect.y + (combo_rect.height - vt.get_height()) // 2))
        # Arrow
        ax = combo_rect.right - 16
        ay = combo_rect.y + combo_rect.height // 2
        tri = [(ax - 6, ay - 3), (ax + 6, ay - 3), (ax, ay + 4)] if not getattr(st, 'spawn_combo_open', False) else [(ax - 6, ay + 3), (ax + 6, ay + 3), (ax, ay - 4)]
        pygame.draw.polygon(screen, (200, 200, 210), tri)
        st._combo_spawn_type = combo_rect
        try:
            st._tooltips.append((combo_rect, "Spawn Type: Selecciona el tipo de luz a spawnear."))
        except Exception:
            pass
        # Dropdown items
        st._combo_spawn_items = []
        if getattr(st, 'spawn_combo_open', False):
            items = list(getattr(st, 'spawn_types', ["Torch", "Lamp", "Magic", "Custom"]))
            item_h = combo_h
            drop_h = item_h * len(items)
            drop_rect = pygame.Rect(combo_rect.x, combo_rect.bottom + 2, combo_rect.width, drop_h)
            # Container
            dd = pygame.Surface(drop_rect.size, pygame.SRCALPHA)
            dd.fill((28, 28, 34, 245))
            screen.blit(dd, drop_rect.topleft)
            for idx, it in enumerate(items):
                ir = pygame.Rect(drop_rect.x, drop_rect.y + idx * item_h, drop_rect.width, item_h)
                # Highlight hovered
                try:
                    mx, my = pygame.mouse.get_pos()
                    if ir.collidepoint(mx, my):
                        pygame.draw.rect(screen, (60, 60, 80, 255), ir)
                except Exception:
                    pass
                it_s = self.font.render(it, True, (230, 230, 240))
                screen.blit(it_s, (ir.x + 10, ir.y + (item_h - it_s.get_height()) // 2))
                st._combo_spawn_items.append((ir, it))
        by += row
        pw = (w - 16 - 16) // 3  # 3 buttons with small gaps
        st._btn_preset_torch = self._draw_button(screen, x + 8, by, pw, row - 8, "Torch", st.spawn_preset == "Torch")
        st._btn_preset_lamp = self._draw_button(screen, x + 8 + pw + 8, by, pw, row - 8, "Lamp", st.spawn_preset == "Lamp")
        st._btn_preset_magic = self._draw_button(screen, x + 8 + (pw + 8) * 2, by, pw, row - 8, "Magic", st.spawn_preset == "Magic")
        by += row
        # --- Spawn params (steppers) ----------------------------------------
        def draw_stepper(label: str, val_text: str, minus_attr: str, plus_attr: str) -> None:
            nonlocal by
            labr = self._draw_label(screen, x + 8, by - row // 2, label, (200, 200, 210))
            try:
                tips = {
                    "Radius": "Radio de la luz en píxeles (coste ↑ con radios grandes).",
                    "Intensity": "Intensidad 0..~2.5 (escala el color de la luz).",
                    "Falloff": "Atenuación exponencial: mayor = borde más suave.",
                    "Flicker Amp": "Amplitud del parpadeo (0..1).",
                    "Flicker Spd": "Velocidad del parpadeo (Hz aprox.).",
                    "LowRes Scale": "Resolución del lightmap (↑ = más rápido y borroso).",
                    "Max Lights": "Límite de luces visibles (cap de rendimiento).",
                    "Max Radius": "Límite de radio por luz (cap artístico/técnico).",
                    "Shadow Heroes": "N luces con sombras poligonales (coste ↑).",
                    "Shadow Rays": "Calidad de sombras (más rayos = más calidad/coste).",
                }
                st._tooltips.append((labr, tips.get(label, label)))
            except Exception:
                pass
            bw = 36
            vwx = x + 8 + bw + 6
            st.__dict__[minus_attr] = self._draw_button(screen, x + 8, by, bw, row - 10, "-", False)
            # Value box (non-clickable)
            vb = pygame.Surface((w - 16 - (bw * 2) - 24, row - 10), pygame.SRCALPHA)
            vb.fill((35, 35, 42, 220))
            screen.blit(vb, (vwx, by))
            vt = self.font.render(val_text, True, (230, 230, 240))
            screen.blit(vt, (vwx + 8, by + (row - 10 - vt.get_height()) // 2))
            st.__dict__[plus_attr] = self._draw_button(screen, x + w - 8 - bw, by, bw, row - 10, "+", False)
            by += row
        draw_stepper("Radius", str(st.spawn_radius), "_btn_sr_minus", "_btn_sr_plus")
        draw_stepper("Intensity", f"{st.spawn_intensity:.2f}", "_btn_si_minus", "_btn_si_plus")
        draw_stepper("Falloff", f"{st.spawn_falloff:.2f}", "_btn_sf_minus", "_btn_sf_plus")
        draw_stepper("Flicker Amp", f"{st.spawn_flicker_amp:.2f}", "_btn_fa_minus", "_btn_fa_plus")
        draw_stepper("Flicker Spd", f"{st.spawn_flicker_speed:.2f}", "_btn_fs_minus", "_btn_fs_plus")
        st._btn_single_shot = self._draw_button(screen, x + 8, by, w - 16, row - 8, f"Single-shot: {'ON' if st.spawn_single_shot else 'OFF'}", st.spawn_single_shot)
        by += row
        # --- Manager tunables -------------------------------------------------
        try:
            from roguelike_engine.rendering.lighting import get_global_lighting
            lm = get_global_lighting()
            lrs = lm.current_low_res_scale()
            ml = lm.current_max_lights()
            mr = lm.current_max_radius()
            shn = lm.get_shadow_hero_count()
            shr = lm.get_shadow_rays()
        except Exception:
            lrs = 2; ml = 12; mr = 192; shn = 1; shr = 64
        draw_stepper("LowRes Scale", str(lrs), "_btn_lrs_minus", "_btn_lrs_plus")
        draw_stepper("Max Lights", str(ml), "_btn_ml_minus", "_btn_ml_plus")
        draw_stepper("Max Radius", str(mr), "_btn_mr_minus", "_btn_mr_plus")
        draw_stepper("Shadow Heroes", str(shn), "_btn_sh_hero_minus", "_btn_sh_hero_plus")
        draw_stepper("Shadow Rays", str(shr), "_btn_sh_rays_minus", "_btn_sh_rays_plus")
        # Time scale (minutes per real second)
        try:
            from roguelike_engine.rendering.lighting.daynight import get_global_daynight
            ts_val = float(get_global_daynight().time_scale_minutes_per_second)
        except Exception:
            ts_val = 0.4
        draw_stepper("Time Scale (min/s)", f"{ts_val:.2f}", "_btn_ts_minus", "_btn_ts_plus")
        # --- Color steppers ---------------------------------------------------
        by += row
        labc = self._draw_label(screen, x + 8, by - row // 2, "Color (RGB)", (200, 200, 210))
        try:
            st._tooltips.append((labc, "Color (RGB): Tono de la luz (0..255 por canal)."))
        except Exception:
            pass
        r, g, b = st.spawn_color
        def draw_color_stepper(name: str, val: int, minus_attr: str, plus_attr: str):
            nonlocal by
            bw = 36
            vwx = x + 8 + bw + 6
            self._draw_label(screen, x + 8, by - row // 2, name, (200, 200, 210))
            st.__dict__[minus_attr] = self._draw_button(screen, x + 8, by, bw, row - 10, "-", False)
            vb = pygame.Surface((w - 16 - (bw * 2) - 24, row - 10), pygame.SRCALPHA)
            vb.fill((35, 35, 42, 220))
            screen.blit(vb, (vwx, by))
            vt = self.font.render(str(val), True, (230, 230, 240))
            screen.blit(vt, (vwx + 8, by + (row - 10 - vt.get_height()) // 2))
            st.__dict__[plus_attr] = self._draw_button(screen, x + w - 8 - bw, by, bw, row - 10, "+", False)
            by += row
        draw_color_stepper("R", int(r), "_btn_r_minus", "_btn_r_plus")
        draw_color_stepper("G", int(g), "_btn_g_minus", "_btn_g_plus")
        draw_color_stepper("B", int(b), "_btn_b_minus", "_btn_b_plus")
        # Swatch
        sw = pygame.Surface((w - 16, row - 8))
        sw.fill((int(r), int(g), int(b)))
        screen.blit(sw, (x + 8, by))
        by += row
        # Update content height and restore clip
        st._content_height = max(0, int(by - (list_y - so)))
        screen.set_clip(old_clip)
        st._viewport_rect = pygame.Rect(x, y, w, vp_h)
        # --- Right-side Daytime panel (draw outside clipped area) -------------
        try:
            from roguelike_engine.rendering.lighting.daynight import get_global_daynight
            dn = get_global_daynight()
            gh, gm, gs = dn.get_game_time_hms()
            phase = dn.get_phase()
            intensity = float(dn.get_ambient_intensity())
        except Exception:
            dn = None; gh = gm = gs = 0; phase = "?"; intensity = 1.0
        gap = 12
        rw = 280
        rrow = row
        rp_h = rrow * 8 + 12
        sw, sh = screen.get_size()
        # Default position: to the right of main panel
        rx = x + w + gap
        ry = y
        # If it would overflow screen width, clamp to fit; if still no space, place below
        if rx + rw > sw - 8:
            rx = sw - rw - 8
        if rx < x + 8:  # not enough room at right, place below main panel
            rx = x
            ry = y + vp_h + 8
            # If overflows bottom, clamp upward
            if ry + rp_h > sh - 8:
                ry = max(8, sh - rp_h - 8)
        rbg = pygame.Surface((rw, rp_h), pygame.SRCALPHA)
        rbg.fill((20, 20, 28, 200))
        screen.blit(rbg, (rx, ry))
        st._panel_time_rect = pygame.Rect(rx, ry, rw, rp_h)
        # Title
        self._draw_label(screen, rx + 8, ry + 6, "Daytime Tools", (220, 220, 235))
        # Content
        ty = ry + 6 + rrow
        # Status lines
        t1 = self.font.render(f"Time: {gh:02d}:{gm:02d}:{gs:02d}", True, (230,230,240))
        screen.blit(t1, (rx + 12, ty)); ty += rrow
        t2 = self.font.render(f"Phase: {phase}", True, (230,230,240))
        screen.blit(t2, (rx + 12, ty)); ty += rrow
        t3 = self.font.render(f"Intensity: {intensity:.2f}", True, (230,230,240))
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
        # Scrollbar
        track = pygame.Rect(x + w - 8, y + 6, 6, vp_h - 12)
        st._scrollbar_track = track
        # Thumb proportional height
        ch = max(1, st._content_height)
        if ch <= vp_h:
            thumb_h = track.height
            thumb_y = track.top
        else:
            thumb_h = max(20, int(track.height * (vp_h / ch)))
            max_off = ch - vp_h
            frac = max(0.0, min(1.0, float(so) / float(max_off)))
            thumb_y = track.top + int((track.height - thumb_h) * frac)
        thumb = pygame.Rect(track.left, thumb_y, track.width, thumb_h)
        st._scrollbar_thumb = thumb
        # Draw scrollbar
        pygame.draw.rect(screen, (60, 60, 70), track)
        pygame.draw.rect(screen, (180, 180, 200), thumb)
        # Tooltips (hover)
        def add_tip(rect: pygame.Rect | None, text: str):
            if isinstance(rect, pygame.Rect):
                st._tooltips.append((rect, text))
        add_tip(st._btn_ambient, "Ambient: Multiplica la escena por un tinte global (día/noche).")
        # Daytime panel tooltips
        add_tip(st._panel_time_rect, "Daytime Tools: Atajos para hora, fase e intensidad ambiental.")
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

    def _draw_button(self, screen: pygame.Surface, x: int, y: int, w: int, h: int, text: str, on: bool, *, bg_color: Tuple[int,int,int,int] | None = None, border_color: Tuple[int,int,int] | None = None) -> pygame.Rect:
        rect = pygame.Rect(x, y, w, h)
        # Background color by state
        bg_on = (60, 120, 60, 220)
        bg_off = (120, 60, 60, 220)
        bg = pygame.Surface((w, h), pygame.SRCALPHA)
        fill_col = bg_color if bg_color is not None else (bg_on if on else bg_off)
        bg.fill(fill_col)
        screen.blit(bg, rect.topleft)
        # Border
        pygame.draw.rect(screen, border_color if border_color is not None else (30, 30, 30), rect, width=2)
        # Text
        surf = self.font.render(text, True, (250, 250, 250))
        tx = x + 10
        ty = y + (h - surf.get_height()) // 2
        screen.blit(surf, (tx, ty))
        return rect

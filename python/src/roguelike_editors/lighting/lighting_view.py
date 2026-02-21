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
        # Spawn/Clear controls moved to Light Presets panel
        st._btn_occlusion = self._draw_button(screen, x + 8, by, w - 16, row - 6, f"Tile Occlusion: {'ON' if occlusion_on else 'OFF'}", occlusion_on)
        by += row
        st._btn_shadows = self._draw_button(screen, x + 8, by, w - 16, row - 6, f"Shadows (stub): {'ON' if shadows_on else 'OFF'}", shadows_on)
        # Helper for manager tunables
        def draw_stepper(label: str, val_text: str, minus_attr: str, plus_attr: str) -> None:
            nonlocal by
            labr = self._draw_label(screen, x + 8, by - row // 2, label, (200, 200, 210))
            try:
                tips = {
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
            vb = pygame.Surface((w - 16 - (bw * 2) - 24, row - 10), pygame.SRCALPHA)
            vb.fill((35, 35, 42, 220))
            screen.blit(vb, (vwx, by))
            vt = self.font.render(val_text, True, (230, 230, 240))
            screen.blit(vt, (vwx + 8, by + (row - 10 - vt.get_height()) // 2))
            st.__dict__[plus_attr] = self._draw_button(screen, x + w - 8 - bw, by, bw, row - 10, "+", False)
            by += row
        # Overlay toggles
        by += row
        st._btn_overlay = self._draw_button(screen, x + 8, by, w - 16, row - 6, f"Overlay: {'ON' if getattr(st, 'overlay_visible', True) else 'OFF'}", bool(getattr(st, 'overlay_visible', True)))
        by += row
        st._btn_labels = self._draw_button(screen, x + 8, by, w - 16, row - 6, f"Labels: {'ON' if getattr(st, 'overlay_labels', True) else 'OFF'}", bool(getattr(st, 'overlay_labels', True)))
        by += row
        st._btn_delete_selected = self._draw_button(screen, x + 8, by, w - 16, row - 6, "Delete Selected", False)
        by += row
        # Preset color palette controls (for current hovered/selected preset)
        bw = (w - 16 - 8) // 2
        st._btn_palette_prev = self._draw_button(screen, x + 8, by, bw, row - 6, "Preset Color <", False)
        st._btn_palette_next = self._draw_button(screen, x + 16 + bw, by, bw, row - 6, "Preset Color >", False)
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
        # Time scale moved to Daytime Tools panel
        # Update content height and restore clip
        st._content_height = max(0, int(by - (list_y - so)))
        screen.set_clip(old_clip)
        st._viewport_rect = pygame.Rect(x, y, w, vp_h)
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
        add_tip(st._btn_overlay, "Overlay: Muestra bordes de instancias persistentes de luz.")
        add_tip(st._btn_labels, "Labels: Muestra #id, preset y radio junto a cada borde.")
        add_tip(st._btn_delete_selected, "Delete Selected: Elimina todas las instancias seleccionadas.")
        add_tip(st._btn_palette_prev, "Preset Color <: Cambia al color anterior para el preset actual.")
        add_tip(st._btn_palette_next, "Preset Color >: Cambia al siguiente color para el preset actual.")
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

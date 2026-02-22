from __future__ import annotations

from typing import Dict, List, Optional, Tuple
import pygame

from roguelike_ui.widgets.menu_renderer.menu_renderer import MenuRenderer
from .model import SoundSettingsModel


class SoundsView:
    def __init__(
        self,
        font=None,
        base_font_size: Optional[int] = None,
        underlay_provider=None,
    ) -> None:
        if isinstance(base_font_size, int) and base_font_size > 6:
            self.renderer = MenuRenderer(font_size=int(base_font_size))
        else:
            try:
                base_size = font.get_height() if font else 18
                fs = int(base_size)
            except Exception:
                fs = 18
            self.renderer = MenuRenderer(font_size=fs)
        self.underlay_provider = underlay_provider
        self._fixed_panel_size: Optional[Tuple[int, int]] = None
        self._fixed_screen_size: Optional[Tuple[int, int]] = None
        self.last_layout: Dict[str, List[Optional[pygame.Rect]]] = {}
        self.last_viewport_h: int = 0
        self.max_scroll: float = 0.0

    def compute_fixed_layout(self, screen: pygame.Surface) -> None:
        sw, sh = screen.get_size()
        w = min(int(sw * 0.8), max(520, int(sw * 0.7)))
        title_h = self.renderer.line_height
        rows_h = 14 * (self.renderer.line_height + self.renderer.item_gap)
        instr_h = 3 * self.renderer.line_height
        h = self.renderer.padding_y * 2 + title_h + self.renderer.item_gap + rows_h + self.renderer.item_gap + instr_h
        h = min(h, int(sh * 0.7))
        self._fixed_panel_size = (w, h)
        self._fixed_screen_size = (sw, sh)

    def ensure_visible(self, row_index: int, scroll: float) -> float:
        content_top = self.renderer.padding_y + self.renderer.line_height + self.renderer.item_gap
        h = (self._fixed_panel_size or (0, 0))[1]
        if not h:
            return scroll
        legends_h = 4 * self.renderer.line_height
        content_bottom = h - self.renderer.padding_y - legends_h
        viewport_h = max(0, content_bottom - content_top)
        self.last_viewport_h = viewport_h
        row_h = self.renderer.line_height + self.renderer.item_gap
        row_y = content_top + row_index * row_h
        if row_y < content_top + scroll:
            return max(0.0, row_y - content_top)
        if row_y + self.renderer.line_height > content_top + scroll + viewport_h:
            return min(max(0.0, row_y + self.renderer.line_height - content_top - viewport_h), self.max_scroll)
        return scroll

    def draw(
        self,
        screen: pygame.Surface,
        model: SoundSettingsModel,
        selected: int,
        scroll: float,
        hover_value_idx: Optional[int],
        hover_mute_idx: Optional[int],
        hover_reset_idx: Optional[int],
    ) -> pygame.Rect:
        panel_top_min = None
        if callable(self.underlay_provider):
            try:
                panel_top_min = self.underlay_provider(screen)
            except Exception:
                panel_top_min = None

        overlay_rect = self.renderer._draw_overlay(screen)

        if not self._fixed_panel_size or self._fixed_screen_size != screen.get_size():
            self.compute_fixed_layout(screen)
        w, h = self._fixed_panel_size  # type: ignore[assignment]
        panel_rect = self.renderer._center_rect(screen, (w, h))
        if isinstance(panel_top_min, int) and panel_rect.top < panel_top_min:
            panel_rect.top = panel_top_min
        self.renderer._draw_shadow(screen, panel_rect)
        panel = self.renderer._draw_panel((w, h))

        title = self.renderer.font.render("Opciones de Sonido", True, self.renderer.text_color)
        panel.blit(title, (self.renderer.padding_x, self.renderer.padding_y))

        content_top = self.renderer.padding_y + self.renderer.line_height + self.renderer.item_gap
        legends_lines = 4
        legends_h = legends_lines * self.renderer.line_height
        content_bottom = h - self.renderer.padding_y - legends_h
        viewport_h = max(0, content_bottom - content_top)
        self.last_viewport_h = viewport_h
        row_h = self.renderer.line_height + self.renderer.item_gap
        content_total_h = 14 * row_h
        self.max_scroll = max(0.0, content_total_h - viewport_h)
        scroll = max(0.0, min(self.max_scroll, float(scroll)))

        y = content_top - scroll
        layout: Dict[str, List[Optional[pygame.Rect]]] = {
            "slider_rects": [],
            "value_rects": [],
            "mute_rects": [],
            "reset_rects": [],
        }

        labels = [
            "Música",
            "Ambiente",
            "SFX",
            "Intro (track)",
            "In-game (track)",
            "Ambiente: intervalo mínimo (s)",
            "Ambiente: intervalo máximo (s)",
            "Ducking: atenuación (dB)",
            "Ducking: hold (ms)",
            "Ducking: release (ms)",
            "Zona (seleccionar)",
            "Zona: música (track)",
            "Zona: ambiente mínimo (s)",
            "Zona: ambiente máximo (s)",
        ]

        label_max_w = 0
        for lbl in labels:
            tw, _ = self.renderer.font.size(lbl)
            label_max_w = max(label_max_w, tw)
        label_col_w = label_max_w + 20

        for i in range(14):
            row_top = y
            row_bottom = y + self.renderer.line_height
            if row_bottom < content_top or row_top > content_bottom:
                layout["slider_rects"].append(None)
                layout["value_rects"].append(None)
                layout["mute_rects"].append(None)
                layout["reset_rects"].append(None)
                y += row_h
                continue

            key = ("music", "ambient", "sfx")[i] if i in (0, 1, 2) else None
            is_sel = (i == selected)
            color = self.renderer.accent_color if is_sel else self.renderer.text_color
            if is_sel:
                row_pill = pygame.Rect(self.renderer.padding_x - 6, y, w - (self.renderer.padding_x * 2) + 12, self.renderer.line_height)
                pygame.draw.rect(panel, self.renderer.highlight_color, row_pill, border_radius=self.renderer.radius // 2)

            t = self.renderer.font.render(labels[i], True, color)
            panel.blit(t, (self.renderer.padding_x, y))

            percent_probe = self.renderer.font.render("100%", True, color)
            percent_w = percent_probe.get_width()
            btn_label = "Mute" if not (model.muted.get(key, False) if key else False) else "Unmute"
            bt = self.renderer.font.render(btn_label, True, self.renderer.text_color)
            rt = self.renderer.font.render("Def.", True, self.renderer.text_color)
            btn_pad_x = 14
            btn_w = bt.get_width() + btn_pad_x * 2
            reset_w = rt.get_width() + btn_pad_x * 2
            gap_btns = 12
            right_reserved = 12 + percent_w + 16 + btn_w + gap_btns + reset_w
            btn_rect = None
            reset_rect = None

            slider_h = 10
            sx = self.renderer.padding_x + max(120, label_col_w)
            sy = y + (self.renderer.line_height - slider_h) // 2
            slider_w = max(160, w - self.renderer.padding_x * 2 - (sx - self.renderer.padding_x) - right_reserved)
            track_rect = pygame.Rect(sx, sy, max(0, slider_w), slider_h)
            pygame.draw.rect(panel, (255, 255, 255, 35), track_rect, border_radius=6)
            tick_color = (255, 255, 255, 60)

            if i in (0, 1, 2):
                for tck in (0.0, 0.25, 0.5, 0.75, 1.0):
                    tx = sx + int(slider_w * tck)
                    pygame.draw.line(panel, tick_color, (tx, sy - 4), (tx, sy + slider_h + 4), 1)
                assert key is not None
                val = model.values[key]
                thumb_w = max(4, int(slider_w * val))
                pygame.draw.rect(panel, self.renderer.accent_color, pygame.Rect(sx, sy, thumb_w, slider_h), border_radius=6)
            elif i in (3, 4):
                track = model.intro_track if i == 3 else model.ingame_track
                disp = track or "(sin tracks)"
                tt = self.renderer.font.render(disp, True, color)
                panel.blit(tt, (sx + 8, y + (self.renderer.line_height - tt.get_height()) // 2))
            elif i in (5, 6):
                val = model.ambient_min if i == 5 else model.ambient_max
                vtxt = self.renderer.font.render(f"{val:.1f}s", True, color)
                panel.blit(vtxt, (sx + 8, y + (self.renderer.line_height - vtxt.get_height()) // 2))
            elif i in (7, 8, 9):
                if i == 7:
                    disp = f"{model.duck_db:.0f} dB"
                elif i == 8:
                    disp = f"{model.duck_hold} ms"
                else:
                    disp = f"{model.duck_release} ms"
                vtxt = self.renderer.font.render(disp, True, color)
                panel.blit(vtxt, (sx + 8, y + (self.renderer.line_height - vtxt.get_height()) // 2))
            elif i == 10:
                disp = "(sin zonas)"
                if model.zones:
                    try:
                        disp = str(model.zones[model.zone_index])
                    except Exception:
                        disp = str(model.zones[0])
                vt = self.renderer.font.render(disp, True, color)
                panel.blit(vt, (sx + 8, y + (self.renderer.line_height - vt.get_height()) // 2))
            elif i == 11:
                zname = model.zones[model.zone_index] if model.zones else None
                tr = model.zone_track.get(zname) if zname else None
                disp = tr or "(sin tracks)"
                vt = self.renderer.font.render(disp, True, color)
                panel.blit(vt, (sx + 8, y + (self.renderer.line_height - vt.get_height()) // 2))
            elif i in (12, 13):
                zname = model.zones[model.zone_index] if model.zones else None
                val = float(model.zone_ambient_min.get(zname, model.ambient_min) if i == 12 else model.zone_ambient_max.get(zname, model.ambient_max))
                vtxt = self.renderer.font.render(f"{val:.1f}s", True, color)
                panel.blit(vtxt, (sx + 8, y + (self.renderer.line_height - vtxt.get_height()) // 2))

            if i in (0, 1, 2):
                btn_h = self.renderer.line_height
                bx = min(self.renderer.padding_x + w - self.renderer.padding_x - btn_w, sx + slider_w + 16)
                by = y
                btn_rect = pygame.Rect(bx, by, btn_w, btn_h)
                pygame.draw.rect(panel, (255, 255, 255, 22), btn_rect, border_radius=self.renderer.radius // 2)
                is_hover_btn = (hover_mute_idx == i)
                if is_hover_btn or (model.muted.get(key, False) if key else False):
                    pygame.draw.rect(panel, self.renderer.border_color, btn_rect, width=2, border_radius=self.renderer.radius // 2)
                btx = bx + (btn_w - bt.get_width()) // 2
                bty = by + (btn_h - bt.get_height()) // 2
                panel.blit(bt, (btx, bty))

                rx = min(self.renderer.padding_x + w - self.renderer.padding_x - reset_w, bx + btn_w + gap_btns)
                ry = y
                reset_rect = pygame.Rect(rx, ry, reset_w, btn_h)
                pygame.draw.rect(panel, (255, 255, 255, 22), reset_rect, border_radius=self.renderer.radius // 2)
                is_hover_reset = (hover_reset_idx == i)
                if is_hover_reset:
                    pygame.draw.rect(panel, self.renderer.border_color, reset_rect, width=2, border_radius=self.renderer.radius // 2)
                rtx = rx + (reset_w - rt.get_width()) // 2
                rty = ry + (btn_h - rt.get_height()) // 2
                panel.blit(rt, (rtx, rty))

            layout["slider_rects"].append(track_rect.move(panel_rect.topleft))
            val_surface = None
            if i in (0, 1, 2):
                assert key is not None
                val = model.values[key]
                vtxt = self.renderer.font.render(f"{val*100:.0f}%", True, color)
                val_surface = vtxt
            elif i in (3, 4):
                track = model.intro_track if i == 3 else model.ingame_track
                disp = track or "(sin tracks)"
                tt = self.renderer.font.render(disp, True, color)
                val_surface = tt
            elif i in (5, 6):
                val = model.ambient_min if i == 5 else model.ambient_max
                vtxt = self.renderer.font.render(f"{val:.1f}s", True, color)
                val_surface = vtxt
            elif i in (7, 8, 9):
                if i == 7:
                    disp = f"{model.duck_db:.0f} dB"
                elif i == 8:
                    disp = f"{model.duck_hold} ms"
                else:
                    disp = f"{model.duck_release} ms"
                vtxt = self.renderer.font.render(disp, True, color)
                val_surface = vtxt
            elif i == 10:
                disp = "(sin zonas)"
                if model.zones:
                    try:
                        disp = str(model.zones[model.zone_index])
                    except Exception:
                        disp = str(model.zones[0])
                vt = self.renderer.font.render(disp, True, color)
                val_surface = vt
            elif i == 11:
                zname = model.zones[model.zone_index] if model.zones else None
                tr = model.zone_track.get(zname) if zname else None
                disp = tr or "(sin tracks)"
                vt = self.renderer.font.render(disp, True, color)
                val_surface = vt
            elif i in (12, 13):
                zname = model.zones[model.zone_index] if model.zones else None
                val = float(model.zone_ambient_min.get(zname, model.ambient_min) if i == 12 else model.zone_ambient_max.get(zname, model.ambient_max))
                vtxt = self.renderer.font.render(f"{val:.1f}s", True, color)
                val_surface = vtxt
            if val_surface is not None:
                vx = sx + 8
                vy = y + (self.renderer.line_height - val_surface.get_height()) // 2
                panel.blit(val_surface, (vx, vy))
                layout["value_rects"].append(pygame.Rect(vx, vy, val_surface.get_width(), val_surface.get_height()).move(panel_rect.topleft))
            else:
                layout["value_rects"].append(None)
            layout["mute_rects"].append(btn_rect.move(panel_rect.topleft) if btn_rect else None)
            layout["reset_rects"].append(reset_rect.move(panel_rect.topleft) if reset_rect else None)

            y += self.renderer.line_height + self.renderer.item_gap

        lines = [
            "Arriba/Abajo: seleccionar | Izq/Der: ajustar | P: Aplicar ahora",
            "Volúmenes: 0..100% | Tracks: ←/→ | Ambiente (s) y Ducking: ←/→ | Rueda: scroll",
            "Zonas: selecciona zona y asigna track e intervalos de ambiente",
            "R: reset volúmenes y avanzados por defecto | D: Def. (solo canal seleccionado) | M: Mute | ESC: volver",
        ]
        iy = h - self.renderer.padding_y - len(lines) * (self.renderer.line_height)
        for line in lines:
            lt = self.renderer.font.render(line, True, self.renderer.text_color_dim)
            panel.blit(lt, (self.renderer.padding_x, iy))
            iy += self.renderer.line_height

        surface_to_blit = panel._surf if hasattr(panel, "_surf") else panel
        screen.blit(surface_to_blit, panel_rect.topleft)

        self.last_layout = layout
        return overlay_rect

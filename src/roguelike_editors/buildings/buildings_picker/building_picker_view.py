import os
import pygame
import logging
from typing import Optional
from roguelike_engine.utils.loader import load_image
from roguelike_editors.buildings.buildings_editor_config import (
    THUMB_SIZE, THUMB_PADDING, NAV_HEIGHT,
    COLOR_BORDER, COLOR_HIGHLIGHT, ICON_BACK
)
from roguelike_ui.ui_blocker import register_blocker

class PickerView:
    def __init__(self, editor_state):
        self.editor = editor_state
        # Icono de “atrás”
        self.back_icon = load_image(ICON_BACK, (NAV_HEIGHT, NAV_HEIGHT))
        # Icono de carpeta
        self.folder_icon = load_image("assets/ui/folder_win.png")
        self.folder_icon = pygame.transform.scale(self.folder_icon, (THUMB_SIZE, THUMB_SIZE))
        # Fuente para el label de las carpetas
        self._label_font = pygame.font.SysFont(None, THUMB_SIZE // 4)
        # Fuente para la ruta debajo del grid
        self._path_font = pygame.font.SysFont(None, 14)
        # Cache de miniaturas
        self.thumb_cache: dict[str, pygame.Surface] = {}

        # Estilo consistente con Items Picker Panel
        self.cell_w = THUMB_SIZE
        self.cell_h = THUMB_SIZE
        self.padding = THUMB_PADDING
        self.margin = 8
        self.max_columns: Optional[int] = 12
        self.select_color = (0, 200, 255)

        # Anclas opcionales inyectadas por el orquestador (e.g. toolbars/título)
        self._top_anchor_y: Optional[int] = None
        self._left_anchor_x: Optional[int] = None
        self._reserved_bottom_h: Optional[int] = None

        # Debug snapshots
        self._last_grid_rect = None
        self._last_reserved_h = None

    def render(self, screen, camera):
        sw, sh = screen.get_size()

        # 1) Calcular rect del panel (sin navbar; back como primer icono)
        margin = 20
        reserve_h = self._reserved_bottom_h if self._reserved_bottom_h is not None else 0
        top_anchor_y = self._top_anchor_y if self._top_anchor_y is not None else margin
        grid_top = max(margin, top_anchor_y)
        left_anchor_x = self._left_anchor_x if self._left_anchor_x is not None else margin
        avail_w = max(0, sw - left_anchor_x - margin - 2 * self.margin)

        # Columnas en función del ancho disponible y límites
        cols_fit = max(1, (avail_w + self.padding) // (self.cell_w + self.padding))
        if self.max_columns:
            cols_fit = min(cols_fit, self.max_columns)

        # Filas visibles en función de cantidad de entradas (+1 si hay back)
        try:
            count = len(self.editor.entries)
        except Exception:
            count = 0
        has_back = bool(getattr(self.editor, 'history', []))
        total = count + (1 if has_back else 0)
        rows_needed = 0 if total == 0 else ((total + cols_fit - 1) // cols_fit)
        visible_rows = min(3, max(1, rows_needed))
        needs_scroll = rows_needed > visible_rows

        grid_area_w = cols_fit * self.cell_w + max(0, (cols_fit - 1) * self.padding)
        grid_area_h = visible_rows * self.cell_h + max(0, (visible_rows - 1) * self.padding)
        panel_w = grid_area_w + 2 * self.margin
        # Reservar área para la ruta bajo el grid
        path_h = self._path_font.get_height() + 6
        panel_h = grid_area_h + 2 * self.margin + path_h
        # Altura disponible considerando reserva inferior
        avail_h = max(0, sh - grid_top - margin - reserve_h)
        rect_w = min(panel_w, max(0, sw - left_anchor_x - margin))
        rect_h = min(panel_h, avail_h)
        panel_rect = pygame.Rect(left_anchor_x, grid_top, rect_w, rect_h)

        # Exponer métricas a eventos para hit-testing
        try:
            self.editor.picker_panel_rect = panel_rect
            self.editor.picker_internal_margin = self.margin
            self.editor.picker_cell_w = self.cell_w
            self.editor.picker_cell_h = self.cell_h
            self.editor.picker_padding = self.padding
            self.editor.picker_footer_h = path_h
            self.editor.picker_visible_rows = visible_rows
            self.editor.picker_max_columns = self.max_columns
            self.editor.picker_rows_needed = rows_needed
            self.editor.picker_needs_scroll = needs_scroll
            self.editor.picker_scrollbar_w = 10
            if getattr(self.editor, 'picker_scroll_row', None) is None:
                self.editor.picker_scroll_row = 0
        except Exception:
            pass

        # 2) Fondo semitransparente del panel y registro de bloqueo de UI
        if panel_rect.w > 0 and panel_rect.h > 0:
            bg = pygame.Surface(panel_rect.size, pygame.SRCALPHA)
            bg.fill((20, 20, 20, 180))
            screen.blit(bg, panel_rect.topleft)
            try:
                register_blocker(panel_rect)
            except Exception:
                pass

        # Logs de depuración si el rect cambia
        try:
            if self._last_grid_rect != panel_rect or self._last_reserved_h != reserve_h:
                logging.getLogger(__name__).debug(
                    f"[BuildingPickerView] panel_rect={panel_rect} reserve_h={reserve_h}"
                )
                self._last_grid_rect = panel_rect.copy()
                self._last_reserved_h = reserve_h
        except Exception:
            pass

        # 3) Thumbnails dentro del panel (con primer icono = atrás si hay historial)
        hovered_entry = self._draw_thumbnails(screen, panel_rect)

        # 4) Ruta del archivo debajo del grid (amarillo)
        try:
            path_text = None
            if hovered_entry is not None:
                path_text = hovered_entry.path
            elif getattr(self.editor, 'selected_entry', None) is not None:
                path_text = self.editor.selected_entry.path
            else:
                path_text = getattr(self.editor, 'current_dir', "")
            if path_text:
                txt = self._ellipsize_path(path_text, panel_rect.w - 2 * self.margin, self._path_font)
                txt_surf = self._path_font.render(txt, True, (255, 255, 0))
                tx = panel_rect.left + self.margin
                ty = panel_rect.bottom - self.margin - txt_surf.get_height()
                screen.blit(txt_surf, (tx, ty))
        except Exception:
            pass

        # 5) Efecto de borde parpadeante cuando hay drag activo
        if (pygame.time.get_ticks() // 500) % 2 == 0 and self.editor.dragging_building:
            pygame.draw.rect(screen, (255, 255, 0), panel_rect.inflate(6, 6), 3)

        # 6) Preview de drag (si aplica)
        if self.editor.dragging_building and self.editor.selected_entry:
            self._draw_drag_preview(screen)

    # (Navbar eliminado: back se maneja como primer icono del grid)

    def _draw_thumbnails(self, screen, panel_rect: pygame.Rect):
        entries = self.editor.entries
        # Área disponible para la grilla (sin navbar)
        gx = panel_rect.left + self.margin
        gy = panel_rect.top + self.margin
        gw = max(0, panel_rect.w - 2 * self.margin)
        footer_h = getattr(self.editor, 'picker_footer_h', self._path_font.get_height() + 6)
        gh = max(0, panel_rect.h - 2 * self.margin - footer_h)
        # Reservar espacio para scrollbar si aplica
        needs_scroll = bool(getattr(self.editor, 'picker_needs_scroll', False))
        sb_pad = 4
        sb_w = int(getattr(self.editor, 'picker_scrollbar_w', 10) or 10) if needs_scroll else 0
        gw_effective = max(0, gw - (sb_w + (sb_pad if needs_scroll else 0)))

        cols = max(1, (gw_effective + self.padding) // (self.cell_w + self.padding))
        max_cols = getattr(self.editor, 'picker_max_columns', self.max_columns)
        if max_cols:
            cols = min(cols, max_cols)
        # Total de iconos visibles (incluye 'atrás' como primer icono si hay historial)
        has_back = bool(getattr(self.editor, 'history', []))
        total = len(entries) + (1 if has_back else 0)

        # Desplazamiento vertical por scroll (en filas)
        scroll_row = int(getattr(self.editor, 'picker_scroll_row', 0) or 0)
        visible_rows = int(getattr(self.editor, 'picker_visible_rows', 3) or 3)

        # Construir rects visibles únicamente
        self.item_rects = []
        hovered_entry = None
        mouse_pos = pygame.mouse.get_pos()
        first_vidx = scroll_row * cols
        last_vidx = min(total, first_vidx + visible_rows * cols)

        for vidx in range(first_vidx, last_vidx):
            rel = vidx - first_vidx
            row = rel // cols
            col = rel % cols
            x = gx + col * (self.cell_w + self.padding)
            y = gy + row * (self.cell_h + self.padding)
            rect = pygame.Rect(x, y, self.cell_w, self.cell_h)

            # Borde
            pygame.draw.rect(screen, COLOR_BORDER, rect, 1)

            if has_back and vidx == 0:
                # Dibuja icono de volver como primer elemento
                try:
                    back = pygame.transform.smoothscale(self.back_icon, (self.cell_w, self.cell_h))
                except Exception:
                    back = self.back_icon
                screen.blit(back, (x, y))
                if rect.collidepoint(mouse_pos):
                    hovered_entry = None  # back no tiene path de archivo
                continue

            # Resto: archivos/directorios reales
            entry_idx = vidx - (1 if has_back else 0)
            entry = entries[entry_idx]
            if entry.is_dir:
                # Icono de carpeta
                screen.blit(self.folder_icon, (x, y))
                # Nombre de la carpeta centrado dentro del icono
                try:
                    name_surf = self._label_font.render(entry.name, True, (220, 220, 220))
                    # Fondo semitransparente para mejorar legibilidad
                    pad = 4
                    bg_rect = name_surf.get_rect(center=rect.center).inflate(pad*2, pad)
                    bg = pygame.Surface(bg_rect.size, pygame.SRCALPHA)
                    bg.fill((0, 0, 0, 140))
                    screen.blit(bg, bg_rect.topleft)
                    name_rect = name_surf.get_rect(center=rect.center)
                    screen.blit(name_surf, name_rect)
                except Exception:
                    pass
            else:
                thumb = self.thumb_cache.get(entry.path)
                if not thumb:
                    img = load_image(entry.path)
                    thumb = pygame.transform.scale(img, (self.cell_w, self.cell_h))
                    self.thumb_cache[entry.path] = thumb
                screen.blit(thumb, (x, y))

            # Hover detection
            if rect.collidepoint(mouse_pos):
                hovered_entry = entry

        # Dibujar barra de scroll si es necesaria
        if needs_scroll:
            track_x = gx + gw_effective + sb_pad
            track_y = gy
            track_h = gh
            track_rect = pygame.Rect(track_x, track_y, sb_w, track_h)
            # Track
            track = pygame.Surface((sb_w, track_h), pygame.SRCALPHA)
            track.fill((50, 50, 50, 150))
            screen.blit(track, (track_x, track_y))
            # Thumb
            total_rows = max(1, int(getattr(self.editor, 'picker_rows_needed', visible_rows)))
            vis_rows = max(1, int(visible_rows))
            ratio = min(1.0, vis_rows / total_rows)
            thumb_h = max(12, int(ratio * track_h))
            max_thumb_y = track_h - thumb_h
            denom = max(1, total_rows - vis_rows)
            thumb_y = int((scroll_row / denom) * max_thumb_y) if denom > 0 else 0
            thumb_rect = pygame.Rect(track_x, track_y + thumb_y, sb_w, thumb_h)
            pygame.draw.rect(screen, (255, 255, 0), thumb_rect)
            # Exponer rects para eventos
            try:
                self.editor.picker_scroll_track_rect = track_rect
                self.editor.picker_scroll_thumb_rect = thumb_rect
            except Exception:
                pass

        return hovered_entry

    def _ellipsize_path(self, text: str, max_width: int, font: pygame.font.Font) -> str:
        """Recorta el comienzo del texto con '...' para que entre en max_width."""
        if font.size(text)[0] <= max_width:
            return text
        prefix = "..."
        # Recortar desde el inicio hasta que quepa
        start = 0
        while start < len(text):
            candidate = prefix + text[start:]
            if font.size(candidate)[0] <= max_width:
                return candidate
            start += 1
        return prefix

    def _draw_drag_preview(self, screen):
        mx, my = pygame.mouse.get_pos()
        entry = self.editor.selected_entry
        img = load_image(entry.path)
        # Escalamos al THUMB_SIZE*2 para preview (por ejemplo)
        w = THUMB_SIZE * 2
        h = img.get_height() * w // img.get_width()
        surf = pygame.transform.scale(img, (w, h))
        # Dibujamos semitransparente
        surf.set_alpha(200)
        screen.blit(surf, (mx - w//2, my - h//2))
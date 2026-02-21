from __future__ import annotations

import pygame
from .utils import get_surface, clamp


class MenuRenderer:
    """
    Render profesional del menú con tamaño dinámico y estética moderna.
    - Fondo atenuado (overlay)
    - Panel con esquinas redondeadas y sombra
    - Resaltado de opción seleccionada
    - Dimensiones calculadas según contenido
    """
    def __init__(self, font_size=36):
        # Tipografía base
        self.font = pygame.font.SysFont("Arial", font_size)
        self.font_size = int(font_size)

        # Estilos
        self.panel_bg = (22, 24, 28)
        self.panel_alpha = 235
        self.overlay_color = (0, 0, 0, 140)
        self.text_color = (230, 233, 240)
        self.text_color_dim = (180, 185, 195)
        self.accent_color = (255, 200, 0)
        self.highlight_color = (255, 200, 0, 38)  # bajo alfa para pill
        self.border_color = (255, 220, 0)
        # Tokens para botones (unificados)
        self.button_bg = (255, 255, 255, 22)
        self.button_pad_x = 14

        # Layout
        self.padding_x = 28
        self.padding_y = 24
        self.item_gap = max(8, font_size // 3)
        self.line_height = font_size + max(6, font_size // 6)
        self.radius = 12
        self.shadow_offset = (5, 6)

        # Registro de blits para pruebas/depuración
        self.last_blits = []

    # ---- Elementos comunes ----
    def _draw_button(self, panel, rect: pygame.Rect, text_surface: pygame.Surface, *, hover: bool = False, active: bool = False):
        """
        Dibuja un botón estándar (fondo translúcido, borde en hover/activo) y centra el texto.
        """
        pygame.draw.rect(panel, self.button_bg, rect, border_radius=self.radius // 2)
        if hover or active:
            pygame.draw.rect(panel, self.border_color, rect, width=2, border_radius=self.radius // 2)
        tx = rect.x + (rect.width - text_surface.get_width()) // 2
        ty = rect.y + (rect.height - text_surface.get_height()) // 2
        panel.blit(text_surface, (tx, ty))

    # ---- Utilidades de dibujo ----
    def _draw_overlay(self, screen):
        from .core import draw_overlay as _core_draw_overlay
        return _core_draw_overlay(self, screen)

    def _draw_shadow(self, screen, rect):
        from .core import draw_shadow as _core_draw_shadow
        return _core_draw_shadow(self, screen, rect)

    def _draw_panel(self, size):
        from .core import draw_panel as _core_draw_panel
        return _core_draw_panel(self, size)

    def _draw_scrollbar(self, panel: pygame.Surface, track_rect: pygame.Rect, *,
                        max_visible: int, total: int, start_index: int) -> None:
        """Dibuja una pista de scrollbar y su thumb, calculando tamaño/posición.
        Reutilizado por listas/tabl as con overflow vertical.
        """
        if total <= max_visible or track_rect.height <= 0:
            return
        # Pista
        pygame.draw.rect(panel, (255, 255, 255, 28), track_rect, border_radius=3)
        # Tamaño del thumb proporcional a visible/total
        thumb_h = max(24, int(track_rect.height * (max_visible / max(1, total))))
        max_thumb_top = track_rect.y + track_rect.height - thumb_h
        if (total - max_visible) == 0:
            thumb_top = track_rect.y
        else:
            thumb_top = int(track_rect.y + (track_rect.height - thumb_h) * (start_index / max(1, (total - max_visible))))
        thumb_top = clamp(thumb_top, track_rect.y, max_thumb_top)
        pygame.draw.rect(panel, self.accent_color,
                         pygame.Rect(track_rect.x, int(thumb_top), track_rect.width, int(thumb_h)),
                         border_radius=3)

    def _measure_menu(self, options):
        from .core import measure_menu as _core_measure_menu
        return _core_measure_menu(self, options)

    def _center_rect(self, screen, size):
        from .core import center_rect as _core_center_rect
        return _core_center_rect(self, screen, size)

    # ---- Render principal ----
    def draw(self, screen, selected, options, scroll_offset: int = 0, panel_top_min: int | None = None):
        """Wrapper del menú principal: delega en list_menu.draw."""
        from .list_menu import draw as _draw_list
        return _draw_list(self, screen, selected, options, scroll_offset, panel_top_min)

    def draw_confirm_dialog(self, screen, lines: list[str], *, hover_yes: bool = False, hover_cancel: bool = False):
        """Wrapper del modal de confirmación: delega en modal.draw_confirm_dialog."""
        from .modal import draw_confirm_dialog as _draw_confirm
        return _draw_confirm(self, screen, lines, hover_yes=hover_yes, hover_cancel=hover_cancel)

    def draw_saves_panel(self, screen,
                          selected: int,
                          items: list[str],
                          detail_lines: list[str],
                          *,
                          row_scroll_offset: int = 0,
                          hovered_index: int | None = None,
                          fixed_panel_size: tuple[int, int] | None = None,
                          fixed_list_width: int | None = None,
                          fixed_details_width: int | None = None,
                          hover_details_name: bool = False,
                          editing_name: bool = False,
                          edit_name_text: str | None = None,
                          caret_pos: int = 0,
                          hover_load_button: bool = False,
                          hover_delete_button: bool = False,
                          select_all_edit: bool = False,
                          panel_top_min: int | None = None) -> pygame.Rect:
        """Wrapper del panel de partidas: delega en saves_panel.draw_saves_panel."""
        from .saves_panel import draw_saves_panel as _draw_saves_panel
        return _draw_saves_panel(
            self, screen, selected, items, detail_lines,
            row_scroll_offset=row_scroll_offset,
            hovered_index=hovered_index,
            fixed_panel_size=fixed_panel_size,
            fixed_list_width=fixed_list_width,
            fixed_details_width=fixed_details_width,
            hover_details_name=hover_details_name,
            editing_name=editing_name,
            edit_name_text=edit_name_text,
            caret_pos=caret_pos,
            hover_load_button=hover_load_button,
            hover_delete_button=hover_delete_button,
            select_all_edit=select_all_edit,
            panel_top_min=panel_top_min,
        )

    def draw_saves(self, screen, selected, items, detail_lines):
        """Wrapper de la vista de partidas sin scroll: delega en saves_panel.draw_saves."""
        from .saves_panel import draw_saves as _draw_saves
        return _draw_saves(self, screen, selected, items, detail_lines)

    def draw_table_with_tabs(self, screen, tabs, active_tab_index: int,
                              headers, rows,
                              selected_row: int = 0, selected_col: int = 0,
                              row_scroll_offset: int = 0,
                              hovered_row: int | None = None, hovered_col: int | None = None,
                              fixed_size: tuple[int, int] | None = None,
                              fixed_col_widths: list[int] | None = None,
                              panel_top_min: int | None = None):
        """Wrapper de la tabla con pestañas: delega en table.draw_table_with_tabs."""
        from .table import draw_table_with_tabs as _draw_table_tabs
        return _draw_table_tabs(
            self, screen, tabs, active_tab_index, headers, rows,
            selected_row=selected_row, selected_col=selected_col,
            row_scroll_offset=row_scroll_offset,
            hovered_row=hovered_row, hovered_col=hovered_col,
            fixed_size=fixed_size, fixed_col_widths=fixed_col_widths,
            panel_top_min=panel_top_min,
        )

    def draw_table(self, screen, headers, rows, selected_row: int = 0, selected_col: int = 0, row_scroll_offset: int = 0, hovered_row: int | None = None, hovered_col: int | None = None):
        """Wrapper de la tabla: delega en table.draw_table."""
        from .table import draw_table as _draw_table
        return _draw_table(
            self, screen, headers, rows,
            selected_row=selected_row, selected_col=selected_col,
            row_scroll_offset=row_scroll_offset,
            hovered_row=hovered_row, hovered_col=hovered_col,
        )

    def draw_message(self, screen, lines):
        """Wrapper del mensaje simple: delega en modal.draw_message."""
        from .modal import draw_message as _draw_message
        return _draw_message(self, screen, lines)

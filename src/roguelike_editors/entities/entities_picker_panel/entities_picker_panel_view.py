import pygame
import logging
from roguelike_engine.utils.loader import load_image
from roguelike_engine.utils.loader import load_image
from roguelike_game.factories.monster.config import MONSTER_DEFS
from math import ceil
from roguelike_editors.entities.entities_picker_panel.entities_picker_panel_model import EntityPickerPanelModel
from roguelike_ui.panel import DraggablePanel
from roguelike_ui.widgets.hover import draw_hover
from roguelike_ui.ui_blocker import register_blocker


class EntityPickerPanelView:


    """
    Vista que renderiza el panel UI del editor de entidades (jugadores y monstruos).

    Características:
    - Panel dinámico con scroll.
    - Soporte para arrastrar (DraggablePanel).
    - Renderización de celdas con iconos, tintado y hover.
    """

    def __init__(self, assets: dict[str, pygame.Surface], font: pygame.font.Font):
        self.assets = assets
        self.font = font
        self.blink_interval = 500
        self._picker_logged = False
        self._last_active_tab = None
        self._center_pixel_logged = set()

        # Configuración visual del panel
        self.margin = 20
        self.cell_size = 64
        self.text_margin = 4
        self.columns = 10

        # Posición inicial del panel (alineado con Tile Picker)
        self.x = 10 + 32 + 4
        self.y = self._calculate_y_position()

        # Panel arrastrable
        self.draggable_panel = DraggablePanel(0, 0)

    # ----------------------------
    # CÁLCULO DE POSICIONES
    # ----------------------------
    def _calculate_y_position(self) -> int:
        """Calcula la posición Y del panel bajo el título dinámico."""
        from roguelike_ui.widgets.title_panel import TitlePanel
        dummy = TitlePanel(text="", font=self.font, x=0, y=0)
        title_height = self.font.get_height() + dummy.padding_y * 2
        return 10 + title_height

    def _calculate_panel_size(self, entity_count: int) -> tuple[int, int]:
        """Calcula el tamaño dinámico del panel según cantidad de entidades."""
        rows = ceil(entity_count / self.columns)
        cell_height = self.cell_size + self.text_margin + self.font.get_height()
        used_cols = min(self.columns, entity_count)
        panel_w = self.margin + used_cols * self.cell_size + (used_cols + 1) * self.margin
        panel_h = self.margin + rows * (cell_height + self.margin)
        return panel_w, panel_h

    def _calculate_scroll(self, model: EntityPickerPanelModel, screen_height: int, total_rows: int, cell_height: int) -> int:
        """Determina el scroll actual basado en el tamaño del panel y pantalla."""
        max_visible_rows = max(1, (screen_height - 2 * self.margin) // (cell_height + self.margin))
        return max(0, min(model.scroll_index, total_rows - max_visible_rows))

    def _truncate_text(self, text: str, max_width: int) -> str:
        """Trunca texto con '...' si excede el ancho máximo permitido."""
        if self.font.size(text)[0] <= max_width:
            return text
        text = text.rstrip()
        while self.font.size(text + '...')[0] > max_width and text:
            text = text[:-1]
        return text + '...'

    # ----------------------------
    # DIBUJADO PRINCIPAL
    # ----------------------------
    def draw(self, screen: pygame.Surface, model: EntityPickerPanelModel) -> None:
        """Dibuja el panel completo si está visible."""
        if not model.visible:
            self._picker_logged = False
            return

        # Lista completa de entidades (jugador + monstruos)
        if model.active_tab == "Players":
            entity_ids = list(model.player_stats.keys())
        else:
            entity_ids = list(model.monsters.keys())

        # Reset debug flag on tab change
        if self._last_active_tab != model.active_tab:
            self._picker_logged = False
            self._last_active_tab = model.active_tab


        if model.visible and not self._picker_logged:
            for ent_id in entity_ids:
                logging.debug(f"[DEBUG][Picker] ent_id={ent_id}")
            self._picker_logged = True
        # Tamaño dinámico del panel
        # Tamaño dinámico de la parte de grid
        panel_w, grid_h = self._calculate_panel_size(len(entity_ids))
        # Altura del header de pestañas
        tab_padding_y = 5
        header_height = self.font.get_height() + tab_padding_y * 2
        # Altura total del panel (header + grid)
        panel_h = header_height + grid_h

        # Ajustar panel arrastrable
        self.draggable_panel.resize(panel_w, panel_h)
        if self.draggable_panel.pos is None:
            self.draggable_panel.pos = (self.x, self.y)
        else:
            self.x, self.y = self.draggable_panel.pos

        # Asignar rect del panel (para eventos)
        model.panel_rect = pygame.Rect(self.x, self.y, panel_w, panel_h)
        # Actualizar rect global para suprimir hover DropHoverRenderSystem
        register_blocker(model.panel_rect)
        # Fondo opaco para todo el panel (oculta elementos subyacentes)
        # pygame.draw.rect(screen, (50, 50, 50), model.panel_rect)  # Omitido fondo opaco para usar semitransparente

        # Dibujar fondo semitransparente con borde redondeado
        self._draw_panel_background(screen, panel_w, panel_h)
        # Dibujar pestañas
        self._draw_tabs(screen, model)
        # Parpadeo de borde en modo spawn
        if model.blink:
            now = pygame.time.get_ticks()
            if (now // self.blink_interval) % 2 == 0:
                pygame.draw.rect(screen, (255, 255, 0), (self.x - 3, self.y - 3, panel_w + 6, panel_h + 6), 4)

        # Dibujar contenido (grid) bajo pestañas
        header_height = max(rect.height for rect in model.tab_rects.values()) if model.tab_rects else 0
        orig_y = self.y
        self.y = orig_y + header_height
        self._draw_entity_grid(screen, model, entity_ids)
        self.y = orig_y

    # ----------------------------
    # SUBRENDERIZADO
    # ----------------------------
    def _draw_tabs(self, screen: pygame.Surface, model: EntityPickerPanelModel) -> None:
        """Dibuja las pestañas Players/Monsters en el encabezado del panel."""
        font_h = self.font.get_height()
        padding_x, padding_y = 10, 5
        x_cursor, y = self.x, self.y
        model.tab_rects.clear()
        mouse_pos = pygame.mouse.get_pos()
        for label in ("Players", "Monsters"):
            text_w, text_h = self.font.size(label)
            w = text_w + padding_x * 2
            h = text_h + padding_y * 2
            rect = pygame.Rect(x_cursor, y, w, h)
            model.tab_rects[label] = rect
            # Estilo para pestaña seleccionada o hover
            is_active = (model.active_tab == label)
            is_hover = rect.collidepoint(mouse_pos)
            if is_active or is_hover:
                # Fondo amarillo semitransparente
                tab_surf = pygame.Surface((w, h), pygame.SRCALPHA)
                tab_surf.fill((255, 255, 0, 100))
                screen.blit(tab_surf, (rect.x, rect.y))
                # Borde amarillo
                pygame.draw.rect(screen, (255, 255, 0), rect, 2)
            else:
                # Fondo gris
                default_color = (100, 100, 100)
                pygame.draw.rect(screen, default_color, rect)
                pygame.draw.rect(screen, (255, 255, 255), rect, 2)
            text_surf = self.font.render(label, True, (0, 0, 0))
            text_x = x_cursor + (w - text_surf.get_width()) // 2
            text_y = y + padding_y
            screen.blit(text_surf, (text_x, text_y))
            x_cursor += w

    def _draw_panel_background(self, screen: pygame.Surface, width: int, height: int) -> None:
        """Dibuja el fondo del panel con opacidad."""
        bg_surf = pygame.Surface((width, height), pygame.SRCALPHA)
        bg_surf.fill((0, 0, 0, 200))  # Fondo semitransparente
        
        screen.blit(bg_surf, (self.x, self.y))

    def _draw_entity_grid(self, screen: pygame.Surface, model: EntityPickerPanelModel, entity_ids: list[str]) -> None:
        """Dibuja la grilla de entidades con íconos, hover y selección."""
        font_h = self.font.get_height()
        cell_height = self.cell_size + self.text_margin + font_h
        screen_w, screen_h = screen.get_size()

        total_rows = (len(entity_ids) + self.columns - 1) // self.columns
        scroll = self._calculate_scroll(model, screen_h, total_rows, cell_height)
        header_height = next(iter(model.tab_rects.values())).height if model.tab_rects else 0
        y_start = self.y + header_height + self.margin

        # Renderizado fila por fila
        for idx, ent_id in enumerate(entity_ids):
            col, row = idx % self.columns, idx // self.columns

            # Saltar filas fuera de scroll
            if row < scroll or row >= scroll + max(1, (screen_h - 2 * self.margin) // (cell_height + self.margin)):
                continue

            x = self.x + self.margin + col * (self.cell_size + self.margin)
            y = self.y + self.margin + (row - scroll) * (cell_height + self.margin)
            cell_rect = pygame.Rect(x, y, self.cell_size, cell_height)

            # Fondo celda
            pygame.draw.rect(screen, (50, 50, 50), cell_rect)

            # Hover visual
            if ent_id == model.hovered_id:
                draw_hover(screen, cell_rect)

            # Render icono con posible tintado
            self._draw_entity_icon(screen, ent_id, x, y, model)

        # Resaltar seleccionado o hover
        self._highlight_selected(screen, model, entity_ids, scroll, screen_h, cell_height)

    def _draw_entity_icon(self, screen: pygame.Surface, ent_id: str, x: int, y: int, model: EntityPickerPanelModel) -> None:
        icon = self.assets.get(ent_id)
        """Renderiza el icono de la entidad, aplicando tint si existe."""

        if not icon:
            return
        # Escalar icono al tamaño de celda
        icon_surf = pygame.transform.smoothscale(icon, (self.cell_size, self.cell_size))
        if ent_id not in self._center_pixel_logged:
            center = (self.cell_size // 2, self.cell_size // 2)
            center_pixel = icon_surf.get_at(center)
            logging.debug(f'[TINT][PickerView] ent_id={ent_id} original_asset_size={icon.get_size()} scaled_asset_size={icon_surf.get_size()} center_pixel={center_pixel}')
            self._center_pixel_logged.add(ent_id)

        # Dibujar icono
        screen.blit(icon_surf, (x, y))
        # Mostrar nombre de la entidad debajo del icono
        label = self._truncate_text(ent_id, self.cell_size)
        text_surf = self.font.render(label, True, (255, 255, 255))
        # Reducir tamaño del texto
        scale_text = 0.65
        w_t, h_t = text_surf.get_size()
        text_surf = pygame.transform.smoothscale(text_surf, (int(w_t * scale_text), int(h_t * scale_text)))
        text_x = x + (self.cell_size - text_surf.get_width()) // 2
        text_y = y + self.cell_size + self.text_margin
        screen.blit(text_surf, (text_x, text_y))

    def _highlight_selected(self, screen: pygame.Surface, model: EntityPickerPanelModel, entity_ids: list[str], scroll: int, screen_h: int, cell_height: int) -> None:
        """Dibuja un rectángulo amarillo para resaltar la entidad seleccionada o hover."""
        header_height = next(iter(model.tab_rects.values())).height if model.tab_rects else 0
        y_start = self.y + header_height + self.margin
        active = model.selected_id or model.hovered_id
        if active not in entity_ids:
            return

        idx_h = entity_ids.index(active)
        col, row = idx_h % self.columns, idx_h // self.columns

        # Verificar si está en rango visible
        max_visible_rows = max(1, (screen_h - 2 * self.margin) // (cell_height + self.margin))
        if not (scroll <= row < scroll + max_visible_rows):
            return

        x = self.x + self.margin + col * (self.cell_size + self.margin)
        y = self.y + self.margin + (row - scroll) * (cell_height + self.margin)

        if model.selection_blink:
            now = pygame.time.get_ticks()
            if (now // self.blink_interval) % 2 == 0:
                pygame.draw.rect(screen, (255, 255, 0), (x - 2, y - 2, self.cell_size + 4, cell_height + 4), 3)
        else:
            pygame.draw.rect(screen, (255, 255, 0), (x - 2, y - 2, self.cell_size + 4, cell_height + 4), 3)

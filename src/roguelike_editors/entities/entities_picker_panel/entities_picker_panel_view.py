import pygame
from math import ceil
from roguelike_editors.entities.entities_picker_panel.entities_picker_panel_model import EntityPickerPanelModel
from roguelike_ui.panel import DraggablePanel
from roguelike_ui.widgets.hover import draw_hover


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
            return

        # Lista completa de entidades (jugador + monstruos)
        entity_ids = list(model.player_stats.keys()) + list(model.monsters.keys())

        # Tamaño dinámico del panel
        panel_w, panel_h = self._calculate_panel_size(len(entity_ids))

        # Ajustar panel arrastrable
        self.draggable_panel.resize(panel_w, panel_h)
        if self.draggable_panel.pos is None:
            self.draggable_panel.pos = (self.x, self.y)
        else:
            self.x, self.y = self.draggable_panel.pos

        # Asignar rect del panel (para eventos)
        model.panel_rect = pygame.Rect(self.x, self.y, panel_w, panel_h)

        # Dibujar fondo semitransparente con borde redondeado
        self._draw_panel_background(screen, panel_w, panel_h)
        # Parpadeo de borde en modo spawn
        if model.blink:
            now = pygame.time.get_ticks()
            if (now // self.blink_interval) % 2 == 0:
                pygame.draw.rect(screen, (255, 255, 0), (self.x - 3, self.y - 3, panel_w + 6, panel_h + 6), 4)

        # Dibujar contenido (grid)
        self._draw_entity_grid(screen, model, entity_ids)

    # ----------------------------
    # SUBRENDERIZADO
    # ----------------------------
    def _draw_panel_background(self, screen: pygame.Surface, width: int, height: int) -> None:
        """Dibuja el fondo del panel con opacidad."""
        bg_surf = pygame.Surface((width, height), pygame.SRCALPHA)
        bg_surf.fill((0, 0, 0, 180))  # Fondo semitransparente
        
        screen.blit(bg_surf, (self.x, self.y))

    def _draw_entity_grid(self, screen: pygame.Surface, model: EntityPickerPanelModel, entity_ids: list[str]) -> None:
        """Dibuja la grilla de entidades con íconos, hover y selección."""
        font_h = self.font.get_height()
        cell_height = self.cell_size + self.text_margin + font_h
        screen_w, screen_h = screen.get_size()

        total_rows = (len(entity_ids) + self.columns - 1) // self.columns
        scroll = self._calculate_scroll(model, screen_h, total_rows, cell_height)

        # Renderizado fila por fila
        for idx, ent_id in enumerate(entity_ids):
            col, row = idx % self.columns, idx // self.columns

            # Saltar filas fuera de scroll
            if row < scroll or row >= scroll + max(1, (screen_h - 2 * self.margin) // (cell_height + self.margin)):
                continue

            x = self.x + self.margin + col * (self.cell_size + self.margin)
            y = self.y + self.margin + (row - scroll) * (cell_height + self.margin)
            cell_rect = pygame.Rect(x, y, self.cell_size, self.cell_size)

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
        """Renderiza el icono de la entidad, aplicando tint si existe."""
        icon = self.assets.get(ent_id)
        if not icon:
            return

        icon_surf = pygame.transform.smoothscale(icon, (self.cell_size, self.cell_size))

        # Aplicar tint si existe
        tint = None
        if ent_id in model.monsters:
            tint = model.monsters.get(ent_id, {}).get("tint")
        elif ent_id in model.player_stats:
            tint = model.player_stats.get(ent_id, {}).get("tint")

        if tint:
            color = tuple(tint) if len(tint) == 4 else (*tint, 255)
            tinted = icon_surf.copy()
            tinted.fill(color, special_flags=pygame.BLEND_RGBA_MULT)
            icon_surf = tinted

        screen.blit(icon_surf, (x, y))

    def _highlight_selected(self, screen: pygame.Surface, model: EntityPickerPanelModel, entity_ids: list[str], scroll: int, screen_h: int, cell_height: int) -> None:
        """Dibuja un rectángulo amarillo para resaltar la entidad seleccionada o hover."""
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

        pygame.draw.rect(screen, (255, 255, 0), (x - 2, y - 2, self.cell_size + 4, self.cell_size + 4), 3)

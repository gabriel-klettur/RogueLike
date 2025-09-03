import pygame
import logging
from math import ceil
from roguelike_editors.entities.entities_picker_panel.entities_picker_panel_model import EntityPickerPanelModel
from roguelike_ui.panel import DraggablePanel
from roguelike_ui.widgets.hover import draw_hover
from roguelike_ui.ui_blocker import register_blocker
from roguelike_ui.widgets.picker_panel import PickerPanel, PickerPanelState
from typing import Optional

import logging
logger = logging.getLogger(__name__)

class EntityPickerPanelView:


    """
    Vista que renderiza el panel UI del editor de entidades (jugadores y hostiles).

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

        # Reusable PickerPanel (grid renderer)
        # Cell height includes label area: icon cell + text margin + font height
        cell_h_with_label = self.cell_size + self.text_margin + self.font.get_height()
        self.picker = PickerPanel(
            cell_size=(self.cell_size, cell_h_with_label),
            margin=self.margin,
            padding=self.margin,
            draw_panel_bg=False,
            allow_dragging=False,
            draw_overlays=False,
            grid_bg_color=None,
        )
        self._current_entity_ids: list[str] = []
        self._last_model: Optional[EntityPickerPanelModel] = None
        self.picker.set_item_count(lambda: len(self._current_entity_ids))
        self.picker.set_draw_item(lambda surf, rect, idx, sel, hov: self._draw_entity_cell(surf, rect, self._current_entity_ids[idx]))
        self.picker_state = PickerPanelState(rect=pygame.Rect(0, 0, 0, 0), visible=True)

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

        # Lista completa de entidades (jugador + hostiles)
        if model.active_tab == "Players":
            entity_ids = list(model.player_stats.keys())
        elif model.active_tab == "Hostile":
            # Ocultar entradas de hostiles marcadas como pendientes (no confirmadas)
            entity_ids = [mid for mid, m in model.monsters.items() if not (isinstance(m, dict) and m.get('__pending__'))]
        else:
            # Otras pestañas aún no implementadas
            entity_ids = []

        # Reset debug flag on tab change
        if self._last_active_tab != model.active_tab:
            self._picker_logged = False
            self._last_active_tab = model.active_tab


        if model.visible and not self._picker_logged:
            for ent_id in entity_ids:
                logger.debug(f" ent_id={ent_id}")
            self._picker_logged = True
        # Tamaño dinámico del panel
        # Tamaño dinámico de la parte de grid
        panel_w, grid_h = self._calculate_panel_size(len(entity_ids))
        # Altura del header de pestañas
        tab_padding_y = 5
        header_height = self.font.get_height() + tab_padding_y * 2
        # Altura del footer (etiqueta centrada con nombre de entidad)
        footer_font = self.font
        footer_h = footer_font.get_height() + 10
        # Altura total del panel (header + grid + footer)
        panel_h = header_height + grid_h + footer_h

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

        # Dibujar contenido (grid) con PickerPanel bajo pestañas
        header_height = max(rect.height for rect in model.tab_rects.values()) if model.tab_rects else 0
        orig_y = self.y
        self.y = orig_y + header_height

        # Sync picker state with model
        self._current_entity_ids = entity_ids
        self._last_model = model
        # Grid rect excluding header/footer
        self.picker_state.rect = pygame.Rect(self.x, self.y, panel_w, grid_h)
        # Map model selection/hover to indices
        self.picker_state.selected_index = (
            entity_ids.index(model.selected_id) if model.selected_id in entity_ids else None
        )
        self.picker_state.hovered_index = (
            entity_ids.index(model.hovered_id) if model.hovered_id in entity_ids else None
        )
        # Convert row-based scroll to pixel-based scroll
        cell_h_with_label = self.cell_size + self.text_margin + self.font.get_height()
        self.picker_state.scroll_y = max(0, model.scroll_index) * (cell_h_with_label + self.margin)

        # Render grid
        self.picker.render(screen, self.picker_state)
        # Dibujar footer con etiqueta centrada (hovered o selected)
        # Use orig_y because self.y was temporarily offset by header_height
        footer_y = orig_y + header_height + grid_h
        # Fondo del footer (semi-transparente)
        footer_bg = pygame.Surface((panel_w, footer_h), pygame.SRCALPHA)
        footer_bg.fill((0, 0, 0, 220))
        screen.blit(footer_bg, (self.x, footer_y))
        # Texto del footer
        label_text = model.hovered_id or model.selected_id or ""
        if label_text:
            pretty = label_text.replace("_", " ").title()
            text_surf = footer_font.render(pretty, True, (255, 230, 0))
            tx = self.x + (panel_w - text_surf.get_width()) // 2
            ty = footer_y + (footer_h - text_surf.get_height()) // 2
            screen.blit(text_surf, (tx, ty))
        self.y = orig_y

    # ----------------------------
    # SUBRENDERIZADO
    # ----------------------------
    def _draw_tabs(self, screen: pygame.Surface, model: EntityPickerPanelModel) -> None:
        """Dibuja las pestañas Players/Hostile/Neutral/Aliades/Specials en el encabezado del panel."""
        font_h = self.font.get_height()
        padding_x, padding_y = 10, 5
        x_cursor, y = self.x, self.y
        model.tab_rects.clear()
        mouse_pos = pygame.mouse.get_pos()
        for label in ("Players", "Hostile", "Neutral", "Aliades", "Specials"):
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

    def _draw_entity_cell(self, screen: pygame.Surface, rect: pygame.Rect, ent_id: str) -> None:
        """Dibuja una celda: fondo, icono y etiqueta, usando el rect provisto."""
        # Fondo celda
        pygame.draw.rect(screen, (50, 50, 50), rect)
        # Render icono y etiqueta dentro de la celda
        self._draw_entity_icon(screen, ent_id, rect.x, rect.y, self._last_model)
        # Overlays de hover/selección
        if not self._last_model:
            return
        if ent_id == self._last_model.hovered_id:
            draw_hover(screen, rect)
        if ent_id == self._last_model.selected_id:
            if self._last_model.selection_blink:
                now = pygame.time.get_ticks()
                if (now // self.blink_interval) % 2 == 0:
                    pygame.draw.rect(screen, (255, 255, 0), rect.inflate(4, 4), 3)
            else:
                pygame.draw.rect(screen, (255, 255, 0), rect.inflate(4, 4), 3)

    def _draw_entity_icon(self, screen: pygame.Surface, ent_id: str, x: int, y: int, model: EntityPickerPanelModel) -> None:
        icon = self.assets.get(ent_id)
        """Renderiza el icono de la entidad, aplicando tint si existe."""

        if not icon:
            return
        # Escalar icono manteniendo relación de aspecto para que NO se vea perfectamente cuadrado
        orig_w, orig_h = icon.get_size()
        pad = 6  # padding interno para que no toque los bordes
        max_w = self.cell_size - 2 * pad
        max_h = self.cell_size - 2 * pad
        if orig_w == 0 or orig_h == 0:
            return
        scale = min(max_w / orig_w, max_h / orig_h)
        new_w = max(1, int(orig_w * scale))
        new_h = max(1, int(orig_h * scale))
        icon_surf = pygame.transform.smoothscale(icon, (new_w, new_h))
        if ent_id not in self._center_pixel_logged:
            center = (self.cell_size // 2, self.cell_size // 2)
            center_pixel = icon_surf.get_at(center)
            logger.debug(f' ent_id={ent_id} original_asset_size={icon.get_size()} scaled_asset_size={icon_surf.get_size()} center_pixel={center_pixel}')
            self._center_pixel_logged.add(ent_id)

        # Posicionar icono: bottom-align dentro de la celda, centrado horizontal
        dest_x = x + (self.cell_size - new_w) // 2
        dest_y = y + (self.cell_size - new_h - pad)  # anclado abajo dejando pad

        # Sombra sutil para dar profundidad profesional
        shadow_surf = pygame.Surface((new_w, new_h), pygame.SRCALPHA)
        shadow_surf.fill((0, 0, 0, 80))
        screen.blit(shadow_surf, (dest_x + 2, dest_y + 2))

        # Dibujar icono
        screen.blit(icon_surf, (dest_x, dest_y))
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

    # Nota: El resaltado de hover/selección ahora lo maneja PickerPanel con overlays.

import pygame
from roguelike_ui.ui_blocker import register_blocker
from roguelike_editors.entities.entities_properties_panel.entities_properties_panel_model import EntityPropertiesPanelModel
from roguelike_ui.panel import DraggablePanel
from roguelike_ui.widgets.hover import draw_hover
from roguelike_editors.entities.entities_properties_panel.services.assets_constants import (
    TYPE_TAB_PROPERTIES,
    TYPE_TAB_ASSETS,
    SUBTAB_SET,
    SUBTAB_NO_SET,
    STATE_ADD,
)
from roguelike_editors.entities.entities_properties_panel.services.entity_flatten import (
    flatten_entity_data,
)



class EntityPropertiesPanelView:
    """
    Vista encargada de renderizar el panel de propiedades de la entidad seleccionada.
    
    Características:
    - Fondo semitransparente.
    - Soporte para arrastre (DraggablePanel).
    - Dibujo dinámico de propiedades con hover, foco y edición activa.
    """

    def __init__(self, font: pygame.font.Font, blink_interval: int = 500):
        self.font = font
        self.blink_interval = blink_interval
        self.draggable_panel = DraggablePanel(0, 0)
        self.thumbnail_cache: dict[str, pygame.Surface|None] = {}
        # UI constants (avoid magic numbers in rendering code)
        self.PAD = 10
        self.MARGIN = 20
        self.SCROLLBAR_WIDTH = 8
        self.MAX_PANEL_W = 500
        self.TAB_PADDING_Y = 5
        self.SUBPAD_X = 8

    # ----------------------------
    # RENDER PRINCIPAL
    # ----------------------------
    def draw(self, screen: pygame.Surface, model: EntityPropertiesPanelModel) -> None:
        """Renderiza el panel y las propiedades si hay una entidad seleccionada o en hover."""        
        # Mostrar información de la entidad hovered o seleccionada
        ent_id = model.hovered_entity_id or model.selected_id
        if not ent_id:
            return

        # Datos y dimensiones básicas
        sw, sh = screen.get_size()
        entity_data = self._get_entity_data(model)
        # Filtrar datos según pestaña activa y sub-asset seleccionado
        if self.type_assets_controller.model.active_type_tab == TYPE_TAB_PROPERTIES:
            filtered = {k: v for k, v in entity_data.items() if not k.startswith('asset')}
        elif self.type_assets_controller.model.active_type_tab == TYPE_TAB_ASSETS:
            # Filtrar por categoría de asset seleccionada
            active_state = self.state_tabs_controller.model.active_state_tab
            if active_state == STATE_ADD:
                filtered = {}
            else:
                prefix = f"asset_{active_state}_"
                filtered = {k: v for k, v in entity_data.items() if k.startswith(prefix)}
        else:
            filtered = {}
        lines = [ent_id] + [f"{k}: {v}" for k, v in filtered.items()]
        font_h = self.font.get_height()

        # Calcular tamaño del panel (incluye pestañas y subtabs)
        pad, margin = self.PAD, self.MARGIN
        max_w = max(self.font.size(line)[0] for line in lines)
        panel_w = min(max_w + pad * 2, sw - margin * 2, self.MAX_PANEL_W)
        # Asegurar ancho mínimo para subtabs de assets vacíos
        if self.type_assets_controller.model.active_type_tab == TYPE_TAB_ASSETS:
            subpad_x = self.SUBPAD_X
            state_tabs = self.state_tabs_controller.model.state_tabs
            subtabs_total = sum(self.font.size(label.capitalize())[0] + subpad_x * 2 for label in state_tabs)
            panel_w = max(panel_w, subtabs_total)
        # Altura del header de pestañas
        tab_padding_y = self.TAB_PADDING_Y
        primary_header = font_h + tab_padding_y * 2
        # Altura del header de subtabs de assets
        state_header = primary_header if self.type_assets_controller.model.active_type_tab == TYPE_TAB_ASSETS else 0
        sub_header = state_header
        # Altura del contenido con padding al fondo
        bottom_padding = 0
        # Altura máxima del contenido para no sobrepasar la pantalla
        max_content_h = sh - margin - bottom_padding - primary_header - state_header - sub_header
        if self.type_assets_controller.model.active_type_tab == TYPE_TAB_ASSETS:
            # Ajustar panel para nombre, tint y cuadrícula 3x3
            grid_w = panel_w - pad * 2
            cell_size = int(grid_w / 3)
            orig_content_h = pad + font_h + 2 + font_h + pad + cell_size * 3 + pad + font_h + pad + font_h
            content_h = min(orig_content_h, max_content_h)
        else:
            orig_content_h = len(lines) * (font_h + 2) + pad * 2
            content_h = min(orig_content_h, max_content_h)
        panel_h = primary_header + state_header + sub_header + content_h

        # Posición inicial (esquina superior derecha)
        px, py = sw - panel_w - margin, margin

        # Ajustar panel draggable
        self.draggable_panel.resize(panel_w, panel_h)
        if self.draggable_panel.pos is None:
            self.draggable_panel.pos = (px, py)
        else:
            px, py = self.draggable_panel.pos
        # Anchoring X to right edge of screen
        px = sw - panel_w - margin

        # Actualizar rect para detección de eventos
        model.panel_rect = pygame.Rect(px, py, panel_w, panel_h)
        register_blocker(model.panel_rect)

        # 1. Dibujar fondo
        self._draw_background(screen, px, py, panel_w, panel_h)

        # Dibujar pestañas principales (properties/assets)
        self.type_assets_controller.draw(screen)
        if self.type_assets_controller.model.active_type_tab == TYPE_TAB_ASSETS:
            self.state_tabs_controller.draw(screen)
            self.set_ot_assets_tab_controller.draw(screen)

        # 2. Dibujar contenido según pestaña
        if self.type_assets_controller.model.active_type_tab == TYPE_TAB_PROPERTIES:
            # Compute scroll metrics
            model.total_lines_height = len(lines) * (font_h + 2)
            model.available_height = content_h - pad * 2
            model.max_scroll = max(0, model.total_lines_height - model.available_height)
            model.scroll_offset = min(max(model.scroll_offset, 0), model.max_scroll)
            # Draw scrollbar
            scrollbar_width = self.SCROLLBAR_WIDTH
            bar_x = px + panel_w - scrollbar_width - pad // 2
            bar_y = py + primary_header + state_header + pad
            bar_h = model.available_height
            if model.total_lines_height:
                thumb_h = max(20, int(bar_h * (model.available_height / model.total_lines_height)))
                thumb_y = bar_y + int((model.scroll_offset / (model.max_scroll or 1)) * (bar_h - thumb_h))
            else:
                thumb_h = bar_h
                thumb_y = bar_y
            pygame.draw.rect(screen, (50, 50, 50), (bar_x, bar_y, scrollbar_width, bar_h))
            pygame.draw.rect(screen, (200, 200, 200), (bar_x, thumb_y, scrollbar_width, thumb_h))
            # Draw properties with scroll
            self._draw_properties(screen, model, lines, px, py + primary_header + state_header, pad, font_h, panel_w)
            self._draw_editing_indicator(screen, model, font_h)
        elif self.type_assets_controller.model.active_type_tab == TYPE_TAB_ASSETS:
            # Delegar grid a grid_controller
            # Clip contenido de grid para no salir del panel
            screen.set_clip(pygame.Rect(px + pad, py + primary_header + state_header + sub_header + pad, panel_w - pad*2, content_h - pad*2))
            self.grid_controller.draw(screen, entity_data, px, py + primary_header + state_header + sub_header, pad, font_h, panel_w)
            # Restaurar recorte tras dibujar grid
            screen.set_clip(None)

    # ----------------------------
    # MÉTODOS PRIVADOS
    # ----------------------------
    def _get_entity_data(self, model: EntityPropertiesPanelModel) -> dict:
        """Obtiene los datos aplanados de la entidad seleccionada o hovered.

        Delegado a services.entity_flatten.flatten_entity_data para mantener la vista
        desacoplada de la estructura JSON interna.
        """
        ent_id = model.hovered_entity_id or model.selected_id
        return flatten_entity_data(model.player_stats, model.player_assets, model.monsters, ent_id)

    def _draw_background(self, screen: pygame.Surface, x: int, y: int, w: int, h: int) -> None:
        """Dibuja el fondo semitransparente del panel."""
        info_surf = pygame.Surface((w, h), pygame.SRCALPHA)
        info_surf.fill((0, 0, 0, 200))
        screen.blit(info_surf, (x, y))

    def _draw_properties(self, screen: pygame.Surface, model: EntityPropertiesPanelModel,
                         lines: list[str], px: int, py: int, pad: int, font_h: int, panel_w: int) -> None:
        """Renderiza las propiedades, maneja hover y actualiza las áreas clicables."""
        # Configurar recorte para no dibujar fuera del área de contenido
        content_start_y = py + pad
        prev_clip = screen.get_clip()
        screen.set_clip(pygame.Rect(px + pad, content_start_y, panel_w - pad*2, model.available_height))
        tx = px + pad
        ty = py + pad - model.scroll_offset
        model.property_entries.clear()

        for i, line in enumerate(lines):
            # ID: línea de encabezado
            if i == 0:
                text = self._truncate_text(line, panel_w - pad * 2)
                txt_surf = self.font.render(text, True, (255, 255, 0))
                screen.blit(txt_surf, (tx, ty))
                ty += font_h + 2
                continue
            # Separar clave y valor
            parts = line.split(': ', 1)
            key = parts[0]
            val_str = parts[1] if len(parts) > 1 else ''
            # Renderizar clave en blanco
            key_text = f'{key}: '
            key_surf = self.font.render(self._truncate_text(key_text, panel_w - pad * 2), True, (255, 255, 255))
            # Renderizar valor en color según contenido
            color = (128, 0, 128) if val_str == 'None' else (255, 255, 0)
            val_surf = self.font.render(self._truncate_text(val_str, panel_w - pad * 2 - key_surf.get_width()), True, color)
            # Registrar área clicable
            rect = pygame.Rect(tx, ty, key_surf.get_width() + val_surf.get_width(), font_h)
            model.property_entries.append((rect, key))
            # Hover visual
            if key == model.hovered_property:
                draw_hover(screen, rect)
            # Dibujar clave y valor
            screen.blit(key_surf, (tx, ty))
            screen.blit(val_surf, (tx + key_surf.get_width(), ty))
            ty += font_h + 2



        # Restaurar recorte tras dibujar propiedades
        screen.set_clip(None)

    def _draw_editing_indicator(self, screen: pygame.Surface, model: EntityPropertiesPanelModel, font_h: int) -> None:
        """Dibuja los indicadores de edición activa o foco."""
        # Si hay edición activa
        if model.editing_property:
            for rect, key in model.property_entries:
                if key == model.editing_property:
                    # Marco púrpura
                    er = rect.inflate(4, 0)
                    pygame.draw.rect(screen, (128, 0, 128), er, 2)

                    # Cursor parpadeante
                    t = pygame.time.get_ticks()
                    if (t % self.blink_interval) < (self.blink_interval // 2):
                        prefix = f"{key}: "
                        caret_x = er.x + self.font.size(prefix + model.editing_text[:model.editing_cursor])[0]
                        caret_y = er.y
                        pygame.draw.line(screen, (255, 255, 255), (caret_x, caret_y),
                                         (caret_x, caret_y + font_h), 2)
                    break

        # Si solo hay foco
        elif model.focused_property:
            for rect, key in model.property_entries:
                if key == model.focused_property:
                    hl_rect = rect.inflate(4, 0)
                    pygame.draw.rect(screen, (255, 255, 0), hl_rect, 2)
                    break

    def _truncate_text(self, text: str, max_width: int) -> str:
        """Trunca el texto y añade '...' si excede el ancho disponible."""
        if self.font.size(text)[0] <= max_width:
            return text
        text = text.rstrip()
        while self.font.size(text + '...')[0] > max_width and text:
            text = text[:-1]
        return text + '...'
import pygame
from roguelike_ui.ui_blocker import register_blocker
from roguelike_editors.entities.entities_properties_panel.entities_properties_panel_model import EntityPropertiesPanelModel
from roguelike_ui.panel import DraggablePanel
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
from roguelike_editors.entities.entities_properties_panel.services.stats_templates import (
    PLAYER_STATS_TEMPLATE,
    MONSTER_STATS_TEMPLATE,
)
from roguelike_editors.entities.entities_properties_panel.render_utils import (
    draw_background,
    draw_properties,
    draw_editing_indicator,
    draw_confirm_button,
    draw_entity_type_selector,
    truncate_text,
)
from roguelike_editors.entities.entities_properties_panel.data_utils import (
    get_entity_stats_data,
    get_entity_data,
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
        self.DEFAULT_PANEL_W = 460
        self.DEFAULT_PANEL_H = 520  # Fixed target height for uniform UX
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
        # Elegir fuente de datos según pestaña activa: stats para Properties, flatten para Assets
        if self.type_assets_controller.model.active_type_tab == TYPE_TAB_PROPERTIES:
            entity_data = self._get_entity_stats_data(model)
            filtered = dict(entity_data)
        elif self.type_assets_controller.model.active_type_tab == TYPE_TAB_ASSETS:
            ent_id_for_assets = model.hovered_entity_id or model.selected_id
            flattened = flatten_entity_data(model.player_stats, model.player_assets, model.monsters, ent_id_for_assets)
            # Filtrar por categoría de asset seleccionada
            active_state = self.state_tabs_controller.model.active_state_tab
            if active_state == STATE_ADD:
                filtered = {}
            else:
                prefix = f"asset_{active_state}_"
                filtered = {k: v for k, v in flattened.items() if k.startswith(prefix)}
            # Datos completos para el grid y cabeceras
            entity_data = flattened
        else:
            filtered = {}
            entity_data = {}
        # Build lines for Properties tab: show editable 'id' for both players and monsters.
        is_props = self.type_assets_controller.model.active_type_tab == TYPE_TAB_PROPERTIES
        lines: list[str] = []
        if is_props:
            lines.append(f"id: {ent_id}")
        else:
            # Non-properties contexts may keep a simple header
            lines.append(ent_id)
        lines += [f"{k}: {v}" for k, v in filtered.items()]
        font_h = self.font.get_height()

        # Calcular tamaño del panel (incluye pestañas y subtabs)
        pad, margin = self.PAD, self.MARGIN
        max_w = max(self.font.size(line)[0] for line in lines)
        content_w = max_w + pad * 2
        # Ancho predefinido profesional con límites de pantalla
        panel_w = min(sw - margin * 2, max(self.DEFAULT_PANEL_W, min(content_w, self.MAX_PANEL_W)))
        # En modo add-on-system, mantener el ancho original; solo cambiaremos la posición más abajo
        if getattr(model, 'expand_into_picker_space', False):
            pass
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
        # Altura fija del panel (uniforme entre pestañas)
        panel_h_target = min(self.DEFAULT_PANEL_H, sh - margin * 2)
        content_h = max(40, panel_h_target - (primary_header + state_header + sub_header))
        panel_h = primary_header + state_header + sub_header + content_h

        # Posición inicial (esquina superior derecha o anclaje izquierdo override)
        if getattr(model, 'expand_into_picker_space', False):
            # Intentar usar override de X (posición guardada por el controller)
            if self.draggable_panel.pos is not None:
                px, py = self.draggable_panel.pos
            else:
                px = model.panel_left_x_override if model.panel_left_x_override is not None else (sw - panel_w - margin)
                py = margin
        else:
            px, py = sw - panel_w - margin, margin

        # Ajustar panel draggable
        self.draggable_panel.resize(panel_w, panel_h)
        if self.draggable_panel.pos is None:
            self.draggable_panel.pos = (px, py)
        else:
            px, py = self.draggable_panel.pos
        # Anchor a la derecha solo en modo normal; en modo expandido respetar X calculada/arrastrada
        if not getattr(model, 'expand_into_picker_space', False):
            px = sw - panel_w - margin

        # Actualizar rect para detección de eventos
        model.panel_rect = pygame.Rect(px, py, panel_w, panel_h)
        register_blocker(model.panel_rect)

        # 1. Dibujar fondo
        draw_background(screen, px, py, panel_w, panel_h)

        # Dibujar pestañas principales (properties/assets)
        self.type_assets_controller.draw(screen)
        if self.type_assets_controller.model.active_type_tab == TYPE_TAB_ASSETS:
            self.state_tabs_controller.draw(screen)
            self.assets_subtabs_controller.draw(screen)

        # 2. Dibujar contenido según pestaña
        if self.type_assets_controller.model.active_type_tab == TYPE_TAB_PROPERTIES:
            # Dibujar selector de tipo de entidad (arriba del área scrollable)
            sel_consumed_h = 0
            # Reset rect por defecto
            model.entity_type_rect = None
            # Reset confirm rect por defecto
            model.confirm_button_rect = None
            if getattr(model, 'show_add_system_selector', False):
                sel_consumed_h = draw_entity_type_selector(
                    screen,
                    self.font,
                    model,
                    px,
                    py + primary_header + state_header,
                    pad,
                    font_h,
                    panel_w,
                )
            # Compute scroll metrics con altura disponible ajustada
            content_y0 = py + primary_header + state_header + sel_consumed_h
            model.total_lines_height = len(lines) * (font_h + 2)
            # Reservar espacio para el botón de confirmación cuando esté visible en modo Add-System
            confirm_visible = getattr(model, 'show_add_system_selector', False)
            confirm_h = (font_h + 10) if confirm_visible else 0
            model.available_height = max(0, content_h - pad * 2 - sel_consumed_h - confirm_h)
            model.max_scroll = max(0, model.total_lines_height - model.available_height)
            model.scroll_offset = min(max(model.scroll_offset, 0), model.max_scroll)
            # Draw scrollbar only if there is overflow
            if model.max_scroll > 0 and model.available_height > 0:
                scrollbar_width = self.SCROLLBAR_WIDTH
                bar_x = px + panel_w - scrollbar_width - pad // 2
                bar_y = content_y0 + pad
                bar_h = model.available_height
                thumb_h = max(20, int(bar_h * (model.available_height / model.total_lines_height))) if model.total_lines_height else bar_h
                thumb_y = bar_y + int((model.scroll_offset / model.max_scroll) * (bar_h - thumb_h)) if model.max_scroll else bar_y
                pygame.draw.rect(screen, (50, 50, 50), (bar_x, bar_y, scrollbar_width, bar_h))
                pygame.draw.rect(screen, (200, 200, 200), (bar_x, thumb_y, scrollbar_width, thumb_h))
            # Draw properties with scroll bajo el selector
            draw_properties(screen, self.font, model, lines, px, content_y0, pad, font_h, panel_w)
            draw_editing_indicator(screen, self.font, model, self.blink_interval, font_h)
            # Dibujar botón Confirm al fondo del panel cuando se está añadiendo una entidad al sistema
            if confirm_visible:
                draw_confirm_button(screen, self.font, model, px, py, panel_w, panel_h, pad, font_h)
        elif self.type_assets_controller.model.active_type_tab == TYPE_TAB_ASSETS:
            # Calcular métricas de scroll para Assets
            grid_w = panel_w - pad * 2
            cell_size = int(grid_w / 3)
            model.assets_total_height = (
                pad + font_h + 2 + font_h +  # nombre + tint
                pad + cell_size * 3 +        # grilla 3x3
                pad + font_h +               # info ruta
                pad + font_h                 # combo "Activo"
            )
            model.assets_available_height = content_h - pad * 2
            model.assets_max_scroll = max(0, model.assets_total_height - model.assets_available_height)
            model.assets_scroll_offset = min(max(model.assets_scroll_offset, 0), model.assets_max_scroll)

            # Dibujar scrollbar de assets solo si hay overflow
            if model.assets_max_scroll > 0:
                scrollbar_width = self.SCROLLBAR_WIDTH
                bar_x = px + panel_w - scrollbar_width - pad // 2
                bar_y = py + primary_header + state_header + sub_header + pad
                bar_h = model.assets_available_height
                thumb_h = max(20, int(bar_h * (model.assets_available_height / model.assets_total_height))) if model.assets_total_height else bar_h
                thumb_y = bar_y + int((model.assets_scroll_offset / model.assets_max_scroll) * (bar_h - thumb_h)) if model.assets_max_scroll else bar_y
                pygame.draw.rect(screen, (50, 50, 50), (bar_x, bar_y, scrollbar_width, bar_h))
                pygame.draw.rect(screen, (200, 200, 200), (bar_x, thumb_y, scrollbar_width, thumb_h))

            # Clip contenido y aplicar desplazamiento vertical
            content_clip = pygame.Rect(
                px + pad,
                py + primary_header + state_header + sub_header + pad,
                panel_w - pad * 2,
                content_h - pad * 2,
            )
            screen.set_clip(content_clip)
            self.grid_controller.draw(
                screen,
                entity_data,
                px,
                py + primary_header + state_header + sub_header - model.assets_scroll_offset,
                pad,
                font_h,
                panel_w,
            )
            # Restaurar recorte tras dibujar grid
            screen.set_clip(None)

    # ----------------------------
    # MÉTODOS PRIVADOS (delegados)
    # ----------------------------
    def _get_entity_data(self, model: EntityPropertiesPanelModel) -> dict:
        return get_entity_data(model)

    def _draw_background(self, screen: pygame.Surface, x: int, y: int, w: int, h: int) -> None:
        draw_background(screen, x, y, w, h)

    def _get_entity_stats_data(self, model: EntityPropertiesPanelModel) -> dict:
        return get_entity_stats_data(model)

    def _draw_properties(self, screen: pygame.Surface, model: EntityPropertiesPanelModel,
                         lines: list[str], px: int, py: int, pad: int, font_h: int, panel_w: int) -> None:
        draw_properties(screen, self.font, model, lines, px, py, pad, font_h, panel_w)

    def _draw_editing_indicator(self, screen: pygame.Surface, model: EntityPropertiesPanelModel, font_h: int) -> None:
        draw_editing_indicator(screen, self.font, model, self.blink_interval, font_h)

    def _truncate_text(self, text: str, max_width: int) -> str:
        return truncate_text(self.font, text, max_width)

    def _draw_entity_type_selector(self, screen: pygame.Surface, model: EntityPropertiesPanelModel,
                                   px: int, py: int, pad: int, font_h: int, panel_w: int) -> int:
        return draw_entity_type_selector(screen, self.font, model, px, py, pad, font_h, panel_w)

    def _draw_confirm_button(self, screen: pygame.Surface, model: EntityPropertiesPanelModel,
                              px: int, py: int, panel_w: int, panel_h: int, pad: int, font_h: int) -> None:
        draw_confirm_button(screen, self.font, model, px, py, panel_w, panel_h, pad, font_h)
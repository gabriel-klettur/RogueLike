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
from roguelike_editors.entities.entities_properties_panel.services.stats_templates import (
    PLAYER_STATS_TEMPLATE,
    MONSTER_STATS_TEMPLATE,
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
                sel_consumed_h = self._draw_entity_type_selector(
                    screen,
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
            self._draw_properties(screen, model, lines, px, content_y0, pad, font_h, panel_w)
            self._draw_editing_indicator(screen, model, font_h)
            # Dibujar botón Confirm al fondo del panel cuando se está añadiendo una entidad al sistema
            if confirm_visible:
                self._draw_confirm_button(screen, model, px, py, panel_w, panel_h, pad, font_h)
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
    # MÉTODOS PRIVADOS
    # ----------------------------
    def _get_entity_data(self, model: EntityPropertiesPanelModel) -> dict:
        """Obtiene los datos aplanados de la entidad seleccionada o hovered.

        Delegado a services.entity_flatten.flatten_entity_data para mantener la vista
        desacoplada de la estructura JSON interna.
        """
        ent_id = model.hovered_entity_id or model.selected_id
        # En modo "Add Entities on System" con selector visible, mostrar SOLO las propiedades
        # de la sección 'stats' según el tipo elegido (Player/Monster).
        if getattr(model, 'show_add_system_selector', False):
            sel_type = getattr(model, 'add_system_entity_type', 'Monster')
            if sel_type == 'Player':
                # Para jugadores, los stats del modelo ya están disponibles a nivel de clase
                # en model.player_stats[ent_id]. Si no existe, devolver dict vacío.
                return dict(model.player_stats.get(ent_id, {}))
            else:
                # Para monstruos, los stats viven dentro de la entrada de monstruo.
                monster = model.monsters.get(ent_id, {}) if model.monsters else {}
                return dict(monster.get('stats', {}))
        # Comportamiento normal: aplanar toda la entidad (stats + assets en claves 'asset_*')
        return flatten_entity_data(model.player_stats, model.player_assets, model.monsters, ent_id)

    def _draw_background(self, screen: pygame.Surface, x: int, y: int, w: int, h: int) -> None:
        """Dibuja el fondo semitransparente del panel."""
        info_surf = pygame.Surface((w, h), pygame.SRCALPHA)
        info_surf.fill((0, 0, 0, 200))
        screen.blit(info_surf, (x, y))

    def _get_entity_stats_data(self, model: EntityPropertiesPanelModel) -> dict:
        """Retorna sólo los 'stats' según el selector 'Type of Entity',
        fusionando con una plantilla de claves esperadas y aplanando
        anidados simples (p. ej., 'basic_trail.interval').

        - Player: plantilla PLAYER_STATS_TEMPLATE + stats jugador
        - Monster: plantilla MONSTER_STATS_TEMPLATE + stats monstruo
        """
        def _flatten(d: dict) -> dict:
            flat: dict = {}
            for k, v in d.items():
                if isinstance(v, dict):
                    for sk, sv in v.items():
                        flat[f"{k}.{sk}"] = sv
                else:
                    flat[k] = v
            return flat

        ent_id = model.hovered_entity_id or model.selected_id
        if getattr(model, 'show_add_system_selector', False):
            sel_type = getattr(model, 'add_system_entity_type', 'Monster')
        else:
            # Inferir tipo por pertenencia del id
            sel_type = 'Player' if ent_id in model.player_stats else 'Monster'
        if sel_type == 'Player':
            tmpl = PLAYER_STATS_TEMPLATE
            src = model.player_stats.get(ent_id, {})
        else:
            tmpl = MONSTER_STATS_TEMPLATE
            src = (model.monsters.get(ent_id, {}) or {}).get('stats', {})

        # Deep-ish merge (1 nivel) y aplanado
        merged: dict = {}
        # copiar plantilla
        for k, v in tmpl.items():
            if isinstance(v, dict):
                merged[k] = dict(v)
            else:
                merged[k] = v
        # sobreescribir con valores reales
        for k, v in src.items():
            if isinstance(v, dict) and isinstance(merged.get(k), dict):
                merged[k].update(v)
            else:
                merged[k] = v

        return _flatten(merged)

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
            # Línea de encabezado sólo si no es par clave:valor
            is_header = (i == 0) and (': ' not in line)
            if is_header:
                text = self._truncate_text(line, panel_w - pad * 2)
                txt_surf = self.font.render(text, True, (255, 255, 0))
                screen.blit(txt_surf, (tx, ty))
                ty += font_h + 2
                continue
            # Separar clave y valor
            parts = line.split(': ', 1)
            if len(parts) != 2:
                # Si no tiene formato clave: valor, omitir
                continue
            key, val_str = parts[0], parts[1]
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

    def _draw_entity_type_selector(self, screen: pygame.Surface, model: EntityPropertiesPanelModel,
                                   px: int, py: int, pad: int, font_h: int, panel_w: int) -> int:
        """Dibuja el label y combobox 'Type of Entity' en la parte superior del panel Properties.

        Retorna la altura consumida para ajustar el área scrollable inferior.
        """
        tx = px + pad
        ty = py + pad
        # Label
        label = "Type of Entity"
        label_surf = self.font.render(label, True, (255, 255, 255))
        screen.blit(label_surf, (tx, ty))
        # Combobox
        value = getattr(model, 'add_system_entity_type', 'Monster')
        value_text = str(value)
        value_surf = self.font.render(value_text, True, (0, 0, 0))
        cb_pad_x = 8
        cb_w = max(120, value_surf.get_width() + cb_pad_x * 2)
        cb_h = font_h + 6
        cb_x = tx + label_surf.get_width() + pad
        cb_y = ty - 2
        rect = pygame.Rect(cb_x, cb_y, min(cb_w, panel_w - (cb_x - px) - pad), cb_h)
        # Fondo y borde
        pygame.draw.rect(screen, (200, 200, 200), rect)
        pygame.draw.rect(screen, (255, 255, 255), rect, 2)
        # Texto centrado
        text_x = rect.x + (rect.w - value_surf.get_width()) // 2
        text_y = rect.y + (rect.h - value_surf.get_height()) // 2
        screen.blit(value_surf, (text_x, text_y))
        # Guardar rect para eventos
        model.entity_type_rect = rect
        # Altura consumida (label + margen inferior)
        consumed_h = max(label_surf.get_height(), rect.h) + pad
        return consumed_h

    def _draw_confirm_button(self, screen: pygame.Surface, model: EntityPropertiesPanelModel,
                              px: int, py: int, panel_w: int, panel_h: int, pad: int, font_h: int) -> None:
        """Dibuja el botón de confirmación en la parte inferior del panel y guarda su rect en el modelo."""
        btn_text = "Confirm"
        text_surf = self.font.render(btn_text, True, (255, 255, 255))
        btn_h = font_h + 6
        btn_w = panel_w - pad * 2
        btn_x = px + pad
        btn_y = py + panel_h - pad - btn_h
        rect = pygame.Rect(btn_x, btn_y, btn_w, btn_h)
        # Fondo verde y borde
        pygame.draw.rect(screen, (0, 140, 0), rect)
        pygame.draw.rect(screen, (255, 255, 255), rect, 2)
        # Texto centrado
        tx = rect.x + (rect.w - text_surf.get_width()) // 2
        ty = rect.y + (rect.h - text_surf.get_height()) // 2
        screen.blit(text_surf, (tx, ty))
        # Guardar rect
        model.confirm_button_rect = rect
import pygame
from roguelike_ui.ui_blocker import register_blocker
from roguelike_editors.entities.entities_properties_panel.entities_properties_panel_model import EntityPropertiesPanelModel
from roguelike_ui.panel import DraggablePanel
from roguelike_ui.widgets.hover import draw_hover



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
        if self.type_assets_controller.model.active_type_tab == 'properties':
            filtered = {k: v for k, v in entity_data.items() if not k.startswith('asset')}
        elif self.type_assets_controller.model.active_type_tab == 'assets':
            # Filtrar por categoría de asset seleccionada
            active_state = self.state_tabs_controller.model.active_state_tab
            if active_state == 'add state':
                filtered = {}
            else:
                prefix = f"asset_{active_state}_"
                filtered = {k: v for k, v in entity_data.items() if k.startswith(prefix)}
        else:
            filtered = {}
        lines = [ent_id] + [f"{k}: {v}" for k, v in filtered.items()]
        font_h = self.font.get_height()

        # Calcular tamaño del panel (incluye pestañas y subtabs)
        pad, margin = 10, 20
        max_w = max(self.font.size(line)[0] for line in lines)
        panel_w = min(max_w + pad * 2, sw - margin * 2, 500)
        # Asegurar ancho mínimo para subtabs de assets vacíos
        if self.type_assets_controller.model.active_type_tab == 'assets':
            subpad_x = 8
            state_tabs = self.state_tabs_controller.model.state_tabs
            subtabs_total = sum(self.font.size(label.capitalize())[0] + subpad_x * 2 for label in state_tabs)
            panel_w = max(panel_w, subtabs_total)
        # Altura del header de pestañas
        tab_padding_y = 5
        primary_header = font_h + tab_padding_y * 2
        # Altura del header de subtabs de assets
        state_header = primary_header if self.type_assets_controller.model.active_type_tab == 'assets' else 0
        sub_header = state_header
        # Altura del contenido
        if self.type_assets_controller.model.active_type_tab == 'assets':
            # Ajustar panel para nombre, tint y cuadrícula 3x3
            grid_w = panel_w - pad * 2
            cell_size = int(grid_w / 3)
            # pad top + nombre + espaciado + tint + pad + cuadrícula + pad bottom
            content_h = pad + font_h + 2 + font_h + pad + cell_size * 3 + pad + font_h
        else:
            content_h = min(len(lines) * (font_h + 2) + pad * 2, sh - margin * 2 - primary_header - state_header)
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
        if self.type_assets_controller.model.active_type_tab == 'assets':
            self.state_tabs_controller.draw(screen)
            self.set_ot_assets_tab_controller.draw(screen)

        # 2. Dibujar contenido según pestaña
        if self.type_assets_controller.model.active_type_tab == 'properties':
            self._draw_properties(screen, model, lines, px, py + primary_header + state_header, pad, font_h, panel_w)
            self._draw_editing_indicator(screen, model, font_h)
        elif self.type_assets_controller.model.active_type_tab == 'assets':
            # Delegar grid a grid_controller
            self.grid_controller.draw(screen, entity_data, px, py + primary_header + state_header + sub_header, pad, font_h, panel_w)

    # ----------------------------
    # MÉTODOS PRIVADOS
    # ----------------------------
    def _get_entity_data(self, model: EntityPropertiesPanelModel) -> dict:
        """Obtiene los datos de la entidad seleccionada o hovered, incluyendo PLAYER_ASSETS."""

        ent_id = model.hovered_entity_id or model.selected_id
        if not ent_id:
            return {}
        if ent_id in model.player_stats:
            stats = model.player_stats.get(ent_id, {})
            # Flatten sprites_set and no-sets assets for player
            player_assets = model.player_assets.get(ent_id, {})
            sets = player_assets.get('sets', {}).get('sprites_set', {})
            no_sets = player_assets.get('no-sets', {})
            merged = dict(stats)
            merged['id'] = ent_id
            # Map 'walking' to 'chase' UI state
            state_map = {'walking': 'chase'}
            # Map grid directions to sprite sheet directions
            dir_map = {
                'nw': 'up_left', 'n': 'up', 'ne': 'up_right',
                'w': 'left', 'e': 'right', 'sw': 'down_left',
                's': 'down', 'se': 'down_right'
            }
                        # Flatten no-sets first (initial asset_by_asset values)
            for state, dirs in no_sets.items():
                ui_state = state_map.get(state, state)
                for dir_key, path in dirs.items():
                    merged[f'asset_{ui_state}_{dir_key}'] = path
            # Flatten sets (override no-sets for asset set)
            for state, paths in sets.items():
                if paths:
                    ui_state = state_map.get(state, state)
                    sheet_path = paths[0]
                    for dir_key, sprite_dir in dir_map.items():
                        merged[f'asset_{ui_state}_{dir_key}'] = sheet_path
            return merged
        elif ent_id in model.monsters:
            monster = model.monsters.get(ent_id, {})
            # Extraer stats (todo excepto sprites)
            nested = monster.get('sprites', {}) or {}
            stats = {k: v for k, v in monster.items() if k != 'sprites'}
            # Incluir data_assets
            data_assets = nested.get('data_assets', {})
            for ak, av in data_assets.items():
                stats[ak] = av
            # Extraer assets anidados
            assets = {}
            for cat, dirs in nested.get('assets', {}).items():
                for dkey, path in dirs.items():
                    assets_key = f"{cat}_{dkey}"
                    assets[assets_key] = path
            # Combinar stats y assets prefijados
            merged_mon = dict(stats)
            merged_mon['id'] = ent_id
            for k, v in assets.items():
                merged_mon[f"asset_{k}"] = v
            return merged_mon
        return {}


    def _draw_background(self, screen: pygame.Surface, x: int, y: int, w: int, h: int) -> None:
        """Dibuja el fondo semitransparente del panel."""
        info_surf = pygame.Surface((w, h), pygame.SRCALPHA)
        info_surf.fill((0, 0, 0, 200))
        screen.blit(info_surf, (x, y))

    def _draw_properties(self, screen: pygame.Surface, model: EntityPropertiesPanelModel,
                         lines: list[str], px: int, py: int, pad: int, font_h: int, panel_w: int) -> None:
        """Renderiza las propiedades, maneja hover y actualiza las áreas clicables."""
        tx, ty = px + pad, py + pad
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
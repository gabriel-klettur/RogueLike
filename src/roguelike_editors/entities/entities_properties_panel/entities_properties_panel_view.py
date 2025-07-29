import pygame
from roguelike_ui.ui_blocker import register_blocker
from roguelike_editors.entities.entities_properties_panel.entities_properties_panel_model import EntityPropertiesPanelModel
from roguelike_ui.panel import DraggablePanel
from roguelike_ui.widgets.hover import draw_hover
from roguelike_engine.utils.loader import load_image


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
        if model.active_tab == 'properties':
            filtered = {k: v for k, v in entity_data.items() if not k.startswith('asset')}
        elif model.active_tab == 'assets':
            # Filtrar por categoría de asset seleccionada
            if model.active_asset_tab == 'add state':
                filtered = {}
            else:
                prefix = f"asset_{model.active_asset_tab}_"
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
        if model.active_tab == 'assets':
            subpad_x = 8
            subtabs_total = sum(self.font.size(label.capitalize())[0] + subpad_x * 2 for label in model.asset_tabs)
            panel_w = max(panel_w, subtabs_total)
        # Altura del header de pestañas
        tab_padding_y = 5
        primary_header = font_h + tab_padding_y * 2
        # Altura del header de subtabs de assets
        state_header = primary_header if model.active_tab == 'assets' else 0
        # Altura del contenido
        if model.active_tab == 'assets':
            # Ajustar panel para cuadrícula 3x3
            grid_w = panel_w - pad * 2
            cell_size = int(grid_w / 3)
            content_h = cell_size * 3 + pad * 2
        else:
            content_h = min(len(lines) * (font_h + 2) + pad * 2, sh - margin * 2 - primary_header - state_header)
        panel_h = primary_header + state_header + content_h

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

        # Dibujar pestañas
        self._draw_tabs(screen, model)
        # Dibujar subtabs de assets
        if model.active_tab == 'assets':
            self._draw_asset_tabs(screen, model)

        # 2. Dibujar contenido según pestaña
        if model.active_tab == 'properties':
            self._draw_properties(screen, model, lines, px, py + primary_header + state_header, pad, font_h, panel_w)
            self._draw_editing_indicator(screen, model, font_h)
        elif model.active_tab == 'assets':
            self._draw_asset_grid(screen, model, entity_data, px, py + primary_header + state_header, pad, font_h, panel_w)

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
            assets = model.player_assets.get(ent_id, {})
            merged = dict(stats)
            if isinstance(assets, dict):
                for k, v in assets.items():
                    merged[f'asset_{k}'] = v
            else:
                merged['asset'] = assets
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
            for k, v in assets.items():
                merged_mon[f"asset_{k}"] = v
            return merged_mon
        return {}


    def _draw_background(self, screen: pygame.Surface, x: int, y: int, w: int, h: int) -> None:
        """Dibuja el fondo semitransparente del panel."""
        info_surf = pygame.Surface((w, h), pygame.SRCALPHA)
        info_surf.fill((0, 0, 0, 200))
        screen.blit(info_surf, (x, y))

    def _draw_tabs(self, screen: pygame.Surface, model: EntityPropertiesPanelModel) -> None:
        """Dibuja las pestañas del panel: 'properties' y 'assets'."""
        font_h = self.font.get_height()
        padding_x, padding_y = 10, 5
        x_cursor, y = model.panel_rect.x, model.panel_rect.y
        model.tab_rects.clear()
        mouse_pos = pygame.mouse.get_pos()
        for label in model.tabs:
            text_label = label.capitalize()
            text_w, text_h = self.font.size(text_label)
            w = text_w + padding_x * 2
            h = text_h + padding_y * 2
            rect = pygame.Rect(x_cursor, y, w, h)
            model.tab_rects[label] = rect
            is_active = (model.active_tab == label)
            is_hover = rect.collidepoint(mouse_pos)
            if is_active or is_hover:
                tab_surf = pygame.Surface((w, h), pygame.SRCALPHA)
                tab_surf.fill((255, 255, 0, 100))
                screen.blit(tab_surf, (rect.x, rect.y))
                pygame.draw.rect(screen, (255, 255, 0), rect, 2)
            else:
                default_color = (100, 100, 100)
                pygame.draw.rect(screen, default_color, rect)
                pygame.draw.rect(screen, (255, 255, 255), rect, 2)
            text_surf = self.font.render(text_label, True, (0, 0, 0))
            text_x = x_cursor + (w - text_surf.get_width()) // 2
            text_y = y + padding_y
            screen.blit(text_surf, (text_x, text_y))
            x_cursor += w

    def _draw_asset_tabs(self, screen: pygame.Surface, model: EntityPropertiesPanelModel) -> None:
        """Dibuja subtabs de assets cuando active_tab == 'assets'."""
        font_h = self.font.get_height()
        padding_x, padding_y = 8, 4
        x_cursor = model.panel_rect.x
        # Posicionar subtabs justo debajo del header principal
        y = model.panel_rect.y + (font_h + 5 * 2)
        model.asset_tab_rects.clear()
        mouse_pos = pygame.mouse.get_pos()
        for label in model.asset_tabs:
            text_label = label.capitalize()
            text_w, text_h = self.font.size(text_label)
            w = text_w + padding_x * 2
            h = text_h + padding_y * 2
            rect = pygame.Rect(x_cursor, y, w, h)
            model.asset_tab_rects[label] = rect
            is_active = (model.active_asset_tab == label)
            is_hover = rect.collidepoint(mouse_pos)
            if is_active or is_hover:
                tab_surf = pygame.Surface((w, h), pygame.SRCALPHA)
                tab_surf.fill((255, 255, 0, 80))
                screen.blit(tab_surf, (rect.x, rect.y))
                pygame.draw.rect(screen, (255, 255, 0), rect, 2)
            else:
                pygame.draw.rect(screen, (80, 80, 80), rect)
                pygame.draw.rect(screen, (200, 200, 200), rect, 1)
            text_surf = self.font.render(text_label, True, (0, 0, 0))
            text_x = x_cursor + (w - text_surf.get_width()) // 2
            text_y = y + padding_y
            screen.blit(text_surf, (text_x, text_y))
            x_cursor += w

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

    def _draw_asset_grid(self, screen: pygame.Surface, model: EntityPropertiesPanelModel, entity_data: dict, px: int, py: int, pad: int, font_h: int, panel_w: int) -> None:
        """Dibuja una cuadrícula 3x3 de assets para el estado activo."""
        # Parámetros de cuadrícula
        grid_w = panel_w - pad * 2
        cell_size = int(grid_w / 3)
        grid_x = px + pad
        grid_y = py + pad
        # Orden de direcciones
        order = ['nw', 'n', 'ne', 'w', None, 'e', 'sw', 's', 'se']
        for idx, dir_key in enumerate(order):
            row = idx // 3
            col = idx % 3
            x = grid_x + col * cell_size
            y = grid_y + row * cell_size
            # Dibujar celda
            pygame.draw.rect(screen, (150, 150, 150), (x, y, cell_size, cell_size), 1)
            if dir_key:
                key = f'asset_{model.active_asset_tab}_{dir_key}'
                path = entity_data.get(key)
                if path:
                    # Cargar raw scaled
                    raw = self.thumbnail_cache.get(path)
                    if raw is None:
                        try:
                            img = load_image(path)
                            raw = pygame.transform.smoothscale(img, (cell_size - 4, cell_size - 4))
                        except Exception:
                            raw = None
                        self.thumbnail_cache[path] = raw
                    if raw:
                        # Aplicar tint
                        thumb = raw.copy()
                        tint_val = entity_data.get('tint')
                        if tint_val:
                            color = tuple(tint_val) if len(tint_val) == 4 else (*tint_val, 255)
                            thumb.fill(color, special_flags=pygame.BLEND_RGBA_MULT)
                        # Blit thumbnail
                        tx = x + (cell_size - thumb.get_width()) // 2
                        ty = y + (cell_size - thumb.get_height()) // 2
                        screen.blit(thumb, (tx, ty))

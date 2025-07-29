import pygame
from roguelike_engine.utils.loader import load_image
from roguelike_ui.widgets.hover import draw_hover
from roguelike_editors.entities.entities_properties_panel.entities_assets_grid_panel.entities_assets_grid_panel_model import AssetsGridPanelModel

class AssetsGridPanelView:
    """Vista de cuadrícula de assets para el panel de propiedades."""
    def __init__(self, font: pygame.font.Font):
        self.font = font
        self.thumbnail_cache: dict[str, pygame.Surface|None] = {}

    def draw(self, screen: pygame.Surface, model: AssetsGridPanelModel,
             entity_data: dict, px: int, py: int, pad: int,
             font_h: int, panel_w: int) -> None:
        """Dibuja grid de assets, thumbnails, hover y selección."""
        # Limpiar entradas de celdas
        model.asset_cell_entries.clear()
        # Nombre de entidad
        name_x, name_y = px + pad, py + pad
        ent_id = entity_data.get('id') or entity_data.get('name') or ''
        name_surf = self.font.render(ent_id, True, (255, 255, 0))
        screen.blit(name_surf, (name_x, name_y))
        # Tint editable
        tint_val = entity_data.get('tint')
        val_str = str(tint_val) if tint_val is not None else 'None'
        key_surf = self.font.render('tint: ', True, (255, 255, 255))
        color = (128, 0, 128) if val_str == 'None' else (255, 255, 0)
        val_surf = self.font.render(val_str, True, color)
        tint_y = name_y + font_h + 2
        screen.blit(key_surf, (name_x, tint_y))
        screen.blit(val_surf, (name_x + key_surf.get_width(), tint_y))
        # Posición de grid
        grid_x, grid_y = px + pad, tint_y + font_h + pad
        grid_w = panel_w - pad * 2
        cell_size = int(grid_w / 3)
        # Orden y dibujo de celdas
        order = ['nw', 'n', 'ne', 'w', None, 'e', 'sw', 's', 'se']
        for idx, dir_key in enumerate(order):
            row, col = divmod(idx, 3)
            x = grid_x + col * cell_size
            y = grid_y + row * cell_size
            cell_rect = pygame.Rect(x, y, cell_size, cell_size)
            if dir_key:
                asset_key = f"asset_{model.active_asset_tab}_{dir_key}"
                model.asset_cell_entries.append((cell_rect, asset_key))
                # Hover highlight
                if model.hovered_asset_cell == asset_key:
                    hover_surf = pygame.Surface((cell_size, cell_size), pygame.SRCALPHA)
                    hover_surf.fill((255, 255, 0, 80))
                    screen.blit(hover_surf, (x, y))
                # Border
                border_color = (255, 255, 0) if model.selected_asset_cell == asset_key else (150, 150, 150)
                border_width = 2 if model.selected_asset_cell == asset_key else 1
                pygame.draw.rect(screen, border_color, cell_rect, border_width)
                # Thumbnail
                path = entity_data.get(asset_key)
                if path:
                    raw = self.thumbnail_cache.get(path)
                    if raw is None:
                        try:
                            img = load_image(path)
                            raw = pygame.transform.smoothscale(img, (cell_size - 4, cell_size - 4))
                        except Exception:
                            raw = None
                        self.thumbnail_cache[path] = raw
                    if raw:
                        thumb = raw.copy()
                        tint = entity_data.get('tint')
                        if tint:
                            c = tuple(tint) if len(tint) == 4 else (*tint, 255)
                            thumb.fill(c, special_flags=pygame.BLEND_RGBA_MULT)
                        tx = x + (cell_size - thumb.get_width()) // 2
                        ty = y + (cell_size - thumb.get_height()) // 2
                        screen.blit(thumb, (tx, ty))
            else:
                pygame.draw.rect(screen, (150, 150, 150), cell_rect, 1)
        # Mostrar ruta del asset seleccionado/hover
        sel = model.hovered_asset_cell or model.selected_asset_cell
        if sel:
            path = entity_data.get(sel)
            if path:
                info_surf = self.font.render(path, True, (255, 255, 0))
                info_x = px + pad
                info_y = grid_y + cell_size * 3 + pad
                screen.blit(info_surf, (info_x, info_y))

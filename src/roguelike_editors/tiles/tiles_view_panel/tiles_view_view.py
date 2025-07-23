import pygame
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_editors.tiles.tiles_editor_config import OUTLINE_CHOICE, OUTLINE_HOVER, OUTLINE_SEL, BTN_H
from roguelike_engine.utils.loader import load_image
from roguelike_engine.map.model.layer import Layer

class TilesViewPanelView:
    """View for the Tiles View Panel"""
    def __init__(self, controller, state):
        self.controller = controller
        self.state = state

    def render(self, screen, camera, game_map):
        font = pygame.font.SysFont("Arial", 14)
        # Calculate world and mouse positions
        mouse_pos = pygame.mouse.get_pos()
        wx = mouse_pos[0] / camera.zoom + camera.offset_x
        wy = mouse_pos[1] / camera.zoom + camera.offset_y
        col, row = int(wx) // TILE_SIZE, int(wy) // TILE_SIZE

        # Determine hovered tile and layer
        hovered_tile = self.controller._tile_under_mouse(mouse_pos, camera, game_map)
        hovered_layer = Layer.Ground
        for layer in sorted(game_map.tiles_by_layer.keys(), key=lambda l: -l.value):
            grid = game_map.tiles_by_layer[layer]
            if 0 <= row < len(grid) and 0 <= col < len(grid[0]):
                t = grid[row][col]
                if t and getattr(t, "overlay_code", ""):
                    hovered_layer = layer
                    break
        selected_tile = self.controller.editor_state.selected_tile
        choice_path = self.controller.editor_state.current_choice
        choice_sprite = None
        if choice_path:
            choice_sprite = load_image(choice_path, (TILE_SIZE, TILE_SIZE))

        # Define items: sprite rows and layer rows
        sprite_items = [
            ("Hovered", hovered_tile.sprite if hovered_tile and hasattr(hovered_tile, 'sprite') else None, OUTLINE_HOVER),
            ("Selected", selected_tile.sprite if selected_tile and hasattr(selected_tile, 'sprite') else None, OUTLINE_SEL),
            ("Choice", choice_sprite, OUTLINE_CHOICE),
        ]
        layer_items = [
            ("Layer Hovered", f"{hovered_layer.value}: {hovered_layer.name}"),
            ("Layer Selected", f"{self.controller.editor_state.current_layer.value}: {self.controller.editor_state.current_layer.name}"),
        ]

        # Layout settings
        margin_x, margin_y = 12, 12
        padding_x = 8
        spacing_y = 6

        # Compute dynamic panel size
        row_widths = []
        row_heights = []
        # Sprite rows
        for label, sprite, outline in sprite_items:
            tw, th = font.size(label)
            w = TILE_SIZE + padding_x + tw
            h = max(TILE_SIZE, th)
            row_widths.append(w)
            row_heights.append(h)
        # Layer rows
        for label, val in layer_items:
            lw, lh = font.size(label)
            vw, vh = font.size(val)
            w = lw + padding_x + vw
            h = lh + vh + spacing_y
            row_widths.append(w)
            row_heights.append(h)
        content_w = max(row_widths)
        content_h = sum(row_heights) + spacing_y * (len(row_heights) - 1)
        panel_w = content_w + margin_x * 2
        panel_h = content_h + margin_y * 2

        # Determine panel position
        if self.state.pos:
            x0, y0 = self.state.pos
        else:
            toolbar = self.controller.editor_controller.toolbar
            x0 = toolbar.x + toolbar.size + toolbar.padding
            y0 = toolbar.y

        # Create panel surface
        panel = pygame.Surface((panel_w, panel_h), pygame.SRCALPHA)
        panel.fill((30, 30, 30, 220))
        # Border
        pygame.draw.rect(panel, OUTLINE_CHOICE, panel.get_rect(), 2)

        # Draw sprite rows
        y = margin_y
        for idx, (label, sprite, outline) in enumerate(sprite_items):
            sx = margin_x
            sy = y + (row_heights[idx] - TILE_SIZE) // 2
            if sprite:
                panel.blit(sprite, (sx, sy))
            rect = pygame.Rect(sx, sy, TILE_SIZE, TILE_SIZE)
            pygame.draw.rect(panel, outline, rect, 2)
            # Label
            text = font.render(label, True, (245, 245, 245))
            tx = sx + TILE_SIZE + padding_x
            ty = y + (row_heights[idx] - text.get_height()) // 2
            panel.blit(text, (tx, ty))
            y += row_heights[idx] + spacing_y

        # Draw layer rows
        for i, (label, val) in enumerate(layer_items, start=len(sprite_items)):
            # Label
            lbl = font.render(label, True, (245, 245, 245))
            panel.blit(lbl, (margin_x, y))
            y += lbl.get_height() + spacing_y // 2
            # Value
            vtx = font.render(val, True, OUTLINE_CHOICE)
            panel.blit(vtx, (margin_x + padding_x, y))
            y += vtx.get_height() + spacing_y

        # Update state size and blit
        self.state.size = (panel_w, panel_h)
        screen.blit(panel, (x0, y0))

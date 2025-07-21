import pygame
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_editors.tiles.tiles_editor_config import OUTLINE_CHOICE, OUTLINE_HOVER, OUTLINE_SEL
from roguelike_engine.utils.loader import load_image
from roguelike_engine.map.model.layer import Layer

class TilesViewPanelView:
    """View for the Tiles View Panel"""
    def __init__(self, controller, state):
        self.controller = controller
        self.state = state

    def render(self, screen, camera, game_map):
        # Gather panel items
        font = pygame.font.SysFont("Arial", 14)
        mouse_pos = pygame.mouse.get_pos()
        wx = mouse_pos[0] / camera.zoom + camera.offset_x
        wy = mouse_pos[1] / camera.zoom + camera.offset_y
        col = int(wx) // TILE_SIZE
        row = int(wy) // TILE_SIZE
        # Determine hovered layer
        hovered_layer = Layer.Ground
        for layer in sorted(game_map.tiles_by_layer.keys(), key=lambda l: -l.value):
            grid = game_map.tiles_by_layer[layer]
            if 0 <= row < len(grid) and 0 <= col < len(grid[0]):
                t = grid[row][col]
                if t and getattr(t, "overlay_code", ""):
                    hovered_layer = layer
                    break

        items = [
            ("Hovered", self.controller._tile_under_mouse(mouse_pos, camera, game_map), OUTLINE_HOVER),
            ("Selected", self.controller.editor_state.selected_tile, OUTLINE_SEL),
            ("Choice", None, OUTLINE_CHOICE),
            ("Layer Hovered", hovered_layer, None),
            ("Layer Selected", self.controller.editor_state.current_layer, None),
        ]

        # Compute dynamic panel size
        max_text_width = 0
        for label, tile, color in items:
            if label.startswith("Layer"):
                text_str = f"{label}: {tile.name}"
            else:
                text_str = label
            tw, _ = font.size(text_str)
            max_text_width = max(max_text_width, tw)
        margin_x = 10
        panel_w = max(TILE_SIZE, max_text_width) + margin_x * 2
        panel_h = len(items) * (TILE_SIZE + 30)

        # Render panel background
        x0 = self.controller.editor_controller.toolbar.x + self.controller.editor_controller.toolbar.size + 20
        y0 = self.controller.editor_controller.toolbar.y
        panel = pygame.Surface((panel_w, panel_h), pygame.SRCALPHA)
        panel.fill((20, 20, 20, 200))

        # Draw items
        for idx, (label, tile, color) in enumerate(items):
            ty = idx * (TILE_SIZE + 30) + 10
            # Layer labels only text
            if label.startswith("Layer"):
                layer = tile
                text = font.render(f"{label}: {layer.name}", True, (255, 255, 255))
                panel.blit(text, (margin_x, ty + TILE_SIZE + 2))
                continue
            sprite = None
            if label == "Choice" and self.controller.editor_state.current_choice:
                sprite = load_image(self.controller.editor_state.current_choice, (TILE_SIZE, TILE_SIZE))
            elif tile and hasattr(tile, 'sprite'):
                sprite = tile.sprite
            if sprite:
                panel.blit(sprite, ((panel_w - TILE_SIZE)//2, ty))
            rect = pygame.Rect((panel_w - TILE_SIZE)//2, ty, TILE_SIZE, TILE_SIZE)
            if color:
                pygame.draw.rect(panel, color, rect, 3)
            text = font.render(label, True, (255, 255, 255))
            panel.blit(text, (margin_x, ty + TILE_SIZE + 2))

        screen.blit(panel, (x0, y0))

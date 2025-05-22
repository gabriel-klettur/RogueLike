# Path: src/roguelike_game/systems/editor/tiles/view/tile_editor_view.py
import pygame
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_game.systems.editor.tiles.tiles_editor_config import OUTLINE_CHOICE, OUTLINE_HOVER, OUTLINE_SEL
from roguelike_engine.utils.loader import load_image
from roguelike_engine.map.model.layer import Layer

from roguelike_game.systems.editor.tiles.view.tools.tile_toolbar_view import TileToolbarView
from roguelike_game.systems.editor.tiles.view.tools.tile_picker_view import TilePickerView
from roguelike_game.systems.editor.tiles.view.tools.tile_outline_view import TileOutlineView

class TileEditorView:
    def __init__(self, controller, editor_state):
        self.controller = controller        
        self.editor     = editor_state

        self.toolbar_view = TileToolbarView(controller.toolbar)
        self.picker_view  = TilePickerView(editor_state.picker_state, controller.picker.assets)
        self.outline_view = TileOutlineView(controller, editor_state)

    def render(self, screen, camera, map):
        if not self.editor.active:
            return

        self.toolbar_view.render(screen)
        self.picker_view.render(screen)

        if self.editor.view_active:
            self._render_view_panel(screen, camera, map)

        self.outline_view.render(screen, camera, map)

    def _render_view_panel(self, screen, camera, map):
        # Gather panel items
        font = pygame.font.SysFont("Arial", 14)
        mouse_pos = pygame.mouse.get_pos()
        wx = mouse_pos[0] / camera.zoom + camera.offset_x
        wy = mouse_pos[1] / camera.zoom + camera.offset_y
        col = int(wx) // TILE_SIZE
        row = int(wy) // TILE_SIZE
        hovered_layer = Layer.Ground
        for layer in sorted(map.tiles_by_layer.keys(), key=lambda l: -l.value):
            grid = map.tiles_by_layer[layer]
            if 0 <= row < len(grid) and 0 <= col < len(grid[0]):
                t = grid[row][col]
                if t and getattr(t, "overlay_code", ""):
                    hovered_layer = layer
                    break

        items = [
            ("Hovered",        self.controller._tile_under_mouse(mouse_pos, camera, map), OUTLINE_HOVER),
            ("Selected",       self.editor.selected_tile,                     OUTLINE_SEL),
            ("Choice",         None,                                         OUTLINE_CHOICE),
            ("Layer Hovered",  hovered_layer,                                None),
            ("Layer Selected", self.editor.current_layer,                   None),
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
        x0 = self.controller.toolbar.x + self.controller.toolbar.size + 20
        y0 = self.controller.toolbar.y
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
            if label == "Choice" and self.editor.current_choice:
                sprite = load_image(self.editor.current_choice, (TILE_SIZE, TILE_SIZE))
            elif tile:
                sprite = tile.sprite
            if sprite:
                panel.blit(sprite, ((panel_w - TILE_SIZE)//2, ty))
            rect = pygame.Rect((panel_w - TILE_SIZE)//2, ty, TILE_SIZE, TILE_SIZE)
            if color:
                pygame.draw.rect(panel, color, rect, 3)
            text = font.render(label, True, (255, 255, 255))
            panel.blit(text, (margin_x, ty + TILE_SIZE + 2))

        screen.blit(panel, (x0, y0))
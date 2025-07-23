import pygame
from pathlib import Path
from roguelike_engine.utils.loader import load_image
from roguelike_engine.config.config_tiles import TILE_SIZE, OVERLAY_CODE_MAP, DEFAULT_TILE_MAP
from roguelike_engine.config.map_config import global_map_settings
from roguelike_editors.tiles.common.view import screen_to_tile
from roguelike_editors.tiles.tiles_toolbar_panel.tile_toolbar_view import TileToolbarView

from roguelike_editors.tiles.tiles_editor_config import ICON_PATHS_TILE_TOOLBAR

class TileToolbarController:
    """
    Barra de herramientas para el TileEditorController:
      - select
      - brush
      - eyedropper
      - view
    """

    def __init__(self, editor_controller):        
        self.editor_controller = editor_controller
        self.editor_state = editor_controller.editor

        # Cargar iconos (64×64)
        self.icons = {
            tool: load_image(path, (64, 64))
            for tool, path in ICON_PATHS_TILE_TOOLBAR.items()
        }

        # Layout
        self.x = 10
        self.y = 10
        self.size = 64
        self.padding = 8

        # Rects para detectar clicks
        self.icon_rects: dict[str, pygame.Rect] = {}

        self.view = TileToolbarView(self)

    def apply_eyedropper(self, mouse_pos, camera, game_map):
        """Use eyedropper to pick tile sprite under cursor and set brush choice."""
        col, row = screen_to_tile(mouse_pos, camera)
        if not (0 <= row < len(game_map.tiles) and 0 <= col < len(game_map.tiles[0])):
            return
        tile = game_map.tiles[row][col]
        code = tile.overlay_code or tile.tile_type or "#"
        asset_name = OVERLAY_CODE_MAP.get(code) or DEFAULT_TILE_MAP.get(code) or DEFAULT_TILE_MAP.get('#')
        if not asset_name:
            return
        choice_path = f"tiles/{asset_name}.png"
        self.select_tile(choice_path)
        sprite = load_image(choice_path, (TILE_SIZE, TILE_SIZE))
        tile.sprite = sprite
        tile.scaled_cache.clear()
        tile.overlay_code = code
        game_map.view.invalidate_cache()

    def update(self, mouse_pos, camera, game_map):
        """Handle continuous erase (right-click) and bucket fill (Shift+left) for brush tool."""
        if self.editor_state.current_tool != "brush":
            return
        left, _, right = pygame.mouse.get_pressed()
        # Erase with right click
        if right:
            print("[DEBUG][ERASE] Using erase tool")
            tile = self.editor_controller._tile_under_mouse(mouse_pos, camera, game_map)
            if tile:
                row = tile.y // TILE_SIZE; col = tile.x // TILE_SIZE
                try:
                    game_map.matrix[row][col] = None
                except Exception:
                    pass
            return
        # Bucket fill with Shift + left click
        keys = pygame.key.get_pressed()
        if left and (keys[pygame.K_LSHIFT] or keys[pygame.K_RSHIFT]):
            print("[DEBUG][BUCKET] Using Bucket tool")
            tile = self.editor_controller._tile_under_mouse(mouse_pos, camera, game_map)
            selected = getattr(self.editor_state, 'selected_tile', None)
            if tile and selected is not None:
                row = tile.y // TILE_SIZE; col = tile.x // TILE_SIZE
                try:
                    target = game_map.matrix[row][col]
                except Exception:
                    target = None
                if target != selected:
                    self.editor_controller._bucket_fill(game_map, row, col, target, selected)
            return


    def select_tile(self, choice_path):
        """
        Selecciona un tile y cambia herramienta a brush.
        """
        self.editor_state.current_choice = choice_path
        self.editor_state.current_tool = "brush"

    def drag(self, mouse_pos):
        """
        Actualiza posición de la toolbar durante arrastre.
        """
        ts = self.editor_state.toolbar_state
        if ts.dragging:
            ts.pos = (mouse_pos[0] - ts.drag_offset[0], mouse_pos[1] - ts.drag_offset[1])

    def stop_drag(self):
        """
        Finaliza arrastre de la toolbar.
        """
        self.editor_state.toolbar_state.dragging = False
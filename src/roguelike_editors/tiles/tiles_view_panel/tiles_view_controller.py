from roguelike_editors.tiles.tiles_view_panel.tiles_view_view import TilesViewPanelView
from pathlib import Path
from roguelike_engine.utils.loader import load_image
from roguelike_engine.config.config_tiles import TILE_SIZE, OVERLAY_CODE_MAP, DEFAULT_TILE_MAP

class TilesViewPanelController:
    """Controller for the Tiles View Panel"""
    def __init__(self, editor_controller, state):
        self.editor_controller = editor_controller
        self.editor_state = editor_controller.editor
        self.state = state
        # Panel view
        self.view = TilesViewPanelView(self, state)

    def render(self, screen, camera, game_map):
        # Delegate rendering to panel view
        self.view.render(screen, camera, game_map)

    def _tile_under_mouse(self, mouse_pos, camera, game_map):
        return self.editor_controller._tile_under_mouse(mouse_pos, camera, game_map)

    def apply_brush(self, mouse_pos, camera, game_map):
        """Apply overlay brush at the given mouse position."""
        ts = self.editor_state.toolbar_state
        # Only overlay brush when collision mode is off
        if ts.show_collisions or ts.show_collisions_overlay:
            return
        choice = self.editor_state.current_choice
        if not choice:
            return
        tile = self._tile_under_mouse(mouse_pos, camera, game_map)
        if not tile:
            return
        row = tile.y // TILE_SIZE
        col = tile.x // TILE_SIZE
        # Skip duplicate cell
        if self.editor_controller._last_brush_cell == (row, col):
            return
        self.editor_controller._last_brush_cell = (row, col)
        # Determine code from asset path
        asset_name = Path(choice).stem
        code = next((k for k, v in OVERLAY_CODE_MAP.items() if v == asset_name), None)
        if code is None:
            code = next((k for k, v in DEFAULT_TILE_MAP.items() if v == asset_name), None)
        if code is None:
            return
        # Update tile object
        tile.overlay_code = code
        sprite = load_image(choice, (TILE_SIZE, TILE_SIZE))
        tile.sprite = sprite
        tile.scaled_cache.clear()
        # Update full overlay layer data for current layer
        layer = self.editor_state.current_layer
        try:
            game_map.layers[layer][row][col] = code
        except Exception:
            pass
        # Mark pending tile zone for persistence
        zone_name, offx, offy = game_map.get_zone_for(row, col)
        self.editor_controller._pending_tile_zones.add(zone_name)
        game_map.view.invalidate_cache()

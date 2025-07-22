from roguelike_editors.tiles.tiles_collision_panel.tiles_collision_panel_view import TilesCollisionPanelView
import pygame
from roguelike_engine.config.config_tiles import TILE_SIZE

class TilesCollisionPanelController:
    """Controller for the Tiles Collision Panel"""
    def __init__(self, editor_controller, state):
        self.editor_controller = editor_controller
        self.editor_state = editor_controller.editor
        self.state = state
        # Panel view
        self.view = TilesCollisionPanelView(self, state)

    def render(self, screen):
        # Delegate rendering to panel view
        self.view.render(screen)

    def apply_brush(self, mouse_pos, camera, game_map):
        """Apply collision brush at the given mouse position."""
        ts = self.editor_state.toolbar_state
        if not ((ts.show_collisions or ts.show_collisions_overlay) and ts.collision_choice):
            return
        tile, row, col = self.editor_controller._get_brush_cell(mouse_pos, camera, game_map)
        if not tile:
            return
        # Set collision state
        solid = (ts.collision_choice == '#')
        tile.solid = solid
        try:
            game_map.matrix[row][col] = ts.collision_choice
        except Exception:
            pass
        # Update solid_tiles list
        if solid:
            if tile not in game_map.solid_tiles:
                game_map.solid_tiles.append(tile)
        else:
            if tile in game_map.solid_tiles:
                game_map.solid_tiles.remove(tile)
        # Batch collision change
        zone_name, offx, offy = game_map.get_zone_for(row, col)
        local_r, local_c = row - offy, col - offx
        if zone_name in game_map.collision_layers:
            grid = game_map.collision_layers[zone_name]
            if 0 <= local_r < len(grid) and 0 <= local_c < len(grid[0]):
                grid[local_r][local_c] = ts.collision_choice
                self.editor_controller._pending_collision_zones.add(zone_name)
                game_map.view.update_chunks(game_map, camera, [(row, col)])
            else:
                print(f"[Warning] Colisión fuera de rango en zona '{zone_name}': local=({local_r},{local_c}), tamaño=({len(grid)},{len(grid[0])})")

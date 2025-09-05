from roguelike_editors.tiles.tiles_collision_panel.tiles_collision_panel_view import TilesCollisionPanelView

import logging
logger = logging.getLogger(__name__)


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
        """Apply collision brush at the given mouse position, respecting brush size."""
        ts = self.editor_state.toolbar_state
        if not ((ts.show_collisions or ts.show_collisions_overlay) and ts.collision_choice):
            return
        tile, row, col = self.editor_controller._get_brush_cell(mouse_pos, camera, game_map)
        if not tile:
            return
        # Get brush size
        w, h = self.editor_state.size_panel_state.selected_size
        cells = []
        for dy in range(h):
            for dx in range(w):
                r = row + dy
                c = col + dx
                # Skip out-of-bounds
                if not (0 <= r < len(game_map.tiles) and 0 <= c < len(game_map.tiles[0])):
                    continue
                t = game_map.tiles[r][c]
                # Set collision state
                solid = (ts.collision_choice == '#')
                t.solid = solid
                try:
                    game_map.matrix[r][c] = ts.collision_choice
                except Exception:
                    pass
                # Update solid_tiles list
                if solid:
                    if t not in game_map.solid_tiles:
                        game_map.solid_tiles.append(t)
                else:
                    if t in game_map.solid_tiles:
                        game_map.solid_tiles.remove(t)
                cells.append((r, c))
                # Batch collision change in zone
                zone_name, offx, offy = game_map.get_zone_for(r, c)
                local_r, local_c = r - offy, c - offx
                if zone_name in game_map.collision_layers:
                    grid = game_map.collision_layers[zone_name]
                    if 0 <= local_r < len(grid) and 0 <= local_c < len(grid[0]):
                        grid[local_r][local_c] = ts.collision_choice
                        self.editor_controller._pending_collision_zones.add(zone_name)
                    else:
                        logger.warning(f"Colisión fuera de rango en zona '{zone_name}': local=({local_r},{local_c}), tamaño=({len(grid)},{len(grid[0])})")
        # Update view for all painted cells
        if cells:
            # Tutorial pulse: collision painted
            try:
                setattr(self.editor_state, 'tutorial_collision_painted_pulse', True)
            except Exception:
                pass
            game_map.view.update_chunks(game_map, camera, cells)


import pygame

from roguelike_engine.config.config_tiles import TILE_SIZE

from roguelike_editors.tiles.tiles_picker_panel.tile_picker_controller import TilePickerController
from roguelike_editors.tiles.tiles_toolbar_panel.tile_toolbar_controller import TileToolbarController
from roguelike_editors.tiles.tiles_view_panel.tiles_view_controller import TilesViewPanelController
from roguelike_editors.tiles.tiles_title.tiles_tiles_controller import TilesTitleController
from roguelike_editors.tiles.tiles_collision_panel.tiles_collision_panel_controller import TilesCollisionPanelController
from roguelike_editors.tiles.layers_panel.layers_panel_controller import LayersPanelController
from roguelike_editors.tiles.size_panel.size_panel_controller import SizePanelController
from roguelike_editors.tiles.tile_outline_view import TileOutlineView
from pathlib import Path
from roguelike_engine.utils.loader import load_image
import roguelike_engine.config.config_tiles as ct
from roguelike_engine.config.config_tiles import DEFAULT_TILE_MAP
from roguelike_editors.tiles.common.controller import flood_fill
from roguelike_editors.tiles.common.view import screen_to_tile
from roguelike_engine.config.map_config import global_map_settings
import threading



class TileEditorController:
    """
    • Contorno verde  → tile seleccionado
    • Contorno cian   → tile bajo el cursor
    • Toolbar de herramientas
    """
    def __init__(self, editor_state, picker_state):        
        self.editor  = editor_state         # instancia de TileEditorControllerState
        self.picker =                       TilePickerController(self, editor_state, picker_state)
        self.toolbar =                      TileToolbarController(self)        
        self.view_panel_controller =        TilesViewPanelController(self, editor_state.view_panel_state)
        self.title_controller =             TilesTitleController(editor_state, editor_state.title_state)
        self.collision_panel_controller =   TilesCollisionPanelController(self, editor_state.collision_panel_state)
        self.layers_panel_controller =      LayersPanelController(editor_state, editor_state.layers_panel_state)
        self.size_panel_controller =        SizePanelController(self, editor_state.size_panel_state)
        self.outline_view =                 TileOutlineView(self, editor_state)
        
        self.brush_cache: dict[str, pygame.Surface] = {}
        
        self._last_brush_cell: tuple[int,int] | None = None
        self._pending_collision_zones = set()
        self._pending_tile_zones = set()
        self._pending_cells = []

    def select_tile_at(self, mouse_pos, camera, map):
        tile = self._tile_under_mouse(mouse_pos, camera, map)
        if tile:
            self.editor.selected_tile = tile
            # Abrir paleta de selección de tiles
            self.picker.open()

    def apply_brush(self, mouse_pos, camera, map):
        """
        Delegates brush painting to collision or overlay panel controllers,
        handling both collision and overlay brush logic inline.
        Also records each painted cell for partial view updates.
        """
        """
        Delegates brush painting to collision or overlay panel controllers,
        handling both collision and overlay brush logic inline.
        """
        ts = self.editor.toolbar_state
        # Collision brush
        if (ts.show_collisions or ts.show_collisions_overlay) and ts.collision_choice:
            return self.collision_panel_controller.apply_brush(mouse_pos, camera, map)

        # Overlay brush: only when collision modes are off
        if ts.show_collisions or ts.show_collisions_overlay:
            return

        choice = self.editor.current_choice
        if not choice:
            return
        tile, row, col = self._get_brush_cell(mouse_pos, camera, map)
        if not tile:
            return
        # Determine code from asset path
        # Build relative key: strip 'tiles/' prefix and extension
        choice_rel = choice.replace("\\", "/")
        if choice_rel.startswith("tiles/"):
            choice_rel = choice_rel[len("tiles/"):]
        key = choice_rel.rsplit(".", 1)[0]
        # Try direct overlay mapping
        code = key if key in ct.OVERLAY_CODE_MAP else None
        if code is None:
            # fallback: match mapping value
            code = next((k for k, v in ct.OVERLAY_CODE_MAP.items() if v == key), None)
        if code is None:
            # fallback: default char mapping
            code = next((k for k, v in DEFAULT_TILE_MAP.items() if v == key), None)
        if code is None:
            return
        # Update tile object and paint according to brush size
        w, h = self.editor.size_panel_state.selected_size
        sprite = load_image(choice, (TILE_SIZE, TILE_SIZE))
        layer = self.editor.current_layer
        # Paint rectangle of size w x h from top-left cell
        for dy in range(h):
            for dx in range(w):
                r = row + dy
                c = col + dx
                if 0 <= r < len(map.tiles) and 0 <= c < len(map.tiles[0]):
                    try:
                        map.layers[layer][r][c] = code
                    except Exception:
                        continue
                    t = map.tiles[r][c]
                    t.overlay_code = code
                    t.sprite = sprite
                    t.scaled_cache.clear()
                    zone_name, offx, offy = map.get_zone_for(r, c)
                    self._pending_tile_zones.add(zone_name)
                    self._pending_cells.append((r, c))
        map.view.update_chunks(map, camera, self._pending_cells)




    def apply_eyedropper(self, mouse_pos, camera, map):
        """
        Delegates eyedropper action to TileToolbarController.
        """
        return self.toolbar.apply_eyedropper(mouse_pos, camera, map)

    def _tile_under_mouse(self, mouse_pos, camera, map):
        """
        Determine the tile under the given screen coordinates.

        Converts the screen (mouse_pos) through the camera's zoom and offset
        to world coordinates, maps them to tile grid indices (col, row),
        checks bounds, and returns the tile if within the map; otherwise None.
        """
        col, row = screen_to_tile(mouse_pos, camera)
        if 0 <= row < len(map.tiles) and 0 <= col < len(map.tiles[0]):
            return map.tiles[row][col]
        return None

    def _get_brush_cell(self, mouse_pos, camera, map):
        """
        Helper to get the tile under mouse, compute row/col,
        skip duplicates, and update last brush cell.
        """
        tile = self._tile_under_mouse(mouse_pos, camera, map)
        if not tile:
            return None, None, None
        row = tile.y // TILE_SIZE
        col = tile.x // TILE_SIZE
        if self._last_brush_cell == (row, col):
            return None, None, None
        self._last_brush_cell = (row, col)
        return tile, row, col

    def update(self, camera, game_map):
        """
        1) Actualiza el tile bajo el cursor (hover).
        2) Delegates continuous input actions to TileToolbarController.
        """
        # --- 1) Hover del cursor ---
        col, row = screen_to_tile(pygame.mouse.get_pos(), camera)
        self.editor.hovered_tile = (col, row)
        if not self.editor.active or self.editor.picker_state.open:
            return
        # Delegate continuous actions to toolbar controller
        return self.toolbar.update(pygame.mouse.get_pos(), camera, game_map)

    def _bucket_fill(self, game_map, start_row, start_col, target, replacement):
        """
        Flood-fill 4-direccional iterativo.
        """
        flood_fill(game_map.matrix, start_row, start_col, target, replacement)

    def start_brush(self):
        """
        Begin a new brush operation by resetting pending change trackers.

        Clears the sets tracking modified collision and overlay zones,
        cell list, and resets the last brush cell state so subsequent strokes
        start with a clean slate.
        """
        """
        Begin a new brush operation by resetting pending change trackers.

        Clears the sets tracking modified collision and overlay zones,
        and resets the last brush cell state so subsequent strokes
        start with a clean slate.
        """
        self._pending_collision_zones = set()
        self._pending_tile_zones = set()
        self._pending_cells = []
        self._last_brush_cell = None

    def flush_brush(self, map, camera):
        """
        Finalize brush stroke: save collision zones and defer heavy overlay writes.
        Then trigger partial redraws of only modified chunks.
        """
        """
        Finalize brush stroke: save collision zones immediately and
        defer heavy overlay writes to a background thread.
        """
        # Capture and clear pending state
        collision_zones = list(self._pending_collision_zones)
        tile_zones = list(self._pending_tile_zones)
        cells = list(self._pending_cells)
        # reset trackings
        self._pending_collision_zones.clear()
        self._pending_tile_zones.clear()
        self._pending_cells.clear()
        self._last_brush_cell = None


        # Synchronously save collision changes
        for zone in collision_zones:
            map.collision_manager.save(zone)

        # Asynchronously save overlay changes
        def _save_overlays(zones):
            from roguelike_engine.map.model.overlay.overlay_manager import save_layers
            for zone in zones:
                offx, offy = global_map_settings.zone_offsets.get(zone, (0, 0))
                if zone != 'no_zone':
                    zh, zw = global_map_settings.zone_height, global_map_settings.zone_width
                else:
                    zh = len(map.tiles)
                    zw = len(map.tiles[0]) if map.tiles else 0
                zone_layers = {}
                for l, full in map.layers.items():
                    sub = []
                    for ry in range(zh):
                        y = offy + ry
                        sub.append(full[y][offx:offx+zw] if 0 <= y < len(full) else [''] * zw)
                    zone_layers[l] = sub
                save_layers(zone, zone_layers)
        threading.Thread(target=_save_overlays, args=(tile_zones,), daemon=True).start()

        # Update caches
        try:
            map.save_cache()
        except Exception as e:
            print(f"[ERROR][TileEditorController] failed to update map cache: {e}")
        map.collision_layers = map.collision_manager.load(map)
        try:
            map.view.update_chunks(map, camera, cells)
        except Exception:
            map.view.invalidate_cache()
        if hasattr(self, "ecs_world"):
            self.ecs_world.invalidate_spatial_index()
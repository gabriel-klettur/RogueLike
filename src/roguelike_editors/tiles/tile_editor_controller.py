
import pygame

from roguelike_engine.config.config_tiles import TILE_SIZE

from roguelike_editors.tiles.tiles_picker_panel.tile_picker_controller import TilePickerController
from roguelike_editors.tiles.tiles_toolbar_panel.tile_toolbar_controller import TileToolbarController
from roguelike_editors.tiles.tiles_view_panel.tiles_view_controller import TilesViewPanelController
from roguelike_editors.tiles.tiles_title.tiles_tiles_controller import TilesTitleController
from roguelike_editors.tiles.tiles_collision_panel.tiles_collision_panel_controller import TilesCollisionPanelController
from roguelike_editors.tiles.layers_panel.layers_panel_controller import LayersPanelController
from roguelike_editors.tiles.tile_outline_view import TileOutlineView
from pathlib import Path
from roguelike_engine.utils.loader import load_image
from roguelike_engine.config.config_tiles import OVERLAY_CODE_MAP, DEFAULT_TILE_MAP
from roguelike_editors.tiles.common.controller import flood_fill



class TileEditorController:
    """
    • Contorno verde  → tile seleccionado
    • Contorno cian   → tile bajo el cursor
    • Toolbar de herramientas
    """
    def __init__(self, editor_state, picker_state):        
        self.editor  = editor_state         # instancia de TileEditorControllerState
        self.picker =                       TilePickerController(editor_state, picker_state)
        self.toolbar =                      TileToolbarController(self)        
        self.view_panel_controller =        TilesViewPanelController(self, editor_state.view_panel_state)
        self.title_controller =             TilesTitleController(editor_state, editor_state.title_state)
        self.collision_panel_controller =   TilesCollisionPanelController(self, editor_state.collision_panel_state)
        self.layers_panel_controller =      LayersPanelController(editor_state, editor_state.layers_panel_state)
        self.outline_view =                 TileOutlineView(self, editor_state)
        
        self.brush_cache: dict[str, pygame.Surface] = {}
        
        self._last_brush_cell: tuple[int,int] | None = None
        self._pending_collision_zones = set()
        self._pending_tile_zones = set()

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
        tile = self._tile_under_mouse(mouse_pos, camera, map)
        if not tile:
            return
        row = tile.y // TILE_SIZE
        col = tile.x // TILE_SIZE
        # Skip duplicate cell
        if self._last_brush_cell == (row, col):
            return
        self._last_brush_cell = (row, col)
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
        layer = self.editor.current_layer
        try:
            map.layers[layer][row][col] = code
        except Exception:
            pass
        # Mark pending tile zone for persistence
        zone_name, offx, offy = map.get_zone_for(row, col)
        self._pending_tile_zones.add(zone_name)
        map.view.invalidate_cache()




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
        and resets the last brush cell state so subsequent strokes
        start with a clean slate.
        """
        self._pending_collision_zones = set()
        self._pending_tile_zones = set()
        self._last_brush_cell = None

    def flush_brush(self, map):
        """
        Finalize and persist all modifications made during the brush stroke.

        Saves changes for each affected collision and overlay zone to disk,
        reloads underlying data stores, updates in-memory caches and view
        invalidations, and then clears pending trackers for the next stroke.
        """

        # Flush collision layer saves
        for zone in getattr(self, '_pending_collision_zones', []):

            map.collision_manager.save(zone)
        # Flush tile overlay saves
        
        from roguelike_engine.map.model.overlay.overlay_manager import save_layers
        for zone in getattr(self, '_pending_tile_zones', []):
            offx, offy = global_map_settings.zone_offsets.get(zone, (0, 0))
            if zone != 'no_zone':
                zh, zw = global_map_settings.zone_height, global_map_settings.zone_width
            else:
                zh, zw = len(map.tiles), len(map.tiles[0]) if map.tiles else 0
            zone_layers = {}
            for l, full in map.layers.items():
                sub = []
                for ry in range(zh):
                    y = offy + ry
                    sub.append(full[y][offx:offx+zw] if 0 <= y < len(full) else [''] * zw)
                zone_layers[l] = sub
            save_layers(zone, zone_layers)
            # Debug: recargar JSON justo tras guardar
            from roguelike_engine.map.model.overlay.factory import get_overlay_store
            store = get_overlay_store()
            raw = store.load(zone)

        # Actualizar cache tras guardar overlays y colisiones
        try:
            map.save_cache()

        except Exception as e:
            print(f"[ERROR][TileEditorController] failed to update map cache: {e}")
        # Refresh in-memory collision layers and invalidate view cache
        map.collision_layers = map.collision_manager.load(map)
        map.view.invalidate_cache()

        if hasattr(self, "ecs_world"):
            self.ecs_world.invalidate_spatial_index()

        # Reset pending
        self._pending_collision_zones.clear()
        self._pending_tile_zones.clear()
        self._last_brush_cell = None
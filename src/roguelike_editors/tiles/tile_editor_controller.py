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
from roguelike_editors.tiles.tiles_tutorial_panel.tiles_tutorial_panel_controller import TilesTutorialPanelController

from roguelike_engine.utils.loader import load_image
from roguelike_editors.tiles.tiles_editor_config import BRUSH_UPDATE_THROTTLE_MS
import roguelike_engine.config.config_tiles as ct
from roguelike_engine.config.config_tiles import DEFAULT_TILE_MAP
from roguelike_editors.tiles.common.controller import flood_fill
from roguelike_editors.tiles.common.view import screen_to_tile
from roguelike_engine.config.map_config import global_map_settings
import threading
from roguelike_ui.ui_blocker import is_blocked

import logging
logger = logging.getLogger(__name__)

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
        self.layers_panel_controller =      LayersPanelController(self, editor_state.layers_panel_state)
        self.size_panel_controller =        SizePanelController(self, editor_state.size_panel_state)
        self.outline_view =                 TileOutlineView(self, editor_state)
        # Tutorial panel controller (overlay)
        self.tutorial_controller =          TilesTutorialPanelController(self)
        
        self.brush_cache: dict[str, pygame.Surface] = {}
        # Cache to avoid recomputing overlay code mapping for the same choice path
        self._code_cache: dict[str, str] = {}
        # Track whether we already updated chunks during this stroke
        self._did_partial_updates: bool = False
        
        # Throttle timer for chunk updates (ms since pygame init)
        self._last_chunk_update_ms: int = 0
        
        self._last_brush_cell: tuple[int,int] | None = None
        self._pending_collision_zones = set()
        self._pending_tile_zones = set()
        self._pending_cells = []
        self._pending_cells_set: set[tuple[int, int]] = set()

    def select_tile_at(self, mouse_pos, camera, map):
        tile = self._tile_under_mouse(mouse_pos, camera, map)
        if tile:
            self.editor.selected_tile = tile

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
        # Determine code from asset path (cached)
        code = self._code_cache.get(choice)
        if code is None:
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
            self._code_cache[choice] = code
        # Update tile object and paint according to brush size
        w, h = self.editor.size_panel_state.selected_size
        # Cache brush sprite at TILE_SIZE to avoid reloading every frame
        sprite = self.brush_cache.get(choice)
        if sprite is None:
            sprite = load_image(choice, (TILE_SIZE, TILE_SIZE))
            self.brush_cache[choice] = sprite
        layer = self.editor.current_layer
        codes_grid = map.layers.get(layer)
        # Paint rectangle of size w x h from top-left cell
        changed_cells: list[tuple[int, int]] = []
        t0 = pygame.time.get_ticks()
        for dy in range(h):
            for dx in range(w):
                r = row + dy
                c = col + dx
                if 0 <= r < len(map.tiles) and 0 <= c < len(map.tiles[0]):
                    # Skip if current layer grid already has the same code (true no-op)
                    existing = None
                    try:
                        if codes_grid and 0 <= r < len(codes_grid) and 0 <= c < len(codes_grid[0]):
                            existing = codes_grid[r][c]
                    except Exception:
                        existing = None
                    if existing == code:
                        continue
                    t = map.tiles[r][c]
                    # Write overlay code to layers structure
                    map.layers[layer][r][c] = code
                    t.overlay_code = code
                    t.sprite = sprite
                    t.scaled_cache.clear()
                    zone_name, offx, offy = map.get_zone_for(r, c)
                    self._pending_tile_zones.add(zone_name)
                    cell = (r, c)
                    if cell not in self._pending_cells_set:
                        self._pending_cells.append(cell)
                        self._pending_cells_set.add(cell)
                    changed_cells.append(cell)
        t1 = pygame.time.get_ticks()
        # Update only changed chunks for this brush step (avoid full cache invalidation)
        if changed_cells:
            # Tutorial pulse: painted at least one tile
            try:
                setattr(self.editor, 'tutorial_brush_painted_pulse', True)
            except Exception:
                pass
            # Deduplicate cells and throttle updates to at most once per interval
            now = pygame.time.get_ticks()
            if now - self._last_chunk_update_ms >= BRUSH_UPDATE_THROTTLE_MS:
                unique_cells = list(set(changed_cells))
                try:
                    map.view.update_chunks(map, camera, unique_cells)
                    self._did_partial_updates = True
                    self._last_chunk_update_ms = now
                except Exception:
                    # Fallback to full invalidation only if partial update fails
                    map.view.invalidate_cache()
                    self._did_partial_updates = False
            # Debug perf log for brush inner loop
            try:
                logger.debug(f"[Brush] loop_ms={t1 - t0} cells={len(changed_cells)} throttled={(now - self._last_chunk_update_ms) < BRUSH_UPDATE_THROTTLE_MS}")
            except Exception:
                pass

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
        # Panning with middle mouse
        dx, dy = pygame.mouse.get_rel()
        if pygame.mouse.get_pressed()[1]:
            camera.offset_x -= dx / camera.zoom
            camera.offset_y -= dy / camera.zoom
        # --- 1) Hover del cursor ---
        mouse_pos = pygame.mouse.get_pos()
        # Supresión explícita: si el cursor está sobre el toolbar, anular hover
        over_toolbar = False
        try:
            panel = self.toolbar.view.widget.panel  # ToolbarView.panel
            panel_pos = panel.pos or (self.toolbar.x, self.toolbar.y)
            panel_rect = pygame.Rect(panel_pos, panel.surface.get_size())
            over_toolbar = panel_rect.collidepoint(mouse_pos)
        except Exception:
            over_toolbar = False

        if over_toolbar or is_blocked(*mouse_pos):
            self.editor.hovered_tile = None
        else:
            col, row = screen_to_tile(mouse_pos, camera)
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
        self._pending_cells_set = set()
        self._last_brush_cell = None
        self._did_partial_updates = False
        self._last_chunk_update_ms = 0

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
        self._pending_cells_set.clear()
        self._last_brush_cell = None

        # If nothing changed during this stroke, do not touch caches or layers
        if not collision_zones and not tile_zones and not cells:
            return


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
            logger.error(f"[ERROR][TileEditorController] failed to update map cache: {e}")
        map.collision_layers = map.collision_manager.load(map)
        try:
            if not self._did_partial_updates and cells:
                map.view.update_chunks(map, camera, cells)
        except Exception:
            map.view.invalidate_cache()
        # Ensure immediate on-screen refresh regardless of partial update path
        # Some scenarios (very short strokes, throttled updates, or layer filters)
        # may leave stale chunk caches; force a full cache invalidation so the
        # next render rebuilds the affected zoom surfaces from current in-memory layers.
        try:
            map.view.invalidate_cache()
        except Exception:
            pass
        if hasattr(self, "ecs_world"):
            try:
                # Ensure ECS rebuild uses the same MapManager instance being edited
                try:
                    self.ecs_world.map_manager = map
                except Exception:
                    pass
                self.ecs_world.rebuild_spatial_index()
            except Exception:
                # Fallback: at least mark dirty
                try:
                    self.ecs_world.invalidate_spatial_index()
                except Exception:
                    pass
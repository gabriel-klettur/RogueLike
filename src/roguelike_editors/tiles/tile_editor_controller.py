
import pygame

from roguelike_engine.config.config_tiles import TILE_SIZE

from roguelike_engine.config.map_config import global_map_settings
from roguelike_editors.tiles.tiles_picker_panel.tile_picker_controller import TilePickerController
from roguelike_editors.tiles.tiles_toolbar_panel.tile_toolbar_controller import TileToolbarController
from roguelike_editors.tiles.tiles_view_panel.tiles_view_controller import TilesViewPanelController
from roguelike_editors.tiles.tiles_title.tiles_tiles_controller import TilesTitleController
from roguelike_editors.tiles.tiles_collision_panel.tiles_collision_panel_controller import TilesCollisionPanelController
from roguelike_editors.tiles.layers_panel.layers_panel_controller import LayersPanelController
from roguelike_editors.tiles.tile_outline_view import TileOutlineView



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
        Delegates brush painting to collision or overlay panel controllers.
        """
        ts = self.editor.toolbar_state
        # Collision brush
        if (ts.show_collisions or ts.show_collisions_overlay) and ts.collision_choice:
            return self.collision_panel_controller.apply_brush(mouse_pos, camera, map)
        # Overlay brush
        return self.view_panel_controller.apply_brush(mouse_pos, camera, map)


    def apply_eyedropper(self, mouse_pos, camera, map):
        """
        Delegates eyedropper action to TileToolbarController.
        """
        return self.toolbar.apply_eyedropper(mouse_pos, camera, map)

    def _tile_under_mouse(self, mouse_pos, camera, map):
        mx, my = mouse_pos
        world_x = mx / camera.zoom + camera.offset_x
        world_y = my / camera.zoom + camera.offset_y
        col = int(world_x // TILE_SIZE)
        row = int(world_y // TILE_SIZE)
        if 0 <= row < len(map.tiles) and 0 <= col < len(map.tiles[0]):
            return map.tiles[row][col]
        return None

    def update(self, camera, game_map):
        """
        1) Actualiza el tile bajo el cursor (hover).
        2) Delegates continuous input actions to TileToolbarController.
        """
        # --- 1) Hover del cursor ---
        mx, my = pygame.mouse.get_pos()
        wx = mx / camera.zoom + camera.offset_x
        wy = my / camera.zoom + camera.offset_y
        col = int(wx) // TILE_SIZE
        row = int(wy) // TILE_SIZE
        self.editor.hovered_tile = (col, row)
        if not self.editor.active or self.editor.picker_state.open:
            return
        # Delegate continuous actions to toolbar controller
        return self.toolbar.update((mx, my), camera, game_map)

    def _bucket_fill(self, game_map, start_row, start_col, target, replacement):
        """
        Flood-fill 4-direccional iterativo.
        """
        stack = [(start_row, start_col)]
        visited = set()

        while stack:
            r, c = stack.pop()
            if (r, c) in visited:
                continue
            visited.add((r, c))

            # Limites y coincidencia
            if r < 0 or c < 0:
                continue
            try:
                current = game_map.matrix[r][c]
            except Exception:
                # si usas get_tile:
                # current = game_map.get_tile(c, r)
                continue

            if current != target:
                continue

            # Reemplazamos
            try:
                game_map.matrix[r][c] = replacement
            except Exception:
                # o: game_map.set_tile(c, r, replacement)
                pass

            # Vecinos
            stack.extend([
                (r + 1, c),
                (r - 1, c),
                (r, c + 1),
                (r, c - 1),
            ])

    def start_brush(self):
        """Initialize pending brush changes."""
        self._pending_collision_zones = set()
        self._pending_tile_zones = set()
        self._last_brush_cell = None

    def flush_brush(self, map):
        """Persist pending changes after brush stroke ends."""

        # Flush collision layer saves
        for zone in getattr(self, '_pending_collision_zones', []):

            map.collision_manager.save(zone)
        # Flush tile overlay saves
        from roguelike_engine.config.map_config import global_map_settings
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
from pathlib import Path
import pygame

from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_engine.config.config_tiles import OVERLAY_CODE_MAP, DEFAULT_TILE_MAP
from roguelike_engine.config.map_config import global_map_settings

from roguelike_editors.tiles.tiles_picker_panel.tile_picker_controller import TilePickerController
from roguelike_editors.tiles.tiles_toolbar_panel.tile_toolbar_controller import TileToolbarController
from roguelike_editors.tiles.tiles_view_panel.tiles_view_controller import TilesViewPanelController
from roguelike_editors.tiles.tiles_title.tiles_tiles_controller import TilesTitleController
from roguelike_editors.tiles.tiles_collision_panel.tiles_collision_panel_controller import TilesCollisionPanelController
from roguelike_editors.tiles.layers_panel.layers_panel_controller import LayersPanelController
from roguelike_editors.tiles.tile_outline_view import TileOutlineView

from roguelike_engine.utils.loader import load_image

class TileEditorController:
    """
    • Contorno verde  → tile seleccionado
    • Contorno cian   → tile bajo el cursor
    • Toolbar de herramientas
    """
    def __init__(self, editor_state, picker_state):        
        self.editor  = editor_state         # instancia de TileEditorControllerState
        self.picker =                       TilePickerController(editor_state, picker_state)
        self.toolbar =                      TileToolbarController(editor_state)        
        self.view_panel_controller =        TilesViewPanelController(self, editor_state.view_panel_state)
        self.title_controller =             TilesTitleController(editor_state, editor_state.title_state)
        self.collision_panel_controller =   TilesCollisionPanelController(editor_state, editor_state.collision_panel_state)
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
        Pinta el sprite seleccionado sobre el tile bajo la ratón
        y persiste el código de overlay (quitando el prefijo "tiles/").
        """
        # Collision editing when in collision mode
        if (self.editor.toolbar_state.show_collisions or self.editor.toolbar_state.show_collisions_overlay) and self.editor.toolbar_state.collision_choice:
            tile = self._tile_under_mouse(mouse_pos, camera, map)
            if tile:
                # Set collision state
                solid = True if self.editor.toolbar_state.collision_choice == '#' else False
                tile.solid = solid
                # Update matrix
                row = tile.y // TILE_SIZE
                col = tile.x // TILE_SIZE
                # Skip if same cell as last to reduce churn
                if self._last_brush_cell == (row, col):
                    return
                self._last_brush_cell = (row, col)
                try:
                    map.matrix[row][col] = self.editor.toolbar_state.collision_choice
                except Exception:
                    pass
                # Update MapManager.solid_tiles for collision
                if solid:
                    if tile not in map.solid_tiles:
                        map.solid_tiles.append(tile)
                else:
                    if tile in map.solid_tiles:
                        map.solid_tiles.remove(tile)

                # Batch collision change and immediate update
                zone_name, offx, offy = map.get_zone_for(row, col)
                local_r, local_c = row - offy, col - offx
                if zone_name in map.collision_layers:
                    grid = map.collision_layers[zone_name]
                    # Bounds check before updating
                    if 0 <= local_r < len(grid) and 0 <= local_c < len(grid[0]):
                        grid[local_r][local_c] = self.editor.toolbar_state.collision_choice
                        self._pending_collision_zones.add(zone_name)
                        # Immediate collision save and refresh
                        map.collision_manager.save(zone_name)
                        map.collision_layers = map.collision_manager.load(map)
                        map.view.invalidate_cache()
                    else:
                        print(f"[Warning] Colisión fuera de rango en zona '{zone_name}': local=({local_r},{local_c}), tamaño=({len(grid)},{len(grid[0])})")
                return

        # 1) Encuentra el tile bajo el cursor
        tile = self._tile_under_mouse(mouse_pos, camera, map)
        if not tile or not self.editor.current_choice:
            return

        # 2) Carga el nuevo sprite
        choice = self.editor.current_choice
        # Cache sprite surfaces per choice
        if choice not in self.brush_cache:
            self.brush_cache[choice] = load_image(choice, (TILE_SIZE, TILE_SIZE))
        sprite = self.brush_cache[choice]
        tile.sprite = sprite
        tile.scaled_cache.clear()

        # 3) Calcula el código de overlay sin el prefijo "tiles/"
        full = Path(self.editor.current_choice).with_suffix('')  # ej. "tiles/dungeon/dungeon_1"
        try:
            code = full.relative_to("tiles").as_posix()            # "dungeon/dungeon_1"
        except ValueError:
            code = full.as_posix()                                 # si no empieza por "tiles/"

        tile.overlay_code = code

        # 4) Actualizar en memoria y persistir solo la zona
        layer = self.editor.current_layer
        row = tile.y // TILE_SIZE; col = tile.x // TILE_SIZE
        # determinar zona y offsets
        for zn,(ox,oy) in global_map_settings.zone_offsets.items():
            if ox <= col < ox + global_map_settings.zone_width and oy <= row < oy + global_map_settings.zone_height:
                zone_name, offx, offy = zn, ox, oy
                break
        else:
            zone_name, offx, offy = 'no_zone', 0, 0
        # 4.1) actualizar map.layers y map.tiles_by_layer
        try:
            map.layers[layer][row][col] = code
        except Exception:
            pass
        grid = map.tiles_by_layer.get(layer)
        if grid and 0 <= row < len(grid) and 0 <= col < len(grid[0]):
            t = grid[row][col]
            if t:
                t.sprite = sprite
                t.scaled_cache.clear()
                t.overlay_code = code
        # 4.2) extraer subgrids de map.layers para la zona
        # Batch overlay change for later persistence
        self._pending_tile_zones.add(zone_name)

        # Debug for brush
        local_r = row - offy
        local_c = col - offx

        map.view.invalidate_cache()

    def apply_eyedropper(self, mouse_pos, camera, map):
        """
        Selecciona el sprite bajo el cursor, lo aplica al tile y guarda el overlay igual que el brush.
        """
        # 1) Encuentra el tile bajo el cursor
        tile = self._tile_under_mouse(mouse_pos, camera, map)
        if not tile:
            return

        # 2) Determinar código de overlay o tipo base
        code = tile.overlay_code or tile.tile_type or "#"

        # 3) Mapear código a nombre de asset en OVERLAY_CODE_MAP o DEFAULT_TILE_MAP
        if code in OVERLAY_CODE_MAP:
            asset_name = OVERLAY_CODE_MAP[code]
        else:
            asset_name = DEFAULT_TILE_MAP.get(code)
        # Fallback al asset de muro por defecto si no existe mapping
        if not asset_name:
            asset_name = DEFAULT_TILE_MAP.get('#')
        if not asset_name:
            return

        # 4) Ruta relativa para el picker y brush (sin prefijo 'assets/')
        choice_path = f"tiles/{asset_name}.png"
        self.toolbar.select_tile(choice_path)

        # 5) Cargar y asignar sprite al tile
        sprite = load_image(choice_path, (TILE_SIZE, TILE_SIZE))
        tile.sprite = sprite
        tile.scaled_cache.clear()

        # 6) Fijar overlay_code al código original
        tile.overlay_code = code

        # 7) Debug only: EyeDropper, no persistence
        layer = self.editor.current_layer
        row = tile.y // TILE_SIZE; col = tile.x // TILE_SIZE
        # Determine zone for debug
        zone_name = 'no_zone'
        for zn, (ox, oy) in global_map_settings.zone_offsets.items():
            if ox <= col < ox + global_map_settings.zone_width and oy <= row < oy + global_map_settings.zone_height:
                zone_name = zn
                break

        map.view.invalidate_cache()

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
        2) Si el editor está activo y el picker está cerrado, aplica
           pintado continuo, borrado y relleno en bucket.
        """
        # --- 1) Hover del cursor ---
        mx, my = pygame.mouse.get_pos()
        wx = mx / camera.zoom + camera.offset_x
        wy = my / camera.zoom + camera.offset_y
        col = int(wx) // TILE_SIZE
        row = int(wy) // TILE_SIZE
        # Guardamos la celda ‘hovered’ para la vista
        self.editor.hovered_tile = (col, row)

        # --- 2) Si no estamos editando o el picker está abierto, salimos ---
        if not self.editor.active or self.editor.picker_state.open:
            return

        # --- 3) Botones del ratón ---
        left, middle, right = pygame.mouse.get_pressed()

        # Tile actualmente seleccionado en el picker
        selected = getattr(self.editor, 'selected_tile', None)

        # --- 4) PINTAR con botón izquierdo ---
        if left and selected is not None:
            # Ajusta según tu API de game_map:
            # por ejemplo, si accedes directo a la matriz:
            try:
                game_map.matrix[row][col] = selected
            except Exception:
                # o bien: game_map.set_tile(col, row, selected)
                pass

        # --- 5) BORRAR con botón derecho ---
        elif right:
            try:
                game_map.matrix[row][col] = None
            except Exception:
                # o bien: game_map.set_tile(col, row, None)
                pass

        # --- 6) BUCKET FILL con Shift + click izquierdo ---
        keys = pygame.key.get_pressed()
        if left and (keys[pygame.K_LSHIFT] or keys[pygame.K_RSHIFT]) and selected is not None:
            # Obtenemos el valor actual para reemplazar
            try:
                target = game_map.matrix[row][col]
            except Exception:
                # o: target = game_map.get_tile(col, row)
                target = None

            if target != selected:
                self._bucket_fill(game_map, row, col, target, selected)

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
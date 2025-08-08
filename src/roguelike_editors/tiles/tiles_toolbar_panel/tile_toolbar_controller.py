import pygame
from roguelike_engine.utils.loader import load_image
from roguelike_engine.config.config_tiles import TILE_SIZE, OVERLAY_CODE_MAP, DEFAULT_TILE_MAP, INVERSE_OVERLAY_MAP
from roguelike_engine.config.map_config import global_map_settings
from roguelike_engine.map.model.overlay.overlay_manager import load_layers, save_layers
from roguelike_engine.tile.utils.assets import load_base_tile_images
from roguelike_editors.tiles.common.view import screen_to_tile
from roguelike_editors.tiles.tiles_toolbar_panel.tile_toolbar_view import TileToolbarView
from roguelike_editors.tiles.tiles_editor_config import ICON_PATHS_TILE_TOOLBAR, BRUSH_UPDATE_THROTTLE_MS


class TileToolbarController:
    """
    Controlador principal de la barra de herramientas del editor de tiles.

    Provee funcionalidades para:
      - Carga y renderización de iconos de herramientas.
      - Selección de herramienta (select, brush, eyedropper, bucket, erase).
      - Gestión de arrastre (drag & drop) de la barra.
      - Operaciones de borrado y restauración de tiles en el mapa.
    """
    def __init__(self, editor_controller):
        """
        Inicializa el controlador con el editor principal.
        Args:
            editor_controller: Instancia del controlador de editor que contiene estado y vistas.
        """
        self.editor_controller = editor_controller
        self.editor_state = editor_controller.editor
        # Posición inicial y espaciado de la barra
        self.x, self.y = 10, 70
        self.size, self.padding = 64, 8
        # Carga los iconos de la toolbar al tamaño definido
        self.icons = self._load_icons()
        # Rects para detección de clicks sobre iconos
        self.icon_rects: dict[str, pygame.Rect] = {}
        # Instancia de la vista asociada a esta toolbar
        self.view = TileToolbarView(self)        

    def _load_icons(self) -> dict[str, pygame.Surface]:
        """
        Carga todas las imágenes de iconos definidas en la configuración.
        Ajusta cada imagen al tamaño de la toolbar.
        Returns:
            Un diccionario que mapea el nombre de la herramienta a su Surface de pygame.
        """
        return {
            tool: load_image(path, (self.size, self.size))
            for tool, path in ICON_PATHS_TILE_TOOLBAR.items()
        }

    def apply_eyedropper(self, mouse_pos, camera, game_map):
        """
        Selecciona el tile bajo el cursor como nueva brocha (eyedropper).

        Este método:
        1. Convierte coordenadas de pantalla a coordenadas de tile.
        2. Valida que el tile exista en el mapa.
        3. Marca el efecto visual de eyedropper en el estado.
        4. Obtiene el código de overlay o tipo de tile.
        5. Carga y selecciona el asset correspondiente.

        Args:
            mouse_pos: Tuple (x, y) de la posición del cursor.
            camera: Cámara usada para conversión de coordenadas.
            game_map: Instancia del mapa de juego.
        """
        col, row = screen_to_tile(mouse_pos, camera)
        if not self._in_bounds(row, col, game_map):
            return
        tile = game_map.tiles[row][col]
        # If sampling an overlay (non-empty), align the brush layer with EXACT layer that holds that code.
        # If sampling a base/default tile (no overlay), keep current layer unchanged.
        code_here = getattr(tile, 'overlay_code', '') or ''
        if code_here:
            try:
                for lname, grid in game_map.layers.items():
                    if 0 <= row < len(grid) and 0 <= col < len(grid[0]) and grid[row][col] == code_here:
                        self.editor_state.current_layer = lname
                        break
            except Exception:
                pass
        self._flash_eyedropper(tile)
        code = tile.overlay_code or None
        if code is not None and code != '':
            # Overlay present: resolve directly to overlay asset name
            asset_name = OVERLAY_CODE_MAP.get(code)
        else:
            # No overlay: derive base asset then try to map to an overlay variant
            base_char = tile.tile_type or '#'
            base_asset = DEFAULT_TILE_MAP.get(base_char) or DEFAULT_TILE_MAP.get('#')
            # Try to find at least one overlay code that uses the same asset name
            candidates = INVERSE_OVERLAY_MAP.get(base_asset, [])
            if candidates:
                # Prefer the first candidate's asset name
                asset_name = OVERLAY_CODE_MAP.get(candidates[0], base_asset)
            else:
                # No overlay variant exists for this base asset; nothing useful to pick for brush
                return
        if not asset_name:
            return
        self._select_and_load(asset_name, tile, game_map)

    def update(self, mouse_pos, camera, game_map):
        """
        Maneja acciones continuas para la herramienta brush:
        - Borrar con clic derecho (erase).
        - Relleno por cubo con Shift + clic izquierdo (bucket fill).
        Se invoca cada frame para comprobar si hay pulsación de botones.

        Args:
            mouse_pos: Posición actual del ratón.
            camera: Cámara del juego para cálculos.
            game_map: Objeto del mapa de tiles.
        """
        if self.editor_state.current_tool != "brush":
            return
        left, _, right = pygame.mouse.get_pressed()
        if right:
            self._erase_tile(mouse_pos, camera, game_map)
            return
        keys = pygame.key.get_pressed()
        if left and (keys[pygame.K_LSHIFT] or keys[pygame.K_RSHIFT]):
            self._bucket_fill(mouse_pos, camera, game_map)
            return

    def select_tile(self, asset_name: str):
        """
        Selecciona un asset como brocha y cambia la herramienta activa a brush.
        Args:
            asset_name: Nombre base del asset (sin ruta ni extensión).
        """
        choice_path = f"tiles/{asset_name}.png"
        self.editor_state.current_choice = choice_path
        self.editor_state.current_tool = "brush"

    def drag(self, mouse_pos):
        """
        Actualiza la posición de la barra de herramientas durante un arrastre.
        Sólo se mueve si el estado 'dragging' está activo.
        Args:
            mouse_pos: Posición actual del ratón.
        """
        ts = self.editor_state.toolbar_state
        if ts.dragging:
            ts.pos = (mouse_pos[0] - ts.drag_offset[0], mouse_pos[1] - ts.drag_offset[1])

    def stop_drag(self):
        """
        Finaliza el arrastre de la barra, desactivando el flag correspondiente.
        """
        self.editor_state.toolbar_state.dragging = False

    def delete_tile(self, game_map, camera):
        """
        Elimina el sprite de los tiles en la región actualmente seleccionada.
        Recorre la región según el tamaño del brush y limpia cada sprite.
        Args:
            game_map: Mapa de juego donde se aplicará el borrado.
            camera: Cámara del juego para conversión de coordenadas.
        """
        tile = self.editor_state.selected_tile
        if tile is None:
            return
        changed = self._clear_tiles_in_region(tile, game_map)
        # Throttled partial chunk updates for feedback during drag
        if changed:
            now = pygame.time.get_ticks()
            last = getattr(self.editor_controller, "_last_chunk_update_ms", 0)
            if now - last >= BRUSH_UPDATE_THROTTLE_MS:
                try:
                    game_map.view.update_chunks(game_map, camera, list(set(changed)))
                    self.editor_controller._did_partial_updates = True
                    self.editor_controller._last_chunk_update_ms = now
                except Exception:
                    game_map.view.invalidate_cache()
                    self.editor_controller._did_partial_updates = False

    def set_default(self, game_map, camera):
        """
        Restaura la región seleccionada al estado por defecto y guarda cambios de overlay.
        Determina la zona geográfica, recarga mapas base y aplica sprite por defecto.
        Args:
            game_map: Mapa de juego donde se aplicará la restauración.
            camera: Cámara del juego para conversión de coordenadas.
        """
        tile = self.editor_state.selected_tile
        if tile is None:
            return
        # Cache base map once per session to avoid reloading on every drag step
        base_map = getattr(self, "_base_map_cache", None)
        if base_map is None:
            base_map = load_base_tile_images()
            self._base_map_cache = base_map
        changed = self._reset_region_in_memory(tile, game_map, base_map, camera)
        # Throttled partial chunk updates for feedback during drag
        if changed:
            now = pygame.time.get_ticks()
            last = getattr(self.editor_controller, "_last_chunk_update_ms", 0)
            if now - last >= BRUSH_UPDATE_THROTTLE_MS:
                try:
                    game_map.view.update_chunks(game_map, camera, list(set(changed)))
                    self.editor_controller._did_partial_updates = True
                    self.editor_controller._last_chunk_update_ms = now
                except Exception:
                    game_map.view.invalidate_cache()
                    self.editor_controller._did_partial_updates = False

    # --- Métodos auxiliares privados ---
    def _in_bounds(self, row, col, game_map) -> bool:
        """
        Verifica que las coordenadas de fila/columna están dentro de los límites del mapa.
        Returns:
            True si está dentro, False en caso contrario.
        """
        return 0 <= row < len(game_map.tiles) and 0 <= col < len(game_map.tiles[0])

    def _flash_eyedropper(self, tile):
        """
        Inicia el efecto visual de eyedropper y almacena el tile seleccionado.
        """
        self.editor_state.eyedropper_flash_start = pygame.time.get_ticks()
        self.editor_state.selected_tile = tile

    def _select_and_load(self, asset_name, tile, game_map):
        """
        Selecciona la ruta del asset, lo carga y actualiza el sprite del tile.
        Args:
            asset_name: Nombre base del archivo de imagen.
            tile: Instancia del tile a modificar.
            game_map: Mapa de juego para invalidar caché.
        """
        choice_path = f"tiles/{asset_name}.png"
        self.select_tile(asset_name)
        sprite = load_image(choice_path, (TILE_SIZE, TILE_SIZE))
        tile.sprite = sprite
        tile.scaled_cache.clear()
        tile.overlay_code = tile.overlay_code or tile.tile_type or "#"
        game_map.view.invalidate_cache()

    def _erase_tile(self, mouse_pos, camera, game_map):
        """
        Borra el tile bajo el cursor estableciendo su posición a None.
        Utiliza el método interno para localizar el tile.
        """
        tile = self.editor_controller._tile_under_mouse(mouse_pos, camera, game_map)
        if not tile:
            return
        row, col = tile.y // TILE_SIZE, tile.x // TILE_SIZE
        try:
            game_map.matrix[row][col] = None
        except Exception:
            pass

    def _bucket_fill(self, mouse_pos, camera, game_map):
        """
        Rellena una región con la brocha seleccionada usando algoritmo de flood fill.
        Se activa con Shift + clic izquierdo en modo brush.
        """
        tile = self.editor_controller._tile_under_mouse(mouse_pos, camera, game_map)
        selected = getattr(self.editor_state, 'selected_tile', None)
        if not tile or selected is None:
            return
        row, col = tile.y // TILE_SIZE, tile.x // TILE_SIZE
        try:
            target = game_map.matrix[row][col]
        except Exception:
            target = None
        if target != selected:
            self.editor_controller._bucket_fill(game_map, row, col, target, selected)

    def _clear_tiles_in_region(self, tile, game_map):
        """
        Limpia los sprites de una región rectangular centrada en el tile dado.
        """
        layer = self.editor_state.current_layer
        origin_row, origin_col = tile.y // TILE_SIZE, tile.x // TILE_SIZE
        w, h = self.editor_state.size_panel_state.selected_size
        grid = game_map.tiles_by_layer.get(layer)
        changed_cells = []
        for dy in range(h):
            for dx in range(w):
                r, c = origin_row + dy, origin_col + dx
                if grid and 0 <= r < len(grid) and 0 <= c < len(grid[0]):
                    t = grid[r][c]
                    if t:
                        t.sprite = pygame.Surface((TILE_SIZE, TILE_SIZE), pygame.SRCALPHA)
                        t.scaled_cache.clear()
                        # Update overlay in-memory
                        t.overlay_code = ''
                        codes_grid = game_map.layers.get(layer)
                        if codes_grid and 0 <= r < len(codes_grid) and 0 <= c < len(codes_grid[0]):
                            codes_grid[r][c] = ''
                        # Track pending zones/cells for batched persistence and partial redraws
                        zone_name, _, _ = game_map.get_zone_for(r, c)
                        self.editor_controller._pending_tile_zones.add(zone_name)
                        cell = (r, c)
                        if cell not in self.editor_controller._pending_cells_set:
                            self.editor_controller._pending_cells.append(cell)
                            self.editor_controller._pending_cells_set.add(cell)
                        changed_cells.append(cell)
        return changed_cells

    def _determine_zone(self, tile) -> tuple[str, int, int]:
        """
        Identifica la zona del mapa donde se localiza el tile.
        Itera offset de zonas globales para ubicar coordenadas.
        Returns:
            Una tupla (zone_name, offset_x, offset_y).
        """
        origin_row, origin_col = tile.y // TILE_SIZE, tile.x // TILE_SIZE
        for name, (ox, oy) in global_map_settings.zone_offsets.items():
            w, h = global_map_settings.zone_width, global_map_settings.zone_height
            if ox <= origin_col < ox + w and oy <= origin_row < oy + h:
                return name, ox, oy
        return 'no_zone', 0, 0

    def _reset_region_in_memory(self, tile, game_map, base_map, camera):
        """
        Restaura sprites y overlay de una región al estado por defecto SOLO en memoria,
        sin guardar ni invalidar caché. Marca zonas/celdas para persistencia diferida.
        """
        origin_row, origin_col = tile.y // TILE_SIZE, tile.x // TILE_SIZE
        w, h = self.editor_state.size_panel_state.selected_size
        max_r = len(game_map.tiles)
        max_c = len(game_map.tiles[0])
        changed_cells = []
        layer = self.editor_state.current_layer
        for dy in range(h):
            for dx in range(w):
                r, c = origin_row + dy, origin_col + dx
                if 0 <= r < max_r and 0 <= c < max_c:
                    grid = game_map.tiles_by_layer.get(layer)
                    t = grid[r][c] if grid else None
                    if t:
                        default_imgs = base_map.get(t.tile_type)
                        sprite = None
                        if default_imgs is not None:
                            sprite = default_imgs[0] if isinstance(default_imgs, list) else default_imgs
                        else:
                            # Fallback a DEFAULT_TILE_MAP si no hay entrada en base_map
                            variant = DEFAULT_TILE_MAP.get(t.tile_type)
                            if variant:
                                sprite = load_image(f"tiles/{variant}.png", (TILE_SIZE, TILE_SIZE))
                        if sprite is not None:
                            t.sprite = sprite
                            t.scaled_cache.clear()
                        t.overlay_code = ''
                        codes_grid = game_map.layers.get(layer)
                        if codes_grid and 0 <= r < len(codes_grid) and 0 <= c < len(codes_grid[0]):
                            codes_grid[r][c] = ''
                        # Track pending zones/cells
                        zone_name, _, _ = game_map.get_zone_for(r, c)
                        self.editor_controller._pending_tile_zones.add(zone_name)
                        cell = (r, c)
                        if cell not in self.editor_controller._pending_cells_set:
                            self.editor_controller._pending_cells.append(cell)
                            self.editor_controller._pending_cells_set.add(cell)
                        changed_cells.append(cell)
        return changed_cells

"""
Module: roguelike_editors.tiles.tiles_picker_panel.tile_picker_controller

Provides TilePickerController to manage the floating tile picker panel,
including asset loading, directory navigation, tile selection, and persistence of overlay data.
"""
import pygame
import json
from pathlib import Path
from typing import Dict, Tuple, Deque, Optional
from collections import deque

from roguelike_engine.utils.loader import load_image
from roguelike_engine.config.config import ASSETS_DIR
from roguelike_engine.map.model.overlay.overlay_manager import load_layers, save_layers
from roguelike_engine.map.model.layer import Layer
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_editors.tiles.tiles_picker_panel.tile_picker_view import TilePickerView
from roguelike_engine.config.map_config import global_map_settings

from roguelike_editors.tiles.tiles_editor_config import (
    BASE_TILE_DIR,
    ARROW_UP_ICON,
    FOLDER_ICON,
    FILE_PATTERNS,
    THUMB
)

import logging
logger = logging.getLogger(__name__)

class TilePickerController:
    """
    Ventana flotante de selección de tiles y explorador de directorios.
    """

    def __init__(self, editor_controller, editor_state, picker_state):
        self.editor_controller = editor_controller        
        self.editor_state = editor_state
        self.picker_state = picker_state
        self.picker_state = picker_state

        # Directorio base y directorio actual para explorar
        self.base_dir = Path(ASSETS_DIR) / BASE_TILE_DIR
        self.current_dir = self.base_dir

        # Caches para mejorar el rendimiento al abrir el picker
        # - Miniaturas por ruta relativa (value) -> Surface optimizada
        # - Tamaños originales por ruta relativa -> (w, h)
        self._thumb_cache: Dict[str, pygame.Surface] = {}
        self._orig_size_cache: Dict[str, Tuple[int, int]] = {}
        # Iconos reutilizables (evita recargar por cada carpeta)
        thumb_size = (THUMB, THUMB)
        try:
            self._folder_icon = load_image(FOLDER_ICON, thumb_size)
            self._arrow_icon = load_image(ARROW_UP_ICON, thumb_size)
        except Exception:
            # En caso de fallo de carga, usar superficies vacías para no romper la UI
            self._folder_icon = pygame.Surface(thumb_size, pygame.SRCALPHA)
            self._arrow_icon = pygame.Surface(thumb_size, pygame.SRCALPHA)

        # Cola de miniaturas pendientes (carga incremental)
        self._thumb_queue: Deque[str] = deque()
        # Directorio de caché en disco para miniaturas
        root = Path(__file__).parents[4]
        self._thumb_cache_dir: Path = root / "data" / "cache" / "thumbs"
        self._thumb_cache_dir.mkdir(parents=True, exist_ok=True)

        # Lista de entradas (valor, Surface, is_dir)
        
        self.assets = []
        self._load_assets()
        self.view = TilePickerView(self, self.picker_state, self.assets)
        self._load_positions()

    def _load_positions(self):
        """Load tile order from JSON and reorder assets list."""
        try:
            root = Path(__file__).parents[4]
            pos_file = root / "data" / "tiles" / "editor_tiles_picker_position.json"
            # Load maestro JSON with per-folder orders
            if pos_file.exists():
                with open(pos_file, "r", encoding="utf-8") as f:
                    data = json.load(f)
                orders = data.get("orders", {})
            else:
                orders = {}
            # Determine key for current tile directory
            key = str(self.current_dir.relative_to(Path(ASSETS_DIR)))
            order = orders.get(key, [])
            logger.debug(f" Loaded order for '{key}': {order}")
            asset_map = {value: (value, surf, is_dir, orig) for (value, surf, is_dir, orig) in self.assets}
            new_assets = []
            for val in order:
                if val in asset_map:
                    new_assets.append(asset_map.pop(val))
            # Append remaining assets
            new_assets.extend(asset_map.values())
            self.assets = new_assets
            self.view.assets = self.assets
        except Exception as e:
            logger.error(f" Error loading positions: {e}")

    def swap_positions(self, i, j):
        """Swap two assets by index and persist new order to JSON."""
        self.assets[i], self.assets[j] = self.assets[j], self.assets[i]
        self.view.assets = self.assets
        # Invalidate static grid cache after swap so the view rebuilds with new order
        if hasattr(self.view, 'assets_cache_surf'):
            del self.view.assets_cache_surf
        if hasattr(self.view, 'assets_cache_size'):
            del self.view.assets_cache_size
        try:
            root = Path(__file__).parents[4]
            pos_file = root / "data" / "tiles" / "editor_tiles_picker_position.json"
            # Load existing maestro JSON or init empty
            if pos_file.exists():
                with open(pos_file, "r", encoding="utf-8") as f:
                    data = json.load(f)
            else:
                data = {}
            orders = data.get("orders", {})
            # Determine key for current tile directory
            key = str(self.current_dir.relative_to(Path(ASSETS_DIR)))
            orders[key] = [value for (value, _, _, _) in self.assets]
            logger.debug(f" Saved order for '{key}': {orders[key]}")
            data["orders"] = orders
            with open(pos_file, "w", encoding="utf-8") as f:
                json.dump(data, f, indent=2)
        except Exception as e:
            logger.error(f" Error saving positions: {e}")

    def _load_assets(self):
        """
        Rellena self.assets con:
         - Entrada ".." para subir (si no estamos en base)
         - Carpetas en current_dir
         - Archivos que casan con FILE_PATTERNS
        Cada entrada es tupla (value, surface, is_dir).
        """
        logger.debug(f" Loading assets from {self.current_dir}")
        self.assets.clear()
        thumb_size = (THUMB, THUMB)

        # Placeholder for base_dir: occupy first slot without clickable asset
        if self.current_dir == self.base_dir:
            placeholder = pygame.Surface(thumb_size, pygame.SRCALPHA)
            self.assets.append(("", placeholder, False, None))

        # Flecha hacia arriba
        if self.current_dir != self.base_dir:
            # Reutiliza icono cacheado
            self.assets.append(("..", self._arrow_icon, True, None))

        # Subdirectorios
        for entry in sorted(self.current_dir.iterdir()):
            if entry.is_dir():
                # Reutiliza icono cacheado
                self.assets.append((entry.name, self._folder_icon, True, None))

        # Archivos según patrones
        seen = {}
        for pattern in FILE_PATTERNS:
            for f in sorted(self.current_dir.glob(pattern)):
                key = f.name.lower()
                if key not in seen:
                    seen[key] = f

        for f in seen.values():
            rel_path = str(f.relative_to(Path(ASSETS_DIR)))
            try:
                # 1) Si ya está en memoria, usarla
                if rel_path in self._thumb_cache:
                    thumb = self._thumb_cache[rel_path]
                    orig_size = self._orig_size_cache.get(rel_path)
                    self.assets.append((rel_path, thumb, False, orig_size))
                    continue

                # 2) Intentar usar caché en disco (si existe y está fresca por mtime)
                disk_thumb, meta_size = self._get_disk_thumb(rel_path, f, thumb_size)
                if disk_thumb is not None:
                    self._thumb_cache[rel_path] = disk_thumb
                    if meta_size:
                        self._orig_size_cache[rel_path] = meta_size
                    self.assets.append((rel_path, disk_thumb, False, meta_size))
                    continue

                # 3) Si no hay miniatura, insertar placeholder y encolar para carga incremental
                placeholder = pygame.Surface(thumb_size, pygame.SRCALPHA)
                placeholder.fill((40, 40, 40))
                pygame.draw.rect(placeholder, (90, 90, 90), placeholder.get_rect(), 1)
                self.assets.append((rel_path, placeholder, False, None))
                self._thumb_queue.append(rel_path)
            except Exception as e:
                logger.error(f" ERROR cargando {rel_path}: {e}")

    def _disk_thumb_key(self, rel_path: str, src_file: Path) -> str:
        """Genera un nombre de archivo único por ruta + tamaño + mtime."""
        safe = rel_path.replace("\\", "/").replace("/", "__")
        try:
            mtime = int(src_file.stat().st_mtime)
        except Exception:
            mtime = 0
        return f"{safe}_{THUMB}x{THUMB}_{mtime}"

    def _get_disk_thumb(self, rel_path: str, src_file: Path, thumb_size) -> Tuple[Optional[pygame.Surface], Optional[Tuple[int, int]]]:
        """Devuelve (thumb_surface, orig_size) si existe en caché de disco y es válida; si no, (None, None)."""
        key = self._disk_thumb_key(rel_path, src_file)
        png_path = self._thumb_cache_dir / f"{key}.png"
        meta_path = self._thumb_cache_dir / f"{key}.json"
        if png_path.exists():
            try:
                surf = pygame.image.load(str(png_path)).convert_alpha()
                orig_size = None
                if meta_path.exists():
                    with open(meta_path, "r", encoding="utf-8") as mf:
                        md = json.load(mf)
                        w, h = md.get("w"), md.get("h")
                        if isinstance(w, int) and isinstance(h, int):
                            orig_size = (w, h)
                return surf, orig_size
            except Exception as e:
                logger.debug(f" Disk thumb not usable for {rel_path}: {e}")
        return None, None

    def process_thumb_queue(self, max_items: int = 8):
        """Carga incrementalmente miniaturas pendientes para no bloquear el hilo principal."""
        if not self._thumb_queue:
            return
        processed = 0
        thumb_size = (THUMB, THUMB)
        while self._thumb_queue and processed < max_items:
            rel_path = self._thumb_queue.popleft()
            src_file = Path(ASSETS_DIR) / rel_path
            if not src_file.exists():
                continue
            try:
                full_img = pygame.image.load(str(src_file)).convert_alpha()
                orig_size = full_img.get_size()
                thumb = pygame.transform.scale(full_img, thumb_size).convert_alpha()
                # Guardar en memoria
                self._thumb_cache[rel_path] = thumb
                self._orig_size_cache[rel_path] = orig_size
                # Guardar en disco + metadatos
                key = self._disk_thumb_key(rel_path, src_file)
                png_path = self._thumb_cache_dir / f"{key}.png"
                meta_path = self._thumb_cache_dir / f"{key}.json"
                try:
                    pygame.image.save(thumb, str(png_path))
                    with open(meta_path, "w", encoding="utf-8") as mf:
                        json.dump({"w": orig_size[0], "h": orig_size[1]}, mf)
                except Exception as e:
                    logger.debug(f" Could not persist disk thumb for {rel_path}: {e}")
                # Actualizar entrada en self.assets (puede haber cambiado el orden tras _load_positions)
                for i, (v, _, is_dir, _) in enumerate(self.assets):
                    if v == rel_path and not is_dir:
                        self.assets[i] = (rel_path, thumb, False, orig_size)
                        break
                processed += 1
            except Exception as e:
                logger.debug(f" Error generating thumb for {rel_path}: {e}")
        # Invalida la caché de la vista si se procesó algo para forzar el re-render del grid
        if processed and hasattr(self, 'view'):
            if hasattr(self.view, 'assets_cache_surf'):
                try:
                    del self.view.assets_cache_surf
                except Exception:
                    pass
            if hasattr(self.view, 'assets_cache_size'):
                try:
                    del self.view.assets_cache_size
                except Exception:
                    pass

    def _load_tileset_assets(self, image_value: str, grid_size: int):
        """
        Generate grid tiles from a selected image tileset using given grid pixel size.
        """
        full_path = Path(ASSETS_DIR) / image_value
        full_img = self._load_full_image(full_path, image_value)
        if full_img is None:
            return
        thumb_size = (THUMB, THUMB)
        # Slice and load tileset assets
        self.assets = self._slice_tileset(full_img, image_value, grid_size, thumb_size)
        logger.debug(f" Loaded {len(self.assets)} tiles from {image_value} using grid size {grid_size}")
        # Refresh view to use new assets list
        self.view.assets = self.assets
        # Save individual tile images to disk
        self._save_tileset_slices(full_img, image_value, grid_size)

    def _load_full_image(self, full_path: Path, image_value: str):
        """
        Carga una imagen completa, retorna Surface o None.
        """
        try:
            return pygame.image.load(str(full_path))
        except Exception as e:
            logger.error(f" ERROR cargando tileset {full_path}: {e}")
        return None

    def _slice_tileset(self, full_img, image_value: str, grid_size: int, thumb_size):
        """
        Divide la imagen en tiles de tamaño grid_size y escala a thumb_size.
        """
        width, height = full_img.get_size()
        assets = []
        for y in range(0, height, grid_size):
            for x in range(0, width, grid_size):
                rect = pygame.Rect(x, y, grid_size, grid_size)
                sub_surf = full_img.subsurface(rect).copy()
                thumb = pygame.transform.scale(sub_surf, thumb_size)
                name = f"{Path(image_value).stem}_{x}_{y}"
                assets.append((name, thumb, False, (grid_size, grid_size)))
        return assets

    def _save_tileset_slices(self, full_img, image_value: str, grid_size: int):
        """Save each grid tile from a tileset image as a separate PNG file."""
        from pathlib import Path
        # Determine output directory next to original tileset
        base = Path(ASSETS_DIR) / Path(image_value).parent
        out_dir = base / f"{Path(image_value).stem}_slices"
        out_dir.mkdir(parents=True, exist_ok=True)
        width, height = full_img.get_size()
        count = 0
        for y in range(0, height, grid_size):
            for x in range(0, width, grid_size):
                rect = pygame.Rect(x, y, grid_size, grid_size)
                sub = full_img.subsurface(rect).copy()
                fname = f"{Path(image_value).stem}_{x}_{y}.png"
                pygame.image.save(sub, str(out_dir / fname))
                count += 1
        logger.debug(f" Saved {count} slices to {out_dir}")
        # Regenerate tiles.json mapping and update overlay config
        try:
            from scripts.generate_overlay_map import main as generate_overlay_map
            import json
            import roguelike_engine.config.config_tiles as ct
            generate_overlay_map()
            logger.debug(" Regenerated tiles.json mapping")
            with open(ct.TILES_MAP_PATH, "r", encoding="utf-8") as f:
                ct.OVERLAY_CODE_MAP = json.load(f)
            ct.INVERSE_OVERLAY_MAP.clear()
            for code, name in ct.OVERLAY_CODE_MAP.items():
                ct.INVERSE_OVERLAY_MAP.setdefault(name, []).append(code)
            logger.debug(" Updated overlay mapping in config_tiles")
        except Exception as e:
            logger.error(f" Error updating overlay mapping: {e}")

    def is_over(self, mouse_pos) -> bool:
        """
        Verifica si el mouse está sobre el picker.
        """
        if not self.picker_state.surface or not self.picker_state.pos:
            return False
        x0, y0 = self.picker_state.pos
        w, h = self.picker_state.surface.get_size()
        mx, my = mouse_pos
        return x0 <= mx <= x0 + w and y0 <= my <= y0 + h

    def drag(self, mouse_pos):
        """
        Arrastra el picker.
        """
        if self.picker_state.dragging:
            self.picker_state.pos = (
                mouse_pos[0] - self.picker_state.drag_offset[0],
                mouse_pos[1] - self.picker_state.drag_offset[1]
            )

    def stop_drag(self):
        """
        Detiene el arrastre del picker.
        """
        self.picker_state.dragging = False

    def scroll(self, dy):
        """
        Desplaza el scroll del picker.
        """
        # Actualiza el scroll del estado del picker (no el editor principal)
        self.picker_state.scroll_offset = max(0, self.picker_state.scroll_offset - dy * 30)

    def open(self):
        """
        Abre el selector de tiles: reinicia estado y scroll.
        """
        self.picker_state.open = True
        self.picker_state.current_choice = None
        self.picker_state.dragging = False
        # Reiniciar scroll en el editor
        self.picker_state.scroll_offset = 0
        # Recargar lista de assets
        self._load_assets()
        self._load_positions()
        # Invalida la caché de la cuadrícula estática para forzar reconstrucción con los nuevos assets
        if hasattr(self.view, 'assets_cache_surf'):
            del self.view.assets_cache_surf
        if hasattr(self.view, 'assets_cache_size'):
            del self.view.assets_cache_size

    def _close(self):
        """
        Cierra el selector de tiles.
        """
        self.picker_state.open = False
        self.picker_state.current_choice = None
        self.picker_state.dragging = False

    def _persist_overlay(self, tile, code: str, map):
        """
        Persiste el overlay del tile seleccionado.
        """
        # Calcular posición global del tile
        row = tile.y // TILE_SIZE
        col = tile.x // TILE_SIZE

        # Determinar zona según configuración
        zone_name = None
        zone_offset_x = zone_offset_y = 0
        for zn, (ox, oy) in global_map_settings.zone_offsets.items():
            if ox <= col < ox + global_map_settings.zone_width and oy <= row < oy + global_map_settings.zone_height:
                zone_name = zn
                zone_offset_x, zone_offset_y = ox, oy
                break
        if zone_name is None:
            zone_name = "no_zone"

        # Persistir JSON de capas de la zona
        layer = self.editor_state.current_layer
        zone_layers = load_layers(zone_name) or {}
        # Dimensiones de la zona
        if zone_name != 'no_zone':
            h,w = global_map_settings.zone_height, global_map_settings.zone_width
        else:
            h = len(map.tiles); w = len(map.tiles[0]) if map.tiles else 0
        # Asegurar grid por capa en la zona
        for l in Layer:
            zone_layers.setdefault(l, [["" for _ in range(w)] for _ in range(h)])
        # Índices locales
        if zone_name in global_map_settings.zone_offsets:
            local_row = row - zone_offset_y
            local_col = col - zone_offset_x
        else:
            local_row, local_col = row, col
        # Actualizar la capa seleccionada de la zona
        try:
            zone_layers[layer][local_row][local_col] = code
        except Exception:
            pass
        # Guardar sólo la zona
        save_layers(zone_name, zone_layers)

        # Actualizar in-memory de map.layers y map.tiles_by_layer
        if layer in map.layers:
            try:
                map.layers[layer][row][col] = code
            except Exception:
                pass
        grid = map.tiles_by_layer.get(layer)
        if grid and 0 <= row < len(grid) and 0 <= col < len(grid[0]):
            t = grid[row][col]
            if t:
                t.overlay_code = code
        map.view.invalidate_cache()
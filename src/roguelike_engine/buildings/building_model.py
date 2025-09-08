import os
import types
import pygame
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_engine.config.map_config import global_map_settings
from roguelike_engine.utils.loader import load_image
from typing import Dict, Tuple, Optional
import logging
logger = logging.getLogger(__name__)

# Shared services and types for buildings package
from roguelike_engine.buildings.services.zones import zone_offset
from roguelike_engine.buildings.services.types import (
    CollisionMap,
    VisualStateMap,
    StateThresholds,
    RectList,
    ColliderScope,
)
from roguelike_engine.buildings.services.collisions import (
    image_to_grid_size,
    resample_collision_map,
)

# Cache for building images: key = (image_path, scale)
_BUILDING_IMAGE_CACHE: Dict[Tuple[str, Optional[Tuple[int,int]]], pygame.Surface] = {}

class BuildingModel:
    """
    Modelo de datos para un edificio:
    • Coordenadas relativas en su zona (rel_x, rel_y).
    • Ruta de la imagen, propiedades físicas (solid, escala original, split).
    • Cálculos de colisión (tiles, rectángulo) y propiedades de tamaño.
    """

    def __init__(
        self,
        rel_x: int,
        rel_y: int,
        image_path: str,
        solid: bool = True,
        scale: tuple[int,int] | None = None,
        *,
        split_ratio: float = 0.5,
        z_bottom: int | None = None,
        z_top: int | None = None
    ):
        # ── Datos de posición relativa y zona (se asigna externamente) ──
        self.rel_x = rel_x
        self.rel_y = rel_y
        self.zone: str | None = None

        # ── Propiedades del edificio ──
        self.solid = solid
        self.image_path = image_path
        self.split_ratio = max(0.0, min(split_ratio, 1.0))

        # ── Soporte de múltiples imágenes por estado visual ──
        # images_by_state: { state_name -> image_path }
        self.images_by_state: dict[str, str] = {}
        # thresholds opcionales para mapear porcentaje de vida agregada -> estado
        # Formato sugerido: lista ordenada desc por min_ratio, p.ej.
        # [ {"state": "healthy", "min_ratio": 0.66}, {"state": "damaged", "min_ratio": 0.33}, {"state": "critical", "min_ratio": 0.0} ]
        self.state_thresholds: list[dict] | None = None
        # estado visual actual aplicado (si None, usa image_path base)
        self.current_visual_state: str | None = None

        # ── Caches internos, inicializados en la "lógica de carga" ──
        self.image: pygame.Surface | None = None
        self.original_scale: tuple[int,int] | None = None
        self._collision_map: list[list[str]] = []
        self._collision_tiles_cache: list[pygame.Rect] | None = None
        self._collision_tile_objs: list[types.SimpleNamespace] | None = None

        # Alcance de colisión por edificio: 'CG' (global) o 'CU' (único)
        self.collider_scope: ColliderScope = 'CG'

        # ── Z-layers por defecto (se pueden sobreescribir) ──
        from roguelike_engine.config.config_z_layer import Z_LAYERS
        self.z_bottom = z_bottom if z_bottom is not None else Z_LAYERS["building_low"]
        self.z_top    = z_top    if z_top    is not None else Z_LAYERS["building_high"]
        self.z = self.z_bottom  # compatibilidad temporal

        # ── Al final, llamamos a una rutina privada para cargar y escalar la imagen ──
        self._load_and_prepare_image(scale)

    def __repr__(self) -> str:
        name = os.path.basename(self.image_path)
        w, h = self.original_scale or (0,0)
        return (f"<BuildingModel '{name}' rel=({self.rel_x},{self.rel_y}) zone={self.zone!r} "
                f"size=({w}x{h}) split={self.split_ratio:.2f} "
                f"Zs=({self.z_bottom},{self.z_top}) solid={self.solid}>")

    # ---- Zona helpers delegados a services.zones ----

    # ───────────── Propiedades de posición absoluta ─────────────
    @property
    def x(self) -> int:
        ox, _ = zone_offset(self.zone, global_map_settings.zone_offsets)
        return ox * TILE_SIZE + self.rel_x

    @x.setter
    def x(self, value: int):
        ox, _ = zone_offset(self.zone, global_map_settings.zone_offsets, warn_context="x_set")
        px = int(value)
        self.rel_x = px - ox * TILE_SIZE

    @property
    def y(self) -> int:
        _, oy = zone_offset(self.zone, global_map_settings.zone_offsets)
        return oy * TILE_SIZE + self.rel_y

    @y.setter
    def y(self, value: int):
        _, oy = zone_offset(self.zone, global_map_settings.zone_offsets, warn_context="y_set")
        py = int(value)
        self.rel_y = py - oy * TILE_SIZE

    # ───────────── Lógica de carga y escalado inicial ─────────────
    def _load_and_prepare_image(self, scale: tuple[int,int] | None):
        """
        Carga la imagen usando pygame y la escalada inicial:
        • Si se proporciona 'scale', la aplica directamente.
        • Si la imagen es muy grande (>512×512), reduce a 1/4.
        • Guarda en self.image y self.original_scale.
        """
        # Use cache to avoid reloading and re-scaling
        key = (self.image_path, scale)
        if key in _BUILDING_IMAGE_CACHE:
            surf = _BUILDING_IMAGE_CACHE[key]
            self.original_scale = surf.get_size()
        else:
            raw = load_image(self.image_path)
            if scale:
                surf = pygame.transform.scale(raw, scale)
                self.original_scale = scale
            else:
                w, h = raw.get_size()
                if w > 512 or h > 512:
                    new_size = (w // 4, h // 4)
                    surf = pygame.transform.scale(raw, new_size)
                    self.original_scale = new_size
                else:
                    surf = raw
                    self.original_scale = (w, h)
            _BUILDING_IMAGE_CACHE[key] = surf
        self.image = surf
        # Después de cambiar el tamaño, recalcular el “corte” en píxeles:
        self._cut_world = int(self.image.get_height() * self.split_ratio)

    # ───────────── Métodos de redimensionamiento ─────────────
    def resize(self, new_width: int, new_height: int):
        """
        Redimensiona a new_width×new_height recargando la imagen desde disco:
        • Limpia caches relacionados con el renderizado.
        • Recalcula self._cut_world para el split.
        """
        surf = load_image(self.image_path)
        surf = pygame.transform.scale(surf, (new_width, new_height))
        self.image = surf
        # Importante: no sobrescribir original_scale aquí.
        # original_scale representa el tamaño inicial al cargar el edificio
        # y es el objetivo de reset_to_original_size().
        self._cut_world = int(new_height * self.split_ratio)
        # Ajustar el collision_map al nuevo tamaño de imagen (grid por TILE_SIZE)
        try:
            new_rows, new_cols = self._image_to_grid_size()
            self._resample_collision_map(new_rows, new_cols)
        except Exception:
            # Si algo falla, al menos garantizamos un mapa válido
            if not self._collision_map:
                self._collision_map = [["."]]
        # Invalidar caches de colisión y renderizado (se regenerarán cuando sea necesario)
        self._collision_tiles_cache = None
        self._collision_tile_objs = None

    def reset_to_original_size(self):
        """
        Restaura el tamaño original (self.original_scale) recargando la imagen.
        """
        if self.original_scale:
            w, h = self.original_scale
            self.resize(w, h)
        else:
            logger.warning("⚠️ No se encontró escala original para este edificio.")

    # ───────────── Estados visuales (multi-imagen) ─────────────
    def set_images_by_state(self, images_by_state: dict[str, str], initial_state: str | None = None):
        """
        Define el mapeo de estados visuales a rutas de imagen.
        Si initial_state está presente y existe en el mapeo, aplica ese estado.
        Mantiene el tamaño original al cambiar.
        """
        try:
            self.images_by_state = dict(images_by_state or {})
        except Exception:
            self.images_by_state = {}
        if initial_state and initial_state in self.images_by_state:
            self.set_visual_state(initial_state)

    def set_state_thresholds(self, thresholds: list[dict] | None):
        """
        Establece los umbrales opcionales para convertir un ratio [0..1] a nombre de estado.
        Espera una lista de dicts con llaves {"state": str, "min_ratio": float} ordenada desc.
        """
        try:
            if isinstance(thresholds, list):
                self.state_thresholds = [dict(t) for t in thresholds]
            else:
                self.state_thresholds = None
        except Exception:
            self.state_thresholds = None

    def _apply_image_path(self, new_image_path: str):
        """
        Cambia la imagen del modelo manteniendo la escala original si existe.
        Invalida caches de colisión para forzar recálculo cuando corresponda.
        """
        try:
            self.image_path = new_image_path
            # Mantener tamaño previo si estaba definido
            target_scale = self.original_scale
            self._load_and_prepare_image(target_scale)
            # Invalidate collision caches since geometry may map differently post-scale
            self._collision_tiles_cache = None
            self._collision_tile_objs = None
        except Exception as ex:
            logger.warning(f"[BuildingModel] No se pudo aplicar nueva imagen '{new_image_path}': {ex}")

    def set_visual_state(self, state: str) -> bool:
        """
        Cambia el estado visual a 'state' si existe en images_by_state.
        Retorna True si se aplicó un cambio de imagen.
        """
        if not isinstance(state, str) or not self.images_by_state:
            return False
        path = self.images_by_state.get(state)
        if not path:
            return False
        if self.current_visual_state == state and self.image is not None:
            return False
        self.current_visual_state = state
        self._apply_image_path(path)
        # Recalcular corte vertical según split_ratio y nueva imagen
        if self.image is not None:
            self._cut_world = int(self.image.get_height() * self.split_ratio)
        return True

    # ───────────── Colisiones (collision_map + collision_tiles) ─────────────
    @property
    def collision_rect(self) -> pygame.Rect:
        """
        Retorna el rectángulo de colisión real, que corresponde a la parte inferior
        del edificio (después de aplicar el split en self._cut_world).
        """
        full_h = self.image.get_height()
        cut_h = self._cut_world
        return pygame.Rect(
            self.x,
            self.y + cut_h,
            self.image.get_width(),
            full_h - cut_h
        )

    @property
    def collision_tiles(self) -> list[pygame.Rect]:
        """
        Construye, si no existe, la lista de rectángulos de colisión por cada celda '#'
        de self._collision_map. Crea también objetos envoltorio con flag 'solid'.
        """
        if self._collision_tiles_cache is None:
            cache: list[pygame.Rect] = []
            for row_idx, row in enumerate(self._collision_map):
                for col_idx, cell in enumerate(row):
                    if cell == "#":
                        x = self.x + col_idx * TILE_SIZE
                        y = self.y + row_idx * TILE_SIZE
                        cache.append(pygame.Rect(x, y, TILE_SIZE, TILE_SIZE))
            self._collision_tiles_cache = cache
            # También creamos una lista de SimpleNamespace para quien necesite .solid y .rect
            self._collision_tile_objs = [
                types.SimpleNamespace(solid=True, rect=rect)
                for rect in cache
            ]
        return self._collision_tiles_cache

    @property
    def collision_tile_objs(self) -> list[types.SimpleNamespace]:
        """
        Acceso rápido a los objetos de colisión (con atributos .solid y .rect).
        """
        # Asegurarnos de que collision_tiles ya haya sido calculado
        _ = self.collision_tiles
        return self._collision_tile_objs or []

    # ───────────── Utilidades de grid para collision_map ─────────────
    def _image_to_grid_size(self) -> tuple[int, int]:
        """
        Devuelve (rows, cols) de la grilla de colisión a partir del tamaño de la imagen.
        Garantiza al menos 1×1 celdas para permitir edición incluso en tamaños pequeños.
        """
        return image_to_grid_size(self.image, TILE_SIZE)

    def _resample_collision_map(self, new_rows: int, new_cols: int):
        """
        Redimensiona self._collision_map a (new_rows×new_cols) usando remuestreo
        nearest-neighbor para preservar lo existente al escalar.
        Si el mapa actual es vacío, inicializa con '.' (caminable).
        """
        self._collision_map = resample_collision_map(self._collision_map, new_rows, new_cols)

    # ───────────── Acceso al mapa de colisión en bruto ─────────────
    @property
    def collision_map(self) -> CollisionMap:
        """
        El collision_map se debe cargar por fuera (p. ej. desde JSON) antes de utilizarlo.
        Aquí sólo devolvemos la referencia. No se modifica internamente en este modelo.
        """
        return self._collision_map

    @collision_map.setter
    def collision_map(self, data: CollisionMap):
        """
        Setter que invalida el cache de collision_tiles cuando cambia el mapa.
        """
        self._collision_map = data
        self._collision_tiles_cache = None
        self._collision_tile_objs = None

    # ───────────── Invalidation helpers ─────────────
    def invalidate_collision_caches(self) -> None:
        """Invalida caches derivados de collision_map (rects y objetos)."""
        self._collision_tiles_cache = None
        self._collision_tile_objs = None

    # Support pickling BuildingModel: omit surfaces and reconstruct on unpickle
    def __getstate__(self):
        return {
            'rel_x': self.rel_x,
            'rel_y': self.rel_y,
            'zone': self.zone,
            'solid': self.solid,
            'image_path': self.image_path,
            'split_ratio': self.split_ratio,
            'z_bottom': self.z_bottom,
            'z_top': self.z_top,
            'collision_map': self._collision_map,
            'original_scale': self.original_scale,
            'collider_scope': self.collider_scope,
            'images_by_state': self.images_by_state,
            'state_thresholds': self.state_thresholds,
            'current_visual_state': self.current_visual_state,
        }

    def __setstate__(self, state):
        self.rel_x = state['rel_x']
        self.rel_y = state['rel_y']
        self.zone = state.get('zone', None)
        self.solid = state['solid']
        self.image_path = state['image_path']
        self.split_ratio = state['split_ratio']
        self.z_bottom = state['z_bottom']
        self.z_top = state['z_top']
        self.z = self.z_bottom
        self._collision_map = state['collision_map']
        self._collision_tiles_cache = None
        self._collision_tile_objs = None
        self.original_scale = state.get('original_scale')
        # Restaurar alcance de colisión por edificio
        self.collider_scope = state.get('collider_scope', 'CG')
        # Multi-state visual support
        self.images_by_state = state.get('images_by_state', {}) or {}
        self.state_thresholds = state.get('state_thresholds')
        self.current_visual_state = state.get('current_visual_state')
        # Reload image using cached loader
        self._load_and_prepare_image(self.original_scale)
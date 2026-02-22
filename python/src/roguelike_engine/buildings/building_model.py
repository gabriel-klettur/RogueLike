import os
import pygame
from roguelike_engine.config.map_config import global_map_settings
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_engine.utils.loader import load_image
import logging
logger = logging.getLogger(__name__)

# Shared services and types for buildings package
from roguelike_engine.buildings.services.zones import zone_offset
from roguelike_engine.buildings.services.types import (
    ColliderScope,
)

# Ensure base zone keys exist in offsets at import time to avoid KeyError in tests
def _ensure_base_zones_in_offsets() -> None:
    try:
        offsets = global_map_settings.zone_offsets
        # Ensure sentinel names exist
        offsets.setdefault("no zone", (0, 0))
        offsets.setdefault("no-zone", (0, 0))
        # Do NOT inject 'lobby'/'dungeon' when using zones.json (multi-world mode)
        # This allows worlds with an empty zones.json to remain truly empty.
        use_json = True
        try:
            use_json = bool(getattr(global_map_settings, 'use_zones_json', True))
        except Exception:
            use_json = True
        if not use_json:
            # If base zones are missing, derive from dynamic layout and add aliases
            if "lobby" not in offsets or "dungeon" not in offsets:
                try:
                    dyn = global_map_settings._dynamic_offsets()
                    if "lobby" not in offsets and "lobby" in dyn:
                        offsets["lobby"] = dyn["lobby"]
                    if "dungeon" not in offsets and "dungeon" in dyn:
                        offsets["dungeon"] = dyn["dungeon"]
                except Exception:
                    # Minimal fallback if dynamic computation fails
                    try:
                        lob = global_map_settings.lobby_offset
                        dun = global_map_settings.calculate_dungeon_offset(lob)
                        offsets.setdefault("lobby", lob)
                        offsets.setdefault("dungeon", dun)
                    except Exception:
                        pass
    except Exception:
        # Best-effort guard; do not raise on import
        pass

_ensure_base_zones_in_offsets()

# Utils split out to keep this module lean
from roguelike_engine.buildings.model_utils.image_ops import (
    load_and_prepare_image as _mu_load_and_prepare_image,
)
from roguelike_engine.buildings.model_utils.pickling_ops import (
    model_getstate as _mu_model_getstate,
    model_setstate as _mu_model_setstate,
)
from roguelike_engine.buildings.model_mixins import BuildingCollisionMixin

class BuildingModel(BuildingCollisionMixin):
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
        # Cached collision mask of the full image (alpha-based)
        self._mask_full: pygame.Mask | None = None

        # Alcance de colisión por edificio: 'CG' (global) o 'CU' (único)
        self.collider_scope: ColliderScope = 'CG'

        # --- Flash (damage tint) runtime state ---
        self._flash_until_ts: float = 0.0
        self._flash_color: tuple[int, int, int] = (255, 255, 255)
        self._flash_blink_interval: float = 0.05

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
        # Delegate to model_utils image loader (with internal cache)
        surf, applied_size = _mu_load_and_prepare_image(
            self.image_path, scale, loader=load_image
        )
        self.original_scale = applied_size
        self.image = surf
        # Después de cambiar el tamaño, recalcular el “corte” en píxeles:
        self._cut_world = int(self.image.get_height() * self.split_ratio)
        # Invalidate collision mask cache (image changed)
        self._mask_full = None

    # ───────────── Métodos de redimensionamiento ─────────────
    def resize(self, new_width: int, new_height: int, *, resample_collision: bool = True):
        """
        Redimensiona a new_width×new_height. Si el tamaño es distinto, recarga y escala
        la imagen desde disco; si es igual, evita recargar. Controla el remuestreo de la
        grilla de colisiones vía `resample_collision`.
        """
        cur_w = int(self.image.get_width()) if self.image is not None else None
        cur_h = int(self.image.get_height()) if self.image is not None else None
        if cur_w != new_width or cur_h != new_height:
            surf = load_image(self.image_path)
            surf = pygame.transform.scale(surf, (new_width, new_height))
            self.image = surf
        # Importante: no sobrescribir original_scale aquí.
        self._cut_world = int(new_height * self.split_ratio)
        # Remuestrear sólo si así se solicita
        if resample_collision:
            try:
                new_rows, new_cols = self._image_to_grid_size()
                self._resample_collision_map(new_rows, new_cols)
            except Exception:
                if not self._collision_map:
                    self._collision_map = [["."]]
        # Invalidar caches derivados
        self._collision_tiles_cache = None
        self._collision_tile_objs = None
        self._mask_full = None

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
            # Invalidate image mask cache
            self._mask_full = None
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

    # ----------------------------- Flash (damage tint) API -----------------------------
    def trigger_flash(self, color: tuple[int, int, int] = (255, 255, 255), duration: float = 0.08, *, blink_interval: float | None = None) -> None:
        """Start a temporary flash effect with the given color and duration.
        The actual tint is applied by BuildingView at render time; this only stores timing.
        """
        try:
            import time as _t
            self._flash_color = (int(color[0]), int(color[1]), int(color[2]))
            self._flash_until_ts = float(_t.time()) + max(0.0, float(duration))
            if blink_interval is not None:
                bi = max(0.0, float(blink_interval))
                self._flash_blink_interval = bi if bi > 0.0 else self._flash_blink_interval
        except Exception:
            # Best-effort; ignore failures
            pass
    # Support pickling BuildingModel: omit surfaces and reconstruct on unpickle
    def __getstate__(self):
        return _mu_model_getstate(self)

    def __setstate__(self, state):
        _mu_model_setstate(self, state)
        # Reload image using cached loader
        self._load_and_prepare_image(self.original_scale)
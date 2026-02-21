from dataclasses import dataclass, field
from pathlib import Path
from typing import Dict, Tuple, Union, Literal
from collections.abc import Mapping
import json
import logging
from functools import cached_property
from collections import deque

from roguelike_engine.config.config import DATA_DIR

logger = logging.getLogger(__name__)

# Dict wrapper that can synthesize base/sentinel zone keys on demand
class _OffsetsDict(dict):
    """A dict that auto-fills missing base/sentinel zone keys.

    Guarantees that direct indexing like offsets['lobby'] or offsets['no zone']
    never raises KeyError by computing reasonable defaults using MapSettings.
    """
    def __init__(self, ms: "MapSettings", *args, **kwargs):
        super().__init__(*args, **kwargs)
        self._ms = ms

    def __missing__(self, key):
        try:
            k = str(key)
        except Exception:
            k = key
        low = k.lower() if isinstance(k, str) else k
        # Sentinels map to (0,0)
        if isinstance(low, str) and low in ("no zone", "no-zone"):
            val = (0, 0)
            self[k] = val
            return val
        # Base zones: compute from dynamic layout or fallbacks
        if isinstance(low, str) and low == "lobby":
            try:
                dyn = self._ms._dynamic_offsets()
                val = dyn.get("lobby", (0, 0))
            except Exception:
                try:
                    val = self._ms.lobby_offset
                except Exception:
                    val = (0, 0)
            self["lobby"] = val
            return val
        if isinstance(low, str) and low == "dungeon":
            try:
                dyn = self._ms._dynamic_offsets()
                val = dyn.get("dungeon", (0, 0))
            except Exception:
                try:
                    lob = self._ms.lobby_offset
                    val = self._ms.calculate_dungeon_offset(lob)
                except Exception:
                    val = (0, 0)
            self["dungeon"] = val
            return val
        # Case-insensitive aliasing for any existing key
        if isinstance(k, str):
            for ex in list(self.keys()):
                if isinstance(ex, str) and ex.lower() == low:
                    return self[ex]
        raise KeyError(key)


@dataclass
class MapSettings:
    """
    Configuración central para generación y carga de mapas.
    """
    # Flag para decidir tipo de carga de offsets: JSON o dinámico
    use_zones_json: bool = True         #! Mas adelante deberiamos trabajar sobre el offset no dinamico.

    # Auto-ajuste de límites: expande global_width/global_height si es necesario
    auto_expand: bool = True

    # Modo avanzado: permitir offsets lógicos negativos sin recentrado interno
    # (el runtime actual sigue usando índices 0-based; este flag se utilizará
    # en fases posteriores para cambiar cómo interpretamos zone_offsets).
    use_negative_offsets: bool = False

    # Tamaño total del mapa (en tiles)
    global_width: int = 150
    global_height: int = 150
    # Origen lógico del mundo en coordenadas de tile (puede ser negativo)
    world_origin_x: int = 0
    world_origin_y: int = 0

    # Tamaño de cada zona (en tiles)
    zone_width: int = 50
    zone_height: int = 50

    # Configuración de mazmorra
    dungeon_connect_side: Literal['bottom', 'top', 'left', 'right'] = 'bottom'
    dungeon_tunnel_thickness: int = 3
    dungeon_max_rooms: Union[int, Literal['MAX'], None] = 10

    # Zonas dinámicas: nombre -> (zona padre, lado de conexión)
    additional_zones: Dict[str, Tuple[str, Literal['bottom', 'top', 'left', 'right']]] = field(default_factory=lambda: {              
        'empty_left': ('lobby', 'left'),  # Zona vacía a la izquierda del lobby
    })



    # Directorio para mapas de debug generados automáticamente
    debug_maps_dir: Path = field(default_factory=lambda:
        Path(__file__).resolve().parent.parent.parent / 'data' / 'debug_maps'
    )

    # Mundo activo y carpeta raíz de mundos
    current_world: str = "base"
    worlds_dir: Path = field(default_factory=lambda:
        Path(DATA_DIR) / 'worlds'
    )

    def __setattr__(self, name, value):
        if name == "zone_offsets" and not isinstance(value, _OffsetsDict):
            if isinstance(value, Mapping):
                value = _OffsetsDict(self, dict(value))
            elif isinstance(value, dict):
                value = _OffsetsDict(self, value)
        object.__setattr__(self, name, value)

    def __getattribute__(self, name):
        val = object.__getattribute__(self, name)
        if name == "zone_offsets" and not isinstance(val, _OffsetsDict):
            if isinstance(val, Mapping):
                wrapped = _OffsetsDict(self, dict(val))
            elif isinstance(val, dict):
                wrapped = _OffsetsDict(self, val)
            else:
                return val
            object.__getattribute__(self, "__dict__")["zone_offsets"] = wrapped
            return wrapped
        return val

    @property
    def zone_size(self) -> Tuple[int, int]:
        """Dimensiones de cada zona en tiles."""
        return (self.zone_width, self.zone_height)

    # --- Helpers de conversión entre coordenadas lógicas e internas -------

    def logical_to_internal_tile(self, tx: int, ty: int) -> Tuple[int, int]:
        """Convierte (tx, ty) lógicos a índices internos de matriz.

        En el diseño con offsets negativos, las coordenadas lógicas pueden ser
        negativas. Internamente, la matriz del mundo sigue siendo 0-based;
        usamos world_origin_x/world_origin_y como traslación.

        En el runtime actual (solo offsets no negativos), world_origin_x/y
        serán típicamente 0 y esta conversión es la identidad.
        """
        ox0 = getattr(self, "world_origin_x", 0)
        oy0 = getattr(self, "world_origin_y", 0)
        return tx - ox0, ty - oy0

    def internal_to_logical_tile(self, ix: int, iy: int) -> Tuple[int, int]:
        """Convierte índices internos (ix, iy) a coordenadas lógicas.

        Inversa de logical_to_internal_tile. Útil para exponer coordenadas
        de tiles en el espacio lógico (por ejemplo, para depuración o
        herramientas de edición que necesitan trabajar con offsets negativos).
        """
        ox0 = getattr(self, "world_origin_x", 0)
        oy0 = getattr(self, "world_origin_y", 0)
        return ix + ox0, iy + oy0

    @cached_property
    def zone_offsets(self) -> Dict[str, Tuple[int, int]]:
        """
        Offsets de cada zona en tiles.
        Si use_zones_json es True, lee data/map/zones/zones.json;
        de lo contrario, calcula dinámicamente lobby y dungeon.
        """
        # Si no usamos JSON, fallback inmediato
        if not self.use_zones_json:
            return _OffsetsDict(self, self._dynamic_offsets())

        # Intentar cargar offsets desde JSON
        try:
            content = self.ZONES_INDEX.read_text(encoding='utf-8')
            data = json.loads(content)
            # Validar formato: cada offset es una secuencia de dos ints
            json_offsets = {zone: (int(offset[0]), int(offset[1])) for zone, offset in data.items()}
            # Normalizar nombres base: 'Lobby'/'lObBy' -> 'lobby', idem 'dungeon'
            offsets: Dict[str, Tuple[int, int]] = {}
            for name, off in json_offsets.items():
                low = name.lower()
                if low == 'lobby':
                    offsets['lobby'] = off
                elif low == 'dungeon':
                    offsets['dungeon'] = off
                else:
                    offsets[name] = off
            # Caso especial: zones.json vacío => no auto-inyectar lobby/dungeon
            if not offsets:
                # Solo sentinelas; no expand/validate
                offsets.setdefault('no zone', (0, 0))
                offsets.setdefault('no-zone', (0, 0))
                return _OffsetsDict(self, offsets)
            # Asegurar zonas base si faltan (solo si hay algo definido por el usuario)
            if 'lobby' not in offsets:
                lobby_off = self.lobby_offset
                offsets['lobby'] = lobby_off
            if 'dungeon' not in offsets:
                offsets['dungeon'] = self.calculate_dungeon_offset(offsets['lobby'])
            # Colapsar entradas que compartan el mismo offset lógico en una sola zona.
            # Esto evita que clones como "zone_150_0" y "zone_150_0_1" se dibujen
            # superpuestos y asegura una vista coherente en todo el runtime.
            offsets = self._dedupe_zone_offsets(offsets)
            # No inyectar zonas adicionales dinámicas aquí; dejamos que zones.json las defina
            # Ajustar límites del world para incluir todas las zonas definidas
            if self.auto_expand:
                self.global_width, self.global_height, offsets = self.expand_limits(offsets)
            else:
                self.validate_limits(offsets)
            # Inject sentinel after limits are finalized so it doesn't affect expansion/validation
            offsets.setdefault('no zone', (0, 0))
            offsets.setdefault('no-zone', (0, 0))
            return _OffsetsDict(self, offsets)
        except Exception:
            # En caso de fallo, usar dinámico
            return _OffsetsDict(self, self._dynamic_offsets())

    @property
    def zone_offsets_internal(self) -> Dict[str, Tuple[int, int]]:
        """Offsets de zona en coordenadas internas (índices 0-based).

        Por ahora, equivalen a ``zone_offsets`` ya que el runtime sigue
        trabajando con offsets no negativos tras el auto_expand. Cuando
        ``use_negative_offsets`` esté activo y cambiemos la semántica de
        ``zone_offsets`` a coordenadas lógicas (potencialmente negativas),
        esta propiedad será el punto único de conversión a índices internos.
        """
        try:
            offsets = dict(self.zone_offsets)  # type: ignore[arg-type]
        except Exception:
            offsets = {}
        return _OffsetsDict(self, offsets)

    @property
    def logical_zone_offsets(self) -> Dict[str, Tuple[int, int]]:
        """Offsets lógicos de zonas en tiles antes del recentrado por auto_expand.

        Se calculan combinando ``zone_offsets`` (ya normalizados a coordenadas
        no negativas) con ``world_origin_x/world_origin_y`` registrados en
        ``expand_limits``. Las zonas centinela ("no zone", "no-zone") se
        mantienen siempre en (0, 0) para preservar su semántica.
        """
        try:
            offsets = dict(self.zone_offsets)  # type: ignore[arg-type]
        except Exception:
            offsets = {}
        logical: Dict[str, Tuple[int, int]] = {}
        for name, (ox, oy) in offsets.items():
            try:
                low = str(name).lower()
            except Exception:
                low = name
            if isinstance(low, str) and low in ("no zone", "no-zone"):
                logical[name] = (0, 0)
            else:
                logical[name] = self.internal_to_logical_tile(ox, oy)
        return logical

    def _is_auto_zone_name(self, name: object) -> bool:
        """Heurística para detectar nombres de zona auto-generados.

        Considera automáticos los que siguen el patrón aproximado
        ``zone_<x>_<y>[_idx]``, donde x, y e idx son enteros (x/y pueden ser negativos).
        """
        try:
            s = str(name)
        except Exception:
            return False
        if not s.startswith("zone_"):
            return False
        tail = s[len("zone_"):]
        parts = tail.split("_")
        if not (2 <= len(parts) <= 3):
            return False
        for part in parts:
            # Permitir signo negativo en componentes de coordenadas
            if not part or part == "-":
                return False
            if not part.lstrip("-").isdigit():
                return False
        return True

    def _dedupe_zone_offsets(self, offsets: Dict[str, Tuple[int, int]]) -> Dict[str, Tuple[int, int]]:
        """Colapsa entradas que comparten el mismo offset lógico en una sola zona.

        Estrategia:
          - Para cada par (x, y), se agrupan todos los nombres asociados.
          - Si solo hay uno, se conserva sin cambios.
          - Si hay varios:
              * Se priorizan nombres "no automáticos" (no generados tipo zone_X_Y[_N]).
              * Si todos son automáticos, se prioriza el que no tiene sufijo numérico;
                en último término, se usa el alfabéticamente primero.
          - Las zonas descartadas se ignoran en runtime y se registran por log.
        """
        if not offsets:
            return offsets

        by_coord: Dict[Tuple[int, int], list[str]] = {}
        for name, off in offsets.items():
            by_coord.setdefault(off, []).append(name)

        changed = False
        result: Dict[str, Tuple[int, int]] = {}
        for off, names in by_coord.items():
            if len(names) == 1:
                result[names[0]] = off
                continue

            changed = True
            # Preferir nombres no automáticos (renombrados por el usuario, p.ej. "Forest")
            non_auto = [n for n in names if not self._is_auto_zone_name(n)]
            if non_auto:
                chosen = sorted(non_auto)[0]
            else:
                # Todos automáticos: preferir los que no terminan en sufijo numérico (_1, _2, ...)
                no_suffix = [n for n in names if not str(n).split("_")[-1].lstrip("-").isdigit()]
                if no_suffix:
                    chosen = sorted(no_suffix)[0]
                else:
                    chosen = sorted(names)[0]

            result[chosen] = off
            dropped = [n for n in names if n != chosen]
            if dropped:
                try:
                    logger.warning(
                        "[MapSettings] Duplicate zones at offset (%s,%s): keeping '%s', ignoring %s",
                        off[0], off[1], chosen, dropped,
                    )
                except Exception:
                    # Fallback silencioso si el logging falla por cualquier motivo
                    pass

        return result if changed else offsets

    # Rutas dependientes del mundo activo
    @property
    def ZONES_INDEX(self) -> Path:
        """Ruta al índice de zonas del mundo activo."""
        override = self.__dict__.get('_zones_index_override')
        if override is not None:
            return override
        return (self.worlds_dir / self.current_world / 'zones' / 'zones.json')

    @ZONES_INDEX.setter
    def ZONES_INDEX(self, value: Union[str, Path, None]) -> None:
        if value is None:
            self.__dict__.pop('_zones_index_override', None)
        else:
            try:
                self.__dict__['_zones_index_override'] = Path(value)
            except Exception:
                self.__dict__['_zones_index_override'] = value
        self.refresh_zone_offsets()

    @property
    def overlays_dir(self) -> Path:
        """Directorio de overlays por zona del mundo activo."""
        override = self.__dict__.get('_zones_index_override')
        if override is not None:
            return override.parent / 'overlays'
        return (self.worlds_dir / self.current_world / 'zones' / 'overlays')

    @property
    def collisions_dir(self) -> Path:
        """Directorio de colisiones por zona del mundo activo."""
        override = self.__dict__.get('_zones_index_override')
        if override is not None:
            return override.parent.parent / 'collisions'
        return (self.worlds_dir / self.current_world / 'collisions')

    @property
    def buildings_dir(self) -> Path:
        """Directorio de persistencia de edificios del mundo activo."""
        return (self.worlds_dir / self.current_world / 'buildings')

    def _dynamic_offsets(self) -> Dict[str, Tuple[int, int]]:
        """
        Calcula offsets por defecto: lobby centrado, dungeon adyacente y zonas dinámicas adicionales.
        """
        lobby_off = self.lobby_offset
        offsets: Dict[str, Tuple[int, int]] = {}
        # Lobby siempre al centro
        offsets['lobby'] = lobby_off
        # Dungeon por defecto
        offsets['dungeon'] = self.calculate_dungeon_offset(lobby_off)
        # Resolver offsets de zonas dinámicas usando BFS basado en parent->children
        # Construir mapa de dependencias
        children: Dict[str, list[Tuple[str, Literal['bottom','top','left','right']]]] = {}
        for zone, (parent, side) in self.additional_zones.items():
            children.setdefault(parent, []).append((zone, side))
        # Inicializar cola con padres que existen en offsets
        queue = deque([p for p in children if p in offsets])
        # Recorrer árbol de zonas
        while queue:
            parent = queue.popleft()
            for (zone, side) in children.get(parent, []):
                offsets[zone] = self.calculate_offset(offsets[parent], side)
                queue.append(zone)
        # Verificar que todas las zonas fueron procesadas
        missing = [z for z in self.additional_zones if z not in offsets]
        if missing:
            raise KeyError(f"Dependencias no satisfechas para zonas: {missing}")
        # Asegurar que distintas claves no colapsan en el mismo offset
        # (p.ej., configuraciones adicionales mal definidas).
        offsets = self._dedupe_zone_offsets(offsets)
        if self.auto_expand:
            self.global_width, self.global_height, offsets = self.expand_limits(offsets)
        else:
            self.validate_limits(offsets)
        # Inject sentinel after limits are finalized so it doesn't affect expansion/validation
        offsets.setdefault('no zone', (0, 0))
        offsets.setdefault('no-zone', (0, 0))
        return offsets

    @property
    def lobby_offset(self) -> Tuple[int, int]:
        """
        Offset (x, y) para centrar la zona "lobby" en el mapa global.
        """
        n_cols = self.global_width // self.zone_width
        n_rows = self.global_height // self.zone_height
        if n_cols < 1 or n_rows < 1:
            return (
                (self.global_width - self.zone_width) // 2,
                (self.global_height - self.zone_height) // 2
            )
        center_col = n_cols // 2
        center_row = n_rows // 2
        rem_x = self.global_width - n_cols * self.zone_width
        rem_y = self.global_height - n_rows * self.zone_height
        start_x = rem_x // 2
        start_y = rem_y // 2
        return (
            start_x + center_col * self.zone_width,
            start_y + center_row * self.zone_height
        )

    def calculate_dungeon_offset(
        self,
        lobby_off: Tuple[int, int]
    ) -> Tuple[int, int]:
        """
        Offset (x, y) para colocar la mazmorra adyacente a la zona "lobby"
        según dungeon_connect_side.
        """
        off_x, off_y = lobby_off
        side = self.dungeon_connect_side
        if side == 'bottom':
            return off_x, off_y + self.zone_height
        if side == 'top':
            return off_x, off_y - self.zone_height
        if side == 'left':
            return off_x - self.zone_width, off_y
        return off_x + self.zone_width, off_y

    def calculate_offset(self, base_off: Tuple[int, int], side: Literal['bottom', 'top', 'left', 'right']) -> Tuple[int, int]:
        """
        Calcula offset desde base_off según el lado especificado.
        """
        off_x, off_y = base_off
        if side == 'bottom':
            return off_x, off_y + self.zone_height
        if side == 'top':
            return off_x, off_y - self.zone_height
        if side == 'left':
            return off_x - self.zone_width, off_y
        if side == 'right':
            return off_x + self.zone_width, off_y
        raise ValueError(f"Lado desconocido: {side}")

    # Validación y auto-expansión de límites del mapa
    def validate_limits(self, offsets: Dict[str, Tuple[int, int]]) -> None:
        """Lanza ValueError si alguna zona excede los límites globales."""
        for name, (ox, oy) in offsets.items():
            if ox < 0 or oy < 0 or ox + self.zone_width > self.global_width or oy + self.zone_height > self.global_height:
                raise ValueError(
                    f"Zona '{name}' fuera de límites: offset=({ox},{oy}), "
                    f"mapa=({self.global_width},{self.global_height}), "
                    f"zona=({self.zone_width},{self.zone_height})"
                )

    def expand_limits(self, offsets: Dict[str, Tuple[int, int]]) -> Tuple[int, int, Dict[str, Tuple[int, int]]]:
        """Ajusta dimensiones y corrige offsets para incluir todas las zonas.

        Conserva el comportamiento previo (desplazar offsets a coordenadas
        no negativas) pero además registra el mínimo offset lógico original en
        ``world_origin_x/world_origin_y`` para usos futuros.
        """
        if not offsets:
            return self.global_width, self.global_height, offsets

        xs = [ox for ox, _ in offsets.values()] + [ox + self.zone_width for ox, _ in offsets.values()]
        ys = [oy for _, oy in offsets.values()] + [oy + self.zone_height for _, oy in offsets.values()]
        min_x, max_x = min(xs), max(xs)
        min_y, max_y = min(ys), max(ys)

        # Registrar el origen lógico previo al desplazamiento como el mínimo
        # entre el valor real y 0. Así, cuando todos los offsets son
        # no negativos, world_origin_* = 0 y la conversión interna↔lógica es
        # la identidad; si hay valores negativos, world_origin_* coincide con
        # el mínimo lógico y se convierte en el desplazamiento de referencia.
        self.world_origin_x = min(min_x, 0)
        self.world_origin_y = min(min_y, 0)

        dx = -self.world_origin_x
        dy = -self.world_origin_y
        new_w = max(self.global_width, max_x) + dx
        new_h = max(self.global_height, max_y) + dy
        new_offsets = {n: (ox + dx, oy + dy) for n, (ox, oy) in offsets.items()}
        return new_w, new_h, new_offsets

    def refresh_zone_offsets(self) -> None:
        try:
            self.__dict__.pop('zone_offsets', None)
        except Exception:
            pass

    def is_blank_world(self) -> bool:
        try:
            z = self.ZONES_INDEX
            if not z.exists():
                return True
            txt = z.read_text(encoding='utf-8').strip()
            if not txt:
                return True
            data = json.loads(txt)
            if isinstance(data, dict):
                user_keys = [k for k in data.keys() if str(k).lower() not in ('no zone', 'no-zone')]
                return len(user_keys) == 0
            return True
        except Exception:
            return True

    # Cambio de mundo (API pública)
    def set_world(self, world_id: str) -> None:
        try:
            self.current_world = str(world_id or self.current_world)
        except Exception:
            self.current_world = world_id
        # Invalidate cached offsets and any dependent properties
        self.refresh_zone_offsets()
        # Clear any ZONES_INDEX override to avoid leaking paths across worlds in tests
        try:
            self.__dict__.pop('_zones_index_override', None)
        except Exception:
            pass

# Instancia global para uso en toda la aplicación
global_map_settings = MapSettings()
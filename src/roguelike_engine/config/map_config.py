from dataclasses import dataclass, field
from pathlib import Path
from typing import Dict, Tuple, Union, Literal
from collections.abc import Mapping
import json
from functools import cached_property
from collections import deque

from roguelike_engine.config.config import DATA_DIR

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

    # Tamaño total del mapa (en tiles)
    global_width: int = 150
    global_height: int = 150

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
            # Asegurar zonas base si faltan
            if 'lobby' not in offsets:
                lobby_off = self.lobby_offset
                offsets['lobby'] = lobby_off
            if 'dungeon' not in offsets:
                offsets['dungeon'] = self.calculate_dungeon_offset(offsets['lobby'])
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

    # Rutas dependientes del mundo activo
    @property
    def ZONES_INDEX(self) -> Path:
        """Ruta al índice de zonas del mundo activo."""
        return (self.worlds_dir / self.current_world / 'zones' / 'zones.json')

    @property
    def overlays_dir(self) -> Path:
        """Directorio de overlays por zona del mundo activo."""
        return (self.worlds_dir / self.current_world / 'zones' / 'overlays')

    @property
    def collisions_dir(self) -> Path:
        """Directorio de colisiones por zona del mundo activo."""
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
        """Ajusta dimensiones y corrige offsets para incluir todas las zonas."""
        xs = [ox for ox, _ in offsets.values()] + [ox + self.zone_width for ox, _ in offsets.values()]
        ys = [oy for _, oy in offsets.values()] + [oy + self.zone_height for _, oy in offsets.values()]
        min_x, max_x = min(xs), max(xs)
        min_y, max_y = min(ys), max(ys)
        dx = -min(min_x, 0)
        dy = -min(min_y, 0)
        new_w = max(self.global_width, max_x) + dx
        new_h = max(self.global_height, max_y) + dy
        new_offsets = {n: (ox + dx, oy + dy) for n, (ox, oy) in offsets.items()}
        return new_w, new_h, new_offsets

    def refresh_zone_offsets(self) -> None:
        try:
            self.__dict__.pop('zone_offsets', None)
        except Exception:
            pass

    # Cambio de mundo (API pública)
    def set_world(self, world_id: str) -> None:
        try:
            self.current_world = str(world_id or self.current_world)
        except Exception:
            self.current_world = world_id
        # Invalidate cached offsets and any dependent properties
        self.refresh_zone_offsets()

# Instancia global para uso en toda la aplicación
global_map_settings = MapSettings()
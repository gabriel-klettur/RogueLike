from dataclasses import dataclass, field
from pathlib import Path
from typing import Dict, Tuple, Union, Literal
import json
from functools import cached_property

from roguelike_engine.config.config import DATA_DIR

@dataclass
class MapSettings:
    """
    Configuración central para generación y carga de mapas.
    """
    # Flag para decidir tipo de carga de offsets: JSON o dinámico
    use_zones_json: bool = False         #! Mas adelante deberiamos trabajar sobre el offset no dinamico.

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
        "extra_dungeon":    ("lobby", "left"),
        "extra_dungeon2":   ("extra_dungeon", "left"),
    })



    # Directorio para mapas de debug generados automáticamente
    debug_maps_dir: Path = field(default_factory=lambda:
        Path(__file__).resolve().parent.parent.parent / 'data' / 'debug_maps'
    )

    # Ruta al índice de zonas dinámico (data/zones/zones.json)
    ZONES_INDEX: Path = field(default_factory=lambda:
        Path(DATA_DIR) / 'zones' / 'zones.json'
    )    

    @property
    def zone_size(self) -> Tuple[int, int]:
        """Dimensiones de cada zona en tiles."""
        return (self.zone_width, self.zone_height)

    @cached_property
    def zone_offsets(self) -> Dict[str, Tuple[int, int]]:
        """
        Offsets de cada zona en tiles.
        Si use_zones_json es True, lee data/zones/zones.json;
        de lo contrario, calcula dinámicamente lobby y dungeon.
        """
        # Si no usamos JSON, fallback inmediato
        if not self.use_zones_json:
            return self._dynamic_offsets()

        # Intentar cargar offsets desde JSON
        try:
            content = self.ZONES_INDEX.read_text(encoding='utf-8')
            data = json.loads(content)
            # Validar formato: cada offset es una secuencia de dos ints
            return {zone: (int(offset[0]), int(offset[1])) for zone, offset in data.items()}
        except Exception:
            # En caso de fallo, usar dinámico
            return self._dynamic_offsets()

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
        # Zonas dinámicas definidas en additional_zones (parent_zone, side)
        for zone, (parent, side) in self.additional_zones.items():
            parent_off = offsets.get(parent)
            if parent_off is None:
                raise KeyError(f"Zona padre '{parent}' no definida para zona '{zone}'")
            offsets[zone] = self.calculate_offset(parent_off, side)
        if self.auto_expand:
            self.global_width, self.global_height, offsets = self.expand_limits(offsets)
        else:
            self.validate_limits(offsets)
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

# Instancia global para uso en toda la aplicación
global_map_settings = MapSettings()

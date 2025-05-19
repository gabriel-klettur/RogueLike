from dataclasses import dataclass, field
from pathlib import Path
from typing import Dict, Tuple, Union, Literal
import json
from functools import cached_property

from roguelike_engine.config.config import DATA_DIR

@dataclass(frozen=True)
class MapSettings:
    """
    Configuración central para generación y carga de mapas.
    """
    # Flag para decidir tipo de carga de offsets: JSON o dinámico
    use_zones_json: bool = False

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
        Calcula offsets por defecto: lobby centrado y dungeon adyacente.
        """
        lobby_off = self.lobby_offset
        dungeon_off = self.calculate_dungeon_offset(lobby_off)
        return {
            'lobby': lobby_off,
            'dungeon': dungeon_off,
        }

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

# Instancia global para uso en toda la aplicación
global_map_settings = MapSettings()

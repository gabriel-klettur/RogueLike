# Path: src/roguelike_engine/config/map_config.py
from dataclasses import dataclass, field
from pathlib import Path
from typing import Dict, Tuple, Union, Literal

@dataclass(frozen=True)
class MapSettings:
    """
    Configuración central para generación y carga de mapas.
    """
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
    

    @property
    def zone_size(self) -> Tuple[int, int]:
        """Dimensiones de cada zona."""
        return (self.zone_width, self.zone_height)

    @property
    def zone_offsets(self) -> Dict[str, Tuple[int, int]]:
        """Offsets de cada zona en tiles."""
        return {'lobby': self.lobby_offset}

    @property
    def lobby_offset(self) -> Tuple[int, int]:
        """
        Offset (x, y) para centrar la zona "lobby" en el mapa global.
        Calculado dinámicamente según grid de zonas.
        """
        n_cols = self.global_width // self.zone_width
        n_rows = self.global_height // self.zone_height
        if n_cols < 1 or n_rows < 1:
            # Si el mapa es más pequeño que una zona
            return (
                (self.global_width - self.zone_width) // 2,
                (self.global_height - self.zone_height) // 2
            )
        # Centro de la cuadrícula de zonas
        center_col = n_cols // 2
        center_row = n_rows // 2
        # Espacios remanentes
        rem_x = self.global_width - n_cols * self.zone_width
        rem_y = self.global_height - n_rows * self.zone_height
        start_x = rem_x // 2
        start_y = rem_y // 2
        return (
            start_x + center_col * self.zone_width,
            start_y + center_row * self.zone_height
        )

    def calculate_dungeon_offset(self, lobby_offset: Tuple[int, int]) -> Tuple[int, int]:
        """
        Offset (x, y) para colocar la mazmorra adyacente a la zona "lobby".
        """
        off_x, off_y = lobby_offset
        side = self.dungeon_connect_side
        if side == 'bottom':
            return off_x, off_y + self.zone_height
        if side == 'top':
            return off_x, off_y - self.zone_height
        if side == 'left':
            return off_x - self.zone_width, off_y
        # 'right'
        return off_x + self.zone_width, off_y

# Instancia global para uso en toda la aplicación
global_map_settings = MapSettings()

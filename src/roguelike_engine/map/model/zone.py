# Path: src/roguelike_engine/map/model/zone.py
from typing import List, Tuple, Optional
from roguelike_engine.config.map_config import global_map_settings
from roguelike_engine.map.model.map_model import Map as MapModel

class Zone:
    """
    Representa una región del mundo, con su propia matriz de caracteres,
    posición global y dimensiones.
    """
    def __init__(
        self,
        name: str,
        offset: Tuple[int, int],
        width: Optional[int] = None,
        height: Optional[int] = None,
    ):
        self.name = name
        self.offset_x, self.offset_y = offset
        # Usar configuración por defecto si no se proporciona
        self.width = width or global_map_settings.zone_width
        self.height = height or global_map_settings.zone_height
        # Matriz local de caracteres (#, ., O, etc.)
        self.matrix: List[List[str]] = [
            ["#" for _ in range(self.width)]
            for _ in range(self.height)
        ]
        # Contenedores para Tiles y overlay de esta zona
        self.tiles: Optional[List[List[object]]] = None
        self.overlay: Optional[List[List[str]]] = None

    def set_matrix_from_rows(self, rows: List[str]) -> None:
        """
        Asigna la matriz local a partir de una lista de strings.
        """
        if len(rows) != self.height or any(len(r) != self.width for r in rows):
            raise ValueError(f"Dimensiones inválidas para zone '{self.name}': "
                             f"esperado {self.width}x{self.height}, "
                             f"recibido {len(rows)}x{len(rows[0]) if rows else 0}")
        self.matrix = [list(row) for row in rows]

    def global_coords(self, x: int, y: int) -> Tuple[int, int]:
        """
        Convierte coordenadas locales (dentro de la zona) a globales.
        """
        gx = self.offset_x + x
        gy = self.offset_y + y
        return gx, gy

    def load_tiles_and_overlay(
        self,
        loader: MapModel,
        map_name: str,
    ) -> None:
        """
        Carga la representación Tile y overlay usando MapLoader existente.
        """
        # loader.load espera rows y map_name; devuelve (matrix, tiles, overlay)
        _, tiles, overlay = loader.load([
            ''.join(row) for row in self.matrix
        ], f"{map_name}_{self.name}")
        self.tiles = tiles
        self.overlay = overlay

    def __repr__(self):
        return (
            f"<Zone '{self.name}' size={self.width}x{self.height} "
            f"offset=({self.offset_x},{self.offset_y})>"
        )

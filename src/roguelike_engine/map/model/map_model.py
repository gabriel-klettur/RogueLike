# Path: src/roguelike_engine/map/model/map_model.py
from dataclasses import dataclass, field
from typing import List, Optional, Dict
from roguelike_engine.tile.model.tile import Tile
from roguelike_engine.map.model.layer import Layer
from roguelike_engine.tile.loader import load_tiles_from_text

@dataclass
class Map:
    """
    Contenedor inmutable con todos los datos resultantes de la construcción de un mapa.
    """
    matrix: List[str]                          # Cada elemento es una fila del mapa (“########”, “#...#”, …)
    layers: Dict[Layer, List[List[str]]]       # Códigos de tile por capa
    tiles_by_layer: Dict[Layer, List[List[Tile]]]  # Objetos Tile agrupados por capa
    metadata: Dict                             # Datos extra (p. ej. lista de habitaciones)
    name: str                                  # Clave usada para persistir el overlay (lobby_map, combined_map…)
    # Campos legacy, se calculan tras init
    overlay: Optional[List[List[str]]] = field(init=False)
    tiles: List[List[Tile]] = field(init=False)  # Grid combinado de Tiles, para compatibilidad

    def __post_init__(self):
        # Fallback legacy: overlay = Ground
        ground = self.layers.get(Layer.Ground)
        self.overlay = ground
        # Generar tiles combinados usando overlay Ground
        self.tiles = load_tiles_from_text(self.matrix, self.overlay)

    def get_tile(self, layer: Layer, x: int, y: int) -> Optional[Tile]:
        """Devuelve el Tile en la capa y coordenadas dadas, o None si está fuera de rango."""
        grid = self.tiles_by_layer.get(layer)
        if not grid:
            return None
        if 0 <= y < len(grid) and 0 <= x < len(grid[0]):
            return grid[y][x]
        return None

    def get_tiles_for_layer(self, layer: Layer) -> List[List[Tile]]:
        """Devuelve la matriz de Tiles para la capa indicada."""
        return self.tiles_by_layer.get(layer, [])

    def get_layer(self, layer: Layer) -> List[List[str]]:
        """Devuelve la matriz de códigos (strings) para la capa indicada."""
        return self.layers.get(layer, [])

    def set_tile(self, layer: Layer, x: int, y: int, code: str) -> None:
        """Actualiza el código en la capa dada y reconstruye el Tile correspondiente."""
        codes = self.layers.get(layer)
        if codes and 0 <= y < len(codes) and 0 <= x < len(codes[0]):
            codes[y][x] = code
            # Reconstruir tiles para la capa
            new_grid = load_tiles_from_text(self.matrix, codes)
            self.tiles_by_layer[layer] = new_grid
            # Si modificamos Ground, actualizar legacy overlay y tiles
            if layer == Layer.Ground:
                self.overlay = codes
                self.tiles = load_tiles_from_text(self.matrix, self.overlay)
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
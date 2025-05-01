#Path:  src/roguelike_engine/map/merger/edge_to_edge.py

from typing import List, Tuple, Sequence, Optional
from .interfaces import MergerStrategy

class EdgeToEdgeMerger(MergerStrategy):
    """
    Conecta el borde inferior del lobby con el borde superior de la dungeon.
    """
    def merge(
        self,
        handmade_map: Sequence[Sequence[str]],
        generated_map: Sequence[Sequence[str]],
        offset_x: int = 0,
        offset_y: int = 0,
        dungeon_rooms: Optional[List[Tuple[int,int,int,int]]] = None
    ) -> List[List[str]]:
        # Implementación simplificada: copia handmade y dibuja túnel vertical central
        new_map = [list(row) for row in generated_map]
        # Superponer
        for y, row in enumerate(handmade_map):
            for x, ch in enumerate(row):
                new_map[y+offset_y][x+offset_x] = ch
        # Túnel central
        mid_x = offset_x + len(handmade_map[0]) // 2
        for y in range(offset_y+len(handmade_map), len(new_map)):
            new_map[y][mid_x] = '='
        return new_map

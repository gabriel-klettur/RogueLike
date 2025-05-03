
# Path: src/roguelike_engine/map/loader/text_loader.py
from typing import List, Sequence


def parse_map_text(map_data: List[str]) -> List[List[str]]:
    """
    Convierte una lista de strings en una matriz de caracteres.

    :param map_data: cada elemento es una fila del mapa como string.
    :return: matriz [filas][columnas] de caracteres.
    """
    return [list(line) for line in map_data]
#Path: src/roguelike_game/entities/npc/utils/geometry.py

import math


def calculate_distance(x1: float, y1: float, x2: float, y2: float) -> float:
    """
    Devuelve la distancia euclidiana entre los puntos (x1, y1) y (x2, y2).
    """
    return math.hypot(x2 - x1, y2 - y1)
from enum import IntEnum

class Layer(IntEnum):
    """
    Capas de render del mapa en orden z ascendente.

    Nota: el valor numérico define el orden de pintado. Mantener estos
    valores estables para preservar caches y persistencia.
    """
    Ground = 0
    FloorDecals = 1
    Collision = 2
    ObjectsLow = 3
    WallsBottom = 4
    Decorations = 5
    WallsTop = 6
    ObjectsHigh = 7
    OverheadDetails = 8
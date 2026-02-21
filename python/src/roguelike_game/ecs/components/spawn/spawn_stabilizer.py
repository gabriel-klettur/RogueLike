from dataclasses import dataclass


@dataclass
class SpawnStabilizer:
    """
    Marca entidades recién spawneadas para aplicar una estabilización de posición
    que elimine solapes durante unos pocos frames posteriores al spawn.
    """
    frames_remaining: int = 7
    max_search_radius: int = 12

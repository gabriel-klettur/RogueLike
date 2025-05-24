
# Path: src/roguelike_engine/map/model/overlay/interfaces.py
from abc import ABC, abstractmethod
from typing import Optional, List

class OverlayStore(ABC):
    """
    Interfaz para cargar y guardar capas de overlay de mapa.
    """

    @abstractmethod
    def load(self, map_name: str) -> Optional[List[List[str]]]:
        """
        Devuelve la capa overlay (matriz de códigos) para el mapa dado,
        o None si no existe.
        """
        ...

    @abstractmethod
    def save(self, map_name: str, overlay: List[List[str]]) -> None:
        """
        Persiste la capa overlay (matriz de códigos) para el mapa dado.
        """
        ...
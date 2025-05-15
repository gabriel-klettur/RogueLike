
# Path: src/roguelike_engine/map/model/exporter/interfaces.py
from abc import ABC, abstractmethod

class MapExporter(ABC):
    """
    Interfaz para exportar mapas en distintos formatos.
    """
    @abstractmethod
    def export(self, map_data: list[list[str]], output_dir: str = None) -> str:
        """
        Exporta el mapa y devuelve el nombre de archivo generado (relativo al directorio).
        """
        ...
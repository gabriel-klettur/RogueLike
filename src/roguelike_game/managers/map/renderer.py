"""
Módulo de renderizado de mapa usando ChunkedMapView.
"""
from roguelike_engine.map.view.chunked_map_view import ChunkedMapView


class MapRenderer:
    """
    Maneja la vista y render del mapa.
    """
    def __init__(self):
        self.view = ChunkedMapView()

    def render(self, surface, camera, map_model):
        """
        Dibuja el mapa en pantalla usando la vista por chunks.
        """
        return self.view.render(surface, camera, map_model)

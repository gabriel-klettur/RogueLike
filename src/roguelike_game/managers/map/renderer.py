"""
Módulo de renderizado de mapa usando ChunkedMapView.
"""
from roguelike_engine.map.view.chunked_map_view import ChunkedMapView

class MapRenderer:
    """
    Maneja la vista y render de tiles.
    """
    def __init__(self):
        self.view = ChunkedMapView()

    def render(self, surface, camera, tiles):
        """
        Dibuja tiles en pantalla usando la vista.
        """
        self.view.draw(surface, camera, tiles)

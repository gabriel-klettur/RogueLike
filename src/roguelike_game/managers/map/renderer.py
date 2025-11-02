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
        # Limpiar fondo a negro antes de dibujar (importante cuando no hay chunks visibles)
        try:
            surface.fill((0, 0, 0))
        except Exception:
            pass
        return self.view.render(surface, camera, map_model)

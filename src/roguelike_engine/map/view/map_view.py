# Path: src/roguelike_engine/map/view/map_view.py
import pygame
from roguelike_engine.map.model.map_model import Map as MapModel

class MapView:
    """
    Vista para renderizar mapas (tiles y overlays) en la pantalla.
    Cumple con MVC: recibe MapModel, Camera y Surface para dibujar.
    """
    def __init__(self):
        # Aquí podrías inicializar caches o configuraciones de renderizado
        pass

    def render(self, screen: pygame.Surface, camera, map_model: MapModel) -> list[pygame.Rect]:
        """
        Dibuja los tiles visibles del mapa y retorna la lista de dirty rects.

        :param screen: Surface donde dibujar
        :param camera: instancia de Camera para transformaciones
        :param map_model: instancia de Map con datos de matrix, tiles y overlay
        :return: lista de rects sucios para optimizar redraw
        """
        dirty_rects: list[pygame.Rect] = []
        # 1) Tiles
        for tile in map_model.tiles_in_region:
            if not camera.is_in_view(tile.x, tile.y, tile.sprite_size):
                continue
            rect = tile.render_tiles(screen, camera)
            if rect:
                dirty_rects.append(rect)

        return dirty_rects
# src/roguelike_engine/map/view/map_view.py

import pygame
from roguelike_engine.map.view.zone_view import ZoneView
from roguelike_engine.config.config_tiles import TILE_SIZE

class MapView:
    """
    Orquesta el render de todas las zonas del mapa,
    delegando cada una a ZoneView.
    """
    def __init__(self):
        self.zone_view = ZoneView()

    def render(
        self,
        screen: pygame.Surface,
        camera,
        map_manager
    ) -> list[pygame.Rect]:
        dirty_rects: list[pygame.Rect] = []

        # Recorrer cada zona y pintar sus tiles + contorno
        for zone_name, tiles in map_manager.tiles_by_zone.items():
            # Antes de pintar, opcionalmente filtrar por visibilidad:
            visible = [
                t for t in tiles
                if camera.is_in_view(t.x, t.y, (TILE_SIZE, TILE_SIZE))
            ]
            self.zone_view.render_zone(screen, camera, zone_name, visible)

            # (Opcional) añadir bounding rect al dirty list
            # Calculamos de nuevo el bounding box en pantalla:
            if visible:
                xs = [t.x for t in visible]
                ys = [t.y for t in visible]
                min_x, max_x = min(xs), max(xs) + TILE_SIZE
                min_y, max_y = min(ys), max(ys) + TILE_SIZE
                tl = camera.apply((min_x, min_y))
                br = camera.apply((max_x, max_y))
                dirty_rects.append(pygame.Rect(tl, (br[0]-tl[0], br[1]-tl[1])))

        return dirty_rects
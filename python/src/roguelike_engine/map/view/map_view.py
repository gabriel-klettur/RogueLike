"""
High-level map view that orchestrates zone rendering via ZoneView.
"""
import pygame
from roguelike_engine.zone.zone_view import ZoneView
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_engine.utils.pickle_utils import rebuild_map_view

class MapView:
    """
    Orquesta el render de todas las zonas del mapa,
    delegando cada una a ZoneView.
    """
    def __init__(self):
        self.zone_view = ZoneView()

    def __reduce__(self):
        """Provide a stable reconstruction path for pickle.
        Returning a top-level factory function avoids relying on class identity
        checks that can fail under certain import/reload scenarios.
        """
        return (rebuild_map_view, ())

    def render(
        self,
        screen: pygame.Surface,
        camera,
        map_manager
    ) -> list[pygame.Rect]:
        dirty_rects: list[pygame.Rect] = []

        # Cache is_in_view for faster lookup
        is_in_view = camera.is_in_view
        tile_size = (TILE_SIZE, TILE_SIZE)

        # Recorrer cada zona y pintar sus tiles + contorno
        for zone_name, tiles in map_manager.tiles_by_zone.items():
            # Antes de pintar, opcionalmente filtrar por visibilidad:
            visible = [t for t in tiles if is_in_view(t.x, t.y, tile_size)]
            if not visible:
                continue
            self.zone_view.render_zone(screen, camera, zone_name, visible)

            # Bounding rect calculation optimized: single pass
            min_x = min_y = float('inf')
            max_x = max_y = float('-inf')
            for t in visible:
                if t.x < min_x:
                    min_x = t.x
                if t.x > max_x:
                    max_x = t.x
                if t.y < min_y:
                    min_y = t.y
                if t.y > max_y:
                    max_y = t.y
            max_x += TILE_SIZE
            max_y += TILE_SIZE
            tl = camera.apply((min_x, min_y))
            br = camera.apply((max_x, max_y))
            dirty_rects.append(pygame.Rect(tl, (br[0] - tl[0], br[1] - tl[1])))

        return dirty_rects
import pygame
from roguelike_engine.config.map_config import global_map_settings
from roguelike_engine.tile.tile_model import Tile
from roguelike_engine.diagnostics.helpers import draw_zone_border
from roguelike_engine.config.config_minimap import MINIMAP_ZONE_COLORS, MINIMAP_ZONE_BORDER_WIDTH


class ZoneView:
    """
    Renderiza una sola zona: todos sus tiles y opcionalmente su contorno.
    """
    def __init__(self, border_width: int = MINIMAP_ZONE_BORDER_WIDTH):
        # Mantener ancho por defecto para el render del mundo; se puede pasar otro valor.
        self.border_width = border_width
        # Unificar paleta con el minimapa: usar MINIMAP_ZONE_COLORS por nombre de zona
        default_col = MINIMAP_ZONE_COLORS.get('default', (200, 200, 200))
        self.colors = {
            zone: MINIMAP_ZONE_COLORS.get(str(zone).lower(), default_col)
            for zone in global_map_settings.zone_offsets
        }

    def render_zone(
        self,
        screen: pygame.Surface,
        camera,
        zone_name: str,
        tiles: list[Tile]
    ):
        if not tiles:
            return
        # 1) Dibujar todos los sprites de la zona
        # Pre-calculate zoom key and cache camera.apply for performance
        z = round(camera.zoom, 2)
        blit = screen.blit
        apply = camera.apply
        for tile in tiles:
            # Obtener sprite escalado o original
            sprite = tile.scaled_cache.get(z)
            if sprite is None:
                sprite = tile.sprite
            blit(sprite, apply((tile.x, tile.y)))

        # 2) Dibujar contorno de la zona (usar helper centralizado)
        draw_zone_border(screen, camera, tiles, zone_name, self.colors, self.border_width)
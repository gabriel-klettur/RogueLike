import pygame
from roguelike_engine.config.map_config import global_map_settings
from roguelike_engine.config.config_tiles import TILE_SIZE

class MapEditorView:
    """
    Vista para el Map Editor: dibuja zonas, etiquetas y resaltados.
    """
    def __init__(self, controller, state, map_manager):
        self.controller = controller
        self.state = state
        self.map_manager = map_manager
        # fuente para etiquetas
        self.font = pygame.font.SysFont(None, 16)

    def render(self, screen, camera, map_manager):
        if not self.state.active:
            return
        zones = global_map_settings.zone_offsets
        zone_w, zone_h = global_map_settings.zone_size
        for zone, (ox, oy) in zones.items():
            # ocultar si está marcado
            hidden = zone in self.state.hidden_zones
            # color y alpha según estado
            if hidden:
                outline_color = (100, 100, 100)
                fill_color = (*outline_color, 50)
            else:
                if zone == self.state.selected_zone:
                    outline_color = (0, 255, 0)
                else:
                    outline_color = (0, 128, 255)
                fill_color = (*outline_color, 50)
            # rect en píxeles globales
            px, py = ox * TILE_SIZE, oy * TILE_SIZE
            pw, ph = zone_w * TILE_SIZE, zone_h * TILE_SIZE
            # convertir a coordenadas de pantalla
            screen_tl = camera.apply((px, py))
            screen_size = camera.scale((pw, ph))
            # dibujo semitransparente
            surf = pygame.Surface(screen_size, pygame.SRCALPHA)
            surf.fill(fill_color)
            screen.blit(surf, screen_tl)
            # borde
            pygame.draw.rect(screen, outline_color, (*screen_tl, *screen_size), 2)
            # etiqueta
            label = self.font.render(zone, True, (255, 255, 255))
            screen.blit(label, screen_tl)
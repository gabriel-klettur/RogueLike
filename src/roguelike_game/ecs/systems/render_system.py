import pygame
from ..components.scale import Scale
from roguelike_game.systems.config_z_layer import DEFAULT_Z

class RenderSystem:
    def __init__(self, screen):
        self.screen = screen
        # Cache for scaled sprites: {(eid, zoom): Surface}
        self._scaled_sprite_cache = {}
        # Reusable rect for blitting and culling
        self._blit_rect = pygame.Rect(0, 0, 0, 0)

    def update(self, world, screen, camera):
        """Renderiza sprites ordenados por capa Z y posición Y para manejar profundidad."""
        # Preparar culling de pantalla
        screen_rect = screen.get_rect()
        # Cache de componentes para renderizado eficiente
        comps = world.components
        pos_map = comps['Position']
        sprite_map = comps['Sprite']
        z_map = comps['ZLayer']
        scale_map = comps['Scale']
        # Entidades con Position y Sprite
        eids = [eid for eid in pos_map if eid in sprite_map]
        # Ordenar por capa Z y posición Y
        eids.sort(key=lambda eid: (z_map[eid].layer if eid in z_map else DEFAULT_Z,
                                  pos_map[eid].y))
        camapply = camera.apply
        zoom = round(camera.zoom, 2)
        for eid in eids:
            pos = pos_map[eid]
            sprite = sprite_map[eid]
            sc = scale_map.get(eid)
            if sc and sc.scale != 1.0:
                key = (eid, zoom)
                cache = self._scaled_sprite_cache
                if key not in cache:
                    orig = sprite.image
                    w, h = orig.get_size()
                    cache[key] = pygame.transform.scale(
                        orig,
                        (int(w * sc.scale), int(h * sc.scale))
                    )
                image = cache[key]
            else:
                image = sprite.image
            dest = camapply((pos.x, pos.y))
            # Skip off-screen sprites using reusable rect
            self._blit_rect.size = image.get_size()
            self._blit_rect.topleft = dest
            if not screen_rect.colliderect(self._blit_rect):
                continue
            screen.blit(image, dest)
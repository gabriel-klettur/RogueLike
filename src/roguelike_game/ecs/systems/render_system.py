import pygame
from ..components.scale import Scale
from roguelike_game.systems.config_z_layer import DEFAULT_Z

class RenderSystem:
    def __init__(self, screen):
        self.screen = screen

    def update(self, world, screen, camera):
        """Renderiza sprites ordenados por capa Z y posición Y para manejar profundidad."""
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
        for eid in eids:
            pos = pos_map[eid]
            sprite = sprite_map[eid]
            image = sprite.image
            sc = scale_map.get(eid)
            if sc and sc.scale != 1.0:
                w, h = image.get_size()
                image = pygame.transform.scale(image,
                                              (int(w * sc.scale), int(h * sc.scale)))
            screen.blit(image, camapply((pos.x, pos.y)))
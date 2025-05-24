import pygame
from ..components.scale import Scale
from roguelike_game.systems.config_z_layer import DEFAULT_Z

class RenderSystem:
    def __init__(self, screen):
        self.screen = screen

    def update(self, world, screen, camera):
        """Renderiza sprites ordenados por capa Z y posición Y para manejar profundidad."""
        # Recolecta e identifica Z de cada entidad
        eids = list(world.get_entities_with('Position', 'Sprite'))
        sorted_eids = sorted(
            eids,
            key=lambda eid: (
                (world.components['ZLayer'].get(eid).layer)
                if world.components['ZLayer'].get(eid)
                else DEFAULT_Z,
                world.components['Position'][eid].y
            )
        )
        for eid in sorted_eids:
            pos = world.components['Position'][eid]
            sprite = world.components['Sprite'][eid]
            # Escalado si existe componente Scale
            scale_comp: Scale = world.components['Scale'].get(eid)
            image = sprite.image
            if scale_comp and scale_comp.scale != 1.0:
                w, h = image.get_size()
                image = pygame.transform.scale(
                    image, (int(w * scale_comp.scale), int(h * scale_comp.scale))
                )
            # Dibujar en pantalla
            screen.blit(image, camera.apply((pos.x, pos.y)))